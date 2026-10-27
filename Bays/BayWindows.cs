using System;
using System.Collections.Generic;
using System.Globalization;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Geometry;
using Halfling.Gui;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(PartTriggerButton), "OnClicked")]
    internal static class BayPanelOpenPatch
    {
        public static readonly ID<PartTriggerGuiRules> TriggerId = new ID<PartTriggerGuiRules>("ztx_bay_panel");
        public static readonly ID<PartTriggerGuiRules> StationTriggerId = new ID<PartTriggerGuiRules>("ztx_station_panel");

        static bool Prefix(PartTriggerButton __instance)
        {
            ID<PartTriggerGuiRules> id = __instance.Rules.TriggerID;
            if (!id.Equals(TriggerId) && !id.Equals(StationTriggerId)) return true;
            try
            {
                foreach (Part p in __instance.Sim.PlayerInput.SelectedParts)
                {
                    if (PartUITrigger.GetOnPart(p, id) == null) continue;
                    if (ZtxBayTerminalComponent.ForPart(p) != null) BayTerminalWindow.Open(__instance.Game, p);
                    else if (ZtxBayStationComponent.ForPart(p) != null) BayStationWindow.Open(__instance.Game, p);
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Exception("BayPanelOpenPatch.Prefix", ex);
            }
            return false;
        }
    }

    internal sealed class BayTerminalWindow : RailPartWindow
    {
        private static BayTerminalWindow s_instance;
        private readonly TextEditField _name;
        private readonly Label _bay;
        private readonly Label _design;
        private readonly Label _held;
        private readonly Label _crew;
        private readonly Label _supply;
        private readonly Label _energy;
        private readonly Label _status;
        private int _lastN1 = -1, _lastN2 = -1;
        private readonly TextEditField _flyOut, _push;
        private readonly LayoutBox _flyOutRow, _pushRow;
        private readonly List<HiddenRow> _hidden = new List<HiddenRow>();

        private sealed class HiddenRow
        {
            public LayoutBox Row;
            public DropList List;
            public Dictionary<int, ListItem> Items;
            public ID<PartToggleGuiRules> Id;
            public int Fallback;
        }

        public static Part Current => s_instance != null && s_instance.IsActive ? s_instance._part : null;

        public static void Open(GameRoot game, Part terminal)
        {
            if (game == null || terminal == null) return;
            SensorAreaOverlay.Attach(game.Sim);
            if (s_instance == null || !ReferenceEquals(s_instance._game, game))
            {
                s_instance = new BayTerminalWindow(game);
                game.Gui.FloatingWindows.AddChild(s_instance);
            }
            s_instance._part = terminal;
            s_instance._lastN1 = s_instance._lastN2 = -1;
            s_instance.SelfActive = true;
            game.Gui.FloatingWindows.Children.RaiseToFront(s_instance);
        }

        public static void Reset()
        {
            s_instance = null;
        }

        private BayTerminalWindow(GameRoot game) : base(game, "ZtxBayPanel/Title", new Vector2(480f, 736f))
        {
            AddToggleRow("ZtxBayPanel/Build", "ZtxBayPanel/TipBuild", BayManager.BuildId, "ZtxDockPanel/Off", "ZtxDockPanel/On");
            AddToggleRow("ZtxBayPanel/Release", "ZtxBayPanel/TipRelease", BayManager.ReleaseId, "ZtxDockPanel/Off", "ZtxDockPanel/On");
            AddToggleRow("ZtxBayPanel/Repeat", "ZtxBayPanel/TipRepeat", BayManager.RepeatId, "ZtxDockPanel/Off", "ZtxDockPanel/On");
            AddHiddenRow("ZtxBayPanel/Launch", "ZtxBayPanel/TipLaunch", BayManager.LaunchId, BayManager.LAUNCH_DRIFT, "ZtxBayPanel/Drift", "ZtxBayPanel/FlyOut", "ZtxBayPanel/Push");
            _flyOut = NumberRow("ZtxBayPanel/FlyOutDistance", "ZtxBayPanel/TipFlyOutDistance", out _flyOutRow);
            _flyOut.FocusController.Defocused += (sender, e) => CommitFlyOut();
            _push = NumberRow("ZtxBayPanel/PushSpeed", "ZtxBayPanel/TipPushSpeed", out _pushRow);
            _push.FocusController.Defocused += (sender, e) => CommitPush();
            AddHiddenRow("ZtxBayPanel/Facing", "ZtxBayPanel/TipFacing", BayManager.FacingId, 0, "ZtxBayPanel/FacingN", "ZtxBayPanel/FacingNE", "ZtxBayPanel/FacingE",
                "ZtxBayPanel/FacingSE", "ZtxBayPanel/FacingS", "ZtxBayPanel/FacingSW", "ZtxBayPanel/FacingW", "ZtxBayPanel/FacingNW");
            AddHiddenRow("ZtxBayPanel/Crew", "ZtxBayPanel/TipCrew", HatchLinks.BayCrewId, HatchLinks.CREW_OFF, "ZtxBayPanel/CrewOff", "ZtxBayPanel/CrewBoard", "ZtxBayPanel/CrewReturn");
            AddHiddenRow("ZtxBayPanel/Resupply", "ZtxBayPanel/TipResupply", BayManager.ResupplyId, 0, "ZtxDockPanel/Off", "ZtxDockPanel/On");
            AddSection("ZtxBayPanel/ReleaseWhen");
            AddToggleRow("ZtxBayPanel/WhenBuilt", "ZtxBayPanel/TipWhenBuilt", BayManager.WhenIds[BayRelease.BUILT], "ZtxDockPanel/Off", "ZtxDockPanel/On");
            AddToggleRow("ZtxBayPanel/WhenCrewed", "ZtxBayPanel/TipWhenCrewed", BayManager.WhenIds[BayRelease.CREWED], "ZtxDockPanel/Off", "ZtxDockPanel/On");
            AddToggleRow("ZtxBayPanel/WhenSupplied", "ZtxBayPanel/TipWhenSupplied", BayManager.WhenIds[BayRelease.SUPPLIED], "ZtxDockPanel/Off", "ZtxDockPanel/On");
            AddToggleRow("ZtxBayPanel/WhenRepaired", "ZtxBayPanel/TipWhenRepaired", BayManager.WhenIds[BayRelease.REPAIRED], "ZtxDockPanel/Off", "ZtxDockPanel/On");
            AddToggleRow("ZtxBayPanel/WhenCharged", "ZtxBayPanel/TipWhenCharged", BayManager.WhenIds[BayRelease.CHARGED], "ZtxDockPanel/Off", "ZtxDockPanel/On");
            LayoutBox nameRow = Row();
            nameRow.DockedChildren.Add(RowLabel("ZtxBayPanel/Name", "ZtxBayPanel/TipName"));
            _name = LogicGui.Typing(new TextEditField(TextEditField.Style.SingleLine) { DockMode = DockMode.Left, Width = FieldWidth });
            _name.ToolTips.TextProvider = Strings.KeyString("ZtxBayPanel/TipName");
            _name.TextChanged += OnNameChanged;
            nameRow.DockedChildren.Add(_name);
            AddChild(nameRow);
            _bay = new Label { Height = RowHeight };
            AddChild(_bay);
            _design = new Label { Height = RowHeight };
            AddChild(_design);
            _held = new Label { Height = RowHeight };
            AddChild(_held);
            _crew = new Label { Height = RowHeight };
            AddChild(_crew);
            _supply = new Label { Height = RowHeight };
            AddChild(_supply);
            _energy = new Label { Height = RowHeight };
            AddChild(_energy);
            _status = new Label { Height = RowHeight };
            AddChild(_status);
            Button clear = LogicGui.SmallButton("ZtxBayPanel/ClearDesign", 160f, LogicPalette.ActDelete, "ZtxBayPanel/TipClearDesign");
            clear.Height = RowHeight;
            clear.Clicked += delegate
            {
                if (_part == null) return;
                try { _game.NetManager.EnqueueInput(GripStore.WriteInput(_part, (BayManager.ClearId, 1))); }
                catch (Exception ex) { Log.Exception("BayTerminalWindow.ClearDesign", ex); }
            };
            LayoutBox clearRow = Row();
            clearRow.DockedChildren.Add(clear);
            AddChild(clearRow);
        }

        private HiddenRow AddHiddenRow(string labelKey, string tipKey, ID<PartToggleGuiRules> id, int fallback, params string[] textKeys)
        {
            var h = new HiddenRow { Row = Row(), List = MakeList(FieldWidth), Items = new Dictionary<int, ListItem>(), Id = id, Fallback = fallback };
            h.Row.DockedChildren.Add(RowLabel(labelKey, tipKey));
            h.List.ToolTips.TextProvider = Strings.KeyString(tipKey);
            var entries = new List<(int, string)>();
            for (int i = 0; i < textKeys.Length; i++) entries.Add((i, Text(textKeys[i])));
            Fill(h.List, h.Items, entries);
            h.List.ListBox.SelectionManager.WidgetSelected += (sender, e) =>
            {
                if (_refreshing || _part == null || !(e.Widget.UserData is int v)) return;
                if (BayValues.Get(_part, h.Id, h.Fallback) == v) return;
                _game.NetManager.EnqueueInput(GripStore.WriteInputAll(Targets(), (h.Id, v)));
            };
            h.Row.DockedChildren.Add(h.List);
            AddChild(h.Row);
            _hidden.Add(h);
            return h;
        }

        private TextEditField NumberRow(string labelKey, string tipKey, out LayoutBox row)
        {
            row = Row();
            row.DockedChildren.Add(RowLabel(labelKey, tipKey));
            TextEditField f = LogicGui.Typing(new TextEditField(TextEditField.Style.SingleLine) { DockMode = DockMode.Left, Width = 120f });
            f.ToolTips.TextProvider = Strings.KeyString(tipKey);
            row.DockedChildren.Add(f);
            AddChild(row);
            return f;
        }

        private void CommitFlyOut()
        {
            try
            {
                ZtxBayTerminalComponent t = ZtxBayTerminalComponent.ForPart(_part);
                if (t == null) return;
                int cur = BayValues.FlyOutCells(_part, t.Rules);
                int v = BayValues.ParseFlyOut(_flyOut.Text, cur);
                if (v != cur) _game.NetManager.EnqueueInput(GripStore.WriteInputAll(Targets(), (BayValues.FlyOutId, v)));
                LogicGui.SetFieldText(_flyOut, v.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { Log.Exception("BayTerminalWindow.CommitFlyOut", ex); }
        }

        private void CommitPush()
        {
            try
            {
                ZtxBayTerminalComponent t = ZtxBayTerminalComponent.ForPart(_part);
                if (t == null) return;
                int cur = BayValues.PushTenths(_part, t.Rules);
                int v = BayValues.ParsePushTenths(_push.Text, cur);
                if (v != cur) _game.NetManager.EnqueueInput(GripStore.WriteInputAll(Targets(), (BayValues.PushTenthsId, v)));
                LogicGui.SetFieldText(_push, BayValues.FormatTenths(v));
            }
            catch (Exception ex) { Log.Exception("BayTerminalWindow.CommitPush", ex); }
        }

        private void OnNameChanged(object sender, EventArgs e)
        {
            if (_refreshing || _part == null) return;
            try
            {
                RailCartRules.PackName(_name.Text, out int n1, out int n2);
                if (n1 == _lastN1 && n2 == _lastN2) return;
                _lastN1 = n1;
                _lastN2 = n2;
                _game.NetManager.EnqueueInput(GripStore.WriteInput(_part, (BayManager.Name1Id, n1), (BayManager.Name2Id, n2)));
                Log.Info($"Bay terminal at {_part.Location}: name -> {RailCartRules.UnpackName(n1, n2)}.");
            }
            catch (Exception ex) { Log.Exception("BayTerminalWindow.OnNameChanged", ex); }
        }

        protected override void OnUpdatingUIState(object sender, EventArgs e)
        {
            try
            {
                if (PartGone()) { SelfActive = false; return; }
                RefreshTargets();
                FitToggleRows();
                ZtxBayTerminalComponent t = ZtxBayTerminalComponent.ForPart(_part);
                if (t == null) return;
                foreach (HiddenRow h in _hidden) Select(h.List, h.Items, BayValues.Get(_part, h.Id, h.Fallback));
                int launch = BayValues.Get(_part, BayManager.LaunchId, BayManager.LAUNCH_DRIFT);
                if (_flyOutRow.SelfActive != (launch == BayManager.LAUNCH_FLY)) _flyOutRow.SelfActive = launch == BayManager.LAUNCH_FLY;
                if (_pushRow.SelfActive != (launch == BayManager.LAUNCH_PUSH)) _pushRow.SelfActive = launch == BayManager.LAUNCH_PUSH;
                if (!_flyOut.FocusController.IsFocused)
                    LogicGui.SetFieldText(_flyOut, BayValues.FlyOutCells(_part, t.Rules).ToString(CultureInfo.InvariantCulture));
                if (!_push.FocusController.IsFocused)
                    LogicGui.SetFieldText(_push, BayValues.FormatTenths(BayValues.PushTenths(_part, t.Rules)));
                BayState bay = t.Bay;
                if (!_name.FocusController.IsFocused)
                {
                    int n1 = BayValues.Get(_part, BayManager.Name1Id, 0), n2 = BayValues.Get(_part, BayManager.Name2Id, 0);
                    if (n1 != _lastN1 || n2 != _lastN2)
                    {
                        _refreshing = true;
                        try { LogicGui.SetFieldText(_name, RailCartRules.UnpackName(n1, n2)); }
                        finally { _refreshing = false; }
                        _lastN1 = n1;
                        _lastN2 = n2;
                    }
                }
                int working = 0;
                foreach (ZtxBayStationComponent s in bay.Stations) if (s.Target != null || s.Repair != null) working++;
                ShowText(_bay, Strings.FormatText("ZtxBayPanel/BayFmt", bay.Stations.Count, working));
                ShowText(_design, bay.Pieces > 1
                    ? Strings.FormatText("ZtxBayPanel/PiecesFmt", bay.Pieces)
                    : bay.BlueprintDesign.Count > 0
                        ? Strings.FormatText("ZtxBayPanel/PendingFmt", bay.BlueprintDesign.Count)
                        : bay.Design.Count > 0
                            ? Strings.FormatText("ZtxBayPanel/DesignFmt", bay.Done, bay.Design.Count)
                            : Strings.GetText("ZtxBayPanel/DesignNone"));
                ShowText(_held, bay.Held != null
                    ? Strings.FormatText("ZtxBayPanel/HeldFmt", bay.Held.UniqueID, bay.Held.Metadata.PlainShipName)
                    : Strings.GetText("ZtxBayPanel/HeldNone"));
                ShowText(_crew, bay.Held == null ? "" : Strings.FormatText("ZtxBayPanel/CrewFmt",
                    bay.CrewNote.Length > 0 ? bay.CrewNote : bay.Crewed ? "quarters full" : "quarters not full"));
                ShowText(_energy, bay.Held == null ? "" : Strings.FormatText("ZtxBayPanel/EnergyFmt", bay.Charged ? "full" : "not full"));
                ShowText(_supply, bay.Held == null ? "" : Strings.FormatText("ZtxBayPanel/SupplyFmt",
                    bay.Supplied ? "full" : "wants " + bay.Missing));
                ShowText(_status, Strings.FormatText("ZtxDockPanel/StatusFmt", bay.Waiting));
            }
            catch (Exception ex)
            {
                Log.Exception("BayTerminalWindow.OnUpdatingUIState", ex);
                SelfActive = false;
            }
        }
    }

    internal sealed class BayStationWindow : RailPartWindow
    {
        private static BayStationWindow s_instance;
        private readonly Label _bay;
        private readonly Label _status;

        public static Part Current => s_instance != null && s_instance.IsActive ? s_instance._part : null;

        public static void Open(GameRoot game, Part station)
        {
            if (game == null || station == null) return;
            SensorAreaOverlay.Attach(game.Sim);
            if (s_instance == null || !ReferenceEquals(s_instance._game, game))
            {
                s_instance = new BayStationWindow(game);
                game.Gui.FloatingWindows.AddChild(s_instance);
            }
            s_instance._part = station;
            s_instance.SelfActive = true;
            game.Gui.FloatingWindows.Children.RaiseToFront(s_instance);
        }

        public static void Reset()
        {
            s_instance = null;
        }

        private BayStationWindow(GameRoot game) : base(game, "ZtxBayPanel/StationTitle", new Vector2(460f, 160f))
        {
            AddToggleRow("ZtxBayPanel/StationOn", "ZtxBayPanel/TipStation", BayManager.StationOnId, "ZtxDockPanel/Off", "ZtxDockPanel/On");
            _bay = new Label { Height = RowHeight };
            AddChild(_bay);
            _status = new Label { Height = RowHeight };
            AddChild(_status);
        }

        protected override void OnUpdatingUIState(object sender, EventArgs e)
        {
            try
            {
                if (PartGone()) { SelfActive = false; return; }
                RefreshTargets();
                FitToggleRows();
                ZtxBayStationComponent s = ZtxBayStationComponent.ForPart(_part);
                if (s == null) return;
                ZtxBayTerminalComponent t = BayManager.TerminalOfStation(s);
                ShowText(_bay, t != null
                    ? Strings.FormatText("ZtxBayPanel/StationBayFmt", t.Part.Location, s.Rules.ReachCells)
                    : Strings.FormatText("ZtxBayPanel/StationNoBayFmt", s.Rules.ReachCells));
                ShowText(_status, Strings.FormatText("ZtxDockPanel/StatusFmt", s.Status));
            }
            catch (Exception ex)
            {
                Log.Exception("BayStationWindow.OnUpdatingUIState", ex);
                SelfActive = false;
            }
        }
    }
}
