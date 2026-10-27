using System.Collections.Generic;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogicRuleOrder
    {
        public static Dictionary<int, int> Remap(List<int> current, List<int> desired)
        {
            var slots = new List<int>(current);
            slots.Sort();
            var map = new Dictionary<int, int>();
            for (int i = 0; i < desired.Count && i < slots.Count; i++) map[desired[i]] = slots[i];
            return map;
        }

        public static List<int> MovedTo(List<int> order, int item, int index)
        {
            var desired = new List<int>(order);
            desired.Remove(item);
            if (index < 0) index = 0;
            if (index > desired.Count) index = desired.Count;
            desired.Insert(index, item);
            return desired;
        }
    }
}
