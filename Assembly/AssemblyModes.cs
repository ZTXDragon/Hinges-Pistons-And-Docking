using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Cosmoteer.Crew;
using Cosmoteer.Modes;
using Cosmoteer.Modes.Pvp;
using Cosmoteer.Modes.Pvp.Domination;
using Cosmoteer.Modes.Pvp.Pointmatch.Arena;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class AssemblyModes
    {
        public static int WholeValue(int[] segmentValues)
        {
            int sum = 0;
            if (segmentValues != null)
                foreach (int v in segmentValues) sum += v;
            return sum;
        }

        public static bool CountsAsSignificant(bool own, bool anySegmentSignificant) => own || anySegmentSignificant;

        public static bool SignificantShip(Ship ship)
        {
            bool own = ship.IsSignificant;
            if (own) return true;
            Ship[] segs = AssemblyAi.SegmentsOf(ship);
            return CountsAsSignificant(false, segs != null && segs.Any(s => s.IsSignificant));
        }

        private static int SegmentsValue(Ship[] segs) => WholeValue(segs.Select(s => s.TotalPhysicalValue).ToArray());

        public static int ValueForCapture(Ship ship)
        {
            Ship[] segs = AssemblyAi.SegmentsOf(ship);
            return segs == null ? ship.TotalPhysicalValue : SegmentsValue(segs);
        }

        public static int ValueForArena(Ship ship)
        {
            Ship[] segs = AssemblyAi.SegmentsOf(ship);
            return segs != null && ReferenceEquals(AssemblyAi.AnchorFor(ship), ship) ? SegmentsValue(segs) : ship.TotalPhysicalValue;
        }

        public static int ArenaPoints(ArenaSimModeManager mode, Ship ship)
        {
            int own = mode.GetShipPoints(ship);
            if (own != 0) return own;
            Ship anchor = AssemblyAi.SegmentsOf(ship) == null ? ship : AssemblyAi.AnchorFor(ship);
            return ReferenceEquals(anchor, ship) ? own : mode.GetShipPoints(anchor);
        }

        internal static Ship ArenaAnchorOf(ArenaSimModeManager mode, Ship segment)
        {
            if (segment == null || AssemblyAi.SegmentsOf(segment) == null) return null;
            Ship anchor = AssemblyAi.AnchorFor(segment);
            if (anchor == null || ReferenceEquals(anchor, segment) || !mode._shipInfos.ContainsKey(anchor)) return null;
            return anchor;
        }

        internal static void FoldInto(ArenaSimModeManager mode, Ship segment, Ship anchor)
        {
            if (!mode._shipInfos.TryGetValue(segment, out var seg) || seg.RemainingPoints <= 0) return;
            if (!mode._shipInfos.TryGetValue(anchor, out var into)) return;
            into.RemainingPoints += seg.RemainingPoints;
            Log.Info($"Arena: segment {segment.UniqueID} handed its {seg.RemainingPoints} point(s) to its ship's record (anchor {anchor.UniqueID}).");
            seg.RemainingPoints = 0;
        }

        internal static void Credit(ArenaSimModeManager mode, Ship anchor, Vector2? location)
        {
            if (!mode._shipInfos.TryGetValue(anchor, out var info) || info.RemainingPoints <= 0) return;
            mode.AccumulateDamage(anchor, info, location);
            mode.GiveKillCredit(anchor, info);
        }
    }

    [HarmonyPatch(typeof(ArenaSimModeManager), nameof(ArenaSimModeManager.OnPartDamaged))]
    internal static class ArenaPartDamagedPatch
    {
        static void Prefix(ArenaSimModeManager __instance, Ship ship, out Ship __state)
        {
            __state = null;
            try
            {
                __state = AssemblyModes.ArenaAnchorOf(__instance, ship);
                if (__state != null) AssemblyModes.FoldInto(__instance, ship, __state);
            }
            catch (Exception ex) { LogThrottle.Exception("ArenaPartDamagedPatch", ex); }
        }

        static void Postfix(ArenaSimModeManager __instance, Part part, Ship __state)
        {
            try
            {
                if (__state != null && part.ExistsInSim(__instance.Sim)) AssemblyModes.Credit(__instance, __state, part.WorldCenter);
            }
            catch (Exception ex) { LogThrottle.Exception("ArenaPartDamagedPatch", ex); }
        }
    }

    [HarmonyPatch(typeof(ArenaSimModeManager), nameof(ArenaSimModeManager.OnPartDestroyed))]
    internal static class ArenaPartDestroyedPatch
    {
        static void Prefix(ArenaSimModeManager __instance, Ship ship, out Ship __state)
        {
            __state = null;
            try
            {
                __state = AssemblyModes.ArenaAnchorOf(__instance, ship);
                if (__state != null) AssemblyModes.FoldInto(__instance, ship, __state);
            }
            catch (Exception ex) { LogThrottle.Exception("ArenaPartDestroyedPatch", ex); }
        }

        static void Postfix(ArenaSimModeManager __instance, Ship ship, Part part, Ship __state)
        {
            try
            {
                if (__state != null) AssemblyModes.Credit(__instance, __state, ship.TransformPointToWorld(part.LocalCenter));
            }
            catch (Exception ex) { LogThrottle.Exception("ArenaPartDestroyedPatch", ex); }
        }
    }

    [HarmonyPatch(typeof(ArenaSimModeManager), nameof(ArenaSimModeManager.OnCrewKilled))]
    internal static class ArenaCrewKilledPatch
    {
        static void Prefix(ArenaSimModeManager __instance, Ship wasAssignedToShip, out Ship __state)
        {
            __state = null;
            try
            {
                __state = AssemblyModes.ArenaAnchorOf(__instance, wasAssignedToShip);
                if (__state != null) AssemblyModes.FoldInto(__instance, wasAssignedToShip, __state);
            }
            catch (Exception ex) { LogThrottle.Exception("ArenaCrewKilledPatch", ex); }
        }

        static void Postfix(ArenaSimModeManager __instance, Vector2 worldLoc, Ship __state)
        {
            try
            {
                if (__state != null) AssemblyModes.Credit(__instance, __state, worldLoc);
            }
            catch (Exception ex) { LogThrottle.Exception("ArenaCrewKilledPatch", ex); }
        }
    }

    [HarmonyPatch(typeof(SimModeManager), nameof(SimModeManager.IncludeInIntegrityHash))]
    internal static class IntegrityHashSegmentsPatch
    {
        static void Postfix(Ship ship, ref bool __result)
        {
            try
            {
                if (!__result && ship != null && AssemblyFacade.Enabled && HingeJointManager.ActivePinCount > 0)
                    __result = AssemblyModes.SignificantShip(ship);
            }
            catch (Exception ex) { LogThrottle.Exception("IntegrityHashSegmentsPatch", ex); }
        }
    }

    [HarmonyPatch]
    internal static class ModeSitesPatch
    {
        public const int EXPECTED = 10;
        private static readonly Dictionary<MethodBase, int> s_sites = new Dictionary<MethodBase, int>();
        public static string Summary => $"AssemblyModes: {s_sites.Values.Sum()}/{EXPECTED} game-mode reads patched (team value, Domination, Arena)";

        private static MethodInfo Sig => AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.IsSignificant));
        private static MethodInfo Val => AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.TotalPhysicalValue));
        private static MethodInfo Pts => AccessTools.Method(typeof(ArenaSimModeManager), nameof(ArenaSimModeManager.GetShipPoints));

        private static int Count(MethodBase m, MethodInfo g)
        {
            try { return PatchProcessor.GetOriginalInstructions(m).Count(ci => (ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Equals(ci.operand, g)); }
            catch { return 0; }
        }

        private static List<(MethodBase M, (MethodInfo From, MethodInfo To)[] Map, int Expected)> Plan()
        {
            var plan = new List<(MethodBase, (MethodInfo, MethodInfo)[], int)>();
            MethodInfo sigTo = AccessTools.Method(typeof(AssemblyModes), nameof(AssemblyModes.SignificantShip));
            MethodInfo capVal = AccessTools.Method(typeof(AssemblyModes), nameof(AssemblyModes.ValueForCapture));
            MethodInfo arenaVal = AccessTools.Method(typeof(AssemblyModes), nameof(AssemblyModes.ValueForArena));
            MethodInfo ptsTo = AccessTools.Method(typeof(AssemblyModes), nameof(AssemblyModes.ArenaPoints));
            void Add(MethodBase m, int expected, params (MethodInfo, MethodInfo)[] map)
            {
                if (m != null) plan.Add((m, map, expected));
                else Log.Error("AssemblyModes: a target method was not found -- that read stays vanilla.");
            }
            Add(AccessTools.Method(typeof(PvpSimModeManager), nameof(PvpSimModeManager.CalculateTeamValue)), 1, (Sig, sigTo));
            Add(AccessTools.Method(typeof(PvpSimModeManager), nameof(PvpSimModeManager.CalculatePlayerValue)), 1, (Sig, sigTo));
            const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            MethodBase capture = typeof(CapturePoint).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Concat(new[] { typeof(CapturePoint) })
                .SelectMany(t => t.GetMethods(ALL))
                .FirstOrDefault(m => m.Name.Contains("GetOccupyingTeam") && m.Name.Contains("b__") && Count(m, Sig) == 1 && Count(m, Val) == 1);
            Add(capture, 2, (Sig, sigTo), (Val, capVal));
            Add(AccessTools.Method(typeof(ArenaSimModeManager), "AccumulateDamage"), 1, (Val, arenaVal));
            MethodBase local = typeof(ArenaSimModeManager).GetMethods(ALL).FirstOrDefault(m => m.Name.Contains("AccumulateDamageForPlayer"));
            Add(local, 1, (Val, arenaVal));
            Add(AccessTools.Method(typeof(ArenaSimModeManager), "GiveKillCredit"), 1, (Sig, sigTo));
            foreach (string n in new[] { "GetShipMouseOverText", "GetAllegianceForReducedShip", "ModifyAIAttackValue" })
                Add(AccessTools.Method(typeof(ArenaSimModeManager), n), 1, (Pts, ptsTo));
            return plan;
        }

        private static bool Prepare(MethodBase original) => original != null || Plan().Count > 0;

        private static IEnumerable<MethodBase> TargetMethods() => Plan().Select(p => p.M);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var list = new List<CodeInstruction>(instructions);
            var entry = Plan().FirstOrDefault(p => Equals(p.M, original));
            if (entry.M == null) return list;
            int hits = 0;
            foreach (var (from, to) in entry.Map)
                hits += list.Count(ci => (ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Equals(ci.operand, from));
            if (hits != entry.Expected || entry.Map.Any(mp => mp.From == null || mp.To == null))
            {
                s_sites[original] = 0;
                Log.Error($"AssemblyModes: found {hits} reads in {original.DeclaringType?.Name}.{original.Name}, expected {entry.Expected} -- left vanilla.");
                return list;
            }
            foreach (CodeInstruction ci in list)
            {
                if (ci.opcode != OpCodes.Call && ci.opcode != OpCodes.Callvirt) continue;
                foreach (var (from, to) in entry.Map)
                    if (Equals(ci.operand, from))
                    {
                        ci.opcode = OpCodes.Call;
                        ci.operand = to;
                        break;
                    }
            }
            s_sites[original] = hits;
            return list;
        }
    }
}
