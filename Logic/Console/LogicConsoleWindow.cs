using System;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Gui;
using Halfling.Gui.Components.Hierarchy;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class LogicConsoleWindow : WindowBox
    {
        private const float Divider = 8f;
        private const float SideMin = 380f;
        private const float PaneMin = 420f;
        private static float PaneIdeal => 1230f * LogicUiScale.Rules;
        private const float SummaryMin = 60f;
        private const float WidthShare = 0.85f, HeightShare = 0.8f;

        private static LogicConsoleWindow s_instance;
        private static LogicConsoleSession s_session;

        public static Part HoveredPart;

        private readonly GameRoot _game;
        private readonly LogicConsoleSession _session;
        private readonly LayoutBox _body;
        private readonly LayoutBox _vsplit;
        private readonly LayoutBox _side;
        private readonly LayoutBox _hsplit;
        private readonly LogicPanelHost _summaryHost;
        private readonly LogicPanelHost _partsHost;
        private Button _toggleSummary, _toggleParts;
        private float _paneW, _sumH, _dragStart;
        private bool _sized;

        public static LogicConsoleSession Session => s_session;

        public static void Open(GameRoot game, Part console)
        {
            if (game == null || console == null) return;
            if (s_instance == null || !ReferenceEquals(s_instance._game, game))
            {
                s_instance = new LogicConsoleWindow(game);
                game.Gui.FloatingWindows.AddChild(s_instance);
                Log.Info("console window created.");
            }
            s_session.Bind(console);
            s_instance.TitleText = Strings.FormatText("ZtxLogicPanel/TitleFmt", console.Ship?.Metadata.PlainShipName ?? "?");
            s_instance.SelfActive = true;
            game.Gui.FloatingWindows.Children.RaiseToFront(s_instance);
            LogicConsoleHighlight.Attach(game.Sim);
            SensorAreaOverlay.Attach(game.Sim);
            Log.Info("console open at " + console.Location + ".");
            ConsoleWindowCost.Opened();
        }

        public static void Reset()
        {
            s_instance = null;
            s_session = null;
            HoveredPart = null;
            LogicFloatingPanel.ResetAll();
            LogicLayoutState.RulesPopped = LogicLayoutState.SummaryPopped = LogicLayoutState.PartsPopped = false;
        }

        public static bool IsShowing => (s_instance != null && s_instance.IsActive) || LogicFloatingPanel.AnyOpen;

        private LogicConsoleWindow(GameRoot game)
        {
            _game = game;
            Size = new Vector2(1600f, 800f);
            ResizeController.MinSize = new Vector2(760f, 360f);
            _body = new LayoutBox { AnchorRect = AnchorPresets.Fill };
            _body.NineSlice.Flags = NineSliceFlags.None;
            _body.DockedChildren.ClipChildren = false;
            AddChild(_body);
            s_session = _session = new LogicConsoleSession(game, this);
            _session.Rules.ExtraToolbar = AddToolbarButtons;

            _session.RulesPage.DockMode = DockMode.Left;
            _body.DockedChildren.Add(_session.RulesPage);
            var vs = new LogicSplitter();
            vs.Started = () => _dragStart = _paneW;
            vs.Moved = d => DragPane(d.X);
            vs.Clicked = () => { if (LogicLayoutState.SideCollapsed) LogicLayoutState.SideCollapsed = false; };
            _vsplit = LogicSplitter.Bar(vs, DockMode.Left, Divider);
            _body.DockedChildren.Add(_vsplit);

            _side = new LayoutBox { DockMode = DockMode.Left };
            _side.NineSlice.Flags = NineSliceFlags.None;
            _side.DockedChildren.ClipChildren = false;
            _body.DockedChildren.Add(_side);
            _summaryHost = new LogicPanelHost("ZtxLogicPanel/PanelSummary", _session.Summary.Root) { DockMode = DockMode.Top };
            _summaryHost.CollapseClicked = () => LogicLayoutState.SummaryCollapsed = !LogicLayoutState.SummaryCollapsed;
            _summaryHost.CloseClicked = () => LogicLayoutState.SummaryClosed = true;
            _side.DockedChildren.Add(_summaryHost);
            var hs = new LogicSplitter();
            hs.Started = () => _dragStart = _sumH;
            hs.Moved = d => LogicLayoutState.SummaryHeight = _dragStart + d.Y;
            _hsplit = LogicSplitter.Bar(hs, DockMode.Top, Divider);
            _side.DockedChildren.Add(_hsplit);
            _partsHost = new LogicPanelHost("ZtxLogicPanel/PanelParts", _session.Parts.Root) { DockMode = DockMode.Top };
            _partsHost.AddHeaderWidget(_session.Parts.FilterButton);
            _partsHost.AddHeaderWidget(SizeButton(LogicUiScale.Area.Parts, 34f));
            _summaryHost.AddHeaderWidget(SizeButton(LogicUiScale.Area.Summary, 34f));
            _partsHost.CollapseClicked = () => LogicLayoutState.PartsCollapsed = !LogicLayoutState.PartsCollapsed;
            _partsHost.CloseClicked = () => LogicLayoutState.PartsClosed = true;
            _side.DockedChildren.Add(_partsHost);
            _summaryHost.PopOutEnabled = true;
            _partsHost.PopOutEnabled = true;
            _summaryHost.PopOutClicked = () => PopOut(LogicPanelId.Summary);
            _partsHost.PopOutClicked = () => PopOut(LogicPanelId.Parts);

            Parented += OnWindowParented;
            Unparenting += (s, e) => BoundsProvider = null;
            SelfDeactivated += delegate { HoveredPart = null; _session.Picker.Close(); _session.Editor.Close(); };
            this.BeforeInputWhileActive(OnUpdatingUIState);
            ConsoleWindowCost.Extra = () => LogicRulesTab.WidgetCounts;
            WidgetRules.AddDialogTweens(this);
            WidgetRules.AddBlur(this);
        }

        private void OnWindowParented(object sender, ParentEventArgs e)
        {
            Vector2 screen = Parent.RectProvider.ScreenRect.Size;
            if (!_sized)
            {
                _sized = true;
                Size = new Vector2(Math.Max(760f, screen.X * WidthShare), Math.Max(360f, screen.Y * HeightShare));
            }
            ScreenRect = Rect.CenteredOn(screen / 2f, Size);
            BoundsProvider = Parent.RectProvider;
        }

        private void AddToolbarButtons(LayoutBox bar)
        {
            _toggleSummary = LogicGui.SmallButton("ZtxLogicPanel/ToggleSummary", 90f * LogicUiScale.Rules, LogicPalette.Dim, "ZtxLogicPanel/TipToggleSummary");
            _toggleSummary.Clicked += delegate
            {
                if (LogicLayoutState.SummaryPopped) LogicFloatingPanel.Raise(_game, LogicPanelId.Summary);
                else ToggleClosed(ref LogicLayoutState.SummaryClosed, ref LogicLayoutState.SummaryCollapsed);
            };
            bar.DockedChildren.Add(_toggleSummary);
            _toggleParts = LogicGui.SmallButton("ZtxLogicPanel/ToggleParts", 70f * LogicUiScale.Rules, LogicPalette.Dim, "ZtxLogicPanel/TipToggleParts");
            _toggleParts.Clicked += delegate
            {
                if (LogicLayoutState.PartsPopped) LogicFloatingPanel.Raise(_game, LogicPanelId.Parts);
                else ToggleClosed(ref LogicLayoutState.PartsClosed, ref LogicLayoutState.PartsCollapsed);
            };
            bar.DockedChildren.Add(_toggleParts);
            Button pop = LogicIcons.Make(LogicIcons.PopOut, "ZtxLogicPanel/PanelPopOut", 34f * LogicUiScale.Rules, "ZtxLogicPanel/TipPopOutRules");
            pop.Clicked += delegate
            {
                if (LogicLayoutState.RulesPopped) LogicFloatingPanel.Raise(_game, LogicPanelId.Rules);
                else PopOut(LogicPanelId.Rules);
            };
            bar.DockedChildren.Add(pop);
            bar.DockedChildren.Add(SizeButton(LogicUiScale.Area.Rules, 34f * LogicUiScale.Rules));
        }

        private static void ToggleClosed(ref bool closed, ref bool collapsed)
        {
            closed = !closed;
            if (!closed) { collapsed = false; LogicLayoutState.SideCollapsed = false; }
        }

        private void DragPane(float dx)
        {
            float w = _body.ScreenRect.Size.X;
            float maxPane = w - Divider - SideMin;
            float want = _dragStart + dx;
            if (want > maxPane + SideMin * 0.5f) LogicLayoutState.SideCollapsed = true;
            else
            {
                LogicLayoutState.SideCollapsed = false;
                LogicLayoutState.PaneWidth = Math.Max(PaneMin, Math.Min(maxPane, want));
            }
        }

        private void OnUpdatingUIState(object sender, EventArgs e)
        {
            try
            {
                if (_session.ConsoleGone())
                {
                    SelfActive = false;
                    HoveredPart = null;
                    return;
                }
                _session.Tick();
                Layout();
                if (_toggleSummary != null) LogicGui.SetTextColour(_toggleSummary, LogicLayoutState.SummaryClosed ? LogicPalette.Dim : LogicPalette.LitValue);
                if (_toggleParts != null) LogicGui.SetTextColour(_toggleParts, LogicLayoutState.PartsClosed ? LogicPalette.Dim : LogicPalette.LitValue);
            }
            catch (Exception ex)
            {
                Log.Exception("LogicConsoleWindow.OnUpdatingUIState", ex);
                SelfActive = false;
            }
        }

        private void Layout()
        {
            Vector2 size = _body.ScreenRect.Size;
            float w = size.X, h = size.Y;
            if (w <= 0f || h <= 0f) return;
            bool rulesIn = !LogicLayoutState.RulesPopped;
            bool sumIn = !LogicLayoutState.SummaryClosed && !LogicLayoutState.SummaryPopped;
            bool partsIn = !LogicLayoutState.PartsClosed && !LogicLayoutState.PartsPopped;
            bool sideWanted = sumIn || partsIn;
            bool sideOpen = sideWanted && !(rulesIn && LogicLayoutState.SideCollapsed);

            float paneW, sideW;
            if (!rulesIn) { paneW = 0f; sideW = sideWanted ? w : 0f; }
            else if (!sideWanted) { paneW = w; sideW = 0f; }
            else if (!sideOpen) { paneW = w - Divider; sideW = 0f; }
            else
            {
                float maxPane = Math.Max(0f, w - Divider - SideMin);
                float ideal = float.IsNaN(LogicLayoutState.PaneWidth) ? Math.Min(PaneIdeal, maxPane) : LogicLayoutState.PaneWidth;
                paneW = Math.Min(maxPane, Math.Max(Math.Min(PaneMin, maxPane), ideal));
                sideW = w - Divider - paneW;
            }
            _paneW = paneW;
            if (rulesIn) LogicGui.SetWidth(_session.RulesPage, paneW);
            LogicGui.SetActive(_vsplit, rulesIn && sideWanted);
            LogicGui.SetActive(_side, sideW > 0f);
            LogicGui.SetWidth(_side, sideW);
            if (sideW <= 0f) return;

            _summaryHost.SetCollapsed(LogicLayoutState.SummaryCollapsed);
            _partsHost.SetCollapsed(LogicLayoutState.PartsCollapsed);
            LogicGui.SetActive(_summaryHost, sumIn);
            LogicGui.SetActive(_partsHost, partsIn);
            bool sc = LogicLayoutState.SummaryCollapsed, pc = LogicLayoutState.PartsCollapsed;
            bool split = sumIn && partsIn && !sc && !pc;
            LogicGui.SetActive(_hsplit, split);
            float header = LogicPanelHost.HeaderHeight + 2f;
            float sumH = 0f, partsH = 0f;
            if (sumIn && partsIn)
            {
                if (sc && pc) { sumH = header; partsH = header; }
                else if (sc) { sumH = header; partsH = h - sumH; }
                else if (pc) { partsH = header; sumH = h - partsH; }
                else
                {
                    float want = float.IsNaN(LogicLayoutState.SummaryHeight) ? h * 0.4f : LogicLayoutState.SummaryHeight;
                    sumH = Math.Max(header + SummaryMin, Math.Min(h - Divider - header - SummaryMin, want));
                    partsH = h - Divider - sumH;
                }
            }
            else if (sumIn) sumH = sc ? header : h;
            else if (partsIn) partsH = pc ? header : h;
            _sumH = sumH;
            if (sumIn) _summaryHost.SetSize(sideW, sumH);
            if (partsIn) _partsHost.SetSize(sideW, partsH);
        }
        private Button SizeButton(LogicUiScale.Area area, float width)
        {
            Button b = LogicIcons.Make(LogicIcons.TextSize, "ZtxLogicPanel/SizeLabel", width);
            b.ToolTips.Text = Strings.FormatText("ZtxLogicPanel/TipSizeFmt", LogicUiScale.Percent(area));
            b.Clicked += delegate
            {
                LogicUiScale.Next(area);
                b.ToolTips.Text = Strings.FormatText("ZtxLogicPanel/TipSizeFmt", LogicUiScale.Percent(area));
                _session.Rescale(area);
            };
            return b;
        }

        private void PopOut(LogicPanelId id)
        {
            Widget content;
            string title;
            Vector2 size;
            switch (id)
            {
                case LogicPanelId.Rules:
                    if (LogicLayoutState.RulesPopped) return;
                    content = _session.RulesPage;
                    _body.DockedChildren.Remove(content);
                    LogicLayoutState.RulesPopped = true;
                    title = "ZtxLogicPanel/PanelRules";
                    size = new Vector2(Math.Max(PaneMin, _paneW), _body.ScreenRect.Size.Y);
                    break;
                case LogicPanelId.Summary:
                    if (LogicLayoutState.SummaryPopped) return;
                    content = _summaryHost.Detach();
                    LogicLayoutState.SummaryPopped = true;
                    title = "ZtxLogicPanel/PanelSummary";
                    size = new Vector2(Math.Max(420f, _summaryHost.Width), Math.Max(240f, _summaryHost.Height));
                    break;
                default:
                    if (LogicLayoutState.PartsPopped) return;
                    content = _partsHost.Detach();
                    LogicLayoutState.PartsPopped = true;
                    title = "ZtxLogicPanel/PanelParts";
                    size = new Vector2(Math.Max(420f, _partsHost.Width), Math.Max(300f, _partsHost.Height));
                    break;
            }
            if (content == null) return;
            content.SelfActive = true;
            LogicFloatingPanel.Open(_game, id, title, content, size, DockBack);
        }

        private void DockBack(LogicPanelId id, Widget content)
        {
            switch (id)
            {
                case LogicPanelId.Rules:
                    content.DockMode = DockMode.Left;
                    _body.DockedChildren.Insert(0, content);
                    LogicLayoutState.RulesPopped = false;
                    break;
                case LogicPanelId.Summary:
                    _summaryHost.Attach(content);
                    LogicLayoutState.SummaryPopped = false;
                    break;
                default:
                    _partsHost.Attach(content);
                    LogicLayoutState.PartsPopped = false;
                    break;
            }
        }

        public static void Reopen(GameRoot game)
        {
            Part c = s_session?.Console;
            if (c != null && !s_session.ConsoleGone()) Open(game, c);
        }
    }
}
