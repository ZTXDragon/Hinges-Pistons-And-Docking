using Cosmoteer.Game.Gui;
using Cosmoteer.Localization;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Graphics;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogicGui
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, string> s_keys =
            new System.Runtime.CompilerServices.ConditionalWeakTable<object, string>();

        public static void SetKey(Button b, string key)
        {
            if (b == null || (s_keys.TryGetValue(b, out string last) && last == key)) return;
            s_keys.AddOrUpdate(b, key);
            b.TextProvider = Strings.KeyString(key);
        }

        public static void SetKey(Label l, string key)
        {
            if (l == null || (s_keys.TryGetValue(l, out string last) && last == key)) return;
            s_keys.AddOrUpdate(l, key);
            l.TextProvider = Strings.KeyString(key);
        }

        public static Button FlatButton(float height, int fontSize = 0, bool shrink = true)
        {
            var b = new Button { Height = height };
            b.StateNormalNineSlice.Flags = NineSliceFlags.None;
            foreach (WidgetTextRenderer r in new[] { b.StateNormalTextRenderer, b.StateHighlightedTextRenderer, b.StateDisabledTextRenderer, b.StatePressedTextRenderer })
            {
                r.XmlFormatting = true;
                r.HAlignment = HAlignment.Left;
                r.OversizeMode = shrink ? OversizeMode.Shrink : OversizeMode.None;
                if (fontSize > 0) r.FontSize = fontSize;
            }
            return b;
        }

        public static Label WrapLabel(int fontSize, string hex)
        {
            var l = new Label();
            l.TextRenderer.XmlFormatting = true;
            l.TextRenderer.HAlignment = HAlignment.Left;
            l.TextRenderer.OversizeMode = OversizeMode.Wrap;
            l.TextRenderer.FontSize = fontSize;
            if (hex != null) l.TextRenderer.Color = LogicPalette.ToColor(hex);
            return l;
        }

        public static float FitWrapped(Label l, string text, float width, float lineHeight, float charWidth)
        {
            SetText(l, text);
            int chars = LogicRuleText.StripTags(text).Length;
            int lines = chars == 0 ? 0 : System.Math.Max(1, (int)System.Math.Ceiling(chars * charWidth / System.Math.Max(60f, width)));
            float h = lines * lineHeight;
            SetHeight(l, h);
            SetActive(l, lines > 0);
            return h;
        }

        public static LayoutBox Line(float height)
        {
            var line = new LayoutBox { Height = height };
            Fill(line, LogicPalette.Divider);
            return line;
        }

        public static TextEditField Typing(TextEditField field)
        {
            if (field == null) return null;
            field.BlockOtherInputWhileFocused = true;
            field.ConfigureEnterDefocus();
            return field;
        }

        public static void SetFieldText(TextEditField field, string text)
        {
            if (field == null || field.Text == text) return;
            field.Text = text;
            if (field.FocusController.IsFocused) return;
            field.Root?.AddOneTimePreDrawCallback(root =>
            {
                ScrollBar bar = field.HorizontalScrollBar;
                if (bar != null && !field.FocusController.IsFocused) bar.Value = bar.MinValue;
            });
        }

        public static Button SmallButton(string key, float width, string hex, string tipKey)
        {
            var b = new Button { DockMode = DockMode.Left, Width = width };
            b.TextProvider = Strings.KeyString(key);
            SetTextColour(b, hex);
            if (tipKey != null) b.ToolTips.TextProvider = Strings.KeyString(tipKey);
            return b;
        }

        public static void SetTextColour(Button b, string hex)
        {
            Color c = LogicPalette.ToColor(hex);
            if (b.StateNormalTextRenderer.Color.Equals(c)) return;
            b.StateNormalTextRenderer.Color = c;
            b.StateHighlightedTextRenderer.Color = c;
            b.StatePressedTextRenderer.Color = c;
        }

        public static void Fill(LayoutBox box, string hex)
        {
            box.NineSlice.CenterSprite.CopyFrom(new Sprite((Texture)null, LogicPalette.ToColor(hex), Vector2.Zero, Rect.Zero));
            box.NineSlice.Flags = NineSliceFlags.Center;
        }

        public static void SetText(Button b, string text) { if (b.Text != text) b.Text = text; }

        public static void SetText(Label l, string text) { if (l.Text != text) l.Text = text; }

        public static void SetWidth(Widget w, float v) { if (System.Math.Abs(w.Width - v) > 0.5f) w.Width = v; }

        public static void SetHeight(Widget w, float v) { if (System.Math.Abs(w.Height - v) > 0.5f) w.Height = v; }

        public static void SetActive(Widget w, bool on) { if (w.SelfActive != on) w.SelfActive = on; }

        public static WindowBox WindowOf(Widget w)
        {
            Widget cur = w;
            while (cur != null)
            {
                if (cur is WindowBox wb) return wb;
                cur = cur.Parent?.Widget;
            }
            return null;
        }
    }
}
