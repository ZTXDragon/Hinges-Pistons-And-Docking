using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Rendering;
using Halfling;
using Halfling.Geometry;
using Halfling.Random;
using Halfling.Timing;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class JumpAnim
    {
        private sealed class Group
        {
            public float? OutDelay;
            public Time? InDelay;
            public double Made;
        }

        private const double STALE_SECONDS = 60.0;
        private static readonly ConditionalWeakTable<Ship, Group> s_groups = new ConditionalWeakTable<Ship, Group>();
        [ThreadStatic] private static ShipRenderer t_current;
        private static bool s_inner;

        internal static void Join(IEnumerable<Ship> ships)
        {
            var g = new Group { Made = App.Clock.Time };
            foreach (Ship s in ships)
            {
                if (s == null) continue;
                s_groups.Remove(s);
                s_groups.Add(s, g);
            }
        }

        private static Group GroupOf(Ship ship)
        {
            if (ship == null || !s_groups.TryGetValue(ship, out Group g)) return null;
            if (App.Clock.Time - g.Made > STALE_SECONDS) { s_groups.Remove(ship); return null; }
            return g;
        }

        internal static void OnJumpOut(ShipRenderer r, Direction dir, Ship gateShip, ref float? forceDelay)
        {
            if (forceDelay.HasValue || r?.Ship == null) return;
            Group g = GroupOf(r.Ship);
            if (g == null) return;
            if (!g.OutDelay.HasValue) g.OutDelay = (float)App.Rand.Time(r.Rules.FtlEffects.JumpOutDelay);
            forceDelay = g.OutDelay;
        }

        internal static void AnimateSegmentsOut(Ship ship, Direction dir, IEnumerable<Ship> segments)
        {
            if (s_inner) return;
            s_inner = true;
            try
            {
                float? delay = GroupOf(ship)?.OutDelay;
                foreach (Ship m in segments)
                    if (m?.Renderer != null && m.Sim != null) _ = m.Renderer.AnimateFtlJumpOut(dir, null, delay);
            }
            finally { s_inner = false; }
        }

        internal static void AnimateSegmentsIn(Direction dir, IEnumerable<Ship> segments)
        {
            foreach (Ship m in segments)
                if (m?.Renderer != null && m.Sim != null) _ = m.Renderer.AnimateFtlJumpIn(dir);
        }

        internal static void EnterJumpIn(ShipRenderer r) => t_current = r;
        internal static void LeaveJumpIn() => t_current = null;

        public static Time JumpInDelay(Rand rand, Range<Time> range)
        {
            try
            {
                Group g = GroupOf(t_current?.Ship);
                if (g == null) return rand.Time(range);
                if (!g.InDelay.HasValue) g.InDelay = rand.Time(range);
                return g.InDelay.Value;
            }
            catch
            {
                return rand.Time(range);
            }
        }
    }

    [HarmonyPatch(typeof(ShipRenderer), nameof(ShipRenderer.AnimateFtlJumpOut))]
    internal static class JumpAnimOutPatch
    {
        static void Prefix(ShipRenderer __instance, Direction worldJumpDirection, Ship gateShip, ref float? forceDelay)
        {
            try
            {
                JumpAnim.OnJumpOut(__instance, worldJumpDirection, gateShip, ref forceDelay);
                Ship ship = __instance.Ship;
                if (gateShip == null && ship != null && !JumpCarry.Active && ship.Commands?.CurConfirmedCommand is FtlLocalJumpCommand)
                {
                    List<Ship> segs = LocalJumpCarry.SegmentsFor(ship);
                    if (segs.Count > 0)
                    {
                        JumpAnim.Join(new[] { ship }.Concat(segs));
                        JumpAnim.OnJumpOut(__instance, worldJumpDirection, gateShip, ref forceDelay);
                        JumpAnim.AnimateSegmentsOut(ship, worldJumpDirection, segs);
                    }
                }
            }
            catch (Exception ex) { LogThrottle.Exception("JumpAnimOutPatch", ex); }
        }
    }

    [HarmonyPatch(typeof(ShipRenderer), nameof(ShipRenderer.AnimateFtlJumpIn))]
    internal static class JumpAnimInPatch
    {
        static void Prefix(ShipRenderer __instance) => JumpAnim.EnterJumpIn(__instance);
        static void Postfix() => JumpAnim.LeaveJumpIn();
    }

    [HarmonyPatch]
    internal static class JumpAnimInDelayPatch
    {
        public const int EXPECTED = 1;
        private static int s_sites;
        public static string Summary => $"JumpAnim: {s_sites}/{EXPECTED} jump-in delay site(s) patched (segments of a ship jump in together)";

        static MethodBase TargetMethod() => AccessTools.AsyncMoveNext(AccessTools.Method(typeof(ShipRenderer), nameof(ShipRenderer.AnimateFtlJumpIn)));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            MethodInfo from = AccessTools.Method(typeof(Rand), nameof(Rand.Time), new[] { typeof(Range<Time>) });
            MethodInfo to = AccessTools.Method(typeof(JumpAnim), nameof(JumpAnim.JumpInDelay));
            int hits = 0;
            foreach (CodeInstruction ci in list)
                if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Equals(ci.operand, from) && to != null)
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = to;
                    hits++;
                }
            s_sites = hits;
            return list;
        }
    }
}
