using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Networks;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Logic;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal enum LogicKind { None = 0, Sensor = 1, Button = 2, Actuator = 3, Console = 4, Link = 5 }

    internal static class LogicNet
    {
        private static readonly ID<PartComponentRules> s_logicNodeId = new ID<PartComponentRules>("LogicNode");
        private static readonly ID<PartComponentRules>[] s_sideNodeIds =
        {
            new ID<PartComponentRules>("NodeU"), new ID<PartComponentRules>("NodeR"),
            new ID<PartComponentRules>("NodeD"), new ID<PartComponentRules>("NodeL"),
        };
        private static readonly ID<PartComponentRules> s_logicOutId = new ID<PartComponentRules>("LogicOut");
        private static readonly ID<PartComponentRules> s_signalId = new ID<PartComponentRules>("Signal");
        private static readonly ID<PartToggleGuiRules> s_buttonId = new ID<PartToggleGuiRules>("ztx_logic_button");

        private static readonly Dictionary<string, LogicKind> s_kinds = new Dictionary<string, LogicKind>
        {
            ["ztx.hinges_pistons_docking.weapon_sensor"] = LogicKind.Sensor,
            ["ztx.hinges_pistons_docking.damage_sensor"] = LogicKind.Sensor,
            ["ztx.hinges_pistons_docking.proximity_sensor"] = LogicKind.Sensor,
            ["ztx.hinges_pistons_docking.ship_sensor"] = LogicKind.Sensor,
            ["ztx.hinges_pistons_docking.logic_button"] = LogicKind.Button,
            ["ztx.hinges_pistons_docking.piston_base"] = LogicKind.Actuator,
            ["ztx.hinges_pistons_docking.corridor_piston_base"] = LogicKind.Actuator,
            ["ztx.hinges_pistons_docking.rail_terminal"] = LogicKind.Actuator,
            ["ztx.hinges_pistons_docking.rail_switch"] = LogicKind.Actuator,
            ["ztx.hinges_pistons_docking.rail_cart"] = LogicKind.Actuator,
            ["ztx.hinges_pistons_docking.motor_base"] = LogicKind.Actuator,
            ["ztx.hinges_pistons_docking.hinge_base"] = LogicKind.Actuator,
            ["ztx.hinges_pistons_docking.corridor_hinge_base"] = LogicKind.Actuator,
            ["ztx.hinges_pistons_docking.dock_clamp"] = LogicKind.Actuator,
            ["ztx.hinges_pistons_docking.dock_pad"] = LogicKind.Actuator,
            ["ztx.hinges_pistons_docking.bay_terminal"] = LogicKind.Actuator,
            ["ztx.hinges_pistons_docking.bay_station"] = LogicKind.Actuator,
            ["ztx.hinges_pistons_docking.logic_adapter"] = LogicKind.Actuator,
            ["ztx.hinges_pistons_docking.indicator_lamp"] = LogicKind.Actuator,
            ["ztx.hinges_pistons_docking.logic_console"] = LogicKind.Console,
            [RemoteLinks.PART_ID] = LogicKind.Link,
        };

        public static LogicKind KindOf(Part p) => p == null ? LogicKind.None : KindOfId(p.Rules.ID.ToString());

        public static LogicKind KindOfId(string id) => id != null && s_kinds.TryGetValue(id, out LogicKind k) ? k : LogicKind.None;

        public static string Hex(LogicKind k)
        {
            switch (k)
            {
                case LogicKind.Button: return LogicPalette.KindButton;
                case LogicKind.Sensor: return LogicPalette.KindSensor;
                case LogicKind.Actuator: return LogicPalette.KindActuator;
                case LogicKind.Console: return LogicPalette.KindConsole;
                case LogicKind.Link: return LogicPalette.KindLink;
                default: return LogicPalette.Neutral;
            }
        }

        public static void SubnetworksOf(Part part, List<PartSubnetwork<Part, PartComponent>> into)
        {
            into.Clear();
            if (part == null) return;
            if (part.TryGetComponent<PartNetworkBasicNode>(s_logicNodeId, out PartNetworkBasicNode node) && node.NetworkNode?.Subnetwork != null)
                into.Add(node.NetworkNode.Subnetwork);
            foreach (ID<PartComponentRules> id in s_sideNodeIds)
            {
                if (part.TryGetComponent<PartNetworkBasicNode>(id, out PartNetworkBasicNode side) && side.NetworkNode?.Subnetwork != null
                    && !into.Contains(side.NetworkNode.Subnetwork))
                    into.Add(side.NetworkNode.Subnetwork);
            }
        }

        public static void Members(List<PartSubnetwork<Part, PartComponent>> subs, HashSet<Part> into)
        {
            into.Clear();
            foreach (PartSubnetwork<Part, PartComponent> sub in subs)
            {
                foreach (ComponentNode<Part, PartComponent> node in sub.MemberNodes)
                {
                    Part p = node.ParentNode?.Part;
                    if (p == null || p.IsDestroyed) continue;
                    if (KindOf(p) == LogicKind.None) continue;
                    into.Add(p);
                }
            }
        }

        public static bool ReadOutput(Part p)
        {
            if (p == null || p.IsDestroyed) return false;
            switch (KindOf(p))
            {
                case LogicKind.Button:
                    return LogicSource.IsOn(p, s_buttonId);
                case LogicKind.Sensor:
                    return p.TryGetComponent<IPartComponentToggle>(s_logicOutId, out IPartComponentToggle t) && t.IsToggleOn;
                case LogicKind.Actuator:
                    if (ReadsLogicOut(p)) return p.TryGetComponent<IPartComponentToggle>(s_logicOutId, out IPartComponentToggle lo) && lo.IsToggleOn;
                    if (LogicAction.KindOf(p) == LogicAction.KIND_BAY_STATION) return BayManager.StationWorking(p);
                    return LogicActuatorState.IsDone(p);
                default:
                    return false;
            }
        }

        public static bool ReadsLogicOut(Part p)
        {
            int kind = LogicAction.KindOf(p);
            return kind == LogicAction.KIND_ADAPTER || kind == LogicAction.KIND_LAMP;
        }

        public static float? ReadValue(Part p)
        {
            if (p == null || p.IsDestroyed) return null;
            if (LogicAction.IsPositioned(p)) return LogicActuatorState.Position(p, out float pct, out _) ? pct : float.NaN;
            return p.TryGetComponent<ILogicValue>(s_logicOutId, out ILogicValue v) ? v.Percent : null;
        }

        public static bool IsValueSource(Part p)
        {
            return p != null && !p.IsDestroyed && (p.TryGetComponent<ILogicValue>(s_logicOutId, out ILogicValue _) || LogicAction.IsPositioned(p));
        }

        public static bool ReportsUnits(Part p) => IsValueSource(p);

        public static float? ReadUnits(Part p)
        {
            if (p == null || p.IsDestroyed) return null;
            if (LogicAction.IsPositioned(p)) return LogicActuatorState.Position(p, out _, out float u) ? u : (float?)null;
            return p.TryGetComponent<ILogicValue>(s_logicOutId, out ILogicValue v) ? v.Units : null;
        }

        public static string WhyNot(Part p)
        {
            if (p != null && p.TryGetComponent<ZtxLogicTickerComponent>(s_logicOutId, out ZtxLogicTickerComponent t)) return t.LastWhyNot;
            return "";
        }

        public static int FirstByCell(int[] xs, int[] ys)
        {
            int best = -1;
            for (int i = 0; i < (xs?.Length ?? 0); i++)
                if (best < 0 || ys[i] < ys[best] || (ys[i] == ys[best] && xs[i] < xs[best])) best = i;
            return best;
        }

        public static Part Resolve(Ship home, HashSet<Part> members, LogicRef r)
        {
            if (!r.IsPart) return null;
            if (r.IsCart)
            {
                if (members == null) return null;
                var hits = new List<Part>();
                foreach (Part p in members)
                    if (ZtxRailCartComponent.ForPart(p) != null && RailShipManager.CartId(p) == r.Index) hits.Add(p);
                if (hits.Count <= 1) return hits.Count == 1 ? hits[0] : null;
                var xs = new int[hits.Count];
                var ys = new int[hits.Count];
                for (int i = 0; i < hits.Count; i++) { xs[i] = hits[i].Location.X; ys[i] = hits[i].Location.Y; }
                return hits[FirstByCell(xs, ys)];
            }
            if (MergeService.TryMapHomeCell(home, new IntVector2(r.X, r.Y), out IntVector2 live))
            {
                Part moved = home.Parts[live, PartRectType.Normal];
                if (moved != null && (members == null || members.Contains(moved))) return moved;
            }
            if (RailShipManager.TryMapCartHomeCell(home, new IntVector2(r.X, r.Y), out IntVector2 cartLive))
            {
                Part onCart = home.Parts[cartLive, PartRectType.Normal];
                if (onCart != null && (members == null || members.Contains(onCart))) return onCart;
            }
            Part local = r.Resolve(home);
            if (local != null && (members == null || members.Contains(local))) return local;
            Part best = null;
            if (members != null)
            {
                foreach (Part p in members)
                {
                    if (p.Location.X != r.X || p.Location.Y != r.Y || ReferenceEquals(p.Ship, home)) continue;
                    if (best == null || string.CompareOrdinal(RemoteLinks.ShipKey(p.Ship), RemoteLinks.ShipKey(best.Ship)) < 0) best = p;
                }
            }
            return best ?? local;
        }

        public static ZtxSignalComponent SignalOf(Part p)
        {
            if (p == null || p.IsDestroyed) return null;
            return p.TryGetComponent<ZtxSignalComponent>(s_signalId, out ZtxSignalComponent s) ? s : null;
        }

        public static string PartText(Part p, bool xml)
        {
            if (p == null) return "?";
            string t = p.Rules.NameKey.ToString() + " (" + p.Location.X + "," + p.Location.Y + ")";
            if (ZtxRailCartComponent.ForPart(p) != null)
            {
                Part tp = RailShipManager.TerminalFor(p)?.Part;
                string typed = RailShipManager.CartName(p);
                t = Strings.FormatText("ZtxLogicPanel/CartTextFmt", typed.Length > 0 ? typed : RailShipManager.CartId(p).ToString(),
                    tp != null ? tp.Location.X + "," + tp.Location.Y : "?");
            }
            string via = RemoteLinks.ViaOf(p);
            if (via != null) t += Strings.FormatText("ZtxLogicPanel/ViaFmt", via);
            return xml ? "<color hex=\"" + Hex(KindOf(p)) + "\">" + t + "</color>" : t;
        }
    }
}
