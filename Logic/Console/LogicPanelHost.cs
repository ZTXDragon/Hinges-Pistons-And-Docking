using System;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Graphics.Text;
using Halfling.Gui;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class LogicPanelHost : LayoutBox
    {
        public static float HeaderHeight => 32f;
        private const float FrameInset = 2f;

        private readonly LayoutBox _header;
        private readonly Button _collapse;
        private readonly Button _popOut;
        private readonly Button _close;
        private readonly Label _title;
        private const float IconBase = 30f, TitleBase = 120f;
        private static float IconWidth => IconBase;
        private static float TitleWidth => TitleBase;
        public Widget Content { get; private set; }
        public bool Collapsed { get; private set; }

        public Action CollapseClicked, CloseClicked, PopOutClicked;

        public LogicPanelHost(string titleKey, Widget content)
        {
            NineSlice.CopySettingsFrom(WidgetRules.Instance.HollowWidget.NineSlice);
            DockedChildren.ClipChildren = false;
            _header = new LayoutBox { DockMode = DockMode.Top, Height = HeaderHeight };
            _header.NineSlice.CopySettingsFrom(WindowBox.Default.TitleBox.NineSlice);
            _header.DockedChildren.WidgetPadding = new Vector2(4f, 0f);
            _header.DockedChildren.ClipChildren = false;
            Button close = LogicIcons.Make(LogicIcons.Close, "ZtxLogicPanel/PanelClose", IconWidth, "ZtxLogicPanel/TipPanelClose");
            close.DockMode = DockMode.Right;
            _close = close;
            close.Clicked += delegate { CloseClicked?.Invoke(); };
            _header.DockedChildren.Add(close);
            _popOut = LogicIcons.Make(LogicIcons.PopOut, "ZtxLogicPanel/PanelPopOut", IconWidth, "ZtxLogicPanel/TipPanelPopOut");
            _popOut.DockMode = DockMode.Right;
            _popOut.Clicked += delegate { PopOutClicked?.Invoke(); };
            _popOut.SelfInputActive = false;
            _header.DockedChildren.Add(_popOut);
            _collapse = LogicIcons.Make(LogicIcons.Collapse, "ZtxLogicPanel/Collapse", IconWidth, "ZtxLogicPanel/TipPanelCollapse");
            _collapse.DockMode = DockMode.Right;
            _collapse.Clicked += delegate { CollapseClicked?.Invoke(); };
            _header.DockedChildren.Add(_collapse);
            _title = new Label { DockMode = DockMode.Left, Width = TitleWidth };
            Label title = _title;
            title.TextRenderer.CopySettingsFrom(WindowBox.Default.TitleLabel.TextRenderer);
            title.TextProvider = Strings.KeyString(titleKey);
            title.TextRenderer.HAlignment = HAlignment.Left;
            _header.DockedChildren.Add(title);
            DockedChildren.Add(_header);
            Attach(content);
        }

        public bool PopOutEnabled
        {
            set { if (_popOut.SelfInputActive != value) _popOut.SelfInputActive = value; }
        }

        public void AddHeaderWidget(Widget w)
        {
            w.DockMode = DockMode.Left;
            _header.DockedChildren.Add(w);
        }

        public void Attach(Widget content)
        {
            Content = content;
            if (content == null) return;
            content.DockMode = DockMode.Top;
            DockedChildren.Add(content);
            LogicGui.SetActive(content, !Collapsed);
        }

        public Widget Detach()
        {
            Widget c = Content;
            if (c != null) DockedChildren.Remove(c);
            Content = null;
            return c;
        }

        private bool _collapseSet;

        public void SetCollapsed(bool on)
        {
            if (_collapseSet && Collapsed == on) return;
            _collapseSet = true;
            Collapsed = on;
            LogicIcons.Set(_collapse, on ? LogicIcons.Expand : LogicIcons.Collapse, on ? "ZtxLogicPanel/Expand" : "ZtxLogicPanel/Collapse");
            if (Content != null) LogicGui.SetActive(Content, !on);
        }

        public void SetSize(float w, float h)
        {
            LogicGui.SetWidth(this, w);
            LogicGui.SetHeight(this, h);
            LogicGui.SetHeight(_header, HeaderHeight);
            LogicGui.SetWidth(_close, IconWidth);
            LogicGui.SetWidth(_popOut, IconWidth);
            LogicGui.SetWidth(_collapse, IconWidth);
            LogicGui.SetWidth(_title, TitleWidth);
            if (Content != null) LogicGui.SetHeight(Content, Math.Max(0f, h - HeaderHeight - FrameInset));
        }
    }
}
