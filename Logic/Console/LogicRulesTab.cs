using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Gui;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Graphics;
using Halfling.Gui.Components.Text;

namespace ZTX.HingesPistonsDocking
{
    internal sealed partial class LogicRulesTab
    {
        public static float RowWidth => 1100f * LogicUiScale.Rules;
        public static float RowHeight => 28f * LogicUiScale.Rules;
        private static float NameWidth => 120f * LogicUiScale.Rules;
        private static float NumberWidth => 40f * LogicUiScale.Rules;
        private static float ShareWidth => 70f * LogicUiScale.Rules;
        private static float TargetWidth => 170f * LogicUiScale.Rules;
        private static float PoseWidth => 64f * LogicUiScale.Rules;
        private static float UnitWidth => 46f * LogicUiScale.Rules;
        private static float SpeedWidth => 56f * LogicUiScale.Rules;
        private static float SpeedUnitWidth => 60f * LogicUiScale.Rules;
        private static float ResultWidth => 60f * LogicUiScale.Rules;
        private static float ButtonWidth => 90f * LogicUiScale.Rules;
        private static float ToggleWidth => 80f * LogicUiScale.Rules;
        private static float IndentWidth => 24f * LogicUiScale.Rules;
        private static float LWidth => 36f * LogicUiScale.Rules;
        private static float InWidth => 170f * LogicUiScale.Rules;
        private static float OpWidth => 105f * LogicUiScale.Rules;
        private static float ParamWidth => 68f * LogicUiScale.Rules;
        private static float LineUnitWidth => 44f * LogicUiScale.Rules;
        private static float StateWidth => 80f * LogicUiScale.Rules;
        private static float SmallButton => 28f * LogicUiScale.Rules;
        private static float BoxPad => 6f * LogicUiScale.Rules;
        private const float FrameBorderScale = 1.5f;
        private static float InnerWidth => RowWidth - 2f * BoxPad;
        private static float SectionHeight => 20f * LogicUiScale.Rules;
        private static float GapY => 2f * LogicUiScale.Rules;
        private static float BLabelWidth => 44f * LogicUiScale.Rules;
        private static float ConnWidth => 70f * LogicUiScale.Rules;
        private static float BracketWidth => 36f * LogicUiScale.Rules;
        private static float NotWidth => 40f * LogicUiScale.Rules;
        private static float CounterWidth => 420f * LogicUiScale.Rules;
        private const int SAY_EVERY = 10;
        private const int LIVE_EVERY = 6;
        private const int DELETE_CONFIRM_MS = 3000;
        private const int H_SIGNALS = -1, H_RULES = -2, H_LINES = -3, H_BUTTONS = -4, H_SENSORS = -5, H_ACTUATORS = -6, H_SHARED = -7, H_VARS = -8, H_MEMORY = -9;

        private sealed class HideCell
        {
            public Widget Control;
            public Widget Spacer;
            public bool Shown = true;
            public bool KeepSpace = true;

            public void Show(bool show)
            {
                if (show == Shown) return;
                Shown = show;
                Control.SelfActive = show;
                Spacer.SelfActive = !show && KeepSpace;
            }
        }

        private sealed class LineRow
        {
            public int Line;
            public int Rule;
            public LayoutBox Box;
            public PickerCell A, Op, B;
            public TextCell Value;
            public Button Unit;
            public int LastUnitsState = -1;
            public Label State;
            public int[] LastRaw = { int.MinValue, int.MinValue, int.MinValue };
            public long ItemsKey = long.MinValue;
            public LogicLine Current;
            public int Number;
            public Label BLabel;
            public HideCell BLabelCell, BCell, ValueCell, UnitCell;
            public Button Conn, OpenB, CloseB;
            public HideCell ConnCell, OpenCell, CloseCell;
            public Label Num;
            public Button NotA, NotB;
            public HideCell NotACell, NotBCell;
            public int LastNot = -1;
            public SimpleTextProvider Say;
            public SimpleTextProvider ATip, BTip;
        }

        private sealed class RuleBlock
        {
            public int Rule;
            public LayoutBox Header;
            public TextCell Name;
            public Label Result;
            public Button Toggle;
            public bool IsGroup;
            public Button Share;
            public int LastShared = -1;
            public Button Del;
            public bool DelConfirm;
            public Button Swatch;
            public LayoutBox Palette;
            public Button Preview;
            public TextCell Hex;
            public RuleBlock Group;
            public readonly List<RuleBlock> Members = new List<RuleBlock>();
            public Label Count;
            public Button AddLine;
            public int LastN1 = int.MinValue, LastN2 = int.MinValue;
            public int LastDisabled = -1;
            public List<LineRow> Lines = new List<LineRow>();
            public LayoutBox Box;
            public Button Collapse;
            public readonly List<Widget> Body = new List<Widget>();
            public readonly List<ActRow> Acts = new List<ActRow>();
            public Button AddAct;
            public readonly HashSet<Widget> Hidden = new HashSet<Widget>();
            public Label Expr;
            public Label Warn;
            public bool WarnShown;
            public SimpleTextProvider Say;
            public int SayFrame = int.MinValue / 2;
            public bool BodyBuilt;
            public bool Complete;
            public Label Number;
            public Label KindTag;
            public int LastCode = -1;
            public readonly List<SeqBlock> Seqs = new List<SeqBlock>();
            public int Position, LastPosition = -1, LastColour = -1;
            public string LastResultHex;
            public bool IsFunction;
            public bool IsCondition;
            public RuleBlock Owner;
            public readonly List<RuleBlock> Conds = new List<RuleBlock>();
            public PickerCell AddTop;
            public LayoutBox VarBox;
            public Button AddVar;
        }

        private sealed class BuildState
        {
            public bool Swap;
            public long Start;
            public int StartFrame;
            public long Work;
            public long WorstFrame;
            public readonly List<Widget> Widgets = new List<Widget>();
            public readonly List<RuleBlock> Blocks = new List<RuleBlock>();
            public Button AddRule;
            public Button AddRuleBottom;
            public Button AddGroup;
            public Button AddSignal;
            public readonly List<MemRow> MemRows = new List<MemRow>();
            public Button AddMem;
            public Dictionary<int, int> GroupOf = new Dictionary<int, int>();
            public readonly Dictionary<int, RuleBlock> Groups = new Dictionary<int, RuleBlock>();
            public Label Counter;
        }

        private readonly GameRoot _game;
        private readonly ScrollBox _page;
        private readonly LogicPickerPopup _picker;
        private readonly TextCellEditor _editor;
        private readonly List<Widget> _widgets = new List<Widget>();
        private readonly List<RuleBlock> _blocks = new List<RuleBlock>();
        private Button _addRule;
        private Button _addRuleBottom;
        private Button _addGroup;
        private Button _addSignal;
        private Part _console;
        private ZtxRuleEngineComponent _engine;
        private int _structure = int.MinValue;
        private string _membersSig;
        private bool _refreshing;

        private static readonly ConditionalWeakTable<Part, HashSet<int>> s_collapsed = new ConditionalWeakTable<Part, HashSet<int>>();
        private long _rulesSig = long.MinValue;
        private bool _invertMigrated;
        private Label _counter;
        private bool _storeConverted;
        private int _migrationTick = -1000;
        private int _frame;
        private int _confirmRule;
        private int _confirmUntil;
        private bool _forceFull = true;
        private readonly LogicBuildQueue _queue = new LogicBuildQueue();
        private BuildState _build;
        private List<Part> _buildMembers = new List<Part>();
        private const double BUILD_BUDGET_MS = 8.0;
        private int _lastDataStamp = int.MinValue;
        private bool _lastCommandable;
        private long _itemsKey;
        private int _counterStamp = int.MinValue;
        private string _counterCache;
        private static float RowGap => 4f * LogicUiScale.Rules;
        private int _rowLogAt = -1;
        private LogicRuleText _text;

        public Action<LayoutBox> ExtraToolbar;
        private int _flashRule;
        private int _flashUntil;
        private const int FLASH_FRAMES = 60;

        public ScrollBox Page => _page;

        public void JumpTo(int rule)
        {
            RuleBlock b = _blocks.Find(x => x.Rule == rule);
            if (b == null) return;
            if (b.Group != null && IsCollapsed(b.Group.Rule)) ToggleGroupFold(b.Group);
            if (IsCollapsed(rule)) ToggleCollapse(b);
            Rect view = _page.ScrollBarController.ViewRectProvider.ScreenRect;
            _page.ScrollBarController.ScrollVertical(b.Box.ScreenRect.Top - view.Top - 6f);
            _flashRule = rule;
            _flashUntil = _frame + FLASH_FRAMES;
            _forceFull = true;
        }

        private bool IsCollapsed(int r) => _console != null && s_collapsed.TryGetValue(_console, out HashSet<int> set) && set.Contains(r);

        private void SetCollapsed(int r, bool on)
        {
            if (_console == null) return;
            HashSet<int> set = s_collapsed.GetOrCreateValue(_console);
            if (on) set.Add(r);
            else set.Remove(r);
        }

        public LogicRulesTab(GameRoot game, ScrollBox page, LogicPickerPopup picker, TextCellEditor editor)
        {
            _game = game;
            _page = page;
            _picker = picker;
            _editor = editor;
            AttachDropPainter();
        }

        public void Reset()
        {
            _queue.Clear();
            _build = null;
            ClearPage();
            _structure = int.MinValue;
            _membersSig = null;
            _rulesSig = long.MinValue;
            _forceFull = true;
        }

        private void ClearPage()
        {
            _picker.Close();
            _editor.Close();
            foreach (Widget w in _widgets) _page.Children.Remove(w);
            _widgets.Clear();
            _blocks.Clear();
            _addRule = null;
            _addRuleBottom = null;
            _addGroup = null;
            _addSignal = null;
            _memRows.Clear();
            _addMem = null;
            _counter = null;
        }

        public void Refresh(Part console, ZtxRuleEngineComponent engine, List<Part> members)
        {
            bool rebound = !ReferenceEquals(console, _console);
            if (rebound) _migrated.Clear();
            if (rebound) _invertMigrated = false;
            if (rebound) { _storeConverted = false; _migrationTick = -1000; _bodyStage = 0; }
            _console = console;
            _engine = engine;
            if (engine != null && (_text == null || rebound || !ReferenceEquals(_text.Engine, engine))) _text = new LogicRuleText(engine, console);
            if (engine == null)
            {
                if (_structure != -1) { Reset(); _widgets.Add(Note("ZtxLogicPanel/NoEngine")); _structure = -1; }
                return;
            }
            string msig = MembersSignature(members);
            long rsig = RulesSignature();
            if (rebound || engine.Structure != _structure || msig != _membersSig || rsig != _rulesSig)
            {
                Rebuild(members);
                _structure = engine.Structure;
                _membersSig = msig;
                _rulesSig = rsig;
            }
            if (_queue.Pending > 0)
            {
                BuildState st = _build;
                long spent = _queue.Run(BUILD_BUDGET_MS);
                if (st != null)
                {
                    st.Work += spent;
                    if (spent > st.WorstFrame) st.WorstFrame = spent;
                }
            }
            bool commandable = console.Ship != null && console.Ship.Commands.IsCommandableByLocalPlayer;
            if (!commandable && _lastCommandable) _editor.Close();
            bool waiting = AwaitingDecode(console);
            bool ready = commandable && engine.HasDecoded && !waiting;
            bool pending = MigrateLegacy(ready);
            pending |= MigratePistonPopouts(ready);
            if (ready && !_invertMigrated && AllEnginesDecoded(console.Ship))
            {
                _invertMigrated = true;
                var plan = LogicInvertMigration.Plan(console.Ship);
                if (plan.Count > 0) pending = true;
                WriteParts(plan);
            }
            int logicTick = LogicShipManager.TryGet(console.Ship)?.TickCount ?? 0;
            if (pending) _migrationTick = logicTick;
            if (ready && _invertMigrated && !_storeConverted && !_engine.IsStoreV2 && !HasStoreVersion(console)
                && logicTick - _migrationTick >= 3)
            {
                _storeConverted = true;
                ConvertToStore(console);
            }
            if (ready && _invertMigrated && _engine.IsStoreV2 && _bodyStage < 3 && logicTick - _migrationTick >= 3)
            {
                if (_bodyStage == 0) { ConvertDoLines(); ConvertCommandSteps(); }
                else if (_bodyStage == 1) ConvertBodies();
                else ConvertVariableNames();
                _bodyStage++;
                _migrationTick = logicTick;
            }
            if (_addRule != null) _addRule.SelfInputActive = commandable && !waiting && engine.IsStoreV2 && engine.FreeRule() > 0;
            if (_addRuleBottom != null) _addRuleBottom.SelfInputActive = commandable && !waiting && engine.IsStoreV2 && engine.FreeRule() > 0;
            if (_addSignal != null) _addSignal.SelfInputActive = commandable && !waiting && engine.FreeRule() > 0;
            if (_addGroup != null) _addGroup.SelfInputActive = commandable && !waiting && engine.FreeRule() > 0;
            _frame++;
            if (_counter != null)
            {
                if (_counterCache == null || engine.DataStamp != _counterStamp)
                {
                    _counterCache = CounterText();
                    _counterStamp = engine.DataStamp;
                }
                string c = waiting ? Strings.GetText("ZtxLogicPanel/WaitingForTick") : _counterCache;
                if (_counter.Text != c) _counter.Text = c;
            }
            if (_flashRule != 0 && _frame >= _flashUntil) { _flashRule = 0; _forceFull = true; }
            if (_rowLogAt >= 0 && _frame >= _rowLogAt) { _rowLogAt = -1; LogRowWidths(); LogStatementWidths(); }
            bool all = _forceFull || engine.DataStamp != _lastDataStamp || commandable != _lastCommandable;
            _forceFull = false;
            _lastDataStamp = engine.DataStamp;
            _lastCommandable = commandable;
            _itemsKey = ItemsKey(engine.DataStamp, _membersSig, console.Ship);
            int number = 0;
            for (int i = 0; i < _blocks.Count; i++) _blocks[i].Position = _blocks[i].IsGroup ? 0 : ++number;
            for (int i = 0; i < _blocks.Count; i++)
                if (_blocks[i].Complete && (all || (_frame + i) % LIVE_EVERY == 0)) RefreshBlock(_blocks[i], members, commandable);
            if (all || _frame % LIVE_EVERY == 0) RefreshMemory(commandable);
            ApplyVisibility();
            FitGroups();
            RefreshDrag();
        }

