using System;
using System.Collections.Generic;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Halfling.Geometry;
using Halfling.Gui;
using Halfling.Gui.Components.Hierarchy;

namespace ZTX.HingesPistonsDocking
{
    internal enum LogicPanelId { Rules, Summary, Parts }

    internal sealed class LogicFloatingPanel : WindowBox
    {
        private static readonly List<LogicFloatingPanel> s_open = new List<LogicFloatingPanel>();

        public readonly LogicPanelId Id;
        private readonly GameRoot _game;
        private Widget _content;
        private Rect _savedAnchor;
        private Action<LogicPanelId, Widget> _dockBack;
        private bool _reopenMain;

        public static bool AnyOpen
        {
            get
            {
                foreach (LogicFloatingPanel p in s_open) if (p.IsActive) return true;
                return false;
            }
        }

        public static void ResetAll() => s_open.Clear();

        public static void Raise(GameRoot game, LogicPanelId id)
        {
            foreach (LogicFloatingPanel p in s_open)
                if (p.Id == id && p.IsActive) game.Gui.FloatingWindows.Children.RaiseToFront(p);
        }

        public static void Open(GameRoot game, LogicPanelId id, string titleKey, Widget content, Vector2 size, Action<LogicPanelId, Widget> dockBack)
        {
            var w = new LogicFloatingPanel(game, id, titleKey, content, size, dockBack);
            game.Gui.FloatingWindows.AddChild(w);
            game.Gui.FloatingWindows.Children.RaiseToFront(w);
            s_open.Add(w);
            Log.Info("console panel " + id + " popped out.");
        }

        private LogicFloatingPanel(GameRoot game, LogicPanelId id, string titleKey, Widget content, Vector2 size, Action<LogicPanelId, Widget> dockBack)
        {
            _game = game;
            Id = id;
            _dockBack = dockBack;
            TitleTextProvider = Strings.KeyString(titleKey);
            Size = size;
            ResizeController.MinSize = new Vector2(320f, 200f);
            Button dock = LogicIcons.Make(LogicIcons.Dock, "ZtxLogicPanel/Dock", 34f, "ZtxLogicPanel/TipDock");
            dock.DockMode = DockMode.Right;
            dock.Clicked += delegate { _reopenMain = true; SelfActive = false; };
            TitleBox.DockedChildren.Add(dock);
            _content = content;
            _savedAnchor = content.AnchorRect;
            content.AnchorRect = AnchorPresets.Fill;
            AddChild(content);
            Parented += (s, e) =>
            {
                ScreenRect = Rect.CenteredOn(Parent.RectProvider.ScreenRect.Size / 2f, Size);
                BoundsProvider = Parent.RectProvider;
            };
            Unparenting += (s, e) => BoundsProvider = null;
            SelfDeactivated += delegate { GiveBack(); };
            this.BeforeInputWhileActive(OnUpdate);
            WidgetRules.AddDialogTweens(this);
        }

        private void OnUpdate(object sender, EventArgs e)
        {
            try
            {
                LogicConsoleSession s = LogicConsoleWindow.Session;
                if (s == null || s.ConsoleGone()) { SelfActive = false; return; }
                s.Tick();
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("LogicFloatingPanel.OnUpdate", ex);
                SelfActive = false;
            }
        }

        private void GiveBack()
        {
            if (_content == null) return;
            try
            {
                Widget c = _content;
                _content = null;
                Children.Remove(c);
                c.AnchorRect = _savedAnchor;
                _dockBack?.Invoke(Id, c);
                s_open.Remove(this);
                bool reopen = _reopenMain;
                _game.Gui.FloatingWindows.Children.Remove(this);
                Log.Info("console panel " + Id + " docked back" + (reopen ? " (console window reopened)" : "") + ".");
                if (reopen) LogicConsoleWindow.Reopen(_game);
            }
            catch (Exception ex) { Log.Exception("LogicFloatingPanel.GiveBack", ex); }
        }
    }
}
