using System;
using System.Collections.Generic;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogicGroups
    {
        public const int EDGE_TOP = 0, EDGE_BOTTOM = 1, EDGE_HEADER = 2;

        public static Dictionary<int, int> HeaderOf(List<int> order, Func<int, bool> isHeader, Func<int, bool> inGroup)
        {
            var map = new Dictionary<int, int>();
            int header = 0;
            foreach (int r in order)
            {
                if (isHeader(r)) header = r;
                else if (!inGroup(r)) header = 0;
                map[r] = header;
            }
            return map;
        }

        public static bool Paused(int rule, bool ownDisabled, Dictionary<int, int> headerOf, Func<int, bool> headerDisabled)
        {
            if (ownDisabled) return true;
            if (!headerOf.TryGetValue(rule, out int header) || header == 0 || header == rule) return false;
            return headerDisabled(header);
        }

        public static List<int> Block(List<int> order, int item, Func<int, bool> isHeader, Func<int, bool> inGroup)
        {
            var block = new List<int>();
            if (!order.Contains(item)) return block;
            block.Add(item);
            if (!isHeader(item)) return block;
            foreach (KeyValuePair<int, int> kv in HeaderOf(order, isHeader, inGroup))
                if (kv.Value == item && kv.Key != item) block.Add(kv.Key);
            block.Sort((a, b) => order.IndexOf(a).CompareTo(order.IndexOf(b)));
            return block;
        }

        public static int Members(List<int> order, int header, Func<int, bool> isHeader, Func<int, bool> inGroup) =>
            Block(order, header, isHeader, inGroup).Count - 1;

        public static List<int> MovedBefore(List<int> order, List<int> items, int before)
        {
            if (items.Count == 0 || items.Contains(before)) return new List<int>(order);
            var rest = new List<int>();
            foreach (int r in order) if (!items.Contains(r)) rest.Add(r);
            int at = before == 0 ? rest.Count : rest.IndexOf(before);
            if (at < 0) return new List<int>(order);
            var moved = new List<int>();
            foreach (int r in order) if (items.Contains(r)) moved.Add(r);
            rest.InsertRange(at, moved);
            return rest;
        }

        public static List<(int Before, int Group, int Anchor, int Edge)> Gaps(List<int> order, Func<int, bool> isHeader, Func<int, bool> inGroup,
            Func<int, bool> folded, bool groupDrag)
        {
            Dictionary<int, int> map = HeaderOf(order, isHeader, inGroup);
            var gaps = new List<(int, int, int, int)>();
            int last = 0;
            int i = 0;
            while (i < order.Count)
            {
                int r = order[i];
                gaps.Add((r, 0, r, EDGE_TOP));
                last = r;
                if (!isHeader(r)) { i++; continue; }
                int j = i + 1;
                var members = new List<int>();
                while (j < order.Count && map[order[j]] == r) members.Add(order[j++]);
                int next = j < order.Count ? order[j] : 0;
                if (!groupDrag)
                {
                    if (folded(r) || members.Count == 0) gaps.Add((next, r, r, EDGE_HEADER));
                    else
                    {
                        gaps.Add((members[0], r, r, EDGE_HEADER));
                        for (int k = 1; k < members.Count; k++) gaps.Add((members[k], r, members[k], EDGE_TOP));
                        gaps.Add((next, r, members[members.Count - 1], EDGE_BOTTOM));
                    }
                }
                i = j;
            }
            if (order.Count > 0) gaps.Add((0, 0, last, EDGE_BOTTOM));
            return gaps;
        }
    }
}
