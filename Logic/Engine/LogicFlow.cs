using System.Collections.Generic;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogicFlow
    {
        public const int MAX_DEPTH = 4;
        private const int IF = (int)SeqKind.If, ELSE = (int)SeqKind.Else, END = (int)SeqKind.End;
        private const int REPEAT = (int)SeqKind.Repeat, WHILE = (int)SeqKind.While, TOGETHER = (int)SeqKind.Together;

        public static bool IsOpener(int kind) => kind == IF || kind == REPEAT || kind == WHILE || kind == TOGETHER;

        public static bool IsMarker(int kind) => IsOpener(kind) || kind == ELSE || kind == END;

        public static int[] Match(IList<int> kinds)
        {
            var partner = new int[kinds.Count];
            for (int i = 0; i < partner.Length; i++) partner[i] = -1;
            var stack = new List<int>();
            var elseOf = new Dictionary<int, int>();
            for (int i = 0; i < kinds.Count; i++)
            {
                int k = kinds[i];
                if (IsOpener(k)) stack.Add(i);
                else if (k == ELSE)
                {
                    if (stack.Count == 0) continue;
                    int top = stack[stack.Count - 1];
                    if (kinds[top] != IF || elseOf.ContainsKey(top)) continue;
                    elseOf[top] = i;
                    partner[top] = i;
                }
                else if (k == END)
                {
                    if (stack.Count == 0) continue;
                    int top = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    partner[i] = top;
                    if (elseOf.TryGetValue(top, out int e)) partner[e] = i;
                    else partner[top] = i;
                }
            }
            return partner;
        }

        public static int[] Depths(IList<int> kinds)
        {
            var depth = new int[kinds.Count];
            int d = 0;
            for (int i = 0; i < kinds.Count; i++)
            {
                int k = kinds[i];
                if (IsOpener(k)) { depth[i] = d; d++; }
                else if (k == ELSE) depth[i] = d > 0 ? d - 1 : 0;
                else if (k == END) { if (d > 0) d--; depth[i] = d; }
                else depth[i] = d;
            }
            return depth;
        }

        public static bool Valid(IList<int> kinds, int maxDepth)
        {
            var stack = new List<int>();
            var hasElse = new HashSet<int>();
            for (int i = 0; i < kinds.Count; i++)
            {
                int k = kinds[i];
                if (IsOpener(k))
                {
                    if (stack.Count + 1 > maxDepth) return false;
                    stack.Add(i);
                }
                else if (k == ELSE)
                {
                    if (stack.Count == 0) return false;
                    int top = stack[stack.Count - 1];
                    if (kinds[top] != IF || !hasElse.Add(top)) return false;
                }
                else if (k == END)
                {
                    if (stack.Count == 0) return false;
                    stack.RemoveAt(stack.Count - 1);
                }
            }
            return stack.Count == 0;
        }

        public static List<int> BlockSpan(IList<int> kinds, int index)
        {
            var span = new List<int> { index };
            if (index < 0 || index >= kinds.Count || !IsOpener(kinds[index])) return span;
            int[] partner = Match(kinds);
            int end = partner[index];
            if (end >= 0 && kinds[end] == ELSE) end = partner[end];
            if (end < 0) return span;
            span.Clear();
            for (int i = index; i <= end; i++) span.Add(i);
            return span;
        }

        public static List<int> Markers(IList<int> kinds, int index)
        {
            var marks = new List<int>();
            foreach (int i in BlockSpan(kinds, index))
                if (i == index || ((kinds[i] == ELSE || kinds[i] == END) && Owner(kinds, i) == index)) marks.Add(i);
            return marks;
        }

        private static int Owner(IList<int> kinds, int marker)
        {
            int[] partner = Match(kinds);
            int o = partner[marker];
            if (o >= 0 && kinds[o] == ELSE) return -1;
            if (kinds[marker] == ELSE)
            {
                for (int i = marker - 1; i >= 0; i--) if (partner[i] == marker) return i;
                return -1;
            }
            return o;
        }
    }
}
