using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class AdapterToggles
    {
        public const int CHOICE_OFFSET = 1024, POSE_MAX = 4095, HASH_MASK = 0x3FFFFFFF;
        public const int PRESS = 1;

        private static readonly Regex s_tags = new Regex("<[^>]*>");

        internal sealed class Setting
        {
            public int Hash;
            public string Id;
            public string Name;
            public bool IsTrigger;
            public bool IsCoverage;
            public readonly List<(int Mode, string Label)> Choices = new List<(int, string)>();
        }

        public static int Hash(string id)
        {
            int v = (int)(MergeRecord.Hash(id ?? "") & HASH_MASK);
            return v == 0 ? 1 : v;
        }

        public static int TriggerHash(string id) => Hash("trigger:" + id);

        public static int PackChoice(int mode) => Math.Max(1, Math.Min(POSE_MAX, mode + CHOICE_OFFSET));

        public static int? UnpackChoice(int v) => v <= 0 ? (int?)null : v - CHOICE_OFFSET;

        public static bool Accepts(IList<int> guiModes, IList<int> available, int mode) =>
            guiModes != null && guiModes.Contains(mode) && (available == null || available.Contains(mode));

        public static string LabelOf(string tooltip)
        {
            string t = tooltip ?? "";
            int b = t.IndexOf("<b>", StringComparison.Ordinal);
            if (b >= 0)
            {
                int e = t.IndexOf("</b>", b + 3, StringComparison.Ordinal);
                string inner = e > b ? t.Substring(b + 3, e - b - 3) : t.Substring(b + 3);
                return s_tags.Replace(inner, "").Trim();
            }
            int nl = t.IndexOf('\n');
            if (nl >= 0) t = t.Substring(0, nl);
            int dash = t.IndexOf(" -- ", StringComparison.Ordinal);
            if (dash >= 0) t = t.Substring(0, dash);
            return s_tags.Replace(t, "").Trim();
        }

        public static string SettingName(string id, IList<string> labels, out IList<string> shortLabels)
        {
            var shorts = new List<string>();
            string prefix = null;
            bool shared = labels != null && labels.Count > 0;
            if (shared)
            {
                foreach (string l in labels)
                {
                    int colon = (l ?? "").IndexOf(": ", StringComparison.Ordinal);
                    string p = colon > 0 ? l.Substring(0, colon) : null;
                    if (p == null || (prefix != null && p != prefix)) { shared = false; break; }
                    prefix = p;
                }
            }
            if (labels != null)
                foreach (string l in labels)
                    shorts.Add(shared ? l.Substring(prefix.Length + 2) : l);
            shortLabels = shorts;
            return shared ? prefix : Pretty(id);
        }

        public static string Pretty(string id)
        {
            string s = (id ?? "").Replace('_', ' ').Trim();
            if (s.Length == 0) return "?";
            return char.ToUpper(s[0], CultureInfo.InvariantCulture) + s.Substring(1);
        }

        public static Part TargetOf(Part adapter)
        {
            if (adapter == null || adapter.IsDestroyed || adapter.Ship == null) return null;
            IntVector2 cell = adapter.Location + LogicSource.Facing(adapter);
            Part target = adapter.Ship.Parts[cell, PartRectType.Physical];
            return target == null || target.IsDestroyed || ReferenceEquals(target, adapter) ? null : target;
        }

        public static List<Setting> SettingsOf(Part target)
        {
            var list = new List<Setting>();
            if (target == null) return list;
            var gui = GameApp.Rules.Game.GameGui;
            var seen = new HashSet<int>();
            foreach (PartComponent c in target.Components)
            {
                if (c is PartUIToggle t)
                {
                    if (!t.IsVisible || !gui.ToggleGuisByID.TryGetValue(t.Rules.ToggleID, out PartToggleGuiRules g)) continue;
                    string id = t.Rules.ToggleID.ToString();
                    var s = new Setting { Hash = Hash(id), Id = id };
                    if (!seen.Add(s.Hash)) continue;
                    var modes = new List<int>();
                    var labels = new List<string>();
                    foreach (PartToggleChoice ch in g.Choices)
                    {
                        if (t.Rules.AvailableChoices != null && !t.Rules.AvailableChoices.Contains(ch.ModeNum)) continue;
                        modes.Add(ch.ModeNum);
                        labels.Add(LabelOf(ch.ButtonToolTipKey?.Text ?? ch.ButtonTextKey?.Text ?? ch.ModeNum.ToString(CultureInfo.InvariantCulture)));
                    }
                    s.Name = SettingName(id, labels, out IList<string> shorts);
                    for (int i = 0; i < modes.Count; i++) s.Choices.Add((modes[i], shorts[i].Length > 0 ? shorts[i] : modes[i].ToString(CultureInfo.InvariantCulture)));
                    list.Add(s);
                }
                else if (c is PartUITrigger tr)
                {
                    string id = tr.Rules.TriggerID.ToString();
                    if (id.StartsWith("ztx_", StringComparison.Ordinal) && id.EndsWith("_panel", StringComparison.Ordinal)) continue;
                    var s = new Setting { Hash = TriggerHash(id), Id = id, IsTrigger = true };
                    if (!seen.Add(s.Hash)) continue;
                    string label = gui.TriggerGuisByID.TryGetValue(tr.Rules.TriggerID, out PartTriggerGuiRules tg)
                        ? LabelOf(tg.ButtonToolTipKey?.Text ?? tg.ButtonTextKey?.Text ?? "") : "";
                    s.Name = label.Length > 0 ? label : Pretty(id);
                    s.Choices.Add((PRESS, Strings.GetText("ZtxAdapterPanel/Press")));
                    list.Add(s);
                }
            }
            list.AddRange(AdapterCoverage.SettingsOf(target));
            return list;
        }

        public static Setting Find(Part target, int hash)
        {
            foreach (Setting s in SettingsOf(target))
                if (s.Hash == hash) return s;
            return null;
        }

        public static string ApplyDo(Part adapter, LogicAction a, bool edge)
        {
            if (!a.Sided || !a.OnSet || a.Drive == 0) return "";
            int? mode = UnpackChoice(a.OnPose);
            if (!mode.HasValue) return "";
            Part target = TargetOf(adapter);
            if (target == null)
            {
                LogThrottle.Info("Adapter|none|" + adapter?.Location, "Logic adapter at " + adapter?.Location + ": no part at the arrow; nothing written.");
                return "";
            }
            if (AdapterCoverage.IsCoverageHash(a.Drive)) return AdapterCoverage.Apply(target, a.Drive, mode.Value);
            foreach (PartComponent c in target.Components)
            {
                if (c is PartUIToggle t && Hash(t.Rules.ToggleID.ToString()) == a.Drive) return Write(target, t, mode.Value);
                if (c is PartUITrigger tr && TriggerHash(tr.Rules.TriggerID.ToString()) == a.Drive) return edge ? Press(target, tr) : "";
            }
            LogThrottle.Info("Adapter|missing|" + adapter.Location + "|" + a.Drive, "Logic adapter at " + adapter.Location + ": " + target.Rules.ID + " has no such setting; nothing written.");
            return "";
        }

        private static string Write(Part target, PartUIToggle t, int mode)
        {
            Ship ship = target.Ship;
            if (ship == null || target.IsDestroyed || !t.IsOperational) return "";
            ID<PartToggleGuiRules> id = t.Rules.ToggleID;
            var modes = new List<int>();
            if (GameApp.Rules.Game.GameGui.ToggleGuisByID.TryGetValue(id, out PartToggleGuiRules g))
                foreach (PartToggleChoice ch in g.Choices) modes.Add(ch.ModeNum);
            if (!Accepts(modes, t.Rules.AvailableChoices, mode))
            {
                LogThrottle.Info("Adapter|refused|" + target.Location + "|" + id + "|" + mode,
                    "Logic adapter: " + target.Rules.ID + " at " + target.Location + " does not offer " + id + " = " + mode + "; nothing written.");
                return "";
            }
            if (ship.Sim?.Game?.Mode != null && ship.Sim.Game.Mode.IsToggleChoiceLocked(target.Rules, id, mode, ship)) return "";
            if (t.Mode == mode) return "";
            t.Mode = mode;
            t.Part.MatchingBlueprintPart?.OnPartUIToggleStatesChanged();
            return id + " = " + mode + " on " + target.Rules.ID + " at " + target.Location;
        }

        private static string Press(Part target, PartUITrigger tr)
        {
            if (target.Ship == null || target.IsDestroyed || !tr.IsOperational) return "";
            tr.Trigger();
            return "pressed " + tr.Rules.TriggerID + " on " + target.Rules.ID + " at " + target.Location;
        }
    }

    internal static class AdapterRead
    {
        public const int READ_OPERATIONAL = 0, READ_SETTING = 1, READ_STORAGE = 2, READ_HEAT = 3, READ_COUNT = 4;

        public static float SettingPercent(int index, int count)
        {
            if (count <= 1) return index > 0 ? 100f : 0f;
            return Math.Max(0f, Math.Min(100f, index * 100f / (count - 1)));
        }
    }
}
