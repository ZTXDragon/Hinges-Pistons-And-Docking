using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Text;

namespace ZTX.HingesPistonsDocking
{
    internal sealed partial class LogicRulesTab
    {
        private static float KindTagWidth => 74f * LogicUiScale.Rules;
        private static float VarNameWidth => 140f * LogicUiScale.Rules;
        private static float VarUsersWidth => 180f * LogicUiScale.Rules;
        private const int NEW_VAR = LogicPickerPopup.NEW_ENTRY;

        private int _bodyStage;

        private bool HasCode(int rule) => _text != null && _text.HasCode(rule);

        private bool IsFunctionRule(int r) => _text != null && _text.IsFunction(r);

        private bool IsReadableRule(int r)
        {
            LogicRuleDef d = _engine.Rule(r);
            return d.Exists && !d.IsHeader && !d.IsCondition && !IsFunctionRule(r);
        }

        private bool IsConditionRule(int r) => r >= 1 && r <= _engine.RuleCount && _engine.Rule(r).Exists && _engine.Rule(r).IsCondition;

        private List<int> ConditionsOf(int f) => _text != null ? _text.ConditionsOf(f) : new List<int>();

        private int OwnerFunction(int rule)
        {
            LogicRuleDef d = _engine.Rule(rule);
            return d.IsCondition ? d.FunctionOf : rule;
        }

        private static bool IsCondStep(SeqKind k) => k == SeqKind.If || k == SeqKind.While || k == SeqKind.WaitUntil;

        private void RefreshKindTag(RuleBlock b)
        {
            int code = IsFunctionRule(b.Rule) ? 1 : 0;
            if (code == b.LastCode) return;
            b.LastCode = code;
            b.KindTag.TextProvider = Strings.KeyString(code == 1 ? "ZtxLogicPanel/TagFunction" : "ZtxLogicPanel/TagSignal");
            b.KindTag.ToolTips.TextProvider = Strings.KeyString(code == 1 ? "ZtxLogicPanel/TipTagFunction" : "ZtxLogicPanel/TipTagSignal");
            SetTextColour(b.KindTag, code == 1 ? LogicPalette.Set : LogicPalette.KindSensor);
        }

        private static LayoutBox ConditionBox(float width)
        {
            var box = new LayoutBox();
            box.NineSlice.Flags = NineSliceFlags.None;
            box.Width = width;
            box.Children.LayoutAlgorithm = LayoutAlgorithms.TileTopLeftVertical;
            box.Children.BorderPadding = new Borders(0f);
            box.Children.WidgetPadding = new Vector2(0f, GapY);
            box.Children.ClipChildren = false;
            return box;
        }

        private static void FitCondition(LayoutBox box)
        {
            float h = 0f;
            int n = 0;
            foreach (Widget w in box.Children)
            {
                if (!w.SelfActive) continue;
                h += w.Height;
                n++;
            }
            box.Height = h + Math.Max(0, n - 1) * GapY;
        }

        private static void RefitBlock(RuleBlock b)
        {
            if (!b.IsCondition) { FitBlock(b); return; }
            FitCondition(b.Box);
            if (b.Owner != null) FitBlock(b.Owner);
        }

        private void BuildVariables(RuleBlock block)
        {
            RuleBlock captured = block;
            LayoutBox frame = BlockFrame();
            block.VarBox = frame;
            float width = BlockInnerWidth;
            LayoutBox head = Row(width);
            var title = new Label { DockMode = DockMode.Left, Width = VarNameWidth };
            title.TextProvider = Strings.KeyString("ZtxLogicPanel/SectionVariables");
            title.TextRenderer.HAlignment = HAlignment.Left;
            title.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipSectionVariables");
            SetTextColour(title, LogicPalette.Heading);
            head.DockedChildren.Add(title);
            frame.AddChild(head);
            foreach (int c in ConditionsOf(block.Rule)) frame.AddChild(BuildConditionView(c, block, width).Box);
            LayoutBox addRow = Row(width);
            block.AddVar = MakeButton("ZtxLogicPanel/AddVariable", ButtonWidth + 20f, ButtonRole.Add);
            block.AddVar.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipAddVariable");
            block.AddVar.Clicked += delegate { AddVariable(captured.Rule); };
            addRow.DockedChildren.Add(block.AddVar);
            frame.AddChild(addRow);
            FitFrame(frame);
            AddBody(block, frame);
        }

