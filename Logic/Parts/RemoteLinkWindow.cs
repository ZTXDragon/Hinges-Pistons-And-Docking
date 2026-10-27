using System;
using System.Collections.Generic;
using System.Text;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Geometry;
using Halfling.Graphics.Text;
using Halfling.Gui;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(PartTriggerButton), "OnClicked")]
    internal static class RemoteLinkOpenPatch
    {
        public static readonly ID<PartTriggerGuiRules> TriggerId = new ID<PartTriggerGuiRules>("ztx_link_panel");

        static bool Prefix(PartTriggerButton __instance)
        {
            try
            {
                if (!__instance.Rules.TriggerID.Equals(TriggerId)) return true;
                Part target = null;
                foreach (Part p in __instance.Sim.PlayerInput.SelectedParts)
                {
                    if (PartUITrigger.GetOnPart(p, TriggerId) != null) { target = p; break; }
                }
                if (target == null) return false;
                RemoteLinkWindow.Open(__instance.Game, target);
            }
            catch (Exception ex)
            {
                Log.Exception("RemoteLinkOpenPatch.Prefix", ex);
            }
            return false;
        }
    }

    internal sealed class RemoteLinkWindow : RailPartWindow
    {
        private static RemoteLinkWindow s_instance;
        public static bool IsShowing => s_instance != null && s_instance.IsActive;

        public static void Reset() => s_instance = null;

        private readonly TextEditField _name;
        private readonly DropList _target;
        private readonly Dictionary<int, ListItem> _targetItems = new Dictionary<int, ListItem>();
        private readonly List<Part> _candidates = new List<Part>();
        private readonly List<string> _targetKeys = new List<string>();
        private readonly List<Part> _targetParts = new List<Part>();
        private readonly Label _status;
        private int _lastN1 = int.MinValue, _lastN2 = int.MinValue;
        private string _sig;

        public static void Open(GameRoot game, Part link)
        {
            if (game == null || link == null) return;
            if (s_instance == null || !ReferenceEquals(s_instance._game, game))
            {
                s_instance = new RemoteLinkWindow(game);
                game.Gui.FloatingWindows.AddChild(s_instance);
            }
            s_instance.Bind(link);
            s_instance.SelfActive = true;
            game.Gui.FloatingWindows.Children.RaiseToFront(s_instance);
            LogicConsoleHighlight.Attach(game.Sim);
        }

        private RemoteLinkWindow(GameRoot game) : base(game, "ZtxLinkPanel/Title", new Vector2(520f, 200f))
        {
            LayoutBox r1 = Row();
            r1.DockedChildren.Add(RowLabel("ZtxLinkPanel/Name", "ZtxLinkPanel/TipName"));
            _name = LogicGui.Typing(new TextEditField(TextEditField.Style.SingleLine) { DockMode = DockMode.Left, Width = FieldWidth });
            _name.ToolTips.TextProvider = Strings.KeyString("ZtxLinkPanel/TipName");
            _name.TextChanged += OnNameChanged;
            r1.DockedChildren.Add(_name);
            AddChild(r1);

            LayoutBox r2 = Row();
            r2.DockedChildren.Add(RowLabel("ZtxLinkPanel/ConnectTo", "ZtxLinkPanel/TipConnect"));
            _target = MakeList(FieldWidth);
            _target.ToolTips.TextProvider = Strings.KeyString("ZtxLinkPanel/TipConnect");
            _target.ListBox.SelectionManager.WidgetSelected += OnTargetPicked;
            r2.DockedChildren.Add(_target);
            AddChild(r2);

            _status = new Label { Height = RowHeight * 2f };
            _status.TextRenderer.XmlFormatting = true;
            _status.TextRenderer.OversizeMode = OversizeMode.Wrap;
            AddChild(_status);
            SelfDeactivated += delegate { LogicConsoleWindow.HoveredPart = null; };
        }

        private void Bind(Part link)
        {
            _part = link;
            _sig = null;
            _refreshing = true;
            try
            {
                string name = RemoteLinks.StoredName(link);
                LogicGui.SetFieldText(_name, name);
                _lastN1 = LogicRuleDef.PackName(name, 0);
                _lastN2 = LogicRuleDef.PackName(name, 5);
            }
            finally { _refreshing = false; }
        }

        protected override void OnUpdatingUIState(object sender, EventArgs e)
        {
            try
            {
                if (PartGone()) { SelfActive = false; return; }
                Part p = _part;
                RemoteLinks.Candidates(p.Ship, _candidates);
                var entries = new List<(int, string)> { (0, Text("ZtxLinkPanel/None")) };
                _targetKeys.Clear();
                _targetParts.Clear();
                _targetKeys.Add("");
                _targetParts.Add(null);
                var sig = new StringBuilder();
                foreach (Part c in _candidates)
                {
                    if (ReferenceEquals(c, p)) continue;
                    string disp = RemoteLinks.DisplayName(c);
                    if (!ReferenceEquals(c.Ship, p.Ship)) disp += Text("ZtxLinkPanel/OtherShip");
                    _targetKeys.Add(RemoteLinks.Key(RemoteLinks.DisplayName(c)));
                    _targetParts.Add(c);
                    entries.Add((_targetKeys.Count - 1, disp));
                    sig.Append(disp).Append('|');
                }
                string stored = RemoteLinks.TargetKey(p);
                int current = 0;
                for (int i = 1; i < _targetKeys.Count; i++)
                    if (_targetKeys[i] == stored) { current = i; break; }
                if (current == 0 && stored.Length > 0)
                {
                    _targetKeys.Add(stored);
                    _targetParts.Add(null);
                    entries.Add((_targetKeys.Count - 1, stored + Text("ZtxLinkPanel/Missing")));
                    current = _targetKeys.Count - 1;
                    sig.Append(stored).Append("?|");
                }
                string signature = sig.ToString();
                if (signature != _sig)
                {
                    Fill(_target, _targetItems, entries);
                    _sig = signature;
                }
                Select(_target, _targetItems, current);
                Part outline = null;
                foreach (var kv in _targetItems)
                    if (kv.Value.IsActive && kv.Value.IsMouseOver && kv.Key < _targetParts.Count && _targetParts[kv.Key] != null) { outline = _targetParts[kv.Key]; break; }
                if (outline == null && current > 0 && current < _targetParts.Count) outline = _targetParts[current];
                LogicConsoleWindow.HoveredPart = outline ?? p;
                string status = RemoteLinks.Status(p);
                if (_status.Text != status) _status.Text = status;
            }
            catch (Exception ex)
            {
                Log.Exception("RemoteLinkWindow.OnUpdatingUIState", ex);
                SelfActive = false;
            }
        }

        private void OnNameChanged(object sender, EventArgs e)
        {
            if (_refreshing || _part == null) return;
            try
            {
                string clean = LogicRuleDef.Clean(_name.Text);
                int n1 = LogicRuleDef.PackName(clean, 0), n2 = LogicRuleDef.PackName(clean, 5);
                if (n1 == _lastN1 && n2 == _lastN2) return;
                _lastN1 = n1;
                _lastN2 = n2;
                Write((RemoteLinks.Name1Id, n1), (RemoteLinks.Name2Id, n2));
            }
            catch (Exception ex) { Log.Exception("RemoteLinkWindow.OnNameChanged", ex); }
        }

        private void OnTargetPicked(object sender, WidgetEventArgs e)
        {
            if (_refreshing || _part == null) return;
            try
            {
                if (!(e.Widget.UserData is int index) || index < 0 || index >= _targetKeys.Count) return;
                string key = _targetKeys[index];
                int n1 = LogicRuleDef.PackName(key, 0), n2 = LogicRuleDef.PackName(key, 5);
                if (LogicSource.Mode(_part, RemoteLinks.To1Id, 0) == n1 && LogicSource.Mode(_part, RemoteLinks.To2Id, 0) == n2) return;
                Write((RemoteLinks.To1Id, n1), (RemoteLinks.To2Id, n2));
                Log.Info("remote link at " + _part.Location + ": connect to -> " + (key.Length == 0 ? "none" : key) + ".");
            }
            catch (Exception ex) { Log.Exception("RemoteLinkWindow.OnTargetPicked", ex); }
        }
    }
}
