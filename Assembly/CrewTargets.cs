using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Cosmoteer.Game.Gui.Crew;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Crew;
using Cosmoteer.Simulation;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class CrewTargets
    {
        private const int EVERY_TICKS = 30;
        private static readonly ConditionalWeakTable<Ship, object> s_touched = new ConditionalWeakTable<Ship, object>();
        private static readonly HashSet<CrewSource> s_crossHeld = new HashSet<CrewSource>();
        private static readonly object s_lock = new object();

        public static bool ShouldAdd(bool saved, bool resolves, bool held) => saved && resolves && !held;

        public static bool ShouldKeep(bool saved, bool inGroup) => saved && inGroup;

        internal static void Touched(Ship ship)
        {
            if (ship == null) return;
            lock (s_lock) s_touched.AddOrUpdate(ship, null);
        }

        internal static void Sync(SimRoot sim)
        {
            if (sim == null || sim.Tick % EVERY_TICKS != 0) return;
            int added = 0, dropped = 0;
            foreach (Ship ship in sim.Ships)
            {
                if (ship == null) continue;
                bool touched;
                lock (s_lock) touched = s_touched.TryGetValue(ship, out _);
                bool grouped = CorridorHinge.HasWorkGroup(ship);
                if (!touched && !grouped) continue;
                if (touched) lock (s_lock) s_touched.Remove(ship);
                List<Ship> group = grouped ? CorridorHinge.WorkGroupOf(ship) : new List<Ship>();
                foreach (CrewSource src in CrewSource.GetAllOnShip(ship).ToList())
                {
                    if (src?.Part == null) continue;
                    if (!ship.Metadata.CrewSourceTargets.TryGetValues(new PartSaveData(src.Part), out IReadOnlyCollection<PartSaveData> saved)) continue;
                    foreach (PartSaveData t in saved)
                    {
                        Part p = ship.Parts.GetExactPart(t);
                        bool cross = false;
                        if (p == null)
                            foreach (Ship m in group)
                            {
                                p = m.Parts.GetExactPart(t);
                                if (p != null) { cross = true; break; }
                            }
                        if (p == null || !p.Rules.CanBeCrewSourceTarget) continue;
                        if (!ShouldAdd(true, true, src.IsTargetingPart(p))) continue;
                        src.AddPartTarget(p);
                        added++;
                        if (cross) lock (s_lock) s_crossHeld.Add(src);
                    }
                }
            }
            List<CrewSource> held;
            lock (s_lock) held = s_crossHeld.ToList();
            foreach (CrewSource src in held)
            {
                Ship home = src.Part?.Ship;
                if (home == null || !ReferenceEquals(home.Sim, sim) || src._targetedParts == null)
                {
                    lock (s_lock) s_crossHeld.Remove(src);
                    continue;
                }
                var key = new PartSaveData(src.Part);
                bool any = false;
                foreach (Part p in src._targetedParts.ToList())
                {
                    if (p?.Ship == null || ReferenceEquals(p.Ship, home)) continue;
                    bool saved = home.Metadata.CrewSourceTargets.Contains(key, new PartSaveData(p));
                    if (ShouldKeep(saved, CorridorHinge.SameWorkGroup(home, p.Ship))) { any = true; continue; }
                    src.RemovePartTarget(p);
                    dropped++;
                }
                if (!any) lock (s_lock) s_crossHeld.Remove(src);
            }
            if (added + dropped > 0)
                LogThrottle.Info("CrewTargets|" + added + "|" + dropped, $"Crew targets: {added} saved target(s) re-taken (across corridor hinges or after a split / merge), {dropped} dropped (unsaved, or the corridor closed).");
        }

        public static Part TargetUnderCursor(TargetSquadsState state, Ship ship)
        {
            Part p = state.GetTargetPartUnderCursor(ship);
            if (p != null || ship?.Sim == null || state._selectedSources.Count == 0) return p;
            try
            {
                foreach (Ship m in CorridorHinge.WorkGroupOf(ship))
                {
                    Part q = m.Parts.GetPartUnderWorldPoint(ship.Sim.WorldMouseLoc);
                    if (q != null && q.Rules.CanBeCrewSourceTarget) return q;
                }
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("CrewTargets.TargetUnderCursor", ex);
            }
            return null;
        }
    }

    [HarmonyPatch]
    internal static class CrewTargetsPickerPatch
    {
        public const int EXPECTED = 2;
        private static readonly Dictionary<MethodBase, int> s_sites = new Dictionary<MethodBase, int>();
        public static string Summary => $"CrewTargets: {s_sites.Values.Sum()}/{EXPECTED} crew-target picker site(s) patched (assign crew to parts across corridor hinges)";

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(TargetSquadsState), "Input");
            yield return AccessTools.Method(typeof(TargetSquadsState), "DrawOverlay");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var list = new List<CodeInstruction>(instructions);
            MethodInfo from = AccessTools.Method(typeof(TargetSquadsState), "GetTargetPartUnderCursor");
            MethodInfo to = AccessTools.Method(typeof(CrewTargets), nameof(CrewTargets.TargetUnderCursor));
            int hits = 0;
            foreach (CodeInstruction ci in list)
                if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Equals(ci.operand, from) && to != null)
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = to;
                    hits++;
                }
            if (original != null) s_sites[original] = hits;
            return list;
        }
    }
}
