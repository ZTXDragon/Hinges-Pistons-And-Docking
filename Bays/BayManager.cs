using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Crew;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Modes;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Construction;
using Cosmoteer.Ships.Doors;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Crew;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Geometry;
using Halfling.Scene2D;
using Halfling.Timing;

namespace ZTX.HingesPistonsDocking
{
    internal class BayManager : ShipComponent
    {
        private static readonly ConditionalWeakTable<Ship, BayManager> s_managers = new ConditionalWeakTable<Ship, BayManager>();

        private const int SERVICE_INTERVAL_TICKS = 30;
        private const int SCAN_INTERVAL_TICKS = 5;
        private const float GRID_TOLERANCE_CELLS = 0.05f;
        private const float GRID_TOLERANCE_RAD = 0.01f;
        private const string DEFAULT_NAME = "Bay build";

        public static readonly ID<PartToggleGuiRules> BuildId = new ID<PartToggleGuiRules>("ztx_bay_build");
        public static readonly ID<PartToggleGuiRules> ReleaseId = new ID<PartToggleGuiRules>("ztx_bay_release");
        public static readonly ID<PartToggleGuiRules> RepeatId = new ID<PartToggleGuiRules>("ztx_bay_repeat");
        public static readonly ID<PartToggleGuiRules> LaunchId = new ID<PartToggleGuiRules>("ztx_bay_launch");
        public static readonly ID<PartToggleGuiRules> FacingId = new ID<PartToggleGuiRules>("ztx_bay_facing");
        public static readonly ID<PartToggleGuiRules> StationOnId = new ID<PartToggleGuiRules>("ztx_bay_station_on");
        public static readonly ID<PartToggleGuiRules> Name1Id = new ID<PartToggleGuiRules>("ztx_bay_name_1");
        public static readonly ID<PartToggleGuiRules> Name2Id = new ID<PartToggleGuiRules>("ztx_bay_name_2");
        public static readonly ID<PartToggleGuiRules> ResupplyId = new ID<PartToggleGuiRules>("ztx_bay_resupply");
        public static readonly ID<PartToggleGuiRules> ClearId = new ID<PartToggleGuiRules>("ztx_bay_clear");
        public static readonly ID<PartToggleGuiRules>[] WhenIds =
        {
            new ID<PartToggleGuiRules>("ztx_bay_when_built"),
            new ID<PartToggleGuiRules>("ztx_bay_when_crewed"),
            new ID<PartToggleGuiRules>("ztx_bay_when_supplied"),
            new ID<PartToggleGuiRules>("ztx_bay_when_repaired"),
            new ID<PartToggleGuiRules>("ztx_bay_when_charged"),
        };

        public const int LAUNCH_DRIFT = 0, LAUNCH_FLY = 1, LAUNCH_PUSH = 2;

        private static readonly IntVector2[] s_sides =
        {
            new IntVector2(1, 0), new IntVector2(-1, 0), new IntVector2(0, 1), new IntVector2(0, -1)
        };

        private readonly List<ZtxBayTerminalComponent> _terminals = new List<ZtxBayTerminalComponent>();
        private readonly List<ZtxBayStationComponent> _stations = new List<ZtxBayStationComponent>();
        private int _tick;

        public BayManager()
        {
            base.FixedUpdateBucket = 10;
            base.FixedUpdatingEnabled = true;
        }

        #region Registration

        public static BayManager GetOrCreate(Ship ship)
        {
            if (ship == null) return null;
            if (s_managers.TryGetValue(ship, out BayManager manager)) return manager;
            manager = new BayManager();
            BayManager captured = manager;
            Ship capturedShip = ship;
            if (ship.Sim != null)
                ship.Sim.EnqueueDeterministic(ship.UniqueID, delegate { capturedShip.Components.Add(captured); });
            else
                ship.Components.Add(manager);
            s_managers.Add(ship, manager);
            return manager;
        }

        public static BayManager TryGet(Ship ship)
        {
            if (ship == null) return null;
            s_managers.TryGetValue(ship, out BayManager manager);
            return manager;
        }

        public void RegisterTerminal(ZtxBayTerminalComponent t)
        {
            if (!_terminals.Contains(t)) _terminals.Add(t);
        }

        public void OnTerminalLost(ZtxBayTerminalComponent t)
        {
            _terminals.Remove(t);
            if (t.Bay.Held != null)
            {
                HingeJointManager.ReleaseByTagDeliberately(t);
                DockThrust.Clear(t);
                Log.Info($"Bay terminal at {t.Part?.Location}: removed -- held ship {t.Bay.Held.UniqueID} let go.");
                t.Bay.Held = null;
            }
            foreach (ZtxBayStationComponent s in t.Bay.Stations) StopStation(s);
        }

        public void RegisterStation(ZtxBayStationComponent s)
        {
            if (!_stations.Contains(s)) _stations.Add(s);
        }

        public void DeregisterStation(ZtxBayStationComponent s)
        {
            _stations.Remove(s);
            StopStation(s);
        }

        public static List<Ship> HeldShips(Ship carrier, bool finishedOnly)
        {
            var result = new List<Ship>();
            BayManager mgr = TryGet(carrier);
            if (mgr == null) return result;
            foreach (ZtxBayTerminalComponent t in mgr._terminals)
            {
                Ship held = t.Bay.Held;
                if (held?.Sim == null || result.Contains(held)) continue;
                if (finishedOnly && !t.Bay.Built) continue;
                result.Add(held);
            }
            return result;
        }

        public static bool StationWorking(Part p)
        {
            ZtxBayStationComponent s = ZtxBayStationComponent.ForPart(p);
            return s != null && IsOn(p, StationOnId) && (s.Target.HasValue || s.Repair != null || s.SupplyShip != null);
        }

        public static ZtxBayTerminalComponent TerminalOfStation(ZtxBayStationComponent s)
        {
            BayManager mgr = TryGet(s?.Ship);
            if (mgr == null) return null;
            foreach (ZtxBayTerminalComponent t in mgr._terminals)
                if (t.Bay.InCharge && t.Bay.Stations.Contains(s)) return t;
            return null;
        }

        #endregion

        public override void FixedUpdate(FixedUpdater fixedUpdater, SceneRoot root)
        {
            try
            {
                Ship ship = base.Ship;
                if (ship?.Sim == null || ship.Physics?.Body == null) return;
                if (_terminals.Count == 0 && _stations.Count == 0) return;
                AssemblyIndex.Tick(base.Ship);
                _tick++;
                bool service = _tick % SERVICE_INTERVAL_TICKS == 0;
                if (service)
                {
                    if (_stations.Count > 0) SensorAreaOverlay.Attach(ship.Sim);
                    foreach (ZtxBayTerminalComponent t in _terminals.ToArray()) Service(ship, t);
                    DevoteHeldShips(ship);
                    foreach (ZtxBayStationComponent s in _stations.ToArray()) ServiceStationRepair(ship, s);
                }
                foreach (ZtxBayTerminalComponent t in _terminals.ToArray())
                    if (t.Bay.InCharge) WorkBay(ship, t, fixedUpdater.Interval);
                foreach (ZtxBayStationComponent s in _stations.ToArray())
                {
                    if (s.Target != null) continue;
                    if (s.Repair != null) WorkStationRepair(ship, s, fixedUpdater.Interval);
                    else if (s.SupplyShip != null) WorkStationSupply(ship, s, fixedUpdater.Interval);
                }
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("BayManager.FixedUpdate", ex);
            }
        }

        #region Bay scan (once a second)

        public static bool IsBayStructure(Part p)
        {
            if (p == null || p.IsDestroyed || !BackgroundFrame.Is(p.Rules)) return false;
            return ZtxDockPadComponent.ForPart(p) == null && ZtxDockClampComponent.ForPart(p) == null;
        }

        private static bool IsDock(Part p) =>
            p != null && (ZtxDockPadComponent.ForPart(p) != null || ZtxDockClampComponent.ForPart(p) != null);

        private static IntRect RectOf(Part p) => new PartInfo(p.Rules, p.Location, p.Rotation, p.FlipX).Rect;

        private static IEnumerable<IntVector2> Around(IntRect rect)
        {
            var inside = new HashSet<IntVector2>();
            foreach (IntVector2 c in rect) inside.Add(c);
            var seen = new HashSet<IntVector2>();
            foreach (IntVector2 c in inside)
                foreach (IntVector2 d in s_sides)
                {
                    IntVector2 n = c + d;
                    if (!inside.Contains(n) && seen.Add(n)) yield return n;
                }
        }

