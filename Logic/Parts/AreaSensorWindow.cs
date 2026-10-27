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
    internal static class AreaPanelOpenPatch
    {
        public static readonly ID<PartTriggerGuiRules> TriggerId = new ID<PartTriggerGuiRules>("ztx_area_panel");

        static bool Prefix(PartTriggerButton __instance)
        {
            if (!__instance.Rules.TriggerID.Equals(TriggerId)) return true;
            try
            {
                foreach (Part p in __instance.Sim.PlayerInput.SelectedParts)
                {
                    if (PartUITrigger.GetOnPart(p, TriggerId) == null) continue;
                    AreaSensorWindow.Open(__instance.Game, p);
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Exception("AreaPanelOpenPatch.Prefix", ex);
            }
            return false;
        }
    }

    internal sealed class AreaSensorWindow : RailPartWindow
    {
        private static AreaSensorWindow s_instance;

        private readonly Dictionary<int, ListItem> _measureItems = new Dictionary<int, ListItem>();
        private readonly Dictionary<int, ListItem> _widthItems = new Dictionary<int, ListItem>();
        private readonly Dictionary<int, ListItem> _lengthItems = new Dictionary<int, ListItem>();
        private readonly Dictionary<int, ListItem> _positionItems = new Dictionary<int, ListItem>();
        private readonly DropList _measure, _width, _length, _position;
        private readonly Label _readout;

        public static bool Shows(Part p) => s_instance != null && s_instance.IsActive && ReferenceEquals(s_instance._part, p);

        public static void Open(GameRoot game, Part sensor)
        {
            if (game == null || sensor == null) return;
            if (s_instance == null || !ReferenceEquals(s_instance._game, game))
            {
                s_instance = new AreaSensorWindow(game);
                game.Gui.FloatingWindows.AddChild(s_instance);
            }
            s_instance._part = sensor;
            s_instance.SelfActive = true;
            game.Gui.FloatingWindows.Children.RaiseToFront(s_instance);
            SensorAreaOverlay.Attach(game.Sim);
        }

        public static void Reset()
        {
            s_instance = null;
        }

        private AreaSensorWindow(GameRoot game) : base(game, "ZtxAreaPanel/Title", new Vector2(460f, 250f))
        {
            _measure = AddRow("ZtxAreaPanel/Measure", "ZtxAreaPanel/TipMeasure", AreaSensorGeometry.MeasureId);
            _width = AddRow("ZtxAreaPanel/Width", "ZtxAreaPanel/TipWidth", AreaSensorGeometry.WidthId);
            _length = AddRow("ZtxAreaPanel/Length", "ZtxAreaPanel/TipLength", AreaSensorGeometry.LengthId);
            _position = AddRow("ZtxAreaPanel/Position", "ZtxAreaPanel/TipPosition", AreaSensorGeometry.PositionId);
            _readout = new Label { Height = RowHeight };
            AddChild(_readout);

            Fill(_measure, _measureItems, new List<(int, string)>
            {
                (AreaSensorGeometry.MEASURE_HEALTH, Text("ZtxAreaPanel/MeasureHealth")),
                (AreaSensorGeometry.MEASURE_PRESENT, Text("ZtxAreaPanel/MeasurePresent")),
                (AreaSensorGeometry.MEASURE_MISSING, Text("ZtxAreaPanel/MeasureMissing")),
                (AreaSensorGeometry.MEASURE_OTHER, Text("ZtxAreaPanel/MeasureOther")),
                (AreaSensorGeometry.MEASURE_AMMO, Text("ZtxAreaPanel/MeasureAmmo")),
            }, v => v == AreaSensorGeometry.MEASURE_HEALTH ? "ZtxAreaPanel/TipMeasureHealth"
                 : v == AreaSensorGeometry.MEASURE_PRESENT ? "ZtxAreaPanel/TipMeasurePresent"
                 : v == AreaSensorGeometry.MEASURE_MISSING ? "ZtxAreaPanel/TipMeasureMissing"
                 : v == AreaSensorGeometry.MEASURE_AMMO ? "ZtxAreaPanel/TipMeasureAmmo"
                 : "ZtxAreaPanel/TipMeasureOther");
            Fill(_width, _widthItems, Sizes());
            Fill(_length, _lengthItems, Sizes());
            Fill(_position, _positionItems, new List<(int, string)>
            {
                (AreaSensorGeometry.POSITION_FRONT, Text("ZtxAreaPanel/PosFront")),
                (AreaSensorGeometry.POSITION_CENTRED, Text("ZtxAreaPanel/PosCentred")),
            }, v => v == AreaSensorGeometry.POSITION_FRONT ? "ZtxAreaPanel/TipPosFront" : "ZtxAreaPanel/TipPosCentred");
        }

        private static List<(int, string)> Sizes()
        {
            var sizes = new List<(int, string)>();
            for (int i = 1; i <= AreaSensorGeometry.MAX_SIZE; i++) sizes.Add((i - 1, i.ToString()));
            return sizes;
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
                AreaSettings s = AreaSensorGeometry.Read(_part);
                Select(_measure, _measureItems, s.Measure);
                Select(_width, _widthItems, s.Width - 1);
                Select(_length, _lengthItems, s.Length - 1);
                Select(_position, _positionItems, s.Position);
                string readout = Readout(_part);
                if (_readout.Text != readout) _readout.Text = readout;
            }
            catch (Exception ex)
            {
                Log.Exception("AreaSensorWindow.OnUpdatingUIState", ex);
                SelfActive = false;
            }
        }

        private static string Readout(Part p)
        {
            float? percent = LogicNet.ReadValue(p);
            float? units = LogicNet.ReadUnits(p);
            bool on = LogicNet.ReadOutput(p);
            return Strings.FormatText("ZtxAreaPanel/ReadoutFmt",
                percent.HasValue ? ((int)percent.Value).ToString() : "-",
                units.HasValue ? ((int)units.Value).ToString() : "-",
                Text(on ? "ZtxLogicPanel/SigOn" : "ZtxLogicPanel/SigOff"));
        }

        private void OnPicked(ID<PartToggleGuiRules> id, WidgetEventArgs e)
        {
            if (_refreshing || _part == null) return;
            try
            {
                if (!(e.Widget.UserData is int value)) return;
                if (LogicSource.Mode(_part, id, -1) == value) return;
                Write((id, value));
                Log.Info("area sensor at " + _part.Location + ": " + id + " -> " + value + ".");
            }
            catch (Exception ex) { Log.Exception("AreaSensorWindow.OnPicked", ex); }
        }
    }
}
