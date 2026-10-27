using System;
using System.Collections.Generic;
using System.Text;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class AssemblyGraph
    {
        public const int KIND_ATTACHED = 0;
        public const int KIND_SAME_SHIP = 1;

        private readonly Dictionary<ulong, ulong> _anchorOf = new Dictionary<ulong, ulong>();
        private readonly Dictionary<ulong, List<ulong>> _members = new Dictionary<ulong, List<ulong>>();
        private readonly List<ulong> _anchors = new List<ulong>();
        private readonly Dictionary<ulong, int> _treeIndex = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, List<ulong>> _tops = new Dictionary<ulong, List<ulong>>();

        public static readonly AssemblyGraph Empty = new AssemblyGraph();

        public IReadOnlyList<ulong> Anchors => _anchors;

        public bool TryGetAnchor(ulong ship, out ulong anchor) => _anchorOf.TryGetValue(ship, out anchor);

        public IReadOnlyList<ulong> MembersOf(ulong anchor) => _members.TryGetValue(anchor, out List<ulong> m) ? m : (IReadOnlyList<ulong>)Array.Empty<ulong>();

        public static AssemblyGraph Build(IEnumerable<(ulong A, ulong B, int Kind)> edges) => Build(edges, false, null, null);

        public static AssemblyGraph BuildMarked(IEnumerable<(ulong A, ulong B, int Kind)> edges, ISet<ulong> marked) => Build(edges, false, marked, null);

        public int TreeIndex(ulong ship) => _treeIndex.TryGetValue(ship, out int i) ? i : -1;

        public IReadOnlyList<ulong> TopsOf(ulong anchor) => _tops.TryGetValue(anchor, out List<ulong> t) ? t : (IReadOnlyList<ulong>)Array.Empty<ulong>();

        public ulong PilotOfCluster(IEnumerable<ulong> cluster)
        {
            ulong best = ulong.MaxValue;
            int bestIndex = int.MaxValue;
            foreach (ulong m in cluster)
            {
                int i = TreeIndex(m);
                if (i < 0) i = int.MaxValue;
                if (i < bestIndex || (i == bestIndex && m < best))
                {
                    best = m;
                    bestIndex = i;
                }
            }
            return best;
        }

        public static AssemblyGraph BuildGroups(IEnumerable<(ulong A, ulong B, int Kind)> edges) => Build(edges, true, null, null);

        public static AssemblyGraph BuildGroupsMarked(IEnumerable<(ulong A, ulong B, int Kind)> edges, ISet<ulong> marked) => Build(edges, true, marked, null);

        public static AssemblyGraph BuildGroupsWeighted(IEnumerable<(ulong A, ulong B, int Kind)> edges, ISet<ulong> marked,
            IReadOnlyDictionary<ulong, (int Command, float Mass)> weights) => Build(edges, true, marked, weights);

        private static AssemblyGraph Build(IEnumerable<(ulong A, ulong B, int Kind)> edges, bool everyEdge, ISet<ulong> marked,
            IReadOnlyDictionary<ulong, (int Command, float Mass)> weights)
        {
            var g = new AssemblyGraph();
            var adj = new SortedDictionary<ulong, SortedSet<ulong>>();
            var isChild = new HashSet<ulong>();
            foreach ((ulong a, ulong b, int kind) in edges)
            {
                if ((!everyEdge && kind != KIND_SAME_SHIP) || a == b) continue;
                Link(adj, a, b);
                Link(adj, b, a);
                isChild.Add(b);
            }
            var seen = new HashSet<ulong>();
            foreach (ulong start in adj.Keys)
            {
                if (seen.Contains(start)) continue;
                var component = new List<ulong>();
                var queue = new Queue<ulong>();
                queue.Enqueue(start);
                seen.Add(start);
                while (queue.Count > 0)
                {
                    ulong cur = queue.Dequeue();
                    component.Add(cur);
                    foreach (ulong n in adj[cur])
                        if (seen.Add(n)) queue.Enqueue(n);
                }
                ulong anchor = ulong.MaxValue, lowest = ulong.MaxValue, markedTop = ulong.MaxValue, markedAny = ulong.MaxValue;
                var tops = new List<ulong>();
                foreach (ulong s in component)
                {
                    bool isMarked = marked != null && marked.Contains(s);
                    if (s < lowest) lowest = s;
                    if (isMarked && s < markedAny) markedAny = s;
                    if (!isChild.Contains(s))
                    {
                        tops.Add(s);
                        if (s < anchor) anchor = s;
                        if (isMarked && s < markedTop) markedTop = s;
                    }
                }
                if (markedTop != ulong.MaxValue) anchor = markedTop;
                else if (anchor == ulong.MaxValue) anchor = markedAny != ulong.MaxValue ? markedAny : lowest;
                if (weights != null) anchor = Heaviest(component, weights, marked, isChild);
                tops.Sort();
                var members = new List<ulong>();
                var inTree = new HashSet<ulong> { anchor };
                var q = new Queue<ulong>();
                q.Enqueue(anchor);
                while (q.Count > 0)
                {
                    ulong cur = q.Dequeue();
                    members.Add(cur);
                    foreach (ulong n in adj[cur])
                        if (inTree.Add(n)) q.Enqueue(n);
                }
                g._anchors.Add(anchor);
                g._members[anchor] = members;
                g._tops[anchor] = tops;
                for (int i = 0; i < members.Count; i++)
                {
                    g._anchorOf[members[i]] = anchor;
                    g._treeIndex[members[i]] = i;
                }
            }
            g._anchors.Sort();
            return g;
        }

        private static ulong Heaviest(List<ulong> component, IReadOnlyDictionary<ulong, (int Command, float Mass)> weights,
            ISet<ulong> marked, HashSet<ulong> isChild)
        {
            ulong best = ulong.MaxValue;
            (int Command, float Mass) bw = (int.MinValue, float.MinValue);
            foreach (ulong s in component)
            {
                weights.TryGetValue(s, out (int Command, float Mass) w);
                int c = w.Command.CompareTo(bw.Command);
                if (c == 0) c = w.Mass.CompareTo(bw.Mass);
                if (c == 0) c = Preferred(s, best, marked, isChild);
                if (best == ulong.MaxValue || c > 0)
                {
                    best = s;
                    bw = w;
                }
            }
            return best;
        }

        private static int Preferred(ulong s, ulong best, ISet<ulong> marked, HashSet<ulong> isChild)
        {
            bool sm = marked != null && marked.Contains(s), bm = marked != null && marked.Contains(best);
            if (sm != bm) return sm ? 1 : -1;
            bool st = !isChild.Contains(s), bt = !isChild.Contains(best);
            if (st != bt) return st ? 1 : -1;
            return best.CompareTo(s);
        }

        private static void Link(SortedDictionary<ulong, SortedSet<ulong>> adj, ulong from, ulong to)
        {
            if (!adj.TryGetValue(from, out SortedSet<ulong> set)) adj[from] = set = new SortedSet<ulong>();
            set.Add(to);
        }

        public string Describe()
        {
            var sb = new StringBuilder();
            foreach (ulong a in _anchors)
            {
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(a).Append(": ").Append(string.Join(",", _members[a]));
            }
            return sb.ToString();
        }

        public static T PickCommand<T>(T own, bool ownIsAttack, T holder, bool holderIsAttack) where T : class
        {
            if (holderIsAttack && holder != null) return holder;
            return own;
        }

        public const int ORDER_OTHER = 0;
        public const int ORDER_MOVE = 1;
        public const int ORDER_FOLLOW = 2;
        public const int ORDER_ATTACK = 3;

        public static bool RedirectsToAnchor(int orderKind, bool memberIsAnchor, bool anchorOrderedInSameInput)
        {
            return !memberIsAnchor && !anchorOrderedInSameInput
                && (orderKind == ORDER_MOVE || orderKind == ORDER_FOLLOW || orderKind == ORDER_ATTACK);
        }

        public static bool FollowsLead(bool leadIsAttack, int leadStamp, bool ownIsAttack, int ownStamp)
        {
            if (!leadIsAttack) return false;
            return !(ownIsAttack && ownStamp > leadStamp);
        }

        public static List<(ulong NewAnchor, ulong FromAnchor, bool Transfer)> Inheritors(IReadOnlyDictionary<ulong, ulong> prevAnchorOf, AssemblyGraph now)
        {
            var result = new List<(ulong, ulong, bool)>();
            if (prevAnchorOf == null || prevAnchorOf.Count == 0 || now == null) return result;
            foreach (ulong b in now.Anchors)
            {
                if (prevAnchorOf.TryGetValue(b, out ulong pb) && pb == b) continue;
                ulong from = ulong.MaxValue;
                foreach (ulong m in now.MembersOf(b))
                    if (prevAnchorOf.TryGetValue(m, out ulong pa)) { from = pa; break; }
                if (from == ulong.MaxValue || from == b) continue;
                bool transfer = now.TryGetAnchor(from, out ulong fromNow) && fromNow == b;
                result.Add((b, from, transfer));
            }
            var lone = new List<ulong>();
            foreach (KeyValuePair<ulong, ulong> kv in prevAnchorOf)
                if (kv.Value != kv.Key && !now.TryGetAnchor(kv.Key, out _)) lone.Add(kv.Key);
            lone.Sort();
            foreach (ulong id in lone) result.Add((id, prevAnchorOf[id], false));
            return result;
        }

        public static bool IsNewPlayerOrder(bool changed, bool nowEmpty, bool wasQueuedBefore, bool playerIssued)
            => changed && !nowEmpty && !wasQueuedBefore && playerIssued;
    }
}
