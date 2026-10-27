using System;
using System.Collections.Generic;
using System.Linq;
using Cosmoteer.Modes;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts.Weapons;
using Cosmoteer.Simulation;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class AssemblyVerdict
    {
        [ThreadStatic] private static bool s_applying;
        private static readonly object s_lock = new object();
        private static readonly HashSet<(SimRoot, ulong)> s_pending = new HashSet<(SimRoot, ulong)>();

        public static bool Alive(bool[] segmentPassesModeRule, int totalCrew, bool anyCommandOrWeapon)
        {
            if (segmentPassesModeRule != null)
                foreach (bool b in segmentPassesModeRule)
                    if (b) return true;
            return totalCrew > 0 && anyCommandOrWeapon;
        }

        public static bool AllowJunkNow(SimModeManager mode, Ship ship, int? updateCrew)
        {
            if (s_applying) return true;
            Ship[] members = AssemblyAi.SegmentsOf(ship);
            if (members == null) return !HingeJointManager.IsJointed(ship);
            SimRoot sim = ship.Sim;
            if (sim == null) return true;
            ulong key = AssemblyTopology.Id(members[0]);
            lock (s_lock)
            {
                if (!s_pending.Add((sim, key))) return false;
            }
            sim.EnqueueDeterministic(members[0].UniqueID, () => Judge(mode, sim, members[0], key, updateCrew));
            return false;
        }

        private static void Judge(SimModeManager mode, SimRoot sim, Ship first, ulong key, int? updateCrew)
        {
            lock (s_lock) s_pending.Remove((sim, key));
            JudgeGroup(mode, sim, AssemblyAi.SegmentsOf(first) ?? new[] { first }, updateCrew, "lost its crew or command", logAlive: true);
        }

        private static readonly List<(SimRoot Sim, Ship Ship)> s_cut = new List<(SimRoot, Ship)>();

        public static bool JudgesCut(bool sameShipJoint, bool stillExists, bool alreadyJunk) => sameShipJoint && stillExists && !alreadyJunk;

        public static void NoteCut(Ship a, Ship b, bool sameShipJoint)
        {
            if (!sameShipJoint) return;
            lock (s_lock)
            {
                if (a?.Sim != null) s_cut.Add((a.Sim, a));
                if (b?.Sim != null) s_cut.Add((b.Sim, b));
            }
        }

        public static void JudgeCuts(SimRoot sim)
        {
            List<Ship> sides = null;
            lock (s_lock)
            {
                for (int i = s_cut.Count - 1; i >= 0; i--)
                {
                    if (!ReferenceEquals(s_cut[i].Sim, sim)) continue;
                    (sides ??= new List<Ship>()).Add(s_cut[i].Ship);
                    s_cut.RemoveAt(i);
                }
            }
            if (sides == null || sim?.Mode == null || !AssemblyFacade.Enabled) return;
            var judged = new HashSet<Ship>();
            sides.Sort((x, y) => AssemblyTopology.Id(x).CompareTo(AssemblyTopology.Id(y)));
            foreach (Ship s in sides)
            {
                bool exists = s != null && ReferenceEquals(s.Sim, sim) && s.Parts.Count > 0 && sim.Ships.Contains(s);
                if (!JudgesCut(true, exists, s?.IsJunk ?? true) || judged.Contains(s)) continue;
                Ship[] group = AssemblyAi.SegmentsOf(s) ?? new[] { s };
                foreach (Ship g in group) judged.Add(g);
                JudgeGroup(sim.Mode, sim, group, null, "was cut off without crew or command", logAlive: false);
            }
        }

        private static void JudgeGroup(SimModeManager mode, SimRoot sim, Ship[] group, int? updateCrew, string why, bool logAlive)
        {
            try
            {
                Ship first = group.FirstOrDefault(m => m != null);
                if (first == null) return;
                Ship[] members = group.Where(m => m != null && ReferenceEquals(m.Sim, sim) && !m.IsJunk).ToArray();
                if (members.Length == 0) return;
                ulong key = AssemblyTopology.Id(first);
                var own = new bool[members.Length];
                int crew = 0;
                bool capability = false;
                for (int i = 0; i < members.Length; i++)
                {
                    Ship m = members[i];
                    own[i] = mode.GetAllegianceForReducedShip(m, null, out _) != -3;
                    crew += m.Crew.Assigned.Count;
                    capability |= m.Commands.HasSignificantCommandParts
                        || (!(mode is Cosmoteer.Modes.Pvp.Domination.DominationSimModeManager) && m.Weapons.Any(w => w.Part.IsSignificant && !w.Rules.IsDefensiveOnly));
                }
                if (Alive(own, crew, capability))
                {
                    if (logAlive) LogThrottle.Info("Verdict|" + key, $"Assembly verdict: ship {first.UniqueID} and its {members.Length - 1} segment(s) live on (crew {crew}, command or weapons {(capability ? "yes" : "no")}).");
                    return;
                }
                s_applying = true;
                try
                {
                    foreach (Ship m in members)
                        if (!m.IsJunk) mode.SetReducedShipAllegiance(m, -3, updateCrew);
                }
                finally
                {
                    s_applying = false;
                }
                Log.Info($"Assembly verdict: ship {first.UniqueID} {why} -- all {members.Length} segment(s) are junk now.");
            }
            catch (Exception ex)
            {
                Log.Exception("AssemblyVerdict.Judge", ex);
            }
        }
    }

    [HarmonyPatch(typeof(SimModeManager), "SetReducedShipAllegiance")]
    internal static class AssemblyVerdictPatch
    {
        static bool Prefix(SimModeManager __instance, Ship ship, int allegiance, int? updateCrew)
        {
            try
            {
                if (!AssemblyFacade.Enabled || allegiance != -3 || ship == null || ship.IsJunk) return true;
                return AssemblyVerdict.AllowJunkNow(__instance, ship, updateCrew);
            }
            catch (Exception ex)
            {
                Log.Exception("AssemblyVerdictPatch", ex);
                return true;
            }
        }
    }
}