        private RuleBlock BuildConditionView(int c, RuleBlock owner, float width)
        {
            var cb = new RuleBlock { Rule = c, IsCondition = true, Owner = owner, Box = ConditionBox(width), BodyBuilt = true };
            RuleBlock captured = cb;
            cb.Header = Row(width);
            cb.Collapse = LogicIcons.Make(IsCollapsed(c) ? LogicIcons.Expand : LogicIcons.Collapse, IsCollapsed(c) ? "ZtxLogicPanel/Expand" : "ZtxLogicPanel/Collapse", SmallButton);
            cb.Collapse.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipVarFold");
            cb.Collapse.Clicked += delegate { ToggleVarFold(captured); };
            cb.Header.DockedChildren.Add(cb.Collapse);
            cb.Name = new TextCell(_editor, VarNameWidth, "ZtxLogicPanel/TipVarName");
            ScaleText(cb.Name.Display, HeaderTextScale);
            cb.Name.Text = _engine.Rule(c).Name;
            cb.Name.Changed += () => OnNameChanged(captured);
            cb.Header.DockedChildren.Add(cb.Name.Display);
            cb.Result = new Label { DockMode = DockMode.Left, Width = ResultWidth };
            cb.Result.TextRenderer.HAlignment = HAlignment.Center;
            cb.Result.TextRenderer.OversizeMode = OversizeMode.Shrink;
            cb.Result.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipVarResult");
            cb.Header.DockedChildren.Add(cb.Result);
            cb.Count = new Label { DockMode = DockMode.Left, Width = VarUsersWidth };
            cb.Count.TextRenderer.HAlignment = HAlignment.Left;
            cb.Count.TextRenderer.OversizeMode = OversizeMode.Shrink;
            SetTextColour(cb.Count, LogicPalette.Dim);
            cb.Header.DockedChildren.Add(cb.Count);
            cb.Del = MakeButton("ZtxLogicPanel/DeleteVar", SeqDeleteWidth, ButtonRole.Delete);
            cb.Say = new SimpleTextProvider(Strings.GetText("ZtxLogicPanel/TipDeleteVariable"), null);
            cb.Del.ToolTips.TextProvider = cb.Say;
            cb.Del.Clicked += delegate { OnDeleteVarClicked(captured); };
            cb.Header.DockedChildren.Add(cb.Del);
            AddBody(cb, cb.Header);
            cb.Warn = new Label { Width = width, Height = RowHeight };
            cb.Warn.TextRenderer.XmlFormatting = true;
            cb.Warn.TextRenderer.HAlignment = HAlignment.Left;
            cb.Warn.TextRenderer.OversizeMode = OversizeMode.Shrink;
            AddBody(cb, cb.Warn);
            cb.Hidden.Add(cb.Warn);
            cb.Warn.SelfActive = false;
            for (int k = 1; k <= _engine.LineCount; k++)
            {
                LogicLine line = _engine.Line(k);
                if (!line.IsUsed || line.Rule != c) continue;
                LineRow row = BuildLine(cb, k, line, cb.Lines.Count + 1, width);
                AddBody(cb, row.Box);
                cb.Lines.Add(row);
            }
            cb.Expr = new Label { Width = width, Height = RowHeight };
            cb.Expr.TextRenderer.XmlFormatting = true;
            cb.Expr.TextRenderer.HAlignment = HAlignment.Left;
            cb.Expr.TextRenderer.OversizeMode = OversizeMode.Shrink;
            cb.Expr.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipCondExpr");
            AddBody(cb, cb.Expr);
            LayoutBox addRow = Row(width);
            cb.AddLine = MakeButton("ZtxLogicPanel/AddLine", ButtonWidth, ButtonRole.Add);
            cb.AddLine.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipAddCondLine");
            cb.AddLine.Clicked += delegate { AddLine(captured.Rule); };
            addRow.DockedChildren.Add(cb.AddLine);
            AddBody(cb, addRow);
            if (IsCollapsed(c)) ShowVarBody(cb);
            FitCondition(cb.Box);
            owner.Conds.Add(cb);
            cb.Complete = true;
            return cb;
        }

        private void ToggleVarFold(RuleBlock cb)
        {
            SetCollapsed(cb.Rule, !IsCollapsed(cb.Rule));
            ShowVarBody(cb);
            RefitBlock(cb);
            _forceFull = true;
        }

        private void ShowVarBody(RuleBlock cb)
        {
            bool folded = IsCollapsed(cb.Rule);
            foreach (Widget w in cb.Body)
            {
                if (w == cb.Header) continue;
                bool on = !folded && !cb.Hidden.Contains(w);
                if (w.SelfActive != on) w.SelfActive = on;
            }
            LogicIcons.Set(cb.Collapse, folded ? LogicIcons.Expand : LogicIcons.Collapse, folded ? "ZtxLogicPanel/Expand" : "ZtxLogicPanel/Collapse");
        }

