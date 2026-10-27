using System;
using System.Collections.Generic;
using Cosmoteer.Crew;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Crew;
using Cosmoteer.Ships.Roles;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Doors;
using Cosmoteer.Ships.Paint;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Weapons;
using Cosmoteer.Ships.Statuses;
using Halfling;
using Halfling.Geometry;
using Halfling.Physics2D.Dynamics;
using Halfling.Pooling;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class ShipMerger
    {
        private static readonly IntVector2 s_zero = IntVector2.Zero;

        private static readonly System.Reflection.FieldInfo s_doorLocField =
            AccessTools.Field(typeof(CommonBaseDoor), "<Location>k__BackingField");

        private static readonly AccessTools.FieldRef<CommonBasePart, int> s_rotFlipRef =
            AccessTools.FieldRefAccess<CommonBasePart, int>("_rotFlip");

        private static readonly Type s_tileSplitType = AccessTools.Inner(typeof(TileStatusHandler), "ShipSplitHandler");
        private static readonly AccessTools.FieldRef<object, TileStatusHandler> s_tileSrcRef =
            AccessTools.FieldRefAccess<TileStatusHandler>(s_tileSplitType, "_statusHandler");
        private static readonly AccessTools.FieldRef<object, TempList<Status<IntVector2>>> s_tileListRef =
            AccessTools.FieldRefAccess<TempList<Status<IntVector2>>>(s_tileSplitType, "_splitStatuses");

        public static bool Merge(Ship shipA, Ship shipB, object jointTag)
        {
            return Merge(shipA, shipB, jointTag, GridTransform.Identity);
        }

        public static bool Merge(Ship shipA, Ship shipB, object jointTag, GridTransform xf)
        {
            CommandGateBatch.Enter();
            try
            {
                return MergeCore(shipA, shipB, jointTag, xf);
            }
            finally
            {
                CommandGateBatch.Exit(shipA, shipB);
            }
        }

        private static bool MergeCore(Ship shipA, Ship shipB, object jointTag, GridTransform xf)
        {
            if (shipA?.Sim == null || shipB?.Sim != shipA.Sim || shipA == shipB)
                return false;
            Body bodyA = shipA.Physics?.Body;
            Body bodyB = shipB.Physics?.Body;
            if (bodyA == null || bodyB == null) return false;

            var parts = new List<Part>();
            foreach (Part p in shipB.Parts)
                parts.Add(p);
            if (parts.Count == 0) return false;
            foreach (Part p in parts)
            {
                if (!xf.IsIdentity && (xf.K % 2) == 1 && !p.Rules.IsRotateable)
                {
                    IntVector2 size = p.Rules.GetSize(0);
                    if (size.X != size.Y)
                    {
                        Log.Info($"Merge refused: part {p.Rules.ID} at {p.Location} is non-rotateable with a {size.X}x{size.Y} footprint and cannot survive a {xf.K * 90}-degree merge.");
                        return false;
                    }
                }
                foreach (IntVector2 cell in p.GetCells(PartRectType.Normal))
                {
                    IntVector2 target = xf.TransformCell(cell);
                    Part there = shipA.Parts[target, PartRectType.Normal];
                    if (there != null)
                    {
                        Log.Info($"Merge refused: ship {shipA.UniqueID} already has a part at {target} ({there.Rules.ID}; target of {cell} of {p.Rules.ID} under {xf}).");
                        return false;
                    }
                }
            }

            var keyMap = new Dictionary<PartSaveData, PartSaveData>();
            foreach (Part p in parts)
            {
                TransformedPlacement(p, xf, out IntVector2 loc, out int rot);
                var oldKey = new PartSaveData(p);
                var newKey = new PartSaveData(p.Rules.ID, loc + p.Rules.GetSaveLocationOffset(rot, p.FlipX), rot, p.FlipX);
                keyMap[oldKey] = newKey;
            }
            List<PendingRepairs.Entry> pendB = PendingRepairs.Collect(shipB);
            PendingRepairs.AddKeys(pendB, xf, keyMap);
            var arriving = new HashSet<IntVector2>();
            foreach (Part p in parts)
                foreach (IntVector2 c in p.GetCells(PartRectType.Normal)) arriving.Add(xf.TransformCell(c));

            var contiguityB = shipB.StructureContiguity;
            contiguityB.IsSplitting = true;
            HingeJointManager.BeginSurgery();
            var handlers = new List<IShipSplitHandler>();
            var tileHandlers = new HashSet<IShipSplitHandler>();
            bool batchesOpen = false;
            int movedParts = 0;
            var movedRecords = new List<(Part Part, IntVector2 Loc, int RotFlip, List<Door> Doors, List<ShipCrew> Crew)>();
            var crewOfB = new List<CrewSoul>(shipB.Crew.Assigned);
            try
            {
                HingeJointManager.ReleaseByTag(jointTag);
                bodyB.Position = bodyA.GetWorldPoint(xf.ContinuousOffset * shipA.Scale);
                bodyB.Rotation = bodyA.Rotation + xf.Theta;
                bodyB.LinearVelocity = bodyA.GetLinearVelocityFromWorldPoint(bodyB.WorldCenter);
                bodyB.AngularVelocity = bodyA.AngularVelocity;

                MergeRolesInto(shipA, shipB);
                CopyPartMetadata(shipB, shipA, keyMap, xf);

                shipB.PartNetworks.BeginBatchOperation();
                shipA.PartNetworks.BeginBatchOperation();
                batchesOpen = true;
                foreach (IShipSplitHandlerFactory factory in contiguityB._shipSplitHandlerFactories)
                {
                    IShipSplitHandler h = factory.CreateHandler(shipB);
                    handlers.Add(h);
                    if (!xf.IsIdentity && h.GetType() == s_tileSplitType)
                        tileHandlers.Add(h);
                }
                List<SeamHandover.Record> handover = SeamHandover.Capture(shipB);
                int rekeyed = RekeyCollidingManualJobs(shipA, shipB);
                if (rekeyed > 0) Log.Info($"Merge: {rekeyed} manual transfer job(s) of ship {shipB.UniqueID} took a fresh ID (ship {shipA.UniqueID} already held theirs).");
                InvokeSplitEvent(contiguityB, "ShipSplitting", shipB, shipA);

                var crewOnPart = new List<ShipCrew>();
                var doorsOnPart = new List<Door>();
                foreach (Part part in parts)
                {
                    crewOnPart.Clear();
                    doorsOnPart.Clear();
                    if (part.CrewPresent != null)
                        crewOnPart.AddRange(part.CrewPresent);
                    doorsOnPart.AddRange(part.GetDoors());

                    foreach (IShipSplitHandler h in handlers)
                        h.OnPartSplitting(part, shipA);

                    movedRecords.Add((part, part.Location, s_rotFlipRef(part), new List<Door>(doorsOnPart), new List<ShipCrew>(crewOnPart)));
                    PartForwarding.Record(part, shipB);
                    shipB.Parts.RemovePart(part, PartsManager.CrewAction.None, destroyed: false, removeDoors: false, null);

                    if (!xf.IsIdentity)
                    {
                        TransformedPlacement(part, xf, out IntVector2 newLoc, out int newRot);
                        part.Location = newLoc;
                        s_rotFlipRef(part) = newRot | (part.FlipX ? 4 : 0);
                    }

                    shipA.Parts.AddPart(part);

                    foreach (Door door in doorsOnPart)
                    {
                        shipB.Doors.RemoveDoor(door, destroyed: false);
                        if (!xf.IsIdentity)
                        {
                            DoorLocation dl = door.Location.RotateAround(s_zero, xf.K);
                            dl.Cell += xf.T;
                            s_doorLocField.SetValue(door, dl);
                        }
                        shipA.Doors.AddDoor(door);
                    }
                    foreach (ShipCrew crew in crewOnPart)
                    {
                        if (crew.Soul.HomeShip == shipB)
                        {
                            shipB.Crew.UnassignCrew(crew.Soul, killed: false, removeFromJob: false);
                            shipA.Crew.AssignCrew(crew.Soul);
                        }
                        shipB.Crew.RemoveCrew(crew, unregisterWithSim: false);
                        if (!xf.IsIdentity)
                        {
                            Vector2 grid = crew.Location / shipB.Scale;
                            Vector2 moved = xf.TransformPoint(grid) * shipA.Scale;
                            crew.SetLocation(moved, lerpFromPrevious: false, queueGeometryRefresh: false, clearPath: false);
                            crew.SetRotation(new Direction((float)crew.Rotation + xf.Theta), lerpFromPrevious: false, queueGeometryRefresh: false);
                            if (crew.OverrideApparentLocation.HasValue)
                                crew.OverrideApparentLocation = xf.TransformPoint(crew.OverrideApparentLocation.Value / shipB.Scale) * shipA.Scale;
                            if (crew.OverrideApparentRotation.HasValue)
                                crew.OverrideApparentRotation = new Direction((float)crew.OverrideApparentRotation.Value + xf.Theta);
                        }
                        shipA.Crew.AddCrew(crew, registerWithSim: false);
                    }

                    foreach (IShipSplitHandler h in handlers)
                    {
                        if (tileHandlers.Contains(h))
                            TransferTileStatuses(h, part, shipA, xf);
                        else
                            h.OnPartSplit(part, shipA);
                    }
                    movedParts++;
                    MergeFaultInjection.MergeMoved(movedParts);
                }

                int rehomed = 0;
                foreach (CrewSoul soul in new List<CrewSoul>(shipB.Crew.Assigned))
                {
                    shipB.Crew.UnassignCrew(soul, killed: false, removeFromJob: false);
                    shipA.Crew.AssignCrew(soul);
                    rehomed++;
                }
                if (rehomed > 0) Log.Info($"Merge: {rehomed} crew of ship {shipB.UniqueID} not standing on its parts joined ship {shipA.UniqueID} too.");
                foreach (CrewSoul soul in crewOfB) AssemblyCrewHome.MoveBunkCell(soul, xf);
                int rebound = AssemblyCrewHome.RebindBunks(crewOfB);
                if (rebound > 0) Log.Info($"Merge: {rebound} crew re-bound to their quarters on ship {shipA.UniqueID}.");

                SurgeryKin.Record(shipA, shipB);
                foreach (CrewSoul soul in shipA.Crew.Assigned)
                {
                    Job job = soul.Job;
                    if (job != null && job.Ship != shipA && !SurgeryKin.Are(job.Ship, shipA))
                        job.UnassignCrew(soul);
                }
                if (!xf.IsIdentity)
                    RetargetDestinations(shipA, shipB, parts, xf);
                ShipSplitter.FlushGeometry(shipA);

                if (xf.IsIdentity)
                {
                    for (int i = 0; i < shipA.DecalLayers.Length; i++)
                        shipA.DecalLayers[i].CopyDecalsFrom(shipB.DecalLayers[i], replaceExisting: true, onlyNeeded: true);
                }
                else
                {
                    for (int i = 0; i < shipA.DecalLayers.Length; i++)
                    {
                        foreach (Decal d in shipB.DecalLayers[i])
                        {
                            IntRect r = d.GetRect(shipB.Rules);
                            r.Size -= IntVector2.One;
                            r = r.Rotate90Around(in s_zero, xf.K);
                            Decal moved = d;
                            moved.Location = r.Location + xf.T;
                            moved.Rotation = Mathx.Wrap(d.Rotation + xf.K, 0, 3);
                            shipA.DecalLayers[i].AddDecal(moved, replaceExisting: true);
                        }
                    }
                }

                shipA.Physics.ForceUpdateFixtures();
                PendingRepairs.MatchKeeping(shipA, pi =>
                {
                    foreach (IntVector2 c in pi.Rect) if (arriving.Contains(c)) return true;
                    return false;
                }, PendingRepairs.BlueprintCells(pendB, xf));
                var (carried, carrySkipped) = PendingRepairs.Restore(shipA, pendB, xf);
                PendingRepairs.RemoveStaleWrecks(shipA);
                if (pendB.Count > 0 || carrySkipped > 0)
                    Log.Info($"Merge: carried {carried} pending repair(s) / planned part(s) along ({carrySkipped} skipped, cell taken); ship {shipA.UniqueID}'s own left untouched.");
                shipB.PartNetworks.EndBatchOperation();
                shipA.PartNetworks.EndBatchOperation();
                batchesOpen = false;
                int migrated = MigrateManualJobs(shipB, shipA);
                if (migrated > 0) Log.Info($"Merge: {migrated} manual transfer job(s) of ship {shipB.UniqueID} carried onto ship {shipA.UniqueID}.");
                SeamHandover.Apply(handover, "Merge");
                var counters = IdCounters.Capture(shipA);
                InvokeSplitEvent(contiguityB, "ShipSplit", shipB, shipA);
                counters.KeepForward(shipA);

                Log.Info($"Merge: moved {parts.Count} parts from ship {shipB.UniqueID} onto ship {shipA.UniqueID} under {xf}.");
                movedRecords.Clear();
                CrewTargets.Touched(shipA);
                return true;
            }
            catch (Exception ex)
            {
                Log.Exception("ShipMerger.Merge", ex);
                if (movedRecords.Count > 0)
                {
                    int restored = RollbackMerge(shipA, shipB, movedRecords, xf, arriving);
                    Log.Error($"Merge FAILED MID-WAY: rolled back {restored} of {movedRecords.Count} moved parts onto ship {shipB.UniqueID} (of {parts.Count}). The joint was released before the move; the pair re-forms through the flush-pair path on the next service tick.");
                }
                return false;
            }
            finally
            {
                if (batchesOpen)
                {
                    try { shipB.PartNetworks.EndBatchOperation(); } catch (Exception ex) { Log.Exception("ShipMerger.Merge(end batch B)", ex); }
                    try { shipA.PartNetworks.EndBatchOperation(); } catch (Exception ex) { Log.Exception("ShipMerger.Merge(end batch A)", ex); }
                }
                foreach (IShipSplitHandler h in handlers)
                {
                    try { h.Dispose(); } catch { }
                }
                contiguityB.IsSplitting = false;

                try
                {
                    bool empty = true;
                    foreach (Part _ in shipB.Parts) { empty = false; break; }
                    if (empty)
                    {
                        bool wasSelected = SelectionKeeper.IsDirectlySelected(shipB);
                        RetargetAttacks(shipB, shipA);
                        shipA.Sim.Ships.Remove(shipB, dispose: true, handleLostShip: false);
                        if (wasSelected) SelectionKeeper.MoveTo(shipA, shipB);
                    }
                }
                catch (Exception ex)
                {
                    Log.Exception("ShipMerger.Merge(remove empty ship)", ex);
                }
                HingeJointManager.EndSurgery();
                HingeJointManager.ResumeSuspended();
            }
        }

        private static void RetargetAttacks(Ship from, Ship to)
        {
            try
            {
                if (from?.Sim == null || to == null) return;
                int n = 0;
                foreach (Ship s in from.Sim.Ships)
                {
                    if (s?.Commands == null || ReferenceEquals(s, to) || ReferenceEquals(s, from)) continue;
                    foreach (Command c in s.Commands)
                    {
                        if (c is AttackCommand ac && ReferenceEquals(ac._target, from))
                        {
                            ac.Target = to;
                            n++;
                        }
                    }
                }
                if (n > 0) Log.Info($"Merge: {n} attack order(s) aimed at ship {from.UniqueID} now aim at ship {to.UniqueID}, which took its parts.");
            }
            catch (Exception ex)
            {
                Log.Exception("ShipMerger.RetargetAttacks", ex);
            }
        }

        private static int RollbackMerge(Ship shipA, Ship shipB,
            List<(Part Part, IntVector2 Loc, int RotFlip, List<Door> Doors, List<ShipCrew> Crew)> moved, GridTransform xf,
            HashSet<IntVector2> arriving)
        {
            int restored = 0;
            GridTransform inv = xf.Inverse();
            for (int i = moved.Count - 1; i >= 0; i--)
            {
                var m = moved[i];
                Part part = m.Part;
                try
                {
                    if (part.Ship != shipA) continue;
                    var crewNow = part.CrewPresent != null ? new List<ShipCrew>(part.CrewPresent) : new List<ShipCrew>();
                    var doorsNow = new List<Door>(part.GetDoors());
                    PartForwarding.Record(part, shipA);
                    shipA.Parts.RemovePart(part, PartsManager.CrewAction.None, destroyed: false, removeDoors: false, null);
                    part.Location = m.Loc;
                    s_rotFlipRef(part) = m.RotFlip;
                    shipB.Parts.AddPart(part);
                    foreach (Door door in doorsNow)
                    {
                        shipA.Doors.RemoveDoor(door, destroyed: false);
                        if (!xf.IsIdentity)
                        {
                            DoorLocation dl = door.Location;
                            dl.Cell -= xf.T;
                            dl = dl.RotateAround(s_zero, inv.K);
                            s_doorLocField.SetValue(door, dl);
                        }
                        shipB.Doors.AddDoor(door);
                    }
                    foreach (ShipCrew crew in crewNow)
                    {
                        if (crew.Soul.HomeShip == shipA)
                        {
                            shipA.Crew.UnassignCrew(crew.Soul, killed: false, removeFromJob: false);
                            shipB.Crew.AssignCrew(crew.Soul);
                        }
                        shipA.Crew.RemoveCrew(crew, unregisterWithSim: false);
                        if (!xf.IsIdentity)
                        {
                            Vector2 grid = crew.Location / shipA.Scale;
                            Vector2 back = inv.TransformPoint(grid) * shipB.Scale;
                            crew.SetLocation(back, lerpFromPrevious: false, queueGeometryRefresh: false, clearPath: false);
                            crew.SetRotation(new Direction((float)crew.Rotation - xf.Theta), lerpFromPrevious: false, queueGeometryRefresh: false);
                        }
                        shipB.Crew.AddCrew(crew, registerWithSim: false);
                    }
                    restored++;
                }
                catch (Exception ex)
                {
                    Log.Exception("ShipMerger.RollbackMerge(part)", ex);
                }
            }
            try { shipA.Physics.ForceUpdateFixtures(); shipB.Physics.ForceUpdateFixtures(); }
            catch (Exception ex) { Log.Exception("ShipMerger.RollbackMerge(fixtures)", ex); }
            // TODO: a failure AFTER B's pending entries were restored onto A leaves those copies on A (B keeps its own).
            try
            {
                PendingRepairs.MatchKeeping(shipA, pi =>
                {
                    foreach (IntVector2 c in pi.Rect) if (arriving.Contains(c)) return true;
                    return false;
                });
                PendingRepairs.MatchKeeping(shipB, _ => false);
            }
            catch (Exception ex) { Log.Exception("ShipMerger.RollbackMerge(blueprints)", ex); }
            ShipSplitter.FlushGeometry(shipA);
            ShipSplitter.FlushGeometry(shipB);
            return restored;
        }

        internal static void TransformedPlacement(Part part, GridTransform xf, out IntVector2 loc, out int rot)
        {
            if (xf.IsIdentity)
            {
                loc = part.Location;
                rot = part.Rotation;
                return;
            }
            IntRect r = part.Rect;
            r.Size -= IntVector2.One;
            r = r.Rotate90Around(in s_zero, xf.K);
            rot = part.Rules.IsRotateable ? Mathx.Wrap(part.Rotation + xf.K, 0, 3) : part.Rotation;
            loc = r.Location + xf.T;
        }

        private static void TransferTileStatuses(IShipSplitHandler h, Part part, Ship shipA, GridTransform xf)
        {
            ref TempList<Status<IntVector2>> list = ref s_tileListRef(h);
            if (list == null || list.Count == 0) return;
            TileStatusHandler src = s_tileSrcRef(h);
            TileStatusHandler dest = shipA.Statuses.GetTileHandler(src.StatusType);
            var multi = new MultiStatusList<IntVector2>();
            dest._partMultiStatusLists.Add(part, multi);
            foreach (Status<IntVector2> st in list)
                dest.TransferStatus(st, xf.TransformCell(st.Location));
            list.Clear();
            ((IStatusReceiver)part).OnStatusLocationInfoCreated((IStatusLocationInfo)multi);
        }

        private static void RetargetDestinations(Ship shipA, Ship shipB, List<Part> movedParts, GridTransform xf)
        {
            try
            {
                var moved = new HashSet<Part>(movedParts);
                int fixedCount = 0;
                void Fix(CrewSoul soul)
                {
                    if (soul == null) return;
                    Destination d = soul.Destination;
                    Part part = d.Part;
                    if (part == null || !moved.Contains(part) || !d.SpecificPoint.HasValue) return;
                    Vector2 pt = xf.TransformPoint(d.SpecificPoint.Value / shipB.Scale) * shipA.Scale;
                    soul.Destination = new Destination(part, pt);
                    fixedCount++;
                }
                foreach (CrewSoul soul in shipA.Crew.Assigned)
                    Fix(soul);
                foreach (ShipCrew crew in shipA.Crew.OnShip)
                    if (crew.Soul.HomeShip != shipA) Fix(crew.Soul);
                if (fixedCount > 0)
                    Log.Info($"Merge: {fixedCount} crew destination(s) on moved parts re-targeted.");
            }
            catch (Exception ex)
            {
                Log.Exception("ShipMerger.RetargetDestinations", ex);
            }
        }

        private static void MergeRolesInto(Ship shipA, Ship shipB)
        {
            try
            {
                int added = 0;
                foreach (Role role in shipB.Roles)
                {
                    if (shipA.Roles.GetRoleByID(role.ID) != null) continue;
                    shipA.Roles.AddRole(role.Clone());
                    added++;
                }
                if (added > 0)
                    Log.Info($"Merge: {added} crew role(s) of ship {shipB.UniqueID} added to ship {shipA.UniqueID}.");
            }
            catch (Exception ex)
            {
                Log.Exception("ShipMerger.MergeRolesInto", ex);
            }
        }

        internal static void CopyPartMetadata(Ship shipB, Ship shipA, Dictionary<PartSaveData, PartSaveData> keyMap, GridTransform xf)
        {
            var src = shipB.Metadata;
            var dst = shipA.Metadata;
            if (src == null || dst == null) return;
            PartSaveData Map(PartSaveData k) => keyMap.TryGetValue(k, out PartSaveData m) ? m : k;
            bool Moving(PartSaveData k) => keyMap.ContainsKey(k);
            int copied = 0;

            try
            {
                foreach (var kv in src.PartControlGroups)
                {
                    if (!dst.PartControlGroups.TryGetValue(kv.Key, out HashSet<PartSaveData> set))
                        dst.PartControlGroups.Add(kv.Key, set = new HashSet<PartSaveData>());
                    foreach (PartSaveData psd in kv.Value) { if (!Moving(psd)) continue; set.Add(Map(psd)); copied++; }
                }
            }
            catch (Exception ex) { Log.Exception("ShipMerger.CopyPartMetadata(control groups)", ex); }

            try
            {
                var arriving = new HashSet<PartSaveData>(keyMap.Values);
                var staleToggles = new List<(PartSaveData, Cosmoteer.Data.ID<Cosmoteer.Game.PartToggleGuiRules>)>();
                foreach (var kv in dst.PartUIToggleStates) if (arriving.Contains(kv.Key.Item1)) staleToggles.Add(kv.Key);
                foreach (var key in staleToggles)
                {
                    dst.PartUIToggleStates.Set(key, null);
                    dst.PartUIToggleStates.Set(key, null, blueprintMode: true);
                }
                if (staleToggles.Count > 0)
                    Log.Info($"Merge: {staleToggles.Count} toggle entr{(staleToggles.Count == 1 ? "y" : "ies")} of the arriving parts left on ship {shipA.UniqueID} from before the split cleared.");
                var staleColours = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(
                    System.Linq.Enumerable.Where(dst.PartUIColorValues, kv => arriving.Contains(kv.Key.Item1)), kv => kv.Key));
                foreach (var key in staleColours)
                {
                    dst.PartUIColorValues.Set(key, null);
                    dst.PartUIColorValues.Set(key, null, blueprintMode: true);
                }

                foreach (var kv in src.PartUIToggleStates)
                {
                    if (!Moving(kv.Key.Item1)) continue;
                    var key = (Map(kv.Key.Item1), kv.Key.Item2);
                    dst.PartUIToggleStates.Set(key, kv.Value.Item1);
                    dst.PartUIToggleStates.Set(key, kv.Value.Item2, blueprintMode: true);
                    copied++;
                }
                foreach (var kv in src.PartUIColorValues)
                {
                    if (!Moving(kv.Key.Item1)) continue;
                    var key = (Map(kv.Key.Item1), kv.Key.Item2);
                    dst.PartUIColorValues.Set(key, kv.Value.Item1);
                    dst.PartUIColorValues.Set(key, kv.Value.Item2, blueprintMode: true);
                    copied++;
                }
            }
            catch (Exception ex) { Log.Exception("ShipMerger.CopyPartMetadata(toggles)", ex); }

            try
            {
                foreach (var kv in src.CrewSourceRoles) { if (!Moving(kv.Key)) continue; dst.CrewSourceRoles[Map(kv.Key)] = kv.Value; copied++; }
                foreach (var kv in src.CrewSourceTargets)
                {
                    if (!Moving(kv.Key)) continue;
                    PartSaveData k = Map(kv.Key), v = Map(kv.Value);
                    if (!dst.CrewSourceTargets.Contains(k, v)) { dst.CrewSourceTargets.Add(k, v); copied++; }
                }
                foreach (var kv in src.ResourceSupplierTargets)
                {
                    if (!Moving(kv.Key)) continue;
                    PartSaveData k = Map(kv.Key), v = Map(kv.Value);
                    if (!dst.ResourceSupplierTargets.Contains(k, v)) { dst.ResourceSupplierTargets.Add(k, v); copied++; }
                }
            }
            catch (Exception ex) { Log.Exception("ShipMerger.CopyPartMetadata(crew/resource targets)", ex); }

            try
            {
                foreach (var kv in src.FlexResourceGridTypes)
                {
                    if (shipB.Parts[kv.Key, PartRectType.Normal] == null) continue;
                    dst.FlexResourceGridTypes[xf.TransformCell(kv.Key)] = kv.Value; copied++;
                }
                foreach (var kv in src.ResourceConsumptionToggles) { if (!Moving(kv.Key.Item1)) continue; dst.ResourceConsumptionToggles[(Map(kv.Key.Item1), kv.Key.Item2)] = kv.Value; copied++; }
                foreach (var kv in src.ResourceSupplyToggles) { if (!Moving(kv.Key.Item1)) continue; dst.ResourceSupplyToggles[(Map(kv.Key.Item1), kv.Key.Item2)] = kv.Value; copied++; }
            }
            catch (Exception ex) { Log.Exception("ShipMerger.CopyPartMetadata(resources)", ex); }

            try
            {
                foreach (var kv in src.WeaponSelfTargets)
                {
                    if (!Moving(kv.Key.Item1)) continue;
                    var key = (Map(kv.Key.Item1), kv.Key.Item2);
                    if (!dst.WeaponSelfTargets.ContainsKey(key)) { dst.WeaponSelfTargets.Add(key, Map(kv.Value)); copied++; }
                }
                foreach (var kv in src.CustomWeaponCoverages)
                {
                    if (!Moving(kv.Key.Item1) || kv.Value.Confirmed == default(WeaponCoverage)) continue;
                    dst.CustomWeaponCoverages[(Map(kv.Key.Item1), kv.Key.Item2)] = new MPValue<WeaponCoverage>(kv.Value.Confirmed);
                    copied++;
                }
                foreach (var kv in src.WeaponDirectControlBindings) { if (!Moving(kv.Key.Item1)) continue; dst.WeaponDirectControlBindings[(Map(kv.Key.Item1), kv.Key.Item2)] = kv.Value; copied++; }
            }
            catch (Exception ex) { Log.Exception("ShipMerger.CopyPartMetadata(weapons)", ex); }

            if (copied > 0)
                Log.Info($"Merge: {copied} part-metadata entr{(copied == 1 ? "y" : "ies")} (crew allocations, toggle states, control groups, weapon targets) carried onto ship {shipA.UniqueID}.");
        }

        internal readonly struct IdCounters
        {
            private readonly int _globalJob, _localJob, _globalCommand, _localCommand, _globalTarget, _localTarget;

            private IdCounters(Ship s)
            {
                _globalJob = s.Jobs._nextGlobalJobID;
                _localJob = s.Jobs._nextLocalJobID;
                _globalCommand = s.Commands._nextGlobalCommandID;
                _localCommand = s.Commands._nextLocalCommandID;
                _globalTarget = s.Weapons._nextGlobalTargetID;
                _localTarget = s.Weapons._nextLocalTargetID;
            }

            public static IdCounters Capture(Ship s) => new IdCounters(s);

            public static int AboveLiveJobs(Ship s)
            {
                var byId = s?.Resources?._manualTransferJobsByID;
                if (byId == null) return 0;
                int start = s.Jobs._nextGlobalJobID;
                var live = new List<int>(byId.Count);
                foreach (CommandID id in byId.Keys)
                    if (id.PlayerID == null) live.Add(id.SequenceNum);
                int next = NextAbove(start, live.ToArray());
                s.Jobs._nextGlobalJobID = next;
                return next - start;
            }

            public void KeepForward(Ship s)
            {
                try
                {
                    s.Jobs._nextGlobalJobID = Math.Max(s.Jobs._nextGlobalJobID, _globalJob);
                    s.Jobs._nextLocalJobID = Math.Max(s.Jobs._nextLocalJobID, _localJob);
                    s.Commands._nextGlobalCommandID = Math.Max(s.Commands._nextGlobalCommandID, _globalCommand);
                    s.Commands._nextLocalCommandID = Math.Max(s.Commands._nextLocalCommandID, _localCommand);
                    s.Weapons._nextGlobalTargetID = Math.Max(s.Weapons._nextGlobalTargetID, _globalTarget);
                    s.Weapons._nextLocalTargetID = Math.Max(s.Weapons._nextLocalTargetID, _localTarget);
                    AboveLiveJobs(s);
                }
                catch (Exception ex)
                {
                    Log.Exception("ShipMerger.IdCounters.KeepForward", ex);
                }
            }
        }

        public static int NextAbove(int next, int[] live)
        {
            int result = next;
            if (live == null) return result;
            foreach (int v in live)
                if (v != int.MaxValue && v >= result) result = v + 1;
            return result;
        }

        internal static void EnsureJobIdsAbove(Ship s)
        {
            try { IdCounters.AboveLiveJobs(s); }
            catch (Exception ex) { Log.Exception("ShipMerger.EnsureJobIdsAbove", ex); }
        }

        public static int[] FreshIds(int next, HashSet<int> usedA, HashSet<int> usedB, int count)
        {
            var ids = new int[Math.Max(0, count)];
            int n = next;
            for (int i = 0; i < ids.Length; i++)
            {
                while (usedA.Contains(n) || usedB.Contains(n)) n++;
                ids[i] = n++;
            }
            return ids;
        }

        private static int RekeyCollidingManualJobs(Ship shipA, Ship shipB)
        {
            try
            {
                var byA = shipA?.Resources?._manualTransferJobsByID;
                var byB = shipB?.Resources?._manualTransferJobsByID;
                if (byA == null || byB == null || byA.Count == 0 || byB.Count == 0) return 0;
                var clash = new List<KeyValuePair<CommandID, ResourceTransferJob>>();
                foreach (KeyValuePair<CommandID, ResourceTransferJob> kv in byB)
                    if (byA.ContainsKey(kv.Key)) clash.Add(kv);
                if (clash.Count == 0) return 0;
                clash.Sort((x, y) => x.Key.SequenceNum.CompareTo(y.Key.SequenceNum));
                var usedA = new HashSet<int>();
                var usedB = new HashSet<int>();
                foreach (CommandID id in byA.Keys) if (id.PlayerID == null) usedA.Add(id.SequenceNum);
                foreach (CommandID id in byB.Keys) if (id.PlayerID == null) usedB.Add(id.SequenceNum);
                int next = Math.Max(shipA.Jobs._nextGlobalJobID, shipB.Jobs._nextGlobalJobID);
                int[] fresh = FreshIds(next, usedA, usedB, clash.Count);
                for (int i = 0; i < clash.Count; i++)
                {
                    ResourceTransferJob job = clash[i].Value;
                    var neu = new CommandID(null, fresh[i]);
                    byB.Remove(clash[i].Key);
                    ((Cosmoteer.Ships.Resources.ResourceManager.IResourceTransferJobMembers)job).ManualID = neu;
                    byB.Add(neu, job);
                }
                shipA.Jobs._nextGlobalJobID = Math.Max(shipA.Jobs._nextGlobalJobID, fresh[fresh.Length - 1] + 1);
                return clash.Count;
            }
            catch (Exception ex)
            {
                Log.Exception("ShipMerger.RekeyCollidingManualJobs", ex);
                return 0;
            }
        }

        private static readonly System.Reflection.FieldInfo s_manualJobAdded = AccessTools.Field(typeof(Cosmoteer.Ships.Resources.ResourceManager), "ManualJobAdded");

        private static int MigrateManualJobs(Ship shipB, Ship shipA)
        {
            int n = 0;
            try
            {
                var rb = shipB?.Resources;
                var ra = shipA?.Resources;
                if (rb == null || ra == null || rb._manualTransferJobs.Count == 0) return 0;
                foreach (ResourceTransferJob job in new List<ResourceTransferJob>(rb._manualTransferJobs))
                {
                    if (job?.ManualID == null) continue;
                    CommandID id = job.ManualID.Value;
                    if (ra._manualTransferJobsByID.ContainsKey(id))
                    {
                        var used = new HashSet<int>();
                        foreach (CommandID k in ra._manualTransferJobsByID.Keys) if (k.PlayerID == null) used.Add(k.SequenceNum);
                        int fresh = FreshIds(shipA.Jobs._nextGlobalJobID, used, new HashSet<int>(), 1)[0];
                        rb._manualTransferJobsByID.Remove(id);
                        id = new CommandID(null, fresh);
                        ((Cosmoteer.Ships.Resources.ResourceManager.IResourceTransferJobMembers)job).ManualID = id;
                        rb._manualTransferJobsByID[id] = job;
                        shipA.Jobs._nextGlobalJobID = Math.Max(shipA.Jobs._nextGlobalJobID, fresh + 1);
                    }
                    shipB.Jobs.RemoveJob(job, unassignCrew: false);
                    rb._manualTransferJobs.Remove(job);
                    rb._manualTransferJobsByID.Remove(id);
                    shipA.Jobs.AddJob(job);
                    ra._manualTransferJobs.Add(job);
                    ra._manualTransferJobsByID.Add(id, job);
                    (s_manualJobAdded?.GetValue(ra) as Action<ResourceTransferJob>)?.Invoke(job);
                    n++;
                }
                rb.SetNeedsRefreshFixedUpdatingEnabled();
                ra.SetNeedsRefreshFixedUpdatingEnabled();
                IdCounters.AboveLiveJobs(shipA);
            }
            catch (Exception ex)
            {
                Log.Exception("ShipMerger.MigrateManualJobs", ex);
            }
            return n;
        }

        private static void InvokeSplitEvent(StructureContiguityManager mgr, string name, Ship oldShip, Ship newShip)
        {
            try
            {
                var field = AccessTools.Field(typeof(StructureContiguityManager), name);
                (field?.GetValue(mgr) as Action<Ship, Ship>)?.Invoke(oldShip, newShip);
            }
            catch (Exception ex)
            {
                Log.Exception("ShipMerger.InvokeSplitEvent(" + name + ")", ex);
            }
        }
    }
}