        private void Service(Ship ship, ZtxBayTerminalComponent terminal)
        {
            Part part = terminal.Part;
            if (part == null || part.Ship != ship) return;
            BayState bay = terminal.Bay;

            var structure = new List<Part>();
            var terminals = new List<ZtxBayTerminalComponent>();
            ScanStructure(ship, part, structure, terminals, bay);

            ZtxBayTerminalComponent boss = terminal;
            foreach (ZtxBayTerminalComponent t in terminals)
            {
                IntVector2 a = t.Part.Location, b = boss.Part.Location;
                if (a.Y < b.Y || (a.Y == b.Y && a.X < b.X)) boss = t;
            }
            bay.InCharge = ReferenceEquals(boss, terminal);
            if (!bay.InCharge)
            {
                SetWaiting(terminal, "second terminal on this bay -- not in use");
                return;
            }

            ScanDesign(ship, bay);
            if (bay.BlueprintDesign.Count > 0) DockSection.TakeFromQueue(ship, bay.BlueprintDesign, bay.BlueprintDoors);
            HandleClear(terminal);
            LoadStored(ship, terminal);
            if (BayDesign.ShouldCapture(bay.Pieces, bay.BlueprintDesign.Count, bay.Held != null))
            {
                Capture(ship, terminal);
                LoadStored(ship, terminal);
            }

            CheckHeld(ship, terminal);
            if (bay.Held != null) DockThrust.UpdateWelded(terminal, part, ship, bay.Held);
            else DockThrust.Clear(terminal);

            UpdateFacts(ship, bay);
            Ship worked = bay.Held;
            if (worked?.OptionalConstructionManager?.QueuedDoorDeconstructions.Count > 0) KeepDesignDoors(worked, bay);
            if (BayDesign.BlueprintsStale(bay.Done, bay.DoorsDone, bay.BlueprintedDone, bay.BlueprintedDoors, ReferenceEquals(worked, bay.BlueprintedShip)))
            {
                if (worked != null && bay.Done > 0) AddBlueprints(worked, bay);
                bay.BlueprintedDone = bay.Done;
                bay.BlueprintedDoors = bay.DoorsDone;
                bay.BlueprintedShip = worked;
            }
            if (bay.Held != null)
            {
                bay.Crewed = DockCrew.Crewed(bay.Held);
                bay.Supplied = DockSupply.Supplied(bay.Held, out string wants, DockSupply.MATERIALS);
                bay.Charged = DockSupply.Supplied(bay.Held, out _, DockSupply.ENERGY);
                bay.Missing = wants;
                ServiceCrew(ship, terminal);
            }
            else
            {
                bay.Crewed = bay.Supplied = bay.Charged = false;
                bay.Missing = null;
                bay.CrewNote = "";
            }

            bool[] ticked = Ticked(part);
            if (IsOn(part, RepeatId) && Array.IndexOf(ticked, true) < 0) ticked[BayRelease.BUILT] = true;
            if (bay.Held != null && BayRelease.ShouldRelease(IsOn(part, ReleaseId), ticked, Facts(bay)))
            {
                int homed = DockCrew.Homed(bay.Held), aboard = DockCrew.Aboard(bay.Held);
                int crewMode = HatchLinks.CrewMode(part, HatchLinks.BayCrewId);
                if (BayRelease.BoardingBlocks(crewMode == HatchLinks.CREW_BOARD, homed, aboard))
                {
                    SetWaiting(terminal, $"holding -- {homed - aboard} crew still boarding");
                    return;
                }
                Release(ship, terminal);
                return;
            }

            bool build = IsOn(part, BuildId);
            if (!build) { SetWaiting(terminal, bay.Held != null ? "holding -- Build is off" : "Build is off"); return; }
            if (bay.Pieces > 1) { SetWaiting(terminal, $"the design has {bay.Pieces} separate pieces -- join them or remove one"); return; }
            if (bay.Design.Count == 0) { SetWaiting(terminal, "no design -- place blueprints inside the frames in build mode"); return; }
            if (bay.Stations.Count == 0) { SetWaiting(terminal, "no station on this bay"); return; }

            bool crew = !IsFreeBuild(ship);

            if (bay.Held == null)
            {
                Ship blocker = BlockingShip(ship, bay);
                if (blocker != null) { SetWaiting(terminal, $"ship {blocker.UniqueID} covers the design's cells"); return; }
            }
            if (!crew)
            {
                if (!bay.Built) BuildInstant(ship, terminal);
                SetWaiting(terminal, bay.Built ? "built" : "building");
                return;
            }
            if (bay.Built) { SetWaiting(terminal, "built"); return; }
            SetWaiting(terminal, bay.OutOfReach > 0 ? $"building -- {bay.OutOfReach} parts out of reach of every station" : "building");
        }

        private static void ScanStructure(Ship ship, Part start, List<Part> structure, List<ZtxBayTerminalComponent> terminals, BayState bay)
        {
            var cells = new HashSet<IntVector2>();
            var stations = new List<ZtxBayStationComponent>();
            var seen = new HashSet<Part> { start };
            var queue = new Queue<Part>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                Part p = queue.Dequeue();
                structure.Add(p);
                foreach (IntVector2 c in RectOf(p)) cells.Add(c);
                ZtxBayTerminalComponent t = ZtxBayTerminalComponent.ForPart(p);
                if (t != null) terminals.Add(t);
                ZtxBayStationComponent s = ZtxBayStationComponent.ForPart(p);
                if (s != null) stations.Add(s);
                foreach (IntVector2 c in Around(RectOf(p)))
                {
                    Part q = ship.Parts[c, PartRectType.Normal];
                    if (q != null && IsBayStructure(q) && seen.Add(q)) queue.Enqueue(q);
                }
            }
            bay.StructureCells = cells;
            bay.Stations = stations;
        }

        private static bool Unbuilt(Ship ship, BlueprintPart bp)
        {
            var pi = new PartInfo(bp.Rules, bp.Location, bp.Rotation, bp.FlipX);
            foreach (IntVector2 c in pi.Rect)
                if (ship.Parts[c, PartRectType.Normal] != null) return false;
            return true;
        }

        private static void ScanDesign(Ship ship, BayState bay)
        {
            bay.BlueprintDesign.Clear();
            bay.BlueprintDoors.Clear();
            var cells = new HashSet<IntVector2>();
            var adjacent = new List<BlueprintPart>();
            var seedSeen = new HashSet<BlueprintPart>();
            foreach (IntVector2 cell in bay.StructureCells)
                foreach (IntVector2 d in s_sides)
                {
                    BlueprintPart bp = ship.BlueprintParts[cell + d, PartRectType.Normal];
                    if (bp != null && seedSeen.Add(bp) && Unbuilt(ship, bp)) adjacent.Add(bp);
                }
            HashSet<BlueprintPart> work = BayDesign.CarrierWork(adjacent, bp => BlueprintNeighbours(ship, bp), bp => IsCarrierKind(ship, bp));
            var seeds = new List<BlueprintPart>();
            foreach (BlueprintPart bp in adjacent)
                if (!work.Contains(bp)) seeds.Add(bp);
            List<List<BlueprintPart>> sections = BayDesign.Sections(seeds,
                bp => BlueprintNeighbours(ship, bp).Where(n => !work.Contains(n)),
                bp => TouchesCarrier(ship, bp));
            bay.Pieces = sections.Count;
            if (sections.Count != 1) return;
            foreach (BlueprintPart bp in sections[0])
            {
                var pi = new PartInfo(bp.Rules, bp.Location, bp.Rotation, bp.FlipX);
                bay.BlueprintDesign.Add(pi);
                foreach (IntVector2 c in pi.Rect) cells.Add(c);
            }
            foreach (BlueprintDoor bd in ship.BlueprintDoors)
                if (cells.Contains(bd.Location.Cell) && cells.Contains(bd.Location.OppositeCell))
                    bay.BlueprintDoors.Add(new DoorInfo(bd.Rules, bd.Location));
        }

        private static PartSaveData SaveKey(PartInfo pi) =>
            new PartSaveData(pi.Rules.ID, pi.Location + pi.Rules.GetSaveLocationOffset(pi.Rotation, pi.FlipX), pi.Rotation, pi.FlipX);