        private static long ItemsKey(int dataStamp, string membersSig, Cosmoteer.Ships.Ship ship)
        {
            long h = ((long)dataStamp << 32) ^ (uint)(membersSig?.GetHashCode() ?? 0);
            foreach (var (hash, _) in LogicShared.Names(ship)) h = h * 31 + hash;
            return h;
        }

        private static string MembersSignature(List<Part> members)
        {
            var sb = new System.Text.StringBuilder();
            foreach (Part p in members) sb.Append(LogicRef.Part(p).Pack()).Append(':').Append((int)LogicNet.KindOf(p)).Append('|');
            return sb.ToString();
        }

        private long RulesSignature()
        {
            long h = 17;
            for (int r = 1; r <= _engine.RuleCount; r++)
            {
                h = h * 31 + (_engine.Rule(r).Exists ? 1 : 0);
                h = h * 31 + (_engine.Rule(r).IsHeader ? 1 : 0);
                h = h * 31 + (_engine.Rule(r).InGroup ? 1 : 0);
                h = h * 31 + (_engine.Rule(r).IsCondition ? 1 : 0);
                h = h * 31 + _engine.RawRule(r, ZtxRuleEngineComponent.RULE_T);
            }
            return h;
        }

        private void RefreshBlock(RuleBlock b, List<Part> members, bool commandable)
        {
            LogicRuleDef def = _engine.Rule(b.Rule);
            if (b.IsGroup) { RefreshGroup(b, def, commandable); return; }
            RefreshName(b, commandable);
            int disabled = def.Disabled ? 1 : 0;
            if (disabled != b.LastDisabled)
            {
                b.LastDisabled = disabled;
                b.Toggle.TextProvider = Strings.KeyString(def.Disabled ? "ZtxLogicPanel/RuleDisabled" : "ZtxLogicPanel/RuleEnabled");
                StateTip(b.Toggle, def.Disabled ? "ZtxLogicPanel/ToggleRuleTipDisabled" : "ZtxLogicPanel/ToggleRuleTipEnabled");
            }
            b.Toggle.SelfInputActive = commandable;
            bool paused = _engine.RulePaused(b.Rule);
            int lineCount = b.BodyBuilt ? b.Lines.Count : _engine.RuleLineCount(b.Rule);
            bool fn = b.IsFunction && lineCount == 0;
            string word = fn ? _text.FunctionState(b.Rule, colour: false)
                : lineCount == 0 ? Strings.GetText("ZtxLogicPanel/RuleEmpty")
                : paused ? Strings.GetText("ZtxLogicPanel/RulePaused")
                : Strings.GetText(_engine.RuleResult(b.Rule) ? "ZtxLogicPanel/SigOn" : "ZtxLogicPanel/SigOff");
            if (b.Result.Text != word) b.Result.Text = word;
            string resultHex = fn ? (!paused && _text.FunctionRunning(b.Rule) ? LogicPalette.On : LogicPalette.Dim)
                : lineCount == 0 || paused ? LogicPalette.Dim : _engine.RuleResult(b.Rule) ? LogicPalette.On : LogicPalette.Off;
            if (resultHex != b.LastResultHex) { b.LastResultHex = resultHex; SetTextColour(b.Result, resultHex); }
            RefreshHeaderStyle(b, def, commandable);
            RefreshKindTag(b);
            if (b.BodyBuilt) RefreshBody(b, def, members, commandable);
            int shared = def.Shared ? 1 : 0;
            if (shared != b.LastShared)
            {
                b.LastShared = shared;
                b.Share.TextProvider = Strings.KeyString(def.Shared ? "ZtxLogicPanel/SharedOn" : "ZtxLogicPanel/SharedOff");
                StateTip(b.Share, def.Shared ? "ZtxLogicPanel/TipSharedShared" : "ZtxLogicPanel/TipSharedLocal");
                SetLit(b.Share, def.Shared, LogicPalette.LitNot);
            }
            b.Share.SelfInputActive = commandable && _engine.IsStoreV2;
            RefreshSwatch(b, commandable);
            RefreshDelete(b, commandable);
        }

        private void RefreshBody(RuleBlock b, LogicRuleDef def, List<Part> members, bool commandable)
        {
            if (b.AddLine != null) b.AddLine.SelfInputActive = commandable && _engine.FreeLine() > 0;
            if (b.Expr != null)
            {
                string expr = ExprText(b, def);
                if (b.Expr.Text != expr) b.Expr.Text = expr;
            }
            RefreshDo(b, members, commandable);
            RefreshSequences(b, members, commandable);
            if (b.Warn != null) RefreshWarnings(b, def);
            if (_frame - b.SayFrame >= SAY_EVERY)
            {
                b.SayFrame = _frame;
                bool fn = b.IsFunction && b.Lines.Count == 0;
                b.Say.Text = (fn ? _text.FunctionOutline(b.Rule, xml: false) : RuleSentence(b, def)) + "\n\n"
                    + Strings.GetText(fn ? "ZtxLogicPanel/TipFuncResult" : "ZtxLogicPanel/TipResult");
            }
            foreach (LineRow row in b.Lines) RefreshLine(b, row, members, commandable);
            foreach (RuleBlock c in b.Conds) if (c.Box.SelfActive) RefreshCondition(c, members, commandable);
        }

        private string ExprText(RuleBlock b, LogicRuleDef def) => _text != null ? _text.Expr(b.Rule, colourResult: false) : "";

        private string RuleSentence(RuleBlock b, LogicRuleDef def) => _text != null ? _text.RuleOutline(b.Rule, xml: false) : "";

        private string LineSentence(RuleBlock b, LineRow row) => _text != null ? _text.LineSentence(b.Rule, row.Line) : "";

        private string RuleName(int r) => _text != null ? _text.RuleName(r) : Strings.FormatText("ZtxLogicPanel/RuleUnnamedFmt", r);

        private static int PositionOf(RuleBlock b, int slot)
        {
            for (int i = 0; i < b.Lines.Count; i++) if (b.Lines[i].Line == slot) return i + 1;
            return 0;
        }

        private void RefreshLine(RuleBlock b, LineRow row, List<Part> members, bool commandable)
        {
            bool anyOpen = _picker.IsOpenFor(row.A) || _picker.IsOpenFor(row.Op) || _picker.IsOpenFor(row.B);
            bool changed = false;
            for (int f = 0; f < 3; f++)
            {
                int raw = _engine.RawLine(row.Line, f);
                if (raw != row.LastRaw[f]) changed = true;
            }
            if (changed && !anyOpen)
            {
                for (int f = 0; f < 3; f++) row.LastRaw[f] = _engine.RawLine(row.Line, f);
                row.Current = LogicLine.Unpack(row.LastRaw[0], row.LastRaw[1], row.LastRaw[2]);
            }
            LogicLine cur = row.Current;
            InputClass aClass = ClassOf(cur.A);
            if (!anyOpen)
            {
                bool rebuilt = false;
                if (changed || row.ItemsKey != _itemsKey)
                {
                    row.ItemsKey = _itemsKey;
                    var opItems = OpItems(aClass, cur.Op);
                    if (SignatureOf(opItems) != row.Op.Signature) { FillOps(row.Op, opItems, aClass); rebuilt = true; }
                    var aItems = InputItems(b, row, members, cur.A);
                    if (SignatureOf(aItems) != row.A.Signature) { Fill(row.A, aItems); rebuilt = true; }
                    var bItems = BItems(b, row, members, cur);
                    if (SignatureOf(bItems) != row.B.Signature) { Fill(row.B, bItems); rebuilt = true; }
                }
                if (changed || rebuilt)
                {
                    Select(row.A, cur.A.Pack());
                    Select(row.B, cur.B.Pack());
                    Select(row.Op, (int)cur.Op);
                    ShowValue(row, cur);
                    row.Op.Button.ToolTips.TextProvider = Strings.KeyString(LogicOpCatalog.IsAllowed(aClass, cur.Op) ? OpTipKey(aClass, cur.Op) : "ZtxLogicPanel/TipOpNotOffered");
                }
            }
            BRole role = LogicOpCatalog.RoleOfB(cur.Op);
            Part aPart = cur.A.IsPart ? LogicNet.Resolve(_console.Ship, _engine.Members, cur.A) : null;
            bool aUnits = LogicNet.ReportsUnits(aPart) || IsMemCounterRef(cur.A);
            int feeds = _engine.FeedsLine(row.Line);
            List<ExprTerm> terms = _engine.RuleTerms(b.Rule);
            bool first = feeds == 0 && terms.Count > 0 && terms[0].Line == row.Line;
            row.ConnCell.Show(!first);
            row.OpenCell.Show(feeds == 0);
            row.CloseCell.Show(feeds == 0);
            string conn = feeds != 0 ? Strings.FormatText("ZtxLogicPanel/FeedsFmt", PositionOf(b, feeds))
                : Strings.GetText(cur.Or ? "ZtxLogicPanel/ConnOr" : "ZtxLogicPanel/ConnAnd");
            if (row.Conn.Text != conn)
            {
                row.Conn.TextProvider = null;
                row.Conn.Text = conn;
            }
            StateTip(row.Conn, feeds != 0 ? "ZtxLogicPanel/TipFeeds" : cur.Or ? "ZtxLogicPanel/TipConnOr" : "ZtxLogicPanel/TipConnAnd");
            row.Conn.SelfInputActive = commandable && feeds == 0;
            string open = cur.Open == 0 ? Strings.GetText("ZtxLogicPanel/BracketNone") : new string('(', cur.Open);
            if (row.OpenB.Text != open) { row.OpenB.TextProvider = null; row.OpenB.Text = open; }
            SetLit(row.OpenB, cur.Open > 0, LogicPalette.LitBracket);
            string close = cur.Close == 0 ? Strings.GetText("ZtxLogicPanel/BracketNone") : new string(')', cur.Close);
            if (row.CloseB.Text != close) { row.CloseB.TextProvider = null; row.CloseB.Text = close; }
            SetLit(row.CloseB, cur.Close > 0, LogicPalette.LitBracket);
            row.OpenB.SelfInputActive = commandable;
            row.CloseB.SelfInputActive = commandable;
            row.BLabelCell.Show(role != BRole.None);
            row.BCell.Show(role != BRole.None);
            row.NotACell.Show(cur.Op != LogicOp.None);
            row.NotBCell.Show(role == BRole.Input || role == BRole.Reset);
            int nots = (cur.NotA ? 1 : 0) | (cur.NotB ? 2 : 0);
            if (nots != row.LastNot)
            {
                row.LastNot = nots;
                row.NotA.TextProvider = Strings.KeyString(cur.NotA ? "ZtxLogicPanel/NotOn" : "ZtxLogicPanel/NotOff");
                row.NotB.TextProvider = Strings.KeyString(cur.NotB ? "ZtxLogicPanel/NotOn" : "ZtxLogicPanel/NotOff");
                StateTip(row.NotA, cur.NotA ? "ZtxLogicPanel/TipNotAOn" : "ZtxLogicPanel/TipNotAOff");
                StateTip(row.NotB, cur.NotB ? "ZtxLogicPanel/TipNotBOn" : "ZtxLogicPanel/TipNotBOff");
                SetLit(row.NotA, cur.NotA, LogicPalette.LitNot);
                SetLit(row.NotB, cur.NotB, LogicPalette.LitNot);
            }
            bool test = LogicRuleEval.IsCompare(cur.Op) || LogicOpCatalog.IsStateOp(cur.Op);
            StateTip(row.NotA, test ? (cur.NotA ? "ZtxLogicPanel/TipNotTestOn" : "ZtxLogicPanel/TipNotTestOff")
                : cur.NotA ? "ZtxLogicPanel/TipNotAOn" : "ZtxLogicPanel/TipNotAOff");
            row.NotA.SelfInputActive = commandable;
            row.NotB.SelfInputActive = commandable;
            row.ValueCell.Show(LogicOpCatalog.ShowsValue(cur.Op, cur.B));
            int unitsState = LogicOpCatalog.UnitsState(cur.Op, aUnits || cur.Units);
            row.UnitCell.Show(unitsState != LogicOpCatalog.UNITS_HIDDEN);
            if (row.LastUnitsState != unitsState)
            {
                row.LastUnitsState = unitsState;
                SetLit(row.Unit, unitsState == LogicOpCatalog.UNITS_CHOICE, LogicPalette.LitValue);
            }
            StateTip(row.Unit, unitsState != LogicOpCatalog.UNITS_CHOICE ? "ZtxLogicPanel/TipUnitFixed" : cur.Units ? "ZtxLogicPanel/TipUnitUnits" : "ZtxLogicPanel/TipUnitPercent");
            string aTip = InputTip(b, cur.A), bTip = InputTip(b, cur.B);
            if (row.ATip.Text != aTip) row.ATip.Text = aTip;
            if (row.BTip.Text != bTip) row.BTip.Text = bTip;
            string bl = BLabelText(role, cur.Op);
            if (row.BLabel.Text != bl) row.BLabel.Text = bl;
            row.A.Button.SelfInputActive = commandable;
            row.Op.Button.SelfInputActive = commandable;
            row.B.Button.SelfInputActive = commandable;
            row.Value.Display.SelfInputActive = commandable;
            row.Unit.SelfInputActive = commandable && unitsState == LogicOpCatalog.UNITS_CHOICE;
            string word = cur.Op == LogicOp.None ? Strings.GetText("ZtxLogicPanel/RowOff")
                : Strings.GetText(_engine.LineResult(row.Line) ? "ZtxLogicPanel/SigOn" : "ZtxLogicPanel/SigOff");
            float watch = _engine.LineWatch(row.Line);
            if (!float.IsNaN(watch)) word += LogicRuleEval.IsCompare(cur.Op) ? " " + (cur.Units ? watch.ToString("0.#") : (int)watch + "%") : " " + (int)watch;
            if (row.State.Text != word) row.State.Text = word;
            SetLit(row.Op.Button, cur.Op != LogicOp.None, LogicPalette.LitValue);
            SetLit(row.A.Button, !cur.A.IsNone, LogicPalette.LitValue);
            SetLit(row.B.Button, !cur.B.IsNone, LogicPalette.LitValue);
            SetTextColour(row.State, cur.Op == LogicOp.None ? LogicPalette.Dim : _engine.LineResult(row.Line) ? LogicPalette.On : LogicPalette.Off);
        }

