using System;
using System.Runtime.CompilerServices;
using Halfling.Gui;
using Halfling.Gui.Components.Graphics;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogicUiScale
    {
        public static readonly float[] Steps = { 1f, 0.9f, 0.8f, 0.7f };
        private const int DefaultFontSize = 16;
        private const int MinFontSize = 8;

        public enum Area { Rules = 0, Summary = 1, Parts = 2 }

        public static float Rules => LogicLayoutState.UiScales[(int)Area.Rules];
        public static float Summary => LogicLayoutState.UiScales[(int)Area.Summary];
        public static float Parts => LogicLayoutState.UiScales[(int)Area.Parts];

        public static float Of(Area a) => LogicLayoutState.UiScales[(int)a];

        public static int Percent(Area a) => (int)Math.Round(Of(a) * 100f);

        public static void Next(Area a)
        {
            int i = Array.IndexOf(Steps, Of(a));
            LogicLayoutState.UiScales[(int)a] = Steps[(i + 1) % Steps.Length];
        }

        private sealed class FontBase
        {
            public int Base;
            public float Applied = 1f;
        }

        private static readonly ConditionalWeakTable<WidgetTextRenderer, FontBase> s_fonts = new ConditionalWeakTable<WidgetTextRenderer, FontBase>();

        public static void ApplyFonts(Widget root, float s)
        {
            if (root != null) Walk(root, s);
        }

        private static void Walk(Widget w, float s)
        {
            if (w is Button b)
            {
                Scale(b.StateNormalTextRenderer, s);
                Scale(b.StateHighlightedTextRenderer, s);
                Scale(b.StatePressedTextRenderer, s);
                Scale(b.StateDisabledTextRenderer, s);
            }
            if (w is ListItem li) Scale(li.SelectedTextRenderer, s);
            if (w is Label l) Scale(l.TextRenderer, s);
            if (w is DropList dl)
                foreach (Widget c in dl.ListBox.Children) Walk(c, s);
            if (w is LayoutBox<Widget, Widget> box)
            {
                foreach (Widget c in box.DockedChildren) Walk(c, s);
                foreach (Widget c in box.Children) Walk(c, s);
            }
        }

        private static void Scale(WidgetTextRenderer r, float s)
        {
            if (r == null) return;
            FontBase fb = s_fonts.GetValue(r, k => new FontBase { Base = k.FontSize > 0 ? k.FontSize : DefaultFontSize });
            if (Math.Abs(fb.Applied - s) < 0.001f) return;
            fb.Applied = s;
            int size = Math.Max(MinFontSize, (int)Math.Round(fb.Base * s));
            if (r.FontSize != size) r.FontSize = size;
        }
    }
}
