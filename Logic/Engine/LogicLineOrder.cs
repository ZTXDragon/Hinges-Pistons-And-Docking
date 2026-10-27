using System;
using System.Collections.Generic;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogicLineOrder
    {
        public static Dictionary<int, int> PlanMove(List<int> targetSlots, int line, int beforeSlot)
        {
            var desired = new List<int>(targetSlots);
            desired.Remove(line);
            int at = beforeSlot == 0 ? desired.Count : desired.IndexOf(beforeSlot);
            if (at < 0) at = desired.Count;
            desired.Insert(at, line);
            var current = new List<int>(desired);
            current.Sort();
            return LogicRuleOrder.Remap(current, desired);
        }

        public static List<int> LinksOf(int line, List<int> slots, Func<int, int> localA, Func<int, int> localB, List<int> stepTargets)
        {
            var links = new List<int>();
            foreach (int s in slots)
            {
                if (s == line) continue;
                if (localA(s) == line || localB(s) == line) links.Add(s);
            }
            foreach (int target in new[] { localA(line), localB(line) })
                if (target != 0 && !links.Contains(target)) links.Add(target);
            if (stepTargets != null && stepTargets.Contains(line)) links.Add(0);
            return links;
        }
    }
}