        private void Rebuild(List<Part> members)
        {
            _buildMembers = members;
            _queue.Clear();
            var st = new BuildState { Swap = _addRule != null || _blocks.Count > 0, Start = LogicCost.Now, StartFrame = _frame };
            if (!st.Swap) Reset();
            _build = st;
            PickerCell.Built = 0;
            TextCell.Built = 0;
            _queue.Enqueue("toolbar", () => BuildToolbar(st));
            _queue.Enqueue("memory", () => BuildMemoryBox(st));
            st.GroupOf = LogicGroups.HeaderOf(ExistingRules(), IsHeaderRule, InGroupRule);
            for (int r = 1; r <= _engine.RuleCount; r++)
            {
                if (!_engine.Rule(r).Exists || _engine.Rule(r).IsCondition) continue;
                int rr = r;
                _queue.Enqueue("rule " + rr, () =>
                {
                    RuleBlock block = _engine.Rule(rr).IsHeader ? BuildGroupBlock(rr, _queue.InsertNext) : BuildBlock(rr, _queue.InsertNext);
                    int header = st.GroupOf.TryGetValue(rr, out int hh) ? hh : 0;
                    if (block.IsGroup) st.Groups[rr] = block;
                    if (!block.IsGroup && header != 0 && st.Groups.TryGetValue(header, out RuleBlock group))
                    {
                        group.Box.AddChild(block.Box);
                        group.Members.Add(block);
                        block.Group = group;
                    }
                    else Place(st, block.Box);
                    if (st.Swap) st.Blocks.Add(block);
                    else _blocks.Add(block);
                });
            }
            _queue.Enqueue("finish", () => FinishBuild(st));
        }

        private void BuildToolbar(BuildState st)
        {
            Place(st, NoteWidget("ZtxLogicPanel/LegendShort", xml: true));
            LayoutBox bar = Row();
            st.AddRule = MakeButton("ZtxLogicPanel/AddRule", ButtonWidth + 20f, ButtonRole.Add);
            st.AddRule.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipAddRule");
            st.AddRule.Clicked += delegate { AddRule(atStart: true); };
            bar.DockedChildren.Add(st.AddRule);
            st.AddSignal = MakeButton("ZtxLogicPanel/AddSignal", ButtonWidth + 20f, ButtonRole.Misc);
            st.AddSignal.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipAddSignal");
            st.AddSignal.Clicked += delegate { AddSignal(atStart: true); };
            bar.DockedChildren.Add(st.AddSignal);
            Button help = LogicIcons.Make(LogicIcons.Help, "ZtxLogicPanel/Help", SmallButton);
            help.ToolTips.TextProvider = Strings.KeyString("ZtxConsoleHelp/TipButton");
            help.Clicked += delegate { ConsoleHelpWindow.Open(_game); };
            bar.DockedChildren.Add(help);
            st.Counter = new Label { DockMode = DockMode.Left, Width = CounterWidth };
            st.Counter.TextRenderer.OversizeMode = OversizeMode.Shrink;
            st.Counter.TextRenderer.HAlignment = HAlignment.Left;
            st.Counter.TextRenderer.Color = Color.Gray;
            bar.DockedChildren.Add(st.Counter);
            ExtraToolbar?.Invoke(bar);
            Place(st, bar);
            BuildOrganiseBar(st);
            if (!st.Swap)
            {
                _addRule = st.AddRule;
                _addGroup = st.AddGroup;
                _addSignal = st.AddSignal;
                _counter = st.Counter;
            }
        }

        private void Place(BuildState st, Widget w)
        {
            if (st.Swap) st.Widgets.Add(w);
            else Add(w);
        }

        private void FinishBuild(BuildState st)
        {
            LayoutBox bottom = Row();
            st.AddRuleBottom = MakeButton("ZtxLogicPanel/AddRule", ButtonWidth + 20f, ButtonRole.Add);
            st.AddRuleBottom.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipAddRuleBottom");
            st.AddRuleBottom.Clicked += delegate { AddRule(atStart: false); };
            bottom.DockedChildren.Add(st.AddRuleBottom);
            Place(st, bottom);
            if (st.Swap)
            {
                ClearPage();
                foreach (Widget w in st.Widgets) Add(w);
                _blocks.AddRange(st.Blocks);
                _memRows.Clear();
                _memRows.AddRange(st.MemRows);
                _addMem = st.AddMem;
            }
            _addRule = st.AddRule;
            _addRuleBottom = st.AddRuleBottom;
            _addGroup = st.AddGroup;
            _addSignal = st.AddSignal;
            _counter = st.Counter;
            if (_blocks.Count == 0) _widgets.Add(Note("ZtxLogicPanel/NoRules"));
            if (ReferenceEquals(_build, st)) _build = null;
            _forceFull = true;
            _rowLogAt = _frame + 5;
            Log.Info("console: rules tab built, " + _blocks.Count + " rules, " + _queue.Done + " pieces over " + (_frame - st.StartFrame + 1)
                + " frames, " + LogicCost.Ms(st.Work).ToString("0.0") + " ms work, worst frame " + LogicCost.Ms(st.WorstFrame).ToString("0.0") + " ms, slowest piece '" + _queue.SlowestLabel + "' "
                + LogicCost.Ms(_queue.SlowestStamps).ToString("0.0") + " ms (" + PickerCell.Built + " pickers, " + TextCell.Built + " text cells)"
                + (st.Swap ? ", swapped in" : "") + ".");
        }

        private RuleBlock BuildBlock(int r, Action<string, Action> sink)
        {
            var block = new RuleBlock { Rule = r, Box = Frame(), IsFunction = IsFunctionRule(r) };
            if (block.IsFunction) AttachBars(block);
            RuleBlock captured = block;

            block.Header = Row(InnerWidth);
            AddGrip(block.Header, () => StartRuleDrag(captured));
            block.Collapse = LogicIcons.Make(LogicIcons.Collapse, "ZtxLogicPanel/Collapse", SmallButton);
            block.Collapse.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipCollapse");
            block.Collapse.Clicked += delegate { ToggleCollapse(captured); };
            block.Header.DockedChildren.Add(block.Collapse);
            block.Number = new Label { DockMode = DockMode.Left, Width = NumberWidth };
            block.Number.TextRenderer.HAlignment = HAlignment.Right;
            ScaleText(block.Number, HeaderTextScale);
            block.Header.DockedChildren.Add(block.Number);
            block.KindTag = new Label { DockMode = DockMode.Left, Width = KindTagWidth };
            block.KindTag.TextRenderer.HAlignment = HAlignment.Center;
            block.KindTag.TextRenderer.OversizeMode = OversizeMode.Shrink;
            block.Header.DockedChildren.Add(block.KindTag);
            block.Name = new TextCell(_editor, NameWidth, "ZtxLogicPanel/TipName");
            ScaleText(block.Name.Display, HeaderTextScale);
            block.Name.Text = _engine.Rule(r).Name;
            block.Name.Changed += () => OnNameChanged(captured);
            block.Header.DockedChildren.Add(block.Name.Display);
            AddSwatch(block);
            block.Result = new Label { DockMode = DockMode.Left, Width = ResultWidth };
            block.Result.TextRenderer.HAlignment = HAlignment.Center;
            block.Result.TextRenderer.OversizeMode = OversizeMode.Shrink;
            block.Say = new SimpleTextProvider(Strings.GetText("ZtxLogicPanel/TipResult"), null);
            block.Result.ToolTips.TextProvider = block.Say;
            block.Header.DockedChildren.Add(block.Result);
            block.Toggle = MakeButton("ZtxLogicPanel/RuleEnabled", ToggleWidth);
            block.Toggle.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/ToggleRuleTip");
            block.Toggle.Clicked += delegate { ToggleRule(captured.Rule); };
            block.Header.DockedChildren.Add(block.Toggle);
            block.Share = MakeButton("ZtxLogicPanel/SharedOff", ShareWidth, ButtonRole.Misc);
            block.Share.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipShared");
            block.Share.Clicked += delegate { ToggleShared(captured.Rule); };
            block.Header.DockedChildren.Add(block.Share);
            Button copy = LogicIcons.Make(LogicIcons.Copy, "ZtxLogicPanel/CopyRule", SmallButton);
            copy.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/CopyRuleTip");
            copy.Clicked += delegate { CopyRule(captured.Rule); };
            block.Header.DockedChildren.Add(copy);
            if (block.IsFunction) { block.Share.SelfActive = false; copy.SelfActive = false; }
            block.Del = MakeButton("ZtxLogicPanel/DeleteRule", ButtonWidth, ButtonRole.Delete);
            block.Del.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipDeleteRule");
            block.Del.Clicked += delegate { OnDeleteClicked(captured.Rule); };
            block.Header.DockedChildren.Add(block.Del);
            block.Box.AddChild(block.Header);
            AddPalette(block);
            if (!IsCollapsed(r)) BuildBody(block, sink);
            sink("rule " + r + " fit", () => ApplyCollapse(block, IsCollapsed(block.Rule)));
            sink("rule " + r + " refresh", () =>
            {
                RefreshBlock(block, _buildMembers, _console?.Ship != null && _console.Ship.Commands.IsCommandableByLocalPlayer);
                block.Complete = true;
            });
            return block;
        }

        private static void RunNow(string label, Action piece) => piece();

        private void BuildBody(RuleBlock block, Action<string, Action> sink)
        {
            if (block.BodyBuilt) return;
            block.BodyBuilt = true;
            int r = block.Rule;
            RuleBlock captured = block;
            if (block.IsFunction && _engine.RuleLineCount(r) == 0)
            {
                sink("rule " + r + " warn", () => AddWarn(captured));
                BuildOutput(block, sink);
                return;
            }
            sink("rule " + r + " when", () =>
            {
                AddWarn(captured);
                AddBody(captured, Section("ZtxLogicPanel/SectionWhen"));
            });
            for (int k = 1; k <= _engine.LineCount; k++)
            {
                LogicLine line = _engine.Line(k);
                if (!line.IsUsed || line.Rule != r) continue;
                int kk = k;
                sink("line " + kk, () =>
                {
                    LineRow row = BuildLine(block, kk, line, block.Lines.Count + 1);
                    AddBody(block, row.Box);
                    block.Lines.Add(row);
                });
            }
            sink("rule " + r + " do", () => BuildBodyEnd(block, captured, sink));
        }

        private static void AddWarn(RuleBlock block)
        {
            block.Warn = new Label { Width = InnerWidth, Height = RowHeight };
            block.Warn.TextRenderer.XmlFormatting = true;
            block.Warn.TextRenderer.HAlignment = HAlignment.Left;
            block.Warn.TextRenderer.OversizeMode = OversizeMode.Shrink;
            AddBody(block, block.Warn);
            block.Hidden.Add(block.Warn);
        }

