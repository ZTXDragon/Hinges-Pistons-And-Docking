using System;
using System.Collections.Generic;
using System.Globalization;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Geometry;
using Halfling.Gui;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class RailTerminalWindow : RailPartWindow
    {
        private static RailTerminalWindow s_instance;

        public static bool IsShowing => s_instance != null && s_instance.IsActive;

        private const float W_NAME = 110f, W_STATE = 95f, W_MODE = 95f, W_TARGET = 120f, W_NUM = 60f, W_DRIVE = 85f, W_MERGE = 60f, W_LOAD = 70f;

        private readonly Label _summary, _empty, _dups;
        private readonly TextEditField _gapField;
        private string _lastGap;
        private readonly Button _holdButton;
        private bool? _lastHold;
        private readonly Button _globalButton, _driveButton, _mergeButton;
        private readonly PickerCell _gMode, _gTarget;
        private readonly TextCell _gSpeed, _gPower, _gDwell;
        private bool? _lastGlobal, _lastMerge;
        private int _lastDrive = -1;
        private readonly LogicPickerPopup _picker;
        private readonly TextCellEditor _cells;
        private readonly List<string> _stationNames = new List<string>();
        private readonly List<CartRow> _rows = new List<CartRow>();
        private readonly LayoutBox _cartsPanel;
        private string _cartSig;
        private static readonly List<(int, string)> s_modeItems = new List<(int, string)>();

        private sealed class CartRow
        {
            public Part Cart;
            public LayoutBox Box;
            public Button Name;
            public Label State, Load;
            public PickerCell Mode, Target;
            public TextCell Speed, Power, Dwell;
            public Button Drive, Merge;
            public bool? Greyed;
        }

        public static void Open(GameRoot game, Part terminal)
        {
            if (game == null || terminal == null) return;
            if (s_instance == null || !ReferenceEquals(s_instance._game, game))
            {
                s_instance = new RailTerminalWindow(game);
                game.Gui.FloatingWindows.AddChild(s_instance);
            }
            s_instance._part = terminal;
            s_instance._cartSig = null;
            s_instance._lastGap = null;
            s_instance._lastHold = null;
            s_instance._lastGlobal = s_instance._lastMerge = null;
            s_instance._lastDrive = -1;
            s_instance.SelfActive = true;
            game.Gui.FloatingWindows.Children.RaiseToFront(s_instance);
            LogicConsoleHighlight.Attach(game.Sim);
        }

        public static void Reset()
        {
            s_instance = null;
        }

        private RailTerminalWindow(GameRoot game) : base(game, "ZtxRailPanel/TerminalTitle", new Vector2(900f, 420f))
        {
            if (s_modeItems.Count == 0)
                foreach (int m in new[] { RailShipManager.MODE_GOTO, RailShipManager.MODE_CYCLE, RailShipManager.MODE_SHUTTLE,
                    RailShipManager.MODE_FORWARD, RailShipManager.MODE_BACKWARD, RailCartRules.MODE_HOLD })
                    s_modeItems.Add((m, ModeText(m)));

            _summary = new Label { Height = RowHeight };
            _summary.TextRenderer.XmlFormatting = true;
            AddChild(_summary);
            _dups = new Label { Height = RowHeight };
            _dups.TextRenderer.XmlFormatting = true;
            AddChild(_dups);
            SelfDeactivated += delegate { LogicConsoleWindow.HoveredPart = null; };

            LayoutBox railPanel = FramedPanel("ZtxRailPanel/SectionRail");
            LayoutBox railRow = Row();
            railRow.DockedChildren.Add(RowLabel("ZtxRailPanel/CartGap", "ZtxRailPanel/TipCartGap"));
            _gapField = LogicGui.Typing(new TextEditField(TextEditField.Style.SingleLine) { DockMode = DockMode.Left, Width = 70f });
            _gapField.ToolTips.TextProvider = Strings.KeyString("ZtxRailPanel/TipCartGap");
            _gapField.TextChanged += (sender, e) => OnGapTyped();
            railRow.DockedChildren.Add(_gapField);
            railRow.DockedChildren.Add(Unit("ZtxRailPanel/UnitCells", 60f));
            railRow.DockedChildren.Add(RowLabel("ZtxRailPanel/ShipHold", "ZtxRailPanel/TipShipHold"));
            _holdButton = LogicGui.SmallButton("ZtxRailPanel/ShipHoldOn", 300f, LogicPalette.ActMisc, "ZtxRailPanel/TipShipHold");
            _holdButton.Clicked += delegate { OnHoldClicked(); };
            railRow.DockedChildren.Add(_holdButton);
            railPanel.AddChild(railRow);

            LayoutBox globalPanel = FramedPanel("ZtxRailPanel/SectionGlobal");
            LayoutBox g1 = Row();
            _globalButton = LogicGui.SmallButton("ZtxRailPanel/GlobalOff", 420f, LogicPalette.ActMisc, "ZtxRailPanel/TipGlobal");
            _globalButton.Clicked += delegate { OnGlobalClicked(); };
            g1.DockedChildren.Add(_globalButton);
            globalPanel.AddChild(g1);

            _picker = new LogicPickerPopup(this);
            _cells = new TextCellEditor(this);

            LayoutBox g2 = Row();
            g2.DockedChildren.Add(RowLabel("ZtxRailPanel/Mode", "ZtxRailPanel/TipMode"));
            _gMode = new PickerCell(_picker, W_MODE + 20f, "ZtxRailPanel/Mode") { Picked = v => WriteTerminal("mode -> " + ModeText(v), (RailShipManager.ModeToggleId, v)) };
            g2.DockedChildren.Add(_gMode.Button);
            g2.DockedChildren.Add(RowLabel("ZtxRailPanel/Target", "ZtxRailPanel/TipTarget"));
            _gTarget = new PickerCell(_picker, W_TARGET + 20f, "ZtxRailPanel/Target") { Picked = OnGlobalTargetPicked };
            g2.DockedChildren.Add(_gTarget.Button);
            g2.DockedChildren.Add(RowLabel("ZtxRailPanel/Drive", "ZtxRailPanel/TipDrive"));
            _driveButton = LogicGui.SmallButton("ZtxRailPanel/DriveCart", W_DRIVE + 10f, LogicPalette.ActMisc, "ZtxRailPanel/TipDrive");
            _driveButton.Clicked += delegate { OnGlobalDriveClicked(); };
            g2.DockedChildren.Add(_driveButton);
            globalPanel.AddChild(g2);

            LayoutBox g3 = Row();
            g3.DockedChildren.Add(RowLabel("ZtxRailPanel/Speed", "ZtxRailPanel/TipSpeed"));
            _gSpeed = NumberCell(g3, "ZtxRailPanel/TipSpeed", "ZtxRailPanel/UnitCellsS", () => OnGlobalValueTyped(0));
            g3.DockedChildren.Add(RowLabel("ZtxRailPanel/Power", "ZtxRailPanel/TipPower"));
            _gPower = NumberCell(g3, "ZtxRailPanel/TipPower", "ZtxRailPanel/UnitPercent", () => OnGlobalValueTyped(1));
            g3.DockedChildren.Add(RowLabel("ZtxRailPanel/Dwell", "ZtxRailPanel/TipDwell"));
            _gDwell = NumberCell(g3, "ZtxRailPanel/TipDwell", "ZtxRailPanel/UnitSeconds", () => OnGlobalValueTyped(2));
            g3.DockedChildren.Add(RowLabel("ZtxRailPanel/Merge", "ZtxRailPanel/TipMerge"));
            _mergeButton = LogicGui.SmallButton("ZtxRailPanel/MergeOff", W_MERGE + 10f, LogicPalette.ActMisc, "ZtxRailPanel/TipMerge");
            _mergeButton.Clicked += delegate { OnGlobalMergeClicked(); };
            g3.DockedChildren.Add(_mergeButton);
            globalPanel.AddChild(g3);
            _gMode.SetItems(new List<(int, string)>(s_modeItems));

            _cartsPanel = FramedPanel("ZtxRailPanel/SectionCarts");
            LayoutBox head = Row();
            foreach (var (key, w) in new[] { ("ColCart", W_NAME), ("ColState", W_STATE), ("ColMode", W_MODE), ("ColTarget", W_TARGET),
                ("ColSpeed", W_NUM), ("ColPower", W_NUM), ("ColDwell", W_NUM), ("ColDrive", W_DRIVE), ("ColMerge", W_MERGE), ("ColLoad", W_LOAD) })
            {
                var l = new Label { DockMode = DockMode.Left, Width = w };
                l.TextRenderer.XmlFormatting = true;
                l.Text = LogicPalette.Wrap(LogicPalette.Heading, Text("ZtxRailPanel/" + key));
                head.DockedChildren.Add(l);
            }
            head.DockedChildren.BorderPadding = new Borders(3f + 1f, 0f, 0f, 0f);
            _cartsPanel.AddChild(head);
            _empty = new Label { Height = RowHeight * 2f };
            _empty.TextProvider = Strings.KeyString("ZtxRailPanel/TerminalNone");
            _cartsPanel.AddChild(_empty);
        }

        private static Label Unit(string key, float width)
        {
            var unit = new Label { DockMode = DockMode.Left, Width = width };
            unit.TextProvider = Strings.KeyString(key);
            return unit;
        }

        private TextCell NumberCell(LayoutBox row, string tipKey, string unitKey, Action changed)
        {
            var cell = new TextCell(_cells, W_NUM, tipKey);
            cell.Changed += changed;
            row.DockedChildren.Add(cell.Display);
            row.DockedChildren.Add(Unit(unitKey, 70f));
            return cell;
        }

        protected override void OnUpdatingUIState(object sender, EventArgs e)
        {
            try
            {
                if (PartGone()) { SelfActive = false; return; }
                ShowGap();
                ShowHold();
                ZtxRailTerminalComponent terminal = ZtxRailTerminalComponent.ForPart(_part);
                RailShipManager m = RailShipManager.TryGet(_part.Ship);
                List<Part> carts = terminal != null && m != null ? m.CartsOf(terminal) : new List<Part>();
                var stationItems = StationItems(terminal);
                ShowGlobal(stationItems);
                string sig = "";
                foreach (Part c in carts) sig += c.GetHashCode() + ";";
                if (sig != _cartSig) Rebuild(carts, sig);
                bool global = RailShipManager.GlobalOn(_part);
                int[] counts = new int[6];
                var names = new List<string>();
                foreach (CartRow row in _rows)
                {
                    int state = ShowRow(row, stationItems, global);
                    if (state >= 0 && state < counts.Length) counts[state]++;
                    names.Add(RailShipManager.CartName(row.Cart));
                }
                string summary = Strings.FormatText("ZtxRailPanel/SummaryFmt", carts.Count, counts[RailCartRules.STATE_MOVING], counts[RailCartRules.STATE_DWELLING],
                    counts[RailCartRules.STATE_HOLDING], counts[RailCartRules.STATE_WAITING], counts[RailCartRules.STATE_HOME], counts[RailCartRules.STATE_MERGED],
                    terminal?.Network.Stations.Count ?? 0);
                if (_summary.Text != summary) _summary.Text = summary;
                HashSet<string> dup = RailCartRules.Duplicates(names);
                string dupText = dup.Count == 0 ? "" : LogicPalette.Wrap(LogicPalette.Warn, Strings.FormatText("ZtxRailPanel/TerminalDupFmt", string.Join(", ", dup)));
                if (_dups.Text != dupText) _dups.Text = dupText;
                bool none = carts.Count == 0;
                if (_empty.SelfActive != none) _empty.SelfActive = none;
            }
            catch (Exception ex)
            {
                Log.Exception("RailTerminalWindow.OnUpdatingUIState", ex);
                SelfActive = false;
            }
        }

        private List<(int, string)> StationItems(ZtxRailTerminalComponent terminal)
        {
            _stationNames.Clear();
            _stationNames.Add("");
            var items = new List<(int, string)> { (0, Text("ZtxRailPanel/Home")) };
            if (terminal != null)
                foreach (Part s in terminal.Network.Stations)
                {
                    string name = RailNetwork.DisplayName(s);
                    _stationNames.Add(name);
                    items.Add((_stationNames.Count - 1, name));
                }
            return items;
        }

        private int TargetIndex(string stored, List<(int, string)> items)
        {
            if (stored.Length == 0) return 0;
            for (int i = 1; i < _stationNames.Count; i++)
                if (RailNetwork.Key(_stationNames[i]) == RailNetwork.Key(stored)) return i;
            items.Add((1000, stored + " (?)"));
            return 1000;
        }

        private void Rebuild(List<Part> carts, string sig)
        {
            _picker.Close();
            _cells.Close();
            foreach (CartRow r in _rows) _cartsPanel.Children.Remove(r.Box);
            _rows.Clear();
            foreach (Part cart in carts)
            {
                var row = new CartRow { Cart = cart, Box = Row() };
                FrameRow(row.Box);
                Part c = cart;
                row.Box.MouseEntered += delegate { LogicConsoleWindow.HoveredPart = c; };
                row.Box.MouseLeft += delegate { if (ReferenceEquals(LogicConsoleWindow.HoveredPart, c)) LogicConsoleWindow.HoveredPart = null; };
                row.Name = LogicGui.SmallButton("ZtxRailPanel/TerminalOpen", W_NAME, LogicPalette.ActMisc, "ZtxRailPanel/TipCartRowName");
                row.Name.Clicked += delegate { RailCartWindow.Open(_game, c); };
                row.Box.DockedChildren.Add(row.Name);
                row.State = new Label { DockMode = DockMode.Left, Width = W_STATE };
                row.State.TextRenderer.XmlFormatting = true;
                row.Box.DockedChildren.Add(row.State);
                row.Mode = new PickerCell(_picker, W_MODE, "ZtxRailPanel/TipCartCell") { Picked = v => WriteCartToggles(c, "mode -> " + ModeText(v), (RailShipManager.ModeToggleId, v)) };
                row.Mode.SetItems(new List<(int, string)>(s_modeItems));
                row.Box.DockedChildren.Add(row.Mode.Button);
                row.Target = new PickerCell(_picker, W_TARGET, "ZtxRailPanel/TipCartCell") { Picked = v => OnCartTargetPicked(c, v) };
                row.Box.DockedChildren.Add(row.Target.Button);
                row.Speed = new TextCell(_cells, W_NUM, "ZtxRailPanel/TipSpeed");
                row.Speed.Changed += () => OnCartValueTyped(c, row.Speed, 0);
                row.Box.DockedChildren.Add(row.Speed.Display);
                row.Power = new TextCell(_cells, W_NUM, "ZtxRailPanel/TipPower");
                row.Power.Changed += () => OnCartValueTyped(c, row.Power, 1);
                row.Box.DockedChildren.Add(row.Power.Display);
                row.Dwell = new TextCell(_cells, W_NUM, "ZtxRailPanel/TipDwell");
                row.Dwell.Changed += () => OnCartValueTyped(c, row.Dwell, 2);
                row.Box.DockedChildren.Add(row.Dwell.Display);
                row.Drive = LogicGui.SmallButton("ZtxRailPanel/DriveCart", W_DRIVE, LogicPalette.ActMisc, "ZtxRailPanel/TipCartCell");
                row.Drive.Clicked += delegate
                {
                    int next = RailShipManager.DriveMode(c) == RailShipManager.DRIVE_THRUSTERS ? RailShipManager.DRIVE_CART : RailShipManager.DRIVE_THRUSTERS;
                    WriteCartToggles(c, "drive -> " + (next == RailShipManager.DRIVE_THRUSTERS ? "thrusters" : "cart"), (RailShipManager.DriveToggleId, next));
                };
                row.Box.DockedChildren.Add(row.Drive);
                row.Merge = LogicGui.SmallButton("ZtxRailPanel/MergeOff", W_MERGE, LogicPalette.ActMisc, "ZtxRailPanel/TipCartCell");
                row.Merge.Clicked += delegate
                {
                    bool next = !RailShipManager.MergeOn(c);
                    WriteCartToggles(c, "merge -> " + (next ? "on" : "off"), (RailToggles.MergeId, next ? 1 : 0));
                };
                row.Box.DockedChildren.Add(row.Merge);
                row.Load = new Label { DockMode = DockMode.Left, Width = W_LOAD };
                row.Box.DockedChildren.Add(row.Load);
                _cartsPanel.AddChild(row.Box);
                _rows.Add(row);
            }
            _picker.Raise();
            _cells.Raise();
            _cartSig = sig;
        }

        private int ShowRow(CartRow row, List<(int, string)> stationItems, bool global)
        {
            Part cart = row.Cart;
            string name = RailCartRules.DisplayName(RailShipManager.CartName(cart), RailShipManager.CartId(cart));
            if (row.Name.Text != name) row.Name.Text = name;
            ZtxRailCartComponent rc = ZtxRailCartComponent.ForPart(cart);
            bool riding = rc?.PairedTerminal != null;
            bool merged = !riding && RailShipManager.IsMergedAway(cart);
            string reason = rc?.LastRejectReason;
            bool waiting = reason != null && (reason.EndsWith("waiting", StringComparison.Ordinal) || reason.EndsWith("holding", StringComparison.Ordinal));
            int state = RailCartRules.StateOf(riding, riding && rc.Phase == RailPhase.Moving, riding && rc.Phase == RailPhase.Dwelling,
                !riding && !merged, merged, waiting);
            string stateText = StateText(state);
            if (row.State.Text != stateText) row.State.Text = stateText;
            row.Mode.Show(RailShipManager.Mode(cart));
            var targets = new List<(int, string)>(stationItems);
            int target = TargetIndex(RailShipManager.TargetName(cart), targets);
            if (PickerCell.SignatureOf(targets) != row.Target.Signature) row.Target.SetItems(targets);
            row.Target.Show(target);
            var inv = CultureInfo.InvariantCulture;
            if (!_cells.IsEditing(row.Speed)) row.Speed.Text = RailShipManager.TypedSpeed(cart).ToString("0.#", inv);
            if (!_cells.IsEditing(row.Power)) row.Power.Text = RailShipManager.PowerPercent(cart).ToString(inv);
            if (!_cells.IsEditing(row.Dwell)) row.Dwell.Text = RailShipManager.DwellSeconds(cart).ToString("0.#", inv);
            string drive = Text(RailShipManager.DriveMode(cart) == RailShipManager.DRIVE_THRUSTERS ? "ZtxRailPanel/DriveThrusters" : "ZtxRailPanel/DriveCart");
            if (row.Drive.Text != drive) row.Drive.Text = drive;
            string merge = Text(RailShipManager.MergeOn(cart) ? "ZtxRailPanel/MergeOn" : "ZtxRailPanel/MergeOff");
            if (row.Merge.Text != merge) row.Merge.Text = merge;
            string load = riding ? Strings.FormatText("ZtxRailPanel/TerminalLoadFmt", rc.LoadMass.ToString("0.#", inv)) : "";
            if (row.Load.Text != load) row.Load.Text = load;
            if (row.Greyed != global)
            {
                row.Greyed = global;
                bool editable = !global;
                row.Mode.Button.SelfInputActive = row.Target.Button.SelfInputActive = editable;
                row.Speed.Display.SelfInputActive = row.Power.Display.SelfInputActive = row.Dwell.Display.SelfInputActive = editable;
                row.Drive.SelfInputActive = row.Merge.SelfInputActive = editable;
            }
            return state;
        }

        private static string StateText(int state)
        {
            switch (state)
            {
                case RailCartRules.STATE_WAITING: return LogicPalette.Wrap(LogicPalette.Error, Text("ZtxRailPanel/StateWaiting"));
                case RailCartRules.STATE_MOVING: return LogicPalette.Wrap(LogicPalette.On, Text("ZtxRailPanel/StateMoving"));
                case RailCartRules.STATE_DWELLING: return LogicPalette.Wrap(LogicPalette.KindSensor, Text("ZtxRailPanel/StateDwelling"));
                case RailCartRules.STATE_HOME: return LogicPalette.Wrap(LogicPalette.Neutral, Text("ZtxRailPanel/StateHome"));
                case RailCartRules.STATE_MERGED: return LogicPalette.Wrap(LogicPalette.KindLink, Text("ZtxRailPanel/StateMerged"));
                default: return LogicPalette.Wrap(LogicPalette.Warn, Text("ZtxRailPanel/StateHolding"));
            }
        }

        private void WriteCartToggles(Part cart, string what, params (ID<PartToggleGuiRules> Id, int Value)[] writes)
        {
            if (cart?.Ship == null) return;
            try
            {
                var input = new MPSetUITogglesInput();
                foreach (var (id, value) in writes)
                {
                    PartUIToggle t = PartUIToggle.GetOnPart(cart, id);
                    if (t != null) input.ToggleValues.Add((t, value));
                }
                if (input.IsEmpty) return;
                _game.NetManager.EnqueueInput(input);
                Log.Info("rail terminal at " + _part?.Location + ": cart at " + cart.Location + " " + what + ".");
            }
            catch (Exception ex) { Log.Exception("RailTerminalWindow.WriteCartToggles", ex); }
        }

        private void OnCartTargetPicked(Part cart, int index)
        {
            if (index < 0 || index >= _stationNames.Count) return;
            string name = RailNetwork.Key(_stationNames[index]);
            if (RailNetwork.Key(RailShipManager.TargetName(cart)) == name) return;
            WriteCartToggles(cart, "target -> " + (name.Length == 0 ? "HOME" : name),
                (RailShipManager.Target1Id, LogicRuleDef.PackName(name, 0)), (RailShipManager.Target2Id, LogicRuleDef.PackName(name, 5)));
        }

        private void OnCartValueTyped(Part cart, TextCell cell, int which)
        {
            if (cart?.Ship == null || !TryParse(cell.Text, out float v)) return;
            try
            {
                ID<PartToggleGuiRules> id = which == 0 ? RailToggles.SpeedValueId : which == 1 ? RailToggles.PowerValueId : RailToggles.DwellValueId;
                int stored = which == 0 ? RailCartRules.EncodeSpeed(v) : which == 1 ? RailCartRules.EncodePower((int)Math.Round(v)) : RailCartRules.EncodeDwell(v);
                _game.NetManager.EnqueueInput(GripStore.WriteInput(cart, (id, stored)));
                Log.Info("rail terminal at " + _part?.Location + ": cart at " + cart.Location + " " + (which == 0 ? "speed" : which == 1 ? "power" : "dwell") + " -> " + cell.Text + ".");
            }
            catch (Exception ex) { Log.Exception("RailTerminalWindow.OnCartValueTyped", ex); }
        }

        private static bool TryParse(string text, out float v)
        {
            string t = (text ?? "").Trim().Replace(',', '.');
            return float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && t.Length > 0;
        }

        private void ShowGap()
        {
            string g = RailShipManager.CartGap(_part).ToString("0.#", CultureInfo.InvariantCulture);
            if (g == _lastGap) return;
            _lastGap = g;
            _refreshing = true;
            try { LogicGui.SetFieldText(_gapField, g); }
            finally { _refreshing = false; }
        }

        private void OnGapTyped()
        {
            if (_refreshing || _part?.Ship == null) return;
            try
            {
                if (!TryParse(_gapField.Text, out float v)) return;
                int stored = RailFollow.EncodeGap(v);
                _lastGap = RailFollow.DecodeGap(stored).ToString("0.#", CultureInfo.InvariantCulture);
                _game.NetManager.EnqueueInput(GripStore.WriteInput(_part, (RailToggles.GapValueId, stored)));
                Log.Info("rail terminal at " + _part.Location + ": cart gap -> " + _lastGap + " cells.");
            }
            catch (Exception ex) { Log.Exception("RailTerminalWindow.OnGapTyped", ex); }
        }

        private void ShowHold()
        {
            bool on = RailShipManager.ShipHoldOn(_part);
            if (_lastHold == on) return;
            _lastHold = on;
            _holdButton.TextProvider = Strings.KeyString(on ? "ZtxRailPanel/ShipHoldOn" : "ZtxRailPanel/ShipHoldOff");
        }

        private void OnHoldClicked()
        {
            if (_part?.Ship == null) return;
            try
            {
                bool next = !RailShipManager.ShipHoldOn(_part);
                _game.NetManager.EnqueueInput(GripStore.WriteInput(_part, (RailToggles.ShipHoldValueId, RailShipHold.Encode(next))));
                Log.Info("rail terminal at " + _part.Location + ": ship hold -> " + (next ? "on" : "off") + ".");
            }
            catch (Exception ex) { Log.Exception("RailTerminalWindow.OnHoldClicked", ex); }
        }

        private void ShowGlobal(List<(int, string)> stationItems)
        {
            Part tp = _part;
            bool on = RailShipManager.GlobalOn(tp);
            if (_lastGlobal != on)
            {
                _lastGlobal = on;
                _globalButton.TextProvider = Strings.KeyString(on ? "ZtxRailPanel/GlobalOn" : "ZtxRailPanel/GlobalOff");
            }
            _gMode.Show(RailShipManager.TerminalMode(tp));
            var targets = new List<(int, string)>(stationItems);
            int target = TargetIndex(RailShipManager.TerminalTarget(tp), targets);
            if (PickerCell.SignatureOf(targets) != _gTarget.Signature) _gTarget.SetItems(targets);
            _gTarget.Show(target);
            var inv = CultureInfo.InvariantCulture;
            if (!_cells.IsEditing(_gSpeed)) _gSpeed.Text = RailShipManager.SpeedOf(tp).ToString("0.#", inv);
            if (!_cells.IsEditing(_gPower)) _gPower.Text = RailShipManager.PowerOf(tp).ToString(inv);
            if (!_cells.IsEditing(_gDwell)) _gDwell.Text = RailShipManager.DwellOf(tp).ToString("0.#", inv);
            int drive = RailShipManager.TerminalDrive(tp);
            if (drive != _lastDrive)
            {
                _lastDrive = drive;
                _driveButton.TextProvider = Strings.KeyString(drive == RailShipManager.DRIVE_THRUSTERS ? "ZtxRailPanel/DriveThrusters" : "ZtxRailPanel/DriveCart");
            }
            bool merge = RailShipManager.TerminalMerge(tp);
            if (_lastMerge != merge)
            {
                _lastMerge = merge;
                _mergeButton.TextProvider = Strings.KeyString(merge ? "ZtxRailPanel/MergeOn" : "ZtxRailPanel/MergeOff");
            }
        }

        private void WriteTerminal(string what, params (ID<PartToggleGuiRules> Id, int Value)[] writes)
        {
            if (_part?.Ship == null) return;
            _game.NetManager.EnqueueInput(GripStore.WriteInput(_part, writes));
            Log.Info("rail terminal at " + _part.Location + ": global " + what + ".");
        }

        private void OnGlobalClicked()
        {
            try
            {
                bool next = !RailShipManager.GlobalOn(_part);
                var writes = new List<(ID<PartToggleGuiRules>, int)> { (RailToggles.GlobalId, RailGlobal.Encode(next)) };
                string seeded = "";
                ZtxRailTerminalComponent terminal = ZtxRailTerminalComponent.ForPart(_part);
                RailShipManager m = RailShipManager.TryGet(_part.Ship);
                List<Part> carts = terminal != null && m != null ? m.CartsOf(terminal) : null;
                if (RailGlobal.SeedOnSwitch(RailShipManager.GlobalStored(_part), next) && carts != null && carts.Count > 0)
                {
                    Part c = carts[0];
                    string name = RailNetwork.Key(RailShipManager.TargetName(c));
                    writes.Add((RailShipManager.ModeToggleId, RailShipManager.Mode(c)));
                    writes.Add((RailShipManager.Target1Id, LogicRuleDef.PackName(name, 0)));
                    writes.Add((RailShipManager.Target2Id, LogicRuleDef.PackName(name, 5)));
                    writes.Add((RailToggles.SpeedValueId, RailCartRules.EncodeSpeed(RailShipManager.TypedSpeed(c))));
                    writes.Add((RailToggles.PowerValueId, RailCartRules.EncodePower(RailShipManager.PowerPercent(c))));
                    writes.Add((RailToggles.DwellValueId, RailCartRules.EncodeDwell(RailShipManager.DwellSeconds(c))));
                    writes.Add((RailShipManager.DriveToggleId, RailShipManager.DriveMode(c)));
                    writes.Add((RailToggles.MergeId, RailShipManager.MergeOn(c) ? 1 : 0));
                    seeded = ", settings copied from the cart at " + c.Location;
                }
                WriteTerminal("cart settings -> " + (next ? "on" : "off") + seeded, writes.ToArray());
            }
            catch (Exception ex) { Log.Exception("RailTerminalWindow.OnGlobalClicked", ex); }
        }

        private void OnGlobalTargetPicked(int index)
        {
            if (_part == null || index < 0 || index >= _stationNames.Count) return;
            string name = RailNetwork.Key(_stationNames[index]);
            if (RailNetwork.Key(RailShipManager.TerminalTarget(_part)) == name) return;
            WriteTerminal("target -> " + (name.Length == 0 ? "HOME" : name),
                (RailShipManager.Target1Id, LogicRuleDef.PackName(name, 0)), (RailShipManager.Target2Id, LogicRuleDef.PackName(name, 5)));
        }

        private void OnGlobalValueTyped(int which)
        {
            if (_part?.Ship == null) return;
            TextCell cell = which == 0 ? _gSpeed : which == 1 ? _gPower : _gDwell;
            if (!TryParse(cell.Text, out float v)) return;
            try
            {
                var inv = CultureInfo.InvariantCulture;
                if (which == 0) { int st = RailCartRules.EncodeSpeed(v); WriteTerminal("speed -> " + RailCartRules.DecodeSpeed(st, 0).ToString("0.#", inv), (RailToggles.SpeedValueId, st)); }
                else if (which == 1) { int st = RailCartRules.EncodePower((int)Math.Round(v)); WriteTerminal("power -> " + RailCartRules.DecodePower(st, 0).ToString(inv), (RailToggles.PowerValueId, st)); }
                else { int st = RailCartRules.EncodeDwell(v); WriteTerminal("dwell -> " + RailCartRules.DecodeDwell(st, 0).ToString("0.#", inv), (RailToggles.DwellValueId, st)); }
            }
            catch (Exception ex) { Log.Exception("RailTerminalWindow.OnGlobalValueTyped", ex); }
        }

        private void OnGlobalDriveClicked()
        {
            try
            {
                int next = RailShipManager.TerminalDrive(_part) == RailShipManager.DRIVE_THRUSTERS ? RailShipManager.DRIVE_CART : RailShipManager.DRIVE_THRUSTERS;
                WriteTerminal("drive -> " + (next == RailShipManager.DRIVE_THRUSTERS ? "thrusters" : "cart"), (RailShipManager.DriveToggleId, next));
            }
            catch (Exception ex) { Log.Exception("RailTerminalWindow.OnGlobalDriveClicked", ex); }
        }

        private void OnGlobalMergeClicked()
        {
            try
            {
                bool next = !RailShipManager.TerminalMerge(_part);
                WriteTerminal("merge -> " + (next ? "on" : "off"), (RailToggles.MergeId, next ? 1 : 0));
            }
            catch (Exception ex) { Log.Exception("RailTerminalWindow.OnGlobalMergeClicked", ex); }
        }

        internal static string ModeText(int mode)
        {
            switch (mode)
            {
                case RailShipManager.MODE_CYCLE: return Text("ZtxRailPanel/ModeCycle");
                case RailShipManager.MODE_SHUTTLE: return Text("ZtxRailPanel/ModeShuttle");
                case RailShipManager.MODE_FORWARD: return Text("ZtxRailPanel/ModeForward");
                case RailShipManager.MODE_BACKWARD: return Text("ZtxRailPanel/ModeBackward");
                case RailCartRules.MODE_HOLD: return Text("ZtxRailPanel/ModeHold");
                default: return Text("ZtxRailPanel/ModeGoTo");
            }
        }
    }
}
