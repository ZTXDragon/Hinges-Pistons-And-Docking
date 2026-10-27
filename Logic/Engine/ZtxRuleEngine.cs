using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Networks;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxRuleEngine")]
    internal class ZtxRuleEngineRules : PartComponentRules
    {
        [Serialize(Optional = true)]
        [PortableHash]
        public int Lines = 16;

        [Serialize(Optional = true)]
        [PortableHash]
        public int Rules = 8;

        [Serialize(Optional = true)]
        [PortableHash]
        public int Actuators = 16;

        public override PartComponent CreateComponent()
        {
            return new ZtxRuleEngineComponent(this);
        }
    }

    internal class ZtxRuleEngineComponent : PartComponent, ILogicTicker
    {
        public const int MAX_LINES = 31;
        public const int MAX_RULES = 15;
        public const int LINE_A = 0, LINE_B = 1, LINE_M = 2;
        public const int RULE_T = 0, RULE_M = 1, RULE_N1 = 2, RULE_N2 = 3;
        public const int RULE_C = LogicStore.RULE_C;
        private const int RULE_RAW = 5;
        public const int MAX_ACTIONS = 32;
        public const int ACT_T = 0, ACT_M = 1, ACT_P = 2;
        public const int ACT_S1 = LogicStore.ACT_S1, ACT_S2 = LogicStore.ACT_S2;
        public const int ACT_V = LogicStore.ACT_V, STEP_V = LogicStore.STEP_V;

        private readonly int _legacyLines, _legacyRules, _legacyActs;
        private readonly ID<PartToggleGuiRules>[,] _legacyLineSlots;
        private readonly ID<PartToggleGuiRules>[,] _legacyRuleSlots;
        private readonly ID<PartToggleGuiRules>[,] _legacyActSlots;
        private readonly ID<PartToggleGuiRules>[] _allSlots;
        private PartUIToggle[] _toggleCache;

        private bool _v2;
        private int _storeVersion;
        private bool _decoded;
        private int _dataStamp;

        private int[,] _lineRaw;
        private int[] _lineOwnerRaw;
        private int[,] _ruleRaw;
        private int[,] _actRaw;
        private int[] _actOwnerRaw;
        private int[] _actPosRaw;
        private int[,] _actStRaw;
        private int[] _actDrvRaw;
        private int[] _actLastOn = { -1 };
        private bool[] _actInBody = { false };
        private int[] _seqPass = { 0 };
        private bool[] _seqActive = { false };
        private int[] _actLastSig = { 0 };
        private bool _doPrimed;
        private LogicAction[] _acts;
        private LogicLine[] _lines;
        private LogicRuleDef[] _rules;
        private LogicRuleState[] _states;
        private bool[] _lineCur, _linePrev, _lineWritten;
        private bool[] _ruleCur, _rulePrev, _ruleWritten;
        private float[] _lineWatch;
        private int[] _lineSaved = new int[1];
        private int[] _ruleHash = new int[1];
        private bool[] _groupOff = new bool[1];
        private bool _statesRestored;
        private bool[] _badFormatLogged;

        private List<ExprTerm>[] _terms;
        private List<int>[] _programs;
        private ExprError[] _exprError;
        private int[] _exprUnclosed;
        private int[] _feeds;
        private List<int>[] _ruleLines;
        private List<int>[] _ruleActs;
        private readonly bool[] _exprStack = new bool[64];
        private bool[] _exprStackBig;
        private bool _exprDirty = true;

        public const int STEP_T = 0, STEP_M = 1, STEP_P = 2, STEP_Q = 3, STEP_S = LogicStore.STEP_S;
        private SeqStep[] _steps = new SeqStep[1];
        private int[,] _stepRaw = new int[1, LogicStore.STEP_FIELDS];
        private SeqDef[] _seqs = new SeqDef[1];
        private int[,] _seqRaw = new int[1, LogicStore.SEQ_FIELDS];
        private List<int>[] _seqStepIds = { new List<int>() };
        private List<SeqStep>[] _seqSteps = { new List<SeqStep>() };
        private SeqRun[] _runs = { new SeqRun() };
        private string[] _runNote = { "" };
        private List<int>[] _ruleSeqs = new List<int>[1];
        public const int WARMUP_TICKS = 3;
        private int _warmup = WARMUP_TICKS;
        private bool _runsRestored;
        private SeqHost _host;

        private readonly WireScan _scan = new WireScan();
        private readonly HashSet<Part> _members = new HashSet<Part>();
        private readonly List<Part> _cartMembers = new List<Part>();
        private readonly List<Part> _terminalScratch = new List<Part>();
        private LogicShipManager _manager;
        private int _structure;

        public new ZtxRuleEngineRules Rules => (ZtxRuleEngineRules)base.Rules;

        public int LineCount => _lines.Length - 1;
        public int RuleCount => _rules.Length - 1;
        public int ActionCount => _acts.Length - 1;
        public int StepCount => _steps.Length - 1;
        public int SeqCount => _seqs.Length - 1;
        public bool IsStoreV2 => _v2;
        public int StoreVersion => _storeVersion;
        public bool HasDecoded => _decoded;
        public int DataStamp => _dataStamp;

        public ZtxRuleEngineComponent(ZtxRuleEngineRules rules) : base(rules)
        {
            _legacyLines = Math.Max(1, Math.Min(MAX_LINES, rules.Lines));
            _legacyRules = Math.Max(1, Math.Min(MAX_RULES, rules.Rules));
            _legacyActs = Math.Max(1, Math.Min(MAX_ACTIONS, rules.Actuators));
            _legacyLineSlots = new ID<PartToggleGuiRules>[_legacyLines + 1, 3];
            _legacyRuleSlots = new ID<PartToggleGuiRules>[_legacyRules + 1, 4];
            _legacyActSlots = new ID<PartToggleGuiRules>[_legacyActs + 1, 3];
            string[] lf = { "a", "b", "m" };
            string[] rf = { "t", "m", "n1", "n2" };
            string[] af = { "t", "m", "p" };
            for (int k = 1; k <= _legacyLines; k++)
                for (int f = 0; f < 3; f++) _legacyLineSlots[k, f] = new ID<PartToggleGuiRules>("ztx_line_" + k + "_" + lf[f]);
            for (int r = 1; r <= _legacyRules; r++)
                for (int f = 0; f < 4; f++) _legacyRuleSlots[r, f] = new ID<PartToggleGuiRules>("ztx_rule_" + r + "_" + rf[f]);
            for (int k = 1; k <= _legacyActs; k++)
                for (int f = 0; f < 3; f++) _legacyActSlots[k, f] = new ID<PartToggleGuiRules>("ztx_act_" + k + "_" + af[f]);
            _allSlots = new ID<PartToggleGuiRules>[_legacyLines * 3 + _legacyRules * 4 + _legacyActs * 3];
            int n = 0;
            for (int k = 1; k <= _legacyLines; k++) for (int f = 0; f < 3; f++) _allSlots[n++] = _legacyLineSlots[k, f];
            for (int r = 1; r <= _legacyRules; r++) for (int f = 0; f < 4; f++) _allSlots[n++] = _legacyRuleSlots[r, f];
            for (int k = 1; k <= _legacyActs; k++) for (int f = 0; f < 3; f++) _allSlots[n++] = _legacyActSlots[k, f];
            _lineRaw = new int[1, 3];
            _lineOwnerRaw = new int[1];
            _ruleRaw = new int[1, RULE_RAW];
            _actRaw = new int[1, 3];
            _actOwnerRaw = new int[1];
            _actPosRaw = new int[1];
            _actStRaw = new int[1, 2];
            _actDrvRaw = new int[1];
            _acts = new LogicAction[1];
            _lines = new LogicLine[1];
            _rules = new LogicRuleDef[1];
            _states = new LogicRuleState[1];
            _lineCur = new bool[1]; _linePrev = new bool[1]; _lineWritten = new bool[1];
            _ruleCur = new bool[1]; _rulePrev = new bool[1]; _ruleWritten = new bool[1];
            _lineWatch = new float[1];
            _badFormatLogged = new bool[1];
            _terms = new List<ExprTerm>[1];
            _programs = new List<int>[1];
            _exprError = new ExprError[1];
            _exprUnclosed = new int[1];
            _feeds = new int[1];
            _ruleLines = new List<int>[1];
            _ruleActs = new List<int>[1];
            _ruleSeqs = new List<int>[1];
            SetCapacity(_legacyLines, _legacyRules, _legacyActs);
        }

        private void SetCapacity(int lines, int rules, int acts)
        {
            lines = Math.Max(1, lines);
            rules = Math.Max(1, rules);
            acts = Math.Max(1, acts);
            if (lines != LineCount)
            {
                _lineRaw = Resize2(_lineRaw, lines + 1, 3);
                Array.Resize(ref _lineOwnerRaw, lines + 1);
                int old = _lines.Length;
                Array.Resize(ref _lines, lines + 1);
                Array.Resize(ref _states, lines + 1);
                Array.Resize(ref _lineCur, lines + 1);
                Array.Resize(ref _linePrev, lines + 1);
                Array.Resize(ref _lineWritten, lines + 1);
                Array.Resize(ref _lineWatch, lines + 1);
                Array.Resize(ref _lineSaved, lines + 1);
                Array.Resize(ref _badFormatLogged, lines + 1);
                Array.Resize(ref _feeds, lines + 1);
                for (int k = 0; k <= lines; k++)
                {
                    if (_states[k] == null) _states[k] = new LogicRuleState();
                    if (k >= old) _lineWatch[k] = float.NaN;
                }
                _structure++;
                _exprDirty = true;
            }
            if (rules != RuleCount)
            {
                _ruleRaw = Resize2(_ruleRaw, rules + 1, RULE_RAW);
                Array.Resize(ref _rules, rules + 1);
                Array.Resize(ref _ruleCur, rules + 1);
                Array.Resize(ref _rulePrev, rules + 1);
                Array.Resize(ref _mem, rules + 1);
                Array.Resize(ref _memRead, rules + 1);
                Array.Resize(ref _memSig, rules + 1);
                Array.Resize(ref _ruleWritten, rules + 1);
                Array.Resize(ref _terms, rules + 1);
                Array.Resize(ref _programs, rules + 1);
                Array.Resize(ref _exprError, rules + 1);
                Array.Resize(ref _exprUnclosed, rules + 1);
                Array.Resize(ref _ruleLines, rules + 1);
                Array.Resize(ref _ruleActs, rules + 1);
                Array.Resize(ref _ruleSeqs, rules + 1);
                Array.Resize(ref _ruleHash, rules + 1);
                Array.Resize(ref _groupOff, rules + 1);
                for (int r = 0; r <= rules; r++)
                {
                    if (_ruleSeqs[r] == null) _ruleSeqs[r] = new List<int>();
                    if (_terms[r] == null) _terms[r] = new List<ExprTerm>();
                    if (_programs[r] == null) _programs[r] = new List<int>();
                    if (_ruleLines[r] == null) _ruleLines[r] = new List<int>();
                    if (_ruleActs[r] == null) _ruleActs[r] = new List<int>();
                    if (_rules[r].Name == null) _rules[r].Name = "";
                }
                _structure++;
                _exprDirty = true;
            }
            if (acts != ActionCount)
            {
                _actRaw = Resize2(_actRaw, acts + 1, 3);
                Array.Resize(ref _actOwnerRaw, acts + 1);
                Array.Resize(ref _actPosRaw, acts + 1);
                _actStRaw = Resize2(_actStRaw, acts + 1, 2);
                Array.Resize(ref _actDrvRaw, acts + 1);
                int oldActs = _actLastOn.Length;
                Array.Resize(ref _actLastOn, acts + 1);
                Array.Resize(ref _actLastSig, acts + 1);
                for (int k = oldActs; k <= acts; k++) _actLastOn[k] = -1;
                Array.Resize(ref _acts, acts + 1);
                Array.Resize(ref _actInBody, acts + 1);
                _structure++;
                _exprDirty = true;
            }
        }

        private void SetStepCapacity(int steps)
        {
            steps = Math.Max(0, steps);
            if (steps == StepCount) return;
            _stepRaw = Resize2(_stepRaw, steps + 1, LogicStore.STEP_FIELDS);
            Array.Resize(ref _steps, steps + 1);
            _structure++;
            _exprDirty = true;
        }

        private void SetSeqCapacity(int seqs)
        {
            seqs = Math.Max(0, seqs);
            if (seqs == SeqCount) return;
            _seqRaw = Resize2(_seqRaw, seqs + 1, LogicStore.SEQ_FIELDS);
            Array.Resize(ref _seqs, seqs + 1);
            Array.Resize(ref _seqStepIds, seqs + 1);
            Array.Resize(ref _seqSteps, seqs + 1);
            Array.Resize(ref _runs, seqs + 1);
            Array.Resize(ref _runNote, seqs + 1);
            Array.Resize(ref _seqPass, seqs + 1);
            Array.Resize(ref _seqActive, seqs + 1);
            for (int q = 0; q <= seqs; q++)
            {
                if (_seqStepIds[q] == null) _seqStepIds[q] = new List<int>();
                if (_seqSteps[q] == null) _seqSteps[q] = new List<SeqStep>();
                if (_runs[q] == null) _runs[q] = new SeqRun();
                if (_runNote[q] == null) _runNote[q] = "";
            }
            _structure++;
            _exprDirty = true;
        }

        private static int[,] Resize2(int[,] a, int rows, int cols)
        {
            var b = new int[rows, cols];
            int keep = Math.Min(rows, a.GetLength(0));
            for (int i = 0; i < keep; i++) for (int j = 0; j < cols; j++) b[i, j] = a[i, j];
            return b;
        }

        public ID<PartToggleGuiRules> LineSlot(int line, int field) => _v2 ? new ID<PartToggleGuiRules>(LogicStore.LineName(line, field)) : _legacyLineSlots[line, field];
        public ID<PartToggleGuiRules> RuleSlot(int rule, int field) => _v2 ? new ID<PartToggleGuiRules>(LogicStore.RuleName(rule, field)) : _legacyRuleSlots[rule, field];
        public ID<PartToggleGuiRules> ActionSlot(int k, int field) => _v2 ? new ID<PartToggleGuiRules>(LogicStore.ActName(k, field)) : _legacyActSlots[k, field];
        public ID<PartToggleGuiRules> LineOwnerSlot(int line) => new ID<PartToggleGuiRules>(LogicStore.LineName(line, LogicStore.LINE_R));
        public ID<PartToggleGuiRules> ActionOwnerSlot(int k) => new ID<PartToggleGuiRules>(LogicStore.ActName(k, LogicStore.ACT_R));
        public ID<PartToggleGuiRules> ActionPosSlot(int k) => new ID<PartToggleGuiRules>(LogicStore.ActName(k, LogicStore.ACT_O));

        public int RawLine(int k, int f) => k >= 1 && k <= LineCount ? _lineRaw[k, f] : 0;
        public int RawRule(int r, int f) => r >= 1 && r <= RuleCount ? _ruleRaw[r, f] : 0;
        public int RuleFieldCount => _v2 ? RULE_RAW : 4;
        public int RuleColour(int r) => _v2 && r >= 1 && r <= RuleCount ? _ruleRaw[r, RULE_C] : 0;
        public int RawAction(int k, int f)
        {
            if (k < 1 || k > ActionCount) return 0;
            if (f == ACT_S1) return _actStRaw[k, 0];
            if (f == ACT_S2) return _actStRaw[k, 1];
            if (f == ACT_V) return _actDrvRaw[k];
            return f >= 0 && f < 3 ? _actRaw[k, f] : 0;
        }

        public LogicLine Line(int k) => k >= 1 && k <= LineCount ? _lines[k] : default;
        public LogicRuleDef Rule(int r) => r >= 1 && r <= RuleCount ? _rules[r] : new LogicRuleDef { Name = "" };
        public LogicAction Action(int k) => k >= 1 && k <= ActionCount ? _acts[k] : default;

        public bool LineResult(int k) => k >= 1 && k <= LineCount && _linePrev[k];
        public bool RuleResult(int r) => r >= 1 && r <= RuleCount && _rulePrev[r];
        public bool ActionInBody(int k) => k >= 1 && k < _actInBody.Length && _actInBody[k];
        public bool RulePaused(int r) => r >= 1 && r <= RuleCount && (_rules[r].Disabled || _groupOff[r]);
        public bool GroupOff(int r) => r >= 1 && r <= RuleCount && _groupOff[r];
        public int RuleLineCount(int r) => r >= 1 && r <= RuleCount && _ruleLines != null && _ruleLines[r] != null ? _ruleLines[r].Count : 0;

        public IReadOnlyList<int> RuleLineSlots(int r) => r >= 1 && r <= RuleCount && _ruleLines != null && _ruleLines[r] != null ? _ruleLines[r] : (IReadOnlyList<int>)s_noSlots;

        public IReadOnlyList<int> RuleActionSlots(int r) => r >= 1 && r <= RuleCount && _ruleActs != null && _ruleActs[r] != null ? _ruleActs[r] : (IReadOnlyList<int>)s_noSlots;

        private static readonly int[] s_noSlots = new int[0];
        public float LineWatch(int k) => k >= 1 && k <= LineCount ? _lineWatch[k] : float.NaN;
        public HashSet<Part> Members => _members;
        public int Structure => _structure;

        public List<ExprTerm> RuleTerms(int r) => r >= 1 && r <= RuleCount ? _terms[r] : _terms[0];
        public ExprError RuleExprError(int r, out int unclosed)
        {
            unclosed = r >= 1 && r <= RuleCount ? _exprUnclosed[r] : 0;
            return r >= 1 && r <= RuleCount ? _exprError[r] : ExprError.NoCondition;
        }
        public int FeedsLine(int k) => k >= 1 && k <= LineCount ? _feeds[k] : 0;

        public ID<PartToggleGuiRules> StepSlot(int k, int field) => new ID<PartToggleGuiRules>(LogicStore.StepName(k, field));
        public ID<PartToggleGuiRules> StepOwnerSlot(int k) => new ID<PartToggleGuiRules>(LogicStore.StepName(k, LogicStore.STEP_R));
        public SeqStep Step(int k) => k >= 1 && k <= StepCount ? _steps[k] : default;
        public int RawStep(int k, int f) => k >= 1 && k <= StepCount ? _stepRaw[k, f] : 0;
        public ID<PartToggleGuiRules> SeqSlot(int q, int field) => new ID<PartToggleGuiRules>(LogicStore.SeqName(q, field));
        public SeqDef Seq(int q) => q >= 1 && q <= SeqCount ? _seqs[q] : default;
        public int RawSeq(int q, int f) => q >= 1 && q <= SeqCount ? _seqRaw[q, f] : 0;
        public IReadOnlyList<int> RuleSeqIds(int r) => r >= 1 && r <= RuleCount && _ruleSeqs[r] != null ? _ruleSeqs[r] : (IReadOnlyList<int>)s_noSlots;
        public IReadOnlyList<int> SeqStepIds(int q) => q >= 1 && q <= SeqCount ? _seqStepIds[q] : (IReadOnlyList<int>)s_noSlots;
        public bool SeqRunning(int q) => q >= 1 && q <= SeqCount && _runs[q].Running;
        public int SeqRunIndex(int q) => q >= 1 && q <= SeqCount ? _runs[q].Index : 0;
        public string SeqStatus(int q)
        {
            if (q < 1 || q > SeqCount) return "";
            SeqRun run = _runs[q];
            if (!run.Running) return _runNote[q] ?? "";
            return "step " + (run.Index + 1) + " of " + _seqSteps[q].Count + (run.Queued ? " (one queued)" : "") + (run.Why.Length > 0 ? " -- " + run.Why : "");
        }

        public int SeqPlace(int q)
        {
            SeqDef d = Seq(q);
            if (!d.IsUsed || d.Rule > RuleCount) return 0;
            int place = 1;
            foreach (int k in _ruleActs[d.Rule]) if (_acts[k].Pos <= d.Pos) place++;
            foreach (int o in _ruleSeqs[d.Rule]) if (o != q && (_seqs[o].Pos < d.Pos || (_seqs[o].Pos == d.Pos && o < q))) place++;
            return place;
        }

        public int FreeStep()
        {
            for (int k = 1; k <= StepCount; k++) if (!_steps[k].IsUsed) return k;
            return StepCount + 1;
        }

        public List<int> FreeSteps(int n)
        {
            var l = new List<int>();
            for (int k = 1; k <= StepCount && l.Count < n; k++) if (!_steps[k].IsUsed) l.Add(k);
            for (int k = StepCount + 1; l.Count < n; k++) l.Add(k);
            return l;
        }

        public List<int> FreeRules(int n)
        {
            var l = new List<int>();
            for (int r = 1; r <= RuleCount && l.Count < n; r++) if (!_rules[r].Exists) l.Add(r);
            for (int r = RuleCount + 1; _v2 && l.Count < n; r++) l.Add(r);
            return l;
        }

        public List<int> FreeSeqs(int n)
        {
            var l = new List<int>();
            for (int q = 1; q <= SeqCount && l.Count < n; q++) if (!_seqs[q].IsUsed) l.Add(q);
            for (int q = SeqCount + 1; l.Count < n; q++) l.Add(q);
            return l;
        }

        public int FreeLine()
        {
            for (int k = 1; k <= LineCount; k++) if (!_lines[k].IsUsed) return k;
            return _v2 ? LineCount + 1 : 0;
        }

        public int FreeRule()
        {
            for (int r = 1; r <= RuleCount; r++) if (!_rules[r].Exists) return r;
            return _v2 ? RuleCount + 1 : 0;
        }

        public int FreeAction()
        {
            for (int k = 1; k <= ActionCount; k++) if (!_acts[k].IsUsed) return k;
            return _v2 ? ActionCount + 1 : 0;
        }

        public List<int> FreeLines(int n)
        {
            var l = new List<int>();
            for (int k = 1; k <= LineCount && l.Count < n; k++) if (!_lines[k].IsUsed) l.Add(k);
            for (int k = LineCount + 1; _v2 && l.Count < n; k++) l.Add(k);
            return l;
        }

        public List<int> FreeActions(int n)
        {
            var l = new List<int>();
            for (int k = 1; k <= ActionCount && l.Count < n; k++) if (!_acts[k].IsUsed) l.Add(k);
            for (int k = ActionCount + 1; _v2 && l.Count < n; k++) l.Add(k);
            return l;
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            try
            {
                _manager = LogicShipManager.GetOrCreate(base.Ship);
                _manager?.Register(this);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxRuleEngineComponent.OnPartAttached", ex);
            }
        }

        public override void OnPartDetaching()
        {
            try
            {
                LogicShipManager.TryGet(base.Ship)?.Deregister(this);
                _manager = null;
                _members.Clear();
                _scan.Reset();
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxRuleEngineComponent.OnPartDetaching", ex);
            }
            base.OnPartDetaching();
        }

        public void Tick()
        {
            Part me = base.Part;
            Ship ship = base.Ship;
            if (me == null || ship == null) return;
            float dt = LogicShipManager.TickSeconds;
            int tick = _manager != null ? _manager.TickCount : 0;

            bool timed = LogicCost.Active;
            long s0 = timed ? LogicCost.Now : 0;
            long push = 0;

            Decode(me, ship, tick);
            long s1 = timed ? LogicCost.Now : 0;

            bool rescanned = _scan.Update(me, ship, _members, tick, out long links);
            SplitOldLines(me, ship, tick);
            SyncCarts();
            long s2 = timed ? LogicCost.Now : 0;

            if (!_statesRestored) RestoreLineStates(me, ship);

            Array.Clear(_lineCur, 0, _lineCur.Length);
            Array.Clear(_lineWritten, 0, _lineWritten.Length);
            Array.Clear(_ruleCur, 0, _ruleCur.Length);
            Array.Clear(_ruleWritten, 0, _ruleWritten.Length);

            for (int r = 1; r <= RuleCount; r++)
            {
                ref LogicRuleDef rule = ref _rules[r];
                if (!rule.Exists || rule.IsHeader) continue;
                List<int> ruleLines = _ruleLines[r];
                if (rule.Disabled || _groupOff[r])
                {
                    foreach (int k in ruleLines)
                    {
                        _lineCur[k] = _linePrev[k];
                        _lineWritten[k] = true;
                    }
                    _ruleCur[r] = _rulePrev[r];
                    _ruleWritten[r] = true;
                    continue;
                }
                bool any = false;
                bool result = false;
                foreach (int k in ruleLines)
                {
                    ref LogicLine line = ref _lines[k];
                    bool value = false;
                    float watch = float.NaN;
                    if (LogicRuleEval.IsCompare(line.Op))
                    {
                        float a = ReadNumber(ship, in line.A, line.Units);
                        float t = line.B.IsNone ? line.Value : ReadNumber(ship, in line.B, line.Units);
                        value = LogicRuleEval.Compare(line.Op, a, t, line.Units) != line.NotA;
                        watch = a;
                    }
                    else if (LogicOpCatalog.IsStateOp(line.Op))
                    {
                        Part p = line.A.IsPart ? LogicNet.Resolve(ship, _members, line.A) : null;
                        bool cart = LogicAction.KindOf(p) == LogicAction.KIND_CART;
                        if (line.Op == LogicOp.Merged)
                            value = p != null && _members.Contains(p) && (cart ? RailShipManager.IsCartParked(p)
                                : ZtxRailTerminalComponent.ForPart(p) != null ? RailShipManager.IsTerminalParked(p) : MergeService.IsMerged(p));
                        else if (line.Op == LogicOp.AtStation)
                            value = cart && _members.Contains(p) && RailShipManager.IsAtStation(p, line.B);
                        else if (line.Op == LogicOp.Docked)
                            value = p != null && _members.Contains(p) && DockToggles.IsDocked(p);
                        else if (line.Op == LogicOp.Crewed)
                            value = p != null && _members.Contains(p) && DockToggles.HeldCrewed(p);
                        else if (line.Op == LogicOp.Supplied)
                            value = p != null && _members.Contains(p) && DockToggles.HeldSupplied(p);
                        else if (line.Op == LogicOp.Repaired)
                            value = p != null && _members.Contains(p) && DockToggles.HeldRepaired(p);
                        else if (line.Op == LogicOp.Charged)
                            value = p != null && _members.Contains(p) && DockToggles.HeldCharged(p);
                        else if (line.Op == LogicOp.Built)
                            value = p != null && _members.Contains(p) && DockToggles.HeldBuilt(p);
                        else if (line.Op == LogicOp.Holding)
                            value = cart && _members.Contains(p) && GripService.IsCartHolding(p);
                        else
                        {
                            ActPhase phase = p != null && _members.Contains(p) ? LogicActuatorState.Phase(p) : ActPhase.Unknown;
                            value = line.Op == LogicOp.AtOn ? phase == ActPhase.AtOn
                                : line.Op == LogicOp.AtOff ? phase == ActPhase.AtOff
                                : phase == ActPhase.Moving;
                        }
                        if (line.NotA) value = !value;
                    }
                    else if (_warmup > 0 && LogicRuleState.IsSaved(line.Op))
                    {
                        value = LogicRuleEval.Hold(line.Op, line.Op == LogicOp.Count ? line.Value : line.ParamSeconds, _states[k]);
                        if (line.Op == LogicOp.Count) watch = _states[k].Count;
                    }
                    else if (line.Op == LogicOp.Count)
                    {
                        bool a = ReadInput(ship, in line.A) != line.NotA;
                        bool b = ReadInput(ship, in line.B) != line.NotB;
                        value = LogicRuleEval.Step(line.Op, line.Value, _states[k], a, b, dt);
                        watch = _states[k].Count;
                        SaveLineState(me, ship, k);
                    }
                    else if (line.Op != LogicOp.None)
                    {
                        bool a = ReadInput(ship, in line.A) != line.NotA;
                        bool b = line.UsesB && (ReadInput(ship, in line.B) != line.NotB);
                        value = LogicRuleEval.Step(line.Op, line.ParamSeconds, _states[k], a, b, dt);
                        if (LogicRuleState.IsSaved(line.Op)) SaveLineState(me, ship, k);
                    }
                    _lineWatch[k] = watch;
                    _lineCur[k] = value;
                    _lineWritten[k] = true;
                    result = value;
                    any = true;
                }
                if (!any) continue;
                if (rule.Joined) result = _exprError[r] == ExprError.None && LogicExpr.Run(_programs[r], _lineCur, ExprStack(_programs[r].Count));
                _ruleCur[r] = result;
                _ruleWritten[r] = true;
                long p0 = timed ? LogicCost.Now : 0;
                if (rule.Target.IsPart)
                    PushTo(ship, tick, rule.Target, result, new ActSettings { OnPose = rule.PoseSet ? rule.Pose : float.NaN, OffPose = float.NaN, Speed = rule.Speed });
                foreach (int k in _ruleActs[r])
                {
                    if (_actInBody[k]) continue;
                    if (!_acts[k].Target.IsPart) continue;
                    LogicAction act = _acts[k];
                    bool on = act.Sided ? result : result != act.Invert;
                    if (!act.Sided || result == !act.WhenOff) PushTo(ship, tick, act.Target, on, act.Settings);
                    DriveDo(ship, k, r, on);
                }
                if (timed) push += LogicCost.Now - p0;
            }
            _doPrimed = true;
            for (int r = 1; r <= RuleCount; r++)
                if (_ruleHash[r] != 0 && _rules[r].Exists && !_rules[r].IsHeader)
                    LogicShared.Publish(_manager, _ruleHash[r], _rules[r].Name, _ruleWritten[r] && _ruleCur[r]);
            TickSequences(me, ship);
            if (_warmup > 0) _warmup--;
            Array.Copy(_lineCur, _linePrev, _lineCur.Length);
            Array.Copy(_ruleCur, _rulePrev, _ruleCur.Length);
            if (timed)
            {
                long s3 = LogicCost.Now;
                LogicCost.AddPhase(LogicCost.Phase.Decode, s1 - s0);
                LogicCost.AddPhase(LogicCost.Phase.Scan, s2 - s1 - links);
                LogicCost.AddPhase(LogicCost.Phase.Links, links);
                LogicCost.AddPhase(LogicCost.Phase.Lines, s3 - s2 - push);
                LogicCost.AddPhase(LogicCost.Phase.Push, push);
                LogicCost.NoteConsoleTick(me, s1 - s0, s2 - s1 - links, links, s3 - s2 - push, push, rescanned);
            }
        }

        private void SyncCarts()
        {
            foreach (Part c in _cartMembers) _members.Remove(c);
            _cartMembers.Clear();
            _terminalScratch.Clear();
            foreach (Part p in _members) if (ZtxRailTerminalComponent.ForPart(p) != null) _terminalScratch.Add(p);
            foreach (Part tp in _terminalScratch) RailShipManager.AddCartsOf(tp, _cartMembers);
            foreach (Part c in _cartMembers) _members.Add(c);
        }

        private static readonly string SEQ_PREFIX = "ztx_sr_";
        private static readonly string LINE_STATE_PREFIX = "ztx_ls_";

        private static ID<PartToggleGuiRules> LineStateId(int k) => new ID<PartToggleGuiRules>(LINE_STATE_PREFIX + k);

        private void RestoreLineStates(Part me, Ship ship)
        {
            _statesRestored = true;
            var psd = new PartSaveData(me);
            int restored = 0;
            for (int k = 1; k <= LineCount; k++)
            {
                int? v = ship.Metadata.PartUIToggleStates.Get((psd, LineStateId(k)));
                _lineSaved[k] = v ?? 0;
                if (!v.HasValue || v.Value == 0 || !_lines[k].IsUsed || !LogicRuleState.IsSaved(_lines[k].Op)) continue;
                _states[k] = LogicRuleState.Unpack(v.Value);
                restored++;
            }
            if (restored > 0) Log.Info("console at " + me.Location + ": " + restored + " saved line state(s) restored.");
        }

        private void SaveLineState(Part me, Ship ship, int k)
        {
            int v = _states[k].Pack();
            if (v == _lineSaved[k]) return;
            _lineSaved[k] = v;
            ship.Metadata.PartUIToggleStates.Set((new PartSaveData(me), LineStateId(k)), v == 0 ? (int?)null : v);
        }

        private void ClearLineState(Part me, int k)
        {
            if (!_statesRestored || me?.Ship == null || k >= _lineSaved.Length || _lineSaved[k] == 0) return;
            _lineSaved[k] = 0;
            me.Ship.Metadata.PartUIToggleStates.Set((new PartSaveData(me), LineStateId(k)), null);
        }


        private void TickSequences(Part me, Ship ship)
        {
            if (SeqCount == 0 && !_runsRestored) { _runsRestored = true; return; }
            if (_host == null) _host = MakeHost();
            var psd = new PartSaveData(me);
            if (!_runsRestored)
            {
                for (int q = 1; q <= SeqCount; q++)
                {
                    int? v = ship.Metadata.PartUIToggleStates.Get((psd, new ID<PartToggleGuiRules>(SEQ_PREFIX + "q" + q)));
                    _runs[q] = v.HasValue && v.Value != 0 ? SeqRun.Unpack(v.Value) : new SeqRun();
                }
                _runsRestored = true;
            }
            if (_warmup > 0) return;
            for (int q = 1; q <= SeqCount; q++)
            {
                SeqDef d = _seqs[q];
                SeqRun run = _runs[q];
                if (!d.IsUsed || d.Rule > RuleCount)
                {
                    if (run.Running || run.Queued) ClearRun(ship, psd, q);
                    continue;
                }
                ref LogicRuleDef rule = ref _rules[d.Rule];
                bool repeats = HandlerRun.Repeats(d.Trigger);
                if (!rule.Exists || rule.IsHeader || RulePaused(d.Rule))
                {
                    if (repeats && run.Running) { SeqRunner.Stop(run); SaveRun(ship, psd, q); }
                    _seqActive[q] = false;
                    continue;
                }
                if (_seqSteps[q].Count == 0 && !run.Running) continue;
                int r = d.Rule;
                bool cur = _ruleWritten[r] ? _ruleCur[r] : _rulePrev[r], prev = _rulePrev[r];
                if (d.Trigger == HandlerTrigger.OnCall) { cur = _calls.Remove(q); prev = false; }
                HandlerRun.Decide(d.Trigger, true, cur, prev, run.Running, out bool trigger, out bool stop);
                int before = run.Pack();
                if (stop) SeqRunner.Stop(run);
                if (repeats && !HandlerRun.IsActive(d.Trigger, cur)) _seqActive[q] = false;
                if (trigger)
                {
                    _seqPass[q] = repeats && _seqActive[q] ? _seqPass[q] + 1 : 0;
                    _seqActive[q] = repeats;
                }
                SeqRunner.Tick(run, _seqSteps[q], trigger, d.QueueOne && !repeats, d.StopOnRefusal, q, _host);
                int after = run.Pack();
                if (after != before) ship.Metadata.PartUIToggleStates.Set((psd, new ID<PartToggleGuiRules>(SEQ_PREFIX + "q" + q)), after == 0 ? (int?)null : after);
            }
        }

        private void SaveRun(Ship ship, PartSaveData psd, int q)
        {
            int v = _runs[q].Pack();
            ship.Metadata.PartUIToggleStates.Set((psd, new ID<PartToggleGuiRules>(SEQ_PREFIX + "q" + q)), v == 0 ? (int?)null : v);
        }

        private void ClearRun(Ship ship, PartSaveData psd, int q)
        {
            _runs[q] = new SeqRun();
            ship.Metadata.PartUIToggleStates.Set((psd, new ID<PartToggleGuiRules>(SEQ_PREFIX + "q" + q)), null);
        }

        private SeqHost MakeHost()
        {
            return new SeqHost
            {
                Now = () => ZtxSignalComponent.Clock(base.Ship),
                TicksPerSecond = Math.Max(1, (int)Math.Round(1f / Math.Max(0.001f, LogicShipManager.TickSeconds))),
                Begin = (q, index) =>
                {
                    if (q >= 1 && q <= SeqCount && index >= 0 && index < _seqSteps[q].Count)
                    {
                        SeqStep cs = _seqSteps[q][index];
                        if (cs.Kind == SeqKind.Call) { StartCall(q, index, cs); return; }
                        if (cs.Kind == SeqKind.MemSet || cs.Kind == SeqKind.MemAdd) { RunMemory(q, cs); return; }
                    }
                    if (q >= 1 && q <= SeqCount && index >= 0 && index < _seqSteps[q].Count && _seqSteps[q][index].Kind == SeqKind.Do)
                    {
                        RunDo(q, _seqSteps[q][index]);
                        return;
                    }
                    if (!TryStepTarget(q, index, out SeqStep s, out Part p)) { _host.LastWhy = "actuator not on the wires"; return; }
                    string changed = PistonToggles.ApplyStep(p, s);
                    int kind = LogicAction.KindOf(p);
                    LogThrottle.Info("step|" + base.Part?.Location + "|" + q + "|" + index, "console at " + base.Part?.Location + ": rule " + Seq(q).Rule + " sequence " + SeqPlace(q) + " step " + (index + 1)
                        + " -> " + KindName(kind) + " at " + p.Location + ": " + (changed.Length > 0 ? changed : "no toggle changes") + ".");
                },
                Poll = (q, index, start) =>
                {
                    if (q >= 1 && q <= SeqCount && index >= 0 && index < _seqSteps[q].Count && _seqSteps[q][index].Kind == SeqKind.Do)
                        return PollDo(_seqSteps[q][index], start);
                    if (q >= 1 && q <= SeqCount && index >= 0 && index < _seqSteps[q].Count && _seqSteps[q][index].Kind == SeqKind.Call)
                        return PollCall(q, index);
                    if (!TryStepTarget(q, index, out SeqStep s, out Part p)) { _host.LastWhy = "actuator not on the wires"; return SeqRunner.RUNNING; }
                    int status = PistonToggles.Settle(p, s, start, out string why);
                    _host.LastWhy = why;
                    return status;
                },
                LineTrue = (q, packed) =>
                {
                    LogicRef target = LogicRef.Unpack(packed);
                    if (!SeqTargets.WaitAllowed(target, Seq(q).Rule, LineOwner, IsVariable))
                    {
                        _host.LastWhy = target.IsNone ? "nothing to wait for" : target.IsLocal ? "line of another rule" : "its own rule";
                        return false;
                    }
                    return ReadInput(base.Ship, target);
                },
                Note = (q, text) =>
                {
                    if (q < 1 || q > SeqCount || _runNote[q] == text) return;
                    _runNote[q] = text;
                    Log.Info("console at " + base.Part?.Location + ": rule " + Seq(q).Rule + " sequence " + SeqPlace(q) + " -- " + text + ".");
                },
            };
        }

        private void DriveDo(Ship ship, int k, int rule, bool on)
        {
            int sig = unchecked((((((_actRaw[k, 0] * 31) ^ _actRaw[k, 1]) * 31 ^ _actRaw[k, 2]) * 31 ^ _actStRaw[k, 0]) * 31 ^ _actStRaw[k, 1]) * 31 ^ _actDrvRaw[k]);
            bool edited = _actLastSig[k] != sig;
            bool edge = PistonToggles.ShouldWrite(_doPrimed, _actLastOn[k], on, edited);
            bool write = PistonToggles.ShouldWriteHeld(_doPrimed, _actLastOn[k], on, edited, _acts[k].Hold && _acts[k].Sided);
            _actLastOn[k] = on ? 1 : 0;
            _actLastSig[k] = sig;
            if (!write) return;
            bool ruleOn = on;
            if (!LogicAction.SideGate(_acts[k].Sided, _acts[k].WhenOff, on, out bool planOn)) return;
            string changed = ApplyAction(ship, k, planOn, edge, out int kind, out Part p);
            if (changed.Length > 0)
                Log.Info("console at " + base.Part?.Location + ": rule " + rule + " DO " + (ruleOn ? "ON" : "OFF") + " -> " + KindName(kind)
                    + " at " + p.Location + ": " + changed + ".");
        }

        private void RunDo(int q, SeqStep s)
        {
            int k = s.Drive;
            if (k < 1 || k > ActionCount || !_acts[k].IsUsed || !_acts[k].Target.IsPart) { _host.LastWhy = "the DO line has no part"; return; }
            LogicAction a = _acts[k];
            bool on = LogicAction.StatementOn(a.Sided, a.WhenOff);
            bool edge = !HandlerRun.Repeats(Seq(q).Trigger) || _seqPass[q] == 0;
            PushTo(base.Ship, 0, a.Target, on, a.Settings);
            string changed = ApplyAction(base.Ship, k, on, edge, out int kind, out Part p);
            if (changed.Length > 0)
                Log.Info("console at " + base.Part?.Location + ": function " + Seq(q).Rule + " handler " + SeqPlace(q) + " DO -> " + KindName(kind)
                    + " at " + p.Location + ": " + changed + ".");
        }

        private int PollDo(SeqStep s, int start)
        {
            int k = s.Drive;
            if (k < 1 || k > ActionCount || !_acts[k].IsUsed || !_acts[k].Target.IsPart) return SeqRunner.DONE;
            Part p = LogicNet.Resolve(base.Ship, _members, _acts[k].Target);
            if (p == null || !_members.Contains(p)) { _host.LastWhy = "part not on the wires"; return SeqRunner.RUNNING; }
            int kind = LogicAction.KindOf(p);
            if (!LogicSetStatement.CanWait(kind)) return SeqRunner.DONE;
            int status = PistonToggles.Settle(p, LogicSetStatement.SettleStepOf(_acts[k], kind), start, out string why);
            _host.LastWhy = why;
            return status;
        }

        private string ApplyAction(Ship ship, int k, bool on, bool edge, out int kind, out Part p)
        {
            kind = 0;
            p = LogicNet.Resolve(ship, _members, _acts[k].Target);
            if (p == null || !_members.Contains(p)) return "";
            kind = LogicAction.KindOf(p);
            if (kind == 2)
            {
                PistonToggles.Limits(GripService.PistonOf(p), out float[] speeds, out int max);
                return PistonToggles.Apply(p, PistonToggles.PlanDo(_acts[k], on, speeds, max));
            }
            if (kind == 1 || kind == 3) return ArmToggles.ApplyDo(p, kind, _acts[k], on);
            if (kind == LogicAction.KIND_CART || kind == LogicAction.KIND_SWITCH) return RailToggles.ApplyDo(p, kind, _acts[k], on);
            if (kind == LogicAction.KIND_DOCK) return DockToggles.ApplyDo(p, kind, _acts[k], on);
            if (kind == LogicAction.KIND_BAY || kind == LogicAction.KIND_BAY_STATION) return BayConsole.ApplyDo(p, kind, _acts[k], on);
            if (kind == LogicAction.KIND_BUTTON) return ButtonToggles.ApplyDo(p, _acts[k], on);
            if (kind == LogicAction.KIND_ADAPTER) return AdapterToggles.ApplyDo(p, _acts[k], edge);
            if (kind == LogicAction.KIND_LAMP) return LampToggles.ApplyDo(p, _acts[k]);
            return "";
        }

        private static string KindName(int kind)
        {
            switch (kind)
            {
                case LogicAction.KIND_MOTOR: return "motor";
                case LogicAction.KIND_PISTON: return "piston";
                case LogicAction.KIND_HINGE: return "hinge";
                case LogicAction.KIND_CART: return "cart";
                case LogicAction.KIND_SWITCH: return "switch";
                case LogicAction.KIND_DOCK: return "dock";
                case LogicAction.KIND_BAY: return "bay terminal";
                case LogicAction.KIND_BAY_STATION: return "bay station";
                case LogicAction.KIND_BUTTON: return "button";
                case LogicAction.KIND_ADAPTER: return "adapter";
                case LogicAction.KIND_LAMP: return "lamp";
                default: return "part";
            }
        }

        private int[] _mem = new int[1];
        private bool[] _memRead = new bool[1];
        private int[] _memSig = new int[1];
        private readonly HashSet<int> _calls = new HashSet<int>();
        private readonly Dictionary<long, List<int>> _callWait = new Dictionary<long, List<int>>();

        public static ID<PartToggleGuiRules> MemoryId(int r) => new ID<PartToggleGuiRules>(LogicStore.PREFIX + "m" + r + "_v");

        private bool IsMemoryRule(int r) => r >= 1 && r <= RuleCount && _rules[r].Exists && _rules[r].IsMemory;

        public int MemoryValue(int r)
        {
            if (!IsMemoryRule(r) || r >= _mem.Length) return 0;
            int sig = _ruleRaw[r, RULE_M];
            if (!_memRead[r] || _memSig[r] != sig)
            {
                _memRead[r] = true;
                _memSig[r] = sig;
                int? v = base.Ship != null && base.Part != null ? base.Ship.Metadata.PartUIToggleStates.Get((new PartSaveData(base.Part), MemoryId(r))) : null;
                _mem[r] = v ?? _rules[r].MemoryStart;
            }
            return _mem[r];
        }

        private void SetMemory(int r, int v)
        {
            if (MemoryValue(r) == v && _memRead[r]) return;
            _mem[r] = v;
            if (base.Ship != null && base.Part != null) base.Ship.Metadata.PartUIToggleStates.Set((new PartSaveData(base.Part), MemoryId(r)), v);
        }

        private void RunMemory(int q, SeqStep st)
        {
            int m = st.Target.IsRule ? st.Target.Index : 0;
            if (!IsMemoryRule(m)) { _host.LastWhy = "no memory value picked"; return; }
            int before = MemoryValue(m);
            int after = LogicMemory.Apply(st.Kind, st.PoseSet, st.Pose, st.Not, _rules[m].MemoryCounter, before, _rules[m].MemoryStart);
            SetMemory(m, after);
        }

        private int OwnerFunctionOf(int r) => r >= 1 && r <= RuleCount && _rules[r].IsCondition ? _rules[r].FunctionOf : r;

        private static long CallKey(int q, int index) => ((long)q << 32) | (uint)index;

        private void StartCall(int q, int index, SeqStep st)
        {
            int f = st.Target.IsRule ? st.Target.Index : 0;
            int caller = OwnerFunctionOf(Seq(q).Rule);
            var started = new List<int>();
            if (f < 1 || f > RuleCount || !_rules[f].Exists || _rules[f].IsCondition || _rules[f].IsHeader || f == caller)
            {
                _host.LastWhy = "no function to call";
                return;
            }
            if (!RulePaused(f))
                foreach (int c in _ruleSeqs[f])
                    if (_seqs[c].Trigger == HandlerTrigger.OnCall && _seqSteps[c].Count > 0) { _calls.Add(c); started.Add(c); }
            if (!st.NoWait) _callWait[CallKey(q, index)] = started;
            LogThrottle.Info("call|" + base.Part?.Location + "|" + caller + "|" + f, "console at " + base.Part?.Location + ": function " + caller + " CALL " + f + " -> " + started.Count + " ON CALL block(s)" + (st.NoWait ? ", not waiting." : "."));
        }

        private int PollCall(int q, int index)
        {
            long key = CallKey(q, index);
            if (!_callWait.TryGetValue(key, out List<int> started)) return SeqRunner.DONE;
            foreach (int c in started)
            {
                if (c > SeqCount || !_seqs[c].IsUsed || RulePaused(_seqs[c].Rule)) { _calls.Remove(c); continue; }
                if (_calls.Contains(c) || _runs[c].Running) { _host.LastWhy = "waiting for the called function"; return SeqRunner.RUNNING; }
            }
            _callWait.Remove(key);
            return SeqRunner.DONE;
        }

        public bool IsVariable(int r) => r >= 1 && r <= RuleCount && _rules[r].Exists && _rules[r].IsCondition;

        public int LineOwner(int k) => k >= 1 && k <= LineCount && _lines[k].IsUsed ? _lines[k].Rule : 0;

        private bool TryStepTarget(int q, int index, out SeqStep s, out Part p)
        {
            s = default;
            p = null;
            if (q < 1 || q > SeqCount) return false;
            List<SeqStep> steps = _seqSteps[q];
            if (index < 0 || index >= steps.Count) return false;
            s = steps[index];
            p = LogicNet.Resolve(base.Ship, _members, s.Target);
            return p != null && _members.Contains(p);
        }

        private bool[] ExprStack(int needed)
        {
            if (needed <= _exprStack.Length) return _exprStack;
            if (_exprStackBig == null || _exprStackBig.Length < needed) _exprStackBig = new bool[needed];
            return _exprStackBig;
        }

        private void PushTo(Ship ship, int tick, LogicRef target, bool on, in ActSettings s)
        {
            Part p = LogicNet.Resolve(ship, _members, target);
            if (p == null || !_members.Contains(p)) return;
            LogicNet.SignalOf(p)?.Push(ZtxSignalComponent.Clock(p.Ship), on, s);
        }

        private const int SPLIT_RETRY_TICKS = 30;
        private int _splitCheckTick = LogicAction.SPLIT_NEVER;
        private readonly HashSet<int> _splitLogged = new HashSet<int>();

        private void SplitOldLines(Part me, Ship ship, int tick)
        {
            if (!_v2 || !_decoded || !LogicAction.SplitDue(tick, _splitCheckTick, SPLIT_RETRY_TICKS)) return;
            _splitCheckTick = tick;
            for (int r = 1; r <= RuleCount; r++)
            {
                LogicRuleDef def = _rules[r];
                if (def.Exists && def.Target.IsPart && _splitLogged.Add(-r))
                    Log.Info("rule engine at " + me.Location + ": rule " + r + " still has an old rule-level target " + def.Target + " (from before DO lines; not a DO line, not split).");
            }
            var plans = new List<(int Slot, LogicAction[] Lines)>();
            int extra = 0;
            for (int k = 1; k <= ActionCount; k++)
            {
                LogicAction a = LogicAction.UnpackFull(RawAction(k, 0), RawAction(k, 1), RawAction(k, 2),
                    RawAction(k, ACT_S1), RawAction(k, ACT_S2), RawAction(k, ACT_V));
                if (_v2) a.Rule = _acts[k].Rule;
                if (!a.IsUsed || a.Sided) continue;
                Part p = a.Target.IsPart ? LogicNet.Resolve(ship, _members, a.Target) : null;
                if (a.Target.IsPart && (p == null || !_members.Contains(p)))
                {
                    if (_splitLogged.Add(k)) Log.Info("rule engine at " + me.Location + ": actuator slot " + k + " (" + a + ") stays an old ON / OFF line for now -- its target part is not on this console's network (retried every second).");
                    continue;
                }
                int kind = LogicAction.KindOf(p);
                if (!a.BlankLeaves && LogicAction.IsPositionedKind(kind))
                {
                    var pst = p.Ship.Metadata.PartUIToggleStates;
                    var ppsd = new PartSaveData(p);
                    bool pist = kind == LogicAction.KIND_PISTON;
                    a = LogicAction.FillOldPopouts(a, kind, pst.Get((ppsd, pist ? LogicAction.OldPistonOnId : LogicAction.OldMotorOnId)),
                        pst.Get((ppsd, pist ? LogicAction.OldPistonOffId : LogicAction.OldMotorOffId)));
                }
                LogicAction[] lines = LogicAction.Split(a, kind);
                if (!a.Target.IsPart) lines = new[] { lines[0] };
                plans.Add((k, lines));
                extra += lines.Length - 1;
            }
            if (plans.Count == 0) return;
            List<int> free = extra > 0 ? FreeActions(extra) : new List<int>();
            if (free.Count < extra) return;
            var states = ship.Metadata.PartUIToggleStates;
            var psd = new PartSaveData(me);
            void Put(ID<PartToggleGuiRules> id, int v) => states.Set((psd, id), v);
            void PutLine(int k, LogicAction l)
            {
                Put(ActionSlot(k, ACT_T), l.Target.Pack());
                Put(ActionSlot(k, ACT_M), l.PackMisc());
                Put(ActionSlot(k, ACT_P), l.PackPose());
                Put(ActionSlot(k, ACT_S1), l.OnStation.Pack());
                Put(ActionSlot(k, ACT_S2), 0);
                Put(ActionSlot(k, ACT_V), l.Drive);
            }
            var nextPos = new Dictionary<int, int>();
            int fi = 0;
            foreach (var (k, lines) in plans)
            {
                PutLine(k, lines[0]);
                string text = "rule engine at " + me.Location + ": actuator slot " + k + " split for ON / OFF lines -> " + lines[0];
                if (lines.Length > 1)
                {
                    int k2 = free[fi++];
                    int rule = lines[1].Rule;
                    if (!nextPos.TryGetValue(rule, out int pos))
                    {
                        var used = new List<int>();
                        foreach (int s2 in RuleActionSlots(rule)) used.Add(Action(s2).Pos);
                        foreach (int q in RuleSeqIds(rule)) used.Add(Seq(q).Pos);
                        pos = Math.Min(SeqDef.POS_MAX, LogicOutputStack.NextPos(used));
                    }
                    nextPos[rule] = Math.Min(SeqDef.POS_MAX, pos + 1);
                    PutLine(k2, lines[1]);
                    Put(ActionOwnerSlot(k2), rule);
                    Put(ActionPosSlot(k2), pos);
                    text += " and slot " + k2 + " -> " + lines[1];
                }
                Log.Info(text + ".");
            }
            int ver = states.Get((psd, LogicStore.VersionId)) ?? 1;
            int next = unchecked(ver * 31 + 17);
            Put(LogicStore.VersionId, next == 0 ? 1 : next);
        }

        private void Decode(Part me, Ship ship, int tick)
        {
            var psd = new PartSaveData(me);
            int? ver = ship.Metadata.PartUIToggleStates.Get((psd, LogicStore.VersionId));
            if (ver.HasValue && ver.Value != 0)
            {
                if (!_v2 || ver.Value != _storeVersion)
                {
                    if (!_v2)
                    {
                        Log.Info("rule engine at " + me.Location + ": reading storage v2.");
                        _structure++;
                    }
                    bool full = !_v2;
                    _v2 = true;
                    _storeVersion = ver.Value;
                    DecodeStore(me, ship, psd, full, tick);
                }
            }
            else
            {
                if (_v2)
                {
                    Log.Info("rule engine at " + me.Location + ": storage v2 version missing, reading the declared slots.");
                    _v2 = false;
                    _storeVersion = 0;
                    Array.Clear(_lineOwnerRaw, 0, _lineOwnerRaw.Length);
                    Array.Clear(_actOwnerRaw, 0, _actOwnerRaw.Length);
                    Array.Clear(_actPosRaw, 0, _actPosRaw.Length);
                    Array.Clear(_actStRaw, 0, _actStRaw.Length);
                    Array.Clear(_actDrvRaw, 0, _actDrvRaw.Length);
                    SetCapacity(_legacyLines, _legacyRules, _legacyActs);
                    ForceReread();
                }
                DecodeLegacy(me);
            }
            if (_exprDirty) CompileAll();
            _decoded = true;
        }

        private void ForceReread()
        {
            for (int k = 0; k <= LineCount; k++) for (int f = 0; f < 3; f++) _lineRaw[k, f] = int.MinValue;
            for (int r = 0; r <= RuleCount; r++) for (int f = 0; f < RULE_RAW; f++) _ruleRaw[r, f] = int.MinValue;
            for (int k = 0; k <= ActionCount; k++) for (int f = 0; f < 3; f++) _actRaw[k, f] = int.MinValue;
            for (int k = 0; k <= ActionCount; k++) { _actStRaw[k, 0] = int.MinValue; _actStRaw[k, 1] = int.MinValue; _actDrvRaw[k] = int.MinValue; }
        }

        private void DecodeLegacy(Part me)
        {
            RefreshToggleCache(me);
            for (int k = 1; k <= _legacyLines && k <= LineCount; k++)
                ApplyLine(me, k, Raw((k - 1) * 3), Raw((k - 1) * 3 + 1), Raw((k - 1) * 3 + 2), 0);
            int ruleBase = _legacyLines * 3;
            for (int r = 1; r <= _legacyRules && r <= RuleCount; r++)
                ApplyRule(r, Raw(ruleBase + (r - 1) * 4), Raw(ruleBase + (r - 1) * 4 + 1), Raw(ruleBase + (r - 1) * 4 + 2), Raw(ruleBase + (r - 1) * 4 + 3), 0);
            int actBase = ruleBase + _legacyRules * 4;
            for (int k = 1; k <= _legacyActs && k <= ActionCount; k++)
                ApplyAct(k, Raw(actBase + (k - 1) * 3), Raw(actBase + (k - 1) * 3 + 1), Raw(actBase + (k - 1) * 3 + 2), 0, 0, 0, 0, 0);
        }

        private void DecodeStore(Part me, Ship ship, PartSaveData psd, bool full, int tick)
        {
            StoreImage img = full ? LogicStoreReader.ReadFull(ship, psd, tick)
                : LogicStoreReader.ReadProbe(ship, psd, LineCount, RuleCount, ActionCount, StepCount, SeqCount);
            SetCapacity(img.MaxLine, img.MaxRule, img.MaxAct);
            SetStepCapacity(img.MaxStep);
            SetSeqCapacity(img.MaxSeq);
            for (int k = 1; k <= StepCount; k++)
            {
                int[] rec = img.Steps.TryGetValue(k, out int[] v) ? v : null;
                ApplyStep(k, rec);
            }
            for (int q = 1; q <= SeqCount; q++)
            {
                int[] rec = img.Seqs.TryGetValue(q, out int[] v) ? v : null;
                ApplySeq(ship, psd, q, rec);
            }
            for (int k = 1; k <= LineCount; k++)
            {
                int[] rec = img.Lines.TryGetValue(k, out int[] v) ? v : null;
                ApplyLine(me, k, rec?[0] ?? 0, rec?[1] ?? 0, rec?[2] ?? 0, rec?[LogicStore.LINE_R] ?? 0);
            }
            for (int r = 1; r <= RuleCount; r++)
            {
                int[] rec = img.Rules.TryGetValue(r, out int[] v) ? v : null;
                ApplyRule(r, rec?[0] ?? 0, rec?[1] ?? 0, rec?[2] ?? 0, rec?[3] ?? 0, rec?[RULE_C] ?? 0);
            }
            for (int k = 1; k <= ActionCount; k++)
            {
                int[] rec = img.Acts.TryGetValue(k, out int[] v) ? v : null;
                ApplyAct(k, rec?[0] ?? 0, rec?[1] ?? 0, rec?[2] ?? 0, rec?[LogicStore.ACT_R] ?? 0, rec?[LogicStore.ACT_O] ?? 0,
                    rec?[LogicStore.ACT_S1] ?? 0, rec?[LogicStore.ACT_S2] ?? 0, rec?[LogicStore.ACT_V] ?? 0);
            }
        }

        private void ApplyStep(int k, int[] rec)
        {
            bool same = true;
            for (int f = 0; f < LogicStore.STEP_FIELDS; f++)
                if ((rec?[f] ?? 0) != _stepRaw[k, f]) same = false;
            if (same) return;
            for (int f = 0; f < LogicStore.STEP_FIELDS; f++) _stepRaw[k, f] = rec?[f] ?? 0;
            SeqStep before = _steps[k];
            _steps[k] = SeqStep.Unpack(_stepRaw[k, STEP_T], _stepRaw[k, STEP_M], _stepRaw[k, STEP_P], _stepRaw[k, STEP_Q], _stepRaw[k, LogicStore.STEP_R]);
            if (_steps[k].IsUsed) _steps[k].Station = LogicRef.Unpack(_stepRaw[k, STEP_S]);
            if (_steps[k].IsUsed) _steps[k].Drive = _stepRaw[k, STEP_V];
            SeqStep after = _steps[k];
            _dataStamp++;
            _exprDirty = true;
            if (before.IsUsed != after.IsUsed || before.Seq != after.Seq || before.Order != after.Order)
                _structure++;
        }

        private void ApplySeq(Ship ship, PartSaveData psd, int q, int[] rec)
        {
            int m = rec?[LogicStore.SEQ_M] ?? 0, owner = rec?[LogicStore.SEQ_R] ?? 0;
            if (m == _seqRaw[q, LogicStore.SEQ_M] && owner == _seqRaw[q, LogicStore.SEQ_R]) return;
            _seqRaw[q, LogicStore.SEQ_M] = m;
            _seqRaw[q, LogicStore.SEQ_R] = owner;
            SeqDef before = _seqs[q];
            SeqDef after = SeqDef.Unpack(m, owner);
            _seqs[q] = after;
            if (!before.IsUsed && after.IsUsed && _runsRestored)
            {
                _runs[q] = new SeqRun();
                ship.Metadata.PartUIToggleStates.Set((psd, new ID<PartToggleGuiRules>(SEQ_PREFIX + "q" + q)), null);
            }
            _dataStamp++;
            _exprDirty = true;
            if (before.IsUsed != after.IsUsed || before.Rule != after.Rule || before.Pos != after.Pos) _structure++;
            if (!after.IsUsed) _runNote[q] = "";
        }

        private void ApplyLine(Part me, int k, int a, int b, int m, int owner)
        {
            if (a == _lineRaw[k, 0] && b == _lineRaw[k, 1] && m == _lineRaw[k, 2] && owner == _lineOwnerRaw[k]) return;
            bool sameRecord = a == _lineRaw[k, 0] && b == _lineRaw[k, 1]
                && (m & ~LogicLine.OWNER_MASK) == (_lineRaw[k, 2] & ~LogicLine.OWNER_MASK);
            _lineRaw[k, 0] = a;
            _lineRaw[k, 1] = b;
            _lineRaw[k, 2] = m;
            _lineOwnerRaw[k] = owner;
            _dataStamp++;
            LogicLine before = _lines[k];
            LogicLine l = LogicLine.Unpack(a, b, m);
            if (_v2) l.Rule = m != 0 ? owner : 0;
            _lines[k] = l;
            _exprDirty = true;
            bool ownerOnly = sameRecord && before.IsUsed && l.IsUsed;
            if (!before.IsUsed && l.IsUsed && _statesRestored && me?.Ship != null)
            {
                if (k < _lineSaved.Length) _lineSaved[k] = 0;
                me.Ship.Metadata.PartUIToggleStates.Set((new PartSaveData(me), LineStateId(k)), null);
            }
            if (!ownerOnly)
            {
                _states[k].Reset();
                ClearLineState(me, k);
            }
            if (before.Rule != l.Rule || before.IsUsed != l.IsUsed) _structure++;
            if (l.Format != 0 && l.Format != LogicLine.FORMAT && !_badFormatLogged[k])
            {
                _badFormatLogged[k] = true;
                Log.Info("rule engine at " + me.Location + ": line " + k + " has format " + l.Format + " (current " + LogicLine.FORMAT + "), ignored; delete and re-add it.");
            }
        }

        private void ApplyRule(int r, int t, int m, int n1, int n2, int c)
        {
            if (t == _ruleRaw[r, 0] && m == _ruleRaw[r, 1] && n1 == _ruleRaw[r, 2] && n2 == _ruleRaw[r, 3] && c == _ruleRaw[r, RULE_C]) return;
            _ruleRaw[r, 0] = t;
            _ruleRaw[r, 1] = m;
            _ruleRaw[r, 2] = n1;
            _ruleRaw[r, 3] = n2;
            _ruleRaw[r, RULE_C] = c;
            _dataStamp++;
            bool existed = _rules[r].Exists;
            _rules[r] = LogicRuleDef.Unpack(t, m, n1, n2);
            if (r < _memRead.Length) _memRead[r] = false;
            _ruleHash[r] = _rules[r].Shared && _rules[r].Name.Length > 0 ? LogicShared.Hash(_rules[r].Name) : 0;
            _exprDirty = true;
            if (existed != _rules[r].Exists) _structure++;
        }

        private void ApplyAct(int k, int t, int m, int p, int owner, int pos, int s1, int s2, int drv)
        {
            if (t == _actRaw[k, 0] && m == _actRaw[k, 1] && p == _actRaw[k, 2] && owner == _actOwnerRaw[k] && pos == _actPosRaw[k]
                && s1 == _actStRaw[k, 0] && s2 == _actStRaw[k, 1] && drv == _actDrvRaw[k]) return;
            _actRaw[k, 0] = t;
            _actRaw[k, 1] = m;
            _actRaw[k, 2] = p;
            _actOwnerRaw[k] = owner;
            _actPosRaw[k] = pos;
            _actStRaw[k, 0] = s1;
            _actStRaw[k, 1] = s2;
            _actDrvRaw[k] = drv;
            _dataStamp++;
            LogicAction before = _acts[k];
            LogicAction a = LogicAction.UnpackFull(t, m, p, s1, s2, drv);
            if (_v2 && a.IsUsed) a.Rule = owner;
            a.Pos = a.IsUsed ? pos : 0;
            _acts[k] = a;
            _exprDirty = true;
            if (before.Rule != a.Rule || before.Pos != a.Pos) _structure++;
        }

        private void RefreshToggleCache(Part me)
        {
            if (_toggleCache != null && _toggleCache[0] != null && ReferenceEquals(PartUIToggle.GetOnPart(me, _allSlots[0]), _toggleCache[0])) return;
            if (_toggleCache == null) _toggleCache = new PartUIToggle[_allSlots.Length];
            for (int i = 0; i < _allSlots.Length; i++) _toggleCache[i] = PartUIToggle.GetOnPart(me, _allSlots[i]);
        }

        private int Raw(int index) => _toggleCache[index]?.Mode ?? 0;

        private void CompileGroups()
        {
            var order = new List<int>();
            for (int r = 1; r <= RuleCount; r++) if (_rules[r].Exists && !_rules[r].IsCondition) order.Add(r);
            Dictionary<int, int> headerOf = LogicGroups.HeaderOf(order, r => _rules[r].IsHeader, r => _rules[r].InGroup);
            Array.Clear(_groupOff, 0, _groupOff.Length);
            foreach (int r in order)
                if (!_rules[r].IsHeader) _groupOff[r] = LogicGroups.Paused(r, false, headerOf, h => _rules[h].Disabled);
            for (int r = 1; r <= RuleCount; r++)
            {
                if (!_rules[r].Exists || !_rules[r].IsCondition) continue;
                int f = _rules[r].FunctionOf;
                _groupOff[r] = f >= 1 && f <= RuleCount && (_rules[f].Disabled || _groupOff[f]);
            }
        }

        private void CompileAll()
        {
            _exprDirty = false;
            CompileGroups();
            Array.Clear(_feeds, 0, _feeds.Length);
            for (int r = 0; r <= RuleCount; r++)
            {
                _ruleLines[r].Clear();
                _ruleActs[r].Clear();
            }
            for (int k = 1; k <= LineCount; k++)
            {
                ref LogicLine reader = ref _lines[k];
                if (!reader.IsUsed) continue;
                if (reader.Rule >= 1 && reader.Rule <= RuleCount) _ruleLines[reader.Rule].Add(k);
                MarkFeed(reader.A, k);
                MarkFeed(reader.B, k);
            }
            for (int k = 1; k <= ActionCount; k++)
            {
                int owner = _acts[k].Rule;
                if (_acts[k].IsUsed && owner >= 1 && owner <= RuleCount) _ruleActs[owner].Add(k);
            }
            for (int r = 0; r <= RuleCount; r++) _ruleSeqs[r].Clear();
            for (int q = 0; q <= SeqCount; q++) _seqStepIds[q].Clear();
            for (int q = 1; q <= SeqCount; q++)
                if (_seqs[q].IsUsed && _seqs[q].Rule <= RuleCount) _ruleSeqs[_seqs[q].Rule].Add(q);
            for (int r = 0; r <= RuleCount; r++)
                _ruleSeqs[r].Sort((a, b) => _seqs[a].Pos != _seqs[b].Pos ? _seqs[a].Pos.CompareTo(_seqs[b].Pos) : a.CompareTo(b));
            for (int k = 1; k <= StepCount; k++)
            {
                SeqStep s = _steps[k];
                if (!s.IsUsed || s.Seq > SeqCount || !_seqs[s.Seq].IsUsed) continue;
                _seqStepIds[s.Seq].Add(k);
            }
            for (int q = 0; q <= SeqCount; q++)
            {
                SortSteps(_seqStepIds[q]);
                _seqSteps[q].Clear();
                foreach (int k in _seqStepIds[q]) _seqSteps[q].Add(_steps[k]);
            }
            Array.Clear(_actInBody, 0, _actInBody.Length);
            for (int q = 1; q <= SeqCount; q++)
                foreach (SeqStep s in _seqSteps[q])
                    if (s.Kind == SeqKind.Do && s.Drive >= 1 && s.Drive < _actInBody.Length) _actInBody[s.Drive] = true;
            for (int r = 1; r <= RuleCount; r++)
            {
                List<ExprTerm> terms = _terms[r];
                terms.Clear();
                if (_rules[r].Exists)
                {
                    foreach (int k in _ruleLines[r])
                    {
                        if (_feeds[k] != 0) continue;
                        ref LogicLine line = ref _lines[k];
                        terms.Add(new ExprTerm { Line = k, Or = line.Or, Open = line.Open, Close = line.Close });
                    }
                }
                _exprError[r] = LogicExpr.Compile(terms, _programs[r], out _exprUnclosed[r]);
            }
        }

        private void SortSteps(List<int> ids) => ids.Sort((a, b) => _steps[a].Order != _steps[b].Order ? _steps[a].Order.CompareTo(_steps[b].Order) : a.CompareTo(b));

        private void MarkFeed(LogicRef r, int reader)
        {
            if (!r.IsLocal || r.Index > LineCount || r.Index == reader) return;
            if (!_lines[r.Index].IsUsed || _lines[r.Index].Rule != _lines[reader].Rule) return;
            if (_feeds[r.Index] == 0) _feeds[r.Index] = reader;
        }

        private float ReadNumber(Ship ship, in LogicRef r, bool units)
        {
            float onValue = units ? 1f : 100f;
            if (r.IsPart)
            {
                Part p = LogicNet.Resolve(ship, _members, r);
                if (p == null || !_members.Contains(p)) return 0f;
                if (units)
                {
                    if (LogicNet.IsValueSource(p)) return LogicNet.ReadUnits(p) ?? float.NaN;
                    return LogicNet.ReadOutput(p) ? onValue : 0f;
                }
                float? v = LogicNet.ReadValue(p);
                if (v != null) return v.Value;
                return LogicNet.ReadOutput(p) ? onValue : 0f;
            }
            if (r.IsRule && IsMemoryRule(r.Index)) return MemoryValue(r.Index);
            return ReadInput(ship, in r) ? onValue : 0f;
        }

        private bool ReadInput(Ship ship, in LogicRef r)
        {
            switch (r.Kind)
            {
                case RefKind.On: return true;
                case RefKind.Rule:
                    if (r.Index > RuleCount) return false;
                    if (_rules[r.Index].IsMemory) return MemoryValue(r.Index) != 0;
                    return _ruleWritten[r.Index] ? _ruleCur[r.Index] : _rulePrev[r.Index];
                case RefKind.Local:
                    if (r.Index > LineCount) return false;
                    return _lineWritten[r.Index] ? _lineCur[r.Index] : _linePrev[r.Index];
                case RefKind.Part:
                case RefKind.Cart:
                    Part p = LogicNet.Resolve(ship, _members, r);
                    return p != null && _members.Contains(p) && LogicNet.ReadOutput(p);
                case RefKind.Shared:
                    return LogicShared.Read(ship, r.Index) == true;
                default: return false;
            }
        }
    }
}