        private void BuildBodyEnd(RuleBlock block, RuleBlock captured, Action<string, Action> sink)
        {
            block.Expr = new Label { Width = InnerWidth, Height = RowHeight };
            block.Expr.TextRenderer.XmlFormatting = true;
            block.Expr.TextRenderer.HAlignment = HAlignment.Left;
            block.Expr.TextRenderer.OversizeMode = OversizeMode.Shrink;
            block.Expr.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipExpr");
            AddBody(block, block.Expr);
            LayoutBox addRow = Row(InnerWidth);
            addRow.DockedChildren.Add(new Widget { DockMode = DockMode.Left, Width = IndentWidth });
            block.AddLine = MakeButton("ZtxLogicPanel/AddLine", ButtonWidth, ButtonRole.Add);
            block.AddLine.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipAddLine");
            block.AddLine.Clicked += delegate { AddLine(captured.Rule); };
            addRow.DockedChildren.Add(block.AddLine);
            AddBody(block, addRow);

            if (block.IsFunction) BuildOutput(block, sink);
        }

        private LineRow BuildLine(RuleBlock block, int k, LogicLine line, int number, float width = -1f)
        {
            var row = new LineRow { Line = k, Rule = block.Rule, Number = number, Box = Row(width > 0f ? width : InnerWidth) };
            LineRow crow = row;
            AddGrip(row.Box, () => StartLineDrag(block, crow));
            row.Conn = MakeButton("ZtxLogicPanel/ConnAnd", ConnWidth);
            row.Conn.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipConn");
            row.Conn.Clicked += delegate { OnConnClicked(crow); };
            row.ConnCell = Hideable(row.Box, row.Conn, ConnWidth);
            var l = new Label { DockMode = DockMode.Left, Width = LWidth };
            l.TextRenderer.XmlFormatting = true;
            l.Text = LineNumberText(number);
            row.Say = new SimpleTextProvider(Strings.GetText("ZtxLogicPanel/TipLine"), null);
            l.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipLine");
            row.Num = l;
            row.Box.DockedChildren.Add(l);
            row.OpenB = MakeButton("ZtxLogicPanel/BracketNone", BracketWidth);
            row.OpenB.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipOpen");
            row.OpenB.Clicked += delegate { OnBracketClicked(crow, true); };
            row.OpenCell = Hideable(row.Box, row.OpenB, BracketWidth);
            row.A = MakeList(InWidth, "ZtxLogicPanel/TipA", v => OnLinePicked(crow, 0, v));
            row.Op = MakeList(OpWidth, "ZtxLogicPanel/TipOp", v => OnLinePicked(crow, 2, v), partHover: false);
            row.B = MakeList(InWidth, "ZtxLogicPanel/TipB", v => OnLinePicked(crow, 1, v));
            row.ATip = new SimpleTextProvider(Strings.GetText("ZtxLogicPanel/InputTipNone"), null);
            row.BTip = new SimpleTextProvider(Strings.GetText("ZtxLogicPanel/InputTipNone"), null);
            row.A.Button.ToolTips.TextProvider = row.ATip;
            row.B.Button.ToolTips.TextProvider = row.BTip;
            row.NotA = MakeButton("ZtxLogicPanel/NotOff", NotWidth);
            row.NotA.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipNotA");
            row.NotA.Clicked += delegate { OnNotClicked(crow, true); };
            row.NotACell = Hideable(row.Box, row.NotA, NotWidth);
            row.Box.DockedChildren.Add(row.A.Button);
            row.Box.DockedChildren.Add(row.Op.Button);
            row.BLabel = new Label { DockMode = DockMode.Left, Width = BLabelWidth };
            row.BLabel.TextRenderer.HAlignment = HAlignment.Center;
            row.BLabel.TextRenderer.Color = Color.Gray;
            row.BLabel.TextRenderer.OversizeMode = OversizeMode.Shrink;
            row.BLabelCell = Hideable(row.Box, row.BLabel, BLabelWidth);
            row.NotB = MakeButton("ZtxLogicPanel/NotOff", NotWidth);
            row.NotB.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipNotB");
            row.NotB.Clicked += delegate { OnNotClicked(crow, false); };
            row.NotBCell = Hideable(row.Box, row.NotB, NotWidth);
            row.BCell = Hideable(row.Box, row.B.Button, InWidth);
            row.Value = new TextCell(_editor, ParamWidth, "ZtxLogicPanel/TipParam");
            row.Value.Text = LogicParams.EditText(line.Op, line.Value);
            row.Value.Changed += () => OnValueChanged(crow);
            row.ValueCell = Hideable(row.Box, row.Value.Display, ParamWidth);
            row.Unit = new Button { DockMode = DockMode.Left, Width = LineUnitWidth, Text = LogicParams.Unit(line.Op, line.Units) };
            row.Unit.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipUnit");
            row.Unit.Clicked += delegate { OnUnitClicked(crow); };
            row.UnitCell = Hideable(row.Box, row.Unit, LineUnitWidth);
            row.CloseB = MakeButton("ZtxLogicPanel/BracketNone", BracketWidth);
            row.CloseB.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipClose");
            row.CloseB.Clicked += delegate { OnBracketClicked(crow, false); };
            row.CloseCell = Hideable(row.Box, row.CloseB, BracketWidth);
            row.State = new Label { DockMode = DockMode.Left, Width = StateWidth };
            row.State.TextRenderer.HAlignment = HAlignment.Center;
            row.State.TextRenderer.OversizeMode = OversizeMode.Shrink;
            row.State.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipState");
            row.Box.DockedChildren.Add(row.State);
            Button x = LogicIcons.Make(LogicIcons.Close, "ZtxLogicPanel/DeleteLine", SmallButton);
            x.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipDeleteLine");
            x.Clicked += delegate { DeleteLine(crow.Line); };
            row.Box.DockedChildren.Add(x);
            return row;
        }

        private static LayoutBox Frame()
        {
            var box = new LayoutBox();
            box.NineSlice.CopySettingsFrom(WidgetRules.Instance.HollowWidget.NineSlice);
            box.NineSlice.Borders = box.NineSlice.Borders * FrameBorderScale;
            box.Width = RowWidth + HorizontalInset(box.NineSlice);
            box.Children.LayoutAlgorithm = LayoutAlgorithms.TileTopLeftVertical;
            box.Children.BorderPadding = new Borders(BoxPad);
            box.Children.WidgetPadding = new Vector2(0f, GapY);
            box.Children.ClipChildren = false;
            return box;
        }

        private static void FitHeight(LayoutBox box)
        {
            float h = 0f;
            int n = 0;
            foreach (Widget w in box.Children)
            {
                if (!w.SelfActive) continue;
                h += w.Height;
                n++;
            }
            box.Height = h + Math.Max(0, n - 1) * GapY + 2f * BoxPad + VerticalInset(box.NineSlice);
        }

        private static float VerticalInset(WidgetNineSlice ns)
        {
            return Math.Max(0f, ns.Borders.Top - ns.Bleed.Top + ns.ProvidedRectPadding.Top)
                + Math.Max(0f, ns.Borders.Bottom - ns.Bleed.Bottom + ns.ProvidedRectPadding.Bottom);
        }

        private static float HorizontalInset(WidgetNineSlice ns)
        {
            return Math.Max(0f, ns.Borders.Left - ns.Bleed.Left + ns.ProvidedRectPadding.Left)
                + Math.Max(0f, ns.Borders.Right - ns.Bleed.Right + ns.ProvidedRectPadding.Right);
        }

        private static Label Section(string key)
        {
            var lbl = new Label { Width = InnerWidth, Height = SectionHeight };
            lbl.TextProvider = Strings.KeyString(key);
            lbl.TextRenderer.Color = Color.Gray;
            lbl.TextRenderer.HAlignment = HAlignment.Left;
            return lbl;
        }

