using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch]
    internal static class AssemblyCommandSelectionPatch
    {
        public const int EXPECTED = 3;
        private static readonly Dictionary<string, int> s_expectedPer = new Dictionary<string, int> { { "InputCommands", 2 }, { "GetCommandContext", 1 } };
        private static readonly Dictionary<string, string[]> s_aliases = new Dictionary<string, string[]> { { "InputCommands", new[] { "InputShipCommands" } } };
        private static readonly Dictionary<MethodBase, int> s_sites = new Dictionary<MethodBase, int>();
        public static int Patched => s_sites.Values.Sum();

        public static string Summary => $"AssemblyCommandSelection: {Patched}/{EXPECTED} right-click selection reads patched";

        private static MethodInfo Getter => AccessTools.PropertyGetter(typeof(PlayerInputHandler), nameof(PlayerInputHandler.SelectedShips));

        private static List<MethodBase> Targets()
        {
            var list = new List<MethodBase>();
            foreach (string name in s_expectedPer.Keys)
            {
                MethodBase m = AccessTools.Method(typeof(PlayerInputHandler), name);
                if (m == null && s_aliases.TryGetValue(name, out string[] older))
                    foreach (string alt in older)
                        if ((m = AccessTools.Method(typeof(PlayerInputHandler), alt)) != null) break;
                if (m != null) list.Add(m);
                else Log.Error("AssemblyCommandSelection: PlayerInputHandler." + name + " not found -- that read is left vanilla.");
            }
            return list;
        }

        private static bool Prepare(MethodBase original) => original != null || (Getter != null && Targets().Count > 0);

        private static IEnumerable<MethodBase> TargetMethods() => Targets();

        private static string CanonicalName(string name)
        {
            foreach (var kv in s_aliases)
                if (Array.IndexOf(kv.Value, name) >= 0) return kv.Key;
            return name;
        }

        public static IEnumerable<Ship> ShipsForCommands(PlayerInputHandler input)
        {
            var selected = input.SelectedShips;
            try
            {
                if (!AssemblyFacade.Enabled || HingeJointManager.ActivePinCount == 0) return selected;
                return selected.Select(AssemblyFacade.Redirect).Distinct().ToList();
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("AssemblyCommandSelectionPatch.ShipsForCommands", ex);
                return selected;
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            MethodInfo getter = Getter;
            MethodInfo replacement = AccessTools.Method(typeof(AssemblyCommandSelectionPatch), nameof(ShipsForCommands));
            var list = new List<CodeInstruction>(instructions);
            int hits = 0;
            foreach (CodeInstruction ci in list)
                if ((ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) && Equals(ci.operand, getter)) hits++;
            int expected = original != null && s_expectedPer.TryGetValue(CanonicalName(original.Name), out int e) ? e : -1;
            if (hits != expected || replacement == null)
            {
                if (original != null) s_sites[original] = 0;
                Log.Error($"AssemblyCommandSelection: found {hits} SelectedShips reads in {original?.Name}, expected {expected} -- left vanilla.");
                return list;
            }
            foreach (CodeInstruction ci in list)
            {
                if ((ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) && Equals(ci.operand, getter))
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = replacement;
                }
            }
            if (original != null) s_sites[original] = hits;
            return list;
        }
    }
}