        private static void Capture(Ship ship, ZtxBayTerminalComponent terminal)
        {
            BayState bay = terminal.Bay;
            Part part = terminal.Part;
            try
            {
                Dictionary<(PartSaveData Part, ID<PartToggleGuiRules> ComponentID), int> states = ship.Metadata.PartUIToggleStates.GetMergedBlueprintStates();
                var byPart = new Dictionary<PartSaveData, List<BayDesignCodec.ToggleRec>>();
                foreach (var kv in states)
                {
                    if (!byPart.TryGetValue(kv.Key.Part, out List<BayDesignCodec.ToggleRec> list)) byPart[kv.Key.Part] = list = new List<BayDesignCodec.ToggleRec>();
                    list.Add(new BayDesignCodec.ToggleRec(kv.Key.ComponentID.ToString(), kv.Value));
                }
                var parts = new List<BayDesignCodec.PartRec>();
                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                foreach (PartInfo pi in bay.BlueprintDesign)
                {
                    var rec = new BayDesignCodec.PartRec { Id = pi.Rules.ID.ToString(), X = pi.Location.X, Y = pi.Location.Y, Rotation = pi.Rotation, FlipX = pi.FlipX };
                    if (byPart.TryGetValue(SaveKey(pi), out List<BayDesignCodec.ToggleRec> toggles))
                        rec.Toggles.AddRange(toggles.OrderBy(t => t.Id, StringComparer.Ordinal));
                    parts.Add(rec);
                    foreach (IntVector2 c in pi.Rect)
                    {
                        minX = Math.Min(minX, c.X); minY = Math.Min(minY, c.Y);
                        maxX = Math.Max(maxX, c.X); maxY = Math.Max(maxY, c.Y);
                    }
                }
                var doors = new List<BayDesignCodec.DoorRec>();
                foreach (DoorInfo di in bay.BlueprintDoors)
                    doors.Add(new BayDesignCodec.DoorRec { Id = di.Rules.ID.ToString(), X = di.Location.Cell.X, Y = di.Location.Cell.Y, Orientation = (int)di.Location.Orientation });
                byte[] bytes = BayDesignCodec.Encode(parts, doors);
                DesignStore.Write(part, bytes, "bay", new IntRect(minX, minY, maxX - minX + 1, maxY - minY + 1));
                using (ShipUpdateInfo info = ShipUpdateInfo.Alloc(ship))
                {
                    foreach (PartInfo pi in bay.BlueprintDesign) info.BlueprintPartsToRemove.Add(pi);
                    foreach (DoorInfo di in bay.BlueprintDoors) info.BlueprintDoorsToRemove.Add(di);
                    info.Apply();
                }
                Log.Info($"Bay terminal at {part.Location}: stored the design ({parts.Count} parts, {doors.Count} doors, {bytes.Length} bytes) and took its blueprints off this ship.");
                bay.BlueprintDesign.Clear();
                bay.BlueprintDoors.Clear();
            }
            catch (Exception ex)
            {
                Log.Exception("BayManager.Capture", ex);
            }
        }

        private static void LoadStored(Ship ship, ZtxBayTerminalComponent terminal)
        {
            BayState bay = terminal.Bay;
            Part part = terminal.Part;
            int gen = DesignStore.Gen(part);
            if (gen == bay.StoredGen) return;
            bay.StoredGen = gen;
            bay.Design.Clear();
            bay.Doors.Clear();
            bay.DesignCells.Clear();
            bay.StoredToggles.Clear();
            if (!DesignStore.TryRead(part, out byte[] bytes, out _, out _) || !BayDesignCodec.Decode(bytes, out List<BayDesignCodec.PartRec> parts, out List<BayDesignCodec.DoorRec> doors))
                return;
            int unknown = 0;
            foreach (BayDesignCodec.PartRec rec in parts)
            {
                if (!ship.Rules.PartsByID.TryGetValue(new ID<PartRules>(rec.Id), out PartRules rules)) { unknown++; continue; }
                var pi = new PartInfo(rules, new IntVector2(rec.X, rec.Y), rec.Rotation, rec.FlipX);
                bay.Design.Add(pi);
                bay.StoredToggles[pi] = rec.Toggles;
                foreach (IntVector2 c in pi.Rect) bay.DesignCells.Add(c);
            }
            foreach (BayDesignCodec.DoorRec rec in doors)
            {
                if (!ship.Rules.DoorsByID.TryGetValue(new ID<DoorRules>(rec.Id), out DoorRules rules)) { unknown++; continue; }
                bay.Doors.Add(new DoorInfo(rules, new DoorLocation(new IntVector2(rec.X, rec.Y), (DoorOrientation)rec.Orientation)));
            }
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (IntVector2 c in bay.DesignCells)
            {
                minX = Math.Min(minX, c.X); minY = Math.Min(minY, c.Y);
                maxX = Math.Max(maxX, c.X); maxY = Math.Max(maxY, c.Y);
            }
            if (bay.DesignCells.Count > 0) bay.DesignBounds = new IntRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
            Log.Info($"Bay terminal at {part.Location}: design read ({bay.Design.Count} parts, {bay.Doors.Count} doors" + (unknown > 0 ? $", {unknown} unknown entries skipped" : "") + ").");
        }

        private static void HandleClear(ZtxBayTerminalComponent terminal)
        {
            Part part = terminal.Part;
            if (BayValues.Get(part, ClearId, 0) == 0) return;
            BayValues.SetInSim(part, ClearId, 0);
            if (terminal.Bay.Held != null)
            {
                Log.Info($"Bay terminal at {part.Location}: Clear design refused -- a copy is held; release it first.");
                return;
            }
            DesignStore.Clear(part);
            Log.Info($"Bay terminal at {part.Location}: stored design cleared.");
        }

        private static void ApplyStoredToggles(BayState bay, Ship held)
        {
            foreach (var kv in bay.StoredToggles)
            {
                PartSaveData psd = SaveKey(kv.Key);
                foreach (BayDesignCodec.ToggleRec t in kv.Value)
                {
                    var id = new ID<PartToggleGuiRules>(t.Id);
                    held.Metadata.PartUIToggleStates.Set((psd, id), t.Value);
                    held.Metadata.PartUIToggleStates.Set((psd, id), t.Value, blueprintMode: true);
                }
            }
        }

        private static IEnumerable<BlueprintPart> BlueprintNeighbours(Ship ship, BlueprintPart bp)
        {
            var seen = new HashSet<BlueprintPart>();
            foreach (IntVector2 c in Around(new PartInfo(bp.Rules, bp.Location, bp.Rotation, bp.FlipX).Rect))
            {
                BlueprintPart other = ship.BlueprintParts[c, PartRectType.Normal];
                if (other == null || other == bp || !seen.Add(other) || !Unbuilt(ship, other)) continue;
                yield return other;
            }
        }

        private static PartInfo InfoOf(BlueprintPart bp) => new PartInfo(bp.Rules, bp.Location, bp.Rotation, bp.FlipX);

        private static bool IsCarrierKind(Ship ship, BlueprintPart bp) => IsCarrierKind(ship, InfoOf(bp));

        private static bool IsCarrierKind(Ship ship, PartInfo pi)
        {
            string id = pi.Rules.ID.ToString();
            bool mate = BayDesign.IsDockMateId(id);
            if (!mate && !BackgroundFrame.Is(pi.Rules)) return false;
            return !(mate && FacesCarrierDock(ship, pi, id));
        }

        private static bool FacesCarrierDock(Ship ship, PartInfo pi, string id)
        {
            IntVector2 f = OrthogonalRotation.Up.GetShipRelative(pi.Rotation, pi.FlipX).ToVector();
            Part q = ship.Parts[pi.Location + f, PartRectType.Normal];
            if (q == null || q.IsDestroyed || q.Rules.ID.ToString() != id) return false;
            IntVector2 g = DockGeometry.FacingDir(q);
            return BayDesign.FacesBack(f.X, f.Y, g.X, g.Y);
        }

        private static bool TouchesCarrier(Ship ship, BlueprintPart bp) => TouchesCarrier(ship, InfoOf(bp));

        private static bool TouchesCarrier(Ship ship, PartInfo pi)
        {
            foreach (IntVector2 c in Around(pi.Rect))
            {
                Part q = ship.Parts[c, PartRectType.Normal];
                if (q != null && !IsBayStructure(q) && !IsDock(q)) return true;
            }
            return false;
        }

        public static bool IsFreeBuild(Ship ship) =>
            !BayDesign.StationsBuild((int)ship.Sim.Mode.GetConstructionMode(ship));

        private static readonly List<ZtxBayTerminalComponent> s_noTerminals = new List<ZtxBayTerminalComponent>();

        internal static List<ZtxBayTerminalComponent> TerminalsOf(Ship ship) => TryGet(ship)?._terminals ?? s_noTerminals;

        public static void CaptureAfterBuild(Ship ship)
        {
            BayManager mgr = TryGet(ship);
            if (mgr == null) return;
            foreach (ZtxBayTerminalComponent terminal in mgr._terminals.ToArray())
            {
                if (terminal.Part?.Ship != ship || !terminal.Bay.InCharge) continue;
                BayState bay = terminal.Bay;
                ScanDesign(ship, bay);
                if (bay.BlueprintDesign.Count > 0) DockSection.TakeFromQueue(ship, bay.BlueprintDesign, bay.BlueprintDoors);
                if (!BayDesign.ShouldCapture(bay.Pieces, bay.BlueprintDesign.Count, bay.Held != null)) continue;
                int before = bay.Design.Count;
                Capture(ship, terminal);
                LoadStored(ship, terminal);
                TellCapture(ship, terminal, before, bay.Design.Count);
            }
        }

