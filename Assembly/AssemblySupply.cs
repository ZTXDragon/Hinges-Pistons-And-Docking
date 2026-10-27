using System;
using System.Collections.Generic;
using System.Linq;
using Cosmoteer.Data;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Resources;
using Cosmoteer.Simulation;
using Halfling;

namespace ZTX.HingesPistonsDocking
{
    internal static class AssemblySupply
    {
        private const int EVERY_TICKS = 15;
        private static int s_lastDiagTick = int.MinValue / 2;

        private const int AFTER_SURGERY_TICKS = 6;
        private static int s_seenSurgery;
        private static int s_burstUntil = int.MinValue / 2;

        public static void Reset()
        {
            s_lastDiagTick = int.MinValue / 2;
            s_burstUntil = int.MinValue / 2;
        }

        public static int[] PlanTakes(int capacityLeft, int requested, int threshold, bool ownSource, int[] available)
        {
            var takes = new int[available?.Length ?? 0];
            int need = capacityLeft - requested;
            if (ownSource || need < Math.Max(1, threshold)) return takes;
            for (int i = 0; i < takes.Length && need > 0; i++)
            {
                int give = Math.Min(need, Math.Max(0, available[i]));
                takes[i] = give;
                need -= give;
            }
            return takes;
        }

        private static bool Reachable(IResourceSource src, IResourceSink sink, Ship from, Ship sinkShip, ref int viaCorridor)
        {
            if (src.IsSinkReachable(sink, allowDifferentShips: true, allowAirlockTraversal: true)) return true;
            if (CorridorHinge.SameWorkGroup(from, sinkShip) && CorridorHinge.CanWalkTo(src.Part, sinkShip) && CorridorHinge.CanWalkTo(sink.Part, from))
            {
                viaCorridor++;
                return true;
            }
            return false;
        }

