using System;
using System.Collections.Generic;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Graphics.Text;
using Halfling.Gui;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class ConsoleHelpWindow : WindowBox
    {
        private static readonly string[] s_sections = { "Rules", "Groups", "Drag", "Lines", "Compare", "Do", "Sequences", "Blocks", "Shared", "Network", "Panels" };
        private const int HeadingFont = 16, BodyFont = 14;
        private const float HeadingHeight = 24f, LineHeight = 20f, CharWidth = 7f, Gap = 6f;
        private static readonly Vector2 s_size = new Vector2(620f, 560f);

        private static ConsoleHelpWindow s_instance;

        private readonly GameRoot _game;
        private readonly List<(Label Label, string Text)> _bodies = new List<(Label, string)>();
        private float _fittedWidth = -1f;

        public static void Open(GameRoot game)
        {
            if (game == null) return;
            if (s_instance == null || !ReferenceEquals(s_instance._game, game))
            {
                s_instance = new ConsoleHelpWindow(game);
                game.Gui.FloatingWindows.AddChild(s_instance);
            }
            s_instance.SelfActive = true;
            game.Gui.FloatingWindows.Children.RaiseToFront(s_instance);
        }

        public static void Reset()
        {
            s_instance = null;
        }

        private ConsoleHelpWindow(GameRoot game)
        {
            _game = game;
            TitleTextProvider = Strings.KeyString("ZtxConsoleHelp/Title");
            Size = s_size;
            ResizeController.MinSize = new Vector2(420f, 300f);
            Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
            Children.BorderPadding = new Borders(10f, 10f, 10f + VerticalScrollBar.Width, 10f);
            Children.WidgetPadding = new Vector2(Gap);
            ScrollBarController.ShowVertical = ScrollBarShowMode.WhenNeeded;
            foreach (string id in s_sections)
            {
                Label heading = LogicGui.WrapLabel(HeadingFont, LogicPalette.LitValue);
                heading.TextRenderer.OversizeMode = OversizeMode.Shrink;
                LogicGui.SetText(heading, "<b>" + Strings.GetText("ZtxConsoleHelp/" + id + "Head") + "</b>");
                heading.Height = HeadingHeight;
                AddChild(heading);
                Label body = LogicGui.WrapLabel(BodyFont, LogicPalette.LitValue);
                AddChild(body);
                _bodies.Add((body, Strings.GetText("ZtxConsoleHelp/" + id)));
            }
            Parented += (s, e) =>
            {
                ScreenRect = Rect.CenteredOn(Parent.RectProvider.ScreenRect.Size / 2f, Size);
                BoundsProvider = Parent.RectProvider;
            };
            Unparenting += (s, e) => BoundsProvider = null;
            this.BeforeInputWhileActive(OnUpdatingUIState);
            WidgetRules.AddDialogTweens(this);
        }

        private void OnUpdatingUIState(object sender, EventArgs e)
        {
            try
            {
                float width = ScreenRect.Size.X - Children.BorderPadding.Left - Children.BorderPadding.Right;
                if (width <= 0f || Math.Abs(width - _fittedWidth) < 1f) return;
                _fittedWidth = width;
                foreach (var (label, text) in _bodies) LogicGui.FitWrapped(label, text, width, LineHeight, CharWidth);
            }
            catch (Exception ex)
            {
                Log.Exception("ConsoleHelpWindow.OnUpdatingUIState", ex);
                SelfActive = false;
            }
        }
    }
}
