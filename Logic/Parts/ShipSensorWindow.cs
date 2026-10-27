using System;
using System.Collections.Generic;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Resources;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Geometry;
using Halfling.Gui;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(PartTriggerButton), "OnClicked")]
    internal static class ShipSensorPanelOpenPatch
    {
        public static readonly ID<PartTriggerGuiRules> TriggerId = new ID<PartTriggerGuiRules>("ztx_ship_panel");

        static bool Prefix(PartTriggerButton __instance)
        {
            if (!__instance.Rules.TriggerID.Equals(TriggerId)) return true;
            try
            {
                foreach (Part p in __instance.Sim.PlayerInput.SelectedParts)
                {
                    if (PartUITrigger.GetOnPart(p, TriggerId) == null) continue;
                    ShipSensorWindow.Open(__instance.Game, p);
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Exception("ShipSensorPanelOpenPatch.Prefix", ex);
            }
            return false;
        }
    }

    internal sealed class ShipSensorWindow : RailPartWindow
    {
        private static ShipSensorWindow s_instance;

        private readonly Dictionary<int, ListItem> _measureItems = new Dictionary<int, ListItem>();
        private readonly Dictionary<int, ListItem> _resourceItems = new Dictionary<int, ListItem>();
        private readonly DropList _measure, _resource;
        private readonly Label _readout;

        public static void Open(GameRoot game, Part sensor)
        {
            if (game == null || sensor == null) return;
            if (s_instance == null || !ReferenceEquals(s_instance._game, game))
            {
                s_instance = new ShipSensorWindow(game);
                game.Gui.FloatingWindows.AddChild(s_instance);
            }
            s_instance._part = sensor;
            s_instance.SelfActive = true;
            game.Gui.FloatingWindows.Children.RaiseToFront(s_instance);
        }

        public static void Reset()
        {
            s_instance = null;
        }

        private ShipSensorWindow(GameRoot game) : base(game, "ZtxShipPanel/Title", new Vector2(480f, 180f))
        {
            _measure = AddRow("ZtxShipPanel/Measure", "ZtxShipPanel/TipMeasure", ZtxShipSensorComponent.MeasureId);
            _resource = AddRow("ZtxShipPanel/Resource", "ZtxShipPanel/TipResource", ZtxShipSensorComponent.ResourceId);
            _readout = new Label { Height = RowHeight };
            AddChild(_readout);
            var measures = new List<(int, string)>();
            for (int i = 0; i < ShipSensorMath.MEASURE_COUNT; i++) measures.Add((i, Text("ZtxShipPanel/Measure_" + i)));
            Fill(_measure, _measureItems, measures, v => "ZtxShipPanel/TipMeasure_" + v);
            var resources = new List<(int, string)>();
            if (GameApp.Rules?.Resources != null)
                foreach (ResourceRules r in GameApp.Rules.Resources)
                    if (r != null) resources.Add((ZtxShipSensorComponent.ResourceHash(r.ID.ToString()), r.NameKey.Text));
            resources.Sort((a, b) => string.CompareOrdinal(a.Item2, b.Item2));
            Fill(_resource, _resourceItems, resources, v => "ZtxShipPanel/TipResource");
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
                int measure = LogicSource.Mode(_part, ZtxShipSensorComponent.MeasureId, ShipSensorMath.MEASURE_HULL);
                Select(_measure, _measureItems, measure);
                Select(_resource, _resourceItems, LogicSource.Mode(_part, ZtxShipSensorComponent.ResourceId, 0));
                _resource.SelfInputActive = measure == ShipSensorMath.MEASURE_RESOURCE;
                float? percent = LogicNet.ReadValue(_part);
                float? units = LogicNet.ReadUnits(_part);
                string why = LogicNet.WhyNot(_part);
                string readout = Strings.FormatText("ZtxShipPanel/ReadoutFmt",
                    percent.HasValue ? ((int)percent.Value).ToString() : "-",
                    units.HasValue ? ((int)units.Value).ToString() : "-",
                    Text(LogicNet.ReadOutput(_part) ? "ZtxLogicPanel/SigOn" : "ZtxLogicPanel/SigOff"))
                    + (why.Length > 0 ? " -- " + why : "");
                if (_readout.Text != readout) _readout.Text = readout;
            }
            catch (Exception ex)
            {
                Log.Exception("ShipSensorWindow.OnUpdatingUIState", ex);
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
                Log.Info("ship sensor at " + _part.Location + ": " + id + " -> " + value + ".");
            }
            catch (Exception ex) { Log.Exception("ShipSensorWindow.OnPicked", ex); }
        }
    }
}