        private static HideCell Hideable(LayoutBox row, Widget control, float width)
        {
            var spacer = new Widget { DockMode = DockMode.Left, Width = width };
            spacer.SelfActive = false;
            AddCell(row, control);
            AddCell(row, spacer);
            return new HideCell { Control = control, Spacer = spacer };
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LayoutBox, object> s_flowRows =
            new System.Runtime.CompilerServices.ConditionalWeakTable<LayoutBox, object>();

        private static LayoutBox FlowRow(float width)
        {
            var box = new LayoutBox();
            box.NineSlice.Flags = NineSliceFlags.None;
            box.Width = width;
            box.Height = RowHeight;
            box.Children.LayoutAlgorithm = LayoutAlgorithms.TileTopLeftHorizontalWrap;
            box.Children.WidgetPadding = new Vector2(RowGap, RowGap);
            box.Children.ClipChildren = false;
            box.DockedChildren.ClipChildren = false;
            s_flowRows.Add(box, null);
            return box;
        }

        private static void AddCell(LayoutBox row, Widget w)
        {
            if (s_flowRows.TryGetValue(row, out _))
            {
                w.Height = RowHeight;
                row.Children.Add(w);
            }
            else row.DockedChildren.Add(w);
        }

        private static bool FitFlow(LayoutBox box)
        {
            float inner = box.Width - box.Children.BorderPadding.Left - box.Children.BorderPadding.Right;
            float gapX = box.Children.WidgetPadding.X, gapY = box.Children.WidgetPadding.Y;
            float x = 0f;
            int lines = 1;
            bool any = false;
            foreach (Widget w in box.Children)
            {
                if (!w.SelfActive) continue;
                float cw = w.Width;
                if (any && x + gapX + cw > inner)
                {
                    lines++;
                    x = cw;
                }
                else x += (any ? gapX : 0f) + cw;
                any = true;
            }
            float h = lines * RowHeight + (lines - 1) * gapY;
            if (Math.Abs(box.Height - h) <= 0.5f) return false;
            box.Height = h;
            return true;
        }

        private static void AddBody(RuleBlock b, Widget w)
        {
            b.Box.AddChild(w);
            b.Body.Add(w);
        }

        private static void ApplyCollapse(RuleBlock b, bool collapsed)
        {
            foreach (Widget w in b.Body)
            {
                bool on = !collapsed && !b.Hidden.Contains(w);
                if (w.SelfActive != on) w.SelfActive = on;
            }
            LogicIcons.Set(b.Collapse, collapsed ? LogicIcons.Expand : LogicIcons.Collapse, collapsed ? "ZtxLogicPanel/Expand" : "ZtxLogicPanel/Collapse");
            StateTip(b.Collapse, collapsed ? "ZtxLogicPanel/TipCollapseFolded" : "ZtxLogicPanel/TipCollapseOpen");
            FitBlock(b);
        }

        private void ToggleCollapse(RuleBlock b)
        {
            bool on = !IsCollapsed(b.Rule);
            SetCollapsed(b.Rule, on);
            if (!on && !b.BodyBuilt) BuildBody(b, RunNow);
            ApplyCollapse(b, on);
            _forceFull = true;
        }

        private static string LineNumberText(int n) => LogicPalette.Wrap(LogicPalette.LineRef, Strings.FormatText("ZtxLogicPanel/LineNumFmt", n));

        private string InputTip(RuleBlock b, LogicRef r)
        {
            if (r.IsNone) return Strings.GetText("ZtxLogicPanel/InputTipNone");
            if (r.Kind == RefKind.On) return Strings.GetText("ZtxLogicPanel/InputTipOn");
            if (r.IsRule) return Strings.FormatText("ZtxLogicPanel/InputTipRuleFmt", RuleName(r.Index));
            if (r.IsLocal) return Strings.FormatText("ZtxLogicPanel/InputTipLineFmt", PositionOf(b, r.Index));
            if (r.IsShared) return Strings.FormatText("ZtxLogicPanel/InputTipSharedFmt", LogicShared.NameOf(r.Index) ?? "?");
            Part p = r.IsPart && _console != null ? LogicNet.Resolve(_console.Ship, _engine.Members, r) : null;
            if (p == null) return Strings.GetText("ZtxLogicPanel/InputTipGone");
            string watch = LogicPartWatch.Describe(p);
            string name = LogicNet.PartText(p, false);
            return watch.Length > 0 ? Strings.FormatText("ZtxLogicPanel/InputTipPartFmt", name, watch) : name;
        }

        private static string BLabelText(BRole role, LogicOp op)
        {
            if (role == BRole.Than && op == LogicOp.Equal) return Strings.GetText("ZtxLogicPanel/BLabelTo");
            switch (role)
            {
                case BRole.Input: return Strings.GetText("ZtxLogicPanel/BLabelB");
                case BRole.Reset: return Strings.GetText("ZtxLogicPanel/BLabelReset");
                case BRole.Than: return Strings.GetText("ZtxLogicPanel/BLabelThan");
                case BRole.Station: return Strings.GetText("ZtxLogicPanel/BLabelAt");
                default: return "";
            }
        }

        private void Add(Widget w)
        {
            _page.AddChild(w);
            _widgets.Add(w);
        }

        private void LogRowWidths()
        {
            try
            {
                foreach (RuleBlock b in _blocks)
                {
                    LayoutBox row = b.Lines.Count > 0 ? b.Lines[0].Box : b.Conds.Find(c => c.Lines.Count > 0)?.Lines[0].Box;
                    if (row == null) continue;
                    float declared = 0f, screen = 0f, widestOver = 0f;
                    int n = 0;
                    string widest = "";
                    foreach (Widget w in row.DockedChildren)
                    {
                        if (!w.SelfActive) continue;
                        n++;
                        declared += w.Width;
                        float sw = w.ScreenRect.Size.X;
                        screen += sw;
                        if (sw - w.Width > widestOver) { widestOver = sw - w.Width; widest = w.GetType().Name; }
                    }
                    float gaps = Math.Max(0, n - 1) * RowGap;
                    Log.Info("console: line row at " + LogicUiScale.Percent(LogicUiScale.Area.Rules) + " %: row " + row.Width.ToString("0")
                        + " (screen " + row.ScreenRect.Size.X.ToString("0") + "), " + n + " parts declare " + declared.ToString("0")
                        + " + gaps " + gaps.ToString("0") + ", take " + screen.ToString("0") + " on screen"
                        + (widestOver > 0.5f ? "; most over its width: " + widest + " +" + widestOver.ToString("0") : "") + ".");
                    return;
                }
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.LogRowWidths", ex); }
        }

        private static LayoutBox Row(float width = -1f)
        {
            if (width < 0f) width = RowWidth;
            var box = new LayoutBox();
            box.NineSlice.Flags = NineSliceFlags.None;
            box.Width = width;
            box.Height = RowHeight;
            box.Children.ClipChildren = false;
            box.DockedChildren.ClipChildren = false;
            box.DockedChildren.WidgetPadding = new Vector2(RowGap, 0f);
            return box;
        }

        private Label Note(string key, bool xml = false)
        {
            Label lbl = NoteWidget(key, xml);
            _page.AddChild(lbl);
            return lbl;
        }

        private static Label NoteWidget(string key, bool xml = false)
        {
            var lbl = new Label { Width = RowWidth, Height = xml ? 44f : RowHeight };
            if (xml)
            {
                lbl.TextRenderer.XmlFormatting = true;
                lbl.TextRenderer.OversizeMode = OversizeMode.Wrap;
                lbl.Text = Strings.GetText(key);
            }
            else
            {
                lbl.TextProvider = Strings.KeyString(key);
                lbl.TextRenderer.Color = Color.Gray;
            }
            lbl.TextRenderer.HAlignment = HAlignment.Left;
            return lbl;
        }

        private static Button MakeButton(string key, float width)
        {
            var b = new Button { DockMode = DockMode.Left, Width = width };
            b.TextProvider = Strings.KeyString(key);
            return b;
        }

        private PickerCell MakeList(float width, string tipKey, Action<int> picked, bool partHover = true)
        {
            var cell = new PickerCell(_picker, width, tipKey) { Picked = picked };
            if (partHover) cell.PartOf = PartOf;
            return cell;
        }

        public static string WidgetCounts => "rules tab since build: " + PickerCell.Built + " pickers, " + TextCell.Built + " text cells";

        private static void Fill(PickerCell cell, List<(int Value, string Text)> items) => cell.SetItems(items);

        private Part PartOf(int packed)
        {
            try
            {
                LogicRef r = LogicRef.Unpack(packed);
                return r.IsPart && _console != null ? LogicNet.Resolve(_console.Ship, _engine?.Members, r) : null;
            }
            catch { return null; }
        }

        private static void ClearHover(Part p)
        {
            if (p != null && ReferenceEquals(LogicConsoleWindow.HoveredPart, p)) LogicConsoleWindow.HoveredPart = null;
        }

        private static void Select(PickerCell cell, int value) => cell.Show(value);

        private static string SignatureOf(List<(int Value, string Text)> items) => PickerCell.SignatureOf(items);

        public static string OpText(LogicOp op) => Strings.GetText("ZtxLogicPanel/Op_" + op);
        public static string OpText(InputClass c, LogicOp op) => Strings.GetText("ZtxLogicPanel/Op_" + LogicOpCatalog.LabelKey(c, op));
        private static string OpTipKey(InputClass c, LogicOp op) => op == LogicOp.None ? "ZtxLogicPanel/TipOp" : "PartToggles/ZtxGateOp_" + LogicOpCatalog.LabelKey(c, op);
        private string RuleText(int r)
        {
            LogicRuleDef d = _engine.Rule(r);
            string name = string.IsNullOrEmpty(d.Name) ? Strings.FormatText("ZtxLogicPanel/RuleUnnamedFmt", r) : d.Name;
            return LogicPalette.Wrap(LogicPalette.RuleRef, name);
        }
        private static string LocalText(int k) => LogicPalette.Wrap(LogicPalette.LineRef, Strings.FormatText("ZtxLogicPanel/LocalFmt", k));
        private static string Stale(LogicRef r) => r.IsShared ? LogicPalette.Wrap(LogicPalette.Neutral, SharedMissing(r.Index)) : LogicPalette.Wrap(LogicPalette.Neutral, r + " ?");

        internal static string SharedText(int hash) => LogicPalette.Wrap(LogicPalette.RuleRef, Strings.FormatText("ZtxLogicPanel/SharedFmt", LogicShared.NameOf(hash) ?? "?"));

        internal static string SharedMissing(int hash)
        {
            string name = LogicShared.NameOf(hash);
            return name != null ? Strings.FormatText("ZtxLogicPanel/SharedMissingFmt", name) : Strings.GetText("ZtxLogicPanel/SharedMissing");
        }

        private InputClass ClassOf(LogicRef r)
        {
            if (!r.IsPart) return IsMemCounterRef(r) ? InputClass.Value : InputClass.Bool;
            Part p = _console != null ? LogicNet.Resolve(_console.Ship, _engine?.Members, r) : null;
            if (p == null) return InputClass.Unknown;
            if (LogicNet.KindOf(p) == LogicKind.Actuator)
            {
                int kind = LogicAction.KindOf(p);
                if (kind == LogicAction.KIND_BAY_STATION) return InputClass.Bool;
                if (kind == LogicAction.KIND_SWITCH) return InputClass.Bool;
                if (ZtxRailTerminalComponent.ForPart(p) != null) return InputClass.Terminal;
                if (kind == LogicAction.KIND_ADAPTER) return InputClass.Value;
                if (kind == LogicAction.KIND_LAMP) return InputClass.Bool;
                if (kind == LogicAction.KIND_CART) return InputClass.Cart;
                if (kind == LogicAction.KIND_DOCK || kind == LogicAction.KIND_BAY) return InputClass.Dock;
                return LogicAction.IsPositionedKind(kind) ? InputClass.ActuatorPos : InputClass.Actuator;
            }
            return LogicNet.IsValueSource(p) ? InputClass.Value : InputClass.Bool;
        }

        private static List<(int, string)> OpItems(InputClass c, LogicOp keep)
        {
            var l = new List<(int, string)> { (0, Strings.GetText("ZtxLogicPanel/OpOff")) };
            foreach (LogicOp op in LogicOpCatalog.Allowed(c)) l.Add(((int)op, OpText(c, op)));
            if (!LogicOpCatalog.IsAllowed(c, keep)) l.Add(((int)keep, LogicPalette.Wrap(LogicPalette.Neutral, OpText(keep))));
            return l;
        }

        private void FillOps(PickerCell cell, List<(int Value, string Text)> items, InputClass c)
        {
            cell.TipKey = v => LogicOpCatalog.IsAllowed(c, (LogicOp)v) ? OpTipKey(c, (LogicOp)v) : "ZtxLogicPanel/TipOpNotOffered";
            Fill(cell, items);
        }

        private List<(int, string)> BItems(RuleBlock b, LineRow row, List<Part> members, LogicLine line)
        {
            BRole role = LogicOpCatalog.RoleOfB(line.Op);
            if (role == BRole.Station)
            {
                Part cart = line.A.IsPart && _console != null ? LogicNet.Resolve(_console.Ship, _engine.Members, line.A) : null;
                return StationItems(cart, line.B, "ZtxLogicPanel/InNone");
            }
            if (role != BRole.Than) return InputItems(b, row, members, line.B, actuators: false);
            var l = new List<(int, string)> { (0, Strings.GetText("ZtxLogicPanel/InTyped")) };
            bool kept = line.B.IsNone;
            foreach (Part p in members)
            {
                bool sensor = (LogicNet.KindOf(p) == LogicKind.Sensor || LogicAction.KindOf(p) == LogicAction.KIND_ADAPTER) && LogicNet.IsValueSource(p);
                bool positioned = LogicNet.KindOf(p) == LogicKind.Actuator && LogicAction.IsPositioned(p);
                if (!sensor && !positioned) continue;
                LogicRef r = LogicRef.Part(p);
                if (r.Equals(line.B)) kept = true;
                l.Add((r.Pack(), LogicNet.PartText(p, true)));
            }
            if (!kept) l.Add((line.B.Pack(), Stale(line.B)));
            return l;
        }

        private static string RawText(int kind, int raw)
        {
            return kind == 2 ? (raw / 10f).ToString("0.#", CultureInfo.InvariantCulture) : raw.ToString(CultureInfo.InvariantCulture);
        }

        private static bool TryParseRaw(int kind, string text, int max, out int raw)
        {
            raw = 0;
            string t = (text ?? "").Trim().Replace(',', '.');
            if (t.Length == 0 || !float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return false;
            raw = Math.Max(0, Math.Min(max, (int)Math.Round(kind == 2 ? f * 10f : f)));
            return true;
        }

        private static int MaxPoseRaw(Part target, int kind)
        {
            return kind == 2 ? (int)Math.Round(LogicActuatorState.MaxPose(target) * 10f) : 180;
        }

        private void ShowValue(LineRow row, LogicLine l)
        {
            string text = LogicParams.EditText(l.Op, l.Value);
            if (row.Value.Text != text && !_editor.IsEditing(row.Value))
            {
                _refreshing = true;
                try { row.Value.Text = text; }
                finally { _refreshing = false; }
            }
            string unit = LogicParams.Unit(l.Op, l.Units);
            if (row.Unit.Text != unit) row.Unit.Text = unit;
        }

        internal static bool IsDoTargetId(string id)
        {
            if (id == "ztx.hinges_pistons_docking.rail_terminal" || id == "ztx.hinges_pistons_docking.hinge_base") return false;
            return LogicNet.KindOfId(id) == LogicKind.Actuator || LogicAction.KindOfId(id) == LogicAction.KIND_BUTTON;
        }

        private static List<(int, string)> TargetItems(List<Part> members, LogicRef keep, bool forSteps = false)
        {
            var l = new List<(int, string)> { (0, Strings.GetText("ZtxLogicPanel/InNone")) };
            bool kept = !keep.IsPart;
            foreach (Part p in members)
            {
                if (!IsDoTargetId(p.Rules.ID.ToString())) continue;
                if (forSteps && (LogicNet.KindOf(p) != LogicKind.Actuator || LogicNet.ReadsLogicOut(p))) continue;
                LogicRef r = LogicRef.Part(p);
                if (r.Equals(keep)) kept = true;
                l.Add((r.Pack(), LogicNet.PartText(p, true)));
            }
            if (!kept) l.Add((keep.Pack(), Stale(keep)));
            return l;
        }

        private List<(int, string)> InputItems(RuleBlock b, LineRow row, List<Part> members, LogicRef keep, bool actuators = true)
        {
            var l = new List<(int, string)>
            {
                (H_SIGNALS, Heading("ZtxLogicPanel/GroupSignals")),
                (0, Strings.GetText("ZtxLogicPanel/InNone")),
                (LogicRef.On.Pack(), Strings.GetText("ZtxLogicPanel/InOn")),
            };
            bool kept = keep.IsNone || keep.Kind == RefKind.On;
            bool head = false;
            for (int r = 1; r <= _engine.RuleCount; r++)
            {
                if (r == b.Rule || !IsReadableRule(r)) continue;
                if (!head) { l.Add((H_RULES, Heading("ZtxLogicPanel/GroupRules"))); head = true; }
                LogicRef rr = LogicRef.Rule(r);
                if (rr.Equals(keep)) kept = true;
                l.Add((rr.Pack(), RuleText(r)));
            }
            head = false;
            var own = new HashSet<int>();
            for (int r = 1; r <= _engine.RuleCount; r++)
                if (_engine.Rule(r).Exists && _engine.Rule(r).Shared) own.Add(LogicShared.Hash(_engine.Rule(r).Name));
            foreach (var (hash, name) in LogicShared.Names(_console?.Ship))
            {
                if (own.Contains(hash)) continue;
                if (!head) { l.Add((H_SHARED, Heading("ZtxLogicPanel/GroupShared"))); head = true; }
                LogicRef sr = LogicRef.Shared(hash);
                if (sr.Equals(keep)) kept = true;
                l.Add((sr.Pack(), SharedText(hash)));
            }
            head = false;
            for (int m = 1; m <= _engine.RuleCount; m++)
            {
                if (!IsMemoryRule(m)) continue;
                if (!head) { l.Add((H_MEMORY, Heading("ZtxLogicPanel/GroupMemory"))); head = true; }
                LogicRef mr = LogicRef.Rule(m);
                if (mr.Equals(keep)) kept = true;
                l.Add((mr.Pack(), RuleText(m)));
            }
            head = false;
            if (b.IsCondition)
                foreach (int v in ConditionsOf(OwnerFunction(b.Rule)))
                {
                    if (v == b.Rule) continue;
                    if (!head) { l.Add((H_VARS, Heading("ZtxLogicPanel/GroupVariables"))); head = true; }
                    LogicRef vr = LogicRef.Rule(v);
                    if (vr.Equals(keep)) kept = true;
                    l.Add((vr.Pack(), RuleText(v)));
                }
            head = false;
            foreach (LineRow other in b.Lines)
            {
                if (other.Number >= row.Number) continue;
                if (!head) { l.Add((H_LINES, Heading("ZtxLogicPanel/GroupLines"))); head = true; }
                LogicRef loc = LogicRef.Local(other.Line);
                if (loc.Equals(keep)) kept = true;
                l.Add((loc.Pack(), LocalText(other.Number)));
            }
            AddPartGroup(l, members, LogicKind.Button, H_BUTTONS, "ZtxLogicPanel/GroupButtons", keep, ref kept);
            AddPartGroup(l, members, LogicKind.Sensor, H_SENSORS, "ZtxLogicPanel/GroupSensors", keep, ref kept);
            if (actuators) AddPartGroup(l, members, LogicKind.Actuator, H_ACTUATORS, "ZtxLogicPanel/GroupActuators", keep, ref kept);
            if (!kept)
            {
                int pos = keep.IsLocal ? PositionOf(b, keep.Index) : 0;
                l.Add((keep.Pack(), pos > 0
                    ? LogicPalette.Wrap(LogicPalette.Warn, Strings.FormatText("ZtxLogicPanel/LastTickFmt", Strings.FormatText("ZtxLogicPanel/LocalFmt", pos)))
                    : keep.IsRule && keep.Index <= _engine.RuleCount && _engine.Rule(keep.Index).Exists ? RuleText(keep.Index) : Stale(keep)));
            }
            return l;
        }

        private static void AddPartGroup(List<(int, string)> l, List<Part> members, LogicKind kind, int heading, string key, LogicRef keep, ref bool kept)
        {
            bool head = false;
            foreach (Part p in members)
            {
                if (LogicNet.KindOf(p) != kind) continue;
                if (!head) { l.Add((heading, Heading(key))); head = true; }
                LogicRef r = LogicRef.Part(p);
                if (r.Equals(keep)) kept = true;
                l.Add((r.Pack(), LogicNet.PartText(p, true)));
            }
        }

        private static string Heading(string key) => LogicPalette.Wrap(LogicPalette.Heading, Strings.GetText(key));

        private void Write(params (ID<PartToggleGuiRules> Id, int Value)[] writes)
        {
            _forceFull = true;
            if (_console == null) return;
            var list = new List<(Part, ID<PartToggleGuiRules>, int)>(writes.Length);
            foreach (var (id, value) in writes) list.Add((_console, id, value));
            WriteParts(list);
        }

        private int _writtenFrom = int.MinValue;

        private static int StoreVersionOf(Part console) =>
            console?.Ship?.Metadata.PartUIToggleStates.Get((new PartSaveData(console), LogicStore.VersionId)) ?? 0;

        private bool AwaitingDecode(Part console)
        {
            if (_engine == null || console?.Ship == null || !_engine.IsStoreV2) return false;
            int now = StoreVersionOf(console);
            if (_writtenFrom != int.MinValue && now != _writtenFrom) _writtenFrom = int.MinValue;
            return _writtenFrom != int.MinValue || now != _engine.StoreVersion;
        }

        private void WriteParts(List<(Part Part, ID<PartToggleGuiRules> Id, int Value)> writes)
        {
            if (writes == null || writes.Count == 0) return;
            if (_console?.Ship == null || !_console.Ship.Commands.IsCommandableByLocalPlayer) return;
            if (writes.Exists(w => w.Part == _console && w.Id.ToString().StartsWith(LogicStore.PREFIX, StringComparison.Ordinal))
                && AwaitingDecode(_console))
                return;
            _forceFull = true;
            var input = new MPSetUITogglesInput();
            Dictionary<Part, List<MPInput>> store = null;
            foreach (var (part, id, value) in writes)
            {
                if (part == null) continue;
                if (id.ToString().StartsWith(LogicStore.PREFIX, StringComparison.Ordinal))
                {
                    if (part.Ship == null) continue;
                    if (store == null) store = new Dictionary<Part, List<MPInput>>();
                    if (!store.TryGetValue(part, out List<MPInput> batch)) store[part] = batch = new List<MPInput>();
                    batch.Add(new MPSetUIToggleMetadataInput(part.Ship, new PartSaveData(part), id, value, false));
                    continue;
                }
                PartUIToggle t = PartUIToggle.GetOnPart(part, id);
                if (t != null) input.ToggleValues.Add((t, value));
            }
            if (!input.IsEmpty) _game.NetManager.EnqueueInput(input);
            if (store == null) return;
            foreach (KeyValuePair<Part, List<MPInput>> kv in store)
            {
                if (kv.Key == _console) _writtenFrom = StoreVersionOf(_console);
                kv.Value.Add(new MPSetUIToggleMetadataInput(kv.Key.Ship, new PartSaveData(kv.Key), LogicStore.VersionId, NextStoreVersion(), false));
                _game.NetManager.EnqueueInput(new MPMultiInput(kv.Value));
            }
        }

        private static int s_storeSeq;

        private static int NextStoreVersion()
        {
            int v = unchecked(Environment.TickCount * 31 + ++s_storeSeq);
            return v == 0 ? 1 : v;
        }

        private void OwnLine(List<(ID<PartToggleGuiRules>, int)> writes, int k, int rule)
        {
            if (_engine.IsStoreV2) writes.Add((_engine.LineOwnerSlot(k), rule));
        }

        private void OwnAct(List<(ID<PartToggleGuiRules>, int)> writes, int k, int rule)
        {
            if (_engine.IsStoreV2) writes.Add((_engine.ActionOwnerSlot(k), rule));
        }

        private static bool HasStoreVersion(Part console)
        {
            int? ver = console.Ship.Metadata.PartUIToggleStates.Get((new PartSaveData(console), LogicStore.VersionId));
            return ver.HasValue && ver.Value != 0;
        }

        private static bool AllEnginesDecoded(Cosmoteer.Ships.Ship ship)
        {
            LogicShipManager manager = LogicShipManager.TryGet(ship);
            if (manager == null) return false;
            foreach (ZtxRuleEngineComponent e in manager.Engines()) if (!e.HasDecoded) return false;
            return true;
        }

        private void ConvertToStore(Part console)
        {
            try
            {
                List<(string Name, int Value)> w = LogicStore.Convert(_engine.LineCount, _engine.RuleCount, _engine.ActionCount,
                    (k, f) => _engine.RawLine(k, f), k => _engine.Line(k).Rule,
                    (r, f) => _engine.RawRule(r, f),
                    (k, f) => _engine.RawAction(k, f), k => _engine.Action(k).Rule,
                    out int rc, out int lc, out int ac);
                var writes = new List<(Part, ID<PartToggleGuiRules>, int)>(w.Count + 2);
                foreach (var (name, value) in w) writes.Add((console, new ID<PartToggleGuiRules>(name), value));
                if (console.Ship.Metadata.PartUIToggleStates.Get((new PartSaveData(console), LogicStore.ProbeId)).HasValue)
                    writes.Add((console, LogicStore.ProbeId, 0));
                if (writes.Count == 0) writes.Add((console, new ID<PartToggleGuiRules>(LogicStore.RuleName(1, 0)), 0));
                WriteParts(writes);
                Log.Info("console " + console.Location + ": converted to storage v2 (" + rc + " rules, " + lc + " lines, " + ac + " actuators); the old slots stay as a backup.");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.ConvertToStore", ex); }
        }

        private void AddRule(bool atStart) => AddFunction(atStart);

        private int InsertRule(LogicRuleDef def, bool atStart, bool withLine, Action<List<(ID<PartToggleGuiRules>, int)>, int> extra = null)
        {
            int r = _engine.FreeRule();
            if (r == 0) return 0;
            var raw = new int[_engine.RuleFieldCount];
            raw[ZtxRuleEngineComponent.RULE_M] = def.PackMisc();
            raw[ZtxRuleEngineComponent.RULE_T] = 0;
            raw[ZtxRuleEngineComponent.RULE_N1] = LogicRuleDef.PackName(def.Name, 0);
            raw[ZtxRuleEngineComponent.RULE_N2] = LogicRuleDef.PackName(def.Name, 5);
            List<int> order = ExistingRules();
            var current = new List<int>(order) { r };
            var desired = atStart ? LogicRuleOrder.MovedTo(current, r, 0) : LogicRuleOrder.MovedTo(current, r, current.Count);
            Dictionary<int, int> map = _engine.IsStoreV2 ? LogicRuleOrder.Remap(current, desired) : new Dictionary<int, int>();
            int home = map.TryGetValue(r, out int placed) ? placed : r;
            var writes = new List<(ID<PartToggleGuiRules>, int)>();
            AppendRuleRemap(writes, map, r, raw);
            if (!map.ContainsKey(r))
                for (int f = 0; f < raw.Length; f++) writes.Add((_engine.RuleSlot(r, f), raw[f]));
            int k = withLine ? _engine.FreeLine() : 0;
            if (k != 0)
            {
                var line = new LogicLine { Rule = home, Op = LogicOp.Pass, Format = LogicLine.FORMAT };
                writes.Add((_engine.LineSlot(k, ZtxRuleEngineComponent.LINE_M), line.PackMisc()));
                writes.Add((_engine.LineSlot(k, ZtxRuleEngineComponent.LINE_A), 0));
                writes.Add((_engine.LineSlot(k, ZtxRuleEngineComponent.LINE_B), 0));
                OwnLine(writes, k, home);
            }
            extra?.Invoke(writes, home);
            Write(writes.ToArray());
            RemapCollapsed(map);
            return home;
        }

        private List<int> ExistingRules()
        {
            var order = new List<int>();
            for (int s = 1; s <= _engine.RuleCount; s++) if (_engine.Rule(s).Exists && !_engine.Rule(s).IsCondition) order.Add(s);
            return order;
        }

        private void AppendRuleRemap(List<(ID<PartToggleGuiRules>, int)> writes, Dictionary<int, int> map, int newRule = 0, int[] newRaw = null)
        {
            foreach (KeyValuePair<int, int> kv in map)
            {
                if (kv.Key == kv.Value) continue;
                for (int f = 0; f < _engine.RuleFieldCount; f++)
                    writes.Add((_engine.RuleSlot(kv.Value, f), kv.Key == newRule && newRaw != null ? newRaw[f] : _engine.RawRule(kv.Key, f)));
            }
            if (newRule != 0 && newRaw != null && map.TryGetValue(newRule, out int at) && at == newRule)
                for (int f = 0; f < newRaw.Length; f++) writes.Add((_engine.RuleSlot(newRule, f), newRaw[f]));
            int Owner(int rule) => map.TryGetValue(rule, out int to) ? to : rule;
            LogicRef Ref(LogicRef x) => x.IsRule && map.TryGetValue(x.Index, out int to) ? LogicRef.Rule(to) : x;
            for (int k = 1; k <= _engine.LineCount; k++)
            {
                LogicLine l = _engine.Line(k);
                if (!l.IsUsed) continue;
                int owner = Owner(l.Rule);
                LogicRef a = Ref(l.A), bb = Ref(l.B);
                if (owner == l.Rule && a.Equals(l.A) && bb.Equals(l.B)) continue;
                l.Rule = owner;
                l.A = a;
                l.B = bb;
                writes.Add((_engine.LineSlot(k, ZtxRuleEngineComponent.LINE_A), l.PackA()));
                writes.Add((_engine.LineSlot(k, ZtxRuleEngineComponent.LINE_B), l.PackB()));
                writes.Add((_engine.LineSlot(k, ZtxRuleEngineComponent.LINE_M), l.PackMisc()));
                OwnLine(writes, k, owner);
            }
            for (int k = 1; k <= _engine.ActionCount; k++)
            {
                LogicAction act = _engine.Action(k);
                if (!act.IsUsed || Owner(act.Rule) == act.Rule) continue;
                act.Rule = Owner(act.Rule);
                writes.Add((_engine.ActionSlot(k, ZtxRuleEngineComponent.ACT_M), act.PackMisc()));
                OwnAct(writes, k, act.Rule);
            }
            for (int q = 1; q <= _engine.SeqCount; q++)
            {
                SeqDef d = _engine.Seq(q);
                if (d.IsUsed && Owner(d.Rule) != d.Rule) writes.Add((_engine.SeqSlot(q, LogicStore.SEQ_R), Owner(d.Rule)));
            }
            for (int c = 1; c <= _engine.RuleCount; c++)
            {
                LogicRuleDef cd = _engine.Rule(c);
                if (!cd.Exists || !cd.IsCondition || map.ContainsKey(c)) continue;
                LogicRef link = Ref(cd.Target);
                if (!link.Equals(cd.Target)) writes.Add((_engine.RuleSlot(c, ZtxRuleEngineComponent.RULE_T), link.Pack()));
            }
            for (int sk = 1; sk <= _engine.StepCount; sk++)
            {
                SeqStep st = _engine.Step(sk);
                if (!st.IsUsed || !(st.Kind == SeqKind.WaitUntil || st.Kind == SeqKind.If || st.Kind == SeqKind.While || st.Kind == SeqKind.Call)) continue;
                LogicRef target = Ref(st.Target);
                if (!target.Equals(st.Target)) writes.Add((_engine.StepSlot(sk, ZtxRuleEngineComponent.STEP_T), target.Pack()));
            }
        }

        private void RemapCollapsed(Dictionary<int, int> map)
        {
            var was = new Dictionary<int, bool>();
            foreach (KeyValuePair<int, int> kv in map) was[kv.Key] = IsCollapsed(kv.Key);
            foreach (KeyValuePair<int, int> kv in map) SetCollapsed(kv.Value, was[kv.Key]);
        }

        private void OnDeleteClicked(int r)
        {
            _forceFull = true;
            if (_confirmRule == r && Environment.TickCount - _confirmUntil < 0)
            {
                _confirmRule = 0;
                DeleteRule(r);
                return;
            }
            _confirmRule = r;
            _confirmUntil = Environment.TickCount + DELETE_CONFIRM_MS;
        }

        private string CounterText()
        {
            int rules = 0, lines = 0, acts = 0;
            for (int r = 1; r <= _engine.RuleCount; r++) if (_engine.Rule(r).Exists && !_engine.Rule(r).IsHeader && !_engine.Rule(r).IsCondition) rules++;
            for (int k = 1; k <= _engine.LineCount; k++) if (_engine.Line(k).IsUsed) lines++;
            for (int k = 1; k <= _engine.ActionCount; k++) if (_engine.Action(k).IsUsed) acts++;
            if (_engine.IsStoreV2) return Strings.FormatText("ZtxLogicPanel/CounterV2Fmt", rules, lines, acts);
            return Strings.FormatText("ZtxLogicPanel/CounterFmt", rules, _engine.RuleCount, lines, _engine.LineCount, acts, _engine.ActionCount);
        }

        private void OnNotClicked(LineRow row, bool a)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                LogicLine l = row.Current;
                if (!l.IsUsed) return;
                if (a) l.NotA = !l.NotA;
                else l.NotB = !l.NotB;
                int raw = a ? l.PackA() : l.PackB();
                row.LastRaw[a ? 0 : 1] = raw;
                row.Current = l;
                Write((_engine.LineSlot(row.Line, a ? ZtxRuleEngineComponent.LINE_A : ZtxRuleEngineComponent.LINE_B), raw));
                Log.Info("console: line " + row.Line + " -> " + l + ".");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnNotClicked", ex); }
        }

        private void RefreshWarnings(RuleBlock b, LogicRuleDef def)
        {
            var w = new List<string>();
            foreach (LineRow row in b.Lines)
            {
                LogicLine l = row.Current;
                if (!l.IsUsed || l.Op == LogicOp.None) continue;
                if (l.A.IsNone) w.Add(Strings.FormatText("ZtxLogicPanel/WarnNoInputFmt", row.Number));
                bool usesB = LogicOpCatalog.RoleOfB(l.Op) != BRole.None;
                if (ReadsBelow(b, row, l.A) || (usesB && ReadsBelow(b, row, l.B))) w.Add(Strings.FormatText("ZtxLogicPanel/WarnLaterFmt", row.Number));
                if (!LogicOpCatalog.IsAllowed(ClassOf(l.A), l.Op)) w.Add(Strings.FormatText("ZtxLogicPanel/WarnOpFmt", row.Number, OpText(l.Op)));
            }
            if (def.Shared && def.Name.Length > 0)
            {
                int sharers = LogicShared.Publishers(_console?.Ship, LogicShared.Hash(def.Name));
                if (sharers > 1) w.Add(Strings.FormatText("ZtxLogicPanel/WarnSharedTwiceFmt", def.Name, sharers));
            }
            for (int k = 0; k < b.Lines.Count; k++)
            {
                LogicLine sl = b.Lines[k].Current;
                foreach (LogicRef sref in new[] { sl.A, sl.B })
                    if (sl.IsUsed && sref.IsShared && LogicShared.Read(_console?.Ship, sref.Index) == null)
                        w.Add(Strings.FormatText("ZtxLogicPanel/WarnSharedGoneFmt", b.Lines[k].Number, SharedMissing(sref.Index)));
            }
            int driven = def.Target.IsPart ? 1 : 0;
            foreach (ActRow a in b.Acts)
            {
                LogicAction act = a.Current;
                if (!act.Target.IsPart) continue;
                driven++;
                Part p = LogicNet.Resolve(_console.Ship, _engine.Members, act.Target);
                string name = p != null ? LogicNet.PartText(p, false) : act.Target.ToString();
                if (p == null || !_engine.Members.Contains(p))
                {
                    w.Add(Strings.FormatText("ZtxLogicPanel/WarnNotWiredFmt", name));
                    continue;
                }
                string other = OtherDriver(b.Rule, act.Target);
                if (other != null) w.Add(Strings.FormatText("ZtxLogicPanel/WarnSharedFmt", name, other));
            }
            string text = w.Count > 0 ? LogicPalette.Wrap(LogicPalette.Warn, string.Join("  |  ", w)) : "";
            if (driven == 0 && b.Lines.Count > 0 && !b.IsCondition)
                text += (text.Length > 0 ? "  |  " : "") + LogicPalette.Wrap(LogicPalette.Neutral, Strings.FormatText("ZtxLogicPanel/NoteSignalFmt", RuleName(b.Rule)));
            if (b.Warn.Text != text) b.Warn.Text = text;
            bool show = text.Length > 0;
            if (show == b.WarnShown) return;
            b.WarnShown = show;
            if (show) b.Hidden.Remove(b.Warn);
            else b.Hidden.Add(b.Warn);
            bool active = show && !IsCollapsed(b.Rule);
            if (b.Warn.SelfActive != active) b.Warn.SelfActive = active;
            RefitBlock(b);
        }

        private static bool ReadsBelow(RuleBlock b, LineRow row, LogicRef r)
        {
            if (!r.IsLocal) return false;
            int pos = PositionOf(b, r.Index);
            return pos > row.Number;
        }

        private string OtherDriver(int rule, LogicRef target)
        {
            int packed = target.Pack();
            for (int r = 1; r <= _engine.RuleCount; r++)
            {
                if (r == rule || !_engine.Rule(r).Exists) continue;
                if (_engine.Rule(r).Target.Pack() == packed) return RuleName(r);
            }
            for (int k = 1; k <= _engine.ActionCount; k++)
            {
                LogicAction a = _engine.Action(k);
                if (a.IsUsed && a.Rule != rule && a.Target.Pack() == packed && _engine.Rule(a.Rule).Exists) return RuleName(a.Rule);
            }
            return null;
        }

        private void DeleteRule(int r)
        {
            try
            {
                if (_engine == null) return;
                var writes = new List<(ID<PartToggleGuiRules>, int)>();
                var gone = new HashSet<int>(ConditionsOf(r)) { r };
                for (int k = 1; k <= _engine.LineCount; k++)
                {
                    LogicLine line = _engine.Line(k);
                    if (!line.IsUsed || !gone.Contains(line.Rule)) continue;
                    for (int f = 0; f < 3; f++) writes.Add((_engine.LineSlot(k, f), 0));
                }
                for (int k = 1; k <= _engine.ActionCount; k++)
                {
                    if (!_engine.Action(k).IsUsed || !gone.Contains(_engine.Action(k).Rule)) continue;
                    for (int f = 0; f < 3; f++) writes.Add((_engine.ActionSlot(k, f), 0));
                    if (_engine.IsStoreV2) { writes.Add((_engine.ActionSlot(k, ZtxRuleEngineComponent.ACT_S1), 0)); writes.Add((_engine.ActionSlot(k, ZtxRuleEngineComponent.ACT_S2), 0)); writes.Add((_engine.ActionSlot(k, ZtxRuleEngineComponent.ACT_V), 0)); }
                }
                foreach (int g in gone) foreach (int q in _engine.RuleSeqIds(g)) ClearSeqWrites(writes, q);
                if (_engine.Rule(r).IsHeader)
                    foreach (int m in LogicGroups.Block(ExistingRules(), r, IsHeaderRule, InGroupRule))
                    {
                        if (m == r) continue;
                        LogicRuleDef member = _engine.Rule(m);
                        member.InGroup = false;
                        writes.Add((_engine.RuleSlot(m, ZtxRuleEngineComponent.RULE_M), member.PackMisc()));
                    }
                foreach (int g in gone) for (int f = 0; f < _engine.RuleFieldCount; f++) writes.Add((_engine.RuleSlot(g, f), 0));
                int cleared = AppendClearRefs(writes, x => x.IsRule && gone.Contains(x.Index), gone, null);
                Write(writes.ToArray());
                Log.Info("console: rule " + r + " deleted" + (gone.Count > 1 ? " with its " + (gone.Count - 1) + " conditions" : "")
                    + (cleared > 0 ? "; " + cleared + " reference(s) to it cleared" : "") + ".");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.DeleteRule", ex); }
        }

        private void ToggleRule(int r)
        {
            try
            {
                if (_engine == null) return;
                LogicRuleDef def = _engine.Rule(r);
                if (!def.Exists) return;
                def.Disabled = !def.Disabled;
                Write((_engine.RuleSlot(r, ZtxRuleEngineComponent.RULE_M), def.PackMisc()));
                Log.Info("console: rule " + r + (def.Disabled ? " disabled (paused)." : " enabled."));
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.ToggleRule", ex); }
        }

        private void ToggleShared(int r)
        {
            try
            {
                if (_engine == null || !_engine.IsStoreV2) return;
                LogicRuleDef def = _engine.Rule(r);
                if (!def.Exists) return;
                def.Shared = !def.Shared;
                Write((_engine.RuleSlot(r, ZtxRuleEngineComponent.RULE_M), def.PackMisc()));
                Log.Info("console: rule " + r + (def.Shared ? " shared as " + def.Name + "." : " no longer shared."));
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.ToggleShared", ex); }
        }

        private void AddLine(int r)
        {
            try
            {
                int k = _engine?.FreeLine() ?? 0;
                if (k == 0) return;
                var line = new LogicLine { Rule = r, Op = LogicOp.Pass, Format = LogicLine.FORMAT };
                var writes = new List<(ID<PartToggleGuiRules>, int)>
                {
                    (_engine.LineSlot(k, ZtxRuleEngineComponent.LINE_M), line.PackMisc()),
                    (_engine.LineSlot(k, ZtxRuleEngineComponent.LINE_A), 0),
                    (_engine.LineSlot(k, ZtxRuleEngineComponent.LINE_B), 0),
                };
                OwnLine(writes, k, r);
                Write(writes.ToArray());
                Log.Info("console: line " + k + " added to rule " + r + ".");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.AddLine", ex); }
        }

        private void DeleteLine(int k)
        {
            try
            {
                if (_engine == null) return;
                var writes = new List<(ID<PartToggleGuiRules>, int)>
                {
                    (_engine.LineSlot(k, 0), 0), (_engine.LineSlot(k, 1), 0), (_engine.LineSlot(k, 2), 0),
                };
                int cleared = AppendClearRefs(writes, x => x.IsLocal && x.Index == k, null, new HashSet<int> { k });
                Write(writes.ToArray());
                Log.Info("console: line " + k + " deleted" + (cleared > 0 ? "; " + cleared + " reference(s) to it cleared" : "") + ".");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.DeleteLine", ex); }
        }

        private int AppendClearRefs(List<(ID<PartToggleGuiRules>, int)> writes, Func<LogicRef, bool> gone, ICollection<int> goneRules, ICollection<int> goneLines)
        {
            int cleared = 0;
            for (int k = 1; k <= _engine.LineCount; k++)
            {
                LogicLine l = _engine.Line(k);
                if (!l.IsUsed || (goneLines != null && goneLines.Contains(k)) || (goneRules != null && goneRules.Contains(l.Rule))) continue;
                bool a = gone(l.A), b = gone(l.B);
                if (!a && !b) continue;
                if (a) l.A = LogicRef.None;
                if (b) l.B = LogicRef.None;
                writes.Add((_engine.LineSlot(k, ZtxRuleEngineComponent.LINE_A), l.PackA()));
                writes.Add((_engine.LineSlot(k, ZtxRuleEngineComponent.LINE_B), l.PackB()));
                cleared += (a ? 1 : 0) + (b ? 1 : 0);
            }
            for (int c = 1; c <= _engine.RuleCount; c++)
            {
                LogicRuleDef cd = _engine.Rule(c);
                if (!cd.Exists || !cd.IsCondition || (goneRules != null && goneRules.Contains(c)) || !gone(cd.Target)) continue;
                writes.Add((_engine.RuleSlot(c, ZtxRuleEngineComponent.RULE_T), LogicRef.None.Pack()));
                cleared++;
            }
            for (int sk = 1; sk <= _engine.StepCount; sk++)
            {
                SeqStep st = _engine.Step(sk);
                if (!st.IsUsed || !(st.Kind == SeqKind.WaitUntil || st.Kind == SeqKind.If || st.Kind == SeqKind.While || st.Kind == SeqKind.Call)) continue;
                if (goneRules != null && st.Seq >= 1 && st.Seq <= _engine.SeqCount && goneRules.Contains(_engine.Seq(st.Seq).Rule)) continue;
                if (!gone(st.Target)) continue;
                writes.Add((_engine.StepSlot(sk, ZtxRuleEngineComponent.STEP_T), LogicRef.None.Pack()));
                cleared++;
            }
            return cleared;
        }

        private void OnConnClicked(LineRow row)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                LogicLine l = row.Current;
                if (!l.IsUsed || _engine.FeedsLine(row.Line) != 0) return;
                l.Or = !l.Or;
                WriteJoined(row, l);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnConnClicked", ex); }
        }

        private void OnBracketClicked(LineRow row, bool open)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                LogicLine l = row.Current;
                if (!l.IsUsed || _engine.FeedsLine(row.Line) != 0) return;
                if (open) l.Open = (l.Open + 1) % (LogicLine.MAX_BRACKETS + 1);
                else l.Close = (l.Close + 1) % (LogicLine.MAX_BRACKETS + 1);
                WriteJoined(row, l);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnBracketClicked", ex); }
        }

        private void WriteJoined(LineRow row, LogicLine l)
        {
            int m = l.PackMisc();
            var writes = new List<(ID<PartToggleGuiRules>, int)> { (_engine.LineSlot(row.Line, ZtxRuleEngineComponent.LINE_M), m) };
            LogicRuleDef def = _engine.Rule(row.Rule);
            if (def.Exists && !def.Joined)
            {
                def.Joined = true;
                writes.Add((_engine.RuleSlot(row.Rule, ZtxRuleEngineComponent.RULE_M), def.PackMisc()));
                Log.Info("console: rule " + row.Rule + " joined (result = its conditions' expression).");
            }
            row.LastRaw[2] = m;
            row.Current = l;
            Write(writes.ToArray());
            Log.Info("console: line " + row.Line + " -> " + l + ".");
        }

        private void CopyRule(int r)
        {
            try
            {
                if (_engine == null) return;
                LogicRuleDef def = _engine.Rule(r);
                if (!def.Exists) return;
                int free2 = _engine.FreeRule();
                if (free2 == 0) { Log.Info("console: copy rule " + r + ": no free rule slot."); return; }
                var src = new List<int>();
                for (int s = 1; s <= _engine.LineCount; s++)
                {
                    LogicLine l = _engine.Line(s);
                    if (l.IsUsed && l.Rule == r) src.Add(s);
                }
                List<int> free = _engine.FreeLines(src.Count);
                if (free.Count < src.Count)
                {
                    Log.Info("console: copy rule " + r + ": needs " + src.Count + " free lines, " + free.Count + " available.");
                    return;
                }
                var srcActs = new List<int>();
                for (int k = 1; k <= _engine.ActionCount; k++)
                {
                    LogicAction act = _engine.Action(k);
                    if (act.IsUsed && act.Rule == r) srcActs.Add(k);
                }
                List<int> freeActs = _engine.FreeActions(srcActs.Count);
                if (freeActs.Count < srcActs.Count)
                {
                    Log.Info("console: copy rule " + r + ": needs " + srcActs.Count + " free actuator slots, " + freeActs.Count + " available.");
                    return;
                }
                var map = new Dictionary<int, int>();
                for (int i = 0; i < src.Count; i++) map[src[i]] = free[i];
                string name = def.Name ?? "";
                name = LogicRuleDef.Clean(name.Length >= LogicRuleDef.NAME_CHARS ? name.Substring(0, LogicRuleDef.NAME_CHARS - 1) + "2" : name + "2");
                var copy = new LogicRuleDef { Exists = true, Disabled = def.Disabled, Target = def.Target, Name = name, PoseSet = def.PoseSet, Pose = def.Pose, Speed = def.Speed, Joined = def.Joined, InGroup = def.InGroup };
                var raw = new int[_engine.RuleFieldCount];
                raw[ZtxRuleEngineComponent.RULE_M] = copy.PackMisc();
                raw[ZtxRuleEngineComponent.RULE_T] = copy.Target.Pack();
                raw[ZtxRuleEngineComponent.RULE_N1] = LogicRuleDef.PackName(name, 0);
                raw[ZtxRuleEngineComponent.RULE_N2] = LogicRuleDef.PackName(name, 5);
                if (_engine.IsStoreV2) raw[ZtxRuleEngineComponent.RULE_C] = _engine.RuleColour(r);
                List<int> order = ExistingRules();
                int after = order.IndexOf(r) + 1;
                var current = new List<int>(order) { free2 };
                var desired = LogicGroups.MovedBefore(current, new List<int> { free2 }, after < order.Count ? order[after] : 0);
                Dictionary<int, int> ruleMap = _engine.IsStoreV2 ? LogicRuleOrder.Remap(current, desired) : new Dictionary<int, int>();
                int r2 = ruleMap.TryGetValue(free2, out int placed) ? placed : free2;
                LogicRef RuleRef(LogicRef x) => x.IsRule && ruleMap.TryGetValue(x.Index, out int to) ? LogicRef.Rule(to) : x;
                var writes = new List<(ID<PartToggleGuiRules>, int)>();
                AppendRuleRemap(writes, ruleMap, free2, raw);
                if (!ruleMap.ContainsKey(free2))
                    for (int f = 0; f < raw.Length; f++) writes.Add((_engine.RuleSlot(free2, f), raw[f]));
                foreach (int s in src)
                {
                    LogicLine l = _engine.Line(s);
                    l.Rule = r2;
                    l.A = RuleRef(RemapLocal(l.A, map));
                    l.B = RuleRef(RemapLocal(l.B, map));
                    int dest = map[s];
                    writes.Add((_engine.LineSlot(dest, ZtxRuleEngineComponent.LINE_A), l.PackA()));
                    writes.Add((_engine.LineSlot(dest, ZtxRuleEngineComponent.LINE_B), l.PackB()));
                    writes.Add((_engine.LineSlot(dest, ZtxRuleEngineComponent.LINE_M), l.PackMisc()));
                    OwnLine(writes, dest, r2);
                }
                var actMap = new Dictionary<int, int>();
                for (int i = 0; i < srcActs.Count; i++) actMap[srcActs[i]] = freeActs[i];
                for (int i = 0; i < srcActs.Count; i++)
                {
                    LogicAction act = _engine.Action(srcActs[i]);
                    act.Rule = r2;
                    int dest = freeActs[i];
                    writes.Add((_engine.ActionSlot(dest, ZtxRuleEngineComponent.ACT_T), act.Target.Pack()));
                    writes.Add((_engine.ActionSlot(dest, ZtxRuleEngineComponent.ACT_M), act.PackMisc()));
                    writes.Add((_engine.ActionSlot(dest, ZtxRuleEngineComponent.ACT_P), act.PackPose()));
                    OwnAct(writes, dest, r2);
                    if (_engine.IsStoreV2) writes.Add((_engine.ActionPosSlot(dest), act.Pos));
                    if (_engine.IsStoreV2)
                    {
                        writes.Add((_engine.ActionSlot(dest, ZtxRuleEngineComponent.ACT_S1), act.OnStation.Pack()));
                        writes.Add((_engine.ActionSlot(dest, ZtxRuleEngineComponent.ACT_S2), act.OffStation.Pack()));
                        writes.Add((_engine.ActionSlot(dest, ZtxRuleEngineComponent.ACT_V), act.Drive));
                    }
                }
                IReadOnlyList<int> srcSeqs = _engine.RuleSeqIds(r);
                if (_engine.IsStoreV2 && srcSeqs.Count > 0)
                {
                    int stepTotal = 0;
                    foreach (int q in srcSeqs) stepTotal += _engine.SeqStepIds(q).Count;
                    List<int> freeSeqs = _engine.FreeSeqs(srcSeqs.Count), freeSteps = _engine.FreeSteps(stepTotal);
                    int n = 0;
                    for (int i = 0; i < srcSeqs.Count; i++)
                    {
                        int q = srcSeqs[i], q2 = freeSeqs[i];
                        writes.Add((_engine.SeqSlot(q2, LogicStore.SEQ_M), _engine.Seq(q).PackM()));
                        writes.Add((_engine.SeqSlot(q2, LogicStore.SEQ_R), r2));
                        foreach (int sk in _engine.SeqStepIds(q))
                        {
                            SeqStep st = _engine.Step(sk);
                            if (st.Kind == SeqKind.WaitUntil) st.Target = RuleRef(RemapLocal(st.Target, map));
                            int dest = freeSteps[n++];
                            writes.Add((_engine.StepSlot(dest, ZtxRuleEngineComponent.STEP_T), st.Target.Pack()));
                            writes.Add((_engine.StepSlot(dest, ZtxRuleEngineComponent.STEP_M), st.PackM()));
                            writes.Add((_engine.StepSlot(dest, ZtxRuleEngineComponent.STEP_P), st.PackP()));
                            writes.Add((_engine.StepSlot(dest, ZtxRuleEngineComponent.STEP_Q), st.PackQ()));
                            writes.Add((_engine.StepSlot(dest, ZtxRuleEngineComponent.STEP_S), st.Station.Pack()));
                            writes.Add((_engine.StepSlot(dest, ZtxRuleEngineComponent.STEP_V), st.Kind == SeqKind.Do && actMap.TryGetValue(st.Drive, out int copied) ? copied : st.Drive));
                            writes.Add((_engine.StepOwnerSlot(dest), q2));
                        }
                    }
                }
                Write(writes.ToArray());
                RemapCollapsed(ruleMap);
                Log.Info("console: rule " + r + " copied to rule " + r2 + " below it (" + src.Count + " lines).");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.CopyRule", ex); }
        }

        private static LogicRef RemapLocal(LogicRef r, Dictionary<int, int> map)
        {
            return r.IsLocal && map.TryGetValue(r.Index, out int n) ? LogicRef.Local(n) : r;
        }

        private void OnNameChanged(RuleBlock b)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                string clean = LogicRuleDef.Clean(b.Name.Text);
                int n1 = LogicRuleDef.PackName(clean, 0);
                int n2 = LogicRuleDef.PackName(clean, 5);
                if (n1 == b.LastN1 && n2 == b.LastN2) return;
                b.LastN1 = n1;
                b.LastN2 = n2;
                Write((_engine.RuleSlot(b.Rule, ZtxRuleEngineComponent.RULE_N1), n1), (_engine.RuleSlot(b.Rule, ZtxRuleEngineComponent.RULE_N2), n2));
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnNameChanged", ex); }
        }

        private void OnValueChanged(LineRow row)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                LogicLine l = row.Current;
                if (!l.IsUsed || !LogicParams.TryParse(l.Op, row.Value.Text, out int v, l.Units) || v == l.Value) return;
                l.Value = v;
                row.LastRaw[2] = l.PackMisc();
                row.Current = l;
                Write((_engine.LineSlot(row.Line, ZtxRuleEngineComponent.LINE_M), row.LastRaw[2]));
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnValueChanged", ex); }
        }

        private void OnUnitClicked(LineRow row)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                LogicLine l = row.Current;
                if (!l.IsUsed || !LogicRuleEval.IsCompare(l.Op)) return;
                l.Units = !l.Units;
                l.Value = LogicParams.Clamp(l.Op, l.Value, l.Units);
                row.LastRaw[2] = l.PackMisc();
                row.Current = l;
                Write((_engine.LineSlot(row.Line, ZtxRuleEngineComponent.LINE_M), row.LastRaw[2]));
                ShowValue(row, l);
                Log.Info("console: line " + row.Line + " -> " + l + ".");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnUnitClicked", ex); }
        }

        private void OnLinePicked(LineRow row, int field, int value)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                if (value < 0) return;
                LogicLine before = row.Current;
                LogicLine l = before;
                switch (field)
                {
                    case 0:
                        if (l.A.Pack() == value) return;
                        l.A = LogicRef.Unpack(value);
                        break;
                    case 1:
                        if (l.B.Pack() == value) return;
                        l.B = LogicRef.Unpack(value);
                        break;
                    case 2:
                        if ((int)l.Op == value) return;
                        l.Op = (LogicOp)value;
                        break;
                    default:
                        return;
                }
                string fixes = Fit(ref l, field);
                if (field != 2 && IsMemCounterRef(l.A) && LogicRuleEval.IsCompare(l.Op)) l.Units = true;
                if (!LogicRuleEval.IsCompare(l.Op)) l.Units = false;
                l.Value = LogicParams.Clamp(l.Op, l.Value, l.Units);
                int a = l.PackA(), bb = l.PackB(), m = l.PackMisc();
                var writes = new List<(ID<PartToggleGuiRules>, int)>();
                if (a != before.PackA()) writes.Add((_engine.LineSlot(row.Line, ZtxRuleEngineComponent.LINE_A), a));
                if (bb != before.PackB()) writes.Add((_engine.LineSlot(row.Line, ZtxRuleEngineComponent.LINE_B), bb));
                if (m != before.PackMisc()) writes.Add((_engine.LineSlot(row.Line, ZtxRuleEngineComponent.LINE_M), m));
                row.LastRaw[0] = a;
                row.LastRaw[1] = bb;
                row.LastRaw[2] = m;
                row.Current = l;
                Write(writes.ToArray());
                ShowValue(row, l);
                Log.Info("console: line " + row.Line + " -> " + l + (fixes.Length > 0 ? " (" + fixes + ")" : "") + ".");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnLineSelected", ex); }
        }

        private string Fit(ref LogicLine l, int field)
        {
            string fixes = "";
            InputClass ac = ClassOf(l.A);
            if (field == 0 && l.Op != LogicOp.None && !LogicOpCatalog.IsAllowed(ac, l.Op))
            {
                LogicOp to = LogicOpCatalog.Default(ac);
                fixes = l.Op + " -> " + to;
                l.Op = to;
            }
            if (!l.B.IsNone && !LogicOpCatalog.BAccepts(l.Op, ClassOf(l.B)))
            {
                fixes += (fixes.Length > 0 ? ", " : "") + "B cleared";
                l.B = LogicRef.None;
                l.NotB = false;
            }
            if (LogicRuleEval.IsCompare(l.Op) && (l.NotA || l.NotB))
            {
                l.NotA = false;
                l.NotB = false;
                fixes += (fixes.Length > 0 ? ", " : "") + "NOT cleared";
            }
            return fixes;
        }
    }
}
