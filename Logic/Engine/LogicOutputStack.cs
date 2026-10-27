using System.Collections.Generic;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogicOutputStack
    {
        public const int DO = 0, SEQ = 1;

        public static List<int> Order(IList<int> kinds, IList<int> ids, IList<int> pos)
        {
            var order = new List<int>(kinds.Count);
            for (int i = 0; i < kinds.Count; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                if (pos[a] != pos[b]) return pos[a].CompareTo(pos[b]);
                if (kinds[a] != kinds[b]) return kinds[a].CompareTo(kinds[b]);
                if (ids[a] != ids[b]) return ids[a].CompareTo(ids[b]);
                return a.CompareTo(b);
            });
            return order;
        }

        public static List<(int, int)> MoveWrites(IList<int> kinds, IList<int> ids, IList<int> pos, int item, int dir)
        {
            var writes = new List<(int, int)>();
            List<int> order = Order(kinds, ids, pos);
            int i = order.IndexOf(item), j = i + dir;
            if (i < 0 || j < 0 || j >= order.Count) return writes;
            (order[i], order[j]) = (order[j], order[i]);
            for (int k = 0; k < order.Count; k++)
                if (pos[order[k]] != k) writes.Add((order[k], k));
            return writes;
        }

        public static List<(int, int)> MoveBeforeWrites(IList<int> kinds, IList<int> ids, IList<int> pos, int item, int before)
        {
            var writes = new List<(int, int)>();
            List<int> order = Order(kinds, ids, pos);
            if (!order.Contains(item) || item == before) return writes;
            var moved = new List<int>(order);
            moved.Remove(item);
            int at = before < 0 ? moved.Count : moved.IndexOf(before);
            if (at < 0) return writes;
            moved.Insert(at, item);
            for (int k = 0; k < moved.Count; k++)
                if (pos[moved[k]] != k) writes.Add((moved[k], k));
            return writes;
        }

        public static int NextPos(IList<int> pos)
        {
            int next = 0;
            foreach (int p in pos) if (p + 1 > next) next = p + 1;
            return next;
        }
    }
}
