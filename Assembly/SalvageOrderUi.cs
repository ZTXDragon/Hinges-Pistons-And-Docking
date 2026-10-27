using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts.Weapons;
using Cosmoteer.Simulation;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch]
    internal static class SalvageOrderUiPatch
    {
        public const int EXPECTED = 17;
        private static readonly Dictionary<string, int> s_expectedPer = new Dictionary<string, int>
        {
            { "GetCommandContext", 2 }, { "HandleSalvageContextClicked(Ship)", 2 }, { "HandleSalvageContextClicked(Part)", 6 },
            { "HandleNuggetContextClicked", 4 }, { "lambda", 1 }, { "nugget local", 1 },
        };
        private static readonly Dictionary<MethodBase, int> s_sites = new Dictionary<MethodBase, int>();
        public static string Summary => $"SalvageOrderUi: {s_sites.Values.Sum()}/{EXPECTED} salvage / nugget click reads patched";

        private static MethodInfo CrewGetter => AccessTools.PropertyGetter(typeof(PlayerInputHandler), nameof(PlayerInputHandler.SelectedCrewCommandableShips));
        private static MethodInfo SalvageWeaponsGetter => AccessTools.PropertyGetter(typeof(WeaponManager), nameof(WeaponManager.HasOperationalSalvageWeapons));

        private static string Key(MethodBase m)
        {
            if (m.Name.Contains("<HandleNuggetContextClicked>g__")) return "nugget local";
            if (m.DeclaringType != typeof(PlayerInputHandler)) return "lambda";
            if (m.Name != "HandleSalvageContextClicked") return m.Name;
            ParameterInfo[] p = m.GetParameters();
            return p.Length > 0 && p[0].ParameterType == typeof(Ship) ? "HandleSalvageContextClicked(Ship)" : "HandleSalvageContextClicked(Part)";
        }

        private static int Count(MethodBase m, MethodInfo getter)
        {
            try
            {
                return PatchProcessor.GetOriginalInstructions(m).Count(ci => (ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Equals(ci.operand, getter));
            }
            catch
            {
                return 0;
            }
        }

        private static List<MethodBase> Targets()
        {
            var list = new List<MethodBase>();
            const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (MethodInfo m in typeof(PlayerInputHandler).GetMethods(ALL))
                if (m.Name == "GetCommandContext" || m.Name == "HandleSalvageContextClicked" || m.Name == "HandleNuggetContextClicked")
                    list.Add(m);
            MethodInfo sw = SalvageWeaponsGetter;
            if (sw != null)
                foreach (Type nt in typeof(PlayerInputHandler).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                    foreach (MethodInfo m in nt.GetMethods(ALL))
                        if (!m.IsGenericMethodDefinition && !m.ContainsGenericParameters
                            && (Count(m, sw) > 0 || (m.Name.Contains("<HandleNuggetContextClicked>g__") && Count(m, CrewGetter) > 0))) list.Add(m);
            return list;
        }

        private static bool Prepare(MethodBase original) => original != null || (CrewGetter != null && Targets().Count > 0);

        private static IEnumerable<MethodBase> TargetMethods() => Targets();

        public static IEnumerable<Ship> CrewShipsForOrders(PlayerInputHandler input)
        {
            IEnumerable<Ship> vanilla = input.SelectedCrewCommandableShips;
            try
            {
                if (!AssemblyFacade.Enabled || HingeJointManager.ActivePinCount == 0) return vanilla;
                return input.SelectedShips.Select(AssemblyFacade.Redirect).Distinct()
                    .Where(s => s != null && s.Commands.CanExecuteCrewCommandsFromLocalPlayer).ToList();
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("SalvageOrderUiPatch.CrewShipsForOrders", ex);
                return vanilla;
            }
        }

        public static bool ShipHasSalvageWeapons(WeaponManager weapons)
        {
            if (weapons.HasOperationalSalvageWeapons) return true;
            try
            {
                if (HingeJointManager.ActivePinCount == 0) return false;
                Ship[] members = AssemblyIndex.AssemblyOf(weapons.Ship);
                if (members == null) return false;
                foreach (Ship m in members)
                    if (m != null && !ReferenceEquals(m, weapons.Ship) && m.Weapons != null && m.Weapons.HasOperationalSalvageWeapons) return true;
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("SalvageOrderUiPatch.ShipHasSalvageWeapons", ex);
            }
            return false;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var list = new List<CodeInstruction>(instructions);
            if (original == null) return list;
            string key = Key(original);
            bool lambda = key == "lambda";
            bool nuggets = key == "nugget local" || key == "HandleNuggetContextClicked";
            MethodInfo from = lambda ? SalvageWeaponsGetter : CrewGetter;
            MethodInfo to = lambda ? AccessTools.Method(typeof(SalvageOrderUiPatch), nameof(ShipHasSalvageWeapons))
                : nuggets ? AccessTools.Method(typeof(AssemblyStorage), nameof(AssemblyStorage.SinkShips))
                : AccessTools.Method(typeof(SalvageOrderUiPatch), nameof(CrewShipsForOrders));
            int hits = list.Count(ci => (ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Equals(ci.operand, from));
            int expected = s_expectedPer.TryGetValue(key, out int e) ? e : -1;
            if (from == null || to == null || hits != expected)
            {
                s_sites[original] = 0;
                Log.Error($"SalvageOrderUi: found {hits} reads in {original.DeclaringType?.Name}.{original.Name}, expected {expected} -- left vanilla.");
                return list;
            }
            foreach (CodeInstruction ci in list)
            {
                if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Equals(ci.operand, from))
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = to;
                }
            }
            s_sites[original] = hits;
            return list;
        }
    }
}