        private void RefreshCondition(RuleBlock cb, List<Part> members, bool commandable)
        {
            LogicRuleDef def = _engine.Rule(cb.Rule);
            if (!_editor.IsEditing(cb.Name) && cb.Name.Text != def.Name)
            {
                _refreshing = true;
                try { cb.Name.Text = def.Name; }
                finally { _refreshing = false; }
            }
            cb.Name.Display.SelfInputActive = commandable;
            bool paused = _engine.RulePaused(cb.Rule), on = _engine.RuleResult(cb.Rule);
            string word = cb.Lines.Count == 0 ? Strings.GetText("ZtxLogicPanel/RuleEmpty")
                : paused ? Strings.GetText("ZtxLogicPanel/RulePaused")
                : Strings.GetText(on ? "ZtxLogicPanel/SigOn" : "ZtxLogicPanel/SigOff");
            if (cb.Result.Text != word) cb.Result.Text = word;
            string hex = cb.Lines.Count == 0 || paused ? LogicPalette.Dim : on ? LogicPalette.On : LogicPalette.Off;
            if (hex != cb.LastResultHex) { cb.LastResultHex = hex; SetTextColour(cb.Result, hex); }
            int users = ConditionUsers(cb.Rule, 0, null);
            string count = users == 0 ? Strings.GetText("ZtxLogicPanel/VarUnused") : Strings.FormatText("ZtxLogicPanel/VarUsersFmt", users);
            if (cb.Count.Text != count) cb.Count.Text = count;
            cb.Del.SelfInputActive = commandable && users == 0;
            string tip = users == 0 ? Strings.GetText("ZtxLogicPanel/TipDeleteVariable") : Strings.FormatText("ZtxLogicPanel/TipDeleteVariableUsedFmt", users);
            if (cb.Say.Text != tip) cb.Say.Text = tip;
            bool confirm = _confirmVar == cb.Rule && Environment.TickCount - _confirmVarUntil < 0;
            if (confirm != cb.DelConfirm)
            {
                cb.DelConfirm = confirm;
                cb.Del.TextProvider = Strings.KeyString(confirm ? "ZtxLogicPanel/DeleteSure" : "ZtxLogicPanel/DeleteVar");
            }
            cb.AddLine.SelfInputActive = commandable && _engine.FreeLine() > 0;
            string expr = ExprText(cb, def);
            if (cb.Expr.Text != expr) cb.Expr.Text = expr;
            RefreshWarnings(cb, def);
            foreach (LineRow row in cb.Lines) RefreshLine(cb, row, members, commandable);
        }

        private void NewCondition(List<(ID<PartToggleGuiRules>, int)> writes, int c, int function, string name, int line, LogicRef reads)
        {
            name = name ?? "";
            var def = new LogicRuleDef { Exists = true, Joined = true, IsCondition = true, Name = name };
            var raw = new int[_engine.RuleFieldCount];
            raw[ZtxRuleEngineComponent.RULE_T] = LogicRef.Rule(function).Pack();
            raw[ZtxRuleEngineComponent.RULE_M] = def.PackMisc();
            raw[ZtxRuleEngineComponent.RULE_N1] = LogicRuleDef.PackName(name, 0);
            raw[ZtxRuleEngineComponent.RULE_N2] = LogicRuleDef.PackName(name, 5);
            for (int f = 0; f < raw.Length; f++) writes.Add((_engine.RuleSlot(c, f), raw[f]));
            if (line == 0) return;
            var l = new LogicLine { Rule = c, Op = LogicOp.Pass, Format = LogicLine.FORMAT, A = reads };
            writes.Add((_engine.LineSlot(line, ZtxRuleEngineComponent.LINE_M), l.PackMisc()));
            writes.Add((_engine.LineSlot(line, ZtxRuleEngineComponent.LINE_A), l.PackA()));
            writes.Add((_engine.LineSlot(line, ZtxRuleEngineComponent.LINE_B), 0));
            OwnLine(writes, line, c);
        }

        private void ClearConditionWrites(List<(ID<PartToggleGuiRules>, int)> writes, int c)
        {
            for (int k = 1; k <= _engine.LineCount; k++)
            {
                LogicLine line = _engine.Line(k);
                if (!line.IsUsed || line.Rule != c) continue;
                for (int f = 0; f < 3; f++) writes.Add((_engine.LineSlot(k, f), 0));
            }
            for (int f = 0; f < _engine.RuleFieldCount; f++) writes.Add((_engine.RuleSlot(c, f), 0));
        }

