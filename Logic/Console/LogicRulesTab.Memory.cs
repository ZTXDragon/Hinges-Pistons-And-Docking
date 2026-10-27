using System;
using System.Collections.Generic;
using System.Globalization;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Text;

namespace ZTX.HingesPistonsDocking
{
    internal sealed partial class LogicRulesTab
    {
        private static float MemValueWidth => 90f * LogicUiScale.Rules;

        private sealed class MemRow
        {
            public int Rule;
            public LayoutBox Box;
            public TextCell Name, Start;
            public Button Kind, StartFlag, Del;
            public HideCell StartCell, StartFlagCell;
            public Label Value, Users;
            public SimpleTextProvider DelTip;
            public int LastM = int.MinValue;
            public bool DelConfirm;
        }

        private readonly List<MemRow> _memRows = new List<MemRow>();
        private Button _addMem;
        private int _confirmMem, _confirmMemUntil;

        private bool IsMemoryRule(int r) => r >= 1 && r <= _engine.RuleCount && _engine.Rule(r).Exists && _engine.Rule(r).IsMemory;

        private bool IsMemCounterRef(LogicRef x) => x.IsRule && IsMemoryRule(x.Index) && _engine.Rule(x.Index).MemoryCounter;

        private void BuildMemoryBox(BuildState st)
        {
            LayoutBox box = Frame();
            LayoutBox head = Row(InnerWidth);
            var title = new Label { DockMode = DockMode.Left, Width = NameWidth + KindTagWidth };
            title.TextProvider = Strings.KeyString("ZtxLogicPanel/SectionMemory");
            title.TextRenderer.HAlignment = HAlignment.Left;
            title.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipSectionMemory");
            ScaleText(title, HeaderTextScale);
            SetTextColour(title, LogicPalette.Heading);
            head.DockedChildren.Add(title);
            Button add = MakeButton("ZtxLogicPanel/AddMemory", ButtonWidth + 20f, ButtonRole.Add);
            add.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipAddMemory");
            add.Clicked += delegate { AddMemory(); };
            head.DockedChildren.Add(add);
            box.AddChild(head);
            var rows = new List<MemRow>();
            for (int r = 1; r <= _engine.RuleCount; r++)
            {
                if (!IsMemoryRule(r)) continue;
                MemRow row = BuildMemRow(r);
                box.AddChild(row.Box);
                rows.Add(row);
            }
            FitHeight(box);
            st.MemRows.AddRange(rows);
            st.AddMem = add;
            if (!st.Swap)
            {
                _memRows.Clear();
                _memRows.AddRange(rows);
                _addMem = add;
            }
            Place(st, box);
        }

        private MemRow BuildMemRow(int r)
        {
            var row = new MemRow { Rule = r, Box = Row(InnerWidth) };
            MemRow crow = row;
            row.Box.DockedChildren.Add(new Widget { DockMode = DockMode.Left, Width = IndentWidth });
            row.Name = new TextCell(_editor, NameWidth, "ZtxLogicPanel/TipMemName");
            row.Name.Text = _engine.Rule(r).Name;
            row.Name.Changed += () => OnMemNameChanged(crow);
            row.Box.DockedChildren.Add(row.Name.Display);
            row.Kind = MakeButton("ZtxLogicPanel/MemCounter", ToggleWidth + 20f, ButtonRole.Misc);
            row.Kind.Clicked += delegate { ToggleMemKind(crow); };
            row.Box.DockedChildren.Add(row.Kind);
            var startLabel = new Label { DockMode = DockMode.Left, Width = SetLabelWidth };
            startLabel.TextProvider = Strings.KeyString("ZtxLogicPanel/MemStartLabel");
            startLabel.TextRenderer.HAlignment = HAlignment.Right;
            startLabel.TextRenderer.Color = Halfling.Graphics.Color.Gray;
            row.Box.DockedChildren.Add(startLabel);
            row.Start = SetField(PoseWidth, "ZtxLogicPanel/TipMemStart");
            row.Start.Changed += () => OnMemStart(crow);
            row.StartCell = Hideable(row.Box, row.Start.Display, PoseWidth);
            row.StartCell.KeepSpace = false;
            row.StartFlag = MakeButton("ZtxLogicPanel/SigOff", PoseWidth);
            row.StartFlag.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipMemStartFlag");
            row.StartFlag.Clicked += delegate { ToggleMemStartFlag(crow); };
            row.StartFlagCell = Hideable(row.Box, row.StartFlag, PoseWidth);
            row.StartFlagCell.KeepSpace = false;
            row.Value = new Label { DockMode = DockMode.Left, Width = MemValueWidth };
            row.Value.TextRenderer.HAlignment = HAlignment.Center;
            row.Value.TextRenderer.OversizeMode = OversizeMode.Shrink;
            row.Value.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipMemValue");
            row.Box.DockedChildren.Add(row.Value);
            row.Users = new Label { DockMode = DockMode.Left, Width = VarUsersWidth };
            row.Users.TextRenderer.HAlignment = HAlignment.Left;
            row.Users.TextRenderer.OversizeMode = OversizeMode.Shrink;
            SetTextColour(row.Users, LogicPalette.Dim);
            row.Box.DockedChildren.Add(row.Users);
            row.Del = MakeButton("ZtxLogicPanel/DeleteVar", SeqDeleteWidth, ButtonRole.Delete);
            row.DelTip = new SimpleTextProvider(Strings.GetText("ZtxLogicPanel/TipDeleteMemory"), null);
            row.Del.ToolTips.TextProvider = row.DelTip;
            row.Del.Clicked += delegate { OnDeleteMemClicked(crow); };
            row.Box.DockedChildren.Add(row.Del);
            return row;
        }

