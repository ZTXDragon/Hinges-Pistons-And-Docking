using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Construction;
using Cosmoteer.Ships.Doors;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Construction;
using Cosmoteer.Ships.Repairs;
using Halfling.Geometry;
using Halfling.Timing;

namespace ZTX.HingesPistonsDocking
{
    internal enum PipelineState { Done = 0, Building = 1, Spawnable = 2, Waiting = 3 }

    internal static class DockPipeline
    {
        public const int BEAM_GRACE_TICKS = 30;

        public static int IdleAfter(bool worked, int idleTicks) => worked ? 0 : Math.Min(Math.Max(0, idleTicks) + 1, BEAM_GRACE_TICKS + 1);

        public static bool BeamOn(int idleTicks) => idleTicks <= BEAM_GRACE_TICKS;

        public static bool BeamOn(DockBuildState beam, bool worked)
        {
            beam.IdleTicks = IdleAfter(worked, beam.IdleTicks);
            return BeamOn(beam.IdleTicks);
        }

        public static float WorkMark(Part part)
        {
            if (part == null || part.IsDestroyed) return 0f;
            return part.IsUnderConstruction ? (float)PartConstructionTracker.GetConstructionTracker(part).Progress : part.Health;
        }

        public static int HealthPreset(int maxHealth, int cost, int costMinusUnderlying)
        {
            if (cost <= 0) return 1;
            double share = 1.0 - (double)costMinusUnderlying / cost;
            return Math.Max(1, (int)Math.Ceiling(share * maxHealth));
        }

        public static int PickTarget(PipelineState[] states)
        {
            if (states == null) return -1;
            for (int i = 0; i < states.Length; i++)
                if (states[i] == PipelineState.Building) return i;
            for (int i = 0; i < states.Length; i++)
                if (states[i] == PipelineState.Spawnable) return i;
            return -1;
        }

        public static bool CellFree(string occupantId, string tempTileId, string frameId) =>
            occupantId == null || (!string.IsNullOrEmpty(tempTileId) && occupantId == tempTileId)
            || (!string.IsNullOrEmpty(frameId) && occupantId == frameId);

        public static bool CanAffordNew(PartInfo pi, ResourceStock stock, out string why)
        {
            why = "";
            foreach (var (type, n) in UnderlyingShare(pi.Rules))
                if (stock.Total(type) < n) { why = $"waiting for {n} {type} in {(stock.SourceCount >= 0 ? "storage in reach" : "storage")}"; return false; }
            return true;
        }

        public static bool RectFree(Ship ship, PartInfo pi)
        {
            foreach (IntVector2 c in pi.Rect)
                if (ship.Parts[c, PartRectType.Normal] != null) return false;
            return true;
        }

        private static string TempTileOf(PartInfo pi) => pi.Rules.TempConstructionPartPerTile.ToString() ?? "";

        private static string FrameOf(PartInfo pi)
        {
            string perTile = pi.Rules.UnderlyingPartPerTile.ToString() ?? "";
            return perTile.Length > 0 ? perTile : pi.Rules.UnderlyingPart.ToString() ?? "";
        }

        public static PipelineState StateOf(Ship ship, PartInfo pi, out Part part, out string why)
        {
            why = "";
            part = ship.Parts.GetExactPart(pi);
            if (part != null) return part.IsUnderConstruction ? PipelineState.Building : PipelineState.Done;
            string temp = TempTileOf(pi), frame = FrameOf(pi);
            foreach (IntVector2 c in pi.Rect)
            {
                Part q = ship.Parts[c, PartRectType.Normal];
                if (!CellFree(q?.Rules.ID.ToString(), temp, frame)) { why = $"{q.Rules.ID} stands at {c}"; return PipelineState.Waiting; }
            }
            if (!ship.Parts.IsNewPartContiguous(pi)) { why = "nothing to attach it to yet"; return PipelineState.Waiting; }
            if (ShipUpdateInfo.TestBlockage(ship, pi.Rect, ignoreJunk: false)) { why = $"something is in the way at {pi.Location}"; return PipelineState.Waiting; }
            return PipelineState.Spawnable;
        }

        public static List<(ID<ResourceRules> Type, int Count)> UnderlyingShare(PartRules rules)
        {
            var share = new List<(ID<ResourceRules>, int)>();
            Dictionary<ID<ResourceRules>, int> metered = rules.ResourcesMinusUnderlying;
            foreach (var (type, n) in rules.Resources)
            {
                metered.TryGetValue(type, out int m);
                if (n - m > 0) share.Add((type, n - m));
            }
            return share;
        }

