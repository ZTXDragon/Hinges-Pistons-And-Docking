using System;
using System.Collections.Generic;

namespace ZTX.HingesPistonsDocking
{
    internal static class PartSelection
    {
        public static List<T> Pick<T>(IEnumerable<T> selected, T current, Func<T, bool> sameKind, out T display) where T : class
        {
            var list = new List<T>();
            if (selected != null)
                foreach (T p in selected)
                    if (p != null && sameKind(p) && !list.Contains(p)) list.Add(p);
            if (list.Count == 0)
            {
                list.Add(current);
                display = current;
                return list;
            }
            display = list.Contains(current) ? current : list[0];
            return list;
        }
    }
}