        private static void TellCapture(Ship ship, ZtxBayTerminalComponent terminal, int before, int now)
        {
            if (terminal.Part == null) return;
            int kind = BayDesign.CaptureKind(before, now);
            string text = kind == BayDesign.CAPTURE_WARN ? Strings.FormatText("ZtxBayPanel/CaptureReplaced", now, before)
                : kind == BayDesign.CAPTURE_REPLACED ? Strings.FormatText("ZtxBayPanel/CaptureReplacedOk", now, before)
                : Strings.FormatText(IsOn(terminal.Part, BuildId) ? "ZtxBayPanel/CaptureStoredOn" : "ZtxBayPanel/CaptureStoredOff", now);
            BayText.Show(ship, "Capture", text, Halfling.Graphics.Color.White);
        }

        public static HashSet<PartInfo> InstantDesignParts(Ship ship, ICollection<PartInfo> adds) => InstantDesignParts(ship, adds, out _);

        public static ZtxBayTerminalComponent EditRefusal(ShipUpdateInfo sui)
        {
            Ship ship = sui != null && sui.HasShip ? sui.Ship : null;
            BayManager mgr = TryGet(ship);
            if (mgr == null || (sui.PartsToAdd.Count == 0 && sui.BlueprintPartsToAdd.Count == 0)) return null;
            var locked = new HashSet<ZtxBayTerminalComponent>();
            foreach (ZtxBayTerminalComponent t in mgr._terminals)
                if (t.Part?.Ship == ship && t.Bay.InCharge && BayDesign.EditsLocked(IsOn(t.Part, BuildId))) locked.Add(t);
            if (locked.Count == 0) return null;
            var adds = new List<PartInfo>(sui.PartsToAdd);
            foreach (PartInfo pi in sui.BlueprintPartsToAdd) if (!adds.Contains(pi)) adds.Add(pi);
            foreach (ZtxBayTerminalComponent t in locked)
                if (InstantDesignParts(ship, adds, out _, t).Count > 0) return t;
            return null;
        }

        public static HashSet<PartInfo> InstantDesignParts(Ship ship, ICollection<PartInfo> adds, out string why, ZtxBayTerminalComponent only = null)
        {
            var result = new HashSet<PartInfo>();
            int touching = 0, carrierWork = 0, onHull = 0;
            why = "no bay";
            BayManager mgr = TryGet(ship);
            if (mgr == null || adds == null || adds.Count == 0) return result;
            var at = new Dictionary<IntVector2, PartInfo>();
            foreach (PartInfo pi in adds)
                foreach (IntVector2 c in pi.Rect) at[c] = pi;
            Func<PartInfo, IEnumerable<PartInfo>> neighbours = pi =>
            {
                var list = new List<PartInfo>();
                foreach (IntVector2 c in Around(pi.Rect))
                    if (at.TryGetValue(c, out PartInfo o) && !o.Equals(pi) && !list.Contains(o)) list.Add(o);
                return list;
            };
            foreach (ZtxBayTerminalComponent t in mgr._terminals)
            {
                if (only != null && !ReferenceEquals(t, only)) continue;
                HashSet<IntVector2> frame = t.Bay.StructureCells;
                if (frame == null || frame.Count == 0) continue;
                var adjacent = new List<PartInfo>();
                foreach (PartInfo pi in adds)
                    if (!result.Contains(pi) && Around(pi.Rect).Any(frame.Contains)) adjacent.Add(pi);
                HashSet<PartInfo> work = BayDesign.CarrierWork(adjacent, neighbours, pi => IsCarrierKind(ship, pi));
                var seeds = adjacent.Where(pi => !work.Contains(pi)).ToList();
                touching += adjacent.Count;
                carrierWork += work.Count;
                onHull += seeds.Count(pi => TouchesCarrier(ship, pi));
                foreach (List<PartInfo> section in BayDesign.Sections(seeds, pi => neighbours(pi).Where(n => !work.Contains(n)), pi => TouchesCarrier(ship, pi)))
                    foreach (PartInfo pi in section) result.Add(pi);
            }
            why = $"{adds.Count} added, {touching} touch a bay's frames, {carrierWork} carrier work, {onHull} touch the carrier hull, {result.Count} taken";
            return result;
        }

        private static void UpdateFacts(Ship ship, BayState bay)
        {
            Ship held = bay.Held;
            bay.Done = 0;
            bay.OutOfReach = 0;
            bool allBuilt = held != null && bay.Design.Count > 0;
            foreach (PartInfo pi in bay.Design)
            {
                Part p = held?.Parts.GetExactPart(pi);
                bool done = p != null && !p.IsUnderConstruction;
                if (done) { bay.Done++; continue; }
                allBuilt = false;
                if (!InReachOfAny(ship, bay, pi)) bay.OutOfReach++;
            }
            bay.DoorsDone = 0;
            if (held != null)
                foreach (DoorInfo di in bay.Doors)
                {
                    Door d = held.Doors.GetExactDoor(di);
                    if (d != null && !d.IsUnderConstruction) bay.DoorsDone++;
                }
            if (bay.DoorsDone < bay.Doors.Count) allBuilt = false;
            bay.Complete = allBuilt;
            bay.Built = allBuilt;
            bool repaired = bay.Built;
            if (repaired)
                foreach (Part p in held.Parts)
                    if (p.Health < p.Rules.MaxHealth) { repaired = false; break; }
            bay.Repaired = repaired;
        }

        private static bool InReachOfAny(Ship ship, BayState bay, PartInfo pi)
        {
            foreach (ZtxBayStationComponent s in bay.Stations)
                if (s.Part != null && DistanceCells(s.Part, pi) <= s.Rules.ReachCells) return true;
            return false;
        }