        private void RefreshMemory(bool commandable)
        {
            if (_addMem != null) _addMem.SelfInputActive = commandable && _engine.IsStoreV2;
            foreach (MemRow row in _memRows)
            {
                if (!IsMemoryRule(row.Rule)) continue;
                LogicRuleDef d = _engine.Rule(row.Rule);
                int m = _engine.RawRule(row.Rule, ZtxRuleEngineComponent.RULE_M);
                bool counter = d.MemoryCounter;
                if (m != row.LastM)
                {
                    row.LastM = m;
                    row.Kind.TextProvider = Strings.KeyString(counter ? "ZtxLogicPanel/MemCounter" : "ZtxLogicPanel/MemFlag");
                    StateTip(row.Kind, counter ? "ZtxLogicPanel/TipMemKindCounter" : "ZtxLogicPanel/TipMemKindFlag");
                    SetTextColour(row.Kind, LogicPalette.LitValue);
                    row.StartCell.Show(counter);
                    row.StartFlagCell.Show(!counter);
                    row.StartFlag.TextProvider = Strings.KeyString(d.MemoryStart != 0 ? "ZtxLogicPanel/SigOn" : "ZtxLogicPanel/SigOff");
                    SetTextColour(row.StartFlag, d.MemoryStart != 0 ? LogicPalette.On : LogicPalette.Off);
                }
                if (!_editor.IsEditing(row.Start))
                {
                    string start = d.MemoryStart.ToString(CultureInfo.InvariantCulture);
                    if (row.Start.Text != start) { _refreshing = true; try { row.Start.Text = start; } finally { _refreshing = false; } }
                }
                if (!_editor.IsEditing(row.Name) && row.Name.Text != d.Name) { _refreshing = true; try { row.Name.Text = d.Name; } finally { _refreshing = false; } }
                int value = _engine.MemoryValue(row.Rule);
                string vt = counter ? value.ToString(CultureInfo.InvariantCulture) : Strings.GetText(value != 0 ? "ZtxLogicPanel/SigOn" : "ZtxLogicPanel/SigOff");
                if (row.Value.Text != vt) row.Value.Text = vt;
                SetTextColour(row.Value, counter ? LogicPalette.LitValue : value != 0 ? LogicPalette.On : LogicPalette.Off);
                int users = MemoryUsers(row.Rule);
                string ut = users == 0 ? Strings.GetText("ZtxLogicPanel/VarUnused") : Strings.FormatText("ZtxLogicPanel/VarUsersFmt", users);
                if (row.Users.Text != ut) row.Users.Text = ut;
                bool confirm = _confirmMem == row.Rule && Environment.TickCount - _confirmMemUntil < 0;
                if (confirm != row.DelConfirm)
                {
                    row.DelConfirm = confirm;
                    row.Del.TextProvider = Strings.KeyString(confirm ? "ZtxLogicPanel/DeleteSure" : "ZtxLogicPanel/DeleteVar");
                }
                string tip = users == 0 ? Strings.GetText("ZtxLogicPanel/TipDeleteMemory") : Strings.FormatText("ZtxLogicPanel/TipDeleteMemoryUsedFmt", users);
                if (row.DelTip.Text != tip) row.DelTip.Text = tip;
                row.Del.SelfInputActive = commandable && users == 0;
                row.Kind.SelfInputActive = commandable;
                row.StartFlag.SelfInputActive = commandable;
                row.Start.Display.SelfInputActive = commandable;
                row.Name.Display.SelfInputActive = commandable;
            }
        }

        private int MemoryUsers(int r)
        {
            LogicRef me = LogicRef.Rule(r);
            int n = 0;
            for (int k = 1; k <= _engine.LineCount; k++)
            {
                LogicLine l = _engine.Line(k);
                if (l.IsUsed && (l.A.Equals(me) || l.B.Equals(me))) n++;
            }
            for (int k = 1; k <= _engine.StepCount; k++)
            {
                SeqStep st = _engine.Step(k);
                if (st.IsUsed && st.Target.Equals(me)) n++;
            }
            return n;
        }

        private int FirstMemory()
        {
            for (int r = 1; r <= _engine.RuleCount; r++) if (IsMemoryRule(r)) return r;
            return 0;
        }

        private List<(int, string)> MemItems(LogicRef keep)
        {
            var l = new List<(int, string)> { (0, Strings.GetText("ZtxLogicPanel/InNone")) };
            bool kept = keep.IsNone;
            for (int r = 1; r <= _engine.RuleCount; r++)
            {
                if (!IsMemoryRule(r)) continue;
                LogicRef mr = LogicRef.Rule(r);
                if (mr.Equals(keep)) kept = true;
                l.Add((mr.Pack(), RuleText(r)));
            }
            if (!kept) l.Add((keep.Pack(), Stale(keep)));
            return l;
        }

