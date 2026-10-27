using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
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
        private enum DragKind { None, Rule, Group, Line, Output, Step }

        private static float GripWidth => 22f * LogicUiScale.Rules;
        private static float DropLineHeight => 3f * LogicUiScale.Rules;
        private static float EdgeScroll => 40f * LogicUiScale.Rules;
        private static float ScrollStep => 14f * LogicUiScale.Rules;

        private DragKind _dragKind;
        private RuleBlock _dragBlock;
        private LineRow _dragLine;
        private float _dragY = float.NaN;
        private float _dropY = float.NaN, _dropX, _dropW;
        private int _dropBefore;
        private int _dropGroup;
        private int _dragOutKind, _dragOutId;
        private SeqBlock _dragSeq;
        private int _dragStepId;
        private RuleBlock _dropRule;
        private bool _dropOk;
        private bool _dragLogged;
        private Label _dragNote;

        private void AttachDropPainter()
        {
            _page.Components.Add(new WidgetDelegatedRenderer(DrawDrop, _page.NineSlice));
            _dragNote = new Label { DockMode = DockMode.Top, Height = RowHeight };
            _dragNote.TextRenderer.XmlFormatting = true;
            _dragNote.TextRenderer.HAlignment = HAlignment.Left;
            _dragNote.TextRenderer.OversizeMode = OversizeMode.Shrink;
            _dragNote.SelfActive = false;
            _page.DockedChildren.Add(_dragNote);
        }

        private void AddGrip(LayoutBox row, Action start)
        {
            var grip = new LogicDragGrip { Started = start, Moved = OnDragMoved, Ended = OnDragEnded };
            LayoutBox box = LogicDragGrip.Make(grip, GripWidth, RowHeight, LogicPalette.Dim);
            box.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipDragGrip");
            row.DockedChildren.Add(box);
        }

        private bool CanDrag() => _engine != null && _console?.Ship != null && _console.Ship.Commands.IsCommandableByLocalPlayer;

        private void StartRuleDrag(RuleBlock b)
        {
            if (!CanDrag()) return;
            BeginDrag(b.IsGroup ? DragKind.Group : DragKind.Rule, b, null);
        }

        private void StartLineDrag(RuleBlock b, LineRow row)
        {
            if (!CanDrag()) return;
            BeginDrag(DragKind.Line, b, row);
        }

        private void StartOutputDrag(RuleBlock b, int kind, int id)
        {
            if (!CanDrag() || !_engine.IsStoreV2) return;
            BeginDrag(DragKind.Output, b, null);
            _dragOutKind = kind;
            _dragOutId = id;
        }

        private void StartStepDrag(SeqBlock sb, int stepId)
        {
            if (!CanDrag()) return;
            BeginDrag(DragKind.Step, sb.Block, null);
            _dragSeq = sb;
            _dragStepId = stepId;
        }

        private void ComputeStepDrop()
        {
            SeqBlock sb = _dragSeq;
            var gaps = new List<(int, float)>();
            Widget lastBox = null, lastDetail = null;
            Widget mine = null;
            foreach (var (id, box, detail) in sb.Items)
            {
                if (id == _dragStepId) mine = box;
                if (!box.SelfActive) continue;
                gaps.Add((id, box.ScreenRect.Y - GapY / 2f));
                lastBox = box;
                lastDetail = detail;
            }
            if (lastBox != null)
            {
                Widget end = lastDetail != null && lastDetail.SelfActive ? lastDetail : lastBox;
                gaps.Add((0, end.ScreenRect.Y + end.ScreenRect.Height + GapY / 2f));
            }
            _dropRule = null;
            PickGap(gaps);
            if (mine != null) { _dropX = mine.ScreenRect.X; _dropW = mine.ScreenRect.Width; }
            _dropOk = !float.IsNaN(_dropY) && PlanStepMove(sb, _dragStepId, _dropBefore, out _);
        }

        private bool PlanStepMove(SeqBlock sb, int stepId, int before, out List<int> desired)
        {
            var ids = new List<int>(_engine.SeqStepIds(sb.Id));
            var kinds = new List<int>(ids.Count);
            foreach (int k in ids) kinds.Add((int)_engine.Step(k).Kind);
            var moving = new List<int>();
            foreach (int i in LogicFlow.BlockSpan(kinds, ids.IndexOf(stepId))) if (i >= 0 && i < ids.Count) moving.Add(ids[i]);
            desired = LogicGroups.MovedBefore(ids, moving, before);
            if (SameOrder(ids, desired)) return false;
            var after = new List<int>(desired.Count);
            foreach (int k in desired) after.Add((int)_engine.Step(k).Kind);
            return LogicFlow.Valid(after, LogicFlow.MAX_DEPTH);
        }

        private void MoveStepBefore(SeqBlock sb, int stepId, int before)
        {
            if (!PlanStepMove(sb, stepId, before, out List<int> desired)) return;
            var writes = new List<(ID<PartToggleGuiRules>, int)>();
            for (int k = 0; k < desired.Count; k++)
            {
                SeqStep s = _engine.Step(desired[k]);
                if (s.Order == k) continue;
                s.Order = k;
                writes.Add((_engine.StepSlot(desired[k], ZtxRuleEngineComponent.STEP_M), s.PackM()));
            }
            if (writes.Count == 0) return;
            Write(writes.ToArray());
            Log.Info("console: handler " + sb.Id + " statement " + stepId + " moved " + (before == 0 ? "to the end" : "before statement " + before) + ".");
        }

        private void BeginDrag(DragKind kind, RuleBlock b, LineRow row)
        {
            _picker.Close();
            _editor.Close();
            _dragKind = kind;
            _dragBlock = b;
            _dragLine = row;
            _dropY = float.NaN;
            _dragY = float.NaN;
            _dragLogged = false;
        }

        private void OnDragMoved(Vector2 cursor)
        {
            if (_dragKind == DragKind.None) return;
            try
            {
                _dragY = cursor.Y;
                ComputeDrop();
                if (!_dragLogged)
                {
                    _dragLogged = true;
                    Rect from = (_dragLine != null ? _dragLine.Box : _dragBlock.Header).ScreenRect;
                    Log.Info("console: drag " + _dragKind + " from rule " + _dragBlock.Rule + (_dragLine != null ? " line " + _dragLine.Line : "")
                        + ": cursor " + cursor.X.ToString("0") + "," + cursor.Y.ToString("0") + ", grip row " + from.X.ToString("0") + "," + from.Y.ToString("0")
                        + " " + from.Width.ToString("0") + "x" + from.Height.ToString("0") + ", drop before " + _dropBefore + " at y " + _dropY.ToString("0")
                        + (_dropOk ? "" : " (refused)") + ".");
                }
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnDragMoved", ex); }
        }

        private void RefreshDrag()
        {
            if (_dragKind == DragKind.None)
            {
                LogicGui.SetActive(_dragNote, false);
                return;
            }
            if (float.IsNaN(_dragY)) return;
            Rect view = _page.ScrollBarController.ViewRectProvider.ScreenRect;
            if (_dragY < view.Y + EdgeScroll) _page.ScrollBarController.ScrollVertical(-ScrollStep);
            else if (_dragY > view.Y + view.Height - EdgeScroll) _page.ScrollBarController.ScrollVertical(ScrollStep);
            ComputeDrop();
        }

        private void ComputeDrop()
        {
            if (_dragKind == DragKind.Line) ComputeLineDrop();
            else if (_dragKind == DragKind.Output) ComputeOutputDrop();
            else if (_dragKind == DragKind.Step) ComputeStepDrop();
            else ComputeRuleDrop();
            ShowDragNote();
        }

        private Widget OutputTop(RuleBlock b, int kind, int id)
        {
            if (kind == LogicOutputStack.DO) return b.Acts.Find(a => a.Slot == id)?.Box;
            return b.Seqs.Find(s => s.Id == id)?.Box;
        }

        private float OutputBottom(RuleBlock b, int kind, int id)
        {
            var parts = new List<Widget>();
            if (kind == LogicOutputStack.DO)
            {
                ActRow row = b.Acts.Find(a => a.Slot == id);
                if (row != null) { parts.Add(row.Box); parts.Add(row.Settings); }
            }
            else
            {
                SeqBlock sb = b.Seqs.Find(s => s.Id == id);
                if (sb != null) parts.Add(sb.Box);
            }
            float bottom = float.NaN;
            foreach (Widget w in parts)
            {
                if (w == null || !w.SelfActive) continue;
                float y = w.ScreenRect.Y + w.ScreenRect.Height;
                if (float.IsNaN(bottom) || y > bottom) bottom = y;
            }
            return bottom;
        }

        private void ComputeOutputDrop()
        {
            RuleBlock b = _dragBlock;
            OutputLists(b.Rule, out List<int> kinds, out List<int> ids, out List<int> pos);
            List<int> order = LogicOutputStack.Order(kinds, ids, pos);
            int item = -1;
            for (int i = 0; i < kinds.Count; i++) if (kinds[i] == _dragOutKind && ids[i] == _dragOutId) item = i;
            var gaps = new List<(int, float)>();
            int last = -1;
            foreach (int i in order)
            {
                Widget top = OutputTop(b, kinds[i], ids[i]);
                if (top == null || !top.SelfActive) continue;
                gaps.Add((i, top.ScreenRect.Y - GapY / 2f));
                last = i;
            }
            if (last >= 0)
            {
                float bottom = OutputBottom(b, kinds[last], ids[last]);
                if (!float.IsNaN(bottom)) gaps.Add((-1, bottom + GapY / 2f));
            }
            _dropRule = null;
            _dropOk = false;
            PickGap(gaps);
            Widget anchor = item >= 0 ? OutputTop(b, kinds[item], ids[item]) : null;
            if (anchor != null) { _dropX = anchor.ScreenRect.X; _dropW = anchor.ScreenRect.Width; }
            _dropOk = item >= 0 && !float.IsNaN(_dropY) && LogicOutputStack.MoveBeforeWrites(kinds, ids, pos, item, _dropBefore).Count > 0;
        }

        private void MoveOutputBefore(RuleBlock b, int kind, int id, int before)
        {
            OutputLists(b.Rule, out List<int> kinds, out List<int> ids, out List<int> pos);
            int item = -1;
            for (int i = 0; i < kinds.Count; i++) if (kinds[i] == kind && ids[i] == id) item = i;
            if (item < 0) return;
            var writes = new List<(ID<PartToggleGuiRules>, int)>();
            foreach ((int i, int p) in LogicOutputStack.MoveBeforeWrites(kinds, ids, pos, item, before))
            {
                if (kinds[i] == LogicOutputStack.DO) writes.Add((_engine.ActionPosSlot(ids[i]), p));
                else
                {
                    SeqDef def = _engine.Seq(ids[i]);
                    def.Pos = p;
                    writes.Add((_engine.SeqSlot(ids[i], LogicStore.SEQ_M), def.PackM()));
                }
            }
            if (writes.Count == 0) return;
            Write(writes.ToArray());
            Log.Info("console: rule " + b.Rule + " " + (kind == LogicOutputStack.DO ? "DO slot " : "sequence ") + id + " moved "
                + (before < 0 ? "to the end" : "before output block " + before) + ".");
        }

        private List<RuleBlock> VisibleBlocks()
        {
            var list = new List<RuleBlock>();
            foreach (RuleBlock b in _blocks) foreach (RuleBlock c in b.Conds) if (c.Complete && IsShown(c)) list.Add(c);
            foreach (RuleBlock b in _blocks) if (b.Complete && IsShown(b)) list.Add(b);
            return list;
        }

        private RuleBlock BlockOf(int rule) => _blocks.Find(x => x.Rule == rule);

        private static Rect TargetRect(RuleBlock b) => b.IsGroup ? b.Header.ScreenRect : b.Box.ScreenRect;

        private void ComputeRuleDrop()
        {
            List<int> order = ExistingRules();
            bool groupDrag = _dragKind == DragKind.Group;
            _dropY = float.NaN;
            float best = float.MaxValue;
            foreach (var (before, group, anchor, edge) in LogicGroups.Gaps(order, IsHeaderRule, InGroupRule, IsCollapsed, groupDrag))
            {
                RuleBlock a = BlockOf(anchor);
                if (a == null || !a.Complete || !IsShown(a)) continue;
                Rect box = edge == LogicGroups.EDGE_HEADER ? a.Header.ScreenRect : a.Box.ScreenRect;
                float y = edge == LogicGroups.EDGE_TOP ? box.Y - GapY : box.Y + box.Height + GapY;
                float dist = Math.Abs(y - _dragY);
                if (dist >= best) continue;
                best = dist;
                _dropBefore = before;
                _dropGroup = group;
                _dropY = y;
                _dropX = box.X;
                _dropW = box.Width;
            }
            List<int> items = groupDrag ? LogicGroups.Block(order, _dragBlock.Rule, IsHeaderRule, InGroupRule) : new List<int> { _dragBlock.Rule };
            bool groupChanges = !groupDrag && (_dropGroup != 0) != _engine.Rule(_dragBlock.Rule).InGroup;
            _dropOk = !float.IsNaN(_dropY) && (groupChanges || !SameOrder(order, LogicGroups.MovedBefore(order, items, _dropBefore)));
            _dropRule = null;
        }

        private void PickGap(List<(int Before, float Y)> gaps)
        {
            _dropY = float.NaN;
            float best = float.MaxValue;
            foreach (var (before, y) in gaps)
            {
                float d = Math.Abs(y - _dragY);
                if (d >= best) continue;
                best = d;
                _dropBefore = before;
                _dropY = y;
            }
            RuleBlock any = _blocks.Count > 0 ? _blocks[0] : null;
            if (any != null) { _dropX = any.Box.ScreenRect.X; _dropW = any.Box.ScreenRect.Width; }
        }

        private void ComputeLineDrop()
        {
            List<RuleBlock> visible = VisibleBlocks();
            RuleBlock target = null;
            float best = float.MaxValue;
            foreach (RuleBlock b in visible)
            {
                Rect r = TargetRect(b);
                float d = _dragY < r.Y ? r.Y - _dragY : _dragY > r.Y + r.Height ? _dragY - r.Y - r.Height : 0f;
                if (d < best) { best = d; target = b; }
            }
            _dropRule = target;
            _dropOk = false;
            _dropY = float.NaN;
            if (target == null) return;
            Rect box = TargetRect(target);
            _dropX = box.X;
            _dropW = box.Width;
            if (target.IsGroup || (target.IsFunction && target.Lines.Count == 0)) { _dropY = box.Y + box.Height / 2f; _dropBefore = 0; return; }
            var gaps = new List<(int, float)>();
            if (target.BodyBuilt && !IsCollapsed(target.Rule))
            {
                foreach (LineRow row in target.Lines) gaps.Add((row.Line, row.Box.ScreenRect.Y - GapY / 2f));
                if (target.Lines.Count > 0) { Rect last = target.Lines[target.Lines.Count - 1].Box.ScreenRect; gaps.Add((0, last.Y + last.Height + GapY / 2f)); }
            }
            if (gaps.Count == 0) gaps.Add((0, box.Y + box.Height - GapY));
            float keepX = _dropX, keepW = _dropW;
            PickGap(gaps);
            _dropX = keepX;
            _dropW = keepW;
            _dropOk = target.Rule == _dragLine.Rule ? _dropBefore != _dragLine.Line && NextLine(target, _dragLine.Line) != _dropBefore : LineLinks(_dragLine.Line, _dragLine.Rule).Count == 0;
        }

        private static int NextLine(RuleBlock b, int line)
        {
            for (int i = 0; i < b.Lines.Count; i++)
                if (b.Lines[i].Line == line) return i + 1 < b.Lines.Count ? b.Lines[i + 1].Line : 0;
            return -1;
        }

        private List<int> RuleLines(int rule)
        {
            var slots = new List<int>();
            for (int s = 1; s <= _engine.LineCount; s++)
            {
                LogicLine l = _engine.Line(s);
                if (l.IsUsed && l.Rule == rule) slots.Add(s);
            }
            return slots;
        }

        private List<int> LineLinks(int line, int rule)
        {
            var steps = new List<int>();
            foreach (int q in _engine.RuleSeqIds(rule))
                foreach (int sk in _engine.SeqStepIds(q))
                {
                    SeqStep st = _engine.Step(sk);
                    if (st.IsUsed && st.Kind == SeqKind.WaitUntil && st.Target.IsLocal) steps.Add(st.Target.Index);
                }
            return LogicLineOrder.LinksOf(line, RuleLines(rule), s => _engine.Line(s).A.IsLocal ? _engine.Line(s).A.Index : 0,
                s => _engine.Line(s).B.IsLocal ? _engine.Line(s).B.Index : 0, steps);
        }

        private void ShowDragNote()
        {
            string text;
            if (_dragKind == DragKind.Line && _dropRule != null && _dropRule.IsGroup)
                text = LogicPalette.Wrap(LogicPalette.Warn, Strings.GetText("ZtxLogicPanel/DropNotGroup"));
            else if (_dragKind == DragKind.Line && _dropRule != null && _dropRule.IsFunction && _dropRule.Lines.Count == 0)
                text = LogicPalette.Wrap(LogicPalette.Warn, Strings.GetText("ZtxLogicPanel/DropNotFunction"));
            else if (_dragKind == DragKind.Line && _dropRule != null && _dropRule.Rule != _dragLine.Rule && !_dropOk)
            {
                var names = new List<string>();
                foreach (int s in LineLinks(_dragLine.Line, _dragLine.Rule))
                    names.Add(s == 0 ? Strings.GetText("ZtxLogicPanel/DropLinkStep") : Strings.FormatText("ZtxLogicPanel/LineNumFmt", PositionOf(_dragBlock, s)));
                text = LogicPalette.Wrap(LogicPalette.Warn, Strings.FormatText("ZtxLogicPanel/DropLinkedFmt", string.Join(", ", names)));
            }
            else text = LogicPalette.Wrap(LogicPalette.Neutral, Strings.GetText(_dragKind == DragKind.Step ? "ZtxLogicPanel/DragStepNote" : _dragKind == DragKind.Output ? "ZtxLogicPanel/DragOutputNote" : _dragKind == DragKind.Line ? "ZtxLogicPanel/DragLineNote"
                : _dragKind == DragKind.Group ? "ZtxLogicPanel/DragGroupNote" : "ZtxLogicPanel/DragRuleNote"));
            if (_dragNote.Text != text) _dragNote.Text = text;
            LogicGui.SetActive(_dragNote, true);
        }

        private void DrawDrop(GraphicsManager gfx, IntRect pixelRect, Rect r, Color scale)
        {
            if (_dragKind == DragKind.None || float.IsNaN(_dropY) || float.IsNaN(_dragY)) return;
            if (_dropY < r.Y || _dropY > r.Y + r.Height) return;
            gfx.SetToMaterial(null);
            Color c = LogicPalette.ToColor(_dropOk ? LogicPalette.LitValue : LogicPalette.Warn);
            gfx.Draw<Quad>(new Quad(new Rect(_dropX, _dropY - DropLineHeight / 2f, _dropW, DropLineHeight), c * scale));
        }

        private void OnDragEnded(bool moved)
        {
            DragKind kind = _dragKind;
            try
            {
                if (kind == DragKind.None || !moved || float.IsNaN(_dropY)) return;
                if (kind == DragKind.Step)
                {
                    if (_dropOk) MoveStepBefore(_dragSeq, _dragStepId, _dropBefore);
                    return;
                }
                if (kind == DragKind.Output)
                {
                    if (_dropOk) MoveOutputBefore(_dragBlock, _dragOutKind, _dragOutId, _dropBefore);
                    return;
                }
                if (kind == DragKind.Line)
                {
                    if (_dropRule == null || _dropRule.IsGroup || !_dropOk)
                    {
                        Log.Info("console: line " + _dragLine.Line + " drop refused (" + (_dropRule == null ? "no rule" : _dropRule.IsGroup ? "a group header" : "linked to other lines") + ").");
                        return;
                    }
                    MoveLineTo(_dragLine.Line, _dropRule.Rule, _dropBefore);
                    return;
                }
                if (!_dropOk) return;
                List<int> order = ExistingRules();
                if (kind == DragKind.Group) MoveItemsBefore(order, LogicGroups.Block(order, _dragBlock.Rule, IsHeaderRule, InGroupRule), _dropBefore);
                else MoveItemsBefore(order, new List<int> { _dragBlock.Rule }, _dropBefore, _dragBlock.Rule, _dropGroup != 0);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnDragEnded", ex); }
            finally
            {
                _dragKind = DragKind.None;
                _dragBlock = null;
                _dragLine = null;
                _dragSeq = null;
                _dropRule = null;
                _dragY = float.NaN;
                _dropY = float.NaN;
                LogicGui.SetActive(_dragNote, false);
                _forceFull = true;
            }
        }

        private static bool SameOrder(List<int> a, List<int> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private void MoveItemsBefore(List<int> order, List<int> items, int before, int flagRule = 0, bool inGroup = false)
        {
            List<int> desired = LogicGroups.MovedBefore(order, items, before);
            Dictionary<int, int> map = LogicRuleOrder.Remap(order, desired);
            var writes = new List<(ID<PartToggleGuiRules> Id, int Value)>();
            AppendRuleRemap(writes, map);
            if (flagRule != 0)
            {
                LogicRuleDef def = _engine.Rule(flagRule);
                if (def.InGroup != inGroup)
                {
                    def.InGroup = inGroup;
                    int dest = map.TryGetValue(flagRule, out int to) ? to : flagRule;
                    ID<PartToggleGuiRules> id = _engine.RuleSlot(dest, ZtxRuleEngineComponent.RULE_M);
                    int at = writes.FindIndex(w => w.Id.Equals(id));
                    if (at >= 0) writes[at] = (id, def.PackMisc());
                    else writes.Add((id, def.PackMisc()));
                }
            }
            if (writes.Count == 0) return;
            Write(writes.ToArray());
            RemapCollapsed(map);
            Log.Info("console: moved " + string.Join(",", items) + " before " + (before == 0 ? "the end" : "rule " + before) + " (" + items.Count + " rules)"
                + (flagRule != 0 ? (inGroup ? ", inside its group" : ", outside any group") : "") + ".");
        }

        private void MoveLineTo(int k, int targetRule, int beforeSlot)
        {
            try
            {
                LogicLine moving = _engine.Line(k);
                if (!moving.IsUsed) return;
                int from = moving.Rule;
                if (from != targetRule && LineLinks(k, from).Count > 0) return;
                List<int> target = RuleLines(targetRule);
                Dictionary<int, int> map = LogicLineOrder.PlanMove(target, k, beforeSlot);
                var writes = new List<(ID<PartToggleGuiRules>, int)>();
                foreach (KeyValuePair<int, int> kv in map)
                {
                    LogicLine before = _engine.Line(kv.Key);
                    LogicLine l = before;
                    l.Rule = targetRule;
                    l.A = RemapLocal(l.A, map);
                    l.B = RemapLocal(l.B, map);
                    if (kv.Value == kv.Key && l.PackA() == before.PackA() && l.PackB() == before.PackB() && l.PackMisc() == before.PackMisc() && kv.Key != k) continue;
                    writes.Add((_engine.LineSlot(kv.Value, ZtxRuleEngineComponent.LINE_A), l.PackA()));
                    writes.Add((_engine.LineSlot(kv.Value, ZtxRuleEngineComponent.LINE_B), l.PackB()));
                    writes.Add((_engine.LineSlot(kv.Value, ZtxRuleEngineComponent.LINE_M), l.PackMisc()));
                    OwnLine(writes, kv.Value, targetRule);
                }
                foreach (int q in _engine.RuleSeqIds(targetRule))
                    foreach (int sk in _engine.SeqStepIds(q))
                    {
                        SeqStep st = _engine.Step(sk);
                        LogicRef t = RemapLocal(st.Target, map);
                        if (st.Kind == SeqKind.WaitUntil && !t.Equals(st.Target)) writes.Add((_engine.StepSlot(sk, ZtxRuleEngineComponent.STEP_T), t.Pack()));
                    }
                if (writes.Count == 0) return;
                Write(writes.ToArray());
                Log.Info("console: line " + k + " of rule " + from + " moved " + (from == targetRule ? "within it" : "to rule " + targetRule)
                    + " before " + (beforeSlot == 0 ? "the end" : "line slot " + beforeSlot) + " (slot " + map[k] + ").");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.MoveLineTo", ex); }
        }
    }
}
