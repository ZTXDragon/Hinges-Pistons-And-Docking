using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships.Networks;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;
using Halfling.Gui;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class LogicConsoleSession
    {
        private static readonly ID<PartComponentRules> s_engineId = new ID<PartComponentRules>("Engine");
        private const float RulesGap = 10f;

        public readonly GameRoot Game;
        public readonly ScrollBox RulesPage;
        public readonly LogicRulesTab Rules;
        public readonly LogicSummaryPanel Summary;
        public readonly LogicPartsPanel Parts;
        public readonly LogicPickerPopup Picker;
        public readonly TextCellEditor Editor;
        public Part Console { get; private set; }

        private readonly List<PartSubnetwork<Part, PartComponent>> _subs = new List<PartSubnetwork<Part, PartComponent>>();
        private readonly List<Part> _members = new List<Part>();
        private const int MEMBERS_EVERY_FRAMES = 15;
        private int _membersAge;
        private Part _membersFor;
        private LogicRuleText _text;
        private Halfling.Timing.Time _lastTick;
        private bool _ticked;
        private int _fontFrame;
        private const int FONT_EVERY = 15;

        public LogicConsoleSession(GameRoot game, LayoutBox<Widget, Widget> host)
        {
            Game = game;
            RulesPage = new ScrollBox();
            RulesPage.NineSlice.Flags = NineSliceFlags.None;
            RulesPage.Children.LayoutAlgorithm = LayoutAlgorithms.TileTopLeftVertical;
            RulesPage.Children.BorderPadding = new Borders(10f);
            RulesPage.Children.WidgetPadding = new Vector2(RulesGap);
            RulesPage.ScrollBarController.ShowVertical = ScrollBarShowMode.WhenNeeded;
            RulesPage.ScrollBarController.ShowHorizontal = ScrollBarShowMode.WhenNeeded;
            Picker = new LogicPickerPopup(host);
            Editor = new TextCellEditor(host);
            Rules = new LogicRulesTab(game, RulesPage, Picker, Editor);
            Summary = new LogicSummaryPanel();
            Summary.JumpTo = r => Rules.JumpTo(r);
            Parts = new LogicPartsPanel(game, Picker);
        }

        public void Bind(Part console)
        {
            if (ReferenceEquals(console, Console)) return;
            Console = console;
            _text = null;
            Picker.Close();
            Editor.Close();
            Rules.Reset();
            Summary.Reset();
            Parts.Reset();
        }

        public void Rescale(LogicUiScale.Area area)
        {
            switch (area)
            {
                case LogicUiScale.Area.Rules:
                    Picker.Close();
                    Editor.Close();
                    Rules.Reset();
                    break;
                case LogicUiScale.Area.Summary:
                    Summary.Reset();
                    break;
                default:
                    Parts.Reset();
                    break;
            }
            _fontFrame = FONT_EVERY - 1;
            Log.Info("console: " + area + " size " + LogicUiScale.Percent(area) + " %.");
        }

        private void ApplyFonts()
        {
            LogicUiScale.ApplyFonts(RulesPage, LogicUiScale.Rules);
            LogicUiScale.ApplyFonts(Picker.ListWidget, LogicUiScale.Rules);
            LogicUiScale.ApplyFonts(Summary.Root, LogicUiScale.Summary);
            LogicUiScale.ApplyFonts(Parts.Root, LogicUiScale.Parts);
        }

        public bool ConsoleGone()
        {
            Part c = Console;
            return c == null || c.IsDestroyed || c.Ship == null || !c.Ship.ExistsInSim(Game.Sim);
        }

        public void Tick()
        {
            Halfling.Timing.Time now = Halfling.App.Clock.Time;
            if (_ticked && now == _lastTick) return;
            _ticked = true;
            _lastTick = now;
            if (ConsoleGone()) return;
            Part c = Console;
            ConsoleWindowCost.BeginFrame();
            long t0 = LogicCost.Now;
            if (LogicConsoleModel.MembersDue(_membersAge, MEMBERS_EVERY_FRAMES, !ReferenceEquals(_membersFor, c)))
            {
                LogicNet.SubnetworksOf(c, _subs);
                LogicConsoleModel.CollectMembers(c.Ship, _subs, _members);
                _membersAge = 0;
                _membersFor = c;
            }
            else _membersAge++;
            long t1 = LogicCost.Now;
            Parts.Refresh(_members, _subs.Count > 0);
            long t2 = LogicCost.Now;
            c.TryGetComponent<ZtxRuleEngineComponent>(s_engineId, out ZtxRuleEngineComponent engine);
            Rules.Refresh(c, engine, _members);
            long t3 = LogicCost.Now;
            if (engine == null) _text = null;
            else if (_text == null || !ReferenceEquals(_text.Engine, engine)) _text = new LogicRuleText(engine, c);
            Summary.Refresh(_text);
            long t4 = LogicCost.Now;
            ConsoleWindowCost.Add(ConsoleWindowCost.Phase.Members, t1 - t0);
            ConsoleWindowCost.Add(ConsoleWindowCost.Phase.Parts, t2 - t1);
            ConsoleWindowCost.Add(ConsoleWindowCost.Phase.Rules, t3 - t2);
            ConsoleWindowCost.Add(ConsoleWindowCost.Phase.Live, t4 - t3);
            if (++_fontFrame % FONT_EVERY == 0) ApplyFonts();
            ConsoleWindowCost.EndFrame();
        }
    }
}