        private void AddMemory()
        {
            try
            {
                if (_engine == null || !_engine.IsStoreV2) return;
                List<int> slots = _engine.FreeRules(1);
                if (slots.Count == 0) return;
                int r = slots[0];
                var names = new List<string>();
                for (int s = 1; s <= _engine.RuleCount; s++) if (IsMemoryRule(s)) names.Add(_engine.Rule(s).Name ?? "");
                string name = LogicVariables.FreeName(names, "MEM", LogicRuleDef.NAME_CHARS);
                var def = new LogicRuleDef { Exists = true, Joined = true, IsCondition = true, PoseSet = true, Pose = 0, Name = name };
                var raw = new int[_engine.RuleFieldCount];
                raw[ZtxRuleEngineComponent.RULE_M] = def.PackMisc();
                raw[ZtxRuleEngineComponent.RULE_N1] = LogicRuleDef.PackName(name, 0);
                raw[ZtxRuleEngineComponent.RULE_N2] = LogicRuleDef.PackName(name, 5);
                var writes = new List<(ID<PartToggleGuiRules>, int)>();
                for (int f = 0; f < raw.Length; f++) writes.Add((_engine.RuleSlot(r, f), raw[f]));
                writes.Add((ZtxRuleEngineComponent.MemoryId(r), 0));
                Write(writes.ToArray());
                Log.Info("console: memory value " + r + " added as " + name + " (counter, start 0).");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.AddMemory", ex); }
        }

        private void WriteMemoryDef(int r, LogicRuleDef def, bool resetValue)
        {
            var writes = new List<(ID<PartToggleGuiRules>, int)> { (_engine.RuleSlot(r, ZtxRuleEngineComponent.RULE_M), def.PackMisc()) };
            if (resetValue) writes.Add((ZtxRuleEngineComponent.MemoryId(r), def.MemoryStart));
            Write(writes.ToArray());
        }

        private void ToggleMemKind(MemRow row)
        {
            try
            {
                if (_engine == null || !IsMemoryRule(row.Rule)) return;
                LogicRuleDef def = _engine.Rule(row.Rule);
                def.PoseSet = !def.PoseSet;
                def.Pose = 0;
                WriteMemoryDef(row.Rule, def, resetValue: true);
                Log.Info("console: memory value " + row.Rule + " is now a " + (def.PoseSet ? "counter" : "flag") + ".");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.ToggleMemKind", ex); }
        }

        private void OnMemStart(MemRow row)
        {
            if (_refreshing || _engine == null || !IsMemoryRule(row.Rule)) return;
            try
            {
                if (!int.TryParse((row.Start.Text ?? "").Trim(), out int v)) return;
                v = Math.Max(0, Math.Min(LogicRuleDef.POSE_MAX, v));
                LogicRuleDef def = _engine.Rule(row.Rule);
                if (def.Pose == v) return;
                def.Pose = v;
                WriteMemoryDef(row.Rule, def, resetValue: false);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnMemStart", ex); }
        }

        private void ToggleMemStartFlag(MemRow row)
        {
            try
            {
                if (_engine == null || !IsMemoryRule(row.Rule)) return;
                LogicRuleDef def = _engine.Rule(row.Rule);
                def.Pose = def.Pose != 0 ? 0 : 1;
                WriteMemoryDef(row.Rule, def, resetValue: false);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.ToggleMemStartFlag", ex); }
        }

        private void OnMemNameChanged(MemRow row)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                string clean = LogicRuleDef.Clean(row.Name.Text);
                Write((_engine.RuleSlot(row.Rule, ZtxRuleEngineComponent.RULE_N1), LogicRuleDef.PackName(clean, 0)),
                      (_engine.RuleSlot(row.Rule, ZtxRuleEngineComponent.RULE_N2), LogicRuleDef.PackName(clean, 5)));
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnMemNameChanged", ex); }
        }

        private void OnDeleteMemClicked(MemRow row)
        {
            _forceFull = true;
            if (_confirmMem == row.Rule && Environment.TickCount - _confirmMemUntil < 0)
            {
                _confirmMem = 0;
                DeleteMemory(row.Rule);
                return;
            }
            _confirmMem = row.Rule;
            _confirmMemUntil = Environment.TickCount + DELETE_CONFIRM_MS;
        }

        private void DeleteMemory(int r)
        {
            try
            {
                if (_engine == null || !IsMemoryRule(r)) return;
                int users = MemoryUsers(r);
                if (users > 0) { Log.Info("console: memory value " + r + " is used by " + users + "; not deleted."); return; }
                var writes = new List<(ID<PartToggleGuiRules>, int)>();
                ClearConditionWrites(writes, r);
                writes.Add((ZtxRuleEngineComponent.MemoryId(r), 0));
                Write(writes.ToArray());
                Log.Info("console: memory value " + r + " deleted.");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.DeleteMemory", ex); }
        }
    }
}
