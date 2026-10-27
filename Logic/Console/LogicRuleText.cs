using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class LogicRuleText
    {
        private const int SAY_DEPTH = 2;
        private static readonly Regex s_tags = new Regex("<[^>]+>");

        private readonly ZtxRuleEngineComponent _engine;
        private readonly Part _console;
        private bool _xml;
        private const string Indent = "    ";

        public LogicRuleText(ZtxRuleEngineComponent engine, Part console)
        {
            _engine = engine;
            _console = console;
        }

        public ZtxRuleEngineComponent Engine => _engine;

        public bool HasCode(int rule)
        {
            if (_engine.RuleSeqIds(rule).Count > 0) return true;
            foreach (int k in _engine.RuleActionSlots(rule)) if (!_engine.ActionInBody(k)) return true;
            return false;
        }

        public bool IsFunction(int r)
        {
            LogicRuleDef d = _engine.Rule(r);
            return d.Exists && !d.IsHeader && !d.IsCondition && (HasCode(r) || _engine.RuleLineSlots(r).Count == 0);
        }

        public List<int> ConditionsOf(int f)
        {
            var l = new List<int>();
            for (int r = 1; r <= _engine.RuleCount; r++)
            {
                LogicRuleDef d = _engine.Rule(r);
                if (d.Exists && d.IsCondition && d.FunctionOf == f) l.Add(r);
            }
            return l;
        }

        public List<int> BlocksOf(int f)
        {
            var l = new List<int>(_engine.RuleSeqIds(f));
            foreach (int c in ConditionsOf(f)) l.AddRange(_engine.RuleSeqIds(c));
            l.Sort((a, b) => _engine.Seq(a).Pos != _engine.Seq(b).Pos ? _engine.Seq(a).Pos.CompareTo(_engine.Seq(b).Pos) : a.CompareTo(b));
            return l;
        }

        public bool FunctionRunning(int f)
        {
            foreach (int q in BlocksOf(f)) if (_engine.SeqRunning(q)) return true;
            return false;
        }

        public string FunctionState(int f, bool colour)
        {
            if (_engine.RulePaused(f))
            {
                string paused = Strings.GetText("ZtxLogicPanel/RulePaused");
                return colour ? LogicPalette.Wrap(LogicPalette.Warn, paused) : paused;
            }
            bool run = FunctionRunning(f);
            string t = Strings.GetText(run ? "ZtxLogicPanel/FuncRunning" : "ZtxLogicPanel/FuncIdle");
            return colour ? LogicPalette.Wrap(run ? LogicPalette.On : LogicPalette.Dim, t) : t;
        }

        public string FunctionOutline(int f, bool xml)
        {
            string name = RuleName(f);
            if (xml) name = LogicPalette.Wrap(LogicPalette.RuleColour(_engine.RuleColour(f)) ?? LogicPalette.RuleRef, name);
            List<int> blocks = BlocksOf(f);
            var sb = new StringBuilder(Strings.FormatText("ZtxLogicPanel/OutlineFunctionFmt", name, blocks.Count));
            foreach (int q in blocks)
            {
                SeqDef d = _engine.Seq(q);
                string trigger = Strings.GetText("ZtxLogicPanel/Handler" + d.Trigger);
                string cond = d.Rule != f ? RuleName(d.Rule) + " " + Expr(d.Rule, colourResult: xml) : "";
                if (!xml) cond = StripTags(cond);
                sb.Append('\n').Append(Indent).Append(Strings.FormatText("ZtxLogicPanel/OutlineBlockFmt", trigger, cond, _engine.SeqStatus(q)));
            }
            return sb.ToString();
        }

        public static string StripTags(string xml) => s_tags.Replace(xml ?? "", "");

        public int PositionOf(int rule, int slot)
        {
            IReadOnlyList<int> slots = _engine.RuleLineSlots(rule);
            for (int i = 0; i < slots.Count; i++) if (slots[i] == slot) return i + 1;
            return 0;
        }

        public string RuleName(int r)
        {
            LogicRuleDef d = _engine.Rule(r);
            return string.IsNullOrEmpty(d.Name) ? Strings.FormatText("ZtxLogicPanel/RuleUnnamedFmt", r) : d.Name;
        }

        public string ResultText(int r, bool colour)
        {
            LogicRuleDef def = _engine.Rule(r);
            if (_engine.RulePaused(r))
            {
                string paused = Strings.GetText("ZtxLogicPanel/RulePaused");
                return colour ? LogicPalette.Wrap(LogicPalette.Warn, paused) : paused;
            }
            bool on = _engine.RuleResult(r);
            string t = Strings.GetText(on ? "ZtxLogicPanel/SigOn" : "ZtxLogicPanel/SigOff");
            return colour ? LogicPalette.Wrap(on ? LogicPalette.On : LogicPalette.Off, t) : t;
        }

        public bool HasError(int r)
        {
            LogicRuleDef def = _engine.Rule(r);
            if (IsFunction(r) && _engine.RuleLineSlots(r).Count == 0) return false;
            return def.Joined && _engine.RuleExprError(r, out _) != ExprError.None;
        }

        public string Expr(int r, bool colourResult)
        {
            LogicRuleDef def = _engine.Rule(r);
            IReadOnlyList<int> slots = _engine.RuleLineSlots(r);
            if (slots.Count == 0) return "";
            List<ExprTerm> terms = _engine.RuleTerms(r);
            int last = slots[slots.Count - 1];
            bool same = terms.Count == 1 && terms[0].Line == last;
            if (!def.Joined && !same)
                return LogicPalette.Wrap(LogicPalette.Neutral, Strings.FormatText("ZtxLogicPanel/ExprLegacyFmt", slots.Count));
            ExprError err = _engine.RuleExprError(r, out int unclosed);
            if (err != ExprError.None)
            {
                string why = err == ExprError.NoCondition ? Strings.GetText("ZtxLogicPanel/ExprNoCondition")
                    : err == ExprError.CloseWithoutOpen ? Strings.GetText("ZtxLogicPanel/ExprCloseWithoutOpen")
                    : Strings.FormatText("ZtxLogicPanel/ExprNotClosedFmt", unclosed);
                return LogicPalette.Wrap(LogicPalette.Error, why);
            }
            string body = LogicExpr.Text(terms, k => TermText(r, k), Strings.GetText("ZtxLogicPanel/ConnAnd"), Strings.GetText("ZtxLogicPanel/ConnOr"));
            return "= " + body + "  -> " + ResultText(r, colourResult);
        }

        private string TermText(int rule, int slot)
        {
            string colour = _engine.LineResult(slot) ? LogicPalette.On : LogicPalette.Off;
            return LogicPalette.Wrap(colour, Strings.FormatText("ZtxLogicPanel/LocalFmt", PositionOf(rule, slot)));
        }

        private string InputName(int rule, LogicRef r, bool not, int depth)
        {
            string name;
            switch (r.Kind)
            {
                case RefKind.On:
                    name = Strings.GetText("ZtxLogicPanel/SayAlwaysOn");
                    break;
                case RefKind.Rule:
                    name = RuleName(r.Index);
                    if (_xml) name = LogicPalette.Wrap(LogicPalette.RuleColour(_engine.RuleColour(r.Index)) ?? LogicPalette.RuleRef, name);
                    break;
                case RefKind.Local:
                    name = depth < SAY_DEPTH && PositionOf(rule, r.Index) > 0
                        ? "(" + LinePhrase(rule, r.Index, depth + 1) + ")"
                        : Strings.FormatText("ZtxLogicPanel/LocalFmt", PositionOf(rule, r.Index));
                    break;
                case RefKind.Shared:
                    name = Strings.FormatText("ZtxLogicPanel/SharedFmt", LogicShared.NameOf(r.Index) ?? "?");
                    if (_xml) name = LogicPalette.Wrap(LogicPalette.RuleRef, name);
                    break;
                case RefKind.Part:
                case RefKind.Cart:
                    Part p = LogicNet.Resolve(_console?.Ship, _engine?.Members, r);
                    name = p != null ? LogicNet.PartText(p, _xml) : r.ToString();
                    string watch = LogicPartWatch.Describe(p);
                    if (watch.Length > 0) name += " (" + watch + ")";
                    break;
                default:
                    name = Strings.GetText("ZtxLogicPanel/SayNothing");
                    break;
            }
            return not ? Strings.FormatText("ZtxLogicPanel/SayNot", name) : name;
        }

        private string LinePhrase(int rule, int slot, int depth)
        {
            LogicLine l = _engine.Line(slot);
            if (!l.IsUsed || l.Op == LogicOp.None) return Strings.GetText("ZtxLogicPanel/Say_None");
            bool test = (LogicRuleEval.IsCompare(l.Op) || LogicOpCatalog.IsStateOp(l.Op)) && l.NotA;
            string a = InputName(rule, l.A, l.NotA && !test, depth);
            string bb = l.Op == LogicOp.AtStation ? StationText(l.B)
                : LogicOpCatalog.RoleOfB(l.Op) != BRole.None ? InputName(rule, l.B, l.NotB, depth) : "";
            string value;
            if (LogicRuleEval.IsCompare(l.Op))
                value = l.B.IsNone ? LogicParams.Text(l.Op, l.Value, l.Units) : Strings.FormatText("ZtxLogicPanel/SayValueOfFmt", bb);
            else if (l.Op == LogicOp.Count)
                value = l.Value.ToString(CultureInfo.InvariantCulture);
            else
                value = LogicParams.Text(l.Op, l.Value);
            string phrase = Strings.FormatText("ZtxLogicPanel/Say_" + LogicOpCatalog.LabelKey(TerminalRef(l.A) ? InputClass.Terminal : InputClass.Unknown, l.Op), a, bb, value);
            return test ? Strings.FormatText("ZtxLogicPanel/SayNotTestFmt", phrase) : phrase;
        }

        private bool TerminalRef(LogicRef r)
        {
            if (!r.IsPart || _console?.Ship == null) return false;
            Part p = LogicNet.Resolve(_console.Ship, _engine?.Members, r);
            return p != null && ZtxRailTerminalComponent.ForPart(p) != null;
        }

        private string StationText(LogicRef b)
        {
            if (b.Kind == RefKind.On) return Strings.GetText("ZtxRailPanel/Home");
            Part stop = b.Resolve(_console?.Ship);
            return stop != null ? RailNetwork.DisplayName(stop) : b.IsNone ? Strings.GetText("ZtxLogicPanel/SayNothing") : b.ToString();
        }

        public string LineSentence(int rule, int slot) => Strings.FormatText("ZtxLogicPanel/SayLineFmt", LinePhrase(rule, slot, 0));

        public string RuleOutline(int r, bool xml)
        {
            _xml = xml;
            try
            {
                LogicRuleDef def = _engine.Rule(r);
                string name = RuleName(r);
                if (xml) name = LogicPalette.Wrap(LogicPalette.RuleColour(_engine.RuleColour(r)) ?? LogicPalette.RuleRef, name);
                var sb = new StringBuilder();
                IReadOnlyList<int> slots = _engine.RuleLineSlots(r);
                if (slots.Count == 0)
                    sb.Append(Strings.FormatText("ZtxLogicPanel/SayRuleErrorFmt", name, Strings.GetText("ZtxLogicPanel/ExprNoCondition")));
                else
                {
                    List<ExprTerm> terms = _engine.RuleTerms(r);
                    int last = slots[slots.Count - 1];
                    bool same = terms.Count == 1 && terms[0].Line == last;
                    ExprError err = _engine.RuleExprError(r, out int unclosed);
                    if (!def.Joined && !same)
                    {
                        sb.Append(Strings.FormatText("ZtxLogicPanel/OutlineHeadFmt", name));
                        Line(sb, 1, LinePhrase(r, last, 0));
                    }
                    else if (err != ExprError.None)
                        sb.Append(Strings.FormatText("ZtxLogicPanel/SayRuleErrorFmt", name, StripTags(Expr(r, false))));
                    else
                    {
                        sb.Append(Strings.FormatText("ZtxLogicPanel/OutlineHeadFmt", name));
                        int depth = 1;
                        for (int i = 0; i < terms.Count; i++)
                        {
                            ExprTerm t = terms[i];
                            string conn = i > 0 ? Paint(LogicPalette.Warn, Strings.GetText(t.Or ? "ZtxLogicPanel/ConnOr" : "ZtxLogicPanel/ConnAnd")) + " " : "";
                            for (int o = 0; o < t.Open; o++)
                            {
                                Line(sb, depth, conn + Paint(LogicPalette.LitBracket, "("));
                                conn = "";
                                depth++;
                            }
                            Line(sb, depth, conn + LinePhrase(r, t.Line, 0));
                            for (int c = 0; c < t.Close; c++)
                            {
                                depth = depth > 1 ? depth - 1 : 1;
                                Line(sb, depth, Paint(LogicPalette.LitBracket, ")"));
                            }
                        }
                    }
                }
                var drives = new List<string>();
                if (def.Target.IsPart) drives.Add(InputName(r, def.Target, false, SAY_DEPTH));
                foreach (int k in _engine.RuleActionSlots(r))
                {
                    LogicAction a = _engine.Action(k);
                    if (!a.Target.IsPart) continue;
                    Part p = LogicNet.Resolve(_console?.Ship, _engine?.Members, a.Target);
                    string summary = p != null ? StripTags(LogicRulesTab.SettingsSummary(p, LogicAction.KindOf(p), a)) : "";
                    drives.Add(InputName(r, a.Target, false, SAY_DEPTH) + (summary.Length > 0 ? " (" + summary + ")" : ""));
                }
                if (drives.Count > 0)
                {
                    Line(sb, 0, Strings.GetText("ZtxLogicPanel/OutlineDrives"));
                    foreach (string d in drives) Line(sb, 1, d);
                }
                else Line(sb, 0, Strings.GetText("ZtxLogicPanel/SayDrivesNone").Trim());
                if (_engine.RulePaused(r)) Line(sb, 0, Paint(LogicPalette.Warn, Strings.GetText(def.Disabled ? "ZtxLogicPanel/SayPaused" : "ZtxLogicPanel/SayGroupPaused").Trim()));
                return sb.ToString();
            }
            finally { _xml = false; }
        }

        private static void Line(StringBuilder sb, int depth, string text)
        {
            sb.Append('\n');
            for (int i = 0; i < depth; i++) sb.Append(Indent);
            sb.Append(text);
        }

        private string Paint(string hex, string text) => _xml ? LogicPalette.Wrap(hex, text) : text;
    }
}