        private int ConditionUsers(int c, int skipSeq, ICollection<int> skipSteps)
        {
            int n = 0;
            foreach (int q in _engine.RuleSeqIds(c)) if (q != skipSeq) n++;
            LogicRef me = LogicRef.Rule(c);
            for (int k = 1; k <= _engine.StepCount; k++)
            {
                SeqStep st = _engine.Step(k);
                if (st.IsUsed && IsCondStep(st.Kind) && st.Target.Equals(me) && st.Seq != skipSeq && (skipSteps == null || !skipSteps.Contains(k))) n++;
            }
            for (int k = 1; k <= _engine.LineCount; k++)
            {
                LogicLine l = _engine.Line(k);
                if (l.IsUsed && l.Rule != c && (l.A.Equals(me) || l.B.Equals(me))) n++;
            }
            return n;
        }

        private string FreeVarName(int f)
        {
            var names = new List<string>();
            foreach (int c in ConditionsOf(f)) names.Add(_engine.Rule(c).Name ?? "");
            return LogicVariables.FreeName(names, "VAR", LogicRuleDef.NAME_CHARS);
        }

        private int NewVariable(List<(ID<PartToggleGuiRules>, int)> writes, int f)
        {
            List<int> slots = _engine.FreeRules(1);
            int line = _engine.FreeLine();
            if (slots.Count == 0 || line == 0) return 0;
            NewCondition(writes, slots[0], f, FreeVarName(f), line, LogicRef.None);
            return slots[0];
        }

        private int DefaultVariable(List<(ID<PartToggleGuiRules>, int)> writes, int f, int prefer)
        {
            if (IsConditionRule(prefer) && _engine.Rule(prefer).FunctionOf == f) return prefer;
            List<int> vars = ConditionsOf(f);
            return vars.Count > 0 ? vars[0] : NewVariable(writes, f);
        }

