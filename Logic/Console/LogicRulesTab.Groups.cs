using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Graphics;

namespace ZTX.HingesPistonsDocking
{
    internal sealed partial class LogicRulesTab
    {
        private static float CountWidth => 110f * LogicUiScale.Rules;
        private static float SearchWidth => 200f * LogicUiScale.Rules;
        private static float SearchLabelWidth => 70f * LogicUiScale.Rules;
        private static float SwatchSize => 34f * LogicUiScale.Rules;
        private static float PaletteLabelWidth => 80f * LogicUiScale.Rules;
        private static float GroupPad => 10f * LogicUiScale.Rules;
        private static float ToolbarGap => 10f * LogicUiScale.Rules;
        private static float FieldWidth => 520f * LogicUiScale.Rules;
        private static float FieldHeight => 44f * LogicUiScale.Rules;
        private static float HexWidth => 90f * LogicUiScale.Rules;
        private const float GroupTextScale = 1.45f;

        private string _search = "";

        private void AddGroup()
        {
            try
            {
                if (_engine == null || _engine.FreeRule() == 0) return;
                string name = FreeName("GROUP");
                var def = new LogicRuleDef { Exists = true, Joined = true, Name = name, IsHeader = true };
                int home = InsertRule(def, atStart: true, withLine: false);
                Log.Info("console: group " + home + " added as " + name + " at the top (empty).");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.AddGroup", ex); }
        }

        private string FreeName(string stem)
        {
            var names = new HashSet<string>();
            for (int s = 1; s <= _engine.RuleCount; s++) if (_engine.Rule(s).Exists) names.Add(_engine.Rule(s).Name ?? "");
            int n = 1;
            while (names.Contains(stem + n)) n++;
            return stem + n;
        }

        private bool IsHeaderRule(int r) => _engine.Rule(r).IsHeader;

        private bool InGroupRule(int r) => _engine.Rule(r).InGroup;

        private static LayoutBox GroupFrame()
        {
            LayoutBox box = Frame();
            box.NineSlice.CopySettingsFrom(WidgetRules.Instance.StaticWidget.NineSlice);
            box.NineSlice.Borders = box.NineSlice.Borders * FrameBorderScale;
            box.Width = RowWidth + HorizontalInset(box.NineSlice) + 2f * GroupPad + HorizontalInset(box.NineSlice);
            box.Children.BorderPadding = new Borders(GroupPad);
            return box;
        }

        private static void TintGroupFrame(LayoutBox box, string hex)
        {
            Color c = LogicPalette.ToColor(hex);
            WidgetNineSlice ns = box.NineSlice;
            if (ns.CenterSprite.VertexColor.Equals(c)) return;
            foreach (GuiSprite s in new[] { ns.TopLeftSprite, ns.TopSprite, ns.TopRightSprite, ns.LeftSprite, ns.CenterSprite, ns.RightSprite, ns.BottomLeftSprite, ns.BottomSprite, ns.BottomRightSprite })
                s.VertexColor = c;
        }

        private RuleBlock BuildGroupBlock(int r, Action<string, Action> sink)
        {
            var block = new RuleBlock { Rule = r, Box = GroupFrame(), IsGroup = true };
            RuleBlock captured = block;
            block.Header = Row(InnerWidth);
            AddGrip(block.Header, () => StartRuleDrag(captured));
            block.Collapse = LogicIcons.Make(LogicIcons.Collapse, "ZtxLogicPanel/Collapse", SmallButton);
            block.Collapse.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipGroupFold");
            block.Collapse.Clicked += delegate { ToggleGroupFold(captured); };
            block.Header.DockedChildren.Add(block.Collapse);
            block.Name = new TextCell(_editor, NameWidth * 1.5f, "ZtxLogicPanel/TipGroupName");
            ScaleText(block.Name.Display, GroupTextScale);
            block.Name.Text = _engine.Rule(r).Name;
            block.Name.Changed += () => OnNameChanged(captured);
            block.Header.DockedChildren.Add(block.Name.Display);
            AddSwatch(block);
            block.Count = new Label { DockMode = DockMode.Left, Width = CountWidth };
            block.Count.TextRenderer.HAlignment = HAlignment.Left;
            block.Count.TextRenderer.OversizeMode = OversizeMode.Shrink;
            block.Count.TextRenderer.Color = Color.Gray;
            block.Header.DockedChildren.Add(block.Count);
            block.Toggle = MakeButton("ZtxLogicPanel/GroupOn", ToggleWidth);
            block.Toggle.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipGroupToggle");
            block.Toggle.Clicked += delegate { ToggleRule(captured.Rule); };
            block.Header.DockedChildren.Add(block.Toggle);
            block.Del = MakeButton("ZtxLogicPanel/DeleteRule", ButtonWidth, ButtonRole.Delete);
            block.Del.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipDeleteGroup");
            block.Del.Clicked += delegate { OnDeleteClicked(captured.Rule); };
            block.Header.DockedChildren.Add(block.Del);
            block.Box.AddChild(block.Header);
            AddPalette(block);
            block.BodyBuilt = true;
            sink("group " + r + " fit", () => { FitGroup(block); ShowFold(block); });
            sink("group " + r + " refresh", () =>
            {
                RefreshBlock(block, _buildMembers, _console?.Ship != null && _console.Ship.Commands.IsCommandableByLocalPlayer);
                block.Complete = true;
            });
            return block;
        }

        private void RefreshGroup(RuleBlock b, LogicRuleDef def, bool commandable)
        {
            RefreshName(b, commandable);
            int disabled = def.Disabled ? 1 : 0;
            if (disabled != b.LastDisabled)
            {
                b.LastDisabled = disabled;
                b.Toggle.TextProvider = Strings.KeyString(def.Disabled ? "ZtxLogicPanel/GroupOff" : "ZtxLogicPanel/GroupOn");
                StateTip(b.Toggle, def.Disabled ? "ZtxLogicPanel/TipGroupOffState" : "ZtxLogicPanel/TipGroupOnState");
                SetTextColour(b.Toggle, def.Disabled ? LogicPalette.Warn : LogicPalette.On);
            }
            b.Toggle.SelfInputActive = commandable;
            int members = b.Members.Count;
            string count = Strings.FormatText(members == 1 ? "ZtxLogicPanel/GroupMembersOneFmt" : "ZtxLogicPanel/GroupMembersFmt", members);
            if (b.Count.Text != count) b.Count.Text = count;
            int colour = _engine.RuleColour(b.Rule);
            if (colour != b.LastColour)
            {
                b.LastColour = colour;
                string hex = LogicPalette.RuleColour(colour);
                SetTextColour(b.Name.Display, hex ?? LogicPalette.LitValue);
                TintStrip(b.Header, hex ?? LogicPalette.LitValue);
            }
            RefreshSwatch(b, commandable);
            TintGroupFrame(b.Box, def.Disabled ? LogicPalette.Warn : LogicPalette.RuleColour(colour) ?? LogicPalette.LitValue);
            RefreshDelete(b, commandable);
        }

        private void RefreshDelete(RuleBlock b, bool commandable)
        {
            bool confirm = _confirmRule == b.Rule && Environment.TickCount - _confirmUntil < 0;
            if (confirm != b.DelConfirm)
            {
                b.DelConfirm = confirm;
                b.Del.TextProvider = Strings.KeyString(confirm ? "ZtxLogicPanel/DeleteSure" : "ZtxLogicPanel/DeleteRule");
            }
            b.Del.SelfInputActive = commandable;
        }

        private void RefreshName(RuleBlock b, bool commandable)
        {
            int n1 = _engine.RawRule(b.Rule, ZtxRuleEngineComponent.RULE_N1);
            int n2 = _engine.RawRule(b.Rule, ZtxRuleEngineComponent.RULE_N2);
            if ((n1 != b.LastN1 || n2 != b.LastN2) && !_editor.IsEditing(b.Name))
            {
                b.LastN1 = n1;
                b.LastN2 = n2;
                string stored = (LogicRuleDef.UnpackName(n1) + LogicRuleDef.UnpackName(n2)).TrimEnd();
                if (b.Name.Text != stored)
                {
                    _refreshing = true;
                    try { b.Name.Text = stored; }
                    finally { _refreshing = false; }
                }
            }
            b.Name.Display.SelfInputActive = commandable;
        }

        private void AddSwatch(RuleBlock block)
        {
            RuleBlock captured = block;
            block.Swatch = LogicIcons.Make(LogicIcons.Paint, "ZtxLogicPanel/TipSwatch", SmallButton);
            block.Swatch.Clicked += delegate { TogglePalette(captured); };
            block.Header.DockedChildren.Add(block.Swatch);
        }

        private void RefreshSwatch(RuleBlock b, bool commandable)
        {
            if (b.Swatch.SelfActive != _engine.IsStoreV2) b.Swatch.SelfActive = _engine.IsStoreV2;
            b.Swatch.SelfInputActive = commandable;
        }

        private static void PaintSwatch(Button b, string hex)
        {
            Color c = LogicPalette.ToColor(hex ?? LogicPalette.Dim);
            foreach (WidgetNineSlice ns in new[] { b.StateNormalNineSlice, b.StateHighlightedNineSlice, b.StatePressedNineSlice, b.StateDisabledNineSlice })
            {
                ns.CenterSprite.CopyFrom(new Sprite((Texture)null, c, Vector2.Zero, Rect.Zero));
                ns.Flags = NineSliceFlags.Center;
            }
            string text = hex == null ? "-" : "";
            if (b.Text != text) b.Text = text;
        }

        private void AddPalette(RuleBlock block)
        {
            RuleBlock captured = block;
            block.Palette = new LayoutBox { Width = InnerWidth };
            block.Palette.NineSlice.Flags = NineSliceFlags.None;
            block.Palette.Children.LayoutAlgorithm = LayoutAlgorithms.TileTopLeftVertical;
            block.Palette.Children.WidgetPadding = new Vector2(0f, GapY);
            block.Palette.Children.ClipChildren = false;
            LayoutBox presets = Row(InnerWidth);
            presets.DockedChildren.Add(PaletteLabel("ZtxLogicPanel/PaletteLabel"));
            for (int c = 0; c <= LogicPalette.RuleColours.Length; c++)
            {
                int colour = c;
                var square = new Button { DockMode = DockMode.Left, Width = SwatchSize };
                square.ToolTips.TextProvider = Strings.KeyString(c == 0 ? "ZtxLogicPanel/ColourNone" : "ZtxLogicPanel/Colour" + c);
                PaintSwatch(square, LogicPalette.RuleColour(c));
                square.Clicked += delegate { SetColour(captured.Rule, colour); TogglePalette(captured); };
                presets.DockedChildren.Add(square);
            }
            presets.DockedChildren.Add(PaletteLabel("ZtxLogicPanel/PaletteCustom"));
            block.Hex = new TextCell(_editor, HexWidth, "ZtxLogicPanel/TipHex");
            block.Hex.Changed += () => OnHexTyped(captured);
            presets.DockedChildren.Add(block.Hex.Display);
            block.Preview = new Button { DockMode = DockMode.Left, Width = SwatchSize };
            block.Preview.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipPreview");
            PaintSwatch(block.Preview, null);
            presets.DockedChildren.Add(block.Preview);
            block.Palette.AddChild(presets);
            LayoutBox fieldRow = Row(InnerWidth);
            fieldRow.Height = FieldHeight;
            fieldRow.DockedChildren.Add(PaletteLabel("ZtxLogicPanel/PaletteField"));
            var field = new LogicColourField { Picked = (rgb, final) => OnFieldPicked(captured, rgb, final) };
            LayoutBox fieldBox = LogicColourField.Make(field, FieldWidth, FieldHeight);
            fieldBox.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipField");
            fieldRow.DockedChildren.Add(fieldBox);
            block.Palette.AddChild(fieldRow);
            block.Palette.Height = RowHeight + GapY + FieldHeight;
            block.Palette.SelfActive = false;
            block.Box.AddChild(block.Palette);
        }

        private static Label PaletteLabel(string key)
        {
            var label = new Label { DockMode = DockMode.Left, Width = PaletteLabelWidth };
            label.TextProvider = Strings.KeyString(key);
            label.TextRenderer.HAlignment = HAlignment.Right;
            label.TextRenderer.OversizeMode = OversizeMode.Shrink;
            label.TextRenderer.Color = Color.Gray;
            return label;
        }

        private void OnFieldPicked(RuleBlock b, int rgb, bool final)
        {
            try
            {
                string hex = rgb.ToString("X6");
                PaintSwatch(b.Preview, hex);
                if (!_editor.IsEditing(b.Hex)) b.Hex.Text = hex;
                if (final) SetColour(b.Rule, LogicPalette.CustomColour(rgb));
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnFieldPicked", ex); }
        }

        private void OnHexTyped(RuleBlock b)
        {
            int rgb = LogicPalette.ParseHex(b.Hex.Text);
            if (rgb < 0) return;
            PaintSwatch(b.Preview, rgb.ToString("X6"));
            SetColour(b.Rule, LogicPalette.CustomColour(rgb));
        }

        private void TogglePalette(RuleBlock b)
        {
            if (_engine == null || !_engine.IsStoreV2) return;
            b.Palette.SelfActive = !b.Palette.SelfActive;
            if (b.IsGroup) FitGroup(b);
            else FitBlock(b);
            _forceFull = true;
        }

        private void SetColour(int r, int colour)
        {
            if (_engine == null || !_engine.IsStoreV2) return;
            if (_engine.RuleColour(r) == colour) return;
            Write((_engine.RuleSlot(r, ZtxRuleEngineComponent.RULE_C), colour));
            Log.Info("console: rule " + r + " colour " + colour + ".");
        }

        private void ToggleGroupFold(RuleBlock b)
        {
            SetCollapsed(b.Rule, !IsCollapsed(b.Rule));
            ShowFold(b);
            _forceFull = true;
        }

        private void ShowFold(RuleBlock b)
        {
            bool folded = IsCollapsed(b.Rule);
            LogicIcons.Set(b.Collapse, folded ? LogicIcons.Expand : LogicIcons.Collapse, folded ? "ZtxLogicPanel/Expand" : "ZtxLogicPanel/Collapse");
            StateTip(b.Collapse, folded ? "ZtxLogicPanel/TipGroupFolded" : "ZtxLogicPanel/TipGroupOpen");
        }

        private sealed class TipMark
        {
            public string Key;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Widget, TipMark> s_tipMarks =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Widget, TipMark>();

        private static void StateTip(Widget w, string key)
        {
            TipMark mark = s_tipMarks.GetOrCreateValue(w);
            if (mark.Key == key) return;
            mark.Key = key;
            w.ToolTips.TextProvider = Strings.KeyString(key);
        }

        private static void FitGroup(RuleBlock g)
        {
            float h = 0f;
            int n = 0;
            foreach (Widget w in g.Box.Children)
            {
                if (!w.SelfActive) continue;
                h += w.Height;
                n++;
            }
            float target = h + Math.Max(0, n - 1) * GapY + 2f * GroupPad + VerticalInset(g.Box.NineSlice);
            if (Math.Abs(g.Box.Height - target) > 0.5f) g.Box.Height = target;
        }

        private void FitGroups()
        {
            foreach (RuleBlock b in _blocks) if (b.IsGroup) FitGroup(b);
        }

        private void BuildOrganiseBar(BuildState st)
        {
            Place(st, new Widget { Width = RowWidth, Height = ToolbarGap });
            LayoutBox bar = Row();
            st.AddGroup = MakeButton("ZtxLogicPanel/AddGroup", ButtonWidth + 20f, ButtonRole.Group);
            st.AddGroup.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipAddGroup");
            st.AddGroup.Clicked += delegate { AddGroup(); };
            bar.DockedChildren.Add(st.AddGroup);
            Button fold = MakeButton("ZtxLogicPanel/FoldAll", ButtonWidth, ButtonRole.Misc);
            fold.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipFoldAll");
            fold.Clicked += delegate { FoldAll(true); };
            bar.DockedChildren.Add(fold);
            Button unfold = MakeButton("ZtxLogicPanel/UnfoldAll", ButtonWidth, ButtonRole.Misc);
            unfold.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipUnfoldAll");
            unfold.Clicked += delegate { FoldAll(false); };
            bar.DockedChildren.Add(unfold);
            var label = new Label { DockMode = DockMode.Left, Width = SearchLabelWidth };
            label.TextProvider = Strings.KeyString("ZtxLogicPanel/Search");
            label.TextRenderer.HAlignment = HAlignment.Right;
            label.TextRenderer.Color = Color.Gray;
            bar.DockedChildren.Add(label);
            var search = new TextCell(_editor, SearchWidth, "ZtxLogicPanel/TipSearch");
            search.Text = _search;
            search.Changed += () => { _search = search.Text ?? ""; _forceFull = true; };
            bar.DockedChildren.Add(search.Display);
            Place(st, bar);
        }

        private void FoldAll(bool fold)
        {
            try
            {
                foreach (RuleBlock b in _blocks)
                {
                    SetCollapsed(b.Rule, fold);
                    if (b.IsGroup) { ShowFold(b); continue; }
                    if (fold) { ApplyCollapse(b, true); continue; }
                    RuleBlock captured = b;
                    if (captured.BodyBuilt) ApplyCollapse(captured, false);
                    else _queue.Enqueue("unfold " + captured.Rule, () => { BuildBody(captured, RunNow); ApplyCollapse(captured, IsCollapsed(captured.Rule)); });
                }
                _forceFull = true;
                Log.Info("console: " + (fold ? "folded" : "unfolded") + " all " + _blocks.Count + " rules and groups.");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.FoldAll", ex); }
        }

        private void ApplyVisibility()
        {
            string q = (_search ?? "").Trim().ToUpperInvariant();
            bool searching = q.Length > 0;
            var groupShows = new Dictionary<RuleBlock, bool>();
            foreach (RuleBlock b in _blocks)
            {
                if (!b.IsGroup) continue;
                groupShows[b] = !searching || NameOf(b).Contains(q);
            }
            foreach (RuleBlock b in _blocks)
            {
                if (b.IsGroup) continue;
                bool match = !searching || NameOf(b).Contains(q);
                bool show;
                if (b.Group == null) show = match;
                else if (searching)
                {
                    show = match || NameOf(b.Group).Contains(q);
                    if (show) groupShows[b.Group] = true;
                }
                else show = !IsCollapsed(b.Group.Rule);
                SetShown(b, show);
            }
            foreach (KeyValuePair<RuleBlock, bool> kv in groupShows) SetShown(kv.Key, kv.Value);
        }

        private string NameOf(RuleBlock b) => (_engine.Rule(b.Rule).Name ?? "").ToUpperInvariant();

        private static void SetShown(RuleBlock b, bool show)
        {
            if (b.Box.SelfActive != show) b.Box.SelfActive = show;
        }

        private static bool IsShown(RuleBlock b) => b.IsCondition ? b.Box.SelfActive && b.Owner != null && (b.Owner.VarBox == null || b.Owner.VarBox.SelfActive) && IsShown(b.Owner)
            : b.Box.SelfActive && (b.Group == null || b.Group.Box.SelfActive);
    }
}
