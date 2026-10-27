using System;
using System.Collections.Generic;
using Cosmoteer.Crew;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Crew;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Doors;
using Cosmoteer.Ships.Paint;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;
using Halfling.Physics2D.Dynamics;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class ShipSplitter
    {
        private static void AttachOrphans(Ship ship, List<Part> farParts, Part anchor)
        {
            var far = new HashSet<Part>(farParts);
            if (far.Contains(anchor)) return;
            var reached = new HashSet<Part> { anchor };
            var queue = new Queue<Part>();
            queue.Enqueue(anchor);
            var near = new List<Part>();
            while (queue.Count > 0)
            {
                Part cur = queue.Dequeue();
                StructuralAdjacency.NeighboursInto(ship, cur, near, ignoreJointSeams: false, ignoreRailEdges: false);
                foreach (Part n in near)
                {
                    if (far.Contains(n) || reached.Contains(n)) continue;
                    reached.Add(n);
                    queue.Enqueue(n);
                }
            }
            int orphans = 0;
            foreach (Part p in ship.Parts)
            {
                if (far.Contains(p) || reached.Contains(p)) continue;
                farParts.Add(p);
                far.Add(p);
                orphans++;
            }
            if (orphans > 0)
                Log.Info($"Split: {orphans} part(s) beyond the far side were only attached through it and go with it (no engine split).");
        }

        public static Ship Split(Ship shipA, List<Part> farParts, Part anchor = null)
        {
            CommandGateBatch.Enter();
            Ship result = null;
            try
            {
                result = SplitCore(shipA, farParts, anchor);
                return result;
            }
            finally
            {
                CommandGateBatch.Exit(shipA, result);
            }
        }

        private static Ship SplitCore(Ship shipA, List<Part> farParts, Part anchor)
        {
            if (shipA?.Sim == null || farParts == null || farParts.Count == 0)
                return null;
            if (anchor != null) AttachOrphans(shipA, farParts, anchor);

            PendingRepairs.Partition(shipA, PendingRepairs.Collect(shipA), farParts,
                out List<PendingRepairs.Entry> pendNear, out List<PendingRepairs.Entry> pendFar);
            var leaving = new HashSet<(PartRules, IntVector2, int, bool)>();
            foreach (Part p in farParts) leaving.Add(PendingRepairs.KeyOf(p.Rules, p.Location, p.Rotation, p.FlipX));
            foreach (PendingRepairs.Entry e in pendFar) if (e.Blueprint) leaving.Add(PendingRepairs.KeyOf(e.Rules, e.Loc, e.Rot, e.Flip));

            var contiguity = shipA.StructureContiguity;
            contiguity.IsSplitting = true;
            HingeJointManager.BeginSurgery();
            Ship shipB = null;
            var handlers = new List<IShipSplitHandler>();
            bool batchAOpen = false;
            bool batchBOpen = false;
            var moved = new List<Part>();
            try
            {
                shipA.PartNetworks.BeginBatchOperation();
                batchAOpen = true;
                foreach (IShipSplitHandlerFactory factory in contiguity._shipSplitHandlerFactories)
                    handlers.Add(factory.CreateHandler(shipA));

                shipB = Ship.CloneEmptyFrom(shipA, shipA.Rand.UInt64());
                shipA.Sim.Ships.Add(shipB, giveDefaultCommand: false, reseedShipRand: false);
                shipB.Metadata.CopyPartMetadataFrom(shipA.Metadata);
                shipB.Roles.CopyRolesFrom(shipA.Roles);
                shipB.SelfInvulnerable = shipA.SelfInvulnerable;
                shipB.PartNetworks.BeginBatchOperation();
                batchBOpen = true;
                List<SeamHandover.Record> handover = SeamHandover.Capture(shipA);
                InvokeSplitEvent(contiguity, "ShipSplitting", shipA, shipB);

                var crewOnPart = new List<ShipCrew>();
                var doorsOnPart = new List<Door>();
                Dictionary<CrewSoul, bool?> bunkSides = AssemblyCrewHome.BunkSides(shipA, new HashSet<Part>(farParts));
                var standingFar = new HashSet<CrewSoul>();
                foreach (Part part in farParts)
                {
                    crewOnPart.Clear();
                    doorsOnPart.Clear();
                    if (part.CrewPresent != null)
                        crewOnPart.AddRange(part.CrewPresent);
                    doorsOnPart.AddRange(part.GetDoors());

                    foreach (IShipSplitHandler h in handlers)
                        h.OnPartSplitting(part, shipB);

                    PartForwarding.Record(part, shipA);
                    shipA.Parts.RemovePart(part, PartsManager.CrewAction.None, destroyed: false, removeDoors: false, null);
                    shipB.Parts.AddPart(part);
                    moved.Add(part);
                    MergeFaultInjection.SplitMoved(moved.Count);

                    foreach (Door door in doorsOnPart)
                    {
                        shipA.Doors.RemoveDoor(door, destroyed: false);
                        shipB.Doors.AddDoor(door);
                    }
                    foreach (ShipCrew crew in crewOnPart)
                    {
                        standingFar.Add(crew.Soul);
                        shipA.Crew.RemoveCrew(crew, unregisterWithSim: false);
                        shipB.Crew.AddCrew(crew, registerWithSim: false);
                    }

                    foreach (IShipSplitHandler h in handlers)
                        h.OnPartSplit(part, shipB);
                }

                int homedFar = 0, offHome = 0;
                var rehomed = new List<CrewSoul>();
                foreach (KeyValuePair<CrewSoul, bool?> kv in bunkSides)
                {
                    CrewSoul soul = kv.Key;
                    if (!ReferenceEquals(soul.HomeShip, shipA)) continue;
                    if (!AssemblyCrewHome.GoesFar(kv.Value, standingFar.Contains(soul))) continue;
                    shipA.Crew.UnassignCrew(soul, killed: false, removeFromJob: false);
                    shipB.Crew.AssignCrew(soul);
                    rehomed.Add(soul);
                    homedFar++;
                    if (!standingFar.Contains(soul)) offHome++;
                }
                foreach (CrewSoul soul in standingFar)
                    if (ReferenceEquals(soul.HomeShip, shipA)) offHome++;
                AssemblyCrewHome.RebindBunks(rehomed);
                if (homedFar > 0 || offHome > 0)
                    Log.Info($"Split: {homedFar} crew belong to the far side by their bunk; {offHome} stand on the other side's parts and walk home when the ships are at rest.");

                SurgeryKin.Record(shipA, shipB);
                foreach (CrewSoul soul in shipA.Crew.Assigned)
                {
                    Job job = soul.Job;
                    if (job != null && job.Ship != shipA && !SurgeryKin.Are(job.Ship, shipA))
                        job.UnassignCrew(soul);
                }
                shipA.Physics.ForceUpdateFixtures();
                PendingRepairs.MatchKeeping(shipA, pi => leaving.Contains(PendingRepairs.KeyOf(pi.Rules, pi.Location, pi.Rotation, pi.FlipX)));
                PendingRepairs.RemoveWrecks(shipA, pendFar);
                shipA.PartNetworks.EndBatchOperation();
                batchAOpen = false;
                FlushGeometry(shipA);

                foreach (CrewSoul soul in shipB.Crew.Assigned)
                {
                    Job job = soul.Job;
                    if (job != null && job.Ship != shipB && !SurgeryKin.Are(job.Ship, shipB))
                        job.UnassignCrew(soul);
                }
                PendingRepairs.MatchKeeping(shipB, _ => true, PendingRepairs.BlueprintCells(pendFar, GridTransform.Identity));
                var (farBack, farSkipped) = PendingRepairs.Restore(shipB, pendFar, GridTransform.Identity);
                PendingRepairs.RemoveStaleWrecks(shipA);
                PendingRepairs.RemoveStaleWrecks(shipB);
                if (pendFar.Count > 0 || farSkipped > 0)
                    Log.Info($"Split: carried {farBack} pending repair(s) / planned part(s) with the far side ({farSkipped} skipped); {pendNear.Count} left untouched on ship {shipA.UniqueID}.");
                int decalsMoved = 0, decalsShared = 0;
                for (int i = 0; i < shipB.DecalLayers.Length; i++)
                    SplitDecals(shipA, shipB, i, ref decalsMoved, ref decalsShared);
                shipB.Metadata.CopyAestheticsFrom(shipA.Metadata);
                shipB.Physics.ForceUpdateFixtures();

                Body bodyB = shipB.Physics.Body;
                Body bodyA = shipA.Physics.Body;
                bodyB.Position = bodyA.Position;
                bodyB.Rotation = bodyA.Rotation;
                bodyB.LinearVelocity = bodyA.GetLinearVelocityFromWorldPoint(bodyB.WorldCenter);
                bodyB.AngularVelocity = bodyA.AngularVelocity;
                shipB.Physics.PhysicsWrapper.CopyInterpolationFrom(shipA.Physics.PhysicsWrapper);

                shipB.Metadata.FlightDirection = shipA.Metadata.FlightDirection;
                shipB.Metadata.DefaultAttackRadius = shipA.Metadata.DefaultAttackRadius;
                shipB.Metadata.DefaultAttackRotation = shipA.Metadata.DefaultAttackRotation;
                shipB.Metadata.DefaultAttackFollowAngle = shipA.Metadata.DefaultAttackFollowAngle;
                shipB.Metadata.CullPartMetadata();

                shipA.Sim.Ships.RefreshShipPosition(shipB);
                shipB.PartNetworks.EndBatchOperation();
                batchBOpen = false;
                FlushGeometry(shipB);
                InvokeSplitEvent(contiguity, "ShipSplit", shipA, shipB);
                CrewTargets.Touched(shipA);
                CrewTargets.Touched(shipB);
                SeamHandover.Apply(handover, "Split");

                if (shipB.Crew.Assigned.Count == 0)
                    Log.Info($"Hinge split: far segment {shipB.UniqueID} has no assigned crew. It stays player-owned for now, but any later damage event can demote a crewless segment to junk -- put a crewed part (bunk/control) on the far side.");

                Log.Info($"Hinge split: moved {farParts.Count} parts from ship {shipA.UniqueID} to new segment {shipB.UniqueID}; decals {decalsMoved} moved, {decalsShared} shared.");
                moved.Clear();
                return shipB;
            }
            catch (Exception ex)
            {
                Log.Exception("ShipSplitter.Split", ex);
                if (shipB != null && moved.Count > 0)
                {
                    int restored = 0;
                    for (int i = moved.Count - 1; i >= 0; i--)
                    {
                        Part part = moved[i];
                        try
                        {
                            var doors = new List<Door>(part.GetDoors());
                            var crew = part.CrewPresent != null ? new List<ShipCrew>(part.CrewPresent) : new List<ShipCrew>();
                            PartForwarding.Record(part, shipB);
                            shipB.Parts.RemovePart(part, PartsManager.CrewAction.None, destroyed: false, removeDoors: false, null);
                            shipA.Parts.AddPart(part);
                            foreach (Door door in doors)
                            {
                                shipB.Doors.RemoveDoor(door, destroyed: false);
                                shipA.Doors.AddDoor(door);
                            }
                            foreach (ShipCrew c in crew)
                            {
                                if (c.Soul.HomeShip == shipB)
                                {
                                    shipB.Crew.UnassignCrew(c.Soul, killed: false, removeFromJob: false);
                                    shipA.Crew.AssignCrew(c.Soul);
                                }
                                shipB.Crew.RemoveCrew(c, unregisterWithSim: false);
                                shipA.Crew.AddCrew(c, registerWithSim: false);
                            }
                            restored++;
                        }
                        catch (Exception rex)
                        {
                            Log.Exception("ShipSplitter.Split(rollback part)", rex);
                        }
                    }
                    Log.Error($"Split rolled back {restored} of {moved.Count} moved parts onto ship {shipA.UniqueID}.");
                }
                if (shipB != null)
                {
                    foreach (CrewSoul soul in new List<CrewSoul>(shipB.Crew.Assigned))
                    {
                        try
                        {
                            shipB.Crew.UnassignCrew(soul, killed: false, removeFromJob: false);
                            shipA.Crew.AssignCrew(soul);
                            AssemblyCrewHome.RebindBunks(new[] { soul });
                        }
                        catch (Exception cex) { Log.Exception("ShipSplitter.Split(rollback crew)", cex); }
                    }
                }
                return null;
            }
            finally
            {
                if (batchAOpen)
                {
                    try { shipA.PartNetworks.EndBatchOperation(); } catch (Exception ex) { Log.Exception("ShipSplitter.Split(end batch A)", ex); }
                }
                if (batchBOpen && shipB != null)
                {
                    try { shipB.PartNetworks.EndBatchOperation(); } catch (Exception ex) { Log.Exception("ShipSplitter.Split(end batch B)", ex); }
                }
                foreach (IShipSplitHandler h in handlers)
                {
                    try { h.Dispose(); } catch { }
                }
                contiguity.IsSplitting = false;
                try
                {
                    if (shipB != null)
                    {
                        bool empty = true;
                        foreach (Part _ in shipB.Parts) { empty = false; break; }
                        if (empty)
                        {
                            shipA.Sim.Ships.Remove(shipB, dispose: true, handleLostShip: false);
                            shipA.Physics?.ForceUpdateFixtures();
                            PendingRepairs.MatchKeeping(shipA, _ => false, PendingRepairs.BlueprintCells(pendFar, GridTransform.Identity));
                            var (back, skipped) = PendingRepairs.Restore(shipA, pendFar, GridTransform.Identity);
                            PendingRepairs.RemoveStaleWrecks(shipA);
                            if (pendFar.Count > 0) Log.Info($"Split rollback: {back} pending repair(s) / planned part(s) put back on ship {shipA.UniqueID} ({skipped} skipped).");
                            FlushGeometry(shipA);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Exception("ShipSplitter.Split(remove empty segment)", ex);
                }
                HingeJointManager.EndSurgery();
                HingeJointManager.ResumeSuspended();
            }
        }

        internal static void FlushGeometry(Ship ship)
        {
            try
            {
                ship?.Renderer?.RefreshGeometry();
            }
            catch (Exception ex)
            {
                Log.Exception("ShipSplitter.FlushGeometry", ex);
            }
        }

        private static void SplitDecals(Ship shipA, Ship shipB, int layer, ref int moved, ref int shared)
        {
            var toArm = new List<Decal>();
            var leaveMain = new List<Decal>();
            foreach (Decal d in shipA.DecalLayers[layer])
            {
                IntRect r = d.GetRect(shipA.Rules);
                if (!shipB.Parts.HasPartAt(r)) continue;
                toArm.Add(d);
                if (!shipA.Parts.HasPartAt(r)) leaveMain.Add(d);
            }
            foreach (Decal d in toArm)
                shipB.DecalLayers[layer].AddDecal(d, replaceExisting: true);
            foreach (Decal d in leaveMain)
                shipA.DecalLayers[layer].RemoveDecal(d.Location);
            moved += leaveMain.Count;
            shared += toArm.Count - leaveMain.Count;
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
                Log.Exception("ShipSplitter.InvokeSplitEvent(" + name + ")", ex);
            }
        }
    }
}
