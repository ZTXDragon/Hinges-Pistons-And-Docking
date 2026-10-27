using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Geometry;
using Halfling.Gui;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(PartTriggerButton), "OnClicked")]
    internal static class AdapterPanelOpenPatch
    {
        public static readonly ID<PartTriggerGuiRules> TriggerId = new ID<PartTriggerGuiRules>("ztx_adapter_panel");

        static bool Prefix(PartTriggerButton __instance)
        {
            if (!__instance.Rules.TriggerID.Equals(TriggerId)) return true;
            try
            {
                foreach (Part p in __instance.Sim.PlayerInput.SelectedParts)
                {
                    if (PartUITrigger.GetOnPart(p, TriggerId) == null) continue;
                    AdapterWindow.Open(__instance.Game, p);
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Exception("AdapterPanelOpenPatch.Prefix", ex);
            }
            return false;
        }
    }

    internal sealed class AdapterWindow : RailPartWindow
    {
        private static AdapterWindow s_instance;

        private readonly Dictionary<int, ListItem> _readItems = new Dictionary<int, ListItem>();
        private readonly Dictionary<int, ListItem> _settingItems = new Dictionary<int, ListItem>();
        private readonly DropList _read, _setting;
        private readonly Label _linked, _readout;
        private string _settingSig;

        public static void Open(GameRoot game, Part adapter)
        {
            if (game == null || adapter == null) return;
            if (s_instance == null || !ReferenceEquals(s_instance._game, game))
            {
                s_instance = new AdapterWindow(game);
                game.Gui.FloatingWindows.AddChild(s_instance);
            }
            s_instance._part = adapter;
            s_instance._settingSig = null;
            s_instance.SelfActive = true;
            game.Gui.FloatingWindows.Children.RaiseToFront(s_instance);
        }

        public static void Reset()
        {
            s_instance = null;
        }

        private AdapterWindow(GameRoot game) : base(game, "ZtxAdapterPanel/Title", new Vector2(500f, 220f))
        {
            LayoutBox r0 = Row();
            r0.DockedChildren.Add(RowLabel("ZtxAdapterPanel/Linked", "ZtxAdapterPanel/TipLinked"));
            _linked = new Label { DockMode = DockMode.Left, Width = FieldWidth };
            _linked.ToolTips.TextProvider = Strings.KeyString("ZtxAdapterPanel/TipLinked");
            r0.DockedChildren.Add(_linked);
            AddChild(r0);
            _read = AddRow("ZtxAdapterPanel/Read", "ZtxAdapterPanel/TipRead", ZtxLogicAdapterComponent.ReadId);
            _setting = AddRow("ZtxAdapterPanel/Setting", "ZtxAdapterPanel/TipSetting", ZtxLogicAdapterComponent.ToggleId);
            _readout = new Label { Height = RowHeight };
            AddChild(_readout);
            var reads = new List<(int, string)>();
            for (int i = 0; i < AdapterRead.READ_COUNT; i++) reads.Add((i, Text("ZtxAdapterPanel/Read_" + i)));
            Fill(_read, _readItems, reads, v => "ZtxAdapterPanel/TipRead_" + v);
        }

        private DropList AddRow(string labelKey, string tipKey, ID<PartToggleGuiRules> id)
        {
            LayoutBox row = Row();
            row.DockedChildren.Add(RowLabel(labelKey, tipKey));
            DropList list = MakeList(FieldWidth);
            list.ToolTips.TextProvider = Strings.KeyString(tipKey);
            list.ListBox.SelectionManager.WidgetSelected += (s, e) => OnPicked(id, e);
            row.DockedChildren.Add(list);
            AddChild(row);
            return list;
        }

        protected override void OnUpdatingUIState(object sender, EventArgs e)
        {
            try
            {
                if (PartGone()) { SelfActive = false; return; }
                Part target = AdapterToggles.TargetOf(_part);
                string linked = target != null ? target.Rules.NameKey.Text + " (" + target.Location.X + "," + target.Location.Y + ")" : Text("ZtxAdapterPanel/NoTarget");
                if (_linked.Text != linked) _linked.Text = linked;
                int read = LogicSource.Mode(_part, ZtxLogicAdapterComponent.ReadId, AdapterRead.READ_OPERATIONAL);
                Select(_read, _readItems, read);
                var settings = new List<(int, string)> { (0, Text("ZtxAdapterPanel/SettingNone")) };
                foreach (AdapterToggles.Setting s in AdapterToggles.SettingsOf(target))
                    if (!s.IsTrigger && !s.IsCoverage) settings.Add((s.Hash, s.Name));
                string sig = string.Join("|", settings);
                if (sig != _settingSig)
                {
                    _settingSig = sig;
                    Fill(_setting, _settingItems, settings, v => "ZtxAdapterPanel/TipSetting");
                }
                Select(_setting, _settingItems, LogicSource.Mode(_part, ZtxLogicAdapterComponent.ToggleId, 0));
                _setting.SelfInputActive = read == AdapterRead.READ_SETTING;
                float? percent = LogicNet.ReadValue(_part);
                float? units = LogicNet.ReadUnits(_part);
                string why = LogicNet.WhyNot(_part);
                string readout = Strings.FormatText("ZtxAdapterPanel/ReadoutFmt",
                    percent.HasValue ? ((int)percent.Value).ToString() : "-",
                    units.HasValue ? ((int)units.Value).ToString() : "-",
                    Text(LogicNet.ReadOutput(_part) ? "ZtxLogicPanel/SigOn" : "ZtxLogicPanel/SigOff"))
                    + (why.Length > 0 ? " -- " + why : "");
                if (_readout.Text != readout) _readout.Text = readout;
            }
            catch (Exception ex)
            {
                Log.Exception("AdapterWindow.OnUpdatingUIState", ex);
                SelfActive = false;
            }
        }

        private void OnPicked(ID<PartToggleGuiRules> id, WidgetEventArgs e)
        {
            if (_refreshing || _part == null) return;
            try
            {
                if (!(e.Widget.UserData is int value)) return;
                if (LogicSource.Mode(_part, id, int.MinValue) == value) return;
                Write((id, value));
                Log.Info("logic adapter at " + _part.Location + ": " + id + " -> " + value + ".");
            }
            catch (Exception ex) { Log.Exception("AdapterWindow.OnPicked", ex); }
        }
    }
}
