using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;
using Halfling.Graphics;
using Halfling.Graphics.Text;
using Halfling.Gui;

namespace ZTX.HingesPistonsDocking
{
    internal sealed partial class LogicRulesTab
    {
        private static float OutputTagWidth => 44f * LogicUiScale.Rules;

        private void BuildOutput(RuleBlock block, Action<string, Action> sink)
        {
            RuleBlock captured = block;
            sink("rule " + block.Rule + " variables", () => BuildVariables(captured));
            OutputLists(block.Rule, out List<int> kinds, out List<int> ids, out List<int> pos);
            foreach (int i in LogicOutputStack.Order(kinds, ids, pos))
            {
                int id = ids[i];
                if (kinds[i] == LogicOutputStack.DO)
                {
                    sink("actuator " + id, () =>
                    {
                        ActRow row = BuildAct(captured, id);
                        if (IsExpanded(id)) BuildSettings(row);
                        AddBody(captured, row.Box);
                        AddBody(captured, row.Settings);
                        if (!IsExpanded(id)) captured.Hidden.Add(row.Settings);
                        captured.Acts.Add(row);
                    });
                }
                else sink("sequence " + id, () => BuildSeqBlock(captured, id));
            }
            sink("rule " + block.Rule + " output end", () => { BuildDoEnd(captured); BuildAddTop(captured); });
        }

        private void OutputLists(int rule, out List<int> kinds, out List<int> ids, out List<int> pos)
        {
            kinds = new List<int>();
            ids = new List<int>();
            pos = new List<int>();
            foreach (int k in _engine.RuleActionSlots(rule))
            {
                if (_engine.ActionInBody(k)) continue;
                kinds.Add(LogicOutputStack.DO);
                ids.Add(k);
                pos.Add(_engine.Action(k).Pos);
            }
            var owners = new List<int> { rule };
            owners.AddRange(ConditionsOf(rule));
            foreach (int owner in owners)
                foreach (int q in _engine.RuleSeqIds(owner))
                {
                    kinds.Add(LogicOutputStack.SEQ);
                    ids.Add(q);
                    pos.Add(_engine.Seq(q).Pos);
                }
        }

        private int NextOutputPos(int rule)
        {
            OutputLists(rule, out _, out _, out List<int> pos);
            return Math.Min(SeqDef.POS_MAX, LogicOutputStack.NextPos(pos));
        }

        private static Label OutputTag(string key, string tipKey)
        {
            var l = new Label { DockMode = DockMode.Left, Width = OutputTagWidth };
            l.TextProvider = Strings.KeyString(key);
            l.TextRenderer.HAlignment = HAlignment.Left;
            l.TextRenderer.OversizeMode = OversizeMode.Shrink;
            l.ToolTips.TextProvider = Strings.KeyString(tipKey);
            SetTextColour(l, LogicPalette.Set);
            return l;
        }
    }
}
