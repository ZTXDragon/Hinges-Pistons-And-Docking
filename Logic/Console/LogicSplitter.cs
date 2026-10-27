using System;
using Halfling.Geometry;
using Halfling.Gui;
using Halfling.Gui.Components.Input;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class LogicSplitter : BaseWidgetDragController
    {
        private const float CLICK_SLOP = 3f;
        private float _moved;

        public Action Started;
        public Action<Vector2> Moved;
        public Action Clicked;

        public LogicSplitter()
        {
            GetType().GetEvent("DragEnded").AddEventHandler(this, new EventHandler<EventArgs>((s, e) => { if (_moved < CLICK_SLOP) Clicked?.Invoke(); }));
        }

        public static LayoutBox Bar(LogicSplitter splitter, DockMode dock, float thickness)
        {
            var bar = new LayoutBox { DockMode = dock };
            bool vertical = dock == DockMode.Left || dock == DockMode.Right;
            if (vertical) bar.Width = thickness;
            else bar.Height = thickness;
            bar.NineSlice.Flags = NineSliceFlags.None;
            splitter.DragCursor = vertical ? WindowBox.Default.ResizeController.ResizeLeftCursor : WindowBox.Default.ResizeController.ResizeTopCursor;
            bar.Components.Add(splitter);
            return bar;
        }

        public override void OnDragStarted(Vector2 cursorStartLoc)
        {
            _moved = 0f;
            Started?.Invoke();
        }

        public override bool OnDragContinued(Vector2 cursorStartLoc, Vector2 totalCursorDelta)
        {
            _moved = Math.Max(_moved, Math.Abs(totalCursorDelta.X) + Math.Abs(totalCursorDelta.Y));
            Moved?.Invoke(totalCursorDelta);
            return true;
        }
    }
}
