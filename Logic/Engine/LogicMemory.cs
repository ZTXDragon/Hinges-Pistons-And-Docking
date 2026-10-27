using System;
using System.Collections.Generic;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogicMemory
    {
        public const int LIMIT = 1000000;

        public static int Apply(SeqKind kind, bool poseSet, int pose, bool minus, bool counter, int current, int start)
        {
            if (kind == SeqKind.MemSet)
            {
                if (!poseSet) return start;
                return counter ? pose : (pose != 0 ? 1 : 0);
            }
            if (kind == SeqKind.MemAdd)
            {
                if (!counter) return current != 0 ? 0 : 1;
                long v = (long)current + (minus ? -pose : pose);
                return (int)Math.Max(-LIMIT, Math.Min(LIMIT, v));
            }
            return current;
        }
    }

    internal static class LogicCalls
    {
        public static bool CanCall(int caller, int target, Func<int, IEnumerable<int>> callsOf)
        {
            if (target == caller) return false;
            return !Reaches(target, caller, callsOf);
        }

        public static bool Reaches(int from, int to, Func<int, IEnumerable<int>> callsOf)
        {
            var seen = new HashSet<int>();
            var stack = new Stack<int>();
            stack.Push(from);
            while (stack.Count > 0)
            {
                int f = stack.Pop();
                if (f == to) return true;
                if (!seen.Add(f)) continue;
                foreach (int g in callsOf(f)) stack.Push(g);
            }
            return false;
        }
    }
}
