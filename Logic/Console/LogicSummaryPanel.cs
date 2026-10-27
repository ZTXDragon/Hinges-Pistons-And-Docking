using System;
using System.Collections.Generic;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Graphics.Text;
using Halfling.Gui;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class LogicSummaryPanel
    {
        private static float RowHeight => 24f * LogicUiScale.Summary;
        private static float SentenceLineHeight => 20f * LogicUiScale.Summary;
        private static float CharWidth => 7.5f * LogicUiScale.Summary;
        private const int REFRESH_EVERY = 6;

        private sealed class Row
        {
            public int Rule;
            public bool IsHeader;
            public Button Head;
            public Label Sentence;
            public float SentenceWidth = -1f;
        }

        public readonly ScrollBox Root;
        private readonly Label _empty;
        private readonly List<Row> _rows = new List<Row>();
        private readonly HashSet<int> _open = new HashSet<int>();
        private long _setSig = long.MinValue;
        private long _resultSig = long.MinValue;
        private int _lastStamp = int.MinValue;
        private int _frame;
        private bool _force = true;

        public Action<int> JumpTo;

        public LogicSummaryPanel()
        {
            Root = new ScrollBox();
            Root.NineSlice.Flags = NineSliceFlags.None;
            Root.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
            Root.Children.BorderPadding = new Borders(6f, 6f, 6f + Root.VerticalScrollBar.Width, 6f);
            Root.Children.WidgetPadding = new Vector2(2f);
            Root.ScrollBarController.ShowVertical = ScrollBarShowMode.WhenNeeded;
            _empty = new Label { Height = RowHeight };
            _empty.TextProvider = Strings.KeyString("ZtxLogicPanel/NoRules");
            _empty.TextRenderer.Color = Color.Gray;
            _empty.TextRenderer.HAlignment = HAlignment.Left;
            Root.AddChild(_empty);
        }

        public void Reset()
        {
            Clear();
            _open.Clear();
            _setSig = long.MinValue;
            _force = true;
        }

        private void Clear()
        {
            foreach (Row r in _rows)
            {
                Root.Children.Remove(r.Head);
                Root.Children.Remove(r.Sentence);
            }
            _rows.Clear();
        }

        public void Refresh(LogicRuleText text)
        {
            _frame++;
            ZtxRuleEngineComponent engine = text?.Engine;
            if (engine == null)
            {
                if (_rows.Count > 0) Clear();
                LogicGui.SetActive(_empty, true);
                return;
            }
            long setSig = 17, resultSig = 17;
            for (int r = 1; r <= engine.RuleCount; r++)
            {
                LogicRuleDef d = engine.Rule(r);
                setSig = setSig * 31 + (d.Exists ? r : 0) + (d.IsHeader ? 7919 : 0) + (d.IsCondition ? 104729 : 0);
                if (d.Exists) resultSig = resultSig * 31 + (engine.RulePaused(r) ? 2 : engine.RuleResult(r) ? 1 : 0);
            }
            if (setSig != _setSig)
            {
                Rebuild(engine);
                _setSig = setSig;
                _force = true;
            }
            LogicGui.SetActive(_empty, _rows.Count == 0);
            bool all = _force || resultSig != _resultSig || engine.DataStamp != _lastStamp || _frame % REFRESH_EVERY == 0;
            _force = false;
            _resultSig = resultSig;
            _lastStamp = engine.DataStamp;
            if (!all) return;
            float width = Root.ScreenRect.Size.X - 12f - Root.VerticalScrollBar.Width;
            int number = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].IsHeader) RefreshHeaderRow(_rows[i], text);
                else RefreshRow(_rows[i], ++number, text, width);
            }
        }

        private void Rebuild(ZtxRuleEngineComponent engine)
        {
            Clear();
            for (int r = 1; r <= engine.RuleCount; r++)
            {
                if (!engine.Rule(r).Exists || engine.Rule(r).IsCondition) continue;
                var row = new Row { Rule = r, IsHeader = engine.Rule(r).IsHeader, Head = LogicGui.FlatButton(RowHeight) };
                row.Head.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipSummaryRow");
                Row captured = row;
                row.Head.Clicked += delegate { OnClicked(captured); };
                row.Sentence = new Label { Height = SentenceLineHeight };
                row.Sentence.TextRenderer.HAlignment = HAlignment.Left;
                row.Sentence.TextRenderer.OversizeMode = OversizeMode.Wrap;
                row.Sentence.TextRenderer.XmlFormatting = true;
                row.Sentence.TextRenderer.Color = LogicPalette.ToColor(LogicPalette.LineRef);
                row.Sentence.SelfActive = _open.Contains(r) && !row.IsHeader;
                Root.AddChild(row.Head);
                Root.AddChild(row.Sentence);
                _rows.Add(row);
            }
        }

        private void RefreshRow(Row row, int position, LogicRuleText text, float width)
        {
            int r = row.Rule;
            string nameHex = text.HasError(r) ? LogicPalette.Error
                : LogicPalette.RuleColour(text.Engine.RuleColour(r)) ?? LogicPalette.RuleRef;
            bool fn = text.IsFunction(r) && text.Engine.RuleLineSlots(r).Count == 0;
            string expr = fn ? "" : text.Expr(r, colourResult: true);
            string head = LogicPalette.Wrap(LogicPalette.Neutral, "#" + position) + " " + LogicPalette.Wrap(nameHex, text.RuleName(r))
                + (fn ? "  " + text.FunctionState(r, colour: true) : expr.Length > 0 ? "  " + expr : "  " + text.ResultText(r, colour: true));
            LogicGui.SetText(row.Head, head);
            bool open = _open.Contains(r);
            LogicGui.SetActive(row.Sentence, open);
            if (!open) return;
            string sentence = fn ? text.FunctionOutline(r, xml: true) : text.RuleOutline(r, xml: true);
            if (row.Sentence.Text != sentence || Math.Abs(row.SentenceWidth - width) > 1f)
            {
                row.Sentence.Text = sentence;
                row.SentenceWidth = width;
                int lines = 0;
                foreach (string line in sentence.Split('\n'))
                    lines += Math.Max(1, (int)Math.Ceiling(LogicRuleText.StripTags(line).Length * CharWidth / Math.Max(80f, width)));
                LogicGui.SetHeight(row.Sentence, lines * SentenceLineHeight + 4f);
            }
        }

        private void RefreshHeaderRow(Row row, LogicRuleText text)
        {
            ZtxRuleEngineComponent engine = text.Engine;
            string hex = LogicPalette.RuleColour(engine.RuleColour(row.Rule)) ?? LogicPalette.Heading;
            string head = LogicPalette.Wrap(hex, text.RuleName(row.Rule))
                + (engine.Rule(row.Rule).Disabled ? "  " + LogicPalette.Wrap(LogicPalette.Warn, Strings.GetText("ZtxLogicPanel/GroupOffShort")) : "");
            LogicGui.SetText(row.Head, head);
        }

        private void OnClicked(Row row)
        {
            try
            {
                if (row.IsHeader) { JumpTo?.Invoke(row.Rule); return; }
                if (!_open.Add(row.Rule)) _open.Remove(row.Rule);
                _force = true;
                JumpTo?.Invoke(row.Rule);
            }
            catch (Exception ex) { Log.Exception("LogicSummaryPanel.OnClicked", ex); }
        }
    }
}
