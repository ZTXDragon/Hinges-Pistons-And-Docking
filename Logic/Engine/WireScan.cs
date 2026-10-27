using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Networks;
using Cosmoteer.Ships.Parts;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class WireScan
    {
        private readonly List<PartSubnetwork<Part, PartComponent>> _subs = new List<PartSubnetwork<Part, PartComponent>>();
        private readonly HashSet<Part> _local = new HashSet<Part>();
        private long _signature;
        private bool _valid;
        private bool _hasLinks;
        private long _linkPrint;
        private bool _linkValid;
        private static readonly List<Ship> s_ships = new List<Ship>();
        private static readonly List<PartSubnetwork<Part, PartComponent>> s_linkSubs = new List<PartSubnetwork<Part, PartComponent>>();

        public void Reset()
        {
            _valid = false;
            _local.Clear();
            _hasLinks = false;
            _linkValid = false;
        }

        private sealed class SubPrint
        {
            public int Tick = int.MinValue;
            public long Signature;
        }
        private static readonly ConditionalWeakTable<PartSubnetwork<Part, PartComponent>, SubPrint> s_prints =
            new ConditionalWeakTable<PartSubnetwork<Part, PartComponent>, SubPrint>();

        public bool Update(Part console, Ship ship, HashSet<Part> members, int tick, out long linkStamps)
        {
            linkStamps = 0;
            LogicNet.SubnetworksOf(console, _subs);
            long sig = Signature(_subs, tick);
            bool rescanned = !_valid || sig != _signature;
            if (rescanned)
            {
                _signature = sig;
                _valid = true;
                LogicNet.Members(_subs, _local);
                _hasLinks = false;
                foreach (Part p in _local) if (RemoteLinks.IsLink(p)) { _hasLinks = true; break; }
            }
            if (!_hasLinks)
            {
                if (rescanned)
                {
                    members.Clear();
                    members.UnionWith(_local);
                }
                return rescanned;
            }
            long t0 = LogicCost.Now;
            long lp = LinkPrint(ship, tick);
            if (rescanned || !_linkValid || lp != _linkPrint)
            {
                _linkPrint = lp;
                _linkValid = true;
                members.Clear();
                members.UnionWith(_local);
                RemoteLinks.Expand(ship, members);
            }
            linkStamps = LogicCost.Now - t0;
            return rescanned;
        }

        private static long LinkPrint(Ship home, int tick)
        {
            HingeJointManager.GetAssemblyShips(home, s_ships, identity: true);
            long h = s_ships.Count;
            foreach (Ship ship in s_ships)
            {
                LogicShipManager m = LogicShipManager.TryGet(ship);
                if (m == null) continue;
                foreach (ZtxRemoteLinkComponent l in m.Links)
                {
                    Part p = l.Part;
                    if (p == null || p.IsDestroyed || !ReferenceEquals(p.Ship, ship)) continue;
                    long e = RuntimeHelpers.GetHashCode(p);
                    e = e * 31 + RuntimeHelpers.GetHashCode(ship);
                    e = e * 31 + LogicSource.Mode(p, RemoteLinks.Name1Id, 0);
                    e = e * 31 + LogicSource.Mode(p, RemoteLinks.Name2Id, 0);
                    e = e * 31 + LogicSource.Mode(p, RemoteLinks.To1Id, 0);
                    e = e * 31 + LogicSource.Mode(p, RemoteLinks.To2Id, 0);
                    LogicNet.SubnetworksOf(p, s_linkSubs);
                    e = e * 31 + Signature(s_linkSubs, tick);
                    h += e * unchecked((long)0x9E3779B97F4A7C15UL);
                }
            }
            return h;
        }

        private static long Signature(List<PartSubnetwork<Part, PartComponent>> subs, int tick)
        {
            long h = 17;
            foreach (PartSubnetwork<Part, PartComponent> sub in subs)
            {
                SubPrint print = s_prints.GetOrCreateValue(sub);
                if (print.Tick != tick)
                {
                    print.Tick = tick;
                    print.Signature = SubSignature(sub);
                }
                h = h * 31 + print.Signature;
            }
            return h;
        }

        private static long SubSignature(PartSubnetwork<Part, PartComponent> sub)
        {
            long h = RuntimeHelpers.GetHashCode(sub);
            h = h * 31 + sub.MemberCount;
            IReadOnlyList<ComponentNode<Part, PartComponent>> nodes = sub.MemberNodes;
            for (int i = 0; i < nodes.Count; i++) h = h * 31 + RuntimeHelpers.GetHashCode(nodes[i]);
            return h;
        }
    }
}
