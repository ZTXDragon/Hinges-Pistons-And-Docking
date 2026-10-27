using System;
using System.Collections.Generic;
using Cosmoteer.Game;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Graphics.Text;
using Halfling.Gui;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class LogicPartsPanel
    {
        private const int TextFont = 14;
        private const int GroupFont = 16;
        private const float CharWidthAt14 = 7f;
        private const int REFRESH_EVERY = 6;
        private const double BUILD_BUDGET_MS = 8.0;
        private static float LineHeight => 20f * LogicUiScale.Parts;
        private static float GroupHeight => 24f * LogicUiScale.Parts;
        private static float GroupGap => 10f * LogicUiScale.Parts;
        private static float CardPad => 6f * LogicUiScale.Parts;
        private static float MenuRowHeight => 24f * LogicUiScale.Parts;
        private static float CharWidth => CharWidthAt14 * LogicUiScale.Parts;

        private sealed class Card
        {
            public Part Part;
            public LayoutBox Box;
            public Button Name;
            public Label Status;
            public Label Settings;
            public string LastStatus, LastSettings;
            public float LastWidth = -1f;
        }

        public readonly ScrollBox Root;
        public readonly Button FilterButton;
        private readonly GameRoot _game;
        private readonly LayoutBox _menu;
        private readonly List<Widget> _menuWidgets = new List<Widget>();
        private readonly List<Widget> _widgets = new List<Widget>();
        private readonly List<Card> _cards = new List<Card>();
        private readonly List<Part> _builtFor = new List<Part>();
        private readonly LogicBuildQueue _queue = new LogicBuildQueue();
        private string _filterSig = "";
        private string _typesSig = "";
        private bool _built;
        private bool _menuOpen;
        private int _frame;

        public LogicPartsPanel(GameRoot game, LogicPickerPopup picker)
        {
            _game = game;
            Root = new ScrollBox();
            Root.NineSlice.Flags = NineSliceFlags.None;
            Root.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
            Root.Children.BorderPadding = new Borders(6f, 6f, 6f + Root.VerticalScrollBar.Width, 6f);
            Root.Children.WidgetPadding = new Vector2(4f);
            Root.ScrollBarController.ShowVertical = ScrollBarShowMode.WhenNeeded;
            _menu = new LayoutBox();
            _menu.NineSlice.Flags = NineSliceFlags.None;
            _menu.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
            _menu.Children.WidgetPadding = new Vector2(2f);
            _menu.SelfActive = false;
            Root.AddChild(_menu);
            FilterButton = LogicIcons.Make(LogicIcons.Filter, "ZtxLogicPanel/Filter", 34f, "ZtxLogicPanel/TipFilter");
            FilterButton.Clicked += delegate { _menuOpen = !_menuOpen; LogicGui.SetActive(_menu, _menuOpen); };
        }

        public void Reset()
        {
            _queue.Clear();
            ClearRows();
            _typesSig = "";
        }

        private void ClearRows()
        {
            foreach (Widget w in _widgets) Root.Children.Remove(w);
            _widgets.Clear();
            _cards.Clear();
            _builtFor.Clear();
            _built = false;
        }

        private float TextWidth => Root.ScreenRect.Size.X - 12f - Root.VerticalScrollBar.Width - 2f * CardPad - 4f;

        public void Refresh(List<Part> members, bool anyNetwork)
        {
            _frame++;
            RefreshMenu(members);
            string filterSig = string.Join(",", LogicLayoutState.HiddenTypes);
            if (!_built || filterSig != _filterSig || !LogicConsoleModel.SameMembers(members, _builtFor))
            {
                _filterSig = filterSig;
                Rebuild(members, anyNetwork);
            }
            if (_queue.Pending > 0) _queue.Run(BUILD_BUDGET_MS);
            float width = TextWidth;
            for (int i = 0; i < _cards.Count; i++)
            {
                Card c = _cards[i];
                if (c.Part == null || c.Part.IsDestroyed) continue;
                if ((_frame + i) % REFRESH_EVERY == 0 || Math.Abs(c.LastWidth - width) > 1f) RefreshCard(c, width);
            }
        }

        private void Rebuild(List<Part> members, bool anyNetwork)
        {
            _queue.Clear();
            ClearRows();
            _built = true;
            _builtFor.AddRange(members);
            if (!anyNetwork) { AddNote("ZtxLogicPanel/NoNetwork"); return; }
            if (members.Count == 0) { AddNote("ZtxLogicPanel/NoParts"); return; }
            var order = new List<string>();
            var groups = new Dictionary<string, List<Part>>();
            foreach (Part p in members)
            {
                string id = p.Rules.ID.ToString();
                if (LogicLayoutState.HiddenTypes.Contains(id)) continue;
                if (!groups.TryGetValue(id, out List<Part> list)) { groups[id] = list = new List<Part>(); order.Add(id); }
                list.Add(p);
            }
            bool first = true;
            foreach (string id in order)
            {
                List<Part> list = groups[id];
                Part sample = list[0];
                bool gap = !first;
                first = false;
                _queue.Enqueue("group " + id, () => AddGroupHeader(sample, list.Count, gap));
                foreach (Part p in list)
                {
                    Part captured = p;
                    _queue.Enqueue("part " + p.Location, () => AddCard(captured));
                }
            }
        }

        private void Add(Widget w)
        {
            Root.AddChild(w);
            _widgets.Add(w);
            LogicUiScale.ApplyFonts(w, LogicUiScale.Parts);
        }

        private void AddNote(string key)
        {
            Label l = LogicGui.WrapLabel(TextFont, LogicPalette.Neutral);
            LogicGui.FitWrapped(l, Strings.GetText(key), Math.Max(200f, TextWidth), LineHeight, CharWidth);
            Add(l);
        }

        private void AddGroupHeader(Part sample, int count, bool gap)
        {
            if (gap) Add(new Widget { Height = GroupGap });
            var l = new Label { Height = GroupHeight };
            l.Text = Strings.FormatText("ZtxLogicPanel/GroupCountFmt", sample.Rules.NameKey.ToString(), count);
            l.TextRenderer.FontSize = GroupFont;
            l.TextRenderer.Color = LogicPalette.ToColor(LogicNet.Hex(LogicNet.KindOf(sample)));
            l.TextRenderer.HAlignment = HAlignment.Left;
            Add(l);
            Add(LogicGui.Line(1f));
        }

        private void AddCard(Part p)
        {
            var c = new Card { Part = p, Box = new LayoutBox() };
            c.Box.NineSlice.CopySettingsFrom(WidgetRules.Instance.HollowWidget.NineSlice);
            c.Box.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
            c.Box.Children.BorderPadding = new Borders(CardPad);
            c.Box.Children.WidgetPadding = new Vector2(0f, 2f);
            c.Box.MouseEntered += delegate { LogicConsoleWindow.HoveredPart = p; };
            c.Box.MouseLeft += delegate { if (ReferenceEquals(LogicConsoleWindow.HoveredPart, p)) LogicConsoleWindow.HoveredPart = null; };
            c.Name = LogicGui.FlatButton(LineHeight, TextFont, shrink: false);
            c.Name.Text = LogicNet.PartText(p, true);
            c.Name.ToolTips.TextProvider = Strings.KeyString("ZtxLogicPanel/TipPartRow");
            c.Name.Clicked += delegate { Focus(p); };
            c.Box.AddChild(c.Name);
            c.Status = LogicGui.WrapLabel(TextFont, null);
            c.Box.AddChild(c.Status);
            c.Settings = LogicGui.WrapLabel(TextFont, LogicPalette.Neutral);
            c.Box.AddChild(c.Settings);
            _cards.Add(c);
            Add(c.Box);
            RefreshCard(c, TextWidth);
        }

        private void RefreshCard(Card c, float width)
        {
            string status = LogicConsoleModel.PartStatus(c.Part);
            string settings = LogicConsoleModel.PartSettings(_game, c.Part);
            if (status == c.LastStatus && settings == c.LastSettings && Math.Abs(c.LastWidth - width) <= 1f) return;
            c.LastStatus = status;
            c.LastSettings = settings;
            c.LastWidth = width;
            float h = LineHeight;
            h += LogicGui.FitWrapped(c.Status, status, width, LineHeight, CharWidth);
            h += LogicGui.FitWrapped(c.Settings, settings, width, LineHeight, CharWidth);
            LogicGui.SetHeight(c.Box, h + 2f * CardPad + 6f);
        }

        private void Focus(Part p)
        {
            try
            {
                if (p == null || p.IsDestroyed || _game?.Sim?.Camera == null) return;
                _game.Sim.Camera.ViewController.FocusOn(p, append: false, playSound: true, cancelOnArrival: true);
            }
            catch (Exception ex) { Log.Exception("LogicPartsPanel.Focus", ex); }
        }

        private void RefreshMenu(List<Part> members)
        {
            var ids = new List<string>();
            var samples = new Dictionary<string, Part>();
            foreach (Part p in members)
            {
                string id = p.Rules.ID.ToString();
                if (samples.ContainsKey(id)) continue;
                samples[id] = p;
                ids.Add(id);
            }
            string sig = string.Join(",", ids) + "|" + string.Join(",", LogicLayoutState.HiddenTypes) + "|" + LogicUiScale.Percent(LogicUiScale.Area.Parts);
            if (sig == _typesSig) return;
            _typesSig = sig;
            foreach (Widget w in _menuWidgets) _menu.Children.Remove(w);
            _menuWidgets.Clear();
            var bar = new LayoutBox { Height = MenuRowHeight };
            bar.NineSlice.Flags = NineSliceFlags.None;
            bar.DockedChildren.WidgetPadding = new Vector2(4f, 0f);
            Button all = LogicGui.SmallButton("ZtxLogicPanel/FilterAll", 60f * LogicUiScale.Parts, LogicPalette.ActAdd, null);
            all.Clicked += delegate { LogicLayoutState.HiddenTypes.Clear(); };
            bar.DockedChildren.Add(all);
            Button none = LogicGui.SmallButton("ZtxLogicPanel/FilterNone", 60f * LogicUiScale.Parts, LogicPalette.ActDelete, null);
            none.Clicked += delegate { foreach (string id in ids) LogicLayoutState.HiddenTypes.Add(id); };
            bar.DockedChildren.Add(none);
            _menu.AddChild(bar);
            _menuWidgets.Add(bar);
            foreach (string id in ids)
            {
                Part sample = samples[id];
                bool shown = !LogicLayoutState.HiddenTypes.Contains(id);
                Button b = LogicGui.FlatButton(MenuRowHeight, TextFont, shrink: false);
                string hex = shown ? LogicNet.Hex(LogicNet.KindOf(sample)) : LogicPalette.Dim;
                b.Text = LogicPalette.Wrap(hex, (shown ? "[x] " : "[ ] ") + sample.Rules.NameKey.ToString());
                string captured = id;
                b.Clicked += delegate
                {
                    if (!LogicLayoutState.HiddenTypes.Remove(captured)) LogicLayoutState.HiddenTypes.Add(captured);
                };
                _menu.AddChild(b);
                _menuWidgets.Add(b);
            }
            LayoutBox line = LogicGui.Line(2f);
            _menu.AddChild(line);
            _menuWidgets.Add(line);
            LogicUiScale.ApplyFonts(_menu, LogicUiScale.Parts);
            float h = (ids.Count + 1) * (MenuRowHeight + 2f) + 2f + 8f;
            LogicGui.SetHeight(_menu, h);
        }
    }
}
