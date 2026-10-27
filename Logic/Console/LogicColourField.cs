using System;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Gui;
using Halfling.Gui.Components.Graphics;
using Halfling.Gui.Components.Input;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class LogicColourField : BaseWidgetDragController
    {
        private const int COLUMNS = 48, ROWS = 12;
        private const float MARK = 6f;
        private LayoutBox _box;
        private Vector2 _last;
        private float _u = float.NaN, _v = float.NaN;

        public Action<int, bool> Picked;

        public LogicColourField()
        {
            GetType().GetEvent("DragEnded").AddEventHandler(this, new EventHandler<EventArgs>((s, e) => Pick(_last, true)));
        }

        public static LayoutBox Make(LogicColourField field, float width, float height)
        {
            var box = new LayoutBox { DockMode = DockMode.Left, Width = width, Height = height };
            box.NineSlice.Flags = NineSliceFlags.None;
            field._box = box;
            box.Components.Add(new WidgetDelegatedRenderer(field.Draw, box.NineSlice));
            box.Components.Add(field);
            return box;
        }

        private void Draw(GraphicsManager gfx, IntRect pixelRect, Rect r, Color scale)
        {
            gfx.SetToMaterial(null);
            Color[] cells = Cells();
            float w = r.Width / COLUMNS, h = r.Height / ROWS;
            for (int x = 0; x < COLUMNS; x++)
                for (int y = 0; y < ROWS; y++)
                    gfx.Draw<Quad>(new Quad(new Rect(r.X + x * w, r.Y + y * h, w + 0.5f, h + 0.5f), cells[x * ROWS + y] * scale));
            if (float.IsNaN(_u)) return;
            float mx = r.X + _u * r.Width - MARK / 2f, my = r.Y + _v * r.Height - MARK / 2f;
            Color ring = LogicPalette.ToColor(_v < 0.5f ? "000000" : "FFFFFF");
            gfx.Draw<Quad>(new Quad(new Rect(mx - 1f, my - 1f, MARK + 2f, MARK + 2f), ring * scale));
            gfx.Draw<Quad>(new Quad(new Rect(mx, my, MARK, MARK), LogicPalette.ToColor(LogicPalette.PickerColour(_u, _v).ToString("X6")) * scale));
        }

        private static Color[] s_cells;

        private static Color[] Cells()
        {
            if (s_cells != null) return s_cells;
            var cells = new Color[COLUMNS * ROWS];
            for (int x = 0; x < COLUMNS; x++)
                for (int y = 0; y < ROWS; y++)
                    cells[x * ROWS + y] = Color.FromHex(LogicPalette.PickerColour((x + 0.5f) / COLUMNS, (y + 0.5f) / ROWS).ToString("X6"));
            return s_cells = cells;
        }

        private void Pick(Vector2 cursor, bool final)
        {
            if (_box == null) return;
            Rect r = _box.ScreenRect;
            if (r.Width <= 0f || r.Height <= 0f) return;
            _u = Math.Max(0f, Math.Min(1f, (cursor.X - r.X) / r.Width));
            _v = Math.Max(0f, Math.Min(1f, (cursor.Y - r.Y) / r.Height));
            Picked?.Invoke(LogicPalette.PickerColour(_u, _v), final);
        }

        public override void OnDragStarted(Vector2 cursorStartLoc)
        {
            _last = cursorStartLoc;
            Pick(cursorStartLoc, false);
        }

        public override bool OnDragContinued(Vector2 cursorStartLoc, Vector2 totalCursorDelta)
        {
            _last = cursorStartLoc + totalCursorDelta;
            Pick(_last, false);
            return true;
        }
    }
}
