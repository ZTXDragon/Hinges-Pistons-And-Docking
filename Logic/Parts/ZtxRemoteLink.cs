using System;
using System.Collections.Generic;
using System.Text;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Networks;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxRemoteLink")]
    internal class ZtxRemoteLinkRules : PartComponentRules
    {
        public override PartComponent CreateComponent()
        {
            return new ZtxRemoteLinkComponent(this);
        }
    }

    internal class ZtxRemoteLinkComponent : PartComponent
    {
        public ZtxRemoteLinkComponent(ZtxRemoteLinkRules rules) : base(rules) { }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            try
            {
                LogicShipManager m = LogicShipManager.GetOrCreate(base.Ship);
                if (m != null && !m.Links.Contains(this)) m.Links.Add(this);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxRemoteLinkComponent.OnPartAttached", ex);
            }
        }

        public override void OnPartDetaching()
        {
            try
            {
                LogicShipManager.TryGet(base.Ship)?.Links.Remove(this);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxRemoteLinkComponent.OnPartDetaching", ex);
            }
            base.OnPartDetaching();
        }
    }

    internal static class RemoteLinks
    {
        public const string PART_ID = "ztx.hinges_pistons_docking.remote_link";
        public static readonly ID<PartToggleGuiRules> Name1Id = new ID<PartToggleGuiRules>("ztx_link_name_1");
        public static readonly ID<PartToggleGuiRules> Name2Id = new ID<PartToggleGuiRules>("ztx_link_name_2");
        public static readonly ID<PartToggleGuiRules> To1Id = new ID<PartToggleGuiRules>("ztx_link_to_1");
        public static readonly ID<PartToggleGuiRules> To2Id = new ID<PartToggleGuiRules>("ztx_link_to_2");

        private static readonly List<Ship> s_ships = new List<Ship>();
        private static readonly List<(int Order, Part Part)> s_ordered = new List<(int, Part)>();
        private static readonly List<Part> s_all = new List<Part>();
        private static readonly List<Part> s_queue = new List<Part>();
        private static readonly HashSet<Part> s_seen = new HashSet<Part>();
        private static readonly List<PartSubnetwork<Part, PartComponent>> s_subs = new List<PartSubnetwork<Part, PartComponent>>();
        private static readonly HashSet<Part> s_found = new HashSet<Part>();
        private static readonly Dictionary<Part, string> s_via = new Dictionary<Part, string>();
        private static readonly HashSet<Part> s_status = new HashSet<Part>();
        private static readonly List<PartSubnetwork<Part, PartComponent>> s_statusSubs = new List<PartSubnetwork<Part, PartComponent>>();

        public static void Reset()
        {
            s_ships.Clear(); s_ordered.Clear(); s_all.Clear(); s_queue.Clear(); s_seen.Clear();
            s_subs.Clear(); s_found.Clear(); s_via.Clear(); s_status.Clear(); s_statusSubs.Clear();
        }

        public static bool IsLink(Part p)
        {
            return p != null && p.Rules.ID.ToString() == PART_ID;
        }

        public static string StoredName(Part link)
        {
            if (link == null) return "";
            int n1 = LogicSource.Mode(link, Name1Id, 0);
            int n2 = LogicSource.Mode(link, Name2Id, 0);
            return (LogicRuleDef.UnpackName(n1) + LogicRuleDef.UnpackName(n2)).TrimEnd();
        }

        public static string DisplayName(Part link)
        {
            string n = StoredName(link);
            return n.Length > 0 ? n : "LINK " + link.Location.X + "," + link.Location.Y;
        }

        public static string Key(string name)
        {
            name = name ?? "";
            if (name.StartsWith("LINK ", StringComparison.OrdinalIgnoreCase)) name = "L" + name.Substring(5);
            return LogicRuleDef.Clean(name);
        }

        public static string TargetKey(Part link)
        {
            if (link == null) return "";
            int t1 = LogicSource.Mode(link, To1Id, 0);
            int t2 = LogicSource.Mode(link, To2Id, 0);
            return (LogicRuleDef.UnpackName(t1) + LogicRuleDef.UnpackName(t2)).TrimEnd();
        }

        public static void Candidates(Ship home, List<Part> into)
        {
            into.Clear();
            if (home == null) return;
            HingeJointManager.GetAssemblyShips(home, s_ships, identity: true);
            s_ships.Sort(delegate (Ship a, Ship b)
            {
                int ha = ReferenceEquals(a, home) ? 0 : 1, hb = ReferenceEquals(b, home) ? 0 : 1;
                return ha != hb ? ha.CompareTo(hb) : string.CompareOrdinal(ShipKey(a), ShipKey(b));
            });
            s_ordered.Clear();
            for (int i = 0; i < s_ships.Count; i++)
            {
                LogicShipManager m = LogicShipManager.TryGet(s_ships[i]);
                if (m == null) continue;
                foreach (ZtxRemoteLinkComponent l in m.Links)
                {
                    Part p = l.Part;
                    if (p == null || p.IsDestroyed || !ReferenceEquals(p.Ship, s_ships[i])) continue;
                    s_ordered.Add((i, p));
                }
            }
            s_ordered.Sort(delegate ((int Order, Part Part) a, (int Order, Part Part) b)
            {
                if (a.Order != b.Order) return a.Order.CompareTo(b.Order);
                int y = a.Part.Location.Y.CompareTo(b.Part.Location.Y);
                return y != 0 ? y : a.Part.Location.X.CompareTo(b.Part.Location.X);
            });
            foreach (var e in s_ordered) into.Add(e.Part);
        }

        public static string ShipKey(Ship s)
        {
            return s == null ? "" : s.UniqueID.ToString();
        }

        private static bool Linked(string myKey, string myTarget, Part other)
        {
            string otherKey = Key(DisplayName(other));
            if (myTarget.Length > 0 && myTarget == otherKey) return true;
            string otherTarget = TargetKey(other);
            return otherTarget.Length > 0 && otherTarget == myKey;
        }

        public static void Expand(Ship home, HashSet<Part> members)
        {
            s_via.Clear();
            s_seen.Clear();
            if (home == null || members == null || members.Count == 0) return;
            s_queue.Clear();
            foreach (Part p in members)
            {
                if (IsLink(p) && s_seen.Add(p)) s_queue.Add(p);
            }
            if (s_queue.Count == 0) return;
            Candidates(home, s_all);
            for (int i = 0; i < s_queue.Count; i++)
            {
                Part link = s_queue[i];
                string myKey = Key(DisplayName(link));
                string myTarget = TargetKey(link);
                foreach (Part other in s_all)
                {
                    if (ReferenceEquals(other, link) || s_seen.Contains(other)) continue;
                    if (!Linked(myKey, myTarget, other)) continue;
                    s_seen.Add(other);
                    s_queue.Add(other);
                    string via = DisplayName(other);
                    LogicNet.SubnetworksOf(other, s_subs);
                    LogicNet.Members(s_subs, s_found);
                    foreach (Part p in s_found)
                    {
                        if (IsLink(p))
                        {
                            members.Add(p);
                            if (s_seen.Add(p)) s_queue.Add(p);
                        }
                        else if (members.Add(p))
                        {
                            s_via[p] = via;
                        }
                    }
                }
            }
        }

        public static string ViaOf(Part p)
        {
            return p != null && s_via.TryGetValue(p, out string v) ? v : null;
        }

        public static string Status(Part link)
        {
            if (link == null || link.Ship == null) return "";
            LogicNet.SubnetworksOf(link, s_statusSubs);
            LogicNet.Members(s_statusSubs, s_status);
            s_status.Add(link);
            int before = CountParts(s_status);
            Expand(link.Ship, s_status);
            int after = CountParts(s_status);
            var names = new StringBuilder();
            foreach (Part p in s_seen)
            {
                if (ReferenceEquals(p, link)) continue;
                if (names.Length > 0) names.Append(", ");
                names.Append(DisplayName(p));
            }
            if (names.Length == 0)
            {
                string t = TargetKey(link);
                return t.Length == 0 ? Strings.GetText("ZtxLinkPanel/StatusUnlinked") : Strings.FormatText("ZtxLinkPanel/StatusMissingFmt", t);
            }
            return Strings.FormatText("ZtxLinkPanel/StatusLinkedFmt", names.ToString(), after - before);
        }

        private static int CountParts(HashSet<Part> set)
        {
            int n = 0;
            foreach (Part p in set) if (!IsLink(p)) n++;
            return n;
        }
    }
}
