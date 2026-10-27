using System;
using System.Collections.Generic;
using System.Text;

namespace ZTX.HingesPistonsDocking
{
    internal struct ExprTerm
    {
        public int Line;
        public bool Or;
        public int Open;
        public int Close;
    }

    internal enum ExprError { None = 0, NoCondition = 1, CloseWithoutOpen = 2, NotClosed = 3 }

    internal static class LogicExpr
    {
        public const int AND = -1;
        public const int OR = -2;
        private const int OPEN = 0;

        private static int Prec(int op) => op == AND ? 2 : 1;

        public static ExprError Compile(List<ExprTerm> terms, List<int> program, out int unclosed)
        {
            program.Clear();
            unclosed = 0;
            if (terms.Count == 0) return ExprError.NoCondition;
            var ops = new List<int>();
            int depth = 0;
            for (int i = 0; i < terms.Count; i++)
            {
                ExprTerm t = terms[i];
                if (i > 0)
                {
                    int op = t.Or ? OR : AND;
                    while (ops.Count > 0 && ops[ops.Count - 1] != OPEN && Prec(ops[ops.Count - 1]) >= Prec(op))
                    {
                        program.Add(ops[ops.Count - 1]);
                        ops.RemoveAt(ops.Count - 1);
                    }
                    ops.Add(op);
                }
                for (int k = 0; k < t.Open; k++)
                {
                    ops.Add(OPEN);
                    depth++;
                }
                program.Add(t.Line);
                for (int k = 0; k < t.Close; k++)
                {
                    if (depth == 0)
                    {
                        program.Clear();
                        return ExprError.CloseWithoutOpen;
                    }
                    while (ops[ops.Count - 1] != OPEN)
                    {
                        program.Add(ops[ops.Count - 1]);
                        ops.RemoveAt(ops.Count - 1);
                    }
                    ops.RemoveAt(ops.Count - 1);
                    depth--;
                }
            }
            if (depth > 0)
            {
                program.Clear();
                unclosed = depth;
                return ExprError.NotClosed;
            }
            for (int i = ops.Count - 1; i >= 0; i--) program.Add(ops[i]);
            return ExprError.None;
        }

        public static bool Run(List<int> program, bool[] values, bool[] stack)
        {
            int sp = 0;
            for (int i = 0; i < program.Count; i++)
            {
                int x = program[i];
                if (x > 0)
                {
                    stack[sp++] = x < values.Length && values[x];
                    continue;
                }
                if (sp < 2) return false;
                bool b = stack[--sp];
                bool a = stack[--sp];
                stack[sp++] = x == AND ? a && b : a || b;
            }
            return sp == 1 && stack[0];
        }

        public static string Text(List<ExprTerm> terms, Func<int, string> termText, string and, string or)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < terms.Count; i++)
            {
                ExprTerm t = terms[i];
                if (i > 0) sb.Append(' ').Append(t.Or ? or : and).Append(' ');
                sb.Append('(', t.Open).Append(termText(t.Line)).Append(')', t.Close);
            }
            return sb.ToString();
        }

        public static int SelfTest(out string first)
        {
            var fails = new List<string>();
            var program = new List<int>();
            var stack = new bool[64];
            var v = new bool[8];
            var t = new List<ExprTerm> { T(1, false, 0, 0), T(2, true, 0, 0), T(3, false, 0, 0) };
            v[1] = true; v[2] = false; v[3] = false;
            Check(fails, Compile(t, program, out _) == ExprError.None && Run(program, v, stack), "AND before OR");
            t = new List<ExprTerm> { T(1, false, 1, 0), T(2, true, 0, 1), T(3, false, 0, 0) };
            Check(fails, Compile(t, program, out _) == ExprError.None && !Run(program, v, stack), "brackets first");
            t = new List<ExprTerm> { T(1, false, 0, 0), T(2, false, 0, 0), T(3, true, 0, 0), T(4, false, 0, 0) };
            v[1] = false; v[2] = true; v[3] = true; v[4] = true;
            Check(fails, Compile(t, program, out _) == ExprError.None && Run(program, v, stack), "two AND groups, second ON");
            v[4] = false;
            Check(fails, !Run(program, v, stack), "two AND groups, both OFF");
            t = new List<ExprTerm> { T(1, false, 2, 0), T(2, true, 0, 2), T(3, false, 0, 0) };
            v[1] = true; v[3] = true;
            Check(fails, Compile(t, program, out _) == ExprError.None && Run(program, v, stack), "nested brackets");
            t = new List<ExprTerm> { T(1, false, 1, 0), T(2, true, 0, 0) };
            Check(fails, Compile(t, program, out int open) == ExprError.NotClosed && open == 1 && program.Count == 0, "unclosed bracket");
            t = new List<ExprTerm> { T(1, false, 0, 1) };
            Check(fails, Compile(t, program, out _) == ExprError.CloseWithoutOpen, ") without (");
            Check(fails, Compile(new List<ExprTerm>(), program, out _) == ExprError.NoCondition && !Run(program, v, stack), "no condition");
            t = new List<ExprTerm> { T(2, true, 0, 0) };
            v[2] = true;
            Check(fails, Compile(t, program, out _) == ExprError.None && Run(program, v, stack), "single condition");
            Check(fails, Text(new List<ExprTerm> { T(1, false, 1, 0), T(2, true, 0, 1), T(3, false, 0, 0) }, k => "L" + k, "AND", "OR") == "(L1 OR L2) AND L3", "text");
            first = fails.Count > 0 ? fails[0] : "";
            return fails.Count;
        }

        private static ExprTerm T(int line, bool or, int open, int close) => new ExprTerm { Line = line, Or = or, Open = open, Close = close };

        private static void Check(List<string> fails, bool ok, string what)
        {
            if (!ok) fails.Add(what);
        }
    }
}
