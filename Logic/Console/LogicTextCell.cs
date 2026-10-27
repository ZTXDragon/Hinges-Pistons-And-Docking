using System;
using Cosmoteer.Localization;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Graphics;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class TextCell
    {
        public readonly Button Display;
        private string _text = "";

        public event Action Changed;

        public static int Built;

        public TextCell(TextCellEditor editor, float width, string tipKey)
        {
            Display = new Button { DockMode = DockMode.Left, Width = width };
            Display.ToolTips.TextProvider = Strings.KeyString(tipKey);
            foreach (WidgetTextRenderer r in new[] { Display.StateNormalTextRenderer, Display.StateHighlightedTextRenderer, Display.StatePressedTextRenderer, Display.StateDisabledTextRenderer })
                r.OversizeMode = OversizeMode.Shrink;
            Display.Clicked += delegate { editor.Open(this); };
            Built++;
        }

        public string Text
        {
            get => _text;
            set
            {
                _text = value ?? "";
                if (Display.Text != _text) Display.Text = _text;
            }
        }

        internal void RaiseChanged() => Changed?.Invoke();
    }

    internal sealed class TextCellEditor
    {
        private LayoutBox<Widget, Widget> _host;
        private TextEditField _field;
        private TextCell _cell;
        private bool _filling;

        public TextCellEditor(LayoutBox<Widget, Widget> host)
        {
            _host = host;
        }

        public bool IsEditing(TextCell cell) => cell != null && ReferenceEquals(_cell, cell);

        public void Raise()
        {
            if (_host == null || _field == null) return;
            _host.Children.Remove(_field);
            _host.AddChild(_field);
        }

        public void Open(TextCell cell)
        {
            if (cell == null) return;
            try
            {
                if (_field == null) Create();
                WindowBox w = LogicGui.WindowOf(cell.Display);
                if (w != null && !ReferenceEquals(w, _host))
                {
                    _host.Children.Remove(_field);
                    w.AddChild(_field);
                    _host = w;
                }
                Close();
                _cell = cell;
                _filling = true;
                try { _field.Text = cell.Text; }
                finally { _filling = false; }
                _field.SelfActive = true;
                _field.ScreenRect = cell.Display.ScreenRect;
                TextCell opening = cell;
                TextEditField field = _field;
                field.Root?.AddOneTimePreInputCallback(root =>
                {
                    if (!ReferenceEquals(_cell, opening) || !field.SelfActive) return;
                    field.ScreenRect = opening.Display.ScreenRect;
                    field.FocusController.RequestFocus();
                    field.TextSelection.SelectAll();
                });
            }
            catch (Exception ex)
            {
                Log.Exception("TextCellEditor.Open", ex);
                Hide();
            }
        }

        public void Close()
        {
            if (_field == null) return;
            try { _field.FocusController.RelinquishFocus(); }
            catch (Exception ex) { Log.Exception("TextCellEditor.Close", ex); }
            Hide();
        }

        private void Create()
        {
            long t0 = LogicCost.Now;
            _field = LogicGui.Typing(new TextEditField(TextEditField.Style.SingleLine) { IgnoreInLayout = true });
            _field.TextChanged += (s, e) => OnTextChanged();
            object focus = _field.FocusController;
            focus.GetType().GetEvent("Defocused").AddEventHandler(focus, new EventHandler<EventArgs>((s, e) => Hide()));
            _field.SelfActive = false;
            _host.AddChild(_field);
            Log.Info("console: shared edit field created in " + LogicCost.Ms(LogicCost.Now - t0).ToString("0.0") + " ms.");
        }

        private void OnTextChanged()
        {
            if (_filling || _cell == null) return;
            try
            {
                _cell.Text = _field.Text;
                _cell.RaiseChanged();
            }
            catch (Exception ex) { Log.Exception("TextCellEditor.OnTextChanged", ex); }
        }

        private void Hide()
        {
            _cell = null;
            if (_field != null && _field.SelfActive) _field.SelfActive = false;
        }
    }
}