        internal static void Tick(SimRoot sim)
        {
            if (sim == null || !AssemblyFacade.Enabled || HingeJointManager.ActivePinCount == 0) return;
            if (SurgeryKin.Version != s_seenSurgery)
            {
                s_seenSurgery = SurgeryKin.Version;
                s_burstUntil = sim.Tick + AFTER_SURGERY_TICKS;
            }
            if (sim.Tick % EVERY_TICKS != 0 && sim.Tick > s_burstUntil) return;
            var done = new HashSet<Ship>();
            int filedJobs = 0, filedUnits = 0;
            int dSinks = 0, dCovered = 0, dOwn = 0, dNone = 0;
            int nType = 0, nPrio = 0, nReach = 0, viaCorridor = 0;
            string eType = null, ePrio = null, eReach = null;
            foreach (Ship ship in sim.Ships)
            {
                if (ship == null || done.Contains(ship)) continue;
                Ship[] segs = AssemblyAi.SegmentsOf(ship);
                if (segs == null) continue;
                foreach (Ship s in segs) done.Add(s);
                foreach (Ship sinkShip in segs)
                {
                    if (sinkShip?.Resources == null) continue;
                    var sinks = sinkShip.Resources.Sinks
                        .Where(k => k?.Part != null && !k.IsConstructionSink && k.ResourceType != ResourceRules.Stackable)
                        .OrderBy(k => k.Part.Location.Y).ThenBy(k => k.Part.Location.X).ThenBy(k => k.ResourceType.ToString(), StringComparer.Ordinal)
                        .ToList();
                    foreach (IResourceSink sink in sinks)
                    {
                        ID<ResourceRules> type = sink.ResourceType;
                        int capacity = sink.GetRemainingCapacity(type, false);
                        int requested = sinkShip.Resources.GetRequestedDelivery(sink, MPValueType.Confirmed);
                        dSinks++;
                        if (capacity - requested < Math.Max(1, sink.ResupplyThreshold)) { dCovered++; continue; }
                        bool own = sinkShip.Resources.Sources.Any(src => src.ResourceType == type && src.IsValidResourceSource && src.Resources > 0
                            && sink.CheckPriority(src, false, MPValueType.Confirmed)
                            && src.IsSinkReachable(sink, allowDifferentShips: false, allowAirlockTraversal: false));
                        if (own) { dOwn++; continue; }
                        var ordered = new List<ResourceTransferJob>(ResourceTransferJob.GetTransferJobsFor(sink, MPValueType.Confirmed));
                        var candidates = new List<(IResourceSource Src, Ship From, int Spare)>();
                        int typeSeen = 0, prioOk = 0;
                        foreach (Ship from in segs)
                        {
                            if (ReferenceEquals(from, sinkShip) || from?.Resources == null) continue;
                            var srcs = new List<IResourceSource>();
                            foreach (IResourceSource src in from.Resources.Sources)
                            {
                                if (src?.Part == null || src.ResourceType != type || !src.IsValidResourceSource) continue;
                                typeSeen++;
                                if (!sink.CheckPriority(src, false, MPValueType.Confirmed)) continue;
                                prioOk++;
                                if (ordered.Any(j => j.IsManual && ReferenceEquals(j.Source, src))) continue;
                                if (!Reachable(src, sink, from, sinkShip, ref viaCorridor)) continue;
                                srcs.Add(src);
                            }
                            srcs = srcs.OrderByDescending(src => src.SortPriority).ThenBy(src => src.Part.Location.Y).ThenBy(src => src.Part.Location.X)
                                .ToList();
                            foreach (IResourceSource src in srcs)
                            {
                                int spare = src.Resources - from.Resources.GetAnticipatedPickUp(src, MPValueType.Confirmed)
                                    - sinkShip.Resources.GetAnticipatedPickUp(src, MPValueType.Confirmed);
                                candidates.Add((src, from, spare));
                            }
                        }
                        if (candidates.Count == 0)
                        {
                            dNone++;
                            string what = $"{type} for {sink.Part.Rules.ID} at {sink.Part.Location} on ship {sinkShip.UniqueID}";
                            if (typeSeen == 0) { nType++; eType = eType ?? what; }
                            else if (prioOk == 0) { nPrio++; ePrio = ePrio ?? what; }
                            else { nReach++; eReach = eReach ?? what; }
                            continue;
                        }
                        int[] takes = PlanTakes(capacity, requested, sink.ResupplyThreshold, own, candidates.Select(c => c.Spare).ToArray());
                        for (int i = 0; i < takes.Length; i++)
                        {
                            if (takes[i] <= 0) continue;
                            ShipMerger.EnsureJobIdsAbove(sinkShip);
                            sinkShip.Resources.ManualResourceTransfer(candidates[i].Src, sink, takes[i], sticky: false, TradeType.None, 0,
                                autoCancel: false, confirmed: true, sinkShip.Jobs.AllocGlobalJobID());
                            filedJobs++;
                            filedUnits += takes[i];
                            LogThrottle.Info("Supply|" + sinkShip.UniqueID + "|" + type,
                                $"Assembly supply: {takes[i]} {type} for {sink.Part.Rules.ID} at {sink.Part.Location} on ship {sinkShip.UniqueID} from {candidates[i].Src.Part.Rules.ID} on ship {candidates[i].From.UniqueID} (crew carry it across).");
                        }
                    }
                }
            }
            if (dSinks > 0 && HingeJointManager.ActivePinCount > 0 && sim.Tick - s_lastDiagTick >= 300)
            {
                s_lastDiagTick = sim.Tick;
                LogThrottle.Info("SupplyDiag|" + dSinks + "|" + dCovered + "|" + dOwn + "|" + dNone + "|" + filedJobs,
                    $"Assembly supply check: {dSinks} segment sink(s) wanting a resource -- {dCovered} already covered, {dOwn} left to their own ship's supply, {dNone} with no reachable source on another segment, {filedJobs} job(s) filed now." +
                    $" No source: {nType} none of the type on another segment{(eType != null ? " (e.g. " + eType + ")" : "")}, {nPrio} refused by priority{(ePrio != null ? " (e.g. " + ePrio + ")" : "")}, {nReach} unreachable or already ordered{(eReach != null ? " (e.g. " + eReach + ")" : "")}; {viaCorridor} source(s) reachable only through a corridor hinge.");
            }
        }
    }
}
