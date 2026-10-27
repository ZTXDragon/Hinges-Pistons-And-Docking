using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Doors;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Construction;
using Halfling.Geometry;
using Halfling.Timing;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class DockRepairState
    {
        public Ship Ship;
        public Part Part;
        public PartInfo Rebuild;
        public bool IsRebuild;
        public bool IsDoor;
        public DoorInfo DoorInfo;
        public Door Door;
        public List<(ID<ResourceRules> Type, int Count)> Paid;
        public string LastLogged;
        public float DeliveryCarry;
        public readonly DockBuildState Beam = new DockBuildState();

        public Part BeamPart => IsDoor ? Ship?.Parts[DoorInfo.Location.Cell, PartRectType.Normal] : Part;

        public string Describe() =>
            IsDoor ? $"door at {DoorInfo.Location.Cell}" : IsRebuild ? $"{Rebuild.Rules?.ID} at {Rebuild.Location}" : $"{Part?.Rules.ID} at {Part?.Location}";
    }

    internal static class DockRepair
    {
        public static int PickNearest(float[] distances, float reach)
        {
            int best = -1;
            if (distances == null) return best;
            for (int i = 0; i < distances.Length; i++)
            {
                if (distances[i] > reach) continue;
                if (best < 0 || distances[i] < distances[best]) best = i;
            }
            return best;
        }

        public static bool FindTarget(Ship carrier, Part dockPart, float reachCells, out DockRepairState target, out string rebuildWhy)
        {
            target = null;
            rebuildWhy = null;
            Vector2 from = dockPart.WorldPhysicalCenter;
            float scale = carrier.Scale.X > 0f ? carrier.Scale.X : 1f;
            var options = new List<DockRepairState>();
            var distances = new List<float>();
            foreach (Ship held in DockShipManager.HeldShips(carrier))
            {
                if (held?.Physics?.Body == null) continue;
                foreach (Part p in held.Parts)
                {
                    bool building = p.IsUnderConstruction;
                    if (!building && p.Health >= p.Rules.MaxHealth) continue;
                    float d = (p.WorldPhysicalCenter - from).Length / scale;
                    if (d > reachCells) continue;
                    options.Add(new DockRepairState { Ship = held, Part = p });
                    distances.Add(d);
                }
                foreach (BlueprintPart bp in held.BlueprintParts)
                {
                    var pi = new PartInfo(bp.Rules, bp.Location, bp.Rotation, bp.FlipX);
                    if (held.Parts.GetExactPart(pi) != null) continue;
                    Vector2 world = held.Physics.Body.GetWorldPoint(pi.LocalCenter * held.Scale);
                    float d = (world - from).Length / scale;
                    if (d > reachCells) continue;
                    if (DockPipeline.StateOf(held, pi, out _, out string why) != PipelineState.Spawnable)
                    {
                        if (rebuildWhy == null) rebuildWhy = $"{pi.Rules.ID} at {pi.Location} on ship {held.UniqueID}: {why}";
                        continue;
                    }
                    options.Add(new DockRepairState { Ship = held, Rebuild = pi, IsRebuild = true });
                    distances.Add(d);
                }
                foreach (BlueprintDoor bd in held.BlueprintDoors)
                {
                    var di = new DoorInfo(bd.Rules, bd.Location);
                    Door door = held.Doors.GetExactDoor(di);
                    if (door != null && !door.IsUnderConstruction) continue;
                    if (door == null && held.Doors[di.Location] != null) continue;
                    Vector2 world = held.Physics.Body.GetWorldPoint(di.Location.Center * held.Scale);
                    float d = (world - from).Length / scale;
                    if (d > reachCells) continue;
                    if (door == null && !DockPipeline.DoorReady(held, di))
                    {
                        if (rebuildWhy == null) rebuildWhy = $"door at {di.Location.Cell} on ship {held.UniqueID}: a side is not built yet";
                        continue;
                    }
                    options.Add(new DockRepairState { Ship = held, IsDoor = true, DoorInfo = di, Door = door });
                    distances.Add(d);
                }
            }
            int pick = PickNearest(distances.ToArray(), reachCells);
            if (pick < 0) return false;
            target = options[pick];
            return true;
        }

        private static bool WorkDoor(ResourceStock stock, DockRepairState t, ref int budget, out bool finished, out string why)
        {
            finished = false;
            why = "";
            Ship ship = t.Ship;
            Door door = ship.Doors.GetExactDoor(t.DoorInfo);
            if (door == null)
            {
                if (!DockPipeline.DoorReady(ship, t.DoorInfo)) { why = "waiting for both sides of the door"; return false; }
                try
                {
                    Cosmoteer.Ships.Repairs.DestroyedDoor wreck = ship.DestroyedDoors[t.DoorInfo.Location];
                    if (wreck != null) ship.DestroyedDoors.RemoveDestroyedDoor(wreck);
                    door = ship.Doors.CreateDoor(t.DoorInfo.Rules, t.DoorInfo.Location, builtByPlayer: true, underConstruction: true);
                }
                catch (Exception ex)
                {
                    LogThrottle.Exception("DockRepair.WorkDoor", ex);
                    finished = true;
                    return false;
                }
                if (door == null) { finished = true; return false; }
            }
            t.Door = door;
            if (!door.IsUnderConstruction) { finished = true; return true; }
            bool starved = false;
            if (budget > 0) DockPipeline.FeedDoor(stock, door, free: false, ref budget, ref starved);
            Door now = ship.Doors.GetExactDoor(t.DoorInfo);
            t.Door = now;
            if (now != null && !now.IsUnderConstruction) finished = true;
            else if (starved) why = "waiting for materials in storage";
            return true;
        }

        public static bool Work(ResourceStock stock, DockRepairState t, Time dt, float workPerSecond, ref int budget, Vector2 dockWorld, out bool finished, out string why)
        {
            finished = false;
            why = "";
            if (t.Ship == null || t.Ship.Sim == null) { finished = true; return false; }
            if (t.IsDoor) return WorkDoor(stock, t, ref budget, out finished, out why);
            if (t.IsRebuild && t.Part == null)
            {
                Part existing = t.Ship.Parts.GetExactPart(t.Rebuild);
                if (existing != null) t.Part = existing;
                else
                {
                    if (DockPipeline.StateOf(t.Ship, t.Rebuild, out _, out why) != PipelineState.Spawnable) { finished = true; return false; }
                    t.Paid = DockPipeline.UnderlyingShare(t.Rebuild.Rules);
                    if (!DockPipeline.TrySpawn(t.Ship, t.Rebuild, free: false, out Part spawned, out why, store: stock)) return false;
                    t.Part = spawned;
                }
            }
            Part p = t.Part;
            if (p == null || p.IsDestroyed || p.Ship != t.Ship) { finished = true; return false; }
            bool starved = false;
            if (p.IsUnderConstruction)
            {
                if (budget > 0) DockPipeline.Feed(stock, p, free: false, ref budget, ref starved);
                DockPipeline.Work(stock.Ship, p, dt, workPerSecond, dockWorld);
                if (starved) why = "waiting for materials in storage";
                return true;
            }
            if (p.Health >= p.Rules.MaxHealth) { finished = true; return false; }
            PartRepairTracker tracker = PartRepairTracker.GetOrCreateRepairTracker(p);
            foreach (var (type, _) in p.Rules.Resources)
            {
                if (budget <= 0) break;
                int need = tracker.GetRemainingResourcesNeeded(type);
                if (need <= 0) continue;
                int take = Math.Min(Math.Min(need, stock.Total(type)), budget);
                if (take <= 0) { starved = true; continue; }
                int removed = stock.Remove(type, take);
                if (removed <= 0) { starved = true; continue; }
                tracker.ReceiveResources(type, removed, playSound: true);
                budget -= removed;
            }
            int before = p.Health;
            using (stock.Ship.Sim.PushCurrentActionSourcePlayer(stock.Ship.Metadata.PlayerIndex, null, isForgivable: true))
                tracker.DoRepair(dt, workPerSecond);
            if (p.Health >= p.Rules.MaxHealth) finished = true;
            else if (p.Health == before && starved) why = "waiting for materials in storage";
            return true;
        }
    }
}