        private static float DistanceCells(Part station, PartInfo pi)
        {
            Vector2 a = DockGeometry.CellCenter(station.Location);
            Vector2 b = pi.LocalCenter;
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool[] Ticked(Part terminal)
        {
            var t = new bool[BayRelease.CONDITIONS];
            for (int i = 0; i < t.Length; i++) t[i] = IsOn(terminal, WhenIds[i]);
            return t;
        }

        private static bool[] Facts(BayState bay) => BayRelease.Facts(bay.Built, bay.Crewed, bay.Supplied, bay.Repaired, bay.Charged);

        private static bool StartsFromBay(BayState bay, PartInfo pi) => BayDesign.TouchesAny(pi.Rect, bay.StructureCells);

        private static void SetWaiting(ZtxBayTerminalComponent terminal, string why)
        {
            BayState bay = terminal.Bay;
            bay.Waiting = why;
            if (bay.LastLogged == why) return;
            bay.LastLogged = why;
            Log.Info($"Bay terminal at {terminal.Part?.Location}: {why}.");
        }

        #endregion

        #region Held ship

        private static void CheckHeld(Ship ship, ZtxBayTerminalComponent terminal)
        {
            BayState bay = terminal.Bay;
            if (bay.Held != null)
            {
                if (bay.Held.Sim == null || bay.Held.Parts.Count == 0)
                {
                    Log.Info($"Bay terminal at {terminal.Part.Location}: the held ship is gone.");
                    HingeJointManager.ReleaseByTagDeliberately(terminal);
                    bay.Held = null;
                }
                else if (!HingeJointManager.HasPinWithTag(terminal) && !Weld(ship, terminal, bay.Held))
                {
                    Log.Info($"Bay terminal at {terminal.Part.Location}: lost hold of ship {bay.Held.UniqueID}.");
                    bay.Held = null;
                }
                return;
            }
            if (bay.Design.Count == 0) return;
            Ship found = FindHeldAgain(ship, bay);
            if (found == null) return;
            bay.Held = found;
            if (Weld(ship, terminal, found))
                Log.Info($"Bay terminal at {terminal.Part.Location}: holding ship {found.UniqueID} again.");
            else
                bay.Held = null;
        }

        private static Ship FindHeldAgain(Ship ship, BayState bay)
        {
            var body = ship.Physics.Body;
            Rect rr = (Rect)bay.DesignBounds;
            RotRect bounds = RotRect.CenteredOn(body.GetWorldPoint(rr.Center * ship.Scale), rr.Size * ship.Scale, new Direction(body.Rotation));
            Ship found = null;
            ship.Sim.Ships.DetHasIntersectingWith(bounds, approximate: true, junk: false, condition: s =>
            {
                if (found != null || ReferenceEquals(s, ship) || ReferenceEquals(s, bay.LastReleased) || s.Physics?.Body == null) return false;
                if (s.Metadata.PlayerIndex != ship.Metadata.PlayerIndex || IsHeldByAnyBay(s)) return false;
                if (!OnCarrierGrid(ship, s)) return false;
                foreach (PartInfo pi in bay.Design)
                    if (s.Parts.GetExactPart(pi) != null) { found = s; break; }
                return false;
            });
            return found;
        }

        internal static bool IsHeldByAnyBay(Ship s)
        {
            foreach (Ship carrier in s.Sim.Ships)
            {
                BayManager mgr = TryGet(carrier);
                if (mgr == null) continue;
                foreach (ZtxBayTerminalComponent t in mgr._terminals)
                    if (ReferenceEquals(t.Bay.Held, s)) return true;
            }
            return false;
        }

        private static bool OnCarrierGrid(Ship carrier, Ship other)
        {
            var a = carrier.Physics.Body;
            var b = other.Physics.Body;
            Vector2 oa = a.GetWorldPoint(Vector2.Zero), ob = b.GetWorldPoint(Vector2.Zero);
            float dx = ob.X - oa.X, dy = ob.Y - oa.Y;
            double ang = -a.Rotation;
            float lx = (float)(dx * Math.Cos(ang) - dy * Math.Sin(ang));
            float ly = (float)(dx * Math.Sin(ang) + dy * Math.Cos(ang));
            float scale = carrier.Scale.X > 0f ? carrier.Scale.X : 1f;
            return BayGrid.OnGrid(lx / scale, ly / scale, b.Rotation - a.Rotation, GRID_TOLERANCE_CELLS, GRID_TOLERANCE_RAD);
        }

        private static bool Weld(Ship ship, ZtxBayTerminalComponent terminal, Ship held)
        {
            if (held?.Physics?.Body == null || ship.Physics?.Body == null) return false;
            Vector2 anchor = ship.Physics.Body.GetWorldPoint(DockGeometry.CellCenter(terminal.Part.Location) * ship.Scale);
            HingeJointManager.CreateTaggedWeld(terminal, ship, held, anchor, collideConnected: false);
            if (!HingeJointManager.HasPinWithTag(terminal)) return false;
            HingeJointManager.SetDockSeparate(terminal, true, held);
            return true;
        }

        private static string NameOf(Part terminal)
        {
            string name = RailCartRules.UnpackName(BayValues.Get(terminal, Name1Id, 0), BayValues.Get(terminal, Name2Id, 0));
            return string.IsNullOrEmpty(name) ? DEFAULT_NAME : name;
        }

        private static Ship CreateHeld(Ship ship, ZtxBayTerminalComponent terminal)
        {
            Ship held = Ship.CloneEmptyFrom(ship, ship.Rand.UInt64());
            ship.Sim.Ships.Add(held, giveDefaultCommand: false, reseedShipRand: false);
            held.Metadata.CopyPartMetadataFrom(ship.Metadata);
            held.Roles.CopyRolesFrom(ship.Roles);
            ApplyStoredToggles(terminal.Bay, held);
            string name = NameOf(terminal.Part);
            try { if (ShipMetadata.IsValidShipName(name)) held.Metadata.PlainShipName = name; }
            catch (Exception ex) { Log.Info($"Bay terminal: name \"{name}\" not taken ({ex.GetType().Name})."); }
            return held;
        }

        private static void MatchVelocity(Ship ship, Ship held)
        {
            var body = ship.Physics.Body;
            var pw = held.Physics?.PhysicsWrapper;
            if (pw == null || held.Physics.Body == null) return;
            pw.LinearVelocity = body.GetLinearVelocityFromWorldPoint(held.Physics.Body.WorldCenter);
            pw.AngularVelocity = body.AngularVelocity;
        }

        private static bool SpawnFirst(Ship ship, ZtxBayTerminalComponent terminal, PartInfo pi, bool instant, ResourceStock stock, out Part spawned, out string why)
        {
            spawned = null;
            why = "";
            if (ShipUpdateInfo.TestBlockage(ship, pi.Rect, ignoreJunk: false)) { why = $"something is in the way at {pi.Location}"; return false; }
            if (!instant && stock != null && !DockPipeline.CanAffordNew(pi, stock, out why)) return false;
            Ship held = CreateHeld(ship, terminal);
            bool ok;
            if (instant)
            {
                spawned = held.Parts.CreatePart(pi.Rules, pi.Location, pi.Rotation, pi.FlipX, spawnedFromDestroyed: false, builtByPlayer: true,
                    defaultToMinHealth: false, underConstruction: false);
                ok = spawned != null;
            }
            else ok = DockPipeline.TrySpawn(held, pi, free: false, out spawned, out why, store: stock);
            if (!ok)
            {
                ship.Sim.Ships.Remove(held, dispose: true, handleLostShip: false);
                return false;
            }
            MatchVelocity(ship, held);
            terminal.Bay.Held = held;
            SetFlightDirection(terminal, held);
            if (Weld(ship, terminal, held))
                Log.Info($"Bay terminal at {terminal.Part.Location}: started ship {held.UniqueID} \"{held.Metadata.PlainShipName}\" with {pi.Rules.ID} at {pi.Location}.");
            else
                Log.Info($"Bay terminal at {terminal.Part.Location}: started ship {held.UniqueID}; the weld waits for its body.");
            return true;
        }

        private static void BuildInstant(Ship ship, ZtxBayTerminalComponent terminal)
        {
            BayState bay = terminal.Bay;
            if (bay.Held == null)
            {
                int first = bay.Design.FindIndex(pi => StartsFromBay(bay, pi));
                if (first < 0) { SetWaiting(terminal, "no design part touches the bay's frames"); return; }
                if (!SpawnFirst(ship, terminal, bay.Design[first], instant: true, null, out _, out string why))
                {
                    SetWaiting(terminal, why);
                    return;
                }
            }
            Ship held = bay.Held;
            bool progress = true;
            int made = 0;
            while (progress)
            {
                progress = false;
                foreach (PartInfo pi in bay.Design)
                {
                    if (held.Parts.GetExactPart(pi) != null) continue;
                    if (!DockPipeline.RectFree(held, pi)) continue;
                    if (!held.Parts.IsNewPartContiguous(pi)) continue;
                    if (ShipUpdateInfo.TestBlockage(held, pi.Rect, ignoreJunk: false)) continue;
                    ApplyTogglesFor(bay, held, pi);
                    Part p = held.Parts.CreatePart(pi.Rules, pi.Location, pi.Rotation, pi.FlipX, spawnedFromDestroyed: false, builtByPlayer: true,
                        defaultToMinHealth: false, underConstruction: false);
                    if (p != null) { progress = true; made++; }
                }
            }
            foreach (DoorInfo di in bay.Doors)
            {
                if (held.Doors.GetExactDoor(di) != null || held.Doors[di.Location] != null || !DockPipeline.DoorReady(held, di)) continue;
                try { held.Doors.CreateDoor(di.Rules, di.Location, builtByPlayer: true, underConstruction: false); }
                catch (Exception ex) { Log.Info($"Bay: door at {di.Location.Cell} not placed ({ex.GetType().Name})."); }
            }
            AddBlueprints(held, bay);
            UpdateFacts(ship, bay);
            Log.Info($"Bay terminal at {terminal.Part.Location}: built {made + 1} parts of ship {held.UniqueID} at once ({bay.Done}/{bay.Design.Count}).");
        }

        private static ShipFlightDirection SetFlightDirection(ZtxBayTerminalComponent terminal, Ship held)
        {
            var dir = (ShipFlightDirection)BayRelease.FlightDirectionOf(BayValues.Get(terminal.Part, FacingId, 0));
            if (held?.Metadata != null && held.Metadata.FlightDirection.Confirmed != dir)
                held.Metadata.FlightDirection = new Cosmoteer.Game.Multiplayer.MPValue<ShipFlightDirection>(dir);
            return dir;
        }

        private static void ApplyTogglesFor(BayState bay, Ship target, PartInfo pi)
        {
            if (!bay.StoredToggles.TryGetValue(pi, out List<BayDesignCodec.ToggleRec> toggles)) return;
            PartSaveData psd = SaveKey(pi);
            foreach (BayDesignCodec.ToggleRec t in toggles)
            {
                var id = new ID<PartToggleGuiRules>(t.Id);
                target.Metadata.PartUIToggleStates.Set((psd, id), t.Value);
                target.Metadata.PartUIToggleStates.Set((psd, id), t.Value, blueprintMode: true);
            }
        }

        private static void AddBlueprints(Ship held, BayState bay)
        {
            try
            {
                var parts = new HashSet<PartInfo>(bay.Design);
                var doors = new HashSet<DoorInfo>(bay.Doors);
                using (ShipUpdateInfo info = ShipUpdateInfo.Alloc(held))
                {
                    info.MatchBlueprintsToPhysical(matchDestroyed: false);
                    info.BlueprintPartsToRemove.Clear();
                    info.BlueprintDoorsToRemove.Clear();
                    info.BlueprintPartsToAdd.RemoveWhere(pi => !parts.Contains(pi) || held.Parts.GetExactPart(pi) == null || held.Parts.GetExactPart(pi).IsUnderConstruction);
                    info.BlueprintDoorsToAdd.RemoveWhere(di => !doors.Contains(di));
                    if (info.BlueprintPartsToAdd.Count + info.BlueprintDoorsToAdd.Count > 0) info.Apply();
                }
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("BayManager.AddBlueprints", ex);
            }
        }

        private static void KeepDesignDoors(Ship held, BayState bay)
        {
            if (bay.Doors.Count == 0) return;
            try
            {
                var doors = new HashSet<DoorInfo>(bay.Doors);
                var added = new List<DoorInfo>();
                using (ShipUpdateInfo info = ShipUpdateInfo.Alloc(held))
                {
                    info.MatchBlueprintsToPhysical(matchDestroyed: false);
                    info.BlueprintPartsToAdd.Clear();
                    info.BlueprintPartsToRemove.Clear();
                    info.BlueprintDoorsToRemove.Clear();
                    info.BlueprintDoorsToAdd.RemoveWhere(di => !doors.Contains(di));
                    added.AddRange(info.BlueprintDoorsToAdd);
                    if (added.Count > 0) info.Apply();
                }
                Cosmoteer.Ships.Construction.ConstructionManager cm = held.OptionalConstructionManager;
                int cancelled = 0;
                foreach (DoorInfo di in bay.Doors)
                {
                    Door d = held.Doors.GetExactDoor(di);
                    if (d != null && cm != null && cm.IsDeconstructionQueued(d) && cm.TryRemoveDoorDeconstruct(d)) cancelled++;
                }
                if (added.Count == 0 && cancelled == 0) return;
                int left = cm?.QueuedDoorDeconstructions.Count ?? 0;
                Log.Info($"Bay: {added.Count} doors of docked ship {held.UniqueID} had no blueprint and were marked for removal; blueprinted them from the bay's design and cancelled {cancelled} removals ({left} door removals still queued).");
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("BayManager.KeepDesignDoors", ex);
            }
        }

        private static Ship BlockingShip(Ship ship, BayState bay)
        {
            var body = ship.Physics.Body;
            Rect rr = (Rect)bay.DesignBounds;
            RotRect bounds = RotRect.CenteredOn(body.GetWorldPoint(rr.Center * ship.Scale), rr.Size * ship.Scale, new Direction(body.Rotation));
            var cellSize = new Vector2(0.6f, 0.6f) * ship.Scale;
            Ship found = null;
            ship.Sim.Ships.DetHasIntersectingWith(bounds, approximate: true, junk: null, condition: other =>
            {
                if (found != null || ReferenceEquals(other, ship)) return false;
                foreach (IntVector2 c in bay.DesignCells)
                {
                    RotRect cellRect = RotRect.CenteredOn(body.GetWorldPoint(new Vector2(c.X + 0.5f, c.Y + 0.5f) * ship.Scale), cellSize, new Direction(body.Rotation));
                    if (other.Parts.DetIntersectsWithWorldShape(cellRect, p => !BackgroundFrame.Is(p.Rules))) { found = other; break; }
                }
                return false;
            });
            return found;
        }

        private void Release(Ship ship, ZtxBayTerminalComponent terminal)
        {
            BayState bay = terminal.Bay;
            Ship held = bay.Held;
            Part part = terminal.Part;
            List<CrewSoul> stranded = DockCrew.HomedButOn(held, ship);
            if (stranded.Count > 0)
            {
                int back = DockCrew.Move(stranded, held, ship, part.WorldPhysicalCenter, out int stuck);
                Log.Info($"Bay terminal at {part.Location}: {back} crew of ship {held.UniqueID} were still on this ship and belong to it again" + (stuck > 0 ? $"; {stuck} found no free quarters." : "."));
            }
            AddBlueprints(held, bay);
            HingeJointManager.ReleaseByTagDeliberately(terminal);
            DockThrust.Clear(terminal);
            foreach (ZtxBayStationComponent s in bay.Stations) StopStation(s);
            bay.Held = null;
            bay.LastReleased = held;
            bay.Built = false;
            int launch = BayValues.Get(part, LaunchId, LAUNCH_DRIFT);
            string how = "drifts";
            ShipFlightDirection dir = ShipFlightDirection.Up;
            try
            {
                dir = SetFlightDirection(terminal, held);
                int flyOut = BayValues.FlyOutCells(part, terminal.Rules);
                if (launch == LAUNCH_FLY && DockRelease.FlyClear(ship, part, held, bay.DesignBounds, flyOut, out float cells))
                    how = $"flies {cells:0} cells out along the arrow ({flyOut} past the bay)";
                else if (launch == LAUNCH_PUSH && held.Physics?.PhysicsWrapper != null)
                {
                    var body = ship.Physics.Body;
                    IntVector2 f = DockGeometry.FacingDir(part);
                    Vector2 step = body.GetWorldPoint(new Vector2(f.X, f.Y) * ship.Scale) - body.GetWorldPoint(Vector2.Zero);
                    float push = BayValues.PushTenths(part, terminal.Rules) / 10f;
                    held.Physics.PhysicsWrapper.LinearVelocity += step * push;
                    how = $"is pushed out at {push:0.0} cells/s along the arrow";
                }
            }
            catch (Exception ex)
            {
                Log.Exception("BayManager.Release(launch)", ex);
            }
            bool repeat = IsOn(part, RepeatId);
            if (!repeat)
            {
                PartUIToggle t = PartUIToggle.GetOnPart(part, BuildId);
                if (t != null && t.Mode != 0)
                {
                    t.Mode = 0;
                    part.MatchingBlueprintPart?.OnPartUIToggleStatesChanged();
                }
            }
            Log.Info($"Bay terminal at {part.Location}: released ship {held.UniqueID} \"{held.Metadata.PlainShipName}\" with flight direction {dir}; it {how}." +
                     (repeat ? " Repeat is on: the next copy starts once the design's cells are clear." : " Repeat is off: Build switched off."));
            bay.LastLogged = null;
        }

        #endregion

        #region Stations (every tick)

        private static void StopStation(ZtxBayStationComponent s)
        {
            DockBeam.EndBeam(s.Beam);
            s.Target = null;
            s.DeliveryCarry = 0f;
            if (s.Repair != null)
            {
                DockBeam.EndBeam(s.Repair.Beam);
                s.Repair = null;
            }
        }

        private void WorkBay(Ship ship, ZtxBayTerminalComponent terminal, Time dt)
        {
            BayState bay = terminal.Bay;
            Part tPart = terminal.Part;
            if (tPart == null || tPart.Ship != ship) return;
            bool crew = !IsFreeBuild(ship);
            bool active = crew && IsOn(tPart, BuildId) && BayDesign.CanBuild(bay.Pieces, bay.Design.Count) && !bay.Built;
            if (bay.Held == null && active && BlockingShip(ship, bay) != null) active = false;
            int supplySet = bay.Held == null || !bay.Built ? 0
                : (BayValues.Get(tPart, ResupplyId, 0) != 0 ? DockSupply.MATERIALS : 0) | (IsOn(tPart, WhenIds[BayRelease.CHARGED]) ? DockSupply.ENERGY : 0);
            bool supplyHeld = supplySet != 0;
            var taken = new HashSet<PartInfo>();
            foreach (ZtxBayStationComponent s in bay.Stations)
                if (s.Target.HasValue) taken.Add(s.Target.Value);
            foreach (ZtxBayStationComponent s in bay.Stations)
            {
                if (s.Part == null || s.Part.Ship != ship) continue;
                if (!active || !IsOn(s.Part, StationOnId))
                {
                    if (s.Target != null) { DockBeam.EndBeam(s.Beam); s.Target = null; }
                    if (!IsOn(s.Part, StationOnId)) { s.Status = "switched off"; s.SupplyShip = null; continue; }
                    if (supplyHeld) { s.SupplyShip = bay.Held; s.SupplySet = supplySet; }
                    else if (bay.Held != null && ReferenceEquals(s.SupplyShip, bay.Held)) s.SupplyShip = null;
                    if (s.Repair == null && s.SupplyShip == null) s.Status = "idle";
                    continue;
                }
                s.SupplyShip = null;
                try
                {
                    WorkStation(ship, terminal, s, dt, taken);
                }
                catch (Exception ex)
                {
                    LogThrottle.Exception("BayManager.WorkStation", ex);
                    DockBeam.EndBeam(s.Beam);
                    s.Target = null;
                }
            }
        }

        private static void WorkStation(Ship ship, ZtxBayTerminalComponent terminal, ZtxBayStationComponent s, Time dt, HashSet<PartInfo> taken)
        {
            BayState bay = terminal.Bay;
            ZtxBayStationRules rules = s.Rules;
            if (rules.MaxBuildSpeed > 0f && ship.Physics.Body.LinearVelocity.Length() > rules.MaxBuildSpeed)
            {
                DockBeam.EndBeam(s.Beam);
                s.DeliveryCarry = 0f;
                s.Status = "paused -- the carrier is too fast";
                return;
            }
            if (s.Repair != null) { DockBeam.EndBeam(s.Repair.Beam); s.Repair = null; }

            Ship held = bay.Held;
            if (held != null && !HingeJointManager.HasPinWithTag(terminal) && !Weld(ship, terminal, held)) return;

            Part target = null;
            if (s.Target.HasValue && held != null)
            {
                Part p = held.Parts.GetExactPart(s.Target.Value);
                if (p != null && p.IsUnderConstruction) target = p;
                else { taken.Remove(s.Target.Value); s.Target = null; }
            }
            if (target == null && --s.ScanCooldown <= 0)
            {
                s.ScanCooldown = SCAN_INTERVAL_TICKS;
                int n = bay.Design.Count;
                var states = new int[n];
                var dist = new float[n];
                var busy = new bool[n];
                string firstWhy = null;
                for (int i = 0; i < n; i++)
                {
                    PartInfo pi = bay.Design[i];
                    dist[i] = DistanceCells(s.Part, pi);
                    busy[i] = taken.Contains(pi);
                    if (held == null)
                    {
                        bool start = StartsFromBay(bay, pi) && !ShipUpdateInfo.TestBlockage(ship, pi.Rect, ignoreJunk: false);
                        states[i] = start ? (int)PipelineState.Spawnable : (int)PipelineState.Waiting;
                        if (firstWhy == null && !start && dist[i] <= rules.ReachCells) firstWhy = "a copy starts from a part touching the bay's frames";
                        continue;
                    }
                    states[i] = (int)DockPipeline.StateOf(held, pi, out _, out string why);
                    if (firstWhy == null && states[i] == (int)PipelineState.Waiting && dist[i] <= rules.ReachCells) firstWhy = why;
                }
                int pick = BayStation.PickOrHelp(states, dist, busy, rules.ReachCells);
                if (pick < 0)
                {
                    DockBeam.EndBeam(s.Beam);
                    s.Status = firstWhy != null ? "waiting -- " + firstWhy : "nothing in reach to build";
                    WorkDoors(ship, bay, s, dt);
                    return;
                }
                PartInfo chosen = bay.Design[pick];
                if (states[pick] == (int)PipelineState.Building) target = held.Parts.GetExactPart(chosen);
                else if (held == null)
                {
                    if (!SpawnFirst(ship, terminal, chosen, instant: false, StockOf(ship, s), out target, out string why)) { s.Status = "waiting -- " + why; return; }
                    held = bay.Held;
                }
                else
                {
                    ApplyTogglesFor(bay, held, chosen);
                    if (!DockPipeline.TrySpawn(held, chosen, free: false, out target, out string why, store: StockOf(ship, s)))
                    {
                        s.Status = "waiting -- " + why;
                        return;
                    }
                }
                s.Target = chosen;
                taken.Add(chosen);
            }
            if (target == null) return;

            s.DeliveryCarry = Math.Min(s.DeliveryCarry + rules.DeliveryPerSecond * (float)dt, Math.Max(1f, rules.DeliveryPerSecond));
            int budget = (int)s.DeliveryCarry, before = budget;
            bool starved = false;
            float mark = DockPipeline.WorkMark(target);
            if (budget > 0) DockPipeline.Feed(StockOf(ship, s), target, free: false, ref budget, ref starved);
            if (rules.WorkPerSecond > 0f) DockPipeline.Work(ship, target, dt, rules.WorkPerSecond, s.Part.WorldPhysicalCenter);
            s.DeliveryCarry -= before - budget;
            Part still = held?.Parts.GetExactPart(s.Target.Value);
            bool building = still != null && still.IsUnderConstruction;
            bool beam = DockPipeline.BeamOn(s.Beam, !building || before != budget || DockPipeline.WorkMark(still) > mark);
            DockBeam.UpdateBeam(s.Beam, s.Part, building && beam ? still : null, rules.BeamMediaEffects, rules.BeamHitMediaEffects);
            s.Status = starved ? $"building {s.Target.Value.Rules.ID} -- waiting for materials in this station's reach" : $"building {s.Target.Value.Rules.ID} at {s.Target.Value.Location}";
            if (still == null || !still.IsUnderConstruction) { taken.Remove(s.Target.Value); s.Target = null; }
        }

        private static void WorkDoors(Ship ship, BayState bay, ZtxBayStationComponent s, Time dt)
        {
            Ship held = bay.Held;
            if (held == null) return;
            foreach (PartInfo pi in bay.Design)
            {
                Part p = held.Parts.GetExactPart(pi);
                if (p == null || p.IsUnderConstruction) return;
            }
            s.DeliveryCarry = Math.Min(s.DeliveryCarry + s.Rules.DeliveryPerSecond * (float)dt, Math.Max(1f, s.Rules.DeliveryPerSecond));
            int budget = (int)s.DeliveryCarry, before = budget;
            bool starved = false;
            foreach (DoorInfo di in bay.Doors)
            {
                if (budget <= 0) break;
                Door d = held.Doors.GetExactDoor(di);
                if (d != null && !d.IsUnderConstruction) continue;
                if (d == null)
                {
                    if (held.Doors[di.Location] != null || !DockPipeline.DoorReady(held, di)) continue;
                    try { d = held.Doors.CreateDoor(di.Rules, di.Location, builtByPlayer: true, underConstruction: true); }
                    catch (Exception ex) { LogThrottle.Exception("BayManager.WorkDoors", ex); continue; }
                    if (d == null) continue;
                }
                if (d.IsUnderConstruction) DockPipeline.FeedDoor(StockOf(ship, s), d, free: false, ref budget, ref starved);
            }
            s.DeliveryCarry -= before - budget;
            if (before != budget) s.Status = "building doors";
        }

        private static void ServiceStationRepair(Ship ship, ZtxBayStationComponent s)
        {
            Part part = s.Part;
            if (part == null || part.Ship != ship) return;
            if (!IsOn(part, StationOnId) || s.Target != null) { if (s.Repair != null) { DockBeam.EndBeam(s.Repair.Beam); s.Repair = null; } return; }
            DockRepairState r = s.Repair;
            if (r != null && !DockShipManager.HeldShips(ship).Contains(r.Ship)) { DockBeam.EndBeam(r.Beam); s.Repair = null; r = null; }
            if (r != null) return;
            if (!DockRepair.FindTarget(ship, part, s.Rules.ReachCells, out DockRepairState t, out string rebuildWhy))
            {
                if (rebuildWhy != null) LogThrottle.Info("BayRebuild|" + part.Location + "|" + rebuildWhy, $"Bay station at {part.Location}: cannot rebuild {rebuildWhy}.");
                if (s.SupplyShip == null) PickPadSupply(ship, s);
                return;
            }
            s.SupplyShip = null;
            s.SupplySink = null;
            s.Repair = t;
            s.Status = (t.IsRebuild || t.IsDoor ? "rebuilding " : "repairing ") + t.Describe() + $" on ship {t.Ship.UniqueID}";
            Log.Info($"Bay station at {part.Location}: {s.Status}.");
        }

        private void DevoteHeldShips(Ship ship)
        {
            if (_stations.Count == 0) return;
            foreach (Ship held in DockShipManager.HeldShips(ship))
            {
                if (held?.Physics?.Body == null || !InAnyStationReach(held)) continue;
                int taken = BayDevotion.TakeAll(held, InStationReach);
                if (held.OptionalConstructionManager?.QueuedDoorDeconstructions.Count > 0)
                    foreach (ZtxBayTerminalComponent t in _terminals) KeepDesignDoors(held, t.Bay);
                if (taken > 0)
                    LogThrottle.Info("BayDevote|" + held.UniqueID, $"Bay: took {taken} queued builds and repairs of docked ship {held.UniqueID} away from its crew -- the stations do them.");
            }
        }

        private bool InAnyStationReach(Ship held)
        {
            foreach (Part p in held.Parts)
                if (InStationReach(p.WorldPhysicalCenter)) return true;
            return false;
        }

        private bool InStationReach(Vector2 world)
        {
            float scale = base.Ship.Scale.X > 0f ? base.Ship.Scale.X : 1f;
            foreach (ZtxBayStationComponent s in _stations)
            {
                Part sp = s.Part;
                if (sp == null || sp.Ship != base.Ship || !IsOn(sp, StationOnId)) continue;
                if ((world - sp.WorldPhysicalCenter).Length <= s.Rules.ReachCells * scale) return true;
            }
            return false;
        }

        private static void PickPadSupply(Ship ship, ZtxBayStationComponent s)
        {
            float scale = ship.Scale.X > 0f ? ship.Scale.X : 1f;
            var docked = new List<Ship>(DockShipManager.PadShips(ship));
            foreach (Ship other in DockShipManager.ClampSupplyShips(ship))
                if (!docked.Contains(other)) docked.Add(other);
            foreach (Ship other in docked)
            {
                if (DockSupply.NextSink(other, StockOf(ship, s), s.Part.WorldPhysicalCenter, s.Rules.ReachCells, scale, out _) == null) continue;
                s.SupplyShip = other;
                s.SupplySet = DockSupply.ALL;
                s.ScanCooldown = 0;
                return;
            }
        }

        private static bool StillSupplied(Ship ship, Ship target) =>
            HeldShips(ship, finishedOnly: false).Contains(target) || DockShipManager.PadShips(ship).Contains(target)
            || DockShipManager.ClampSupplyShips(ship).Contains(target);

        private const int STOCK_REFRESH_TICKS = 30;

        private static ResourceStock StockOf(Ship ship, ZtxBayStationComponent s)
        {
            int now = ship.Sim?.Tick ?? 0;
            if (s.Stock == null || !ReferenceEquals(s.Stock.Ship, ship) || now - s.StockTick >= STOCK_REFRESH_TICKS || now < s.StockTick)
            {
                s.Stock = ResourceStock.InReach(ship, s.Part.WorldPhysicalCenter, s.Rules.ReachCells);
                s.StockTick = now;
            }
            return s.Stock;
        }

        public static bool AnyStationReaches(Ship carrier, Ship other) => TryGet(carrier)?.InAnyStationReach(other) ?? false;

        private static void WorkStationSupply(Ship ship, ZtxBayStationComponent s, Time dt)
        {
            Part part = s.Part;
            Ship target = s.SupplyShip;
            if (part == null || part.Ship != ship || !IsOn(part, StationOnId) || target?.Sim == null)
            {
                DockBeam.EndBeam(s.Beam);
                s.SupplyShip = null;
                s.SupplySink = null;
                return;
            }
            try
            {
                float scale = ship.Scale.X > 0f ? ship.Scale.X : 1f;
                if (--s.ScanCooldown <= 0 || s.SupplySink?.Part == null || !ReferenceEquals(s.SupplySink.Part.Ship, target))
                {
                    s.ScanCooldown = SCAN_INTERVAL_TICKS;
                    string missing = null;
                    s.SupplySink = StillSupplied(ship, target) ? DockSupply.NextSink(target, StockOf(ship, s), part.WorldPhysicalCenter, s.Rules.ReachCells, scale, out missing, s.SupplySet) : null;
                    if (s.SupplySink == null)
                    {
                        DockBeam.EndBeam(s.Beam);
                        s.Status = missing != null ? "waiting for " + missing + " in storage within this station's reach" : "idle";
                        if (missing == null) s.SupplyShip = null;
                        return;
                    }
                }
                bool charging = DockSupply.IsBatterySink(s.SupplySink);
                float rate = charging ? s.Rules.ChargePerSecond : s.Rules.DeliveryPerSecond;
                float carry = Math.Min((charging ? s.ChargeCarry : s.DeliveryCarry) + rate * (float)dt, Math.Max(1f, rate));
                int budget = (int)carry, before = budget;
                DockSupply.Deliver(StockOf(ship, s), target, s.SupplySink, ref budget);
                carry -= before - budget;
                if (charging) s.ChargeCarry = carry;
                else s.DeliveryCarry = carry;
                if (before == budget) s.ScanCooldown = 0;
                DockBeam.UpdateBeam(s.Beam, part, s.SupplySink.Part, s.Rules.BeamMediaEffects, s.Rules.BeamHitMediaEffects);
                s.Status = (charging ? "charging " : "resupplying ") + s.SupplySink.ResourceType + " on ship " + target.UniqueID
                           + " from " + StockOf(ship, s).Describe(s.SupplySink.ResourceType) + " in reach";
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("BayManager.WorkStationSupply", ex);
                DockBeam.EndBeam(s.Beam);
                s.SupplyShip = null;
                s.SupplySink = null;
            }
        }

        private static void ServiceCrew(Ship ship, ZtxBayTerminalComponent terminal)
        {
            BayState bay = terminal.Bay;
            Part part = terminal.Part;
            Ship held = bay.Held;
            int mode = HatchLinks.CrewMode(part, HatchLinks.BayCrewId);
            bay.CrewNote = "";
            try
            {
                if (mode == HatchLinks.CREW_BOARD)
                {
                    var hatches = new List<Part>();
                    foreach (ZtxBayStationComponent s in bay.Stations) if (s.Part != null) hatches.Add(s.Part);
                    bool anyQuarters = false;
                    foreach (Part h in hatches)
                        if (CrewSource.s_targetedParts.TryGetValue(h, out List<CrewSource> list) && list.Count > 0) anyQuarters = true;
                    if (!anyQuarters) { bay.CrewNote = "no quarters assigned to this bay's stations"; return; }
                    if (!bay.Built) { bay.CrewNote = "crew board when the ship is built"; return; }
                    List<CrewSoul> souls = DockCrew.AssignedTo(ship, hatches);
                    if (souls.Count == 0) { bay.CrewNote = bay.Crewed ? "crewed" : "assigned quarters are empty"; return; }
                    int moved = DockCrew.Move(souls, ship, held, part.WorldPhysicalCenter, out int left);
                    if (moved > 0) Log.Info("Bay terminal at " + part.Location + ": " + moved + " crew now belong to ship " + held.UniqueID + (left > 0 ? "; " + left + " found no free quarters there." : "."));
                    bay.CrewNote = left > 0 ? left + " assigned crew wait for free quarters" : "crew boarding";
                }
                else if (mode == HatchLinks.CREW_RETURN)
                {
                    List<CrewSoul> souls = DockCrew.HomedOn(held);
                    if (souls.Count == 0) { bay.CrewNote = "no crew aboard the held ship"; return; }
                    int moved = DockCrew.Move(souls, held, ship, part.WorldPhysicalCenter, out int left);
                    if (moved > 0) Log.Info("Bay terminal at " + part.Location + ": " + moved + " crew of ship " + held.UniqueID + " now belong to this ship" + (left > 0 ? "; " + left + " found no free quarters here." : "."));
                    bay.CrewNote = left > 0 ? left + " crew wait for free quarters on this ship" : "crew returning";
                }
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("BayManager.ServiceCrew", ex);
            }
        }

        private static void WorkStationRepair(Ship ship, ZtxBayStationComponent s, Time dt)
        {
            DockRepairState r = s.Repair;
            Part part = s.Part;
            if (r == null || part == null || part.Ship != ship) return;
            try
            {
                r.DeliveryCarry = Math.Min(r.DeliveryCarry + s.Rules.DeliveryPerSecond * (float)dt, Math.Max(1f, s.Rules.DeliveryPerSecond));
                int budget = (int)r.DeliveryCarry, before = budget;
                float mark = DockPipeline.WorkMark(r.Part);
                DockRepair.Work(StockOf(ship, s), r, dt, s.Rules.WorkPerSecond, ref budget, part.WorldPhysicalCenter, out bool finished, out string why);
                r.DeliveryCarry -= before - budget;
                bool beam = DockPipeline.BeamOn(r.Beam, before != budget || DockPipeline.WorkMark(r.Part) != mark);
                if (finished)
                {
                    Log.Info($"Bay station at {part.Location}: {(r.IsRebuild || r.IsDoor ? "rebuilt" : "repaired")} {r.Describe()} on ship {r.Ship?.UniqueID}.");
                    DockBeam.EndBeam(r.Beam);
                    s.Repair = null;
                    s.Status = "idle";
                    return;
                }
                DockBeam.UpdateBeam(r.Beam, part, beam ? r.BeamPart : null, s.Rules.BeamMediaEffects, s.Rules.BeamHitMediaEffects);
                if (!beam && !string.IsNullOrEmpty(why)) s.Status = (r.IsRebuild || r.IsDoor ? "rebuilding " : "repairing ") + r.Describe() + " -- " + why;
            }
            catch (Exception ex)
            {
                Log.Exception("BayManager.WorkStationRepair", ex);
                DockBeam.EndBeam(r.Beam);
                s.Repair = null;
            }
        }

        #endregion

        public static bool IsOn(Part part, ID<PartToggleGuiRules> id)
        {
            if (part == null) return false;
            PartUIToggle toggle = PartUIToggle.GetOnPart(part, id);
            return toggle != null && toggle.Mode != 0;
        }
    }
}
