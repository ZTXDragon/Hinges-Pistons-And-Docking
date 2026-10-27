using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Parts.Weapons;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch]
    internal static class AssemblyWeaponReadPatch
    {
        public const int EXPECTED = 3;
        private static readonly HashSet<MethodBase> s_done = new HashSet<MethodBase>();
        public static int Patched => s_done.Count;
        private static readonly List<string> s_failed = new List<string>();

        public static string Summary => $"AssemblyWeapons: {Patched}/{EXPECTED} read sites patched" + (s_failed.Count > 0 ? " -- left vanilla: " + string.Join(", ", s_failed) : "");

        private static List<MethodBase> s_targets;

        private static bool Prepare(MethodBase original) => original != null || (s_targets ??= Find()).Count > 0;

        private static IEnumerable<MethodBase> TargetMethods() => s_targets ??= Find();

        private static List<MethodBase> Find()
        {
            var found = new List<MethodBase>();
            void Add(MethodBase m, string name)
            {
                if (m != null) found.Add(m);
                else s_failed.Add(name + " (not found)");
            }
            Add(AccessTools.Method(typeof(Weapon), "GetShipTarget", new[] { typeof(bool), typeof(bool) }), "Weapon.GetShipTarget");
            Add(AccessTools.Method(typeof(TurretWeapon), "AcquireTarget"), "TurretWeapon.AcquireTarget");
            Add(AccessTools.Method(typeof(FixedWeapon), "GetEnemyPartInSights"), "FixedWeapon.GetEnemyPartInSights");
            return found;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            MethodInfo getter = AccessTools.PropertyGetter(typeof(CommandManager), "CurConfirmedCommand");
            MethodInfo replacement = AccessTools.Method(typeof(AssemblyOrders), nameof(AssemblyOrders.ForWeapons));
            var list = new List<CodeInstruction>(instructions);
            int hits = 0;
            foreach (CodeInstruction ci in list)
                if ((ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) && Equals(ci.operand, getter)) hits++;
            string name = original.DeclaringType?.Name + "." + original.Name;
            if (hits != 1 || getter == null || replacement == null)
            {
                if (!s_failed.Contains($"{name} ({hits} reads)")) s_failed.Add($"{name} ({hits} reads)");
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
            s_done.Add(original);
            return list;
        }
    }
}
