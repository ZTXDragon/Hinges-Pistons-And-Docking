using System.Collections.Generic;
using Cosmoteer.Ships;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogicShared
    {
        public const int HASH_MASK = 0x7FFFFFF;

        private static readonly Dictionary<int, string> s_known = new Dictionary<int, string>();
        private static readonly List<Ship> s_assembly = new List<Ship>();

        public static int Hash(string name)
        {
            string clean = LogicRuleDef.Clean(name ?? "");
            int v = (int)(MergeRecord.Hash(clean) & HASH_MASK);
            return v == 0 ? 1 : v;
        }

        internal sealed class Entry
        {
            public string Name;
            public bool On;
            public int Count;
        }

        internal sealed class Table
        {
            private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();

            public void Publish(int hash, string name, bool value)
            {
                if (!_entries.TryGetValue(hash, out Entry e))
                {
                    e = new Entry { Name = name ?? "" };
                    _entries[hash] = e;
                }
                e.On |= value;
                e.Count++;
            }

            public bool? Read(int hash) => _entries.TryGetValue(hash, out Entry e) ? e.On : (bool?)null;

            public int Publishers(int hash) => _entries.TryGetValue(hash, out Entry e) ? e.Count : 0;

            public void Clear() => _entries.Clear();

            public IEnumerable<KeyValuePair<int, Entry>> Entries => _entries;
        }

        public static void Publish(LogicShipManager manager, int hash, string name, bool value)
        {
            if (manager == null || hash == 0) return;
            manager.SharedNow.Publish(hash, name, value);
            if (!string.IsNullOrEmpty(name)) s_known[hash] = name;
        }

        public static bool? Read(Ship ship, int hash)
        {
            bool? result = null;
            foreach (LogicShipManager m in Managers(ship))
            {
                bool? v = m.SharedLast.Read(hash);
                if (v.HasValue) result = (result ?? false) | v.Value;
            }
            return result;
        }

        public static int Publishers(Ship ship, int hash)
        {
            int n = 0;
            foreach (LogicShipManager m in Managers(ship)) n += m.SharedLast.Publishers(hash);
            return n;
        }

        public static List<(int Hash, string Name)> Names(Ship ship)
        {
            var seen = new Dictionary<int, string>();
            foreach (LogicShipManager m in Managers(ship))
                foreach (KeyValuePair<int, Entry> kv in m.SharedLast.Entries)
                    if (!seen.ContainsKey(kv.Key)) seen[kv.Key] = kv.Value.Name;
            var list = new List<(int, string)>();
            foreach (KeyValuePair<int, string> kv in seen) list.Add((kv.Key, kv.Value));
            list.Sort((a, b) => string.CompareOrdinal(a.Item2, b.Item2));
            return list;
        }

        public static string NameOf(int hash) => s_known.TryGetValue(hash, out string n) ? n : null;

        private static List<LogicShipManager> Managers(Ship ship)
        {
            var list = new List<LogicShipManager>();
            if (ship == null) return list;
            HingeJointManager.GetAssemblyShips(ship, s_assembly, identity: true);
            foreach (Ship s in s_assembly)
            {
                LogicShipManager m = LogicShipManager.TryGet(s);
                if (m != null) list.Add(m);
            }
            return list;
        }
    }
}
