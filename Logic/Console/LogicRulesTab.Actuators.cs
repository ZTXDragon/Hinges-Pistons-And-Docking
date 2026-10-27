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
        private static float MoreWidth => 100f * LogicUiScale.Rules;
        private static float SetLabelWidth => 44f * LogicUiScale.Rules;
        private static float AccelWidth => 110f * LogicUiScale.Rules;
        private static float StrengthWidth => 120f * LogicUiScale.Rules;
        private static float InvertWidth => 100f * LogicUiScale.Rules;
        private static float ActStateWidth => 160f * LogicUiScale.Rules;
        private static float SummaryWidth => 300f * LogicUiScale.Rules;
        private static float WaitsWidth => 96f * LogicUiScale.Rules;
        private static float SetExtraWidth => 160f * LogicUiScale.Rules;
        private static float SetStateTrim => 50f * LogicUiScale.Rules;
        private static float ChannelWidth => 120f * LogicUiScale.Rules;
        private static float StationWidth => 150f * LogicUiScale.Rules;
        private static float CartModeWidth => 120f * LogicUiScale.Rules;
        private static float CartSpeedWidth => 140f * LogicUiScale.Rules;
        private static float DwellWidth => 110f * LogicUiScale.Rules;
        private const string SetHex = LogicPalette.Set;
        private readonly HashSet<int> _pistonMigrated = new HashSet<int>();
        private static float WhenWidth => 70f * LogicUiScale.Rules;
        private static float HoldWidth => 56f * LogicUiScale.Rules;
        private static float AdapterWidth => 180f * LogicUiScale.Rules;

        private sealed class ActRow
        {
            public int Slot;
            public RuleBlock Block;
            public SeqBlock Seq;
            public int StepId;
            public Widget Indent;
            public int LastDepth = -1;
            public Button WaitsBtn;
            public TextCell SetTimeout;
            public HideCell WaitsCell, SetTimeoutCell;
            public LayoutBox Box;
            public LayoutBox Settings;
            public PickerCell Target;
            public Button More;
            public Button Invert;
            public Label State;
            public Label Summary;
            public Label Number;
            public Label OnUnit, OffUnit, SpeedUnit;
            public TextCell On, Off, Speed;
            public PickerCell Accel, Strength;
            public PickerCell MergeAt;
            public HideCell OnLabelCell, OnCell, OnUnitCell, OffLabelCell, OffCell, OffUnitCell;
            public HideCell SpeedLabelCell, SpeedCell, SpeedUnitCell, AccelCell, StrengthCell;
            public HideCell MergeAtCell;
            public PickerCell Grip;
            public HideCell GripCell;
            public Button Up, Down;
            public PickerCell OnStation, OnMode, OffStation, OffMode;
            public HideCell OnStationCell, OnModeCell, OffStationCell, OffModeCell;
            public TextCell CartSpeedT, CartPowerT, CartDwellT;
            public readonly List<HideCell> CartFieldCells = new List<HideCell>();
            public PickerCell CartMerge;
            public HideCell CartMergeCell;
            public Button When;
            public Button Hold;
            public PickerCell AdapterSet, AdapterVal, LampLight;
            public HideCell AdapterSetCell, AdapterValCell, LampLightCell;
            public int MergeItemsSided = -1;
            public PickerCell DockOnA, DockOnB, DockOffA, DockOffB;
            public HideCell DockOnACell, DockOnBCell, DockOffACell, DockOffBCell;
            public PickerCell[] Bay;
            public HideCell[] BayCells;
            public int BayKind = -1;
            public int DockKind = -1;
            public int[] LastRaw = { int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue };
            public int LastKind = -1;
            public LogicAction Current;
            public bool SettingsBuilt;
        }

        private static readonly ConditionalWeakTable<Part, HashSet<int>> s_expanded = new ConditionalWeakTable<Part, HashSet<int>>();
        private readonly HashSet<int> _migrated = new HashSet<int>();

        private bool IsExpanded(int slot) => _console != null && s_expanded.TryGetValue(_console, out HashSet<int> set) && set.Contains(slot);

        private void SetExpanded(int slot, bool on)
        {
            if (_console == null) return;
            HashSet<int> set = s_expanded.GetOrCreateValue(_console);
            if (on) set.Add(slot);
            else set.Remove(slot);
        }

        private void BuildDoEnd(RuleBlock block)
        {
            LogicRuleDef def = _engine.Rule(block.Rule);
            if (!def.Target.IsPart) return;
            var note = new Label { Width = InnerWidth, Height = RowHeight };
            note.TextRenderer.HAlignment = HAlignment.Left;
            note.TextRenderer.Color = Color.Gray;
            note.TextRenderer.OversizeMode = OversizeMode.Shrink;
            note.Text = Strings.FormatText("ZtxLogicPanel/LegacyTargetFmt", def.Target.ToString());
            AddBody(block, note);
        }

        private ActRow BuildAct(RuleBlock block, int k, SeqBlock seq = null, int stepId = 0)
        {
            var row = new ActRow { Slot = k, Block = block, Seq = seq, StepId = stepId, Box = Row(seq != null ? BlockInnerWidth : InnerWidth), Settings = FlowRow(seq != null ? BlockInnerWidth : InnerWidth) };
            ActRow crow = row;
            row.Indent = new Widget { DockMode = DockMode.Left, Width = IndentWidth };
            row.Box.DockedChildren.Add(row.Indent);
            if (seq != null) AddGrip(row.Box, () => StartStepDrag(crow.Seq, crow.StepId));
            else AddGrip(row.Box, () => StartOutputDrag(crow.Block, LogicOutputStack.DO, crow.Slot));
            if (seq != null)
            {
                row.Number = new Label { DockMode = DockMode.Left, Width = StepNumberWidth };
                row.Box.DockedChildren.Add(row.Number);
                var tag = new Label { DockMode = DockMode.Left, Width = StepKindWidth };
                LogicGui.SetKey(tag, "ZtxLogicPanel/TagSet");
                tag.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipTagSet");
                tag.TextRenderer.HAlignment = HAlignment.Left;
                tag.TextRenderer.OversizeMode = OversizeMode.Shrink;
                ScaleText(tag, HeaderTextScale);
                SetTextColour(tag, LogicPalette.Set);
                row.Box.DockedChildren.Add(tag);
            }
            else row.Box.DockedChildren.Add(OutputTag("ZtxLogicPanel/TagDo", "ZtxLogicPanel/TipTagDo"));
            row.When = MakeButton("ZtxLogicPanel/WhenOn", WhenWidth, ButtonRole.Misc);
            row.When.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipWhen");
            row.When.Clicked += delegate { OnActWhen(crow); };
            row.Box.DockedChildren.Add(row.When);
            row.Hold = MakeButton("ZtxLogicPanel/HoldOff", HoldWidth, ButtonRole.Misc);
            row.Hold.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipHold");
            row.Hold.Clicked += delegate { OnActHold(crow); };
            row.Box.DockedChildren.Add(row.Hold);
            row.Target = MakeList(TargetWidth, "ZtxLogicPanel/TipTarget", v => OnActTargetPicked(crow, v));
            row.Box.DockedChildren.Add(row.Target.Button);
            if (seq != null)
            {
                row.WaitsBtn = MakeButton("ZtxLogicPanel/StepNoWait", WaitsWidth, ButtonRole.Misc);
                row.WaitsBtn.Clicked += delegate { OnSetWaits(crow); };
                row.WaitsCell = Hideable(row.Box, row.WaitsBtn, WaitsWidth);
                row.WaitsCell.KeepSpace = false;
                row.SetTimeout = SetField(SpeedWidth, "ZtxLogicPanel/TipSetTimeout");
                row.SetTimeout.Changed += () => OnSetTimeout(crow);
                row.SetTimeoutCell = Hideable(row.Box, row.SetTimeout.Display, SpeedWidth);
                row.SetTimeoutCell.KeepSpace = false;
            }
            row.More = MakeButton("ZtxLogicPanel/SettingsClosed", MoreWidth, ButtonRole.Misc);
            row.More.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipSettings");
            row.More.Clicked += delegate { ToggleSettings(crow); };
            row.Box.DockedChildren.Add(row.More);
            row.Summary = new Label { DockMode = DockMode.Left, Width = seq != null ? SummaryWidth - SetExtraWidth : SummaryWidth };
            row.Summary.TextRenderer.XmlFormatting = true;
            row.Summary.TextRenderer.HAlignment = HAlignment.Left;
            row.Summary.TextRenderer.OversizeMode = OversizeMode.Shrink;
            row.Summary.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipSummary");
            row.Box.DockedChildren.Add(row.Summary);
            row.State = new Label { DockMode = DockMode.Left, Width = seq != null ? ActStateWidth - SetStateTrim : ActStateWidth };
            row.State.TextRenderer.HAlignment = HAlignment.Center;
            row.State.TextRenderer.OversizeMode = OversizeMode.Shrink;
            row.State.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipActState");
            row.Box.DockedChildren.Add(row.State);
            Button x = LogicIcons.Make(LogicIcons.Close, "ZtxLogicPanel/DeleteLine", SmallButton);
            x.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipDeleteActuator");
            x.Clicked += delegate { if (crow.StepId != 0) DeleteStep(crow.StepId); else DeleteActuator(crow.Slot); };
            row.Box.DockedChildren.Add(x);

            return row;
        }

        private void BuildSettings(ActRow row)
        {
            if (row.SettingsBuilt) return;
            row.SettingsBuilt = true;
            ActRow crow = row;
            row.Settings.Children.BorderPadding = new Borders(row.Seq != null ? IndentWidth + GripWidth + StepNumberWidth + Math.Max(0, row.LastDepth) * BlockIndent : IndentWidth * 2f, 0f, 0f, 0f);
            row.OnLabelCell = Hideable(row.Settings, SetLabel("ZtxLogicPanel/SetOn"), SetLabelWidth);
            row.On = SetField(PoseWidth, "ZtxLogicPanel/TipActOn");
            row.On.Changed += () => OnActPoseChanged(crow, true);
            row.OnCell = Hideable(row.Settings, row.On.Display, PoseWidth);
            row.OnUnit = SetUnit(UnitWidth);
            row.OnUnitCell = Hideable(row.Settings, row.OnUnit, UnitWidth);
            row.OnStation = MakeList(StationWidth, "ZtxLogicPanel/TipCartStation", v => OnActCartPicked(crow, 0, v));
            row.OnStationCell = Hideable(row.Settings, row.OnStation.Button, StationWidth);
            row.OnMode = MakeList(CartModeWidth, "ZtxLogicPanel/TipCartMode", v => OnActCartPicked(crow, 1, v), partHover: false);
            row.OnMode.TipKey = LeaveTip;
            Fill(row.OnMode, CartModeItems());
            row.OnModeCell = Hideable(row.Settings, row.OnMode.Button, CartModeWidth);
            row.DockOnA = MakeList(ChannelWidth, "ZtxLogicPanel/TipDockFirst", v => OnActCartPicked(crow, 8, v), partHover: false);
            row.DockOnACell = Hideable(row.Settings, row.DockOnA.Button, ChannelWidth);
            row.DockOnB = MakeList(ChannelWidth, "ZtxLogicPanel/TipDockSecond", v => OnActCartPicked(crow, 9, v), partHover: false);
            row.DockOnBCell = Hideable(row.Settings, row.DockOnB.Button, ChannelWidth);
            row.Bay = new PickerCell[BayConsole.TERMINAL_CELLS];
            row.BayCells = new HideCell[BayConsole.TERMINAL_CELLS];
            for (int i = 0; i < BayConsole.TERMINAL_CELLS; i++)
            {
                int cell = i;
                row.Bay[i] = MakeList(ChannelWidth, "ZtxLogicPanel/TipBayCell", v => OnActBayPicked(crow, cell, v), partHover: false);
                row.BayCells[i] = Hideable(row.Settings, row.Bay[i].Button, ChannelWidth);
                row.BayCells[i].KeepSpace = false;
            }
            row.AdapterSet = MakeList(AdapterWidth, "ZtxLogicPanel/TipAdapterSetting", v => OnActAdapterPicked(crow, 0, v), partHover: false);
            row.AdapterSetCell = Hideable(row.Settings, row.AdapterSet.Button, AdapterWidth);
            row.AdapterVal = MakeList(AdapterWidth, "ZtxLogicPanel/TipAdapterValue", v => OnActAdapterPicked(crow, 1, v), partHover: false);
            row.AdapterValCell = Hideable(row.Settings, row.AdapterVal.Button, AdapterWidth);
            row.LampLight = MakeList(ChannelWidth, "ZtxLogicPanel/TipLampLight", v => OnActLampPicked(crow, v), partHover: false);
            row.LampLight.TipKey = v => v == 0 ? "ZtxLogicPanel/TipLeave" : "ZtxLogicPanel/TipLampLight_" + v;
            Fill(row.LampLight, LampLightItems());
            row.LampLightCell = Hideable(row.Settings, row.LampLight.Button, ChannelWidth);
            foreach (HideCell c in new[] { row.AdapterSetCell, row.AdapterValCell, row.LampLightCell }) c.KeepSpace = false;
            row.OffLabelCell = Hideable(row.Settings, SetLabel("ZtxLogicPanel/SetOff"), SetLabelWidth);
            row.Off = SetField(PoseWidth, "ZtxLogicPanel/TipActOff");
            row.Off.Changed += () => OnActPoseChanged(crow, false);
            row.OffCell = Hideable(row.Settings, row.Off.Display, PoseWidth);
            row.OffUnit = SetUnit(UnitWidth);
            row.OffUnitCell = Hideable(row.Settings, row.OffUnit, UnitWidth);
            row.OffStation = MakeList(StationWidth, "ZtxLogicPanel/TipCartStation", v => OnActCartPicked(crow, 2, v));
            row.OffStationCell = Hideable(row.Settings, row.OffStation.Button, StationWidth);
            row.OffMode = MakeList(CartModeWidth, "ZtxLogicPanel/TipCartMode", v => OnActCartPicked(crow, 3, v), partHover: false);
            row.OffMode.TipKey = LeaveTip;
            Fill(row.OffMode, CartModeItems());
            row.OffModeCell = Hideable(row.Settings, row.OffMode.Button, CartModeWidth);
            row.DockOffA = MakeList(ChannelWidth, "ZtxLogicPanel/TipDockFirst", v => OnActCartPicked(crow, 10, v), partHover: false);
            row.DockOffACell = Hideable(row.Settings, row.DockOffA.Button, ChannelWidth);
            row.DockOffB = MakeList(ChannelWidth, "ZtxLogicPanel/TipDockSecond", v => OnActCartPicked(crow, 11, v), partHover: false);
            row.DockOffBCell = Hideable(row.Settings, row.DockOffB.Button, ChannelWidth);
            row.CartSpeedT = CartField(row.Settings, row.CartFieldCells, "ZtxLogicPanel/CartSpeedLabel", "ZtxLogicPanel/UnitCellsS", "ZtxLogicPanel/TipCartSpeedText", () => OnActCartDrive(crow));
            row.CartPowerT = CartField(row.Settings, row.CartFieldCells, "ZtxLogicPanel/CartPowerLabel", "ZtxLogicPanel/UnitPercent", "ZtxLogicPanel/TipCartPowerText", () => OnActCartDrive(crow));
            row.CartDwellT = CartField(row.Settings, row.CartFieldCells, "ZtxLogicPanel/CartDwellLabel", "ZtxLogicPanel/UnitSeconds", "ZtxLogicPanel/TipCartDwellText", () => OnActCartDrive(crow));
            row.CartMerge = MakeList(ChannelWidth, "ZtxLogicPanel/TipCartMerge", v => OnActCartPicked(crow, 6, v), partHover: false);
            FillKeyed(row.CartMerge, CartMergeKeys());
            row.CartMergeCell = Hideable(row.Settings, row.CartMerge.Button, ChannelWidth);
            row.SpeedLabelCell = Hideable(row.Settings, SetLabel("ZtxLogicPanel/SetSpeed"), SetLabelWidth);
            row.Speed = SetField(SpeedWidth, "ZtxLogicPanel/TipActSpeed");
            row.Speed.Changed += () => OnActSpeedChanged(crow);
            row.SpeedCell = Hideable(row.Settings, row.Speed.Display, SpeedWidth);
            row.SpeedUnit = SetUnit(SpeedUnitWidth);
            row.SpeedUnitCell = Hideable(row.Settings, row.SpeedUnit, SpeedUnitWidth);
            row.Accel = MakeList(AccelWidth, "ZtxLogicPanel/TipAccel", v => OnActListPicked(crow, true, v), partHover: false);
            Fill(row.Accel, AccelItems());
            row.AccelCell = Hideable(row.Settings, row.Accel.Button, AccelWidth);
            row.Strength = MakeList(StrengthWidth, "ZtxLogicPanel/TipStrength", v => OnActListPicked(crow, false, v), partHover: false);
            Fill(row.Strength, StrengthItems());
            row.StrengthCell = Hideable(row.Settings, row.Strength.Button, StrengthWidth);
            row.Grip = MakeList(ChannelWidth, "ZtxLogicPanel/TipGrip", v => OnActGripPicked(crow, v), partHover: false);
            FillKeyed(row.Grip, new List<(int, string)>
            {
                (0, "GripPart"), (LogicAction.GRIP_ATTACH, "GripAttach"), (LogicAction.GRIP_RELEASE, "GripRelease"), (LogicAction.GRIP_SET_OFF, "GripSetOff"),
            });
            row.GripCell = Hideable(row.Settings, row.Grip.Button, ChannelWidth);
            row.MergeAt = MakeList(ChannelWidth, "ZtxLogicPanel/TipMergeAt", v => OnActMergeAtPicked(crow, v), partHover: false);
            FillKeyed(row.MergeAt, MergeAtKeys(false));
            row.MergeAtCell = Hideable(row.Settings, row.MergeAt.Button, ChannelWidth);
            row.Invert = MakeButton("ZtxLogicPanel/InvertOff", InvertWidth);
            row.Invert.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipInvert");
            row.Invert.Clicked += delegate { OnActInvert(crow); };
            AddCell(row.Settings, row.Invert);
            foreach (HideCell c in new[] { row.OnLabelCell, row.OnCell, row.OnUnitCell, row.OnStationCell, row.OnModeCell, row.DockOnACell, row.DockOnBCell,
                row.OffLabelCell, row.OffCell, row.OffUnitCell, row.OffStationCell, row.OffModeCell, row.DockOffACell, row.DockOffBCell, row.CartMergeCell,
                row.SpeedLabelCell, row.SpeedCell, row.SpeedUnitCell, row.AccelCell, row.StrengthCell, row.GripCell, row.MergeAtCell })
                c.KeepSpace = false;
            row.LastKind = -1;
        }

        private static Label SetLabel(string key)
        {
            var l = new Label { DockMode = DockMode.Left, Width = SetLabelWidth };
            LogicGui.SetKey(l, key);
            l.TextRenderer.HAlignment = HAlignment.Right;
            l.TextRenderer.Color = Color.Gray;
            l.TextRenderer.OversizeMode = OversizeMode.None;
            l.AutoSize.AutoWidthMode = AutoSizeMode.Enable;
            return l;
        }

        private TextCell SetField(float width, string tipKey) => new TextCell(_editor, width, tipKey);

        private static Label SetUnit(float width)
        {
            var l = new Label { DockMode = DockMode.Left, Width = width };
            l.TextRenderer.HAlignment = HAlignment.Left;
            l.TextRenderer.OversizeMode = OversizeMode.None;
            l.AutoSize.AutoWidthMode = AutoSizeMode.Enable;
            return l;
        }

        private static List<(int, string)> StationItems(Part cart, LogicRef keep, string leaveKey)
        {
            var l = new List<(int, string)>
            {
                (0, Strings.GetText(leaveKey)),
                (LogicRef.On.Pack(), Strings.GetText("ZtxRailPanel/Home")),
            };
            bool kept = keep.IsNone || keep.Kind == RefKind.On;
            ZtxRailTerminalComponent terminal = cart != null ? RailShipManager.TerminalFor(cart) : null;
            if (terminal != null)
            {
                foreach (Part s in terminal.Network.Stations)
                {
                    var r = new LogicRef { Kind = RefKind.Part, X = s.Location.X, Y = s.Location.Y };
                    if (r.Equals(keep)) kept = true;
                    l.Add((r.Pack(), RailNetwork.DisplayName(s)));
                }
            }
            if (!kept) l.Add((keep.Pack(), Stale(keep)));
            return l;
        }

        private static List<(int, string)> CartModeItems()
        {
            var l = new List<(int, string)> { (0, Strings.GetText("ZtxLogicPanel/CartModeLeave")) };
            for (int m = 0; m <= RailCartRules.MODE_HOLD; m++) l.Add((m + 1, Strings.FormatText("ZtxLogicPanel/CartModeFmt", RailTerminalWindow.ModeText(m))));
            return l;
        }

        private static List<(int, string)> CartSpeedItems()
        {
            var l = new List<(int, string)> { (0, Strings.GetText("ZtxLogicPanel/CartSpeedLeave")) };
            for (int i = 0; i < RailShipManager.SpeedList.Count; i++)
                l.Add((i + 1, Strings.FormatText("ZtxLogicPanel/CartSpeedFmt", RailShipManager.SpeedList[i].ToString("0.#", CultureInfo.InvariantCulture))));
            return l;
        }

        private static List<(int, string)> CartDwellItems()
        {
            var l = new List<(int, string)> { (0, Strings.GetText("ZtxLogicPanel/CartDwellLeave")) };
            for (int i = 0; i < RailShipManager.DwellList.Count; i++)
                l.Add((i + 1, Strings.FormatText("ZtxLogicPanel/CartDwellFmt", RailShipManager.DwellList[i].ToString("0", CultureInfo.InvariantCulture))));
            return l;
        }

        private static string DockSideText(int kind, bool set, int code, bool on)
        {
            DockToggles.Effective(kind, set, code, on, out int first, out int second);
            var bits = new List<string>();
            if (first != 0) bits.Add(Strings.GetText("ZtxLogicPanel/" + (first == 1 ? "SumDockConnectOn" : "SumDockConnectOff")));
            if (second != 0) bits.Add(Strings.GetText("ZtxLogicPanel/" + (second == 1 ? "SumDockThrustOn" : "SumDockThrustOff")));
            return bits.Count > 0 ? string.Join(", ", bits) : Strings.GetText("ZtxLogicPanel/SumLeave");
        }

        private static List<(int, string)> MergeAtKeys(bool sided)
        {
            if (sided) return new List<(int, string)> { (0, "SideMergeLeave"), (1, "SideMergeOn"), (LogicAction.MERGE_NEVER, "SideMergeOff") };
            return new List<(int, string)> { (0, "MergeAtNever"), (1, "MergeAtOn"), (2, "MergeAtOff"), (3, "MergeAtBoth"), (LogicAction.MERGE_NEVER, "MergeAtNone") };
        }

        private static string LeaveTip(int v) => v == 0 ? "ZtxLogicPanel/TipLeave" : null;

        private static string BayTip(int v) => v == BayConsole.LEAVE ? "ZtxLogicPanel/TipLeave" : v == BayConsole.ON ? "ZtxLogicPanel/TipBayCellOn" : "ZtxLogicPanel/TipBayCellOff";

        private static string OptionTipKey(string key) =>
            key.EndsWith("Leave", StringComparison.Ordinal) || key == "StepGripNone" ? "ZtxLogicPanel/TipLeave" : "ZtxLogicPanel/Tip" + key;

        private static void FillKeyed(PickerCell cell, List<(int Value, string Key)> entries)
        {
            var items = new List<(int, string)>(entries.Count);
            var tips = new Dictionary<int, string>();
            foreach (var (value, key) in entries)
            {
                items.Add((value, Strings.GetText("ZtxLogicPanel/" + key)));
                tips[value] = OptionTipKey(key);
            }
            cell.TipKey = v => tips.TryGetValue(v, out string t) ? t : null;
            Fill(cell, items);
        }

        private static List<(int, string)> BayCellItems(int kind, int cell)
        {
            string name = Strings.GetText("ZtxLogicPanel/" + BayConsole.CellKey(kind, cell));
            return new List<(int, string)>
            {
                (BayConsole.LEAVE, Strings.FormatText("ZtxLogicPanel/BayCellLeaveFmt", name)),
                (BayConsole.ON, Strings.FormatText("ZtxLogicPanel/BayCellOnFmt", name)),
                (BayConsole.OFF, Strings.FormatText("ZtxLogicPanel/BayCellOffFmt", name)),
            };
        }

        private static string BaySideText(int kind, bool set, int code)
        {
            var bits = new List<string>();
            foreach (var (cell, mode) in BayConsole.Plan(kind, set, code))
            {
                string name = Strings.GetText("ZtxLogicPanel/" + BayConsole.CellKey(kind, cell));
                bits.Add(Strings.FormatText(mode != 0 ? "ZtxLogicPanel/BayCellOnFmt" : "ZtxLogicPanel/BayCellOffFmt", name));
            }
            return bits.Count > 0 ? string.Join(", ", bits) : Strings.GetText("ZtxLogicPanel/SumLeave");
        }

        private static List<(int, string)> DockKeys(int kind, bool second)
        {
            if (kind == LogicAction.KIND_BUTTON)
                return new List<(int, string)> { (ButtonToggles.CELL_LEAVE, "StepButtonLeave"), (ButtonToggles.CELL_ON, "StepButtonOn"), (ButtonToggles.CELL_OFF, "StepButtonOff") };
            if (kind == LogicAction.KIND_SWITCH)
                return new List<(int, string)> { (0, "StepSwitchLeave"), (1, "StepSwitchOpen"), (2, "StepSwitchClosed") };
            string leave, on, off;
            if (!second) { leave = "StepDockLeave"; on = "StepDockConnect"; off = "StepDockRelease"; }
            else { leave = "StepThrustLeave"; on = "StepThrustOn"; off = "StepThrustOff"; }
            return new List<(int, string)> { (0, leave), (1, on), (2, off) };
        }

        private void SelectDock(ActRow row, LogicAction a, int kind)
        {
            if (kind == LogicAction.KIND_SWITCH || kind == LogicAction.KIND_BUTTON) { Select(row.DockOnA, a.Sided ? a.OnPose : 0); return; }
            DockToggles.Effective(kind, a.OnSet, a.OnPose, a.Sided ? !a.WhenOff : true, out int onA, out int onB);
            DockToggles.Effective(kind, a.OffSet, a.OffPose, false, out int offA, out int offB);
            Select(row.DockOnA, onA);
            Select(row.DockOnB, onB);
            Select(row.DockOffA, offA);
            Select(row.DockOffB, offB);
        }

        private static List<(int, string)> CartMergeKeys() =>
            new List<(int, string)> { (0, "CartMergeLeave"), (1, "CartMergeOn"), (2, "CartMergeOff") };

        private TextCell CartField(LayoutBox host, List<HideCell> cells, string labelKey, string unitKey, string tipKey, Action changed)
        {
            cells.Add(Hideable(host, SetLabel(labelKey), SetLabelWidth + 10f));
            TextCell t = SetField(SpeedWidth, tipKey);
            t.Changed += () => changed();
            cells.Add(Hideable(host, t.Display, SpeedWidth));
            Label u = SetUnit(UnitWidth);
            LogicGui.SetKey(u, unitKey);
            cells.Add(Hideable(host, u, UnitWidth));
            foreach (HideCell c in cells) c.KeepSpace = false;
            return t;
        }

        internal static void CartDriveValues(int drive, int legacySpeed, int legacyDwell, out float? speed, out int? power, out float? dwell)
        {
            if (drive != 0) { RailCartRules.UnpackDrive(drive, out speed, out power, out dwell); return; }
            speed = legacySpeed > 0 ? RailCartRules.OLD_SPEEDS[Math.Min(RailCartRules.OLD_SPEEDS.Length - 1, legacySpeed - 1)] : (float?)null;
            power = null;
            dwell = legacyDwell > 0 ? RailCartRules.OLD_DWELLS[Math.Min(RailCartRules.OLD_DWELLS.Length - 1, legacyDwell - 1)] : (float?)null;
        }

        private static string Num(float? v) => v.HasValue ? v.Value.ToString("0.#", CultureInfo.InvariantCulture) : "";

        private static bool TryCellNumber(TextCell c, out float v)
        {
            string t = (c?.Text ?? "").Trim().Replace(',', '.');
            return float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && t.Length > 0;
        }

        private static string CartSideText(Part cart, LogicRef station, int modePlusOne)
        {
            string mode = modePlusOne > 0 ? RailTerminalWindow.ModeText(modePlusOne - 1) : "";
            string st = station.Kind == RefKind.On ? Strings.GetText("ZtxRailPanel/Home")
                : station.IsPart ? (RailShipManager.StationPart(cart, station) is Part s ? RailNetwork.DisplayName(s) : station + " ?") : "";
            string t = (mode + " " + st).Trim();
            return t.Length > 0 ? t : Strings.GetText("ZtxLogicPanel/SumLeave");
        }

        private static List<(int, string)> AccelItems()
        {
            return new List<(int, string)>
            {
                (0, Strings.GetText("ZtxLogicPanel/AccelPart")),
                (1, Strings.GetText("ZtxLogicPanel/AccelInstant")),
                (2, Strings.GetText("ZtxLogicPanel/AccelSmooth")),
            };
        }

        private static List<(int, string)> StrengthItems()
        {
            var l = new List<(int, string)> { (0, Strings.GetText("ZtxLogicPanel/StrengthPart")) };
            for (int s = 1; s <= LogicAction.STRENGTH_STEPS; s++) l.Add((s, Strings.FormatText("ZtxLogicPanel/StrengthFmt", (s - 1) * 10)));
            return l;
        }

        private void RefreshDo(RuleBlock b, List<Part> members, bool commandable)
        {
            if (b.AddAct != null) b.AddAct.SelfInputActive = commandable && _engine.FreeAction() > 0;
            foreach (ActRow row in b.Acts) RefreshAct(row, members, commandable);
        }

        private void RefreshAct(ActRow row, List<Part> members, bool commandable)
        {
            bool changed = false;
            for (int f = 0; f < 3; f++)
                if (_engine.RawAction(row.Slot, f) != row.LastRaw[f]) changed = true;
            if (_engine.RawAction(row.Slot, ZtxRuleEngineComponent.ACT_S1) != row.LastRaw[3]
                || _engine.RawAction(row.Slot, ZtxRuleEngineComponent.ACT_S2) != row.LastRaw[4]
                || _engine.RawAction(row.Slot, ZtxRuleEngineComponent.ACT_V) != row.LastRaw[5]) changed = true;
            if (changed)
            {
                for (int f = 0; f < 3; f++) row.LastRaw[f] = _engine.RawAction(row.Slot, f);
                row.LastRaw[3] = _engine.RawAction(row.Slot, ZtxRuleEngineComponent.ACT_S1);
                row.LastRaw[4] = _engine.RawAction(row.Slot, ZtxRuleEngineComponent.ACT_S2);
                row.LastRaw[5] = _engine.RawAction(row.Slot, ZtxRuleEngineComponent.ACT_V);
                row.Current = LogicAction.UnpackFull(row.LastRaw[0], row.LastRaw[1], row.LastRaw[2], row.LastRaw[3], row.LastRaw[4], row.LastRaw[5]);
            }
            LogicAction a = row.Current;
            Part tp = LogicNet.Resolve(_console.Ship, _engine.Members, a.Target);
            int kind = LogicAction.KindOf(tp);
            if (!_picker.IsOpenFor(row.Target))
            {
                var items = TargetItems(members, a.Target);
                bool rebuilt = false;
                if (SignatureOf(items) != row.Target.Signature) { Fill(row.Target, items); rebuilt = true; }
                if (rebuilt || changed) Select(row.Target, a.Target.Pack());
            }
            if (changed || kind != row.LastKind)
            {
                row.LastKind = kind;
                ShowActSettings(row, a, kind);
            }
            bool piston = kind == 2;
            bool pos = kind == 1 || kind == 2, drive = kind == 1 || kind == 2;
            bool cart = kind == LogicAction.KIND_CART;
            if (cart && row.SettingsBuilt)
            {
                foreach (var (cell, keep) in new[] { (row.OnStation, a.OnStation), (row.OffStation, a.OffStation) })
                {
                    if (_picker.IsOpenFor(cell)) continue;
                    var items = StationItems(tp, keep, "ZtxLogicPanel/CartStationLeave");
                    if (SignatureOf(items) != cell.Signature) { Fill(cell, items); Select(cell, keep.Pack()); }
                }
            }
            row.Target.Button.SelfInputActive = commandable;
            row.More.SelfInputActive = commandable;
            bool sides = a.Sided && row.StepId == 0;
            if (row.When.SelfActive != sides) row.When.SelfActive = sides;
            row.When.SelfInputActive = commandable;
            LogicGui.SetKey(row.When, a.WhenOff ? "ZtxLogicPanel/WhenOff" : "ZtxLogicPanel/WhenOn");
            StateTip(row.When, a.WhenOff ? "ZtxLogicPanel/TipWhenOff" : "ZtxLogicPanel/TipWhenOn");
            SetLit(row.When, a.Sided && !a.WhenOff, LogicPalette.LitValue);
            if (row.Hold.SelfActive != sides) row.Hold.SelfActive = sides;
            if (row.StepId != 0) RefreshSetWaits(row, kind, commandable);
            if (row.Number != null)
            {
                int n = StatementNumber(row.Seq.Id, row.StepId);
                string num = n > 0 ? n + "." : "";
                if (row.Number.Text != num) row.Number.Text = num;
            }
            row.Hold.SelfInputActive = commandable;
            LogicGui.SetKey(row.Hold, a.Hold ? "ZtxLogicPanel/HoldOn" : "ZtxLogicPanel/HoldOff");
            StateTip(row.Hold, a.Hold ? "ZtxLogicPanel/TipHoldHold" : "ZtxLogicPanel/TipHoldOnce");
            SetLit(row.Hold, a.Hold, LogicPalette.LitNot);
            SetLit(row.Target.Button, !a.Target.IsNone, LogicPalette.LitValue);
            if (row.SettingsBuilt)
            {
                bool dockKind = kind == LogicAction.KIND_DOCK;
                bool sided = a.Sided, sw = kind == LogicAction.KIND_SWITCH || kind == LogicAction.KIND_BUTTON;
                row.OnLabelCell.Show((pos || cart || dockKind) && !sided);
                row.OnCell.Show(pos);
                row.OnUnitCell.Show(pos);
                row.OffLabelCell.Show((pos || cart || dockKind) && !sided);
                foreach (HideCell c in new[] { row.OnStationCell, row.OnModeCell, row.CartMergeCell }) c.Show(cart);
                foreach (HideCell c in new[] { row.OffStationCell, row.OffModeCell }) c.Show(cart && !sided);
                foreach (HideCell c in row.CartFieldCells) c.Show(cart);
                foreach (PickerCell c in new[] { row.OnStation, row.OnMode, row.OffStation, row.OffMode, row.CartMerge }) c.Button.SelfInputActive = commandable;
                foreach (TextCell c in new[] { row.CartSpeedT, row.CartPowerT, row.CartDwellT }) c.Display.SelfInputActive = commandable;
                row.OffCell.Show(pos && !sided);
                row.OffUnitCell.Show(pos && !sided);
                row.SpeedLabelCell.Show(drive);
                row.SpeedCell.Show(drive);
                row.SpeedUnitCell.Show(drive);
                row.AccelCell.Show(drive);
                row.StrengthCell.Show(drive);
                int mergeSided = sided ? 1 : 0;
                if (piston && row.MergeItemsSided != mergeSided)
                {
                    row.MergeItemsSided = mergeSided;
                    FillKeyed(row.MergeAt, MergeAtKeys(sided));
                    Select(row.MergeAt, a.MergeAt);
                }
                row.MergeAtCell.Show(piston);
                row.MergeAt.Button.SelfInputActive = commandable;
                row.GripCell.Show(piston || cart);
                row.Grip.Button.SelfInputActive = commandable;
                if ((dockKind || sw) && row.DockKind != kind)
                {
                    row.DockKind = kind;
                    FillKeyed(row.DockOnA, DockKeys(kind, false));
                    FillKeyed(row.DockOnB, DockKeys(kind, true));
                    FillKeyed(row.DockOffA, DockKeys(kind, false));
                    FillKeyed(row.DockOffB, DockKeys(kind, true));
                    SelectDock(row, a, kind);
                }
                row.DockOnACell.Show(dockKind || (sw && sided));
                row.DockOnBCell.Show(dockKind);
                row.DockOffACell.Show(dockKind && !sided);
                row.DockOffBCell.Show(dockKind && !sided);
                foreach (PickerCell c in new[] { row.DockOnA, row.DockOnB, row.DockOffA, row.DockOffB }) c.Button.SelfInputActive = commandable;
                bool bayKind = kind == LogicAction.KIND_BAY || kind == LogicAction.KIND_BAY_STATION;
                if (bayKind && row.BayKind != kind)
                {
                    row.BayKind = kind;
                    for (int i = 0; i < BayConsole.CellCount(kind); i++) { row.Bay[i].TipKey = BayTip; Fill(row.Bay[i], BayCellItems(kind, i)); }
                }
                bool adapterKind = kind == LogicAction.KIND_ADAPTER, lampKind = kind == LogicAction.KIND_LAMP;
                row.AdapterSetCell.Show(adapterKind && sided);
                row.AdapterValCell.Show(adapterKind && sided);
                row.LampLightCell.Show(lampKind && sided);
                foreach (PickerCell c in new[] { row.AdapterSet, row.AdapterVal, row.LampLight }) c.Button.SelfInputActive = commandable;
                if (adapterKind && !_picker.IsOpenFor(row.AdapterSet) && !_picker.IsOpenFor(row.AdapterVal))
                {
                    var setItems = AdapterSettingItems(tp, a.Drive);
                    if (SignatureOf(setItems) != row.AdapterSet.Signature) Fill(row.AdapterSet, setItems);
                    Select(row.AdapterSet, a.Drive);
                    var valItems = AdapterValueItems(tp, a.Drive, a.OnSet ? a.OnPose : 0);
                    if (SignatureOf(valItems) != row.AdapterVal.Signature) Fill(row.AdapterVal, valItems);
                    Select(row.AdapterVal, a.OnSet ? a.OnPose : 0);
                }
                if (lampKind)
                {
                    Select(row.LampLight, a.OnSet ? a.OnPose & 7 : 0);
                }
                if (FitFlow(row.Settings)) FitBlock(row.Block);
                for (int i = 0; i < BayConsole.TERMINAL_CELLS; i++)
                {
                    row.BayCells[i].Show(bayKind && sided && i < BayConsole.CellCount(kind));
                    row.Bay[i].Button.SelfInputActive = commandable;
                }
                row.On.Display.SelfInputActive = commandable;
                row.Off.Display.SelfInputActive = commandable;
                row.Speed.Display.SelfInputActive = commandable;
                row.Accel.Button.SelfInputActive = commandable;
                row.Strength.Button.SelfInputActive = commandable;
                row.Invert.SelfInputActive = commandable;
                if (row.Invert.SelfActive != !sided) row.Invert.SelfActive = !sided;
                SetLit(row.Invert, a.Invert, LogicPalette.LitNot);
            }
            string summary = tp != null ? SettingsSummary(tp, kind, a) : "";
            if (row.Summary.Text != summary) row.Summary.Text = summary;
            string state = "";
            if (tp != null)
            {
                LogicActuatorState.IsDone(tp, out string why);
                state = why;
            }
            else if (!a.Target.IsNone) state = "?";
            if (row.State.Text != state) row.State.Text = state;
        }

        internal static string SettingsSummary(Part tp, int kind, LogicAction a)
        {
            if (a.Sided) return SidedSummary(tp, kind, a);
            var parts = new List<string>();
            switch (kind)
            {
                case 1:
                    parts.Add(Term("ZtxLogicPanel/SumOnFmt", a.OnSet ? Deg(a.OnPose) : Strings.GetText("ZtxLogicPanel/SumLeave"), a.OnSet));
                    parts.Add(Term("ZtxLogicPanel/SumOffFmt", a.OffSet ? Deg(a.OffPose) : Strings.GetText("ZtxLogicPanel/SumLeave"), a.OffSet));
                    break;
                case 2:
                    parts.Add(Term("ZtxLogicPanel/SumOnFmt", a.OnSet ? Cells(a.OnPose / 10f) : Strings.GetText("ZtxLogicPanel/SumLeave"), a.OnSet));
                    parts.Add(Term("ZtxLogicPanel/SumOffFmt", a.OffSet ? Cells(a.OffPose / 10f) : Strings.GetText("ZtxLogicPanel/SumLeave"), a.OffSet));
                    break;
                case 3:
                    parts.Add(Term("ZtxLogicPanel/SumOnFmt", Strings.GetText("ZtxLogicPanel/SumFree"), false));
                    parts.Add(Term("ZtxLogicPanel/SumOffFmt", Strings.GetText("ZtxLogicPanel/SumFree"), false));
                    break;
                case LogicAction.KIND_CART:
                {
                    bool onSet = a.OnPose > 0 || !a.OnStation.IsNone, offSet = a.OffPose > 0 || !a.OffStation.IsNone;
                    parts.Add(Term("ZtxLogicPanel/SumOnFmt", CartSideText(tp, a.OnStation, a.OnPose), onSet));
                    parts.Add(Term("ZtxLogicPanel/SumOffFmt", CartSideText(tp, a.OffStation, a.OffPose), offSet));
                    CartDriveValues(a.Drive, a.Speed, a.Strength, out float? sp, out int? pw, out float? dw);
                    if (sp.HasValue) parts.Add(Term("ZtxLogicPanel/SumCartSpeedFmt", Num(sp), true));
                    if (pw.HasValue) parts.Add(Term("ZtxLogicPanel/SumCartPowerFmt", pw.Value.ToString(CultureInfo.InvariantCulture), true));
                    if (dw.HasValue) parts.Add(Term("ZtxLogicPanel/SumCartDwellFmt", Num(dw), true));
                    if (a.MergeAt == 1 || a.MergeAt == 2)
                        parts.Add("<color hex=\"" + SetHex + "\">" + Strings.GetText(a.MergeAt == 1 ? "ZtxLogicPanel/SumCartMergeOn" : "ZtxLogicPanel/SumCartMergeOff") + "</color>");
                    break;
                }
                case LogicAction.KIND_SWITCH:
                    parts.Add(Term("ZtxLogicPanel/SumOnFmt", Strings.GetText("ZtxLogicPanel/SumOpen"), false));
                    parts.Add(Term("ZtxLogicPanel/SumOffFmt", Strings.GetText("ZtxLogicPanel/SumClosed"), false));
                    break;
                case LogicAction.KIND_DOCK:
                {
                    parts.Add(Term("ZtxLogicPanel/SumOnFmt", DockSideText(kind, a.OnSet, a.OnPose, true), a.OnSet));
                    parts.Add(Term("ZtxLogicPanel/SumOffFmt", DockSideText(kind, a.OffSet, a.OffPose, false), a.OffSet));
                    break;
                }
                case LogicAction.KIND_BAY:
                case LogicAction.KIND_BAY_STATION:
                    parts.Add(Term("ZtxLogicPanel/SumOnFmt", BaySideText(kind, a.OnSet, a.OnPose), a.OnSet));
                    parts.Add(Term("ZtxLogicPanel/SumOffFmt", BaySideText(kind, a.OffSet, a.OffPose), a.OffSet));
                    break;
            }
            if (kind == 2 && a.MergeAt != 0)
                parts.Add("<color hex=\"" + SetHex + "\">" + Strings.GetText(a.MergeAt == 1 ? "ZtxLogicPanel/MergeAtOn" : a.MergeAt == 2 ? "ZtxLogicPanel/MergeAtOff" : a.MergeAt == LogicAction.MERGE_NEVER ? "ZtxLogicPanel/MergeAtNone" : "ZtxLogicPanel/MergeAtBoth") + "</color>");
            if ((kind == 2 || kind == LogicAction.KIND_CART) && a.Grip != 0)
                parts.Add("<color hex=\"" + SetHex + "\">" + Strings.GetText(a.Grip == LogicAction.GRIP_ATTACH ? "ZtxLogicPanel/GripAttach" : a.Grip == LogicAction.GRIP_SET_OFF ? "ZtxLogicPanel/GripSetOff" : "ZtxLogicPanel/GripRelease") + "</color>");
            if (a.Invert) parts.Add("<color hex=\"" + SetHex + "\">" + Strings.GetText("ZtxLogicPanel/SumInverted") + "</color>");
            return string.Join("  |  ", parts);
        }

        private static string SidedSummary(Part tp, int kind, LogicAction a)
        {
            var parts = new List<string>();
            string fmt = a.WhenOff ? "ZtxLogicPanel/SumOffFmt" : "ZtxLogicPanel/SumOnFmt";
            string leave = Strings.GetText("ZtxLogicPanel/SumLeave");
            switch (kind)
            {
                case 1: parts.Add(Term(fmt, a.OnSet ? Deg(a.OnPose) : leave, a.OnSet)); break;
                case 2: parts.Add(Term(fmt, a.OnSet ? Cells(a.OnPose / 10f) : leave, a.OnSet)); break;
                case 3: parts.Add(Term(fmt, Strings.GetText("ZtxLogicPanel/SumFree"), false)); break;
                case LogicAction.KIND_CART:
                {
                    parts.Add(Term(fmt, CartSideText(tp, a.OnStation, a.OnPose), a.OnPose > 0 || !a.OnStation.IsNone));
                    CartDriveValues(a.Drive, a.Speed, a.Strength, out float? sp, out int? pw, out float? dw);
                    if (sp.HasValue) parts.Add(Term("ZtxLogicPanel/SumCartSpeedFmt", Num(sp), true));
                    if (pw.HasValue) parts.Add(Term("ZtxLogicPanel/SumCartPowerFmt", pw.Value.ToString(CultureInfo.InvariantCulture), true));
                    if (dw.HasValue) parts.Add(Term("ZtxLogicPanel/SumCartDwellFmt", Num(dw), true));
                    if (a.MergeAt == 1 || a.MergeAt == 2)
                        parts.Add("<color hex=\"" + SetHex + "\">" + Strings.GetText(a.MergeAt == 1 ? "ZtxLogicPanel/SumCartMergeOn" : "ZtxLogicPanel/SumCartMergeOff") + "</color>");
                    break;
                }
                case LogicAction.KIND_SWITCH:
                    parts.Add(Term(fmt, a.OnPose == 1 ? Strings.GetText("ZtxLogicPanel/SumOpen") : a.OnPose == 2 ? Strings.GetText("ZtxLogicPanel/SumClosed") : leave, a.OnPose != 0));
                    break;
                case LogicAction.KIND_ADAPTER:
                    parts.Add(Term(fmt, AdapterSideText(tp, a), a.OnSet));
                    break;
                case LogicAction.KIND_LAMP:
                    parts.Add(Term(fmt, LampSideText(a), a.OnSet));
                    break;
                case LogicAction.KIND_BUTTON:
                    parts.Add(Term(fmt, a.OnPose == ButtonToggles.CELL_ON ? Strings.GetText("ZtxLogicPanel/SumButtonOn") : a.OnPose == ButtonToggles.CELL_OFF ? Strings.GetText("ZtxLogicPanel/SumButtonOff") : leave, a.OnPose != 0));
                    break;
                case LogicAction.KIND_DOCK:
                    parts.Add(Term(fmt, DockSideText(kind, a.OnSet, a.OnPose, !a.WhenOff), a.OnSet));
                    break;
                case LogicAction.KIND_BAY:
                case LogicAction.KIND_BAY_STATION:
                    parts.Add(Term(fmt, BaySideText(kind, a.OnSet, BayConsole.CodeOf(a.OnPose, a.OffPose)), a.OnSet));
                    break;
                default:
                    parts.Add(Term(fmt, Strings.GetText(a.WhenOff ? "ZtxLogicPanel/SumSignalOff" : "ZtxLogicPanel/SumSignalOn"), false));
                    break;
            }
            if (kind == 2 && a.MergeAt != 0)
                parts.Add("<color hex=\"" + SetHex + "\">" + Strings.GetText(a.MergeAt == 1 ? "ZtxLogicPanel/SideMergeOn" : "ZtxLogicPanel/SideMergeOff") + "</color>");
            if ((kind == 2 || kind == LogicAction.KIND_CART) && a.Grip != 0)
                parts.Add("<color hex=\"" + SetHex + "\">" + Strings.GetText(a.Grip == LogicAction.GRIP_ATTACH ? "ZtxLogicPanel/GripAttach" : a.Grip == LogicAction.GRIP_SET_OFF ? "ZtxLogicPanel/GripSetOff" : "ZtxLogicPanel/GripRelease") + "</color>");
            return string.Join("  |  ", parts);
        }

        private static string Term(string fmtKey, string value, bool set)
        {
            string text = Strings.FormatText(fmtKey, value);
            return set ? "<color hex=\"" + SetHex + "\">" + text + "</color>" : text;
        }

        private static string Deg(int degrees) => Strings.FormatText("ZtxLogicPanel/SumDegFmt", degrees);

        private static string Cells(float cells)
        {
            string n = cells.ToString("0.#", CultureInfo.InvariantCulture);
            return Strings.FormatText(n == "1" ? "ZtxLogicPanel/SumCellFmt" : "ZtxLogicPanel/SumCellsFmt", n);
        }

        private void ShowActSettings(ActRow row, LogicAction a, int kind)
        {
            if (!row.SettingsBuilt) { ShowMore(row, a); return; }
            _refreshing = true;
            try
            {
                string on = a.OnSet && kind != 0 ? ActPoseText(kind, a.OnPose) : "";
                string off = a.OffSet && kind != 0 ? ActPoseText(kind, a.OffPose) : "";
                string speed = a.Speed > 0 && (kind == 1 || kind == 2) ? RawText(kind, a.Speed) : "";
                if (row.On.Text != on && !_editor.IsEditing(row.On)) row.On.Text = on;
                if (row.Off.Text != off && !_editor.IsEditing(row.Off)) row.Off.Text = off;
                if (row.Speed.Text != speed && !_editor.IsEditing(row.Speed)) row.Speed.Text = speed;
            }
            finally { _refreshing = false; }
            Select(row.Accel, a.Accel);
            Select(row.Strength, a.Strength);
            Select(row.MergeAt, a.MergeAt);
            Select(row.Grip, a.Grip);
            Select(row.OnStation, a.OnStation.Pack());
            Select(row.OnMode, a.OnPose);
            Select(row.OffStation, a.OffStation.Pack());
            Select(row.OffMode, a.OffPose);
            CartDriveValues(a.Drive, a.Speed, a.Strength, out float? csp, out int? cpw, out float? cdw);
            _refreshing = true;
            try
            {
                if (!_editor.IsEditing(row.CartSpeedT)) row.CartSpeedT.Text = Num(csp);
                if (!_editor.IsEditing(row.CartPowerT)) row.CartPowerT.Text = cpw.HasValue ? cpw.Value.ToString(CultureInfo.InvariantCulture) : "";
                if (!_editor.IsEditing(row.CartDwellT)) row.CartDwellT.Text = Num(cdw);
            }
            finally { _refreshing = false; }
            Select(row.CartMerge, a.MergeAt);
            if (kind == LogicAction.KIND_DOCK || kind == LogicAction.KIND_SWITCH || kind == LogicAction.KIND_BUTTON) SelectDock(row, a, kind);
            if (kind == LogicAction.KIND_BAY || kind == LogicAction.KIND_BAY_STATION)
            {
                int code = a.Sided ? BayConsole.CodeOf(a.OnPose, a.OffPose) : a.OnPose;
                for (int i = 0; i < BayConsole.CellCount(kind); i++) Select(row.Bay[i], a.OnSet ? BayConsole.Digit(code, i) : BayConsole.LEAVE);
            }
            string unit =kind == 2 ? Strings.GetText("ZtxLogicPanel/UnitCells") : kind != 0 ? Strings.GetText("ZtxLogicPanel/UnitDeg") : "";
            if (row.OnUnit.Text != unit) row.OnUnit.Text = unit;
            if (row.OffUnit.Text != unit) row.OffUnit.Text = unit;
            string sunit = kind == 2 ? Strings.GetText("ZtxLogicPanel/UnitCellsS") : kind == 1 ? Strings.GetText("ZtxLogicPanel/UnitDegS") : "";
            if (row.SpeedUnit.Text != sunit) row.SpeedUnit.Text = sunit;
            LogicGui.SetKey(row.Invert, a.Invert ? "ZtxLogicPanel/InvertOn" : "ZtxLogicPanel/InvertOff");
            StateTip(row.Invert, a.Invert ? "ZtxLogicPanel/TipInvertOn" : "ZtxLogicPanel/TipInvertOff");
            ShowMore(row, a);
        }

        private void RefreshSetWaits(ActRow row, int kind, bool commandable)
        {
            SeqStep st = _engine.Step(row.StepId);
            bool can = LogicSetStatement.CanWait(kind);
            row.WaitsCell.Show(can);
            row.SetTimeoutCell.Show(can && st.Settle);
            if (!can) return;
            LogicGui.SetKey(row.WaitsBtn, st.Settle ? "ZtxLogicPanel/StepWaits" : "ZtxLogicPanel/StepNoWait");
            StateTip(row.WaitsBtn, st.Settle ? "ZtxLogicPanel/TipSetWaits" : "ZtxLogicPanel/TipSetNoWait");
            SetLit(row.WaitsBtn, st.Settle, LogicPalette.LitValue);
            row.WaitsBtn.SelfInputActive = commandable;
            row.SetTimeout.Display.SelfInputActive = commandable;
            string t = st.Timeout > 0 ? st.Timeout.ToString(CultureInfo.InvariantCulture) : "";
            if (row.SetTimeout.Text != t && !_editor.IsEditing(row.SetTimeout))
            {
                _refreshing = true;
                try { row.SetTimeout.Text = t; }
                finally { _refreshing = false; }
            }
        }

        private void OnSetWaits(ActRow row)
        {
            if (_refreshing || _engine == null || row.StepId == 0) return;
            try
            {
                SeqStep st = _engine.Step(row.StepId);
                if (!st.IsUsed) return;
                st.Settle = !st.Settle;
                if (st.Settle && st.Timeout == 0) st.Timeout = SeqStep.DEFAULT_COMMAND_TIMEOUT;
                Write((_engine.StepSlot(row.StepId, ZtxRuleEngineComponent.STEP_M), st.PackM()), (_engine.StepSlot(row.StepId, ZtxRuleEngineComponent.STEP_P), st.PackP()));
                Log.Info("console: Set statement " + row.StepId + (st.Settle ? " waits for its part (timeout " + st.Timeout + " s)." : " does not wait."));
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnSetWaits", ex); }
        }

        private void OnSetTimeout(ActRow row)
        {
            if (_refreshing || _engine == null || row.StepId == 0) return;
            try
            {
                SeqStep st = _engine.Step(row.StepId);
                if (!st.IsUsed) return;
                string text = (row.SetTimeout.Text ?? "").Trim();
                int t = 0;
                if (text.Length > 0 && !int.TryParse(text, out t)) return;
                t = Math.Max(0, Math.Min(SeqStep.TIMEOUT_MAX, t));
                if (t == st.Timeout) return;
                st.Timeout = t;
                Write((_engine.StepSlot(row.StepId, ZtxRuleEngineComponent.STEP_P), st.PackP()));
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnSetTimeout", ex); }
        }

        private void ShowMore(ActRow row, LogicAction a)
        {
            string key = IsExpanded(row.Slot) ? "ZtxLogicPanel/SettingsOpen" : a.AnySet ? "ZtxLogicPanel/SettingsClosedSet" : "ZtxLogicPanel/SettingsClosed";
            LogicGui.SetKey(row.More, key);
        }

        private static string ActPoseText(int kind, int raw)
        {
            return kind == 3 ? (raw - 90).ToString(CultureInfo.InvariantCulture) : RawText(kind, raw);
        }

        private static bool TryParseActPose(int kind, Part target, string text, out int raw)
        {
            raw = 0;
            if (kind == 3)
            {
                string t = (text ?? "").Trim().Replace(',', '.');
                if (t.Length == 0 || !float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float deg)) return false;
                raw = Math.Max(0, Math.Min(180, (int)Math.Round(deg) + 90));
                return true;
            }
            if (!TryParseRaw(kind, text, MaxPoseRaw(target, kind), out raw)) return false;
            if (kind == 2) raw = (int)Math.Round(raw / 10f, MidpointRounding.AwayFromZero) * 10;
            return true;
        }

        private void WriteAct(ActRow row, LogicAction a, bool log)
        {
            a.BlankLeaves = true;
            int t = a.Target.Pack(), m = a.PackMisc(), p = a.PackPose();
            int s1 = _engine.IsStoreV2 ? a.OnStation.Pack() : 0, s2 = _engine.IsStoreV2 ? a.OffStation.Pack() : 0;
            int drv = _engine.IsStoreV2 ? a.Drive : 0;
            var writes = new List<(ID<PartToggleGuiRules>, int)>();
            if (t != row.LastRaw[0]) writes.Add((_engine.ActionSlot(row.Slot, ZtxRuleEngineComponent.ACT_T), t));
            if (m != row.LastRaw[1]) writes.Add((_engine.ActionSlot(row.Slot, ZtxRuleEngineComponent.ACT_M), m));
            if (p != row.LastRaw[2]) writes.Add((_engine.ActionSlot(row.Slot, ZtxRuleEngineComponent.ACT_P), p));
            if (_engine.IsStoreV2 && s1 != row.LastRaw[3]) writes.Add((_engine.ActionSlot(row.Slot, ZtxRuleEngineComponent.ACT_S1), s1));
            if (_engine.IsStoreV2 && s2 != row.LastRaw[4]) writes.Add((_engine.ActionSlot(row.Slot, ZtxRuleEngineComponent.ACT_S2), s2));
            if (_engine.IsStoreV2 && drv != row.LastRaw[5]) writes.Add((_engine.ActionSlot(row.Slot, ZtxRuleEngineComponent.ACT_V), drv));
            row.LastRaw[0] = t;
            row.LastRaw[1] = m;
            row.LastRaw[2] = p;
            row.LastRaw[3] = s1;
            row.LastRaw[4] = s2;
            row.LastRaw[5] = drv;
            row.Current = a;
            if (writes.Count == 0) return;
            Write(writes.ToArray());
            if (log) Log.Info("console: actuator slot " + row.Slot + " -> " + a + ".");
        }

        private void AddActuator(int r)
        {
            try
            {
                int k = _engine?.FreeAction() ?? 0;
                if (k == 0) return;
                var a = new LogicAction { Rule = r, BlankLeaves = true, Sided = true };
                var writes = new List<(ID<PartToggleGuiRules>, int)>
                {
                    (_engine.ActionSlot(k, ZtxRuleEngineComponent.ACT_T), 0),
                    (_engine.ActionSlot(k, ZtxRuleEngineComponent.ACT_M), a.PackMisc()),
                    (_engine.ActionSlot(k, ZtxRuleEngineComponent.ACT_P), 0),
                };
                OwnAct(writes, k, r);
                if (_engine.IsStoreV2) writes.Add((_engine.ActionPosSlot(k), NextOutputPos(r)));
                Write(writes.ToArray());
                SetExpanded(k, false);
                Log.Info("console: actuator slot " + k + " added to rule " + r + ".");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.AddActuator", ex); }
        }

        private void DeleteActuator(int k)
        {
            try
            {
                if (_engine == null) return;
                var writes = new List<(ID<PartToggleGuiRules>, int)> { (_engine.ActionSlot(k, 0), 0), (_engine.ActionSlot(k, 1), 0), (_engine.ActionSlot(k, 2), 0) };
                if (_engine.IsStoreV2) writes.Add((_engine.ActionPosSlot(k), 0));
                if (_engine.IsStoreV2)
                {
                    writes.Add((_engine.ActionSlot(k, ZtxRuleEngineComponent.ACT_S1), 0));
                    writes.Add((_engine.ActionSlot(k, ZtxRuleEngineComponent.ACT_S2), 0));
                    writes.Add((_engine.ActionSlot(k, ZtxRuleEngineComponent.ACT_V), 0));
                }
                Write(writes.ToArray());
                SetExpanded(k, false);
                Log.Info("console: actuator slot " + k + " deleted.");
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.DeleteActuator", ex); }
        }

        private void ToggleSettings(ActRow row)
        {
            bool open = !IsExpanded(row.Slot);
            SetExpanded(row.Slot, open);
            if (open && !row.SettingsBuilt) BuildSettings(row);
            _forceFull = true;
            if (open) row.Block.Hidden.Remove(row.Settings);
            else row.Block.Hidden.Add(row.Settings);
            bool active = open && !IsCollapsed(row.Block.Rule) && (row.Seq == null || IsSeqOpen(row.Seq.Id));
            if (row.Settings.SelfActive != active) row.Settings.SelfActive = active;
            FitBlock(row.Block);
            ShowMore(row, row.Current);
        }

        private void OnActTargetPicked(ActRow row, int value)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                if (value == row.Current.Target.Pack()) return;
                LogicAction a = row.Current;
                int before = LogicAction.KindOf(LogicNet.Resolve(_console?.Ship, _engine.Members, a.Target));
                a.Target = LogicRef.Unpack(value);
                int after = LogicAction.KindOf(LogicNet.Resolve(_console?.Ship, _engine.Members, a.Target));
                if (after != before)
                {
                    a.OnSet = a.OffSet = false;
                    a.OnPose = a.OffPose = 0;
                    a.Speed = a.Accel = a.Strength = a.MergeAt = a.Grip = 0;
                    a.OnStation = a.OffStation = LogicRef.None;
                }
                WriteAct(row, a, true);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnActTargetSelected", ex); }
        }

        private void OnActPoseChanged(ActRow row, bool on)
        {
            if (_refreshing || _engine == null || _console == null) return;
            try
            {
                LogicAction a = row.Current;
                if (!a.IsUsed) return;
                Part tp = LogicNet.Resolve(_console.Ship, _engine.Members, a.Target);
                int kind = LogicAction.KindOf(tp);
                int raw = 0;
                bool set = kind != 0 && TryParseActPose(kind, tp, (on ? row.On : row.Off).Text, out raw);
                if (on)
                {
                    a.OnSet = set;
                    a.OnPose = set ? raw : 0;
                }
                else
                {
                    a.OffSet = set;
                    a.OffPose = set ? raw : 0;
                }
                WriteAct(row, a, false);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnActPoseChanged", ex); }
        }

        private void OnActSpeedChanged(ActRow row)
        {
            if (_refreshing || _engine == null || _console == null) return;
            try
            {
                LogicAction a = row.Current;
                if (!a.IsUsed) return;
                Part tp = LogicNet.Resolve(_console.Ship, _engine.Members, a.Target);
                int kind = LogicAction.KindOf(tp);
                a.Speed = (kind == 1 || kind == 2) && TryParseRaw(kind, row.Speed.Text, LogicAction.SPEED_MAX, out int speed) ? speed : 0;
                WriteAct(row, a, false);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnActSpeedChanged", ex); }
        }

        private void OnActListPicked(ActRow row, bool accel, int value)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                LogicAction a = row.Current;
                if (!a.IsUsed) return;
                if (accel) a.Accel = value;
                else a.Strength = value;
                WriteAct(row, a, true);
                ShowMore(row, a);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnActListSelected", ex); }
        }

        private void OnActCartPicked(ActRow row, int which, int value)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                LogicAction a = row.Current;
                if (!a.IsUsed) return;
                switch (which)
                {
                    case 0: a.OnStation = LogicRef.Unpack(value); break;
                    case 1: a.OnPose = Math.Max(0, Math.Min(RailCartRules.MODE_HOLD + 1, value)); break;
                    case 2: a.OffStation = LogicRef.Unpack(value); break;
                    case 3: a.OffPose = Math.Max(0, Math.Min(RailCartRules.MODE_HOLD + 1, value)); break;
                    case 6: a.MergeAt = value == 1 || value == 2 ? value : 0; break;
                    case 8:
                    case 9:
                    case 10:
                    case 11:
                    {
                        int dk = LogicAction.KindOf(LogicNet.Resolve(_console.Ship, _engine.Members, a.Target));
                        bool onSide = which < 10;
                        if (dk == LogicAction.KIND_SWITCH || dk == LogicAction.KIND_BUTTON)
                        {
                            a.OnPose = Math.Max(0, Math.Min(2, value));
                            a.OnSet = a.OnPose != 0;
                            break;
                        }
                        bool semantic = a.Sided ? !a.WhenOff : onSide;
                        DockToggles.Effective(dk, onSide ? a.OnSet : a.OffSet, onSide ? a.OnPose : a.OffPose, semantic, out int fa, out int fb);
                        if (which == 8 || which == 10) fa = Math.Max(0, Math.Min(2, value));
                        else fb = Math.Max(0, Math.Min(2, value));
                        if (onSide) { a.OnSet = true; a.OnPose = DockToggles.Code(fa, fb); }
                        else { a.OffSet = true; a.OffPose = DockToggles.Code(fa, fb); }
                        break;
                    }
                    default: return;
                }
                WriteAct(row, a, true);
                ShowMore(row, a);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnActCartPicked", ex); }
        }

        private void OnActBayPicked(ActRow row, int cell, int value)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                LogicAction a = row.Current;
                if (!a.IsUsed) return;
                int code = BayConsole.With(a.OnSet ? BayConsole.CodeOf(a.OnPose, a.OffPose) : 0, cell, value);
                BayConsole.SplitCode(code, out int onPose, out int offPose);
                a.OnPose = onPose;
                a.OffPose = offPose;
                a.OnSet = code != 0;
                WriteAct(row, a, true);
                ShowMore(row, a);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnActBayPicked", ex); }
        }

        private void OnActCartDrive(ActRow row)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                LogicAction a = row.Current;
                if (!a.IsUsed) return;
                float? sp = TryCellNumber(row.CartSpeedT, out float s) ? s : (float?)null;
                int? pw = TryCellNumber(row.CartPowerT, out float p) ? (int)Math.Round(p) : (int?)null;
                float? dw = TryCellNumber(row.CartDwellT, out float d) ? d : (float?)null;
                a.Drive = RailCartRules.PackDrive(sp, pw, dw);
                a.Speed = 0;
                a.Strength = 0;
                WriteAct(row, a, false);
                ShowMore(row, a);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnActCartDrive", ex); }
        }

        private void OnActMergeAtPicked(ActRow row, int value)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                LogicAction a = row.Current;
                if (!a.IsUsed) return;
                a.MergeAt = value == LogicAction.MERGE_NEVER ? LogicAction.MERGE_NEVER : value & 3;
                WriteAct(row, a, true);
                ShowMore(row, a);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnActMergeAtPicked", ex); }
        }

        private void OnActGripPicked(ActRow row, int value)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                LogicAction a = row.Current;
                if (!a.IsUsed) return;
                a.Grip = value == LogicAction.GRIP_ATTACH || value == LogicAction.GRIP_RELEASE || value == LogicAction.GRIP_SET_OFF ? value : 0;
                WriteAct(row, a, true);
                ShowMore(row, a);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnActGripPicked", ex); }
        }

        private void OnActWhen(ActRow row)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                LogicAction a = row.Current;
                if (!a.IsUsed || !a.Sided) return;
                a.WhenOff = !a.WhenOff;
                WriteAct(row, a, true);
                ShowMore(row, a);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnActWhen", ex); }
        }

        internal static List<(int, string)> AdapterSettingItems(Part adapter, int keep)
        {
            var l = new List<(int, string)> { (0, Strings.GetText("ZtxLogicPanel/AdapterSettingNone")) };
            bool kept = keep == 0;
            Part target = AdapterToggles.TargetOf(adapter);
            foreach (AdapterToggles.Setting s in AdapterToggles.SettingsOf(target))
            {
                if (s.Hash == keep) kept = true;
                l.Add((s.Hash, s.IsTrigger ? Strings.FormatText("ZtxLogicPanel/AdapterButtonFmt", s.Name) : s.Name));
            }
            if (!kept) l.Add((keep, LogicPalette.Wrap(LogicPalette.Neutral, Strings.GetText("ZtxLogicPanel/AdapterSettingGone"))));
            return l;
        }

        internal static List<(int, string)> AdapterValueItems(Part adapter, int hash, int keep)
        {
            var l = new List<(int, string)> { (0, Strings.GetText("ZtxLogicPanel/AdapterValueLeave")) };
            bool kept = keep == 0;
            AdapterToggles.Setting s = hash != 0 ? AdapterToggles.Find(AdapterToggles.TargetOf(adapter), hash) : null;
            if (s != null)
                foreach (var (mode, label) in s.Choices)
                {
                    int packed = AdapterToggles.PackChoice(mode);
                    if (packed == keep) kept = true;
                    l.Add((packed, label));
                }
            if (!kept) l.Add((keep, LogicPalette.Wrap(LogicPalette.Neutral, (AdapterToggles.UnpackChoice(keep) ?? 0) + " ?")));
            return l;
        }

        private static string AdapterSideText(Part adapter, LogicAction a)
        {
            if (!a.OnSet || a.Drive == 0) return Strings.GetText("ZtxLogicPanel/SumLeave");
            AdapterToggles.Setting s = AdapterToggles.Find(AdapterToggles.TargetOf(adapter), a.Drive);
            if (s == null) return Strings.GetText("ZtxLogicPanel/AdapterSettingGone");
            string value = "?";
            foreach (var (mode, label) in s.Choices) if (AdapterToggles.PackChoice(mode) == a.OnPose) value = label;
            return s.IsTrigger ? Strings.FormatText("ZtxLogicPanel/AdapterPressFmt", s.Name) : Strings.FormatText("ZtxLogicPanel/AdapterSetFmt", s.Name, value);
        }

        private static string LampSideText(LogicAction a)
        {
            if (!a.OnSet) return Strings.GetText("ZtxLogicPanel/SumLeave");
            LampToggles.PlanDo(a.OnPose, out int? state);
            return state.HasValue ? Strings.GetText("ZtxLogicPanel/LampLight_" + (state.Value + 1)) : Strings.GetText("ZtxLogicPanel/SumLeave");
        }

        private static List<(int, string)> LampLightItems()
        {
            var l = new List<(int, string)>();
            for (int i = 0; i <= 3; i++) l.Add((i, Strings.GetText("ZtxLogicPanel/LampLight_" + i)));
            return l;
        }

        private void OnActAdapterPicked(ActRow row, int which, int value)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                LogicAction a = row.Current;
                if (!a.IsUsed || !a.Sided) return;
                if (which == 0)
                {
                    if (a.Drive == value) return;
                    a.Drive = value;
                    a.OnSet = false;
                    a.OnPose = 0;
                }
                else
                {
                    a.OnPose = Math.Max(0, Math.Min(AdapterToggles.POSE_MAX, value));
                    a.OnSet = a.OnPose != 0;
                }
                WriteAct(row, a, true);
                ShowMore(row, a);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnActAdapterPicked", ex); }
        }

        private void OnActLampPicked(ActRow row, int value)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                LogicAction a = row.Current;
                if (!a.IsUsed || !a.Sided) return;
                a.OnPose = LampToggles.Code(value);
                a.OnSet = a.OnPose != 0;
                WriteAct(row, a, true);
                ShowMore(row, a);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnActLampPicked", ex); }
        }

        private void OnActHold(ActRow row)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                LogicAction a = row.Current;
                if (!a.IsUsed || !a.Sided) return;
                a.Hold = !a.Hold;
                WriteAct(row, a, true);
                ShowMore(row, a);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnActHold", ex); }
        }

        private void OnActInvert(ActRow row)
        {
            if (_refreshing || _engine == null) return;
            try
            {
                LogicAction a = row.Current;
                if (!a.IsUsed) return;
                a.Invert = !a.Invert;
                WriteAct(row, a, true);
                LogicGui.SetKey(row.Invert, a.Invert ? "ZtxLogicPanel/InvertOn" : "ZtxLogicPanel/InvertOff");
                StateTip(row.Invert, a.Invert ? "ZtxLogicPanel/TipInvertOn" : "ZtxLogicPanel/TipInvertOff");
                ShowMore(row, a);
            }
            catch (Exception ex) { Log.Exception("LogicRulesTab.OnActInvert", ex); }
        }

        private bool MigratePistonPopouts(bool commandable)
        {
            if (!commandable || _engine == null || _console?.Ship == null) return false;
            List<(ID<PartToggleGuiRules>, int)> writes = null;
            List<(Part, ID<PartToggleGuiRules>, int)> partWrites = null;
            for (int k = 1; k <= _engine.ActionCount; k++)
            {
                LogicAction a = _engine.Action(k);
                if (!a.IsUsed || a.BlankLeaves || _pistonMigrated.Contains(k)) continue;
                Part p = LogicNet.Resolve(_console.Ship, _engine.Members, a.Target);
                int kind = LogicAction.KindOf(p);
                if (kind == 0) continue;
                if (!LogicAction.IsPositionedKind(kind)) continue;
                _pistonMigrated.Add(k);
                var psd = new Cosmoteer.Ships.Blueprints.PartSaveData(p);
                var states = p.Ship.Metadata.PartUIToggleStates;
                bool pist = kind == LogicAction.KIND_PISTON;
                a = LogicAction.FillOldPopouts(a, kind, states.Get((psd, pist ? LogicAction.OldPistonOnId : LogicAction.OldMotorOnId)),
                    states.Get((psd, pist ? LogicAction.OldPistonOffId : LogicAction.OldMotorOffId)));
                writes ??= new List<(ID<PartToggleGuiRules>, int)>();
                writes.Add((_engine.ActionSlot(k, ZtxRuleEngineComponent.ACT_M), a.PackMisc()));
                writes.Add((_engine.ActionSlot(k, ZtxRuleEngineComponent.ACT_P), a.PackPose()));
                Log.Info("console: actuator slot " + k + " (" + (kind == 2 ? "piston" : kind == 1 ? "motor" : "hinge") + " at " + p.Location
                    + ") takes its part's old ON / OFF popouts: " + a + ".");
            }
            if (writes != null) Write(writes.ToArray());
            if (partWrites != null) WriteParts(partWrites);
            return writes != null;
        }

        private bool MigrateLegacy(bool commandable)
        {
            if (!commandable || _engine == null || _engine.IsStoreV2) return false;
            List<(ID<PartToggleGuiRules>, int)> writes = null;
            var taken = new HashSet<int>();
            for (int r = 1; r <= _engine.RuleCount; r++)
            {
                LogicRuleDef def = _engine.Rule(r);
                if (!def.Exists || !def.Target.IsPart || _migrated.Contains(r)) continue;
                _migrated.Add(r);
                int k = 0;
                for (int s = 1; s <= _engine.ActionCount && k == 0; s++) if (!_engine.Action(s).IsUsed && !taken.Contains(s)) k = s;
                if (k == 0)
                {
                    Log.Info("console: rule " + r + " keeps its old target " + def.Target + ": no free actuator slot.");
                    continue;
                }
                taken.Add(k);
                var a = new LogicAction { Rule = r, Target = def.Target, OnSet = def.PoseSet, OnPose = def.PoseSet ? def.Pose : 0, Speed = def.Speed };
                writes ??= new List<(ID<PartToggleGuiRules>, int)>();
                writes.Add((_engine.ActionSlot(k, ZtxRuleEngineComponent.ACT_T), a.Target.Pack()));
                writes.Add((_engine.ActionSlot(k, ZtxRuleEngineComponent.ACT_M), a.PackMisc()));
                writes.Add((_engine.ActionSlot(k, ZtxRuleEngineComponent.ACT_P), a.PackPose()));
                def.Target = LogicRef.None;
                def.PoseSet = false;
                def.Pose = 0;
                def.Speed = 0;
                writes.Add((_engine.RuleSlot(r, ZtxRuleEngineComponent.RULE_T), 0));
                writes.Add((_engine.RuleSlot(r, ZtxRuleEngineComponent.RULE_M), def.PackMisc()));
                Log.Info("console: rule " + r + " old target moved to actuator slot " + k + " (" + a + ").");
            }
            if (writes != null) Write(writes.ToArray());
            return writes != null;
        }
    }
}
