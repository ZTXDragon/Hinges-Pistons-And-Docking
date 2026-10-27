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
    internal static class LampPanelOpenPatch
    {
        public static readonly ID<PartTriggerGuiRules> TriggerId = new ID<PartTriggerGuiRules>("ztx_lamp_panel");

        static bool Prefix(PartTriggerButton __instance)
        {
            if (!__instance.Rules.TriggerID.Equals(TriggerId)) return true;
            try
            {
                foreach (Part p in __instance.Sim.PlayerInput.SelectedParts)
                {
                    if (PartUITrigger.GetOnPart(p, TriggerId) == null) continue;
                    LampWindow.Open(__instance.Game, p);
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Exception("LampPanelOpenPatch.Prefix", ex);
            }
            return false;
        }
    }

    internal sealed class LampWindow : RailPartWindow
    {
        private static LampWindow s_instance;

        private readonly TextEditField _name;
        private readonly Dictionary<int, ListItem> _messageItems = new Dictionary<int, ListItem>();
        private readonly Dictionary<int, ListItem> _lightItems = new Dictionary<int, ListItem>();
        private readonly Dictionary<int, ListItem> _sizeItems = new Dictionary<int, ListItem>();
        private readonly DropList _message, _light, _size;
        private readonly ColorPicker _picker;
        private const float PickerHeight = 170f;
        private int _shownRgb = int.MinValue;
        private bool _picking;
        private int _lastN1 = int.MinValue, _lastN2 = int.MinValue;

        public static void Open(GameRoot game, Part lamp)
        {
            if (game == null || lamp == null) return;
            if (s_instance == null || !ReferenceEquals(s_instance._game, game))
            {
                s_instance = new LampWindow(game);
                game.Gui.FloatingWindows.AddChild(s_instance);
            }
            s_instance.Bind(lamp);
            s_instance.SelfActive = true;
            game.Gui.FloatingWindows.Children.RaiseToFront(s_instance);
        }

        public static void Reset()
        {
            s_instance = null;
        }

        private LampWindow(GameRoot game) : base(game, "ZtxLampPanel/Title", new Vector2(480f, 210f + PickerHeight))
        {
            LayoutBox r1 = Row();
            r1.DockedChildren.Add(RowLabel("ZtxLampPanel/Name", "ZtxLampPanel/TipName"));
            _name = LogicGui.Typing(new TextEditField(TextEditField.Style.SingleLine) { DockMode = DockMode.Left, Width = FieldWidth });
            _name.ToolTips.TextProvider = Strings.KeyString("ZtxLampPanel/TipName");
            _name.TextChanged += OnNameChanged;
            r1.DockedChildren.Add(_name);
            AddChild(r1);
            _message = AddRow("ZtxLampPanel/Message", "ZtxLampPanel/TipMessage", LampToggles.MessageId);
            _light = AddRow("ZtxLampPanel/Light", "ZtxLampPanel/TipLight", LampToggles.StateId);
            _size = AddRow("ZtxLampPanel/Size", "ZtxLampPanel/TipSize", LampToggles.SizeId);
            Fill(_message, _messageItems, new List<(int, string)> { (0, Text("ZtxLampPanel/Message_0")), (1, Text("ZtxLampPanel/Message_1")) },
                v => "ZtxLampPanel/TipMessage");
            Fill(_light, _lightItems, new List<(int, string)>
            {
                (LampToggles.STATE_OFF, Text("ZtxLampPanel/Light_0")), (LampToggles.STATE_ON, Text("ZtxLampPanel/Light_1")), (LampToggles.STATE_BLINK, Text("ZtxLampPanel/Light_2")),
            }, v => "ZtxLampPanel/TipLight");
            var sizes = new List<(int, string)>();
            for (int i = 0; i < LampToggles.SIZES; i++) sizes.Add((i, Text("ZtxLampPanel/Size_" + i)));
            Fill(_size, _sizeItems, sizes, v => "ZtxLampPanel/TipSize");
            LayoutBox colourRow = Row();
            colourRow.Height = PickerHeight;
            colourRow.DockedChildren.Add(RowLabel("ZtxLampPanel/Colour", "ZtxLampPanel/TipColour"));
            _picker = new ColorPicker(game, LampToggles.Presets[0]) { DockMode = DockMode.Left, Width = FieldWidth };
            _picker.NineSlice.Flags = NineSliceFlags.None;
            _picker.ColorChangeStarted += (s, e) => _picking = true;
            _picker.ColorChangeCommitted += (s, e) => OnColourCommitted();
            colourRow.DockedChildren.Add(_picker);
            AddChild(colourRow);
        }

        private void Bind(Part lamp)
        {
            _part = lamp;
            _refreshing = true;
            try
            {
                string name = LampToggles.StoredName(lamp);
                LogicGui.SetFieldText(_name, name);
                _lastN1 = LogicRuleDef.PackName(name, 0);
                _lastN2 = LogicRuleDef.PackName(name, 5);
                _shownRgb = int.MinValue;
                _picking = false;
            }
            finally { _refreshing = false; }
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
                Select(_message, _messageItems, LogicSource.Mode(_part, LampToggles.MessageId, 0));
                Select(_light, _lightItems, LogicSource.Mode(_part, LampToggles.StateId, 0));
                Select(_size, _sizeItems, LogicSource.Mode(_part, LampToggles.SizeId, 0));
                int rgb = LogicSource.Mode(_part, LampToggles.RgbId, 0);
                if (!_picking && rgb != _shownRgb)
                {
                    _shownRgb = rgb;
                    _refreshing = true;
                    try { _picker.Color = LampToggles.UnpackRgb(rgb) ?? LampToggles.Presets[0]; }
                    finally { _refreshing = false; }
                }
            }
            catch (Exception ex)
            {
                Log.Exception("LampWindow.OnUpdatingUIState", ex);
                SelfActive = false;
            }
        }

        private void OnNameChanged(object sender, EventArgs e)
        {
            if (_refreshing || _part == null) return;
            try
            {
                string clean = LogicRuleDef.Clean(_name.Text);
                int n1 = LogicRuleDef.PackName(clean, 0), n2 = LogicRuleDef.PackName(clean, 5);
                if (n1 == _lastN1 && n2 == _lastN2) return;
                _lastN1 = n1;
                _lastN2 = n2;
                WriteOnly(_part, (LampToggles.Name1Id, n1), (LampToggles.Name2Id, n2));
            }
            catch (Exception ex) { Log.Exception("LampWindow.OnNameChanged", ex); }
        }

        private void OnColourCommitted()
        {
            _picking = false;
            if (_refreshing || _part == null) return;
            try
            {
                int v = LampToggles.PackRgb(_picker.Color);
                _shownRgb = v;
                if (AllTargetsHave((LampToggles.RgbId, v))) return;
                Write((LampToggles.RgbId, v));
                Log.Info("indicator lamp at " + _part.Location + ": colour -> #" + (v & 0xFFFFFF).ToString("X6") + ".");
            }
            catch (Exception ex) { Log.Exception("LampWindow.OnColourCommitted", ex); }
        }

        private void OnPicked(ID<PartToggleGuiRules> id, WidgetEventArgs e)
        {
            if (_refreshing || _part == null) return;
            try
            {
                if (!(e.Widget.UserData is int value)) return;
                if (LogicSource.Mode(_part, id, int.MinValue) == value) return;
                Write((id, value));
                Log.Info("indicator lamp at " + _part.Location + ": " + id + " -> " + value + ".");
            }
            catch (Exception ex) { Log.Exception("LampWindow.OnPicked", ex); }
        }
    }
}
