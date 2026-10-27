using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling.Timing;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class SelectionCallShims
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ClearSelectedShips(PlayerInputHandler h) => h.ClearSelectedShips();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool SelectShip(PlayerInputHandler h, Ship ship, bool playSound) => h.SelectShip(ship, playSound);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void SelectSingleShip(PlayerInputHandler h, Ship ship, bool playSound) => h.SelectSingleShip(ship, playSound);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool DeselectShip(PlayerInputHandler h, Ship ship) => h.DeselectShip(ship);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void SelectShips(PlayerInputHandler h, IEnumerable<Ship> ships, bool playSound) => h.SelectShips(ships, playSound);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool IsShipSelected(PlayerInputHandler h, Ship ship) => h.IsShipSelected(ship);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Time? GetShipSelectedTime(PlayerInputHandler h, Ship ship) => h.GetShipSelectedTime(ship);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static IEnumerable<Ship> SelectedCommandExecutableShips(PlayerInputHandler h) => h.SelectedCommandExecutableShips;

        private static Dictionary<MethodBase, MethodInfo> s_map;

        public static Dictionary<MethodBase, MethodInfo> Map()
        {
            if (s_map != null) return s_map;
            var map = new Dictionary<MethodBase, MethodInfo>();
            Type t = typeof(PlayerInputHandler);
            void Add(MethodBase original, string shim)
            {
                MethodInfo s = AccessTools.Method(typeof(SelectionCallShims), shim);
                if (original != null && s != null) map[original] = s;
            }
            Add(AccessTools.Method(t, "ClearSelectedShips"), nameof(ClearSelectedShips));
            Add(AccessTools.Method(t, "SelectShip"), nameof(SelectShip));
            Add(AccessTools.Method(t, "SelectSingleShip"), nameof(SelectSingleShip));
            Add(AccessTools.Method(t, "DeselectShip"), nameof(DeselectShip));
            Add(AccessTools.Method(t, "SelectShips"), nameof(SelectShips));
            Add(AccessTools.Method(t, "IsShipSelected"), nameof(IsShipSelected));
            Add(AccessTools.Method(t, "GetShipSelectedTime"), nameof(GetShipSelectedTime));
            Add(AccessTools.PropertyGetter(t, "SelectedCommandExecutableShips"), nameof(SelectedCommandExecutableShips));
            s_map = map;
            return map;
        }
    }

    [HarmonyPatch]
    internal static class SelectionCallShimsPatch
    {
        private static readonly Dictionary<MethodBase, int> s_sites = new Dictionary<MethodBase, int>();
        public static string Summary => $"SelectionCallShims: {s_sites.Values.Sum()} selection call(s) in {s_sites.Count} patched input method(s) kept as real calls";

        private static readonly string[] s_callers =
        {
            "InputSelectShipsAndParts", "InputCommands", "InputShipCommands", "GetCommandContext", "HandleInput", "HandleWeaponTargetContextClicked",
            "HandleSalvageContextClicked", "HandleNuggetContextClicked",
        };

        private static List<MethodBase> Targets()
        {
            var list = new List<MethodBase>();
            const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (MethodInfo m in typeof(PlayerInputHandler).GetMethods(ALL))
                if (Array.IndexOf(s_callers, m.Name) >= 0 && !m.IsGenericMethodDefinition) list.Add(m);
            return list;
        }

        private static bool Prepare(MethodBase original) => original != null || Targets().Count > 0;

        private static IEnumerable<MethodBase> TargetMethods() => Targets();

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            Dictionary<MethodBase, MethodInfo> map = SelectionCallShims.Map();
            var list = new List<CodeInstruction>(instructions);
            int hits = 0;
            foreach (CodeInstruction ci in list)
            {
                if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && ci.operand is MethodBase mb && map.TryGetValue(mb, out MethodInfo shim))
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = shim;
                    hits++;
                }
            }
            if (original != null) s_sites[original] = hits;
            return list;
        }
    }
}
