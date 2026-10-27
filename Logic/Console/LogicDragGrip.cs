using System;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Gui;
using Halfling.Gui.Components.Graphics;
using Halfling.Gui.Components.Input;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class LogicDragGrip : BaseWidgetDragController
    {
        private const float CLICK_SLOP = 4f;
        private const float BAR_SHARE = 0.55f, BAR_HEIGHT = 2f, BAR_GAP = 4f;
        private float _moved;

        public Action Started;
        public Action<Vector2> Moved;
        public Action<bool> Ended;

        public LogicDragGrip()
        {
            GetType().GetEvent("DragEnded").AddEventHandler(this, new EventHandler<EventArgs>((s, e) => Ended?.Invoke(_moved >= CLICK_SLOP)));
        }

        public static LayoutBox Make(LogicDragGrip grip, float width, float height, string hex)
        {
            var box = new LayoutBox { DockMode = DockMode.Left, Width = width, Height = height };
            box.NineSlice.Flags = NineSliceFlags.None;
            grip.DragCursor = WindowBox.Default.ResizeController.ResizeTopCursor;
            Color colour = LogicPalette.ToColor(hex);
            box.Components.Add(new WidgetDelegatedRenderer((gfx, pixelRect, r, scale) =>
            {
                gfx.SetToMaterial(null);
                float w = r.Width * BAR_SHARE, x = r.X + (r.Width - w) / 2f, mid = r.Y + r.Height / 2f;
                for (int i = -1; i <= 1; i++)
                    gfx.Draw<Quad>(new Quad(new Rect(x, mid + i * BAR_GAP - BAR_HEIGHT / 2f, w, BAR_HEIGHT), colour * scale));
            }, box.NineSlice));
            box.Components.Add(grip);
            return box;
        }

        public override void OnDragStarted(Vector2 cursorStartLoc)
        {
            _moved = 0f;
            Started?.Invoke();
        }

        public override bool OnDragContinued(Vector2 cursorStartLoc, Vector2 totalCursorDelta)
        {
            _moved = Math.Max(_moved, Math.Abs(totalCursorDelta.X) + Math.Abs(totalCursorDelta.Y));
            Moved?.Invoke(cursorStartLoc + totalCursorDelta);
            return true;
        }
    }
}
