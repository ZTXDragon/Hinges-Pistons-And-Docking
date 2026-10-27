using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Cosmoteer.Game.Gui;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts.Weapons;
using Cosmoteer.Simulation;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class AssemblyTargetingFanout
    {
        [ThreadStatic] private static int t_depth;
        [ThreadStatic] private static Dictionary<Ship, Ship> t_appendedBy;
        [ThreadStatic] private static Dictionary<Ship, List<Weapon>> t_extra;

        internal static void Enter()
        {
            if (t_depth++ == 0)
            {
                (t_appendedBy ??= new Dictionary<Ship, Ship>()).Clear();
                (t_extra ??= new Dictionary<Ship, List<Weapon>>()).Clear();
            }
        }

        internal static void Exit()
        {
            if (t_depth > 0 && --t_depth == 0)
            {
                t_appendedBy?.Clear();
                t_extra?.Clear();
            }
        }

        private static List<Ship> UiGroupOf(Ship ship)
        {
            Ship root = AssemblyFacade.RootOf(ship);
            if (root == null) return null;
            var group = new List<Ship>();
            HingeJointManager.GetAssemblyShips(ship, group, identity: true);
            group.RemoveAll(m => !ReferenceEquals(m, ship) && (m.Commands == null || !m.Commands.CanExecuteCommandsFromLocalPlayer));
            return group.Count < 2 ? null : group;
        }

        public static IEnumerable<Weapon> ShipWideAcross(WeaponManager weapons, bool confirmed)
        {
            IEnumerable<Weapon> own = weapons.GetShipWideTargetableWeapons(confirmed);
            try
            {
                return Widen(weapons.Ship, confirmed, own);
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("AssemblyTargetingFanout.ShipWideAcross", ex);
                return own;
            }
        }

        internal static IEnumerable<Weapon> Widen(Ship ship, bool confirmed, IEnumerable<Weapon> own)
        {
            if (t_depth == 0 || ship == null) return own;
            List<Ship> members = UiGroupOf(ship);
            if (members == null)
            {
                if (t_appendedBy.TryGetValue(ship, out Ship by) && !ReferenceEquals(by, ship)) return Enumerable.Empty<Weapon>();
                t_appendedBy[ship] = ship;
                return own;
            }
            if (t_appendedBy.TryGetValue(ship, out Ship owner) && !ReferenceEquals(owner, ship))
                return Enumerable.Empty<Weapon>();
            if (!t_extra.TryGetValue(ship, out List<Weapon> extra))
            {
                extra = new List<Weapon>();
                t_extra[ship] = extra;
                t_appendedBy[ship] = ship;
                foreach (Ship m in members)
                {
                    if (ReferenceEquals(m, ship) || t_appendedBy.ContainsKey(m)) continue;
                    t_appendedBy[m] = ship;
                    if (m.Weapons == null) continue;
                    extra.AddRange(m.Weapons.GetShipWideTargetableWeapons(confirmed));
                }
            }
            return extra.Count == 0 ? own : own.Concat(extra);
        }

        public static bool AnyDisplayedAcross(WeaponManager weapons)
        {
            if (weapons == null) return false;
            if (weapons.HasExplicitTargetsDisplayed) return true;
            Ship root = AssemblyFacade.Redirect(weapons.Ship);
            List<Ship> members = UiGroupOf(root);
            if (members == null) return false;
            foreach (Ship m in members)
                if (!ReferenceEquals(m, weapons.Ship) && m.Weapons != null && m.Weapons.HasExplicitTargetsDisplayed) return true;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class AssemblyTargetingScopePatch
    {
        private static List<MethodBase> s_targets;

        private static bool Prepare(MethodBase original) => original != null || (s_targets ??= Find()).Count > 0;

        private static IEnumerable<MethodBase> TargetMethods() => s_targets ??= Find();

        private static List<MethodBase> Find()
        {
            var found = new List<MethodBase>();
            void Add(MethodBase m, string name)
            {
                if (m != null) found.Add(m);
                else Log.Error("AssemblyTargeting: UI handler not found, its segment fan-out is off: " + name);
            }
            Add(AccessTools.Method(typeof(PlayerInputHandler), "HandleWeaponTargetContextClicked"), "PlayerInputHandler.HandleWeaponTargetContextClicked");
            Add(AccessTools.Method(typeof(Miniview), "DrawMiniviewOverlays"), "Miniview.DrawMiniviewOverlays");
            Add(AccessTools.Method(typeof(Miniview), "OnMouseInside"), "Miniview.OnMouseInside");
            Add(AccessTools.Method(typeof(SimOverlayRenderer), "DrawWeaponTargetContextOverlay"), "SimOverlayRenderer.DrawWeaponTargetContextOverlay");
            Add(AccessTools.Method(typeof(ShipsCard), "OnHaltClicked"), "ShipsCard.OnHaltClicked");
            return found;
        }

        private static void Prefix()
        {
            AssemblyTargetingFanout.Enter();
        }

        private static Exception Finalizer(Exception __exception)
        {
            AssemblyTargetingFanout.Exit();
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class AssemblyShipWideSitesPatch
    {
        public const int EXPECTED = 5;
        private static readonly Dictionary<MethodBase, int> s_sites = new Dictionary<MethodBase, int>();
        public static int Patched => s_sites.Values.Sum();

        public static string Summary => $"AssemblyShipWide: {Patched}/{EXPECTED} weapon-list call sites patched";

        private static MethodInfo Target => AccessTools.Method(typeof(WeaponManager), nameof(WeaponManager.GetShipWideTargetableWeapons));

        private static List<MethodBase> s_targets;

        private static bool Prepare(MethodBase original) => original != null || (s_targets ??= Find()).Count > 0;

        private static IEnumerable<MethodBase> TargetMethods() => s_targets ??= Find();

        private static List<MethodBase> Find()
        {
            var found = new List<MethodBase>();
            MethodInfo target = Target;
            var types = new List<Type> { typeof(PlayerInputHandler), typeof(Miniview), typeof(SimOverlayRenderer), typeof(ShipsCard) };
            for (int i = 0; i < types.Count; i++)
                foreach (Type nested in types[i].GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                    types.Add(nested);
            foreach (Type t in types)
            {
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (m.IsAbstract || m.ContainsGenericParameters) continue;
                    try
                    {
                        foreach (CodeInstruction ci in PatchProcessor.GetOriginalInstructions(m))
                        {
                            if ((ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) && Equals(ci.operand, target))
                            {
                                found.Add(m);
                                break;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            if (found.Count == 0) Log.Error("AssemblyShipWide: no weapon-list call site found -- part targets do not reach segment weapons.");
            return found;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            MethodInfo target = Target;
            MethodInfo replacement = AccessTools.Method(typeof(AssemblyTargetingFanout), nameof(AssemblyTargetingFanout.ShipWideAcross));
            var list = new List<CodeInstruction>(instructions);
            int hits = 0;
            foreach (CodeInstruction ci in list)
            {
                if ((ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) && Equals(ci.operand, target) && replacement != null)
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = replacement;
                    hits++;
                }
            }
            if (original != null) s_sites[original] = hits;
            return list;
        }
    }

    [HarmonyPatch]
    internal static class AssemblyHaltSitesPatch
    {
        public const int EXPECTED = 2;
        private static readonly Dictionary<MethodBase, int> s_sites = new Dictionary<MethodBase, int>();
        public static int Patched => s_sites.Values.Sum();

        public static string Summary => $"AssemblyHalt: {Patched}/{EXPECTED} Halt sites patched";

        private static MethodInfo Getter => AccessTools.PropertyGetter(typeof(WeaponManager), nameof(WeaponManager.HasExplicitTargetsDisplayed));

        private static List<MethodBase> s_targets;

        private static bool Prepare(MethodBase original) => original != null || (s_targets ??= Find()).Count > 0;

        private static IEnumerable<MethodBase> TargetMethods() => s_targets ??= Find();

        private static List<MethodBase> Find()
        {
            var found = new List<MethodBase>();
            MethodInfo getter = Getter;
            var types = new List<Type> { typeof(ShipsCard) };
            for (int i = 0; i < types.Count; i++)
                foreach (Type nested in types[i].GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                    types.Add(nested);
            foreach (Type t in types)
            {
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (m.IsAbstract || m.ContainsGenericParameters) continue;
                    try
                    {
                        foreach (CodeInstruction ci in PatchProcessor.GetOriginalInstructions(m))
                        {
                            if ((ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) && Equals(ci.operand, getter))
                            {
                                found.Add(m);
                                break;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            if (found.Count == 0) Log.Error("AssemblyHalt: no HasExplicitTargetsDisplayed read found in ShipsCard -- Halt does not reach segment weapons.");
            return found;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            MethodInfo getter = Getter;
            MethodInfo replacement = AccessTools.Method(typeof(AssemblyTargetingFanout), nameof(AssemblyTargetingFanout.AnyDisplayedAcross));
            var list = new List<CodeInstruction>(instructions);
            int hits = 0;
            foreach (CodeInstruction ci in list)
            {
                if ((ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) && Equals(ci.operand, getter) && replacement != null)
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = replacement;
                    hits++;
                }
            }
            if (original != null) s_sites[original] = hits;
            return list;
        }
    }
}
