using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Input;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;
using Cosmoteer.Simulation;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class UiFanout
    {
        public static List<ulong> ExpandDistinct(IList<ulong> selected, IReadOnlyDictionary<ulong, ulong[]> assemblyOf)
        {
            var result = new List<ulong>();
            var seen = new HashSet<ulong>();
            foreach (ulong s in selected)
            {
                if (seen.Add(s)) result.Add(s);
                if (assemblyOf != null && assemblyOf.TryGetValue(s, out ulong[] members))
                    foreach (ulong m in members)
                        if (seen.Add(m)) result.Add(m);
            }
            return result;
        }

        public static bool IsOwnSegment(ulong[] members, ulong shooter, ulong target)
        {
            if (members == null || shooter == target) return false;
            return Array.IndexOf(members, shooter) >= 0 && Array.IndexOf(members, target) >= 0;
        }

        public static bool IsOwnSegmentShip(Ship shooter, Ship target)
        {
            if (shooter == null || target == null || ReferenceEquals(shooter, target) || HingeJointManager.ActivePinCount == 0) return false;
            Ship[] members = AssemblyIndex.AssemblyOf(shooter);
            if (members == null) return false;
            return Array.IndexOf(members, target) >= 0;
        }

        public static List<Ship> MembersFor(Ship ship)
        {
            var list = new List<Ship>();
            if (ship == null) return list;
            list.Add(ship);
            if (HingeJointManager.ActivePinCount == 0) return list;
            Ship[] members = AssemblyIndex.AssemblyOf(ship);
            if (members == null) return list;
            int owner = ship.Metadata?.PlayerIndex ?? int.MinValue;
            foreach (Ship m in members)
            {
                if (m == null || ReferenceEquals(m, ship) || m.Commands == null) continue;
                if ((m.Metadata?.PlayerIndex ?? int.MaxValue) != owner || !m.Commands.CanExecuteCommandsFromLocalPlayer) continue;
                list.Add(m);
            }
            return list;
        }

        public static List<Ship> Expand(IEnumerable<Ship> selected)
        {
            var result = new List<Ship>();
            var seen = new HashSet<Ship>();
            foreach (Ship s in selected)
                foreach (Ship m in MembersFor(s))
                    if (seen.Add(m)) result.Add(m);
            return result;
        }

        private static void AddDistinct<T>(ICollection<T> into, List<T> found)
        {
            foreach (T t in found)
                if (!into.Contains(t)) into.Add(t);
        }

        public static void TogglesAcross(Ship ship, ID<PartToggleGuiRules> id, ICollection<PartUIToggle> into)
        {
            var found = new List<PartUIToggle>();
            foreach (Ship m in MembersFor(ship)) PartUIToggle.GetAllOnShip(m, id, found);
            AddDistinct(into, found);
        }

        public static void TriggersAcross(Ship ship, ID<PartTriggerGuiRules> id, ICollection<PartUITrigger> into)
        {
            var found = new List<PartUITrigger>();
            foreach (Ship m in MembersFor(ship)) PartUITrigger.GetAllOnShip(m, id, found);
            AddDistinct(into, found);
        }

        public static void TargetersAcross(Ship ship, ID<PartTargeterGuiRules> id, ICollection<PartUITargeter> into)
        {
            var found = new List<PartUITargeter>();
            foreach (Ship m in MembersFor(ship)) PartUITargeter.GetAllOnShip(m, id, found);
            AddDistinct(into, found);
        }

        public static void ColorsAcross(Ship ship, ID<PartColorGuiRules> id, ICollection<PartUIColor> into)
        {
            var found = new List<PartUIColor>();
            foreach (Ship m in MembersFor(ship)) PartUIColor.GetAllOnShip(m, id, found);
            AddDistinct(into, found);
        }

        public static IEnumerable<Part> MatchingAcross(CommonBasePartsManager<Part> parts, Part selected, bool dontCheckRotationFlip, bool exactType)
        {
            var result = new List<Part>();
            foreach (Ship m in MembersFor(selected?.Ship))
                result.AddRange(m.Parts.GetPartsMatchingSelection(selected, dontCheckRotationFlip, exactType));
            if (result.Count == 0 && parts != null && selected?.Ship == null)
                result.AddRange(parts.GetPartsMatchingSelection(selected, dontCheckRotationFlip, exactType));
            return result;
        }
    }

    [HarmonyPatch]
    internal static class UiFanoutButtonSitesPatch
    {
        public const int EXPECTED = 11;
        private static readonly Dictionary<MethodBase, int> s_sites = new Dictionary<MethodBase, int>();
        public static int Patched => s_sites.Values.Sum();
        public static string Summary => $"UiFanout: {Patched}/{EXPECTED} part-button call sites patched";

        private static readonly (Type Owner, string Replacement)[] s_helpers =
        {
            (typeof(PartUIToggle), nameof(UiFanout.TogglesAcross)),
            (typeof(PartUITrigger), nameof(UiFanout.TriggersAcross)),
            (typeof(PartUITargeter), nameof(UiFanout.TargetersAcross)),
            (typeof(PartUIColor), nameof(UiFanout.ColorsAcross)),
        };

        private static List<MethodBase> s_targets;

        private static bool Prepare(MethodBase original) => original != null || (s_targets ??= Find()).Count > 0;

        private static IEnumerable<MethodBase> TargetMethods() => s_targets ??= Find();

        private static MethodInfo Replacement(object operand)
        {
            if (!(operand is MethodInfo mi) || mi.Name != "GetAllOnShip") return null;
            foreach ((Type owner, string repl) in s_helpers)
                if (mi.DeclaringType == owner && mi.GetParameters().Length == 3)
                    return AccessTools.Method(typeof(UiFanout), repl);
            return null;
        }

        private static List<MethodBase> Find()
        {
            var found = new List<MethodBase>();
            var types = new List<Type> { typeof(BasePartToggleButton), typeof(PartTriggerButton), typeof(PartTargeterButton), typeof(PartTargeterCancelButton), typeof(PartColorPickerButton) };
            for (int i = 0; i < types.Count; i++)
                foreach (Type nested in types[i].GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                    types.Add(nested);
            foreach (Type t in types)
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (m.IsAbstract || m.ContainsGenericParameters) continue;
                    try
                    {
                        foreach (CodeInstruction ci in PatchProcessor.GetOriginalInstructions(m))
                            if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Replacement(ci.operand) != null) { found.Add(m); break; }
                    }
                    catch
                    {
                    }
                }
            if (found.Count == 0) Log.Error("UiFanout: no part-button GetAllOnShip call site found -- ship-wide part buttons reach the root only.");
            return found;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var list = new List<CodeInstruction>(instructions);
            int hits = 0;
            foreach (CodeInstruction ci in list)
            {
                if (!(ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt)) continue;
                MethodInfo repl = Replacement(ci.operand);
                if (repl == null) continue;
                ci.opcode = OpCodes.Call;
                ci.operand = repl;
                hits++;
            }
            if (original != null) s_sites[original] = hits;
            return list;
        }
    }

    [HarmonyPatch(typeof(PlayerInputHandler), "InputSelectShipsAndParts")]
    internal static class UiFanoutSameTypePatch
    {
        public static int Patched;
        public static string Summary => $"UiFanout: {Patched}/1 select-same-type call site patched";

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            MethodInfo repl = AccessTools.Method(typeof(UiFanout), nameof(UiFanout.MatchingAcross));
            var sites = new List<CodeInstruction>();
            foreach (CodeInstruction ci in list)
                if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && ci.operand is MethodInfo mi
                    && mi.Name == "GetPartsMatchingSelection" && mi.DeclaringType == typeof(CommonBasePartsManager<Part>))
                    sites.Add(ci);
            if (sites.Count != 1 || repl == null)
            {
                Patched = 0;
                Log.Error($"UiFanout: found {sites.Count} GetPartsMatchingSelection calls in InputSelectShipsAndParts, expected 1 -- left vanilla.");
                return list;
            }
            sites[0].opcode = OpCodes.Call;
            sites[0].operand = repl;
            Patched = 1;
            return list;
        }
    }

    [HarmonyPatch(typeof(PlayerInputHandler), nameof(PlayerInputHandler.HandleInput))]
    internal static class UiFanoutPartHotkeysPatch
    {
        static void Postfix(PlayerInputHandler __instance)
        {
            try
            {
                if (HingeJointManager.ActivePinCount == 0 || __instance.SelectedShips.Count == 0) return;
                bool allowed = __instance.IsPlayerControlEnabled
                    || (__instance.Game?.Gui?.GetSelectionState()?.AllowPartSelectionHotkeys ?? false);
                if (!allowed) return;
                List<Ship> selected = __instance.SelectedShips.ToList();
                List<Ship> expanded = UiFanout.Expand(selected);
                if (expanded.Count == selected.Count) return;
                var extra = expanded.Where(s => !selected.Contains(s)).ToList();

                if (Inputs.Game.SelectAllParts.WasPressed)
                    foreach (Ship m in extra)
                        foreach (Part p in m.Parts)
                            __instance.SelectPart(p);

                for (int g = 0; g < Inputs.ControlGroups.SelectPartGroup.Length; g++)
                {
                    if (!Inputs.ControlGroups.SelectPartGroup[g].WasPressed) continue;
                    var parts = new List<Part>();
                    foreach (Ship m in extra) m.Metadata.GetPartsInControlGroup(g, parts);
                    if (parts.Count == 0) continue;
                    var own = new List<Part>();
                    foreach (Ship s in selected) s.Metadata.GetPartsInControlGroup(g, own);
                    if (own.Count == 0 && !Inputs.Game.AppendSelection) __instance.ClearSelectedParts();
                    __instance.SelectParts(parts, playSound: own.Count == 0);
                }

                if (__instance.SelectedParts.Count > 0 && !Inputs.Game.AppendSelection)
                {
                    for (int g = 0; g < Inputs.ControlGroups.CreatePartGroup.Length; g++)
                    {
                        if (!Inputs.ControlGroups.CreatePartGroup[g].WasPressed) continue;
                        var withParts = new HashSet<Ship>(__instance.SelectedParts.Select(p => p.Ship));
                        foreach (Ship m in expanded)
                            if (!withParts.Contains(m) && m.Metadata.PartControlGroups.ContainsKey(g))
                                __instance.Game.NetManager.EnqueueInput(new MPSetPartGroupInput(m, g));
                    }
                }
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("UiFanoutPartHotkeysPatch", ex);
            }
        }
    }

    [HarmonyPatch(typeof(CommandManager), nameof(CommandManager.CreateCommand))]
    internal static class OwnSegmentCommandPatch
    {
        static bool Prefix(CommandManager __instance, CommandContext context, ref Command __result)
        {
            try
            {
                if (context is CommandContext.ShipContext sc && UiFanout.IsOwnSegmentShip(__instance.Ship, sc.Ship))
                {
                    __result = null;
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("OwnSegmentCommandPatch", ex);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(PlayerInputHandler), nameof(PlayerInputHandler.HandleWeaponTargetContextClicked))]
    internal static class OwnSegmentPartTargetPatch
    {
        static bool Prefix(PlayerInputHandler __instance, Part targetPart)
        {
            try
            {
                Ship target = targetPart?.Ship;
                if (target == null) return true;
                foreach (Ship s in __instance.SelectedShips)
                    if (UiFanout.IsOwnSegmentShip(s, target)) return false;
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("OwnSegmentPartTargetPatch", ex);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ShipsCard), "GetShipsToDisplay")]
    internal static class UiFanoutShipsCardPatch
    {
        static void Postfix(List<Ship> ships)
        {
            try
            {
                if (ships == null || ships.Count < 2 || HingeJointManager.ActivePinCount == 0) return;
                var result = new List<Ship>(ships.Count);
                foreach (Ship s in ships)
                {
                    Ship root = AssemblyFacade.Redirect(s);
                    if (!result.Contains(root)) result.Add(root);
                }
                if (result.Count == ships.Count) return;
                ships.Clear();
                ships.AddRange(result);
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("UiFanoutShipsCardPatch", ex);
            }
        }
    }
}
