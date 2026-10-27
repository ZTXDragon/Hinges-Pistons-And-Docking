using System.Collections.Concurrent;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Commands;
using Cosmoteer.Simulation;

namespace ZTX.HingesPistonsDocking
{
    internal static class AssemblyTopology
    {
        private sealed class Entry
        {
            public SimRoot Sim;
            public int Version;
            public int Tick;
            public AssemblyGraph Groups;
        }

        private static volatile Entry s_last;

        public static AssemblyGraph GroupsOf(SimRoot sim)
        {
            if (sim == null) return AssemblyGraph.Empty;
            int version = HingeJointManager.TopologyVersion;
            int tick = sim.Tick;
            Entry e = s_last;
            if (e != null && ReferenceEquals(e.Sim, sim) && e.Version == version && e.Tick == tick) return e.Groups;
            var edges = new List<(Ship A, Ship B, bool SameShip)>();
            HingeJointManager.CollectAssemblyEdges(edges, sim);
            var ids = new List<(ulong, ulong, int)>(edges.Count);
            foreach ((Ship a, Ship b, bool same) in edges)
                ids.Add((Id(a), Id(b), same ? AssemblyGraph.KIND_SAME_SHIP : AssemblyGraph.KIND_ATTACHED));
            var marked = new HashSet<ulong>();
            HingeJointManager.CollectMarkedTops(marked, sim);
            AssemblyGraph g = AssemblyGraph.BuildGroupsWeighted(ids, marked, WeightsOf(sim, version, edges));
            s_last = new Entry { Sim = sim, Version = version, Tick = tick, Groups = g };
            return g;
        }

        public static ulong Id(Ship s) => s.UniqueID._id;

        private static SimRoot s_weightSim;
        private static int s_weightVersion = -1;
        private static readonly Dictionary<ulong, (int Command, float Mass)> s_weights = new Dictionary<ulong, (int Command, float Mass)>();
        private static readonly ConcurrentDictionary<PartRules, int> s_commandOf = new ConcurrentDictionary<PartRules, int>();

        private static Dictionary<ulong, (int Command, float Mass)> WeightsOf(SimRoot sim, int version, List<(Ship A, Ship B, bool SameShip)> edges)
        {
            lock (s_weights)
            {
                if (!ReferenceEquals(s_weightSim, sim) || s_weightVersion != version)
                {
                    s_weights.Clear();
                    s_weightSim = sim;
                    s_weightVersion = version;
                }
                foreach ((Ship a, Ship b, bool _) in edges)
                {
                    Capture(a);
                    Capture(b);
                }
                return new Dictionary<ulong, (int Command, float Mass)>(s_weights);
            }
        }

        private static void Capture(Ship ship)
        {
            ulong id = Id(ship);
            if (s_weights.ContainsKey(id)) return;
            float mass = ship.Physics?.Body?.Mass ?? 0f;
            if (!(mass > 0f)) return;
            s_weights[id] = (CommandPointsOf(ship), mass);
        }

        public static int CommandPointsOf(Ship ship)
        {
            int total = 0;
            foreach (Part p in ship.Parts)
                if (p != null && !p.IsUnderConstruction) total += s_commandOf.GetOrAdd(p.Rules, CommandPointsOf);
            return total;
        }

        private static int CommandPointsOf(PartRules rules)
        {
            int total = 0;
            foreach (PartComponentRules c in rules.Components)
                if (c is CommandProviderRules cp) total += cp.CommandPoints;
            return total;
        }
    }

    internal static class AnchorMarker
    {
        private static readonly ID<PartToggleGuiRules> s_id = new ID<PartToggleGuiRules>("ztx_anchor");

        public static bool Has(Part part)
        {
            var meta = part?.Ship?.Metadata;
            return meta != null && (meta.PartUIToggleStates.Get((new PartSaveData(part), s_id)) ?? 0) != 0;
        }

        public static void Set(Part part)
        {
            var meta = part?.Ship?.Metadata;
            if (meta == null) return;
            var psd = new PartSaveData(part);
            meta.PartUIToggleStates.Set((psd, s_id), 1);
            meta.PartUIToggleStates.Set((psd, s_id), 1, blueprintMode: true);
        }
    }
}
