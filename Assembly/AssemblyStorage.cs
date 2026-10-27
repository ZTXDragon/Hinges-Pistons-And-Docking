using System;
using System.Collections.Generic;
using System.Linq;
using Cosmoteer.Data;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Resources;
using Cosmoteer.Simulation;
using Halfling;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class AssemblyStorage
    {
        public static int[] Spill(int quantity, int[] capacities)
        {
            if (capacities == null) return new int[0];
            var takes = new int[capacities.Length];
            int left = Math.Max(0, quantity);
            for (int i = 0; i < capacities.Length && left > 0; i++)
            {
                int t = Math.Min(left, Math.Max(0, capacities[i]));
                takes[i] = t;
                left -= t;
            }
            return takes;
        }

        public static bool Fillable(bool ownCrew, bool otherSegmentCrew, bool anyCollector) => ownCrew || otherSegmentCrew || anyCollector;

        public static bool CanBeFilled(Ship segment)
        {
            if (segment == null) return false;
            bool own = segment.Crew.Assigned.Count > 0;
            bool other = false;
            if (!own)
            {
                Ship[] members = AssemblyIndex.AssemblyOf(segment);
                if (members != null)
                    foreach (Ship m in members)
                        if (m != null && !ReferenceEquals(m, segment) && m.Metadata.PlayerIndex == segment.Metadata.PlayerIndex && m.Crew.Assigned.Count > 0)
                        {
                            other = true;
                            break;
                        }
            }
            return Fillable(own, other, AssemblyReach.ShipHasNuggetCollectors(segment));
        }

        public static IEnumerable<Ship> SinkShips(PlayerInputHandler input)
        {
            IEnumerable<Ship> vanilla = input.SelectedCrewCommandableShips;
            try
            {
                if (!AssemblyFacade.Enabled || HingeJointManager.ActivePinCount == 0) return vanilla;
                var result = new List<Ship>();
                var seen = new HashSet<Ship>();
                foreach (Ship root in input.SelectedShips.Select(AssemblyFacade.Redirect).Distinct())
                {
                    if (root == null) continue;
                    Ship[] members = AssemblyIndex.AssemblyOf(root);
                    if (members == null || members.Length < 2 || Array.IndexOf(members, root) < 0)
                    {
                        if (root.Commands.CanExecuteCrewCommandsFromLocalPlayer && seen.Add(root)) result.Add(root);
                        continue;
                    }
                    var order = new List<Ship> { root };
                    order.AddRange(members.Where(m => m != null && !ReferenceEquals(m, root)));
                    foreach (Ship m in order)
                        if (m.Commands.IsCommandableByLocalPlayer && CanBeFilled(m) && seen.Add(m)) result.Add(m);
                }
                return result;
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("AssemblyStorage.SinkShips", ex);
                return vanilla;
            }
        }

        internal struct Wish
        {
            public Ship Owner;
            public HashSet<ID<ResourceRules>> Types;
            public bool AutoCancel;
        }

        public static void SpillNuggets(SimRoot sim, int oldNuggets, List<Wish> wishes)
        {
            var done = new HashSet<(Nugget, Ship[])>();
            for (int k = oldNuggets; k < sim.Nuggets.Count; k++)
            {
                Nugget n = sim.Nuggets[k];
                if (n == null) continue;
                foreach (Wish w in wishes)
                {
                    if (w.Types == null || !w.Types.Contains(n.ResourceType)) continue;
                    Ship[] members = AssemblyIndex.FreshAssemblyOf(w.Owner);
                    if (members == null || members.Length < 2 || !done.Add((n, members))) continue;
                    int requested = 0;
                    foreach (ResourceTransferJob j in ResourceTransferJob.GetTransferJobsFor(n, MPValueType.Confirmed))
                        if (j?.Ship != null && Array.IndexOf(members, j.Ship) >= 0) requested += j.ResourcesRequested.Confirmed;
                    int left = n.Quantity - requested;
                    if (left <= 0) continue;
                    var segs = members.Where(m => m != null && !ReferenceEquals(m, w.Owner) && ReferenceEquals(m.Sim, sim)
                        && m.Metadata.PlayerIndex == w.Owner.Metadata.PlayerIndex && CanBeFilled(m)).ToList();
                    if (segs.Count == 0) continue;
                    int[] caps = segs.Select(m => m.Resources.GetRemainingCapacity(n.ResourceType, includeUntypedStorages: true, includeStored: true,
                        includeCarried: true, includeOffShipCarried: false, includeAnticipatedDeliveries: true)).ToArray();
                    int[] takes = Spill(left, caps);
                    int filed = 0;
                    for (int i = 0; i < segs.Count; i++)
                        if (takes[i] > 0) filed += FileFor(n, segs[i], takes[i], w.AutoCancel);
                    if (filed > 0)
                        LogThrottle.Info("AssemblyStorage|spill", $"Assembly storage: {filed} of a salvaged {n.ResourceType} nugget the ship's storage could not take went to {segs.Count} other segment(s).");
                }
            }
        }

        private static int FileFor(Nugget n, Ship ship, int amount, bool autoCancel)
        {
            int filed = 0;
            while (amount > 0)
            {
                IResourceSink sink = ship.Resources.GetBestReceivingSink(n, amount, MPValueType.Confirmed, out int best);
                if (sink == null) break;
                int take = Math.Min(amount, best);
                if (take <= 0) break;
                ShipMerger.EnsureJobIdsAbove(ship);
                ResourceTransferJob job = ship.Resources.ManualResourceTransfer(n, sink, take, sticky: false, TradeType.None, 0, autoCancel, confirmed: true, ship.Jobs.AllocGlobalJobID());
                job.PlayerTimeIssued = App.Clock.Time;
                amount -= take;
                filed += take;
            }
            return filed;
        }
    }

    [HarmonyPatch(typeof(SalvageJob), nameof(SalvageJob.OnPartSalvaged))]
    internal static class AssemblyStorageSalvagePatch
    {
        static void Prefix(Part salvagedPart, Ship salvagedShip, out List<AssemblyStorage.Wish> __state)
        {
            __state = null;
            try
            {
                if (!AssemblyFacade.Enabled || HingeJointManager.ActivePinCount == 0 || salvagedPart == null) return;
                var wishes = new List<AssemblyStorage.Wish>();
                if (SalvageJob.s_jobsByPart.TryGetValue(salvagedPart, out List<SalvageJob> jobs) && jobs != null)
                    foreach (SalvageJob sj in jobs)
                        if (sj != null && !sj.IsNonActive && sj.Ship != null && sj._autoCollectResourceTypes != null)
                            wishes.Add(new AssemblyStorage.Wish { Owner = sj.Ship, Types = sj._autoCollectResourceTypes, AutoCancel = sj.AutoCancel.Confirmed });
                if (salvagedShip != null)
                {
                    var cmds = new List<SalvageCommand>();
                    SalvageCommand.GetSalvageCommandsForShip(salvagedShip, cmds);
                    foreach (SalvageCommand c in cmds)
                        if (c?.Ship != null && c.AutoCollectResourceTypes != null)
                            wishes.Add(new AssemblyStorage.Wish { Owner = c.Ship, Types = c.AutoCollectResourceTypes, AutoCancel = true });
                }
                if (wishes.Count > 0) __state = wishes;
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("AssemblyStorageSalvagePatch.Prefix", ex);
            }
        }

        static void Postfix(SimRoot sim, int oldNuggets, List<AssemblyStorage.Wish> __state)
        {
            if (__state == null || sim == null) return;
            try
            {
                AssemblyStorage.SpillNuggets(sim, oldNuggets, __state);
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("AssemblyStorageSalvagePatch.Postfix", ex);
            }
        }
    }
}
