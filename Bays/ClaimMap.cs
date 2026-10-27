using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui.Build;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Blueprints.Graphics;
using Cosmoteer.Simulation.Overlays;
using Cosmoteer.Modes;
using Cosmoteer.Ships.Parts;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class ClaimMap
    {
        public static bool Refuses(int left, int top, int width, int height, HashSet<(int, int)> claimed, bool repairInPlace, bool rod)
        {
            if (repairInPlace || rod || claimed == null || claimed.Count == 0) return false;
            for (int x = left; x < left + width; x++)
                for (int y = top; y < top + height; y++)
                    if (claimed.Contains((x, y))) return true;
            return false;
        }

        public static HashSet<(int, int)> ClaimsOn(Ship ship)
        {
            var claims = new HashSet<(int, int)>();
            if (ship == null || HingeJointManager.ActivePinCount == 0 && !HasMergedPiston(ship)) return claims;
            Ship[] segs = AssemblyAi.SegmentsOf(ship);
            if (segs != null)
            {
                foreach (Ship m in segs)
                {
                    if (m == null || ReferenceEquals(m, ship)) continue;
                    foreach (Part p in m.Parts)
                        if (!MergeRebuild.IsRodRules(p.Rules)) AddRect(claims, p.Rules.GetRect(p.Location, p.Rotation));
                    foreach (BlueprintPart bp in m.BlueprintParts)
                        if (!MergeRebuild.IsRodRules(bp.Rules)) AddRect(claims, bp.Rules.GetRect(bp.Location, bp.Rotation));
                    AddMergedHomes(claims, m);
                }
            }
            AddMergedHomes(claims, ship);
            return claims;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Ship, Cached> s_ui = new System.Runtime.CompilerServices.ConditionalWeakTable<Ship, Cached>();

        private sealed class Cached
        {
            public (int, int) Key = (int.MinValue, -1);
            public HashSet<(int, int)> Claims = new HashSet<(int, int)>();
        }

        public static HashSet<(int, int)> CachedClaims(Ship ship)
        {
            if (ship == null) return new HashSet<(int, int)>();
            Ship[] segs = AssemblyAi.SegmentsOf(ship);
            int count = segs == null ? 0 : segs.Sum(m => m.Parts.Count + m.BlueprintParts.Count);
            var key = (ship.Sim?.Tick ?? 0, count + ship.Parts.Count * 7919);
            Cached c = s_ui.GetValue(ship, _ => new Cached());
            if (c.Key != key)
            {
                c.Claims = ClaimsOn(ship);
                c.Key = key;
            }
            return c.Claims;
        }

        private static void AddRect(HashSet<(int, int)> into, IntRect r)
        {
            for (int x = r.Left; x < r.Right; x++)
                for (int y = r.Top; y < r.Bottom; y++)
                    into.Add((x, y));
        }

        private static IEnumerable<ZtxHingeBaseComponent> BasesOf(Ship ship)
        {
            HingeShipManager mgr = HingeShipManager.TryGet(ship);
            return mgr == null ? Enumerable.Empty<ZtxHingeBaseComponent>() : mgr.Bases;
        }

        private static bool HasMergedPiston(Ship ship) =>
            BasesOf(ship).Any(b => b is ZtxPistonBaseComponent && b.Part != null && MergeStore.ReadState(b.Part) == MergeState.Merged && MergeStore.ReadSlide(b.Part) > 0);

        private static void AddMergedHomes(HashSet<(int, int)> into, Ship ship)
        {
            foreach (ZtxHingeBaseComponent b in BasesOf(ship))
            {
                if (!(b is ZtxPistonBaseComponent) || b.Part == null) continue;
                if (MergeStore.ReadState(b.Part) != MergeState.Merged || MergeStore.ReadSlide(b.Part) <= 0) continue;
                MergeRecord rec = MergeStore.Read(b.Part);
                foreach (IntVector2 c in MergeRebuild.HomeCells(b.Part, rec))
                    if (b.Part.Ship.Parts[c, PartRectType.Normal] == null) into.Add((c.X, c.Y));
            }
        }

        public static string Violation(ShipUpdateInfo sui)
        {
            Ship ship = sui.Ship;
            HashSet<(int, int)> claims = ClaimsOn(ship);
            if (claims.Count == 0) return null;
            foreach (PartInfo p in sui.PartsToAdd.Concat(sui.BlueprintPartsToAdd))
            {
                IntRect r = p.Rect;
                bool repair = ship.DestroyedParts != null && ship.DestroyedParts.HasPartAt(r);
                if (Refuses(r.Left, r.Top, r.Width, r.Height, claims, repair, MergeRebuild.IsRodRules(p.Rules)))
                    return $"{p.Rules.ID} at {p.Location}";
            }
            return null;
        }

        public static bool CanApply(ShipUpdateInfo sui, ConstructionMode constructionMode)
        {
            if (!sui.CanApply(constructionMode)) return false;
            if (BayRefuses(sui)) return false;
            try
            {
                if (!AssemblyFacade.Enabled) return true;
                string v = Violation(sui);
                if (v == null) return true;
                LogThrottle.Info("Claims|" + sui.Ship.UniqueID, $"Home cells: an edit of ship {sui.Ship.UniqueID} was refused -- {v} lands on cells reserved for another segment of the ship (an arm's home).");
                Ship ship = sui.Ship;
                if (ship.Commands.IsCommandableByLocalPlayer && ship.Sim?.Game?.Gui != null)
                    ship.Sim.Game.Gui.CreateFloatingText("Reserved: an arm's home cells", ship.WorldCenter, Cosmoteer.Gui.WidgetRules.Instance.BadTextColor);
                return false;
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("ClaimMap.CanApply", ex);
                return true;
            }
        }

        private static bool BayRefuses(ShipUpdateInfo sui)
        {
            try
            {
                ZtxBayTerminalComponent locked = BayManager.EditRefusal(sui);
                if (locked == null) return false;
                Ship ship = sui.Ship;
                LogThrottle.Info("BayEditLocked|" + ship.UniqueID, $"Bay terminal at {locked.Part?.Location}: an edit of ship {ship.UniqueID} that adds to the bay's design was refused -- Build is on.");
                if (BayText.Show(ship, "EditLocked", Cosmoteer.Localization.Strings.GetText("ZtxBayPanel/EditLocked"), Cosmoteer.Gui.WidgetRules.Instance.BadTextColor))
                    ship.Sim.Game.Gui.PlayErrorSound();
                return true;
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("ClaimMap.BayRefuses", ex);
                return false;
            }
        }

        public static bool HasSegmentsOut(Ship ship) => AssemblyAi.SegmentsOf(ship) != null;

        public static int ConflictCount(Ship ship) => Conflicts(ship).Count;

        public static List<string> Conflicts(Ship ship)
        {
            var found = new List<string>();
            if (ship == null) return found;
            HashSet<(int, int)> claims = ClaimsOn(ship);
            if (claims.Count == 0) return found;
            Ship[] segs = AssemblyAi.SegmentsOf(ship);
            foreach (Part p in ship.Parts)
            {
                if (MergeRebuild.IsRodRules(p.Rules)) continue;
                IntRect r = p.Rules.GetRect(p.Location, p.Rotation);
                if (!Refuses(r.Left, r.Top, r.Width, r.Height, claims, false, false)) continue;
                string by = "a merged piston's home";
                if (segs != null)
                    foreach (Ship m in segs)
                    {
                        if (m == null || ReferenceEquals(m, ship)) continue;
                        Part mp = m.Parts[p.Location, PartRectType.Normal];
                        if (mp != null) { by = $"ship {m.UniqueID}'s {mp.Rules.ID} at {mp.Location}"; break; }
                        BlueprintPart mb = m.BlueprintParts[p.Location, PartRectType.Normal];
                        if (mb != null) { by = $"ship {m.UniqueID}'s blueprint {mb.Rules.ID} at {mb.Location}"; break; }
                    }
                found.Add($"{p.Rules.ID} at {p.Location} (claimed by {by})");
            }
            return found;
        }
    }

    [HarmonyPatch]
    internal static class ClaimCanApplySitesPatch
    {
        public const int EXPECTED = 5;
        private static readonly Dictionary<MethodBase, int> s_sites = new Dictionary<MethodBase, int>();
        public static string Summary => $"ClaimMap: {s_sites.Values.Sum()}/{EXPECTED} build-update checks patched (arm home cells reserved)";

        private static MethodInfo From => AccessTools.Method(typeof(ShipUpdateInfo), nameof(ShipUpdateInfo.CanApply));

        private static List<MethodBase> Targets()
        {
            var list = new List<MethodBase>();
            MethodInfo from = From;
            if (from == null) return list;
            const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (Type t in new[] { typeof(ShipHistoryManager), typeof(BuildToolbox), typeof(Cosmoteer.Game.Multiplayer.MPInputs.MPUpdateShipInput) })
                foreach (MethodInfo m in t.GetMethods(ALL))
                {
                    if (m.IsAbstract || m.ContainsGenericParameters) continue;
                    int n;
                    try { n = PatchProcessor.GetOriginalInstructions(m).Count(ci => (ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Equals(ci.operand, from)); }
                    catch { continue; }
                    if (n > 0) list.Add(m);
                }
            return list;
        }

        private static bool Prepare(MethodBase original) => original != null || Targets().Count > 0;

        private static IEnumerable<MethodBase> TargetMethods() => Targets();

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var list = new List<CodeInstruction>(instructions);
            MethodInfo from = From;
            MethodInfo to = AccessTools.Method(typeof(ClaimMap), nameof(ClaimMap.CanApply));
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

    [HarmonyPatch(typeof(ShipUpdateInfo), nameof(ShipUpdateInfo.TryAddBlueprintParts), new Type[] {
        typeof(List<PartInfo>), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(InvalidReasons), typeof(List<Cosmoteer.Ships.Doors.DoorInfo>), typeof(bool) },
        new ArgumentType[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal, ArgumentType.Normal })]
    internal static class ClaimBrushPatch
    {
        static bool Prefix(ShipUpdateInfo __instance, List<PartInfo> parts, bool force, ref InvalidReasons reasons, ref bool __result)
        {
            try
            {
                Ship ship = __instance.p_ship;
                if (force || parts == null || ship == null || !AssemblyFacade.Enabled || HingeJointManager.ActivePinCount == 0) return true;
                HashSet<(int, int)> claims = ClaimMap.CachedClaims(ship);
                if (claims.Count == 0) return true;
                foreach (PartInfo p in parts)
                {
                    IntRect r = p.Rect;
                    bool repair = ship.DestroyedParts != null && ship.DestroyedParts.HasPartAt(r);
                    if (!ClaimMap.Refuses(r.Left, r.Top, r.Width, r.Height, claims, repair, MergeRebuild.IsRodRules(p.Rules))) continue;
                    reasons = InvalidReasons.Blocked;
                    __result = false;
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("ClaimBrushPatch", ex);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(BuildToolbox), "OnRecenterBlueprintsClicked")]
    internal static class RecenterGuardPatch
    {
        static bool Prefix(BuildToolbox __instance)
        {
            try
            {
                Ship ship = __instance._ship;
                if (ship == null || !AssemblyFacade.Enabled || !ClaimMap.HasSegmentsOut(ship)) return true;
                __instance.Game.Gui.PlayErrorSound();
                return false;
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("RecenterGuardPatch", ex);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(BuildToolbox), "OnBlueprintModeUpdatingUIState")]
    internal static class RecenterButtonPatch
    {
        public const int EXPECTED = 5;
        private static readonly Dictionary<MethodBase, int> s_sites = new Dictionary<MethodBase, int>();
        public static string Summary => $"RecenterButton: {s_sites.Values.Sum()}/{EXPECTED} button-state writes patched (Recenter greyed while arms are out)";

        private static BuildToolbox s_current;

        private sealed class FixedText : Halfling.Gui.Components.Text.ITextProvider
        {
            public FixedText(string text) { Text = text; }
            public string Text { get; }
            public event EventHandler<Halfling.Events.ValueChangedEventArgs<string>> TextChanged { add { } remove { } }
        }

        private static readonly FixedText s_why = new FixedText("Bring every arm home first: recentering would move the cells the arms come home to.");
        private static Halfling.Gui.Components.Text.ITextProvider s_vanilla;

        private static bool Blocked(BuildToolbox tb)
        {
            Ship ship = tb?._ship;
            return ship != null && AssemblyFacade.Enabled && ClaimMap.HasSegmentsOut(ship);
        }

        public static void SetActive(Halfling.Gui.Widget w, bool value)
        {
            try
            {
                BuildToolbox tb = s_current;
                if (value && tb != null && ReferenceEquals(w, tb._recenterBtn) && Blocked(tb)) value = false;
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("RecenterButtonPatch.SetActive", ex);
            }
            w.SelfInputActive = value;
        }

        static void Prefix(BuildToolbox __instance)
        {
            s_current = __instance;
            try
            {
                var btn = __instance._recenterBtn;
                if (btn == null || __instance._ship == null) return;
                if (!ReferenceEquals(btn.ToolTips.InputDisabledTextProvider, s_why)) s_vanilla = btn.ToolTips.InputDisabledTextProvider;
                if (Blocked(__instance))
                    btn.ToolTips.InputDisabledTextProvider = s_why;
                else if (ReferenceEquals(btn.ToolTips.InputDisabledTextProvider, s_why))
                    btn.ToolTips.InputDisabledTextProvider = s_vanilla;
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("RecenterButtonPatch", ex);
            }
        }

        static void Postfix() => s_current = null;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var list = new List<CodeInstruction>(instructions);
            MethodInfo from = AccessTools.PropertySetter(typeof(Halfling.Gui.Widget), nameof(Halfling.Gui.Widget.SelfInputActive));
            MethodInfo to = AccessTools.Method(typeof(RecenterButtonPatch), nameof(SetActive));
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

    internal sealed class ClaimOverlay : IBlueprintOverlayRenderer
    {
        private static readonly Color s_tint = new Color(0.95f, 0.30f, 0.25f, 0.28f);
        private static bool s_failed;
        private readonly List<(int, int)> _cells = new List<(int, int)>();

        public static void Register()
        {
            try
            {
                BlueprintOverlayManager.RegisterOverlay(1150, () => new ClaimOverlay());
            }
            catch (Exception ex)
            {
                Log.Exception("ClaimOverlay registration", ex);
            }
        }

        private Ship _for;
        private (int, int) _key;

        public void RefreshData(Ship ship, IReadOnlyList<PartOverlayRenderData> parts, IBlueprintOverlaySettingsProvider settings, BlueprintOverlayFlags flags)
        {
            if (s_failed || ship == null) { _cells.Clear(); return; }
            try
            {
                Ship[] segs = AssemblyAi.SegmentsOf(ship);
                int count = segs == null ? 0 : segs.Sum(m => m.Parts.Count + m.BlueprintParts.Count);
                var key = (ship.Sim?.Tick ?? 0, count + ship.Parts.Count * 7919);
                if (ReferenceEquals(ship, _for) && key == _key) return;
                _for = ship;
                _key = key;
                _cells.Clear();
                _cells.AddRange(ClaimMap.CachedClaims(ship).Where(c => ship.Parts[new IntVector2(c.Item1, c.Item2), PartRectType.Normal] == null));
            }
            catch (Exception ex)
            {
                s_failed = true;
                Log.Exception("ClaimOverlay.RefreshData (overlay off for this session)", ex);
            }
        }

        public void DrawEarly(Ship ship, IBlueprintOverlaySettingsProvider settings) { }

        public void Draw(Ship ship, IBlueprintOverlaySettingsProvider settings)
        {
            if (s_failed || _cells.Count == 0) return;
            try
            {
                App.Graphics.SetToMaterial(null);
                foreach ((int x, int y) in _cells)
                    App.Graphics.Draw(new Quad(new Rect(x, y, 1f, 1f), s_tint));
            }
            catch (Exception ex)
            {
                s_failed = true;
                Log.Exception("ClaimOverlay.Draw (overlay off for this session)", ex);
            }
        }

        public void DrawLate(Ship ship, IBlueprintOverlaySettingsProvider settings) { }

        public void DrawLimited(Ship ship, IBlueprintOverlaySettingsProvider settings) { }

        public void Dispose() { }
    }
}
