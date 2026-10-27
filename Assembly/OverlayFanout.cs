using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Cosmoteer.Modes.Creative.Gui;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling.Timing;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch]
    internal static class OverlayFanoutPatch
    {
        public const int EXPECTED_OVERLAY = 3;
        public const int EXPECTED_AI_MENU = 4;
        private static readonly Dictionary<MethodBase, int> s_overlay = new Dictionary<MethodBase, int>();
        private static readonly Dictionary<MethodBase, int> s_aiMenu = new Dictionary<MethodBase, int>();
        public static string Summary =>
            $"OverlayFanout: {s_overlay.Values.Sum()}/{EXPECTED_OVERLAY} overlay reads, {s_aiMenu.Values.Sum()}/{EXPECTED_AI_MENU} AI menu reads patched";

        private static readonly string[] s_overlayMethods = { "DrawWeaponTargetOverlays", "DrawPartComponentUnderlays", "DrawPartComponentOverlays" };

        private static MethodInfo Getter => AccessTools.PropertyGetter(typeof(PlayerInputHandler), nameof(PlayerInputHandler.SelectedShips));

        private static int Count(MethodBase m)
        {
            try
            {
                MethodInfo g = Getter;
                return PatchProcessor.GetOriginalInstructions(m).Count(ci => (ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Equals(ci.operand, g));
            }
            catch
            {
                return 0;
            }
        }

        private static bool IsAiMenu(MethodBase m)
        {
            Type t = m.DeclaringType;
            return t == typeof(ShipAiMenu) || t?.DeclaringType == typeof(ShipAiMenu);
        }

        private static List<MethodBase> Targets()
        {
            var list = new List<MethodBase>();
            if (Getter == null) return list;
            foreach (string n in s_overlayMethods)
            {
                MethodBase m = AccessTools.Method(typeof(SimOverlayRenderer), n);
                if (m != null) list.Add(m);
                else Log.Error("OverlayFanout: SimOverlayRenderer." + n + " not found -- left vanilla.");
            }
            const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (Type t in new[] { typeof(ShipAiMenu) }.Concat(typeof(ShipAiMenu).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)))
                foreach (MethodInfo m in t.GetMethods(ALL))
                    if (!m.IsAbstract && !m.ContainsGenericParameters && Count(m) > 0) list.Add(m);
            return list;
        }

        private static bool Prepare(MethodBase original) => original != null || Targets().Count > 0;

        private static IEnumerable<MethodBase> TargetMethods() => Targets();

        public static Dictionary<Ship, Time>.KeyCollection ExpandedSelection(PlayerInputHandler input)
        {
            Dictionary<Ship, Time>.KeyCollection vanilla = input.SelectedShips;
            try
            {
                if (!AssemblyFacade.Enabled || HingeJointManager.ActivePinCount == 0) return vanilla;
                var d = new Dictionary<Ship, Time>();
                foreach (Ship s in UiFanout.Expand(vanilla))
                    if (s != null && !d.ContainsKey(s)) d.Add(s, input._selectedShips.TryGetValue(s, out Time t) ? t : default(Time));
                return d.Keys;
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("OverlayFanout.ExpandedSelection", ex);
                return vanilla;
            }
        }

        public static Dictionary<Ship, Time>.KeyCollection RootSelection(PlayerInputHandler input)
        {
            Dictionary<Ship, Time>.KeyCollection vanilla = input.SelectedShips;
            try
            {
                if (!AssemblyFacade.Enabled || HingeJointManager.ActivePinCount == 0) return vanilla;
                var d = new Dictionary<Ship, Time>();
                foreach (Ship s in vanilla)
                {
                    Ship root = AssemblyFacade.Redirect(s);
                    if (root != null && !d.ContainsKey(root)) d.Add(root, input._selectedShips.TryGetValue(s, out Time t) ? t : default(Time));
                }
                return d.Keys;
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("OverlayFanout.RootSelection", ex);
                return vanilla;
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var list = new List<CodeInstruction>(instructions);
            if (original == null) return list;
            bool ai = IsAiMenu(original);
            MethodInfo from = Getter;
            MethodInfo to = AccessTools.Method(typeof(OverlayFanoutPatch), ai ? nameof(RootSelection) : nameof(ExpandedSelection));
            int hits = 0;
            foreach (CodeInstruction ci in list)
            {
                if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Equals(ci.operand, from) && to != null)
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = to;
                    hits++;
                }
            }
            if (ai) s_aiMenu[original] = hits;
            else s_overlay[original] = hits;
            return list;
        }
    }
}
