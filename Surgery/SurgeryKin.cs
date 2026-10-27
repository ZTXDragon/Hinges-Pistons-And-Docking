using System;
using System.Collections.Generic;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class KinTable<T> where T : class
    {
        private sealed class State
        {
            public readonly Dictionary<T, int> FamilyOf;
            public readonly Dictionary<int, long> Until;

            public State(Dictionary<T, int> familyOf, Dictionary<int, long> until)
            {
                FamilyOf = familyOf;
                Until = until;
            }
        }

        private volatile State _state = new State(new Dictionary<T, int>(), new Dictionary<int, long>());
        private int _nextFamily;

        public bool IsEmpty => _state.FamilyOf.Count == 0;

        public void Record(T a, T b, long until)
        {
            if (a == null || b == null) return;
            State s = _state;
            var familyOf = new Dictionary<T, int>(s.FamilyOf);
            var untilOf = new Dictionary<int, long>(s.Until);
            bool hasA = familyOf.TryGetValue(a, out int fa), hasB = familyOf.TryGetValue(b, out int fb);
            int family = hasA ? fa : hasB ? fb : ++_nextFamily;
            if (hasA && hasB && fa != fb)
            {
                var members = new List<T>();
                foreach (KeyValuePair<T, int> kv in familyOf)
                    if (kv.Value == fb) members.Add(kv.Key);
                foreach (T m in members) familyOf[m] = family;
                if (untilOf.TryGetValue(fb, out long ub) && (!untilOf.TryGetValue(family, out long uf) || ub > uf)) untilOf[family] = ub;
                untilOf.Remove(fb);
            }
            familyOf[a] = family;
            familyOf[b] = family;
            if (!untilOf.TryGetValue(family, out long cur) || until > cur) untilOf[family] = until;
            _state = new State(familyOf, untilOf);
        }

        public bool Contains(T a) => a != null && _state.FamilyOf.ContainsKey(a);

        public bool Are(T a, T b)
        {
            if (a == null || b == null) return false;
            State s = _state;
            return s.FamilyOf.TryGetValue(a, out int fa) && s.FamilyOf.TryGetValue(b, out int fb) && fa == fb;
        }

        public void Prune(long now)
        {
            State s = _state;
            if (s.Until.Count == 0) return;
            var ended = new HashSet<int>();
            foreach (KeyValuePair<int, long> kv in s.Until)
                if (kv.Value < now) ended.Add(kv.Key);
            if (ended.Count == 0) return;
            var familyOf = new Dictionary<T, int>();
            foreach (KeyValuePair<T, int> kv in s.FamilyOf)
                if (!ended.Contains(kv.Value)) familyOf[kv.Key] = kv.Value;
            var untilOf = new Dictionary<int, long>();
            foreach (KeyValuePair<int, long> kv in s.Until)
                if (!ended.Contains(kv.Key)) untilOf[kv.Key] = kv.Value;
            _state = new State(familyOf, untilOf);
        }

        public void Clear() => _state = new State(new Dictionary<T, int>(), new Dictionary<int, long>());
    }

    internal static class SurgeryKin
    {
        public const int GRACE_TICKS = 60;

        private static readonly KinTable<Ship> s_kin = new KinTable<Ship>();

        public static int Version { get; private set; }
        private static SimRoot s_sim;

        public static void Record(Ship a, Ship b)
        {
            SimRoot sim = a?.Sim ?? b?.Sim;
            if (sim == null) return;
            if (!ReferenceEquals(sim, s_sim))
            {
                s_kin.Clear();
                s_sim = sim;
            }
            s_kin.Record(a, b, (long)sim.Tick + GRACE_TICKS);
            Version++;
        }

        public static bool GraceApplies(bool kin, bool holding) => kin && holding;

        [ThreadStatic] internal static bool t_holding;

        public static bool Are(Ship a, Ship b) => a != null && b != null && !ReferenceEquals(a, b) && s_kin.Are(a, b);

        public static bool InFamily(Ship ship) => ship != null && s_kin.Contains(ship);

        public static void Prune(SimRoot sim)
        {
            if (sim == null) return;
            if (!ReferenceEquals(sim, s_sim))
            {
                s_kin.Clear();
                s_sim = sim;
                return;
            }
            if (!s_kin.IsEmpty) s_kin.Prune(sim.Tick);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Cosmoteer.Ships.Crew.Jobs.PartCrewJob), nameof(Cosmoteer.Ships.Crew.Jobs.PartCrewJob.IsValidFor))]
    internal static class SurgeryKinHoldingOperatorPatch
    {
        static void Prefix(Cosmoteer.Ships.Crew.Jobs.Job __instance, Cosmoteer.Crew.CrewSoul crew, out bool __state)
        {
            __state = SurgeryKin.t_holding;
            SurgeryKin.t_holding = crew != null && ReferenceEquals(crew.Job, __instance);
        }

        static void Finalizer(bool __state) => SurgeryKin.t_holding = __state;
    }

    [HarmonyLib.HarmonyPatch(typeof(Cosmoteer.Ships.Crew.Jobs.ResourceTransferJob), nameof(Cosmoteer.Ships.Crew.Jobs.ResourceTransferJob.IsValidFor))]
    internal static class SurgeryKinHoldingTransferPatch
    {
        static void Prefix(Cosmoteer.Ships.Crew.Jobs.Job __instance, Cosmoteer.Crew.CrewSoul crew, out bool __state)
        {
            __state = SurgeryKin.t_holding;
            SurgeryKin.t_holding = crew != null && ReferenceEquals(crew.Job, __instance);
        }

        static void Finalizer(bool __state) => SurgeryKin.t_holding = __state;
    }
}
