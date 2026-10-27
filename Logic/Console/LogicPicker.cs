using System;
using System.Collections.Generic;
using System.Text;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Graphics;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class PickerCell
    {
        public readonly Button Button;
        public List<(int Value, string Text)> Items { get; private set; } = new List<(int, string)>();
        public string Signature { get; private set; } = "";
        public int Value { get; private set; } = int.MinValue;
        public Func<int, string> TipKey;
        public Func<int, Part> PartOf;
        public Action<int> Picked;
        public string BaseTipKey;
        private string _shownTip;

        public static int Built;

        public PickerCell(LogicPickerPopup popup, float width, string tipKey)
        {
            Button = new Button { DockMode = DockMode.Left, Width = width };
            Button.ToolTips.TextProvider = Strings.KeyString(tipKey);
            BaseTipKey = tipKey;
            _shownTip = tipKey;
            foreach (WidgetTextRenderer r in new[] { Button.StateNormalTextRenderer, Button.StateHighlightedTextRenderer, Button.StatePressedTextRenderer, Button.StateDisabledTextRenderer })
            {
                r.XmlFormatting = true;
                r.OversizeMode = OversizeMode.Shrink;
            }
            Button.Clicked += delegate { popup.Open(this); };
            Button.MouseEntered += delegate
            {
                Part p = PartOf != null && Value != int.MinValue ? PartOf(Value) : null;
                if (p != null) LogicConsoleWindow.HoveredPart = p;
            };
            Button.MouseLeft += delegate
            {
                Part p = PartOf != null && Value != int.MinValue ? PartOf(Value) : null;
                if (p != null && ReferenceEquals(LogicConsoleWindow.HoveredPart, p)) LogicConsoleWindow.HoveredPart = null;
            };
            Built++;
        }

        public void SetItems(List<(int Value, string Text)> items)
        {
            Items = items ?? new List<(int, string)>();
            Signature = SignatureOf(Items);
            if (Value != int.MinValue) Show(Value);
        }

        public void Show(int value)
        {
            Value = value;
            string text = "?";
            foreach (var (v, t) in Items)
            {
                if (v != value) continue;
                text = t;
                break;
            }
            if (Button.Text != text) Button.Text = text;
            string tip = (TipKey != null ? TipKey(value) : null) ?? BaseTipKey;
            if (tip != null && tip != _shownTip)
            {
                _shownTip = tip;
                Button.ToolTips.TextProvider = Strings.KeyString(tip);
            }
        }

        public static string SignatureOf(List<(int Value, string Text)> items)
        {
            var sb = new StringBuilder();
            foreach (var (v, t) in items) sb.Append(v).Append('=').Append(t).Append('|');
            return sb.ToString();
        }
    }
}
