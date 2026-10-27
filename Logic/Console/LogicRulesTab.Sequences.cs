using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
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
        private static float StepKindWidth => 120f * LogicUiScale.Rules;
        private static float StepStateWidth => 240f * LogicUiScale.Rules;
        private static float TriggerWidth => 110f * LogicUiScale.Rules;
        private static float StepToggleWidth => 110f * LogicUiScale.Rules;
        private static float StepNumberWidth => 34f * LogicUiScale.Rules;
        private static float SeqCountWidth => 80f * LogicUiScale.Rules;
        private static float SeqStatusWidth => 230f * LogicUiScale.Rules;
        private static float SeqDeleteWidth => 70f * LogicUiScale.Rules;
        private static float StepSpeedWidth => 130f * LogicUiScale.Rules;
        private static float VarListWidth => 140f * LogicUiScale.Rules;
        private static float SideWidth => 60f * LogicUiScale.Rules;

        private sealed class SeqBlock
        {
            public int Id;
            public RuleBlock Block;
            public LayoutBox Box, Header;
            public Label Count, Status;
            public PickerCell VarList;
            public HideCell VarCell;
            public Button Side;
            public HideCell SideCell;
            public int LastOwner = -1;
            public PickerCell TriggerList;
            public Button Queue, Refusal, Open, Delete;
            public Button AddDo;
            public PickerCell AddBlockList;
            public readonly List<(int Id, Widget Box, Widget Detail)> Items = new List<(int, Widget, Widget)>();
            public Button AddStep;
            public readonly List<StepRow> Steps = new List<StepRow>();
            public readonly List<Widget> Inner = new List<Widget>();
            public readonly List<Button> InsertButtons = new List<Button>();
            public int LastM = int.MinValue;
            public bool DelConfirm;
        }

        private sealed class StepRow
        {
            public int Id;
            public SeqBlock Seq;
            public LayoutBox Box, Detail;
            public Label Number, State;
            public PickerCell Kind, Target, Accel, Strength, Grip, Merge, Speed;
            public PickerCell Lock;
            public HideCell LockCell;
            public PickerCell Mode, Station;
            public HideCell ModeCell, StationCell;
            public TextCell CartSpeedT, CartPowerT, CartDwellT;
            public readonly List<HideCell> CartFieldCells = new List<HideCell>();
            public Label PoseLabel, PoseUnit;
            public int TargetKind = -1;
            public Widget Indent;
            public Label Marker;
            public Button NotBtn, Delete;
            public TextCell Times;
            public HideCell KindCell, MarkerCell, NotCell, TimesCell, FlagCell;
            public Button FlagBtn;
            public LayoutBox AddRow;
            public Widget AddIndent;
            public int LastDepth = -1;
            public TextCell Pose, Seconds, Timeout;
            public Button NoWait, Up, Down;
            public HideCell TargetCell, NoWaitCell;
            public HideCell PoseLabelCell, PoseCell, PoseUnitCell, SpeedCell, AccelCell, StrengthCell;
            public HideCell GripCell, MergeCell, SecondsLabelCell, SecondsCell, TimeoutLabelCell, TimeoutCell;
            public readonly int[] LastRaw = { int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue };
            public SeqKind LastKind = (SeqKind)(-1);
            public SeqStep Current;
        }

        private static readonly ConditionalWeakTable<Part, HashSet<int>> s_seqClosed = new ConditionalWeakTable<Part, HashSet<int>>();
        private int _confirmSeq;
        private int _confirmSeqUntil;

        private bool IsSeqOpen(int q) => _console == null || !s_seqClosed.TryGetValue(_console, out HashSet<int> set) || !set.Contains(q);

        private void SetSeqOpen(int q, bool open)
        {
            if (_console == null) return;
            HashSet<int> set = s_seqClosed.GetOrCreateValue(_console);
            if (open) set.Remove(q);
            else set.Add(q);
        }

        private void BuildSeqBlock(RuleBlock block, int q)
        {
            var sb = new SeqBlock { Id = q, Block = block, Header = Row(BlockInnerWidth) };
            sb.Box = BlockFrame();
            SeqBlock csb = sb;
            LayoutBox h = sb.Header;
            AddGrip(h, () => StartOutputDrag(csb.Block, LogicOutputStack.SEQ, csb.Id));
            sb.Open = LogicIcons.Make(IsSeqOpen(q) ? LogicIcons.Collapse : LogicIcons.Expand, IsSeqOpen(q) ? "ZtxLogicPanel/Collapse" : "ZtxLogicPanel/Expand", SmallButton);
            sb.Open.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipSeqOpen");
            sb.Open.Clicked += delegate { ToggleSeqOpen(csb); };
            h.DockedChildren.Add(sb.Open);
            sb.TriggerList = MakeList(TriggerWidth, "ZtxLogicPanel/TipTrigger", v => OnSeqKindPicked(csb.Id, v), partHover: false);
            FillKeyed(sb.TriggerList, new List<(int, string)>
            {
                (HandlerRun.WHEN, "HandlerWhen"), (HandlerRun.WHILE, "HandlerWhile"), (HandlerRun.ALWAYS, "HandlerAlways"), (HandlerRun.CALL, "HandlerOnCall"),
            });
            h.DockedChildren.Add(sb.TriggerList.Button);
            sb.Side = MakeButton("ZtxLogicPanel/BlockSideOn", SideWidth, ButtonRole.Misc);
            sb.Side.Clicked += delegate { ToggleSeqSide(csb.Id); };
            sb.SideCell = Hideable(h, sb.Side, SideWidth);
            sb.SideCell.KeepSpace = false;
            sb.VarList = MakeList(VarListWidth, "ZtxLogicPanel/TipBlockVar", v => OnSeqVarPicked(csb, v), partHover: false);
            sb.VarCell = Hideable(h, sb.VarList.Button, VarListWidth);
            sb.VarCell.KeepSpace = false;
            sb.Count = new Label { DockMode = DockMode.Left, Width = SeqCountWidth };
            sb.Count.TextRenderer.HAlignment = HAlignment.Center;
            sb.Count.TextRenderer.OversizeMode = OversizeMode.Shrink;
            h.DockedChildren.Add(sb.Count);
            sb.Queue = MakeButton("ZtxLogicPanel/QueueOff", StepToggleWidth, ButtonRole.Misc);
            sb.Queue.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipQueue");
            sb.Queue.Clicked += delegate { ToggleSeqBit(csb.Id, 1); };
            h.DockedChildren.Add(sb.Queue);
            sb.Refusal = MakeButton("ZtxLogicPanel/RefusalGoOn", StepToggleWidth + 30f, ButtonRole.Misc);
            sb.Refusal.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipRefusal");
            sb.Refusal.Clicked += delegate { ToggleSeqBit(csb.Id, 2); };
            h.DockedChildren.Add(sb.Refusal);
            sb.Status = new Label { DockMode = DockMode.Left, Width = SeqStatusWidth };
            sb.Status.TextRenderer.HAlignment = HAlignment.Left;
            sb.Status.TextRenderer.OversizeMode = OversizeMode.Shrink;
            sb.Status.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipSeqStatus");
            h.DockedChildren.Add(sb.Status);
            sb.Delete = MakeButton("ZtxLogicPanel/DeleteSeq", SeqDeleteWidth, ButtonRole.Delete);
            sb.Delete.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipDeleteSeq");
            sb.Delete.Clicked += delegate { OnDeleteSeqClicked(csb); };
            h.DockedChildren.Add(sb.Delete);
            AddBody(block, sb.Box);
            sb.Box.AddChild(h);

            bool open = IsSeqOpen(q);
            int n = 0;
            foreach (int id in _engine.SeqStepIds(q))
            {
                SeqStep st = _engine.Step(id);
                if (st.Kind == SeqKind.Do)
                {
                    ActRow act = BuildAct(block, st.Drive, csb, id);
                    if (IsExpanded(st.Drive)) BuildSettings(act);
                    AddInner(sb, act.Box, open);
                    AddInner(sb, act.Settings, open);
                    if (!IsExpanded(st.Drive)) block.Hidden.Add(act.Settings);
                    block.Acts.Add(act);
                    sb.Items.Add((id, act.Box, act.Settings));
                    continue;
                }
                StepRow row = BuildStep(sb, id, ++n);
                if (row.AddRow != null) AddInner(sb, row.AddRow, open);
                AddInner(sb, row.Box, open);
                AddInner(sb, row.Detail, open);
                sb.Steps.Add(row);
                sb.Items.Add((id, row.Box, row.Detail));
            }
            LayoutBox addRow = Row(BlockInnerWidth);
            addRow.DockedChildren.Add(new Widget { DockMode = DockMode.Left, Width = IndentWidth + GripWidth + StepNumberWidth });
            sb.AddStep = MakeButton("ZtxLogicPanel/AddStep", ButtonWidth, ButtonRole.Add);
            sb.AddStep.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipAddStep");
            sb.AddStep.Clicked += delegate { AddStep(csb.Id); };
            addRow.DockedChildren.Add(sb.AddStep);
            sb.AddDo = MakeButton("ZtxLogicPanel/AddDoStatement", ButtonWidth, ButtonRole.Add);
            sb.AddDo.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipAddDoStatement");
            sb.AddDo.Clicked += delegate { AddDoStatement(csb.Id); };
            addRow.DockedChildren.Add(sb.AddDo);
            sb.AddBlockList = MakeList(ButtonWidth, "ZtxLogicPanel/TipAddBlock", v => OnAddBlockPicked(csb, v), partHover: false);
            FillKeyed(sb.AddBlockList, new List<(int, string)> { (0, "AddBlock"), (1, "BlockIf"), (2, "BlockIfElse"), (3, "BlockRepeat"), (4, "BlockWhile"), (5, "BlockStop"), (6, "BlockTogether") });
            Select(sb.AddBlockList, 0);
            SetTextColour(sb.AddBlockList.Button, RoleHex(ButtonRole.Add));
            addRow.DockedChildren.Add(sb.AddBlockList.Button);
            AddMoreControls(addRow, sb, 0);
            AddInner(sb, addRow, open);
            block.Seqs.Add(sb);
            FitFrame(sb.Box);
        }

        private void AddInner(SeqBlock sb, Widget w, bool open)
        {
            sb.Box.AddChild(w);
            sb.Block.Body.Add(w);
            sb.Inner.Add(w);
            if (!open) sb.Block.Hidden.Add(w);
        }

        private StepRow BuildStep(SeqBlock sb, int id, int number)
        {
            var row = new StepRow { Id = id, Seq = sb, Box = Row(BlockInnerWidth), Detail = FlowRow(BlockInnerWidth) };
            StepRow crow = row;
            row.Indent = new Widget { DockMode = DockMode.Left, Width = IndentWidth };
            row.Box.DockedChildren.Add(row.Indent);
            AddGrip(row.Box, () => StartStepDrag(crow.Seq, crow.Id));
            row.Number = new Label { DockMode = DockMode.Left, Width = StepNumberWidth, Text = number + "." };
            row.Box.DockedChildren.Add(row.Number);
            row.Kind = MakeList(StepKindWidth, "ZtxLogicPanel/TipStepKind", v => OnStepKindPicked(crow, v), partHover: false);
            var kindKeys = new List<(int, string)> { ((int)SeqKind.Wait, "StepWait"), ((int)SeqKind.WaitUntil, "StepUntil") };
            if (_engine.Step(id).Kind == SeqKind.Command) kindKeys.Insert(0, ((int)SeqKind.Command, "StepCommand"));
            FillKeyed(row.Kind, kindKeys);
            row.KindCell = Hideable(row.Box, row.Kind.Button, StepKindWidth);
            row.KindCell.KeepSpace = false;
            row.Marker = new Label { DockMode = DockMode.Left, Width = StepKindWidth };
            row.Marker.TextRenderer.HAlignment = HAlignment.Left;
            row.Marker.TextRenderer.OversizeMode = OversizeMode.Shrink;
            ScaleText(row.Marker, HeaderTextScale);
            SetTextColour(row.Marker, LogicPalette.Set);
            row.MarkerCell = Hideable(row.Box, row.Marker, StepKindWidth);
            row.MarkerCell.KeepSpace = false;
            row.MarkerCell.Show(false);
            SeqKind built = _engine.Step(id).Kind;
            if (built == SeqKind.End)
            {
                row.Box.DockedChildren.Add(new Widget { DockMode = DockMode.Left, Width = BlockIndent });
                AddInsertControls(row.Box, sb, id);
            }
            else if (built == SeqKind.Else)
            {
                row.AddRow = Row(BlockInnerWidth);
                row.AddIndent = new Widget { DockMode = DockMode.Left, Width = IndentWidth + BlockIndent + GripWidth + StepNumberWidth };
                row.AddRow.DockedChildren.Add(row.AddIndent);
                AddInsertControls(row.AddRow, sb, id);
            }
            row.Target = MakeList(TargetWidth, "ZtxLogicPanel/TipStepTarget", v => OnStepTargetPicked(crow, v));
            row.TargetCell = Hideable(row.Box, row.Target.Button, TargetWidth);
            row.NotBtn = MakeButton("ZtxLogicPanel/NotOff", NotWidth);
            row.NotBtn.Clicked += delegate { OnStepFlag(crow, 1); };
            row.NotCell = Hideable(row.Box, row.NotBtn, NotWidth);
            row.NotCell.KeepSpace = false;
            row.NotCell.Show(false);
            row.Times = SetField(SpeedWidth, "ZtxLogicPanel/TipRepeatTimes");
            row.Times.Changed += () => OnStepTimes(crow);
            row.TimesCell = Hideable(row.Box, row.Times.Display, SpeedWidth);
            row.TimesCell.KeepSpace = false;
            row.TimesCell.Show(false);
            row.FlagBtn = MakeButton("ZtxLogicPanel/SigOn", NotWidth + 20f);
            row.FlagBtn.Clicked += delegate { OnStepFlag(crow, 2); };
            row.FlagCell = Hideable(row.Box, row.FlagBtn, NotWidth + 20f);
            row.FlagCell.KeepSpace = false;
            row.FlagCell.Show(false);
            row.SecondsLabelCell = Hideable(row.Box, SetLabel("ZtxLogicPanel/StepSeconds"), SetLabelWidth);
            row.Seconds = SetField(SpeedWidth, "ZtxLogicPanel/TipStepSeconds");
            row.Seconds.Changed += () => OnStepNumber(crow, 2);
            row.SecondsCell = Hideable(row.Box, row.Seconds.Display, SpeedWidth);
            row.TimeoutLabelCell = Hideable(row.Box, SetLabel("ZtxLogicPanel/StepTimeout"), SetLabelWidth + 30f);
            row.Timeout = SetField(SpeedWidth, "ZtxLogicPanel/TipStepTimeout");
            row.Timeout.Changed += () => OnStepNumber(crow, 3);
            row.TimeoutCell = Hideable(row.Box, row.Timeout.Display, SpeedWidth);
            row.State = new Label { DockMode = DockMode.Left, Width = StepStateWidth };
            row.State.TextRenderer.HAlignment = HAlignment.Left;
            row.State.TextRenderer.OversizeMode = OversizeMode.Shrink;
            row.Box.DockedChildren.Add(row.State);
            row.NoWait = MakeButton("ZtxLogicPanel/StepWaits", StepToggleWidth);
            row.NoWait.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipNoWait");
            row.NoWait.Clicked += delegate { OnStepFlag(crow, 0); };
            row.NoWaitCell = Hideable(row.Box, row.NoWait, StepToggleWidth);
            Button x = LogicIcons.Make(LogicIcons.Close, "ZtxLogicPanel/DeleteLine", SmallButton);
            x.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipDeleteStep");
            x.Clicked += delegate { DeleteStep(crow.Id); };
            row.Delete = x;
            row.Box.DockedChildren.Add(x);

            LayoutBox d = row.Detail;
            d.Children.BorderPadding = new Borders(IndentWidth + GripWidth + StepNumberWidth, 0f, 0f, 0f);
            row.PoseLabel = SetLabel("ZtxLogicPanel/StepPos");
            row.PoseLabelCell = Hideable(d, row.PoseLabel, SetLabelWidth);
            row.Pose = SetField(PoseWidth, "ZtxLogicPanel/TipStepPos");
            row.Pose.Changed += () => OnStepNumber(crow, 0);
            row.PoseCell = Hideable(d, row.Pose.Display, PoseWidth);
            Label pu = SetUnit(UnitWidth);
            LogicGui.SetKey(pu, "ZtxLogicPanel/UnitCells");
            row.PoseUnit = pu;
            row.PoseUnitCell = Hideable(d, pu, UnitWidth);
            row.Station = MakeList(StationWidth, "ZtxLogicPanel/TipCartStation", v => OnStepList(crow, 7, v));
            row.StationCell = Hideable(d, row.Station.Button, StationWidth);
            row.Mode = MakeList(CartModeWidth, "ZtxLogicPanel/TipCartMode", v => OnStepList(crow, 6, v), partHover: false);
            row.Mode.TipKey = LeaveTip;
            Fill(row.Mode, CartModeItems());
            row.ModeCell = Hideable(d, row.Mode.Button, CartModeWidth);
            row.Speed = MakeList(StepSpeedWidth, "ZtxLogicPanel/TipStepSpeed", v => OnStepList(crow, 4, v), partHover: false);
            row.SpeedCell = Hideable(d, row.Speed.Button, StepSpeedWidth);
            row.CartSpeedT = CartField(d, row.CartFieldCells, "ZtxLogicPanel/CartSpeedLabel", "ZtxLogicPanel/UnitCellsS", "ZtxLogicPanel/TipCartSpeedText", () => OnStepCartDrive(crow));
            row.CartPowerT = CartField(d, row.CartFieldCells, "ZtxLogicPanel/CartPowerLabel", "ZtxLogicPanel/UnitPercent", "ZtxLogicPanel/TipCartPowerText", () => OnStepCartDrive(crow));
            row.CartDwellT = CartField(d, row.CartFieldCells, "ZtxLogicPanel/CartDwellLabel", "ZtxLogicPanel/UnitSeconds", "ZtxLogicPanel/TipCartDwellText", () => OnStepCartDrive(crow));
            row.Accel = MakeList(AccelWidth, "ZtxLogicPanel/TipAccel", v => OnStepList(crow, 0, v), partHover: false);
            Fill(row.Accel, AccelItems());
            row.AccelCell = Hideable(d, row.Accel.Button, AccelWidth);
            row.Strength = MakeList(StrengthWidth, "ZtxLogicPanel/TipStrength", v => OnStepList(crow, 1, v), partHover: false);
            Fill(row.Strength, StrengthItems());
            row.StrengthCell = Hideable(d, row.Strength.Button, StrengthWidth);
            row.Grip = MakeList(ChannelWidth, "ZtxLogicPanel/TipStepGrip", v => OnStepList(crow, 2, v), partHover: false);
            FillKeyed(row.Grip, new List<(int, string)> { (0, "StepGripNone"), (1, "GripAttach"), (2, "GripRelease"), (LogicAction.GRIP_SET_OFF, "GripSetOff") });
            row.GripCell = Hideable(d, row.Grip.Button, ChannelWidth);
            row.Merge = MakeList(ChannelWidth, "ZtxLogicPanel/TipStepMerge", v => OnStepList(crow, 3, v), partHover: false);
            FillKeyed(row.Merge, MergeKeys(0));
            row.MergeCell = Hideable(d, row.Merge.Button, ChannelWidth);
            row.Lock = MakeList(ChannelWidth, "ZtxLogicPanel/TipStepLock", v => OnStepList(crow, 5, v), partHover: false);
            FillKeyed(row.Lock, LockKeys(0));
            row.LockCell = Hideable(d, row.Lock.Button, ChannelWidth);
            foreach (HideCell c in new[] { row.PoseLabelCell, row.PoseCell, row.PoseUnitCell, row.StationCell, row.ModeCell, row.SpeedCell,
                row.AccelCell, row.StrengthCell, row.GripCell, row.MergeCell, row.LockCell, row.SecondsLabelCell, row.SecondsCell,
                row.TimeoutLabelCell, row.TimeoutCell })
                c.KeepSpace = false;
            return row;
        }

        private void AddInsertControls(LayoutBox host, SeqBlock sb, int before)
        {
            SeqBlock csb = sb;
            Button step = MakeButton("ZtxLogicPanel/AddStep", ButtonWidth, ButtonRole.Add);
            step.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipInsertStep");
            step.Clicked += delegate { InsertStatement(csb.Id, before, INSERT_WAIT); };
            host.DockedChildren.Add(step);
            Button set = MakeButton("ZtxLogicPanel/AddDoStatement", ButtonWidth, ButtonRole.Add);
            set.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipInsertSetHere");
            set.Clicked += delegate { InsertStatement(csb.Id, before, INSERT_SET); };
            host.DockedChildren.Add(set);
            PickerCell blocks = null;
            blocks = MakeList(ButtonWidth, "ZtxLogicPanel/TipInsertBlock", v =>
            {
                if (v != 0) InsertStatement(csb.Id, before, v);
                Select(blocks, 0);
            }, partHover: false);
            FillKeyed(blocks, new List<(int, string)>
            {
                (0, "AddBlock"), (INSERT_IF, "BlockIf"), (INSERT_IF + 1, "BlockIfElse"), (INSERT_IF + 2, "BlockRepeat"),
                (INSERT_IF + 3, "BlockWhile"), (INSERT_IF + 4, "BlockStop"), (INSERT_IF + 5, "BlockTogether"),
            });
            Select(blocks, 0);
            SetTextColour(blocks.Button, RoleHex(ButtonRole.Add));
            host.DockedChildren.Add(blocks.Button);
            sb.InsertButtons.Add(step);
            sb.InsertButtons.Add(set);
            sb.InsertButtons.Add(blocks.Button);
            AddMoreControls(host, sb, before);
        }

        private void AddMoreControls(LayoutBox host, SeqBlock sb, int before)
        {
            SeqBlock csb = sb;
            Button call = MakeButton("ZtxLogicPanel/AddCall", ButtonWidth, ButtonRole.Add);
            call.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipAddCall");
            call.Clicked += delegate { InsertStatement(csb.Id, before, INSERT_CALL); };
            host.DockedChildren.Add(call);
            PickerCell memory = null;
            memory = MakeList(ButtonWidth, "ZtxLogicPanel/TipAddMemStatement", v =>
            {
                if (v != 0) InsertStatement(csb.Id, before, v);
                Select(memory, 0);
            }, partHover: false);
            FillKeyed(memory, new List<(int, string)>
            {
                (0, "AddMemStatement"), (INSERT_MEMSET, "InsertMemSet"), (INSERT_MEMADD, "InsertMemAdd"), (INSERT_MEMRESET, "InsertMemReset"),
            });
            Select(memory, 0);
            SetTextColour(memory.Button, RoleHex(ButtonRole.Add));
            host.DockedChildren.Add(memory.Button);
            sb.InsertButtons.Add(call);
            sb.InsertButtons.Add(memory.Button);
        }

        private List<(int, string)> CallItems(int f, LogicRef keep)
        {
            var calls = new Dictionary<int, List<int>>();
            for (int k = 1; k <= _engine.StepCount; k++)
            {
                SeqStep st = _engine.Step(k);
                if (!st.IsUsed || st.Kind != SeqKind.Call || !st.Target.IsRule || !_engine.Seq(st.Seq).IsUsed) continue;
                int owner = OwnerFunction(_engine.Seq(st.Seq).Rule);
                if (!calls.TryGetValue(owner, out List<int> list)) calls[owner] = list = new List<int>();
                list.Add(st.Target.Index);
            }
            Func<int, IEnumerable<int>> of = g => calls.TryGetValue(g, out List<int> c) ? c : new List<int>();
            var l = new List<(int, string)> { (0, Strings.GetText("ZtxLogicPanel/InNone")) };
            bool kept = keep.IsNone;
            for (int r = 1; r <= _engine.RuleCount; r++)
            {
                if (!IsFunctionRule(r) || !LogicCalls.CanCall(f, r, of)) continue;
                LogicRef rr = LogicRef.Rule(r);
                if (rr.Equals(keep)) kept = true;
                bool any = false;
                foreach (int s in _engine.RuleSeqIds(r)) if (_engine.Seq(s).Trigger == HandlerTrigger.OnCall) any = true;
                l.Add((rr.Pack(), RuleText(r) + (any ? "" : LogicPalette.Wrap(LogicPalette.Dim, Strings.GetText("ZtxLogicPanel/CallNoBlock")))));
            }
            if (!kept) l.Add((keep.Pack(), Stale(keep)));
            return l;
        }

        private static List<(int, string)> LockKeys(int tkind)
        {
            string leave, on, off;
            if (tkind == LogicAction.KIND_SWITCH) { leave = "StepSwitchLeave"; on = "StepSwitchOpen"; off = "StepSwitchClosed"; }
            else if (tkind == LogicAction.KIND_DOCK) { leave = "StepDockLeave"; on = "StepDockConnect"; off = "StepDockRelease"; }
            else { leave = "StepLockLeave"; on = "StepLockOn"; off = "StepLockOff"; }
            return ThreeWayKeys(leave, on, off);
        }

        private static List<(int, string)> MergeKeys(int tkind)
        {
            if (tkind == LogicAction.KIND_DOCK) return ThreeWayKeys("StepThrustLeave", "StepThrustOn", "StepThrustOff");
            return ThreeWayKeys("StepMergeLeave", "StepMergeOn", "StepMergeOff");
        }

        private static List<(int, string)> ThreeWayKeys(string leave, string on, string off) =>
            new List<(int, string)> { (0, leave), (1, on), (2, off) };

        private static string LockTip(int tkind) => tkind == LogicAction.KIND_SWITCH ? "ZtxLogicPanel/TipStepSwitch"
            : tkind == LogicAction.KIND_DOCK ? "ZtxLogicPanel/TipStepDock" : "ZtxLogicPanel/TipStepLock";

        private static string MergeTip(int tkind) => tkind == LogicAction.KIND_DOCK ? "ZtxLogicPanel/TipStepThrust"
            : "ZtxLogicPanel/TipStepMerge";

        private void RefreshSequences(RuleBlock b, List<Part> members, bool commandable)
        {
            if (b.AddTop != null) b.AddTop.Button.SelfInputActive = commandable && _engine.IsStoreV2;
            foreach (SeqBlock sb in b.Seqs) RefreshSeqBlock(sb, members, commandable);
        }

        private void RefreshSeqBlock(SeqBlock sb, List<Part> members, bool commandable)
        {
            int m = _engine.RawSeq(sb.Id, LogicStore.SEQ_M);
            SeqDef def = _engine.Seq(sb.Id);
            if (m != sb.LastM)
            {
                sb.LastM = m;
                LogicGui.SetKey(sb.Queue, def.QueueOne ? "ZtxLogicPanel/QueueOn" : "ZtxLogicPanel/QueueOff");
                LogicGui.SetKey(sb.Refusal, def.StopOnRefusal ? "ZtxLogicPanel/RefusalStop" : "ZtxLogicPanel/RefusalGoOn");
                StateTip(sb.Queue, def.QueueOne ? "ZtxLogicPanel/TipQueueOne" : "ZtxLogicPanel/TipQueueOff");
                StateTip(sb.Refusal, def.StopOnRefusal ? "ZtxLogicPanel/TipRefusalStop" : "ZtxLogicPanel/TipRefusalGoOn");
                int family = HandlerRun.Family(def.Trigger);
                bool off = HandlerRun.IsOff(def.Trigger);
                if (!_picker.IsOpenFor(sb.TriggerList)) Select(sb.TriggerList, family);
                SetTextColour(sb.TriggerList.Button, LogicPalette.LitValue);
                LogicGui.SetKey(sb.Side, off ? "ZtxLogicPanel/BlockSideOff" : "ZtxLogicPanel/BlockSideOn");
                StateTip(sb.Side, off ? "ZtxLogicPanel/TipBlockSideOff" : "ZtxLogicPanel/TipBlockSideOn");
                SetTextColour(sb.Side, off ? LogicPalette.LitNot : LogicPalette.LitValue);
                sb.SideCell.Show(family == HandlerRun.WHEN || family == HandlerRun.WHILE);
                bool repeats = HandlerRun.Repeats(def.Trigger);
                if (sb.Queue.SelfActive == repeats) sb.Queue.SelfActive = !repeats;
                SetLit(sb.Queue, def.QueueOne, LogicPalette.LitValue);
                SetLit(sb.Refusal, def.StopOnRefusal, LogicPalette.LitValue);
            }
            bool always = HandlerRun.NoVariable(def.Trigger);
            sb.VarCell.Show(!always);
            if (!always && !_picker.IsOpenFor(sb.VarList))
            {
                LogicRef owner = LogicRef.Rule(def.Rule);
                List<(int, string)> vars = VarItems(sb.Block.Rule, owner);
                bool rebuilt = false;
                if (SignatureOf(vars) != sb.VarList.Signature) { Fill(sb.VarList, vars); rebuilt = true; }
                if (rebuilt || def.Rule != sb.LastOwner) { sb.LastOwner = def.Rule; Select(sb.VarList, owner.Pack()); }
            }
            sb.VarList.Button.SelfInputActive = commandable;
            int count = _engine.SeqStepIds(sb.Id).Count;
            string countText = Strings.FormatText("ZtxLogicPanel/SeqStepsFmt", count);
            if (sb.Count.Text != countText) sb.Count.Text = countText;
            string status = _engine.SeqStatus(sb.Id);
            if (sb.Status.Text != status) sb.Status.Text = status;
            SetTextColour(sb.Status, _engine.SeqRunning(sb.Id) ? LogicPalette.On : LogicPalette.Dim);
            bool confirm = _confirmSeq == sb.Id && Environment.TickCount - _confirmSeqUntil < 0;
            if (confirm != sb.DelConfirm)
            {
                sb.DelConfirm = confirm;
                LogicGui.SetKey(sb.Delete, confirm ? "ZtxLogicPanel/DeleteSure" : "ZtxLogicPanel/DeleteSeq");
            }
            foreach (Button bt in new[] { sb.TriggerList.Button, sb.Side, sb.Queue, sb.Refusal, sb.Delete }) bt.SelfInputActive = commandable;
            if (sb.AddStep != null) sb.AddStep.SelfInputActive = commandable;
            foreach (Button bt in sb.InsertButtons) bt.SelfInputActive = commandable;
            if (sb.AddDo != null) sb.AddDo.SelfInputActive = commandable && _engine.FreeAction() > 0;
            if (sb.AddBlockList != null) sb.AddBlockList.Button.SelfInputActive = commandable;
            IndentStatements(sb);
            bool running = _engine.SeqRunning(sb.Id);
            int runIndex = _engine.SeqRunIndex(sb.Id);
            if (!IsSeqOpen(sb.Id)) return;
            foreach (StepRow row in sb.Steps) RefreshStep(row, members, commandable, running, runIndex);
        }

        private void RefreshStep(StepRow row, List<Part> members, bool commandable, bool seqRunning, int runIndex)
        {
            bool changed = false;
            for (int f = 0; f < 4; f++)
                if (_engine.RawStep(row.Id, f) != row.LastRaw[f]) changed = true;
            if (_engine.RawStep(row.Id, ZtxRuleEngineComponent.STEP_S) != row.LastRaw[4]) changed = true;
            if (_engine.RawStep(row.Id, ZtxRuleEngineComponent.STEP_V) != row.LastRaw[5]) changed = true;
            if (changed)
            {
                for (int f = 0; f < 4; f++) row.LastRaw[f] = _engine.RawStep(row.Id, f);
                row.LastRaw[4] = _engine.RawStep(row.Id, ZtxRuleEngineComponent.STEP_S);
                row.LastRaw[5] = _engine.RawStep(row.Id, ZtxRuleEngineComponent.STEP_V);
                row.Current = _engine.Step(row.Id);
            }
            SeqStep s = row.Current;
            bool cmd = s.Kind == SeqKind.Command, wait = s.Kind == SeqKind.Wait, until = s.Kind == SeqKind.WaitUntil;
            bool call = s.Kind == SeqKind.Call, mset = s.Kind == SeqKind.MemSet, madd = s.Kind == SeqKind.MemAdd, mem = mset || madd;
            bool counter = mem && IsMemCounterRef(s.Target);
            Part tpart = cmd && _console?.Ship != null ? LogicNet.Resolve(_console.Ship, _engine.Members, s.Target) : null;
            int tkind = LogicAction.KindOf(tpart);
            bool kindChanged = tkind != row.TargetKind;
            if (kindChanged)
            {
                row.TargetKind = tkind;
                row.Lock.BaseTipKey = LockTip(tkind);
                FillKeyed(row.Lock, LockKeys(tkind));
                row.Merge.BaseTipKey = MergeTip(tkind);
                FillKeyed(row.Merge, MergeKeys(tkind));
                LogicGui.SetKey(row.PoseLabel, tkind == 1 ? "ZtxLogicPanel/StepAngle" : "ZtxLogicPanel/StepPos");
                LogicGui.SetKey(row.PoseUnit, tkind == 1 ? "ZtxLogicPanel/UnitDeg" : "ZtxLogicPanel/UnitCells");
            }
            bool piston = cmd && tkind == 2, motor = cmd && tkind == 1;
            bool cart = cmd && tkind == LogicAction.KIND_CART, sw = cmd && tkind == LogicAction.KIND_SWITCH;
            bool dock = cmd && tkind == LogicAction.KIND_DOCK;
            if (cart && !_picker.IsOpenFor(row.Station))
            {
                var items = StationItems(tpart, s.Station, "ZtxLogicPanel/CartStationLeave");
                if (SignatureOf(items) != row.Station.Signature) { Fill(row.Station, items); Select(row.Station, s.Station.Pack()); }
            }
            if (!_picker.IsOpenFor(row.Kind) && (changed || s.Kind != row.LastKind)) Select(row.Kind, (int)s.Kind);
            if (!_picker.IsOpenFor(row.Target))
            {
                List<(int, string)> items = cmd ? TargetItems(members, s.Target, forSteps: true) : call ? CallItems(row.Seq.Block.Rule, s.Target)
                    : mem ? MemItems(s.Target) : VarItems(row.Seq.Block.Rule, s.Target);
                bool rebuilt = false;
                if (SignatureOf(items) != row.Target.Signature) { Fill(row.Target, items); rebuilt = true; }
                if (rebuilt || changed || s.Kind != row.LastKind) Select(row.Target, s.Target.Pack());
            }
            if (cmd && !_picker.IsOpenFor(row.Speed))
            {
                List<(int, string)> items = StepSpeedItems(s);
                bool rebuilt = false;
                if (SignatureOf(items) != row.Speed.Signature) { Fill(row.Speed, items); rebuilt = true; }
                if (rebuilt || changed || kindChanged || s.Kind != row.LastKind) Select(row.Speed, s.Speed);
            }
            if (changed || kindChanged || s.Kind != row.LastKind)
            {
                row.LastKind = s.Kind;
                ShowStepFields(row, s);
            }
            bool flow = s.Kind >= SeqKind.If, cond = s.Kind == SeqKind.If || s.Kind == SeqKind.While;
            row.KindCell.Show(!flow);
            row.MarkerCell.Show(flow && s.Kind != SeqKind.End);
            if (flow)
            {
                string mkey = mset && !s.PoseSet ? "MemReset" : s.Kind.ToString();
                string marker = Strings.GetText("ZtxLogicPanel/Marker" + mkey);
                if (row.Marker.Text != marker) row.Marker.Text = marker;
                StateTip(row.Marker, "ZtxLogicPanel/TipMarker" + mkey);
            }
            row.TargetCell.Show(cmd || until || cond || call || mem);
            row.NotCell.Show(cond || (madd && counter));
            if (madd && counter)
            {
                LogicGui.SetKey(row.NotBtn, s.Not ? "ZtxLogicPanel/MemMinus" : "ZtxLogicPanel/MemPlus");
                SetTextColour(row.NotBtn, LogicPalette.LitValue);
                StateTip(row.NotBtn, s.Not ? "ZtxLogicPanel/TipMemMinus" : "ZtxLogicPanel/TipMemPlus");
                row.NotBtn.SelfInputActive = commandable;
            }
            else if (cond)
            {
                LogicGui.SetKey(row.NotBtn, s.Not ? "ZtxLogicPanel/NotOn" : "ZtxLogicPanel/NotOff");
                SetLit(row.NotBtn, s.Not, LogicPalette.LitNot);
                StateTip(row.NotBtn, s.Not ? "ZtxLogicPanel/TipCondNotOn" : "ZtxLogicPanel/TipCondNotOff");
                row.NotBtn.SelfInputActive = commandable;
            }
            bool amount = s.Kind == SeqKind.Repeat || (counter && (madd || (mset && s.PoseSet)));
            row.TimesCell.Show(amount);
            bool flagValue = mset && s.PoseSet && !counter && s.Target.IsRule;
            row.FlagCell.Show(flagValue);
            if (flagValue)
            {
                LogicGui.SetKey(row.FlagBtn, s.Pose != 0 ? "ZtxLogicPanel/SigOn" : "ZtxLogicPanel/SigOff");
                SetTextColour(row.FlagBtn, s.Pose != 0 ? LogicPalette.On : LogicPalette.Off);
                StateTip(row.FlagBtn, s.Pose != 0 ? "ZtxLogicPanel/TipMemSetOn" : "ZtxLogicPanel/TipMemSetOff");
                row.FlagBtn.SelfInputActive = commandable;
            }
            if (amount && !_editor.IsEditing(row.Times))
            {
                row.Times.Display.ToolTips.TextProvider = Strings.KeyString(s.Kind == SeqKind.Repeat ? "ZtxLogicPanel/TipRepeatTimes" : "ZtxLogicPanel/TipMemAmount");
                string times = (s.Kind == SeqKind.Repeat ? Math.Max(1, s.Pose) : s.Pose).ToString();
                if (row.Times.Text != times) { _refreshing = true; try { row.Times.Text = times; } finally { _refreshing = false; } }
            }
            row.Times.Display.SelfInputActive = commandable;
            bool canDelete = s.Kind != SeqKind.End;
            if (row.Delete.SelfActive != canDelete) row.Delete.SelfActive = canDelete;
            row.NoWaitCell.Show(cmd || call);
            bool drive = piston || motor;
            row.PoseLabelCell.Show(drive); row.PoseCell.Show(drive); row.PoseUnitCell.Show(drive);
            row.SpeedCell.Show(drive);
            row.AccelCell.Show(drive); row.StrengthCell.Show(drive); row.GripCell.Show(piston || cart); row.MergeCell.Show(piston || cart || dock);
            row.LockCell.Show(sw || dock);
            row.StationCell.Show(cart); row.ModeCell.Show(cart);
            foreach (HideCell c in row.CartFieldCells) c.Show(cart);
            foreach (TextCell c in new[] { row.CartSpeedT, row.CartPowerT, row.CartDwellT }) c.Display.SelfInputActive = commandable;
            row.Lock.Button.SelfInputActive = commandable;
            row.Station.Button.SelfInputActive = commandable;
            row.Mode.Button.SelfInputActive = commandable;
            row.SecondsLabelCell.Show(wait); row.SecondsCell.Show(wait);
            row.TimeoutLabelCell.Show(cmd || until || call); row.TimeoutCell.Show(cmd || until || call);
            ShowDetail(row);
            row.NoWait.SelfInputActive = commandable;
            row.Speed.Button.SelfInputActive = commandable;
            row.Kind.Button.SelfInputActive = commandable;
            row.Target.Button.SelfInputActive = commandable;
            int index = IndexInSeq(row);
            bool running = seqRunning && runIndex == index;
            string state = running ? Strings.GetText("ZtxLogicPanel/StepRunning") : "";
            if (row.State.Text != state) row.State.Text = state;
            SetTextColour(row.State, running ? LogicPalette.On : LogicPalette.Dim);
            int number = StatementNumber(row.Seq.Id, row.Id);
            string num = number > 0 ? number + "." : "";
            if (row.Number.Text != num) row.Number.Text = num;
        }

        private void ShowDetail(StepRow row)
        {
            bool any = false;
            foreach (Widget w in row.Detail.Children) if (w.SelfActive) { any = true; break; }
            RuleBlock b = row.Seq.Block;
            bool changed = FitFlow(row.Detail);
            if (any == b.Hidden.Contains(row.Detail))
            {
                if (any) b.Hidden.Remove(row.Detail);
                else b.Hidden.Add(row.Detail);
                bool active = any && IsSeqOpen(row.Seq.Id) && !IsCollapsed(b.Rule);
                if (row.Detail.SelfActive != active) row.Detail.SelfActive = active;
                changed = true;
            }
            if (changed) FitBlock(b);
        }

        private void ShowStepFields(StepRow row, SeqStep s)
        {
            _refreshing = true;
            try
            {
                SetText(row.Pose, s.PoseSet ? (s.Pose / 10f).ToString("0.#", CultureInfo.InvariantCulture) : "");
                SetText(row.Seconds, (s.Seconds / 10f).ToString("0.#", CultureInfo.InvariantCulture));
                SetText(row.Timeout, s.Timeout > 0 ? s.Timeout.ToString(CultureInfo.InvariantCulture) : "");
            }
            finally { _refreshing = false; }
            Select(row.Accel, s.Accel);
            Select(row.Strength, s.Strength);
            Select(row.Grip, s.Grip);
            Select(row.Merge, s.Merge);
            Select(row.Speed, s.Speed);
            Select(row.Lock, s.Lock);
            Select(row.Station, s.Station.Pack());
            Select(row.Mode, s.Pose);
            CartDriveValues(s.Drive, s.Speed, s.Strength, out float? csp, out int? cpw, out float? cdw);
            _refreshing = true;
            try
            {
                SetText(row.CartSpeedT, Num(csp));
                SetText(row.CartPowerT, cpw.HasValue ? cpw.Value.ToString(CultureInfo.InvariantCulture) : "");
                SetText(row.CartDwellT, Num(cdw));
            }
            finally { _refreshing = false; }
            LogicGui.SetKey(row.NoWait, s.NoWait ? "ZtxLogicPanel/StepNoWait" : "ZtxLogicPanel/StepWaits");
            StateTip(row.NoWait, s.NoWait ? "ZtxLogicPanel/TipStepNoWait" : "ZtxLogicPanel/TipStepWaits");
            SetLit(row.NoWait, s.NoWait, LogicPalette.LitValue);
        }

        private List<(int, string)> StepSpeedItems(SeqStep s)
        {
            var l = new List<(int, string)> { (0, Strings.GetText("ZtxLogicPanel/StepSpeedLeave")) };
            Part p = _console?.Ship != null ? LogicNet.Resolve(_console.Ship, _engine.Members, s.Target) : null;
            if (LogicAction.KindOf(p) == LogicAction.KIND_CART) return CartSpeedItems();
            bool motor = LogicAction.KindOf(p) == 1;
            float[] speeds = motor ? ArmToggles.MotorSpeeds(p) : GripService.PistonOf(p)?.Rules.SpeedModes;
            string fmt = motor ? "ZtxLogicPanel/StepSpeedDegFmt" : "ZtxLogicPanel/StepSpeedFmt";
            bool kept = s.Speed == 0;
            if (speeds != null)
                foreach (float v in speeds)
                {
                    int stored = (int)Math.Round(motor ? v : v * 10f);
                    if (stored <= 0 || l.Exists(e => e.Item1 == stored)) continue;
                    if (stored == s.Speed) kept = true;
                    l.Add((stored, Strings.FormatText(fmt, v.ToString("0.##", CultureInfo.InvariantCulture))));
                }
            if (!kept) l.Add((s.Speed, Strings.FormatText(fmt, (motor ? s.Speed : s.Speed / 10f).ToString("0.#", CultureInfo.InvariantCulture))));
            return l;
        }

        private void SetText(TextCell cell, string text)
        {
            if (cell.Text != text && !_editor.IsEditing(cell)) cell.Text = text;
        }

        private List<(int, string)> VarItems(int f, LogicRef keep)
        {
            var l = new List<(int, string)>();
            bool kept = false;
            if (keep.IsNone) { l.Add((0, Strings.GetText("ZtxLogicPanel/InNone"))); kept = true; }
            foreach (int v in ConditionsOf(f))
            {
                LogicRef r = LogicRef.Rule(v);
                if (r.Equals(keep)) kept = true;
                l.Add((r.Pack(), RuleText(v)));
            }
            if (!kept) l.Add((keep.Pack(), Stale(keep)));
            l.Add((NEW_VAR, LogicPalette.Wrap(RoleHex(ButtonRole.Add), Strings.GetText("ZtxLogicPanel/NewVariable"))));
            return l;
        }

        private void OnSeqVarPicked(SeqBlock sb, int value)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                SeqDef def = _engine.Seq(sb.Id);
                if (!def.IsUsed || HandlerRun.NoVariable(def.Trigger)) return;
                var writes = new List<(ID<PartToggleGuiRules>, int)>();
                int owner = 0;
                if (value == NEW_VAR) owner = NewVariable(writes, sb.Block.Rule);
                else
                {
                    LogicRef r = LogicRef.Unpack(value);
                    if (r.IsRule && IsConditionRule(r.Index) && _engine.Rule(r.Index).FunctionOf == sb.Block.Rule) owner = r.Index;
                }
                if (owner == 0 || owner == def.Rule) { Select(sb.VarList, LogicRef.Rule(def.Rule).Pack()); return; }
                writes.Add((_engine.SeqSlot(sb.Id, LogicStore.SEQ_R), owner));
                Write(writes.ToArray());
                Log.Info("console: block " + sb.Id + " now runs on variable " + owner + " (was " + def.Rule + ").");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnSeqVarPicked", ex); }
        }

        private int StatementNumber(int q, int id)
        {
            int n = 0;
            foreach (int k in _engine.SeqStepIds(q))
            {
                SeqKind kind = _engine.Step(k).Kind;
                bool counted = kind != SeqKind.End && kind != SeqKind.Else;
                if (counted) n++;
                if (k == id) return counted ? n : 0;
            }
            return 0;
        }

        private int IndexInSeq(StepRow row)
        {
            IReadOnlyList<int> ids = _engine.SeqStepIds(row.Seq.Id);
            for (int i = 0; i < ids.Count; i++) if (ids[i] == row.Id) return i;
            return -1;
        }

        private void WriteStep(StepRow row, SeqStep s, bool log, List<(ID<PartToggleGuiRules>, int)> extra = null)
        {
            int t = s.Target.Pack(), m = s.PackM(), p = s.PackP(), q = s.PackQ(), st = s.Station.Pack(), drv = s.Drive;
            var writes = extra ?? new List<(ID<PartToggleGuiRules>, int)>();
            if (t != row.LastRaw[0]) writes.Add((_engine.StepSlot(row.Id, ZtxRuleEngineComponent.STEP_T), t));
            if (m != row.LastRaw[1]) writes.Add((_engine.StepSlot(row.Id, ZtxRuleEngineComponent.STEP_M), m));
            if (p != row.LastRaw[2]) writes.Add((_engine.StepSlot(row.Id, ZtxRuleEngineComponent.STEP_P), p));
            if (q != row.LastRaw[3]) writes.Add((_engine.StepSlot(row.Id, ZtxRuleEngineComponent.STEP_Q), q));
            if (st != row.LastRaw[4]) writes.Add((_engine.StepSlot(row.Id, ZtxRuleEngineComponent.STEP_S), st));
            if (drv != row.LastRaw[5]) writes.Add((_engine.StepSlot(row.Id, ZtxRuleEngineComponent.STEP_V), drv));
            row.LastRaw[0] = t; row.LastRaw[1] = m; row.LastRaw[2] = p; row.LastRaw[3] = q; row.LastRaw[4] = st; row.LastRaw[5] = drv;
            row.Current = s;
            if (writes.Count == 0) return;
            Write(writes.ToArray());
            if (log) Log.Info("console: step " + row.Id + " -> " + s + ".");
        }

        private void AddSequence(int rule)
        {
            try
            {
                if (_engine == null || !_engine.IsStoreV2) return;
                int q = _engine.FreeSeqs(1)[0];
                int step = _engine.FreeStep();
                var def = new SeqDef { Rule = rule, Pos = NextOutputPos(rule) };
                var s = new SeqStep { Seq = q, Kind = SeqKind.Wait, Order = 0, Seconds = 10 };
                SetSeqOpen(q, true);
                Write((_engine.SeqSlot(q, LogicStore.SEQ_M), def.PackM()),
                      (_engine.SeqSlot(q, LogicStore.SEQ_R), rule),
                      (_engine.StepSlot(step, ZtxRuleEngineComponent.STEP_T), 0),
                      (_engine.StepSlot(step, ZtxRuleEngineComponent.STEP_M), s.PackM()),
                      (_engine.StepSlot(step, ZtxRuleEngineComponent.STEP_P), s.PackP()),
                      (_engine.StepSlot(step, ZtxRuleEngineComponent.STEP_Q), s.PackQ()),
                      (_engine.StepOwnerSlot(step), q));
                Log.Info("console: sequence " + q + " added to rule " + rule + " (" + def + ").");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.AddSequence", ex); }
        }

        private void OnDeleteSeqClicked(SeqBlock sb)
        {
            if (_confirmSeq == sb.Id && Environment.TickCount - _confirmSeqUntil < 0)
            {
                _confirmSeq = 0;
                DeleteSeq(sb.Id);
                return;
            }
            _confirmSeq = sb.Id;
            _confirmSeqUntil = Environment.TickCount + 3000;
            _forceFull = true;
        }

        private void DeleteSeq(int q)
        {
            try
            {
                if (_engine == null) return;
                var writes = new List<(ID<PartToggleGuiRules>, int)>();
                ClearSeqWrites(writes, q);
                Write(writes.ToArray());
                SetSeqOpen(q, true);
                Log.Info("console: sequence " + q + " deleted.");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.DeleteSeq", ex); }
        }

        private void ClearSeqWrites(List<(ID<PartToggleGuiRules>, int)> writes, int q)
        {
            foreach (int k in _engine.SeqStepIds(q))
            {
                if (_engine.Step(k).Kind == SeqKind.Do) ClearActionWrites(writes, _engine.Step(k).Drive);
                for (int f = 0; f < LogicStore.STEP_FIELDS; f++) writes.Add((_engine.StepSlot(k, f), 0));
            }
            writes.Add((_engine.SeqSlot(q, LogicStore.SEQ_M), 0));
            writes.Add((_engine.SeqSlot(q, LogicStore.SEQ_R), 0));
        }

        private void ToggleSeqBit(int q, int which)
        {
            try
            {
                if (_engine == null) return;
                SeqDef def = _engine.Seq(q);
                if (!def.IsUsed) return;
                if (which == 1) def.QueueOne = !def.QueueOne;
                else def.StopOnRefusal = !def.StopOnRefusal;
                Write((_engine.SeqSlot(q, LogicStore.SEQ_M), def.PackM()));
                Log.Info("console: sequence " + q + " -> " + def + ".");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.ToggleSeqBit", ex); }
        }

        private void SetSeqTrigger(int q, int value)
        {
            try
            {
                if (_engine == null || _refreshing) return;
                SeqDef def = _engine.Seq(q);
                if (!def.IsUsed || (int)def.Trigger == value) return;
                var trigger = (HandlerTrigger)value;
                if (HandlerRun.NoVariable(trigger) == IsConditionRule(def.Rule)) { ReownHandler(q, trigger); return; }
                def.Trigger = trigger;
                Write((_engine.SeqSlot(q, LogicStore.SEQ_M), def.PackM()));
                Log.Info("console: handler " + q + " -> " + def + ".");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.SetSeqTrigger", ex); }
        }

        private void OnSeqKindPicked(int q, int family)
        {
            if (_refreshing || _engine == null || !_engine.Seq(q).IsUsed) return;
            SetSeqTrigger(q, (int)HandlerRun.Compose(family, HandlerRun.IsOff(_engine.Seq(q).Trigger)));
        }

        private void ToggleSeqSide(int q)
        {
            if (_engine == null || !_engine.Seq(q).IsUsed) return;
            HandlerTrigger t = _engine.Seq(q).Trigger;
            if (HandlerRun.NoVariable(t)) return;
            SetSeqTrigger(q, (int)HandlerRun.Compose(HandlerRun.Family(t), !HandlerRun.IsOff(t)));
        }

        private void ReownHandler(int q, HandlerTrigger trigger)
        {
            try
            {
                SeqDef def = _engine.Seq(q);
                int owner = def.Rule, f = OwnerFunction(owner);
                var writes = new List<(ID<PartToggleGuiRules>, int)>();
                def.Rule = HandlerRun.NoVariable(trigger) ? f : DefaultVariable(writes, f, 0);
                if (def.Rule == 0) return;
                def.Trigger = trigger;
                writes.Add((_engine.SeqSlot(q, LogicStore.SEQ_M), def.PackM()));
                writes.Add((_engine.SeqSlot(q, LogicStore.SEQ_R), def.Rule));
                Write(writes.ToArray());
                Log.Info("console: block " + q + " -> " + trigger + ", owned by rule " + def.Rule + (def.Rule != owner ? " (was " + owner + ")" : "") + ".");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.ReownHandler", ex); }
        }

        private void AddDoStatement(int q) => InsertStatement(q, 0, INSERT_SET);

        private void ClearActionWrites(List<(ID<PartToggleGuiRules>, int)> writes, int k)
        {
            if (k < 1) return;
            for (int f = 0; f < 3; f++) writes.Add((_engine.ActionSlot(k, f), 0));
            if (!_engine.IsStoreV2) return;
            writes.Add((_engine.ActionPosSlot(k), 0));
            writes.Add((_engine.ActionSlot(k, ZtxRuleEngineComponent.ACT_S1), 0));
            writes.Add((_engine.ActionSlot(k, ZtxRuleEngineComponent.ACT_S2), 0));
            writes.Add((_engine.ActionSlot(k, ZtxRuleEngineComponent.ACT_V), 0));
        }

        private void ToggleSeqOpen(SeqBlock sb)
        {
            bool open = !IsSeqOpen(sb.Id);
            SetSeqOpen(sb.Id, open);
            bool active = open && !IsCollapsed(sb.Block.Rule);
            foreach (Widget w in sb.Inner)
            {
                if (open) sb.Block.Hidden.Remove(w);
                else sb.Block.Hidden.Add(w);
                if (w.SelfActive != active) w.SelfActive = active;
            }
            foreach (ActRow act in sb.Block.Acts)
            {
                if (act.Seq != sb || IsExpanded(act.Slot)) continue;
                sb.Block.Hidden.Add(act.Settings);
                if (act.Settings.SelfActive) act.Settings.SelfActive = false;
            }
            LogicIcons.Set(sb.Open, open ? LogicIcons.Collapse : LogicIcons.Expand, open ? "ZtxLogicPanel/Collapse" : "ZtxLogicPanel/Expand");
            FitBlock(sb.Block);
            _forceFull = true;
        }

        private void AddStep(int q) => InsertStatement(q, 0, INSERT_WAIT);

        private void DeleteStep(int id)
        {
            try
            {
                if (_engine == null) return;
                var writes = new List<(ID<PartToggleGuiRules>, int)>();
                if (_engine.Step(id).Kind == SeqKind.Do) { ClearActionWrites(writes, _engine.Step(id).Drive); SetExpanded(_engine.Step(id).Drive, false); }
                var ids = new List<int>(_engine.SeqStepIds(_engine.Step(id).Seq));
                var kinds = new List<int>();
                foreach (int k in ids) kinds.Add((int)_engine.Step(k).Kind);
                var gone = new List<int> { id };
                int at = ids.IndexOf(id);
                if (at >= 0 && LogicFlow.IsOpener(kinds[at])) { gone.Clear(); foreach (int i in LogicFlow.Markers(kinds, at)) gone.Add(ids[i]); }
                foreach (int g in gone)
                    for (int f = 0; f < LogicStore.STEP_FIELDS; f++) writes.Add((_engine.StepSlot(g, f), 0));
                Write(writes.ToArray());
                Log.Info("console: step " + id + " deleted.");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.DeleteStep", ex); }
        }

        private void OnStepKindPicked(StepRow row, int value)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                SeqStep s = row.Current;
                if (!s.IsUsed || (int)s.Kind == value) return;
                bool actuatorBefore = s.Kind == SeqKind.Command;
                s.Kind = (SeqKind)value;
                if ((s.Kind == SeqKind.Command) != actuatorBefore) s.Target = LogicRef.None;
                if (s.Kind == SeqKind.Command && s.Timeout == 0) s.Timeout = SeqStep.DEFAULT_COMMAND_TIMEOUT;
                if (s.Kind == SeqKind.WaitUntil) s.Timeout = 0;
                if (s.Kind == SeqKind.Wait && s.Seconds == 0) s.Seconds = 10;
                var extra = new List<(ID<PartToggleGuiRules>, int)>();
                bool hasCond = s.Target.IsRule && IsConditionRule(s.Target.Index);
                if (s.Kind == SeqKind.WaitUntil && !hasCond)
                {
                    int v = DefaultVariable(extra, row.Seq.Block.Rule, _engine.Seq(row.Seq.Id).Rule);
                    if (v == 0) return;
                    s.Target = LogicRef.Rule(v);
                }
                else if (s.Kind != SeqKind.WaitUntil && hasCond) s.Target = LogicRef.None;
                WriteStep(row, s, true, extra);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnStepKindPicked", ex); }
        }

        private void OnStepTargetPicked(StepRow row, int value)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                SeqStep s = row.Current;
                if (!s.IsUsed || value == s.Target.Pack() || value == H_RULES || value == H_LINES) return;
                if (value == NEW_VAR)
                {
                    var extra = new List<(ID<PartToggleGuiRules>, int)>();
                    int v = NewVariable(extra, row.Seq.Block.Rule);
                    if (v == 0) return;
                    s.Target = LogicRef.Rule(v);
                    WriteStep(row, s, true, extra);
                    return;
                }
                int before = s.Kind == SeqKind.Command ? LogicAction.KindOf(LogicNet.Resolve(_console?.Ship, _engine.Members, s.Target)) : 0;
                s.Target = LogicRef.Unpack(value);
                int after = s.Kind == SeqKind.Command ? LogicAction.KindOf(LogicNet.Resolve(_console?.Ship, _engine.Members, s.Target)) : 0;
                if (s.Kind == SeqKind.Command && after != before)
                {
                    s.PoseSet = false;
                    s.Pose = s.Speed = s.Strength = s.Accel = s.Grip = s.Merge = s.Lock = 0;
                    s.Station = LogicRef.None;
                }
                WriteStep(row, s, true);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnStepTargetPicked", ex); }
        }

        private void OnStepNumber(StepRow row, int which)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                SeqStep s = row.Current;
                if (!s.IsUsed) return;
                TextCell cell = which == 0 ? row.Pose : which == 2 ? row.Seconds : row.Timeout;
                string t = (cell.Text ?? "").Trim().Replace(',', '.');
                bool has = float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && v >= 0f;
                switch (which)
                {
                    case 0:
                        s.PoseSet = has;
                        s.Pose = !has ? 0 : row.TargetKind == 1
                            ? Math.Min(ArmToggles.ANGLE_MAX_TENTHS, (int)Math.Round(v * 10f))
                            : Math.Min(SeqStep.POSE_MAX, (int)Math.Round(v, MidpointRounding.AwayFromZero) * 10);
                        break;
                    case 2:
                        s.Seconds = has ? Math.Min(SeqStep.SECONDS_MAX, (int)Math.Round(v * 10f)) : 0;
                        break;
                    default:
                        s.Timeout = has ? Math.Min(SeqStep.TIMEOUT_MAX, (int)Math.Round(v)) : 0;
                        break;
                }
                WriteStep(row, s, false);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnStepNumber", ex); }
        }

        private void OnStepList(StepRow row, int which, int value)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                SeqStep s = row.Current;
                if (!s.IsUsed) return;
                if (which == 0) s.Accel = value;
                else if (which == 1) s.Strength = value;
                else if (which == 2) s.Grip = value;
                else if (which == 3) s.Merge = value;
                else if (which == 5) s.Lock = Math.Max(0, Math.Min(2, value));
                else if (which == 6) s.Pose = Math.Max(0, Math.Min(RailCartRules.MODE_HOLD + 1, value));
                else if (which == 7) s.Station = LogicRef.Unpack(value);
                else if (which == 8) s.Strength = Math.Max(0, Math.Min(RailShipManager.DwellList.Count, value));
                else s.Speed = Math.Max(0, Math.Min(SeqStep.SPEED_MAX, value));
                WriteStep(row, s, true);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnStepList", ex); }
        }

        private void OnStepCartDrive(StepRow row)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                SeqStep s = row.Current;
                if (!s.IsUsed) return;
                float? sp = TryCellNumber(row.CartSpeedT, out float v1) ? v1 : (float?)null;
                int? pw = TryCellNumber(row.CartPowerT, out float v2) ? (int)Math.Round(v2) : (int?)null;
                float? dw = TryCellNumber(row.CartDwellT, out float v3) ? v3 : (float?)null;
                s.Drive = RailCartRules.PackDrive(sp, pw, dw);
                s.Speed = 0;
                s.Strength = 0;
                WriteStep(row, s, false);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnStepCartDrive", ex); }
        }

        private static float BlockIndent => 22f * LogicUiScale.Rules;

        private void IndentStatements(SeqBlock sb)
        {
            var ids = new List<int>(_engine.SeqStepIds(sb.Id));
            var kinds = new List<int>(ids.Count);
            foreach (int k in ids) kinds.Add((int)_engine.Step(k).Kind);
            int[] depth = LogicFlow.Depths(kinds);
            var depthOf = new Dictionary<int, int>();
            for (int i = 0; i < ids.Count; i++) depthOf[ids[i]] = depth[i];
            foreach (StepRow row in sb.Steps)
            {
                int d = depthOf.TryGetValue(row.Id, out int v) ? v : 0;
                if (d == row.LastDepth) continue;
                row.LastDepth = d;
                row.Indent.Width = IndentWidth + d * BlockIndent;
                row.Detail.Children.BorderPadding = new Borders(IndentWidth + GripWidth + StepNumberWidth + d * BlockIndent, 0f, 0f, 0f);
                if (row.AddIndent != null) row.AddIndent.Width = IndentWidth + (d + 1) * BlockIndent + GripWidth + StepNumberWidth;
            }
            foreach (ActRow act in sb.Block.Acts)
            {
                if (act.Seq != sb) continue;
                int d = depthOf.TryGetValue(act.StepId, out int v) ? v : 0;
                if (d == act.LastDepth) continue;
                act.LastDepth = d;
                act.Indent.Width = IndentWidth + d * BlockIndent;
                act.Settings.Children.BorderPadding = new Borders(IndentWidth + GripWidth + StepNumberWidth + d * BlockIndent, 0f, 0f, 0f);
            }
        }

        private void OnAddBlockPicked(SeqBlock sb, int value)
        {
            if (value != 0) AddBlock(sb.Id, value);
            Select(sb.AddBlockList, 0);
        }

        private void AddBlock(int q, int which) => InsertStatement(q, 0, INSERT_IF + which - 1);

        private const int INSERT_SET = 1, INSERT_WAIT = 2, INSERT_UNTIL = 3, INSERT_IF = 4;
        private const int INSERT_CALL = 10, INSERT_MEMSET = 11, INSERT_MEMADD = 12, INSERT_MEMRESET = 13;

        private static List<SeqKind> InsertKinds(int choice)
        {
            switch (choice)
            {
                case INSERT_SET: return new List<SeqKind> { SeqKind.Do };
                case INSERT_WAIT: return new List<SeqKind> { SeqKind.Wait };
                case INSERT_UNTIL: return new List<SeqKind> { SeqKind.WaitUntil };
                case INSERT_IF: return new List<SeqKind> { SeqKind.If, SeqKind.End };
                case INSERT_IF + 1: return new List<SeqKind> { SeqKind.If, SeqKind.Else, SeqKind.End };
                case INSERT_IF + 2: return new List<SeqKind> { SeqKind.Repeat, SeqKind.End };
                case INSERT_IF + 3: return new List<SeqKind> { SeqKind.While, SeqKind.End };
                case INSERT_IF + 4: return new List<SeqKind> { SeqKind.Stop };
                case INSERT_IF + 5: return new List<SeqKind> { SeqKind.Together, SeqKind.End };
                case INSERT_CALL: return new List<SeqKind> { SeqKind.Call };
                case INSERT_MEMSET: return new List<SeqKind> { SeqKind.MemSet };
                case INSERT_MEMADD: return new List<SeqKind> { SeqKind.MemAdd };
                case INSERT_MEMRESET: return new List<SeqKind> { SeqKind.MemSet };
                default: return new List<SeqKind>();
            }
        }

        private void InsertStatement(int q, int before, int choice)
        {
            try
            {
                if (_engine == null || !_engine.IsStoreV2) return;
                SeqDef def = _engine.Seq(q);
                if (!def.IsUsed) return;
                List<SeqKind> kinds = InsertKinds(choice);
                if (kinds.Count == 0) return;
                int memory = FirstMemory();
                var ids = new List<int>(_engine.SeqStepIds(q));
                List<int> fresh = _engine.FreeSteps(kinds.Count);
                int at = before == 0 ? ids.Count : ids.IndexOf(before);
                if (at < 0) at = ids.Count;
                var desired = new List<int>(ids);
                desired.InsertRange(at, fresh);
                var kindOf = new Dictionary<int, SeqKind>();
                foreach (int k in ids) kindOf[k] = _engine.Step(k).Kind;
                for (int i = 0; i < fresh.Count; i++) kindOf[fresh[i]] = kinds[i];
                var check = new List<int>();
                foreach (int k in desired) check.Add((int)kindOf[k]);
                if (!LogicFlow.Valid(check, LogicFlow.MAX_DEPTH)) { Log.Info("console: handler " + q + ": that would nest blocks deeper than " + LogicFlow.MAX_DEPTH + "; nothing added."); return; }
                int action = kinds.Contains(SeqKind.Do) ? _engine.FreeAction() : 0;
                if (kinds.Contains(SeqKind.Do) && action == 0) return;
                int f = OwnerFunction(def.Rule);
                var pre = new List<(ID<PartToggleGuiRules>, int)>();
                int cond = kinds.Exists(IsCondStep) ? DefaultVariable(pre, f, def.Rule) : 0;
                if (kinds.Exists(IsCondStep) && cond == 0) return;
                var writes = new List<(ID<PartToggleGuiRules>, int)>();
                for (int order = 0; order < desired.Count; order++)
                {
                    int id = desired[order];
                    int fi = fresh.IndexOf(id);
                    if (fi < 0)
                    {
                        SeqStep old = _engine.Step(id);
                        if (old.Order == order) continue;
                        old.Order = order;
                        writes.Add((_engine.StepSlot(id, ZtxRuleEngineComponent.STEP_M), old.PackM()));
                        continue;
                    }
                    SeqKind kind = kinds[fi];
                    var s = kind == SeqKind.Wait ? new SeqStep { Seq = q, Kind = SeqKind.Wait, Order = order, Seconds = 10 }
                        : NewStatement(q, kind, order, choice);
                    writes.Add((_engine.StepSlot(id, ZtxRuleEngineComponent.STEP_T), IsCondStep(kind) && cond != 0 ? LogicRef.Rule(cond).Pack()
                        : (kind == SeqKind.MemSet || kind == SeqKind.MemAdd) && memory != 0 ? LogicRef.Rule(memory).Pack() : 0));
                    writes.Add((_engine.StepSlot(id, ZtxRuleEngineComponent.STEP_M), s.PackM()));
                    writes.Add((_engine.StepSlot(id, ZtxRuleEngineComponent.STEP_P), s.PackP()));
                    writes.Add((_engine.StepSlot(id, ZtxRuleEngineComponent.STEP_Q), 0));
                    writes.Add((_engine.StepSlot(id, ZtxRuleEngineComponent.STEP_V), kind == SeqKind.Do ? action : 0));
                    writes.Add((_engine.StepOwnerSlot(id), q));
                }
                if (action != 0)
                {
                    var a = new LogicAction { Rule = f, BlankLeaves = true, Sided = true };
                    writes.Add((_engine.ActionSlot(action, ZtxRuleEngineComponent.ACT_T), 0));
                    writes.Add((_engine.ActionSlot(action, ZtxRuleEngineComponent.ACT_M), a.PackMisc()));
                    writes.Add((_engine.ActionSlot(action, ZtxRuleEngineComponent.ACT_P), 0));
                    OwnAct(writes, action, f);
                    SetExpanded(action, false);
                }
                writes.AddRange(pre);
                Write(writes.ToArray());
                Log.Info("console: " + string.Join(" / ", kinds) + " added to handler " + q + (before == 0 ? " at the end" : " before statement " + before) + ".");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.InsertStatement", ex); }
        }

        private static SeqStep NewStatement(int q, SeqKind kind, int order, int choice)
        {
            var s = new SeqStep { Seq = q, Kind = kind, Order = order, Pose = kind == SeqKind.Repeat ? 2 : kind == SeqKind.MemAdd ? 1 : 0 };
            if (kind == SeqKind.MemSet) s.PoseSet = choice != INSERT_MEMRESET;
            if (kind == SeqKind.Call) s.Timeout = SeqStep.DEFAULT_COMMAND_TIMEOUT;
            return s;
        }

        private void OnStepTimes(StepRow row)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                SeqStep s = row.Current;
                if (!s.IsUsed || (s.Kind != SeqKind.Repeat && s.Kind != SeqKind.MemSet && s.Kind != SeqKind.MemAdd)) return;
                if (!int.TryParse((row.Times.Text ?? "").Trim(), out int n)) return;
                n = s.Kind == SeqKind.Repeat ? Math.Max(1, Math.Min(MAX_REPEAT, n)) : Math.Max(0, Math.Min(SeqStep.POSE_MAX, n));
                if (n == s.Pose) return;
                s.Pose = n;
                WriteStep(row, s, true);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnStepTimes", ex); }
        }

        private const int MAX_REPEAT = 255;

        private void OnStepFlag(StepRow row, int which)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                SeqStep s = row.Current;
                if (!s.IsUsed) return;
                if (which == 0) s.NoWait = !s.NoWait;
                else if (which == 1) s.Not = !s.Not;
                else if (which == 2) s.Pose = s.Pose != 0 ? 0 : 1;
                WriteStep(row, s, true);
                ShowStepFields(row, s);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnStepFlag", ex); }
        }
    }
}