        public static bool TrySpawn(Ship ship, PartInfo pi, bool free, out Part part, out string why, ResourceStock store = null)
        {
            part = null;
            why = "";
            ResourceStock stock = store ?? ResourceStock.Whole(ship);
            Ship bank = stock.Ship;
            List<(ID<ResourceRules> Type, int Count)> share = free ? new List<(ID<ResourceRules>, int)>() : UnderlyingShare(pi.Rules);
            int cells = 0, framed = 0;
            string frame = FrameOf(pi);
            foreach (IntVector2 c in pi.Rect)
            {
                cells++;
                Part q = ship.Parts[c, PartRectType.Normal];
                if (q != null && frame.Length > 0 && q.Rules.ID.ToString() == frame) framed++;
            }
            if (framed > 0 && cells > 0)
            {
                var scaled = new List<(ID<ResourceRules> Type, int Count)>();
                foreach (var (type, n) in share)
                {
                    int left = (n * (cells - framed) + cells - 1) / cells;
                    if (left > 0) scaled.Add((type, left));
                }
                share = scaled;
            }
            foreach (var (type, n) in share)
            {
                if (stock.Total(type) < n) { why = $"waiting for {n} {type} in {(store != null && stock.SourceCount >= 0 ? "storage in reach" : "storage")}"; return false; }
            }
            ClearTiles(ship, pi, bank);
            foreach (var (type, n) in share) stock.Remove(type, n);
            part = ship.Parts.CreatePart(pi.Rules, pi.Location, pi.Rotation, pi.FlipX, spawnedFromDestroyed: false, builtByPlayer: true,
                defaultToMinHealth: true, underConstruction: true);
            if (part == null) return false;
            part.Health = HealthPreset(pi.Rules.MaxHealth, pi.Rules.Cost, pi.Rules.CostMinusUnderlying);
            DestroyedPart wreck = ship.DestroyedParts.GetExactPart(pi.Rules, pi.Location, pi.Rotation, pi.FlipX);
            if (wreck != null) ship.DestroyedParts.RemoveDestroyedPart(wreck);
            return true;
        }

        private static void ClearTiles(Ship ship, PartInfo pi, Ship bank)
        {
            string temp = TempTileOf(pi), frame = FrameOf(pi);
            var seen = new HashSet<Part>();
            foreach (IntVector2 c in pi.Rect)
            {
                Part q = ship.Parts[c, PartRectType.Normal];
                if (q == null || !seen.Add(q)) continue;
                string id = q.Rules.ID.ToString();
                bool isTemp = temp.Length > 0 && id == temp, isFrame = frame.Length > 0 && id == frame;
                if (!isTemp && !isFrame) continue;
                try { ship.OptionalConstructionManager?.TryRemovePartDeconstruct(q); }
                catch (Exception ex) { LogThrottle.Exception("DockPipeline.ClearTiles(queue)", ex); }
                if (isFrame && q.IsUnderConstruction) Refund(bank, q, null);
                ship.Parts.RemovePart(q, PartsManager.CrewAction.Eject, destroyed: false, removeDoors: false);
            }
        }

        public static void Feed(ResourceStock stock, Part part, bool free, ref int budget, ref bool starved)
        {
            PartConstructionTracker tracker = PartConstructionTracker.GetConstructionTracker(part);
            foreach (ID<ResourceRules> type in part.Rules.ResourcesMinusUnderlying.Keys)
            {
                if (budget <= 0) return;
                int need = tracker.GetRemainingResourcesNeeded(type);
                if (need <= 0) continue;
                int avail = free ? need : stock.Total(type);
                int take = Math.Min(Math.Min(need, avail), budget);
                if (take <= 0) { starved = true; continue; }
                int removed = free ? take : stock.Remove(type, take);
                if (removed <= 0) { starved = true; continue; }
                tracker.ReceiveResources(type, removed, playSound: true, null);
                budget -= removed;
            }
        }

        public static void Work(Ship ship, Part part, Time dt, float workPerSecond, Vector2 dockWorld)
        {
            PartConstructionTracker tracker = PartConstructionTracker.GetConstructionTracker(part);
            if (tracker.Progress >= tracker.MaxProgress) return;
            using (ship.Sim.PushCurrentActionSourcePlayer(ship.Metadata.PlayerIndex, null, isForgivable: true))
                tracker.DoConstruction(dt, workPerSecond, dockWorld, null);
        }

        public static bool DoorReady(Ship ship, DoorInfo di)
        {
            IntVector2 a = di.Location.Cell, b = di.Location.OppositeCell;
            Part pa = ship.Parts[a, PartRectType.Normal], pb = ship.Parts[b, PartRectType.Normal];
            return pa != null && pb != null && !pa.IsUnderConstruction && !pb.IsUnderConstruction;
        }

        public static void FeedDoor(ResourceStock stock, Door door, bool free, ref int budget, ref bool starved)
        {
            DoorConstructionTracker tracker = DoorConstructionTracker.GetConstructionTracker(door);
            foreach (var (type, _) in door.Rules.Resources)
            {
                if (budget <= 0) return;
                int need = tracker.GetRemainingResourcesNeeded(type);
                if (need <= 0) continue;
                int avail = free ? need : stock.Total(type);
                int take = Math.Min(Math.Min(need, avail), budget);
                if (take <= 0) { starved = true; continue; }
                int removed = free ? take : stock.Remove(type, take);
                if (removed <= 0) { starved = true; continue; }
                tracker.ReceiveResources(type, removed, playSound: true);
                budget -= removed;
            }
        }

        public static int Refund(Ship ship, Part part, List<(ID<ResourceRules> Type, int Count)> paidShare)
        {
            int lost = 0;
            PartConstructionTracker tracker = PartConstructionTracker.GetConstructionTracker(part);
            foreach (ID<ResourceRules> type in part.Rules.ResourcesMinusUnderlying.Keys)
            {
                int n = tracker.GetStoredResources(type);
                if (n > 0) lost += n - ship.Resources.AddResources(type, n);
            }
            if (paidShare != null)
                foreach (var (type, n) in paidShare) lost += n - ship.Resources.AddResources(type, n);
            return lost;
        }

    }
}
