using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class StoreImage
    {
        public readonly Dictionary<int, int[]> Lines = new Dictionary<int, int[]>();
        public readonly Dictionary<int, int[]> Rules = new Dictionary<int, int[]>();
        public readonly Dictionary<int, int[]> Acts = new Dictionary<int, int[]>();
        public readonly Dictionary<int, int[]> Steps = new Dictionary<int, int[]>();
        public readonly Dictionary<int, int[]> Seqs = new Dictionary<int, int[]>();
        public int MaxLine, MaxRule, MaxAct, MaxStep, MaxSeq;
    }

    internal static class LogicStore
    {
        public const string PREFIX = "ztx_lg_";
        public static readonly ID<PartToggleGuiRules> VersionId = new ID<PartToggleGuiRules>(PREFIX + "v");
        public static readonly ID<PartToggleGuiRules> ProbeId = new ID<PartToggleGuiRules>(PREFIX + "probe");

        public const int LINE_R = 3;
        public const int ACT_R = 3;
        public const int RULE_C = 4;
        public const int ACT_O = 4;
        public const int ACT_S1 = 5, ACT_S2 = 6;
        public const int ACT_V = 7, STEP_V = 6;
        public const int LINE_FIELDS = 4, RULE_FIELDS = 5, ACT_FIELDS = 8;
        public const int STEP_FIELDS = 7, STEP_R = 4, STEP_S = 5;
        public const int SEQ_FIELDS = 2, SEQ_M = 0, SEQ_R = 1;

        private static readonly string[] s_lineF = { "a", "b", "m", "r" };
        private static readonly string[] s_ruleF = { "t", "m", "n1", "n2", "c" };
        private static readonly string[] s_actF = { "t", "m", "p", "r", "o", "s1", "s2", "v" };
        private static readonly string[] s_stepF = { "t", "m", "p", "q", "r", "s", "v" };
        private static readonly string[] s_seqF = { "m", "r" };

        public static string LineName(int k, int f) => PREFIX + "l" + k + "_" + s_lineF[f];
        public static string RuleName(int r, int f) => PREFIX + "r" + r + "_" + s_ruleF[f];
        public static string ActName(int k, int f) => PREFIX + "a" + k + "_" + s_actF[f];
        public static string StepName(int k, int f) => PREFIX + "s" + k + "_" + s_stepF[f];
        public static string SeqName(int k, int f) => PREFIX + "q" + k + "_" + s_seqF[f];

        public static bool Add(StoreImage img, string name, int value)
        {
            if (name == null || !name.StartsWith(PREFIX, StringComparison.Ordinal) || value == 0) return false;
            int i = PREFIX.Length;
            if (name.Length < i + 4) return false;
            char kind = name[i];
            int us = name.IndexOf('_', i + 1);
            if (us < 0) return false;
            if (!int.TryParse(name.Substring(i + 1, us - i - 1), out int id) || id < 1) return false;
            string field = name.Substring(us + 1);
            switch (kind)
            {
                case 'l': return Put(img.Lines, s_lineF, id, field, value, ref img.MaxLine);
                case 'r': return Put(img.Rules, s_ruleF, id, field, value, ref img.MaxRule);
                case 'a': return Put(img.Acts, s_actF, id, field, value, ref img.MaxAct);
                case 's': return Put(img.Steps, s_stepF, id, field, value, ref img.MaxStep);
                case 'q': return Put(img.Seqs, s_seqF, id, field, value, ref img.MaxSeq);
                default: return false;
            }
        }

        private static bool Put(Dictionary<int, int[]> into, string[] fields, int id, string field, int value, ref int max)
        {
            int f = Array.IndexOf(fields, field);
            if (f < 0) return false;
            if (!into.TryGetValue(id, out int[] rec)) into[id] = rec = new int[fields.Length];
            rec[f] = value;
            if (id > max) max = id;
            return true;
        }

        public static List<(string Name, int Value)> Convert(int lines, int rules, int acts,
            Func<int, int, int> lineRaw, Func<int, int> lineOwner,
            Func<int, int, int> ruleRaw,
            Func<int, int, int> actRaw, Func<int, int> actOwner,
            out int ruleCount, out int lineCount, out int actCount)
        {
            var w = new List<(string, int)>();
            ruleCount = lineCount = actCount = 0;
            int maxAct = 0;
            for (int k = 1; k <= acts; k++)
            {
                int t = actRaw(k, 0), m = actRaw(k, 1), p = actRaw(k, 2);
                if (m == 0) continue;
                AddNonZero(w, ActName(k, 0), t);
                AddNonZero(w, ActName(k, 1), m);
                AddNonZero(w, ActName(k, 2), p);
                AddNonZero(w, ActName(k, ACT_R), actOwner(k));
                actCount++;
                maxAct = k;
            }
            for (int k = 1; k <= lines; k++)
            {
                int a = lineRaw(k, 0), b = lineRaw(k, 1), m = lineRaw(k, 2);
                if (m == 0) continue;
                AddNonZero(w, LineName(k, 0), a);
                AddNonZero(w, LineName(k, 1), b);
                AddNonZero(w, LineName(k, 2), m);
                AddNonZero(w, LineName(k, LINE_R), lineOwner(k));
                lineCount++;
            }
            for (int r = 1; r <= rules; r++)
            {
                int t = ruleRaw(r, 0), m = ruleRaw(r, 1), n1 = ruleRaw(r, 2), n2 = ruleRaw(r, 3);
                if (m == 0) continue;
                LogicRuleDef def = LogicRuleDef.Unpack(t, m, n1, n2);
                if (def.Exists && def.Target.IsPart)
                {
                    int k = ++maxAct;
                    var act = new LogicAction { Rule = r, Target = def.Target, OnSet = def.PoseSet, OnPose = def.PoseSet ? def.Pose : 0, Speed = def.Speed };
                    AddNonZero(w, ActName(k, 0), act.Target.Pack());
                    AddNonZero(w, ActName(k, 1), act.PackMisc());
                    AddNonZero(w, ActName(k, 2), act.PackPose());
                    AddNonZero(w, ActName(k, ACT_R), r);
                    actCount++;
                    def.Target = LogicRef.None;
                    def.PoseSet = false;
                    def.Pose = 0;
                    def.Speed = 0;
                    t = 0;
                    m = def.PackMisc();
                }
                AddNonZero(w, RuleName(r, 0), t);
                AddNonZero(w, RuleName(r, 1), m);
                AddNonZero(w, RuleName(r, 2), n1);
                AddNonZero(w, RuleName(r, 3), n2);
                ruleCount++;
            }
            return w;
        }

        private static void AddNonZero(List<(string, int)> w, string name, int value)
        {
            if (value != 0) w.Add((name, value));
        }

        public static int SelfTest(out string first)
        {
            var fails = new List<string>();
            var lines = new int[4, 3];
            var owners = new int[4];
            var l1 = new LogicLine { Rule = 1, Op = LogicOp.Pass, Format = LogicLine.FORMAT, A = LogicRef.On, NotA = true };
            var l2 = new LogicLine { Rule = 1, Op = LogicOp.OnDelay, Format = LogicLine.FORMAT, A = LogicRef.Local(1), Value = 15, Or = true, Open = 1 };
            var l3 = new LogicLine { Rule = 1, Op = LogicOp.And, Format = LogicLine.FORMAT, A = LogicRef.Rule(2), B = LogicRef.Local(2), NotB = true, Close = 1 };
            LogicLine[] ls = { default, l1, l2, l3 };
            for (int k = 1; k <= 3; k++)
            {
                lines[k, 0] = ls[k].PackA();
                lines[k, 1] = ls[k].PackB();
                lines[k, 2] = ls[k].PackMisc();
                owners[k] = 1;
            }
            var rules = new int[3, 4];
            var r1 = new LogicRuleDef { Exists = true, Joined = true, Name = "DOOR" };
            rules[1, 1] = r1.PackMisc();
            rules[1, 2] = LogicRuleDef.PackName("DOOR", 0);
            var target = new LogicRef { Kind = RefKind.Part, X = 4, Y = -2 };
            var r2 = new LogicRuleDef { Exists = true, Target = target, PoseSet = true, Pose = 35, Speed = 7 };
            rules[2, 0] = target.Pack();
            rules[2, 1] = r2.PackMisc();
            var acts = new int[2, 3];
            var a1 = new LogicAction { Rule = 1, Target = new LogicRef { Kind = RefKind.Part, X = 9, Y = 9 }, Invert = true };
            acts[1, 0] = a1.Target.Pack();
            acts[1, 1] = a1.PackMisc();
            List<(string Name, int Value)> w = Convert(3, 2, 1,
                (k, f) => lines[k, f], k => owners[k], (r, f) => rules[r, f], (k, f) => acts[k, f], k => 1,
                out int rc, out int lc, out int ac);
            Check(fails, rc == 2 && lc == 3 && ac == 2, "conversion counts");
            var img = new StoreImage();
            foreach (var (name, value) in w) Check(fails, Add(img, name, value), "parse " + name);
            Check(fails, img.MaxLine == 3 && img.MaxRule == 2 && img.MaxAct == 2, "maxima");
            for (int k = 1; k <= 3; k++)
                Check(fails, img.Lines[k][0] == lines[k, 0] && img.Lines[k][1] == lines[k, 1] && img.Lines[k][2] == lines[k, 2] && img.Lines[k][LINE_R] == 1, "line " + k + " copied");
            LogicLine back3 = LogicLine.Unpack(img.Lines[3][0], img.Lines[3][1], img.Lines[3][2]);
            Check(fails, back3.A.IsRule && back3.A.Index == 2 && back3.B.IsLocal && back3.B.Index == 2 && back3.NotB && back3.Close == 1, "line 3 refs");
            LogicRuleDef back2 = LogicRuleDef.Unpack(img.Rules[2][0], img.Rules[2][1], img.Rules[2][2], img.Rules[2][3]);
            Check(fails, back2.Exists && back2.Target.IsNone && !back2.PoseSet && back2.Speed == 0, "old target left rule 2");
            LogicAction moved = LogicAction.Unpack(img.Acts[2][0], img.Acts[2][1], img.Acts[2][2]);
            Check(fails, img.Acts[2][ACT_R] == 2 && moved.Target.Equals(target) && moved.OnSet && moved.OnPose == 35 && moved.Speed == 7, "old target became actuator 2");
            LogicAction back1 = LogicAction.Unpack(img.Acts[1][0], img.Acts[1][1], img.Acts[1][2]);
            Check(fails, back1.Invert && img.Acts[1][ACT_R] == 1, "actuator 1 copied");
            Check(fails, (LogicRuleDef.UnpackName(img.Rules[1][2]) + LogicRuleDef.UnpackName(img.Rules[1].Length > 3 ? img.Rules[1][3] : 0)).TrimEnd() == "DOOR", "rule name");
            Check(fails, !w.Exists(e => e.Name.EndsWith("_c", StringComparison.Ordinal)), "old rules carry no colour");
            var ci = new StoreImage();
            Check(fails, Add(ci, RuleName(4, RULE_C), 5) && ci.Rules[4][RULE_C] == 5 && ci.Rules[4][0] == 0, "rule colour round trip");
            Check(fails, RuleName(4, RULE_C) == PREFIX + "r4_c", "rule colour name");
            Check(fails, LogicRef.Unpack(LogicRef.Local(1000).Pack()).Index == 1000 && LogicRef.Unpack(LogicRef.Rule(70000).Pack()).Index == 70000, "wide IDs");
            Check(fails, LogicLine.OwnerNibble(15) == 15 && LogicLine.OwnerNibble(16) != 0 && LogicLine.OwnerNibble(30) != 0 && LogicLine.OwnerNibble(8) == 8, "owner nibble");
            Check(fails, !Add(new StoreImage(), PREFIX + "v", 5) && !Add(new StoreImage(), PREFIX + "probe", 4242) && !Add(new StoreImage(), "ztx_line_1_a", 3), "not records");
            first = fails.Count > 0 ? fails[0] : "";
            return fails.Count;
        }

        private static void Check(List<string> fails, bool ok, string what)
        {
            if (!ok) fails.Add(what);
        }
    }
}
