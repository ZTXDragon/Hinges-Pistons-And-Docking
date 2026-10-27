using System;
using System.Collections.Generic;
using System.Text;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Gui;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Geometry;
using Halfling.Gui;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(PartTriggerButton), "OnClicked")]
    internal static class RailPanelOpenPatch
    {
        public static readonly ID<PartTriggerGuiRules> CartTriggerId = new ID<PartTriggerGuiRules>("ztx_rail_cart_panel");
        public static readonly ID<PartTriggerGuiRules> StopTriggerId = new ID<PartTriggerGuiRules>("ztx_rail_stop_panel");
        public static readonly ID<PartTriggerGuiRules> TerminalTriggerId = new ID<PartTriggerGuiRules>("ztx_rail_terminal_panel");

        static bool Prefix(PartTriggerButton __instance)
        {
            try
            {
                ID<PartTriggerGuiRules> id = __instance.Rules.TriggerID;
                bool cart = id.Equals(CartTriggerId);
                bool stop = id.Equals(StopTriggerId);
                bool terminal = id.Equals(TerminalTriggerId);
                if (!cart && !stop && !terminal) return true;
                Part target = null;
                foreach (Part p in __instance.Sim.PlayerInput.SelectedParts)
                {
                    if (PartUITrigger.GetOnPart(p, id) != null) { target = p; break; }
                }
                if (target == null) return false;
                if (cart) RailCartWindow.Open(__instance.Game, target);
                else if (terminal) RailTerminalWindow.Open(__instance.Game, target);
                else RailStopWindow.Open(__instance.Game, target);
            }
            catch (Exception ex)
            {
                Log.Exception("RailPanelOpenPatch.Prefix", ex);
            }
            return false;
        }
    }

    internal abstract partial class RailPartWindow : WindowBox
    {
        protected const float RowHeight = 28f;
        protected const float LabelWidth = 110f;
        protected const float FieldWidth = 300f;
        protected const float PartWindowWidth = LabelWidth + FieldWidth + 70f;
        private const float ChromeSlack = 4f;
        private const float SectionGap = 8f;
        private const float ScreenMargin = 40f;

        protected readonly GameRoot _game;
        protected Part _part;
        protected bool _refreshing;

        protected RailPartWindow(GameRoot game, string titleKey, Vector2 size)
        {
            _game = game;
            TitleTextProvider = Strings.KeyString(titleKey);
            _minWidth = Math.Max(size.X, PartWindowWidth);
            Size = new Vector2(_minWidth, size.Y);
            ResizeController.MinSize = Size;
            Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
            Children.BorderPadding = new Borders(10f);
            Children.WidgetPadding = new Vector2(5f);
            Parented += (s, e) =>
            {
                ScreenRect = Rect.CenteredOn(Parent.RectProvider.ScreenRect.Size / 2f, Size);
                BoundsProvider = Parent.RectProvider;
                _centerPending = true;
            };
            Unparenting += (s, e) => BoundsProvider = null;
            this.BeforeInputWhileActive(OnUpdatingUIState);
            this.BeforeInputWhileActive(OnFitting);
            WidgetRules.AddDialogTweens(this);
            WidgetRules.AddBlur(this);
        }

        protected static LayoutBox Row()
        {
            var box = new LayoutBox();
            box.NineSlice.Flags = NineSliceFlags.None;
            box.Height = RowHeight;
            box.Children.ClipChildren = false;
            box.DockedChildren.ClipChildren = false;
            box.DockedChildren.WidgetPadding = new Vector2(4f, 0f);
            return box;
        }

        protected static Label RowLabel(string key, string tipKey = null)
        {
            var l = new Label { DockMode = DockMode.Left, Width = LabelWidth };
            l.TextProvider = Strings.KeyString(key);
            if (tipKey != null) l.ToolTips.TextProvider = Strings.KeyString(tipKey);
            return l;
        }

        protected static DropList MakeList(float width)
        {
            var list = new DropList { DockMode = DockMode.Left, Width = width };
            list.StateNormalTextRenderer.XmlFormatting = true;
            list.StateHighlightedTextRenderer.XmlFormatting = true;
            list.StatePressedTextRenderer.XmlFormatting = true;
            list.StateDisabledTextRenderer.XmlFormatting = true;
            return list;
        }

        protected void Fill(DropList list, Dictionary<int, ListItem> items, List<(int Value, string Text)> entries, Func<int, string> tipKey = null)
        {
            _refreshing = true;
            try
            {
                foreach (ListItem old in items.Values) list.ListBox.Children.Remove(old);
                items.Clear();
                foreach (var (value, text) in entries)
                {
                    var item = new ListItem { Text = text, UserData = value };
                    item.StateNormalTextRenderer.XmlFormatting = true;
                    item.StateHighlightedTextRenderer.XmlFormatting = true;
                    item.StatePressedTextRenderer.XmlFormatting = true;
                    item.StateDisabledTextRenderer.XmlFormatting = true;
                    item.SelectedTextRenderer.XmlFormatting = true;
                    string tip = tipKey?.Invoke(value);
                    if (tip != null) item.ToolTips.TextProvider = Strings.KeyString(tip);
                    list.AddChild(item);
                    items[value] = item;
                }
            }
            finally { _refreshing = false; }
        }

        private readonly List<(LayoutBox Row, DropList List, Dictionary<int, ListItem> Items, ID<PartToggleGuiRules> Id, string[] TextKeys)> _toggleRows =
            new List<(LayoutBox, DropList, Dictionary<int, ListItem>, ID<PartToggleGuiRules>, string[])>();
        private Part _toggleRowsFor;
        private float _fitHeight = -1f;
        private bool _centerPending;
        private readonly float _minWidth;
        private readonly List<LayoutBox> _panels = new List<LayoutBox>();
        protected const float PanelPad = 6f;
        private const float FrameInset = 1f;

        protected void AddToggleRow(string labelKey, string tipKey, ID<PartToggleGuiRules> id, params string[] textKeys)
        {
            LayoutBox row = Row();
            row.DockedChildren.Add(RowLabel(labelKey, tipKey));
            DropList list = MakeList(FieldWidth);
            list.ToolTips.TextProvider = Strings.KeyString(tipKey);
            var items = new Dictionary<int, ListItem>();
            list.ListBox.SelectionManager.WidgetSelected += (s, e) =>
            {
                if (_refreshing || _part == null || !(e.Widget.UserData is int v)) return;
                if (AllTargetsHave((id, v))) return;
                Write((id, v));
            };
            row.DockedChildren.Add(list);
            AddChild(row);
            _toggleRows.Add((row, list, items, id, textKeys.Length > 0 ? textKeys : null));
        }

        protected int FitToggleRows()
        {
            Part p = _part;
            bool rebind = !ReferenceEquals(p, _toggleRowsFor);
            _toggleRowsFor = p;
            int shown = 0;
            foreach (var r in _toggleRows)
            {
                PartUIToggle t = p != null ? PartUIToggle.GetOnPart(p, r.Id) : null;
                bool has = t != null;
                if (r.Row.SelfActive != has) r.Row.SelfActive = has;
                if (!has) continue;
                shown++;
                if (rebind)
                {
                    var entries = new List<(int, string)>();
                    var tips = new Dictionary<int, string>();
                    foreach (var c in LogicConsoleModel.ChoicesFor(_game, p, r.Id))
                    {
                        string key = PartWindowFit.EntryKey(r.TextKeys, c.Mode);
                        entries.Add((c.Mode, key != null ? Text(key) : c.Text));
                        tips[c.Mode] = c.TipKey;
                    }
                    Fill(r.List, r.Items, entries, m => tips.TryGetValue(m, out string k) ? k : null);
                }
                bool mixed = false;
                foreach (Part target in Targets())
                {
                    PartUIToggle other = PartUIToggle.GetOnPart(target, r.Id);
                    if (other != null && other.Mode != t.Mode) { mixed = true; break; }
                }
                if (!mixed) Select(r.List, r.Items, t.Mode);
                else if (r.List.SelectedWidget != null)
                {
                    _refreshing = true;
                    try { r.List.SelectedWidget = null; }
                    finally { _refreshing = false; }
                }
            }
            return shown;
        }

        private void OnFitting(object sender, EventArgs e)
        {
            try
            {
                foreach (LayoutBox p in _panels) FitPanel(p);
                var heights = new List<float>();
                foreach (Widget w in Children)
                {
                    if (w.SelfActive) heights.Add(w.Height);
                }
                float chrome = TitleBox.Height + Children.BorderPadding.Top + Children.BorderPadding.Bottom
                    + NineSlice.Borders.Top + NineSlice.Borders.Bottom + ChromeSlack;
                float max = Parent != null ? Parent.RectProvider.ScreenRect.Size.Y - ScreenMargin : 0f;
                float need = PartWindowFit.Height(chrome, Children.WidgetPadding.Y, heights, max);
                if (Math.Abs(need - _fitHeight) < 0.5f && !_centerPending) return;
                _fitHeight = need;
                ResizeController.MinSize = new Vector2(_minWidth, need);
                Size = new Vector2(Math.Max(Size.X, _minWidth), need);
                if (_centerPending && Parent != null)
                {
                    _centerPending = false;
                    ScreenRect = Rect.CenteredOn(Parent.RectProvider.ScreenRect.Size / 2f, Size);
                }
            }
            catch (Exception ex)
            {
                Log.Exception("RailPartWindow.OnFitting", ex);
            }
        }

        protected LayoutBox FramedPanel(string titleKey)
        {
            var box = new LayoutBox();
            box.NineSlice.CopySettingsFrom(WidgetRules.Instance.HollowWidget.NineSlice);
            box.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
            box.Children.BorderPadding = new Borders(PanelPad);
            box.Children.WidgetPadding = new Vector2(4f);
            box.Children.ClipChildren = false;
            if (titleKey != null)
            {
                var heading = new Label { Height = RowHeight };
                heading.TextRenderer.XmlFormatting = true;
                heading.Text = LogicPalette.Wrap(LogicPalette.Heading, Text(titleKey));
                box.AddChild(heading);
            }
            AddChild(box);
            _panels.Add(box);
            return box;
        }

        protected static void FrameRow(LayoutBox row)
        {
            row.NineSlice.CopySettingsFrom(WidgetRules.Instance.HollowWidget.NineSlice);
            row.DockedChildren.BorderPadding = new Borders(3f);
            row.Height = RowHeight + 2f * (3f + FrameInset);
        }

        private static void FitPanel(LayoutBox box)
        {
            float h = 0f;
            int n = 0;
            foreach (Widget c in box.Children)
            {
                if (!c.SelfActive) continue;
                h += c.Height;
                n++;
            }
            h += Math.Max(0, n - 1) * box.Children.WidgetPadding.Y + box.Children.BorderPadding.Top + box.Children.BorderPadding.Bottom + 2f * FrameInset;
            if (Math.Abs(box.Height - h) > 0.5f) box.Height = h;
        }

        protected void AddSection(string titleKey)
        {
            AddChild(new Label { Height = SectionGap });
            var heading = new Label { Height = RowHeight };
            heading.TextRenderer.XmlFormatting = true;
            heading.Text = LogicPalette.Wrap(LogicPalette.Heading, Text(titleKey));
            AddChild(heading);
        }

        protected static void ShowText(Label label, string text)
        {
            if (label.Text != text) label.Text = text;
            float h = PartWindowFit.TextHeight(text, RowHeight);
            if (Math.Abs(label.Height - h) > 0.5f) label.Height = h;
        }

        protected void SetToggleRowInput(ID<PartToggleGuiRules> id, bool on)
        {
            foreach (var r in _toggleRows)
            {
                if (r.Id.Equals(id) && r.List.SelfInputActive != on) r.List.SelfInputActive = on;
            }
        }

        protected void Select(DropList list, Dictionary<int, ListItem> items, int value)
        {
            if (!items.TryGetValue(value, out ListItem item) || ReferenceEquals(list.SelectedWidget, item)) return;
            _refreshing = true;
            try { list.SelectedWidget = item; }
            finally { _refreshing = false; }
        }

        protected void Write(params (ID<PartToggleGuiRules> Id, int Value)[] writes)
        {
            if (_part == null) return;
            var input = new MPSetUITogglesInput();
            foreach (Part target in Targets())
                foreach (var (id, value) in writes)
                {
                    PartUIToggle t = PartUIToggle.GetOnPart(target, id);
                    if (t != null) input.ToggleValues.Add((t, value));
                }
            if (!input.IsEmpty) _game.NetManager.EnqueueInput(input);
        }

        protected void WriteOnly(Part part, params (ID<PartToggleGuiRules> Id, int Value)[] writes)
        {
            if (part == null) return;
            var input = new MPSetUITogglesInput();
            foreach (var (id, value) in writes)
            {
                PartUIToggle t = PartUIToggle.GetOnPart(part, id);
                if (t != null) input.ToggleValues.Add((t, value));
            }
            if (!input.IsEmpty) _game.NetManager.EnqueueInput(input);
        }

        protected void WriteValues(params (ID<PartToggleGuiRules> Id, int Value)[] writes)
        {
            if (_part?.Ship == null) return;
            _game.NetManager.EnqueueInput(GripStore.WriteInputAll(Targets(), writes));
        }

        protected readonly List<Part> _targets = new List<Part>();
        private Label _selectionNote;

        protected bool AllTargetsHave(params (ID<PartToggleGuiRules> Id, int Value)[] values)
        {
            foreach (Part p in Targets())
                foreach (var (id, v) in values)
                    if (p == null || LogicSource.Mode(p, id, int.MinValue) != v) return false;
            return true;
        }

        protected IReadOnlyList<Part> Targets() => _targets.Count > 0 ? (IReadOnlyList<Part>)_targets : new[] { _part };

        protected virtual bool SameKind(Part p) => _part != null && ReferenceEquals(p.Rules, _part.Rules);

        protected virtual void Rebind(Part p) => _part = p;

        protected void RefreshTargets()
        {
            if (_part == null) return;
            IEnumerable<Part> selected = _game?.Sim?.PlayerInput?.SelectedParts;
            List<Part> list = PartSelection.Pick(selected, _part, p => !p.IsDestroyed && p.Ship != null && SameKind(p), out Part display);
            _targets.Clear();
            _targets.AddRange(list);
            if (!ReferenceEquals(display, _part)) Rebind(display);
            if (_selectionNote == null)
            {
                _selectionNote = new Label { Height = RowHeight };
                _selectionNote.TextRenderer.XmlFormatting = true;
                Children.Insert(0, _selectionNote);
            }
            string note = _targets.Count > 1 ? LogicPalette.Wrap(LogicPalette.Warn, Strings.FormatText("ZtxPartPanel/EditingFmt", _targets.Count)) : "";
            if (_selectionNote.Text != note) _selectionNote.Text = note;
            bool show = note.Length > 0;
            if (_selectionNote.SelfActive != show) _selectionNote.SelfActive = show;
        }

        protected bool PartGone()
        {
            Part p = _part;
            return p == null || p.IsDestroyed || p.Ship == null || !p.Ship.ExistsInSim(_game.Sim);
        }

        protected abstract void OnUpdatingUIState(object sender, EventArgs e);

        protected static string Text(string key)
        {
            return Strings.KeyString(key).ToString();
        }
    }

    internal sealed class RailCartWindow : RailPartWindow
    {
        private static RailCartWindow s_instance;
        public static bool IsShowing => s_instance != null && s_instance.IsActive;

        private static readonly ID<PartToggleGuiRules> s_modeId = new ID<PartToggleGuiRules>("ztx_rail_mode");
        private static readonly ID<PartToggleGuiRules> s_dwellId = new ID<PartToggleGuiRules>("ztx_rail_dwell");
        private static readonly ID<PartToggleGuiRules> s_target1Id = new ID<PartToggleGuiRules>("ztx_rail_tgt_1");
        private static readonly ID<PartToggleGuiRules> s_target2Id = new ID<PartToggleGuiRules>("ztx_rail_tgt_2");
        private static readonly ID<PartToggleGuiRules> s_speedId = new ID<PartToggleGuiRules>("ztx_rail_speed");
        private static readonly ID<PartToggleGuiRules> s_driveId = new ID<PartToggleGuiRules>("ztx_rail_drive");
        private static readonly ID<PartToggleGuiRules> s_mergeId = new ID<PartToggleGuiRules>("ztx_rail_merge");
        private static readonly ID<PartToggleGuiRules> s_gripId = new ID<PartToggleGuiRules>("ztx_rail_grip");
        private static readonly ID<PartToggleGuiRules> s_cartName1Id = new ID<PartToggleGuiRules>("ztx_rail_cname_1");
        private static readonly ID<PartToggleGuiRules> s_cartName2Id = new ID<PartToggleGuiRules>("ztx_rail_cname_2");

        private readonly TextEditField _name;
        private readonly Label _idLine;
        private int _lastN1 = int.MinValue, _lastN2 = int.MinValue;
        private readonly DropList _mode, _target;
        private readonly Dictionary<int, ListItem> _modeItems = new Dictionary<int, ListItem>();
        private readonly Dictionary<int, ListItem> _targetItems = new Dictionary<int, ListItem>();
        private readonly TextEditField _speedField, _powerField, _dwellField;
        private string _lastSpeed, _lastPower, _lastDwell;
        private readonly List<string> _targetNames = new List<string>();
        private readonly Label _status;
        private readonly Label _motion;
        private string _stationSig;

        public static void Open(GameRoot game, Part cart)
        {
            if (game == null || cart == null) return;
            if (s_instance == null || !ReferenceEquals(s_instance._game, game))
            {
                s_instance = new RailCartWindow(game);
                game.Gui.FloatingWindows.AddChild(s_instance);
            }
            s_instance.Bind(cart);
            s_instance.SelfActive = true;
            game.Gui.FloatingWindows.Children.RaiseToFront(s_instance);
            LogicConsoleHighlight.Attach(game.Sim);
            SensorAreaOverlay.Attach(game.Sim);
        }

        public static Part Current => IsShowing ? s_instance._part : null;

        public static void Reset()
        {
            s_instance = null;
        }

        private readonly Dictionary<int, Part> _targetParts = new Dictionary<int, Part>();
        private Part _hovered;

        private readonly Label _globalNote;
        private bool? _lastGlobal;

        private RailCartWindow(GameRoot game) : base(game, "ZtxRailPanel/CartTitle", new Vector2(460f, 400f))
        {
            SelfDeactivated += delegate { _hovered = null; LogicConsoleWindow.HoveredPart = null; };
            LayoutBox r0 = Row();
            r0.DockedChildren.Add(RowLabel("ZtxRailPanel/CartName", "ZtxRailPanel/TipCartName"));
            _name = LogicGui.Typing(new TextEditField(TextEditField.Style.SingleLine) { DockMode = DockMode.Left, Width = FieldWidth });
            _name.ToolTips.TextProvider = Strings.KeyString("ZtxRailPanel/TipCartName");
            _name.TextChanged += OnNameChanged;
            r0.DockedChildren.Add(_name);
            AddChild(r0);
            _idLine = new Label { Height = RowHeight };
            AddChild(_idLine);

            LayoutBox r1 = Row();
            r1.DockedChildren.Add(RowLabel("ZtxRailPanel/Mode", "ZtxRailPanel/TipMode"));
            _mode = MakeList(FieldWidth);
            _mode.ToolTips.TextProvider = Strings.KeyString("ZtxRailPanel/TipMode");
            _mode.ListBox.SelectionManager.WidgetSelected += (s, e) => OnPicked(s_modeId, e);
            r1.DockedChildren.Add(_mode);
            AddChild(r1);

            LayoutBox r2 = Row();
            r2.DockedChildren.Add(RowLabel("ZtxRailPanel/Target", "ZtxRailPanel/TipTarget"));
            _target = MakeList(FieldWidth);
            _target.ToolTips.TextProvider = Strings.KeyString("ZtxRailPanel/TipTarget");
            _target.ListBox.SelectionManager.WidgetSelected += OnTargetPicked;
            r2.DockedChildren.Add(_target);
            AddChild(r2);

            _speedField = AddValueRow("ZtxRailPanel/Speed", "ZtxRailPanel/UnitCellsS", "ZtxRailPanel/TipSpeed", 0);
            _powerField = AddValueRow("ZtxRailPanel/Power", "ZtxRailPanel/UnitPercent", "ZtxRailPanel/TipPower", 1);
            _dwellField = AddValueRow("ZtxRailPanel/Dwell", "ZtxRailPanel/UnitSeconds", "ZtxRailPanel/TipDwell", 2);
            AddToggleRow("ZtxRailPanel/Drive", "ZtxRailPanel/TipDrive", s_driveId, "ZtxRailPanel/DriveCart", "ZtxRailPanel/DriveThrusters");
            AddToggleRow("ZtxPartPanel/Merge", "ZtxRailPanel/TipMerge", s_mergeId, "ZtxRailPanel/MergeOff", "ZtxRailPanel/MergeOn");
            AddToggleRow("ZtxPartPanel/Grip", "ZtxRailPanel/TipGrip", s_gripId, "ZtxRailPanel/GripOff", "ZtxRailPanel/GripAttach", "ZtxRailPanel/GripRelease");

            _globalNote = new Label { Height = RowHeight };
            _globalNote.TextRenderer.XmlFormatting = true;
            _globalNote.SelfActive = false;
            AddChild(_globalNote);

            AddGripArea();

            _status = new Label { Height = RowHeight };
            AddChild(_status);
            _motion = new Label { Height = RowHeight };
            AddChild(_motion);

            Fill(_mode, _modeItems, new List<(int, string)>
            {
                (RailShipManager.MODE_GOTO, Text("ZtxRailPanel/ModeGoTo")),
                (RailShipManager.MODE_CYCLE, Text("ZtxRailPanel/ModeCycle")),
                (RailShipManager.MODE_SHUTTLE, Text("ZtxRailPanel/ModeShuttle")),
                (RailShipManager.MODE_FORWARD, Text("ZtxRailPanel/ModeForward")),
                (RailShipManager.MODE_BACKWARD, Text("ZtxRailPanel/ModeBackward")),
                (RailCartRules.MODE_HOLD, Text("ZtxRailPanel/ModeHold")),
            });
        }

        private TextEditField AddValueRow(string labelKey, string unitKey, string tipKey, int which)
        {
            LayoutBox r = Row();
            r.DockedChildren.Add(RowLabel(labelKey, tipKey));
            var f = LogicGui.Typing(new TextEditField(TextEditField.Style.SingleLine) { DockMode = DockMode.Left, Width = 100f });
            f.ToolTips.TextProvider = Strings.KeyString(tipKey);
            f.TextChanged += (sender, e) => OnValueTyped(which);
            r.DockedChildren.Add(f);
            var unit = new Label { DockMode = DockMode.Left, Width = 80f };
            unit.TextProvider = Strings.KeyString(unitKey);
            r.DockedChildren.Add(unit);
            AddChild(r);
            return f;
        }

        private void ShowValues(Part p)
        {
            string sp = RailShipManager.TypedSpeed(p).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            string pw = RailShipManager.PowerPercent(p).ToString(System.Globalization.CultureInfo.InvariantCulture);
            string dw = RailShipManager.DwellSeconds(p).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            _refreshing = true;
            try
            {
                if (sp != _lastSpeed) { _lastSpeed = sp; LogicGui.SetFieldText(_speedField, sp); }
                if (pw != _lastPower) { _lastPower = pw; LogicGui.SetFieldText(_powerField, pw); }
                if (dw != _lastDwell) { _lastDwell = dw; LogicGui.SetFieldText(_dwellField, dw); }
            }
            finally { _refreshing = false; }
        }

        private void OnValueTyped(int which)
        {
            if (_refreshing || _part?.Ship == null) return;
            try
            {
                TextEditField f = which == 0 ? _speedField : which == 1 ? _powerField : _dwellField;
                string t = (f.Text ?? "").Trim().Replace(',', '.');
                if (t.Length == 0 || !float.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) return;
                ID<PartToggleGuiRules> id;
                int stored;
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                if (which == 0) { id = RailToggles.SpeedValueId; stored = RailCartRules.EncodeSpeed(v); _lastSpeed = RailCartRules.DecodeSpeed(stored, 0).ToString("0.#", inv); }
                else if (which == 1) { id = RailToggles.PowerValueId; stored = RailCartRules.EncodePower((int)Math.Round(v)); _lastPower = RailCartRules.DecodePower(stored, 0).ToString(inv); }
                else { id = RailToggles.DwellValueId; stored = RailCartRules.EncodeDwell(v); _lastDwell = RailCartRules.DecodeDwell(stored, 0).ToString("0.#", inv); }
                WriteValues((id, stored));
                Log.Info("rail cart at " + _part.Location + ": " + (which == 0 ? "speed" : which == 1 ? "power" : "dwell") + " -> " + t + ".");
            }
            catch (Exception ex) { Log.Exception("RailCartWindow.OnValueTyped", ex); }
        }

        protected override bool SameKind(Part p) => ZtxRailCartComponent.ForPart(p) != null;

        protected override void Rebind(Part p) => Bind(p);

        private void Bind(Part cart)
        {
            _part = cart;
            _lastGlobal = null;
            _stationSig = null;
            _lastSpeed = _lastPower = _lastDwell = null;
            _refreshing = true;
            try
            {
                string name = RailShipManager.CartName(cart);
                LogicGui.SetFieldText(_name, name);
                RailCartRules.PackName(name, out _lastN1, out _lastN2);
            }
            finally { _refreshing = false; }
        }

        protected override void OnUpdatingUIState(object sender, EventArgs e)
        {
            try
            {
                if (PartGone()) { SelfActive = false; return; }
                RefreshTargets();
                Part p = _part;
                ZtxRailTerminalComponent terminal = RailShipManager.TerminalFor(p);
                var entries = new List<(int, string)> { (0, Text("ZtxRailPanel/Home")) };
                _targetNames.Clear();
                _targetNames.Add("");
                var sig = new StringBuilder();
                if (terminal != null)
                {
                    foreach (Part s in terminal.Network.Stations)
                    {
                        string name = RailNetwork.DisplayName(s);
                        _targetNames.Add(name);
                        entries.Add((_targetNames.Count - 1, name));
                        sig.Append(name).Append('|');
                    }
                }
                string stored = RailShipManager.TargetName(p);
                int current = 0;
                for (int i = 1; i < _targetNames.Count; i++)
                    if (RailNetwork.Key(_targetNames[i]) == RailNetwork.Key(stored)) { current = i; break; }
                if (current == 0 && stored.Length > 0)
                {
                    _targetNames.Add(stored);
                    entries.Add((_targetNames.Count - 1, stored + " (?)"));
                    current = _targetNames.Count - 1;
                    sig.Append(stored).Append("?|");
                }
                string signature = sig.ToString();
                if (signature != _stationSig)
                {
                    Fill(_target, _targetItems, entries);
                    _stationSig = signature;
                    _targetParts.Clear();
                    _hovered = null;
                    if (terminal != null)
                        for (int i = 1; i < _targetNames.Count; i++)
                            _targetParts[i] = terminal.Network.FindStation(_targetNames[i]);
                }
                Select(_target, _targetItems, current);
                _hovered = null;
                foreach (var kv in _targetItems)
                    if (kv.Value.IsActive && kv.Value.IsMouseOver && _targetParts.TryGetValue(kv.Key, out Part hp) && hp != null) { _hovered = hp; break; }
                Part outline = _hovered;
                if (outline == null && current > 0) _targetParts.TryGetValue(current, out outline);
                LogicConsoleWindow.HoveredPart = outline;
                Select(_mode, _modeItems, RailShipManager.Mode(p));
                ShowValues(p);
                FitToggleRows();
                RefreshGripArea();
                bool global = RailShipManager.GlobalSource(p) != null;
                if (_lastGlobal != global)
                {
                    _lastGlobal = global;
                    _mode.SelfInputActive = _target.SelfInputActive = !global;
                    _speedField.SelfInputActive = _powerField.SelfInputActive = _dwellField.SelfInputActive = !global;
                    _globalNote.Text = global ? LogicPalette.Wrap(LogicPalette.Warn, Text("ZtxRailPanel/CartGlobalNote")) : "";
                    _globalNote.SelfActive = global;
                    SetToggleRowInput(s_driveId, !global);
                    SetToggleRowInput(s_mergeId, !global);
                }
                string status = RailShipManager.CartStatus(p);
                ShowText(_status, status);
                string idText = Strings.FormatText("ZtxRailPanel/CartIdFmt", RailShipManager.CartId(p));
                if (_idLine.Text != idText) _idLine.Text = idText;
                string motion = RailShipManager.CartMotion(p);
                ShowText(_motion, motion);
            }
            catch (Exception ex)
            {
                Log.Exception("RailCartWindow.OnUpdatingUIState", ex);
                SelfActive = false;
            }
        }

        private void OnPicked(ID<PartToggleGuiRules> id, WidgetEventArgs e)
        {
            if (_refreshing || _part == null) return;
            try
            {
                if (!(e.Widget.UserData is int value)) return;
                if (AllTargetsHave((id, value))) return;
                Write((id, value));
            }
            catch (Exception ex) { Log.Exception("RailCartWindow.OnPicked", ex); }
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
                WriteOnly(_part, (s_cartName1Id, n1), (s_cartName2Id, n2));
                Log.Info("rail cart at " + _part.Location + ": name -> " + RailCartRules.UnpackName(n1, n2) + ".");
            }
            catch (Exception ex) { Log.Exception("RailCartWindow.OnNameChanged", ex); }
        }

        private void OnTargetPicked(object sender, WidgetEventArgs e)
        {
            if (_refreshing || _part == null) return;
            try
            {
                if (!(e.Widget.UserData is int index) || index < 0 || index >= _targetNames.Count) return;
                string name = RailNetwork.Key(_targetNames[index]);
                int n1 = LogicRuleDef.PackName(name, 0), n2 = LogicRuleDef.PackName(name, 5);
                if (AllTargetsHave((s_target1Id, n1), (s_target2Id, n2))) return;
                Write((s_target1Id, n1), (s_target2Id, n2));
                Log.Info("rail cart at " + _part.Location + ": target -> " + (name.Length == 0 ? "HOME" : name) + ".");
            }
            catch (Exception ex) { Log.Exception("RailCartWindow.OnTargetPicked", ex); }
        }
    }

    internal sealed class RailStopWindow : RailPartWindow
    {
        private static RailStopWindow s_instance;
        public static bool IsShowing => s_instance != null && s_instance.IsActive;

        private static readonly ID<PartToggleGuiRules> s_name1Id = new ID<PartToggleGuiRules>("ztx_rail_name_1");
        private static readonly ID<PartToggleGuiRules> s_name2Id = new ID<PartToggleGuiRules>("ztx_rail_name_2");

        private readonly TextEditField _name;
        private readonly Label _stations;
        private int _lastN1 = int.MinValue, _lastN2 = int.MinValue;

        public static void Open(GameRoot game, Part stop)
        {
            if (game == null || stop == null) return;
            if (s_instance == null || !ReferenceEquals(s_instance._game, game))
            {
                s_instance = new RailStopWindow(game);
                game.Gui.FloatingWindows.AddChild(s_instance);
            }
            s_instance.Bind(stop);
            s_instance.SelfActive = true;
            game.Gui.FloatingWindows.Children.RaiseToFront(s_instance);
            LogicConsoleHighlight.Attach(game.Sim);
        }

        public static void Reset()
        {
            s_instance = null;
        }

        private RailStopWindow(GameRoot game) : base(game, "ZtxRailPanel/StopTitle", new Vector2(460f, 150f))
        {
            LayoutBox r1 = Row();
            r1.DockedChildren.Add(RowLabel("ZtxRailPanel/Name", "ZtxRailPanel/TipStationName"));
            _name = LogicGui.Typing(new TextEditField(TextEditField.Style.SingleLine) { DockMode = DockMode.Left, Width = FieldWidth });
            _name.ToolTips.TextProvider = Strings.KeyString("ZtxRailPanel/TipStationName");
            _name.TextChanged += OnNameChanged;
            r1.DockedChildren.Add(_name);
            AddChild(r1);
            _stations = new Label { Height = RowHeight * 2f };
            _stations.TextRenderer.XmlFormatting = true;
            AddChild(_stations);
            SelfDeactivated += delegate { LogicConsoleWindow.HoveredPart = null; };
        }

        private void Bind(Part stop)
        {
            _part = stop;
            _refreshing = true;
            try
            {
                string name = RailNetwork.StationName(stop);
                LogicGui.SetFieldText(_name, name);
                _lastN1 = LogicRuleDef.PackName(name, 0);
                _lastN2 = LogicRuleDef.PackName(name, 5);
            }
            finally { _refreshing = false; }
        }

        protected override void OnUpdatingUIState(object sender, EventArgs e)
        {
            try
            {
                if (PartGone()) { SelfActive = false; return; }
                Part p = _part;
                if (!RailCartWindow.IsShowing) LogicConsoleWindow.HoveredPart = p;
                RailShipManager m = RailShipManager.TryGet(p.Ship);
                var sb = new StringBuilder();
                sb.Append(Text("ZtxRailPanel/Stations")).Append(' ');
                bool any = false;
                if (m != null)
                {
                    ZtxRailTerminalComponent terminal = null;
                    foreach (ZtxRailTerminalComponent t in m.Terminals)
                        if (t.Network.Contains(p)) { terminal = t; break; }
                    if (terminal != null)
                    {
                        foreach (Part s in terminal.Network.Stations)
                        {
                            if (any) sb.Append(", ");
                            sb.Append(RailNetwork.DisplayName(s));
                            any = true;
                        }
                    }
                }
                if (!any) sb.Append("-");
                string text = sb.ToString();
                if (_stations.Text != text) _stations.Text = text;
            }
            catch (Exception ex)
            {
                Log.Exception("RailStopWindow.OnUpdatingUIState", ex);
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
                Write((s_name1Id, n1), (s_name2Id, n2));
            }
            catch (Exception ex) { Log.Exception("RailStopWindow.OnNameChanged", ex); }
        }
    }
}
