using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Networks;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogicConsoleModel
    {
        public sealed class Column
        {
            public readonly ID<PartToggleGuiRules> ToggleId;
            public readonly string HeaderKey;
            public readonly bool ReadOnlyEngage;
            public Column(string toggleId, string headerKey, bool readOnlyEngage = false)
            {
                ToggleId = new ID<PartToggleGuiRules>(toggleId);
                HeaderKey = headerKey;
                ReadOnlyEngage = readOnlyEngage;
            }
        }

        private static readonly Column WeaponMode = new Column("ztx_weapon_mode", "ZtxLogicPanel/ColMode");
        private static readonly Column ProxRelation = new Column("ztx_prox_relation", "ZtxLogicPanel/ColRelation");
        private static readonly Column ProxRange = new Column("ztx_prox_range", "ZtxLogicPanel/ColRange");
        private static readonly Column ButtonState = new Column("ztx_logic_button", "ZtxLogicPanel/ColState");
        private static readonly Column PistonExtension = new Column("ztx_piston_extension", "ZtxLogicPanel/ColExtension");
        private static readonly Column MotorPosition = new Column("ztx_motor_position", "ZtxLogicPanel/ColPosition");
        private static readonly Column Strength = new Column("ztx_motor_strength", "ZtxLogicPanel/ColStrength");
        private static readonly Column HingeLeft = new Column("ztx_hinge_angle_left", "ZtxLogicPanel/ColLeft");
        private static readonly Column HingeRight = new Column("ztx_hinge_angle_right", "ZtxLogicPanel/ColRight");
        private static readonly Column Engage = new Column("on_off", "ZtxLogicPanel/ColEngage", readOnlyEngage: true);
        private static readonly Column DockActive = new Column("ztx_dock_active", "ZtxLogicPanel/ColSwitch");
        private static readonly Column PadActive = new Column("ztx_pad_active", "ZtxLogicPanel/ColSwitch");

        private static readonly Dictionary<string, Column[]> s_columns = new Dictionary<string, Column[]>
        {
            ["ztx.hinges_pistons_docking.weapon_sensor"] = new[] { WeaponMode },
            ["ztx.hinges_pistons_docking.damage_sensor"] = new Column[0],
            ["ztx.hinges_pistons_docking.proximity_sensor"] = new[] { ProxRelation, ProxRange },
            ["ztx.hinges_pistons_docking.ship_sensor"] = new Column[0],
            ["ztx.hinges_pistons_docking.logic_adapter"] = new Column[0],
            ["ztx.hinges_pistons_docking.indicator_lamp"] = new Column[0],
            ["ztx.hinges_pistons_docking.logic_button"] = new[] { ButtonState },
            ["ztx.hinges_pistons_docking.piston_base"] = new[] { PistonExtension, Strength, Engage },
            ["ztx.hinges_pistons_docking.corridor_piston_base"] = new[] { PistonExtension, Strength, Engage },
            ["ztx.hinges_pistons_docking.motor_base"] = new[] { Strength, Engage },
            ["ztx.hinges_pistons_docking.hinge_base"] = new[] { HingeLeft, HingeRight, Engage },
            ["ztx.hinges_pistons_docking.corridor_hinge_base"] = new[] { Strength, Engage },
            ["ztx.hinges_pistons_docking.dock_clamp"] = new[] { DockActive },
            ["ztx.hinges_pistons_docking.dock_pad"] = new[] { PadActive },
            ["ztx.hinges_pistons_docking.logic_console"] = new Column[0],
        };

        public static LogicKind KindOf(Part p) => LogicNet.KindOf(p);

        public static List<Column> ColumnsFor(Part p)
        {
            var result = new List<Column>();
            if (p == null || !s_columns.TryGetValue(p.Rules.ID.ToString(), out Column[] cols)) return result;
            foreach (Column c in cols)
            {
                if (p.Rules.UIToggleComponentsByID.ContainsKey(c.ToggleId)) result.Add(c);
            }
            return result;
        }

        public static string ColumnSignature(Part p)
        {
            var cols = ColumnsFor(p);
            var sb = new System.Text.StringBuilder(p.Rules.ID.ToString());
            foreach (Column c in cols) sb.Append('|').Append(c.ToggleId);
            return sb.ToString();
        }

        public static bool MembersDue(int framesSince, int every, bool force) => force || framesSince >= every;

        public static void CollectMembers(Ship home, List<PartSubnetwork<Part, PartComponent>> subs, List<Part> into)
        {
            into.Clear();
            var set = new HashSet<Part>();
            LogicNet.Members(subs, set);
            RemoteLinks.Expand(home, set);
            set.RemoveWhere(RemoteLinks.IsLink);
            var carts = new List<Part>();
            foreach (Part p in set) if (ZtxRailTerminalComponent.ForPart(p) != null) RailShipManager.AddCartsOf(p, carts);
            set.UnionWith(carts);
            into.AddRange(set);
            into.Sort(delegate (Part a, Part b)
            {
                int k = ((int)KindOf(a)).CompareTo((int)KindOf(b));
                if (k != 0) return k;
                int y = a.Location.Y.CompareTo(b.Location.Y);
                return y != 0 ? y : a.Location.X.CompareTo(b.Location.X);
            });
        }

        public static bool SameMembers(List<Part> a, List<Part> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!ReferenceEquals(a[i], b[i])) return false;
            return true;
        }

        public static List<(int Mode, string Text, string TipKey)> ChoicesFor(GameRoot game, Part p, ID<PartToggleGuiRules> toggleId)
        {
            var list = new List<(int, string, string)>();
            PartToggleGuiRules gui = null;
            foreach (PartToggleGuiRules g in game.Rules.GameGui.PartToggles)
            {
                if (g.ToggleID.Equals(toggleId)) { gui = g; break; }
            }
            if (gui == null)
            {
                Log.Info("console: no PartToggles entry for " + toggleId + " (cell omitted).");
                return list;
            }
            PartUIToggleRules tr = p.Rules.UIToggleComponentsByID[toggleId];
            foreach (PartToggleChoice choice in gui.Choices)
            {
                int mode = choice.ModeNum;
                if (tr.AvailableChoices != null && !tr.AvailableChoices.Contains(mode)) continue;
                if (game.Mode.IsToggleChoiceLocked(p.Rules, toggleId, mode, p.Ship)) continue;
                string tip = choice.ButtonToolTipKey != null ? choice.ButtonToolTipKey.ToString() : mode.ToString();
                list.Add((mode, ShortText(tip, mode), choice.ButtonToolTipKey?.Key));
            }
            return list;
        }

        private static string ShortText(string tip, int mode)
        {
            if (string.IsNullOrEmpty(tip)) return mode.ToString();
            string s = tip;
            int dash = s.IndexOf(" -- ", StringComparison.Ordinal);
            if (dash > 0) s = s.Substring(0, dash).Trim();
            int colon = s.IndexOf(':');
            if (colon >= 0 && colon < s.Length - 1) s = s.Substring(colon + 1).Trim();
            int stop = s.IndexOf(". ", StringComparison.Ordinal);
            if (stop > 0) s = s.Substring(0, stop).Trim();
            s = s.TrimEnd('.');
            if (s.Length > 28) s = s.Substring(0, 26) + "..";
            return s;
        }

        public static string PartLabel(Part p) => LogicNet.PartText(p, false);

        private static int Mode(Part p, ID<PartToggleGuiRules> id, int fallback) => LogicSource.Mode(p, id, fallback);

        private static string C(string hex, string text) => LogicPalette.Wrap(hex, text);

        private static string OnOff(bool on) => on
            ? C(LogicPalette.On, Strings.GetText("ZtxLogicPanel/SigOn"))
            : C(LogicPalette.Off, Strings.GetText("ZtxLogicPanel/SigOff"));

        public static string PartStatus(Part p)
        {
            LogicKind k = KindOf(p);
            if (k == LogicKind.Actuator)
            {
                bool? sig = LogicSource.IsSettingsDriven(p) ? LogicSource.Pushed(p) : LogicSource.Read(p);
                bool done = LogicActuatorState.IsDone(p, out string why);
                string cmd = C(LogicPalette.LineRef, CommandText(p, sig))
                    + C(done ? LogicPalette.On : LogicPalette.Warn, Strings.FormatText(done ? "ZtxLogicPanel/ActDoneFmt" : "ZtxLogicPanel/ActBusyFmt", why));
                if (sig == null) return Strings.FormatText("ZtxLogicPanel/CardManualFmt", cmd);
                int writers = LogicNet.SignalOf(p)?.Writers(ZtxSignalComponent.Clock(p.Ship)) ?? 0;
                return Strings.FormatText("ZtxLogicPanel/CardSignalFmt", OnOff(sig.Value), C(LogicPalette.LineRef, writers.ToString()), cmd);
            }
            if (k == LogicKind.Sensor || k == LogicKind.Button)
            {
                bool output = LogicNet.ReadOutput(p);
                string whyNot = LogicNet.WhyNot(p);
                if (!string.IsNullOrEmpty(whyNot)) whyNot = C(LogicPalette.Dim, Strings.FormatText("ZtxLogicPanel/SensorWhyNotFmt", whyNot));
                float? value = LogicNet.ReadValue(p);
                float? units = LogicNet.ReadUnits(p);
                if (value != null)
                    whyNot = C(LogicPalette.LineRef, units != null
                        ? Strings.FormatText("ZtxLogicPanel/SensorValueUnitsFmt", (int)value.Value, (int)units.Value)
                        : Strings.FormatText("ZtxLogicPanel/SensorValueFmt", (int)value.Value)) + whyNot;
                return Strings.FormatText("ZtxLogicPanel/CardSensorFmt", OnOff(output), whyNot);
            }
            return "";
        }

        private static readonly Dictionary<string, List<(int Mode, string Text, string TipKey)>> s_choiceCache =
            new Dictionary<string, List<(int, string, string)>>();

        public static string PartSettings(GameRoot game, Part p)
        {
            var parts = new List<string>();
            string watch = LogicPartWatch.Describe(p);
            if (watch.Length > 0) parts.Add(watch);
            foreach (Column col in ColumnsFor(p))
            {
                PartUIToggle t = PartUIToggle.GetOnPart(p, col.ToggleId);
                if (t == null) continue;
                if (col.ReadOnlyEngage)
                {
                    parts.Add(Strings.GetText(JointPower.IsOn(p) ? "ZtxLogicPanel/Engaged" : "ZtxLogicPanel/Dormant"));
                    continue;
                }
                string key = p.Rules.ID + "|" + col.ToggleId;
                if (!s_choiceCache.TryGetValue(key, out List<(int Mode, string Text, string TipKey)> choices))
                    s_choiceCache[key] = choices = ChoicesFor(game, p, col.ToggleId);
                int mode = t.Mode;
                foreach (var c in choices)
                {
                    if (c.Mode != mode) continue;
                    parts.Add(c.Text);
                    break;
                }
            }
            return string.Join("  ·  ", parts);
        }

        private static string CommandText(Part p, bool? sig)
        {
            string id = p.Rules.ID.ToString();
            if (id == "ztx.hinges_pistons_docking.piston_base" || id == "ztx.hinges_pistons_docking.corridor_piston_base")
            {
                ZtxPistonBaseComponent piston = GripService.PistonOf(p);
                return (piston != null ? HingeShipManager.CommandedCells(piston) : 0f).ToString("0.#") + " cells";
            }
            if (id == "ztx.hinges_pistons_docking.motor_base" || id == "ztx.hinges_pistons_docking.corridor_hinge_base")
            {
                return ArmToggles.MotorDegrees(p).ToString("0.#") + " deg";
            }
            if (id == "ztx.hinges_pistons_docking.hinge_base")
            {
                return TypedSettings.Format(TypedSettings.LeftLimit(p, Mode(p, HingeLeft.ToggleId, 9))) + "/"
                    + TypedSettings.Format(TypedSettings.RightLimit(p, Mode(p, HingeRight.ToggleId, 9))) + " deg limits";
            }
            foreach (Column c in ColumnsFor(p))
            {
                if (sig != null) return sig.Value ? "on" : "off";
                return c.ToggleId + " " + (LogicSource.IsOn(p, c.ToggleId) ? "on" : "off");
            }
            return "-";
        }
    }
}