        private void AddVariable(int f)
        {
            try
            {
                if (_engine == null || !_engine.IsStoreV2) return;
                var writes = new List<(ID<PartToggleGuiRules>, int)>();
                int v = NewVariable(writes, f);
                if (v == 0) return;
                Write(writes.ToArray());
                Log.Info("console: function " + f + ": variable " + v + " added.");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.AddVariable", ex); }
        }

        private int _confirmVar, _confirmVarUntil;

        private void OnDeleteVarClicked(RuleBlock cb)
        {
            _forceFull = true;
            if (_confirmVar == cb.Rule && Environment.TickCount - _confirmVarUntil < 0)
            {
                _confirmVar = 0;
                DeleteVariable(cb.Rule);
                return;
            }
            _confirmVar = cb.Rule;
            _confirmVarUntil = Environment.TickCount + DELETE_CONFIRM_MS;
        }

        private void DeleteVariable(int c)
        {
            try
            {
                if (_engine == null || !IsConditionRule(c)) return;
                int users = ConditionUsers(c, 0, null);
                if (users > 0) { Log.Info("console: variable " + c + " is used by " + users + "; not deleted."); return; }
                var writes = new List<(ID<PartToggleGuiRules>, int)>();
                ClearConditionWrites(writes, c);
                Write(writes.ToArray());
                SetCollapsed(c, false);
                Log.Info("console: variable " + c + " deleted.");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.DeleteVariable", ex); }
        }

        private void HandlerWrites(List<(ID<PartToggleGuiRules>, int)> writes, int q, int owner, HandlerTrigger trigger, int pos)
        {
            var def = new SeqDef { Rule = owner, Trigger = trigger, Pos = pos };
            writes.Add((_engine.SeqSlot(q, LogicStore.SEQ_M), def.PackM()));
            writes.Add((_engine.SeqSlot(q, LogicStore.SEQ_R), owner));
            SetSeqOpen(q, true);
        }

        private void AddFunction(bool atStart)
        {
            try
            {
                if (_engine == null || !_engine.IsStoreV2) return;
                List<int> slots = _engine.FreeRules(2);
                int line = _engine.FreeLine();
                List<int> seqs = _engine.FreeSeqs(1);
                if (slots.Count < 2 || line == 0 || seqs.Count == 0) return;
                string name = FreeName("FUNCTION");
                int c = slots[1], q = seqs[0];
                string first = LogicVariables.FreeName(new List<string>(), "VAR", LogicRuleDef.NAME_CHARS);
                int home = InsertRule(new LogicRuleDef { Exists = true, Joined = true, Name = name }, atStart, withLine: false, (writes, f) =>
                {
                    NewCondition(writes, c, f, first, line, LogicRef.None);
                    HandlerWrites(writes, q, c, HandlerTrigger.WhenOn, 0);
                });
                Log.Info("console: function " + home + " added as " + name + " with a WHEN block (condition " + c + ", block " + q + ")" + (atStart ? " at the top" : " at the bottom") + ".");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.AddFunction", ex); }
        }

        private void AddSignal(bool atStart)
        {
            try
            {
                if (_engine == null || _engine.FreeRule() == 0) return;
                string name = FreeName("SIGNAL");
                int home = InsertRule(new LogicRuleDef { Exists = true, Joined = true, Name = name }, atStart, withLine: true);
                Log.Info("console: signal " + home + " added as " + name + (atStart ? " at the top" : " at the bottom") + ".");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.AddSignal", ex); }
        }

        private void AddTopBlock(int f, HandlerTrigger trigger)
        {
            try
            {
                if (_engine == null || !_engine.IsStoreV2) return;
                List<int> seqs = _engine.FreeSeqs(1);
                if (seqs.Count == 0) return;
                var writes = new List<(ID<PartToggleGuiRules>, int)>();
                int owner = HandlerRun.NoVariable(trigger) ? f : DefaultVariable(writes, f, 0);
                if (owner == 0) return;
                HandlerWrites(writes, seqs[0], owner, trigger, NextOutputPos(f));
                Write(writes.ToArray());
                Log.Info("console: function " + f + ": block " + seqs[0] + " added (" + trigger + (owner != f ? ", variable " + owner : "") + ").");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.AddTopBlock", ex); }
        }

        private void AddTopCode(int f, int choice)
        {
            try
            {
                if (_engine == null || !_engine.IsStoreV2) return;
                List<SeqKind> kinds = InsertKinds(choice);
                List<int> seqs = _engine.FreeSeqs(1), steps = _engine.FreeSteps(kinds.Count);
                if (kinds.Count == 0 || seqs.Count == 0 || steps.Count < kinds.Count) return;
                var writes = new List<(ID<PartToggleGuiRules>, int)>();
                int v = kinds.Exists(IsCondStep) ? DefaultVariable(writes, f, 0) : 0;
                if (kinds.Exists(IsCondStep) && v == 0) return;
                int q = seqs[0];
                HandlerWrites(writes, q, f, HandlerTrigger.Always, NextOutputPos(f));
                for (int i = 0; i < kinds.Count; i++)
                {
                    int id = steps[i];
                    var s = new SeqStep { Seq = q, Kind = kinds[i], Order = i, Pose = kinds[i] == SeqKind.Repeat ? 2 : 0 };
                    writes.Add((_engine.StepSlot(id, ZtxRuleEngineComponent.STEP_T), IsCondStep(kinds[i]) ? LogicRef.Rule(v).Pack() : 0));
                    writes.Add((_engine.StepSlot(id, ZtxRuleEngineComponent.STEP_M), s.PackM()));
                    writes.Add((_engine.StepSlot(id, ZtxRuleEngineComponent.STEP_P), s.PackP()));
                    writes.Add((_engine.StepSlot(id, ZtxRuleEngineComponent.STEP_Q), 0));
                    writes.Add((_engine.StepSlot(id, ZtxRuleEngineComponent.STEP_V), 0));
                    writes.Add((_engine.StepOwnerSlot(id), q));
                }
                Write(writes.ToArray());
                Log.Info("console: function " + f + ": top-level " + string.Join(" / ", kinds) + " added as ALWAYS block " + q + ".");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.AddTopCode", ex); }
        }

        private void BuildAddTop(RuleBlock block)
        {
            RuleBlock captured = block;
            LayoutBox row = Row(InnerWidth);
            row.DockedChildren.Add(new Widget { DockMode = DockMode.Left, Width = IndentWidth });
            block.AddTop = MakeList(ButtonWidth, "ZtxLogicPanel/TipAddTopBlock", v => OnAddTopPicked(captured, v), partHover: false);
            FillKeyed(block.AddTop, new List<(int, string)>
            {
                (0, "AddTopBlock"),
                (1 + HandlerRun.WHEN, "HandlerWhen"), (1 + HandlerRun.WHILE, "HandlerWhile"), (1 + HandlerRun.ALWAYS, "HandlerAlways"),
                (1 + 7, "HandlerOnCall"),
                (1 + 3, "BlockIf"), (1 + 4, "BlockIfElse"), (1 + 5, "BlockRepeat"), (1 + 6, "BlockTogether"),
            });
            Select(block.AddTop, 0);
            SetTextColour(block.AddTop.Button, RoleHex(ButtonRole.Add));
            row.DockedChildren.Add(block.AddTop.Button);
            AddBody(block, row);
        }

        private void OnAddTopPicked(RuleBlock block, int value)
        {
            if (value >= 1 && value <= 3) AddTopBlock(block.Rule, HandlerRun.Compose(value - 1, false));
            else if (value == 8) AddTopBlock(block.Rule, HandlerTrigger.OnCall);
            else if (value >= 4 && value <= 7) AddTopCode(block.Rule, value == 4 ? INSERT_IF : value == 5 ? INSERT_IF + 1 : value == 6 ? INSERT_IF + 2 : INSERT_IF + 5);
            Select(block.AddTop, 0);
        }

        private void ConvertCommandSteps()
        {
            try
            {
                var found = new List<(int Step, SeqStep S, LogicAction A)>();
                for (int q = 1; q <= _engine.SeqCount; q++)
                {
                    SeqDef def = _engine.Seq(q);
                    if (!def.IsUsed) continue;
                    foreach (int id in _engine.SeqStepIds(q))
                    {
                        SeqStep st = _engine.Step(id);
                        if (st.Kind != SeqKind.Command || !st.Target.IsPart) continue;
                        Part p = LogicNet.Resolve(_console.Ship, _engine.Members, st.Target);
                        if (p == null) continue;
                        if (!LogicSetStatement.FromCommand(st, LogicAction.KindOf(p), def.Rule, out LogicAction a)) continue;
                        found.Add((id, st, a));
                    }
                }
                if (found.Count == 0) return;
                List<int> slots = _engine.FreeActions(found.Count);
                if (slots.Count < found.Count) return;
                var writes = new List<(ID<PartToggleGuiRules>, int)>();
                for (int i = 0; i < found.Count; i++)
                {
                    var (id, st, a) = found[i];
                    int k = slots[i];
                    writes.Add((_engine.ActionSlot(k, ZtxRuleEngineComponent.ACT_T), a.Target.Pack()));
                    writes.Add((_engine.ActionSlot(k, ZtxRuleEngineComponent.ACT_M), a.PackMisc()));
                    writes.Add((_engine.ActionSlot(k, ZtxRuleEngineComponent.ACT_P), a.PackPose()));
                    OwnAct(writes, k, a.Rule);
                    st.Settle = !st.NoWait;
                    st.NoWait = false;
                    st.Kind = SeqKind.Do;
                    st.Target = LogicRef.None;
                    writes.Add((_engine.StepSlot(id, ZtxRuleEngineComponent.STEP_T), 0));
                    writes.Add((_engine.StepSlot(id, ZtxRuleEngineComponent.STEP_M), st.PackM()));
                    writes.Add((_engine.StepSlot(id, ZtxRuleEngineComponent.STEP_V), k));
                    SetExpanded(k, false);
                }
                Write(writes.ToArray());
                Log.Info("console " + _console?.Location + ": converted " + found.Count + " piston / motor / hinge steps into Set statements (v1.41.0).");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.ConvertCommandSteps", ex); }
        }

        private void ConvertDoLines()
        {
            try
            {
                var plans = new List<(int Rule, List<(int Trigger, int Pos, List<int> Slots)> Groups)>();
                int handlers = 0, statements = 0;
                for (int r = 1; r <= _engine.RuleCount; r++)
                {
                    LogicRuleDef def = _engine.Rule(r);
                    if (!def.Exists || def.IsHeader || def.IsCondition) continue;
                    var acts = new List<(int, bool, bool, bool, int)>();
                    foreach (int k in _engine.RuleActionSlots(r))
                    {
                        if (_engine.ActionInBody(k)) continue;
                        LogicAction a = _engine.Action(k);
                        if (a.IsUsed) acts.Add((k, a.Sided, a.WhenOff, a.Hold, a.Pos));
                    }
                    if (acts.Count == 0) continue;
                    var groups = HandlerConversion.Plan(acts);
                    if (groups.Count == 0) continue;
                    plans.Add((r, groups));
                    handlers += groups.Count;
                    foreach (var g in groups) statements += g.Slots.Count;
                }
                if (handlers == 0) return;
                List<int> seqs = _engine.FreeSeqs(handlers), steps = _engine.FreeSteps(statements);
                var writes = new List<(ID<PartToggleGuiRules>, int)>();
                int qi = 0, si = 0;
                foreach (var (rule, groups) in plans)
                    foreach (var (trigger, pos, slots) in groups)
                    {
                        int q = seqs[qi++];
                        var def = new SeqDef { Rule = rule, Trigger = (HandlerTrigger)trigger, Pos = pos };
                        writes.Add((_engine.SeqSlot(q, LogicStore.SEQ_M), def.PackM()));
                        writes.Add((_engine.SeqSlot(q, LogicStore.SEQ_R), rule));
                        SetSeqOpen(q, true);
                        int order = 0;
                        foreach (int k in slots)
                        {
                            int id = steps[si++];
                            var s = new SeqStep { Seq = q, Kind = SeqKind.Do, Order = order++ };
                            writes.Add((_engine.StepSlot(id, ZtxRuleEngineComponent.STEP_T), 0));
                            writes.Add((_engine.StepSlot(id, ZtxRuleEngineComponent.STEP_M), s.PackM()));
                            writes.Add((_engine.StepSlot(id, ZtxRuleEngineComponent.STEP_P), 0));
                            writes.Add((_engine.StepSlot(id, ZtxRuleEngineComponent.STEP_Q), 0));
                            writes.Add((_engine.StepSlot(id, ZtxRuleEngineComponent.STEP_V), k));
                            writes.Add((_engine.StepOwnerSlot(id), q));
                        }
                    }
                Write(writes.ToArray());
                Log.Info("console " + _console?.Location + ": converted " + statements + " DO lines of " + plans.Count + " rules into " + handlers + " handlers (functions, v1.39.0).");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.ConvertDoLines", ex); }
        }

        private void ConvertBodies()
        {
            try
            {
                var rules = new List<int>();
                for (int r = 1; r <= _engine.RuleCount; r++)
                {
                    LogicRuleDef def = _engine.Rule(r);
                    if (!def.Exists || def.IsHeader || def.IsCondition) continue;
                    if (_engine.RuleLineCount(r) == 0 || !HasGatedBlock(r)) continue;
                    if (def.Target.IsPart) { Log.Info("console " + _console?.Location + ": rule " + r + " still drives a part directly; left as it is."); continue; }
                    rules.Add(r);
                }
                var steps = new List<int>();
                for (int k = 1; k <= _engine.StepCount; k++)
                {
                    SeqStep st = _engine.Step(k);
                    if (!st.IsUsed || !IsCondStep(st.Kind) || !_engine.Seq(st.Seq).IsUsed) continue;
                    if (st.Target.IsRule && IsConditionRule(st.Target.Index)) continue;
                    steps.Add(k);
                }
                if (rules.Count == 0 && steps.Count == 0) return;
                List<int> slots = _engine.FreeRules(rules.Count + steps.Count);
                List<int> lines = _engine.FreeLines(steps.Count);
                if (slots.Count < rules.Count + steps.Count || lines.Count < steps.Count) return;
                var gateOf = new Dictionary<int, int>();
                for (int i = 0; i < rules.Count; i++) gateOf[rules[i]] = slots[i];
                var writes = new List<(ID<PartToggleGuiRules>, int)>();
                foreach (KeyValuePair<int, int> kv in gateOf) ConvertGate(writes, kv.Key, kv.Value, gateOf);
                LogicRef Gate(LogicRef x) => x.IsRule && gateOf.TryGetValue(x.Index, out int g) ? LogicRef.Rule(g) : x;
                for (int k = 1; k <= _engine.LineCount; k++)
                {
                    LogicLine l = _engine.Line(k);
                    if (!l.IsUsed || gateOf.ContainsKey(l.Rule)) continue;
                    LogicRef a = Gate(l.A), b = Gate(l.B);
                    if (a.Equals(l.A) && b.Equals(l.B)) continue;
                    l.A = a;
                    l.B = b;
                    writes.Add((_engine.LineSlot(k, ZtxRuleEngineComponent.LINE_A), l.PackA()));
                    writes.Add((_engine.LineSlot(k, ZtxRuleEngineComponent.LINE_B), l.PackB()));
                }
                ConvertStepConditions(writes, steps, slots.GetRange(rules.Count, steps.Count), lines, gateOf);
                Write(writes.ToArray());
                Log.Info("console " + _console?.Location + ": converted " + rules.Count + " gates into conditions of their functions and gave "
                    + steps.Count + " IF / WHILE / WAIT UNTIL statements their own condition (v1.43.0).");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.ConvertBodies", ex); }
        }

        private bool HasGatedBlock(int r)
        {
            foreach (int q in _engine.RuleSeqIds(r)) if (_engine.Seq(q).Trigger != HandlerTrigger.Always) return true;
            return false;
        }

        private void ConvertGate(List<(ID<PartToggleGuiRules>, int)> writes, int r, int c, Dictionary<int, int> gateOf)
        {
            LogicRuleDef def = _engine.Rule(r);
            var cond = new LogicRuleDef { Exists = true, Joined = def.Joined, Shared = def.Shared, IsCondition = true, Name = def.Name };
            var raw = new int[_engine.RuleFieldCount];
            raw[ZtxRuleEngineComponent.RULE_T] = LogicRef.Rule(r).Pack();
            raw[ZtxRuleEngineComponent.RULE_M] = cond.PackMisc();
            raw[ZtxRuleEngineComponent.RULE_N1] = _engine.RawRule(r, ZtxRuleEngineComponent.RULE_N1);
            raw[ZtxRuleEngineComponent.RULE_N2] = _engine.RawRule(r, ZtxRuleEngineComponent.RULE_N2);
            for (int f = 0; f < raw.Length; f++) writes.Add((_engine.RuleSlot(c, f), raw[f]));
            def.Shared = false;
            writes.Add((_engine.RuleSlot(r, ZtxRuleEngineComponent.RULE_M), def.PackMisc()));
            for (int k = 1; k <= _engine.LineCount; k++)
            {
                LogicLine l = _engine.Line(k);
                if (!l.IsUsed || l.Rule != r) continue;
                l.Rule = c;
                if (l.A.IsRule && gateOf.TryGetValue(l.A.Index, out int ga)) l.A = LogicRef.Rule(ga);
                if (l.B.IsRule && gateOf.TryGetValue(l.B.Index, out int gb)) l.B = LogicRef.Rule(gb);
                writes.Add((_engine.LineSlot(k, ZtxRuleEngineComponent.LINE_A), l.PackA()));
                writes.Add((_engine.LineSlot(k, ZtxRuleEngineComponent.LINE_B), l.PackB()));
                writes.Add((_engine.LineSlot(k, ZtxRuleEngineComponent.LINE_M), l.PackMisc()));
                OwnLine(writes, k, c);
            }
            foreach (int q in _engine.RuleSeqIds(r))
                if (_engine.Seq(q).Trigger != HandlerTrigger.Always) writes.Add((_engine.SeqSlot(q, LogicStore.SEQ_R), c));
        }

        private void ConvertStepConditions(List<(ID<PartToggleGuiRules>, int)> writes, List<int> steps, List<int> slots, List<int> lines, Dictionary<int, int> gateOf)
        {
            var made = new Dictionary<(int, int), int>();
            int next = 0;
            for (int i = 0; i < steps.Count; i++)
            {
                int id = steps[i];
                SeqStep st = _engine.Step(id);
                LogicRef source = st.Target;
                if (source.IsLocal)
                {
                    int owner = _engine.LineOwner(source.Index);
                    source = owner >= 1 ? LogicRef.Rule(owner) : LogicRef.None;
                }
                LogicRef reads = source.IsRule && gateOf.TryGetValue(source.Index, out int g) ? LogicRef.Rule(g) : source;
                int f = OwnerFunction(_engine.Seq(st.Seq).Rule);
                if (!made.TryGetValue((f, reads.Pack()), out int v))
                {
                    v = slots[next];
                    string name = source.IsRule && source.Index <= _engine.RuleCount ? _engine.Rule(source.Index).Name : "";
                    NewCondition(writes, v, f, string.IsNullOrEmpty(name) ? "VAR" : name, lines[next], reads);
                    next++;
                    made[(f, reads.Pack())] = v;
                }
                writes.Add((_engine.StepSlot(id, ZtxRuleEngineComponent.STEP_T), LogicRef.Rule(v).Pack()));
            }
        }

        private void ConvertVariableNames()
        {
            try
            {
                var writes = new List<(ID<PartToggleGuiRules>, int)>();
                int renamed = 0;
                for (int f = 1; f <= _engine.RuleCount; f++)
                {
                    LogicRuleDef d = _engine.Rule(f);
                    if (!d.Exists || d.IsHeader || d.IsCondition) continue;
                    List<int> vars = ConditionsOf(f);
                    if (vars.Count < 2) continue;
                    var names = new List<string>();
                    foreach (int v in vars) names.Add(_engine.Rule(v).Name ?? "");
                    List<string> unique = LogicVariables.UniqueNames(names, LogicRuleDef.NAME_CHARS);
                    for (int i = 0; i < vars.Count; i++)
                    {
                        if (unique[i] == names[i]) continue;
                        writes.Add((_engine.RuleSlot(vars[i], ZtxRuleEngineComponent.RULE_N1), LogicRuleDef.PackName(unique[i], 0)));
                        writes.Add((_engine.RuleSlot(vars[i], ZtxRuleEngineComponent.RULE_N2), LogicRuleDef.PackName(unique[i], 5)));
                        renamed++;
                    }
                }
                if (renamed == 0) return;
                Write(writes.ToArray());
                Log.Info("console " + _console?.Location + ": numbered " + renamed + " variables that shared a name inside their function (v1.44.0).");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.ConvertVariableNames", ex); }
        }
    }
}
