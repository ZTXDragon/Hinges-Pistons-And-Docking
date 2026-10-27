using System;
using System.Collections.Generic;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Graphics;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class LogicPickerPopup
    {
        private readonly DropList _list;
        private readonly List<ListItem> _pool = new List<ListItem>();
        public const int NEW_ENTRY = -20;

        public static bool Pickable(int value) => value >= 0 || value == NEW_ENTRY;

        private PickerCell _cell;
        private PickerCell _target;
        private bool _filling;
        private bool _logged;

        private LayoutBox<Widget, Widget> _host;

        public LogicPickerPopup(LayoutBox<Widget, Widget> host)
        {
            _list = new DropList { IgnoreInLayout = true };
            foreach (WidgetTextRenderer r in new[] { _list.StateNormalTextRenderer, _list.StateHighlightedTextRenderer, _list.StatePressedTextRenderer, _list.StateDisabledTextRenderer })
            {
                r.XmlFormatting = true;
                r.OversizeMode = OversizeMode.Shrink;
            }
            _list.ListBox.SelectionManager.WidgetSelected += OnSelected;
            object focus = _list.FocusController;
            focus.GetType().GetEvent("Defocused").AddEventHandler(focus, new EventHandler<EventArgs>((s, e) => Hide()));
            _list.SelfActive = false;
            _host = host;
            host.AddChild(_list);
        }

        private void FollowCell(Widget cellWidget)
        {
            WindowBox w = LogicGui.WindowOf(cellWidget);
            if (w == null || ReferenceEquals(w, _host)) return;
            _host.Children.Remove(_list);
            w.AddChild(_list);
            _host = w;
        }

        public void Raise()
        {
            if (_host == null || _list == null) return;
            _host.Children.Remove(_list);
            _host.AddChild(_list);
        }

        public bool IsOpen => _cell != null;

        public Widget ListWidget => _list;

        public bool IsOpenFor(PickerCell cell) => cell != null && ReferenceEquals(_cell, cell);

        public void Open(PickerCell cell)
        {
            if (cell == null) return;
            try
            {
                long t0 = LogicCost.Now;
                Close();
                FollowCell(cell.Button);
                _cell = cell;
                _target = cell;
                int n = cell.Items.Count, grown = 0;
                ListItem selected = null;
                _filling = true;
                try
                {
                    while (_pool.Count < n)
                    {
                        _pool.Add(MakeEntry());
                        grown++;
                    }
                    for (int i = 0; i < _pool.Count; i++)
                    {
                        ListItem item = _pool[i];
                        bool used = i < n;
                        if (item.SelfActive != used) item.SelfActive = used;
                        if (!used) continue;
                        var (value, text) = cell.Items[i];
                        if (item.Text != text) item.Text = text;
                        item.UserData = value;
                        item.SelfInputActive = Pickable(value);
                        string tipKey = cell.TipKey != null && value >= 0 ? cell.TipKey(value) : null;
                        item.ToolTips.TextProvider = tipKey != null ? Strings.KeyString(tipKey) : null;
                        if (value == cell.Value) selected = item;
                    }
                    _list.SelectedWidget = selected;
                }
                finally { _filling = false; }
                _list.ScreenRect = cell.Button.ScreenRect;
                _list.SelfActive = true;
                _list.FocusController.RequestFocus();
                if (!_logged)
                {
                    _logged = true;
                    Log.Info("picker list opened: " + n + " entries (" + grown + " new, " + _pool.Count + " pooled) in " + LogicCost.Ms(LogicCost.Now - t0).ToString("0.0") + " ms.");
                }
            }
            catch (Exception ex)
            {
                Log.Exception("LogicPickerPopup.Open", ex);
                Hide();
            }
        }

        public void Close()
        {
            _target = null;
            if (_cell == null && !_list.SelfActive) return;
            try { _list.FocusController.RelinquishFocus(); }
            catch (Exception ex) { Log.Exception("LogicPickerPopup.Close", ex); }
            Hide();
        }

        private void Hide()
        {
            _cell = null;
            if (_list.SelfActive) _list.SelfActive = false;
        }

        private ListItem MakeEntry()
        {
            var item = new ListItem();
            foreach (WidgetTextRenderer r in new[] { item.StateNormalTextRenderer, item.StateHighlightedTextRenderer, item.StatePressedTextRenderer, item.StateDisabledTextRenderer, item.SelectedTextRenderer })
            {
                r.XmlFormatting = true;
                r.OversizeMode = OversizeMode.Shrink;
            }
            ListItem captured = item;
            item.MouseEntered += delegate
            {
                Part p = PartOfEntry(captured);
                if (p != null) LogicConsoleWindow.HoveredPart = p;
            };
            item.MouseLeft += delegate
            {
                Part p = PartOfEntry(captured);
                if (p != null && ReferenceEquals(LogicConsoleWindow.HoveredPart, p)) LogicConsoleWindow.HoveredPart = null;
            };
            _list.AddChild(item);
            return item;
        }

        private Part PartOfEntry(ListItem item)
        {
            PickerCell c = _cell;
            return c?.PartOf != null && item.UserData is int v && v >= 0 ? c.PartOf(v) : null;
        }

        private void OnSelected(object sender, WidgetEventArgs e)
        {
            if (_filling || _target == null) return;
            try
            {
                if (!(e.Widget.UserData is int value) || !Pickable(value)) return;
                PickerCell c = _target;
                c.Show(value);
                c.Picked?.Invoke(value);
            }
            catch (Exception ex) { Log.Exception("LogicPickerPopup.OnSelected", ex); }
        }
    }
}
