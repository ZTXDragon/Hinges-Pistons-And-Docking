using System;
using System.Globalization;
using System.Text;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal enum LogicOp
    {
        None = 0,
        Pass = 1,
        And = 2,
        Or = 3,
        Not = 4,
        Xor = 5,
        AndNot = 6,
        Nand = 7,
        Nor = 8,
        OnDelay = 9,
        OffDelay = 10,
        Pulse = 11,
        Latch = 12,
        Flip = 13,
        Blink = 14,
        Above = 15,
        Below = 16,
        Count = 17,
        AtOn = 18,
        AtOff = 19,
        Moving = 20,
        Equal = 21,
        Merged = 22,
        AtStation = 23,
        Holding = 24,
        Docked = 25,
        Crewed = 26,
        Supplied = 27,
        Repaired = 28,
        Charged = 29,
        Built = 30,
    }

    internal static class LogicParams
    {
        public const int VALUE_MAX = 2047;
        public static readonly float[] Seconds = { 0f, 0.1f, 0.2f, 0.3f, 0.5f, 1f, 1.5f, 2f, 3f, 5f, 10f, 15f, 20f, 30f };
        public static readonly float[] Percents = { 0f, 5f, 10f, 20f, 25f, 30f, 40f, 50f, 60f, 70f, 75f, 80f, 90f, 95f, 100f };

        public static int FromFormat4(LogicOp op, int index)
        {
            if (LogicRuleEval.IsCompare(op)) return index >= 0 && index < Percents.Length ? (int)Percents[index] : 0;
            if (op == LogicOp.Count) return index < 0 ? 1 : index + 1;
            return index >= 0 && index < Seconds.Length ? (int)Math.Round(Seconds[index] * 10f) : 0;
        }

        public static int MinValue(LogicOp op) => op == LogicOp.Count ? 1 : 0;
        public static int MaxValue(LogicOp op, bool units = false) => LogicRuleEval.IsCompare(op) && !units ? 100 : VALUE_MAX;
        public static int Clamp(LogicOp op, int value, bool units = false) => Math.Max(MinValue(op), Math.Min(MaxValue(op, units), value));

        public static bool IsTime(LogicOp op) => !LogicRuleEval.IsCompare(op) && op != LogicOp.Count;

        public static string Unit(LogicOp op, bool units = false) => LogicRuleEval.IsCompare(op) ? (units ? "units" : "%") : op == LogicOp.Count ? "n" : "s";

        public static string EditText(LogicOp op, int value)
        {
            if (!IsTime(op)) return value.ToString(CultureInfo.InvariantCulture);
            float s = value / 10f;
            return s == (int)s ? ((int)s).ToString(CultureInfo.InvariantCulture) : s.ToString("0.#", CultureInfo.InvariantCulture);
        }

        public static string Text(LogicOp op, int value, bool units = false)
        {
            if (LogicRuleEval.IsCompare(op)) return value + (units ? " units" : " %");
            if (op == LogicOp.Count) return "n = " + value;
            return EditText(op, value) + " s";
        }

        public static bool TryParse(LogicOp op, string text, out int value, bool units = false)
        {
            value = MinValue(op);
            string t = (text ?? "").Trim().Replace(',', '.');
            if (t.Length == 0) return true;
            if (!float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return false;
            value = Clamp(op, (int)Math.Round(IsTime(op) ? f * 10f : f), units);
            return true;
        }
    }

    internal enum RefKind
    {
        None = 0,
        On = 1,
        Var = 2,
        Part = 3,
        Local = 4,
        Rule = 5,
        Cart = 6,
        Shared = 7,
    }

    internal struct LogicRef : IEquatable<LogicRef>
    {
        private const int COORD_BITS = 11;
        private const int COORD_MASK = (1 << COORD_BITS) - 1;
        private const int COORD_MIN = -(1 << (COORD_BITS - 1));
        private const int INDEX_MASK = 0x7FFFFFF;
        private const int COORD_MAX = (1 << (COORD_BITS - 1)) - 1;

        public RefKind Kind;
        public int Index;
        public int X;
        public int Y;

        public static readonly LogicRef None = default;
        public static readonly LogicRef On = new LogicRef { Kind = RefKind.On };

        public static LogicRef Rule(int r) => new LogicRef { Kind = RefKind.Rule, Index = r };
        public static LogicRef Local(int line) => new LogicRef { Kind = RefKind.Local, Index = line };
        public static LogicRef Cart(int id) => id >= 1 ? new LogicRef { Kind = RefKind.Cart, Index = id } : None;
        public static LogicRef Shared(int hash) => hash >= 1 ? new LogicRef { Kind = RefKind.Shared, Index = hash & INDEX_MASK } : None;
        public static LogicRef Part(Part p)
        {
            if (p == null) return None;
            if (ZtxRailCartComponent.ForPart(p) != null)
            {
                int id = RailShipManager.CartId(p);
                if (id >= 1) return Cart(id);
            }
            IntVector2 c = p.Location;
            if (MergeService.TryMapLiveCell(p.Ship, c, out IntVector2 home)) c = home;
            return new LogicRef { Kind = RefKind.Part, X = c.X, Y = c.Y };
        }

        public bool IsNone => Kind == RefKind.None;
        public bool IsRule => Kind == RefKind.Rule && Index >= 1;
        public bool IsPart => Kind == RefKind.Part || (Kind == RefKind.Cart && Index >= 1);
        public bool IsCart => Kind == RefKind.Cart && Index >= 1;
        public bool IsLocal => Kind == RefKind.Local && Index >= 1;
        public bool IsShared => Kind == RefKind.Shared && Index >= 1;

        public int Pack()
        {
            switch (Kind)
            {
                case RefKind.None: return 0;
                case RefKind.On: return 1;
                case RefKind.Rule: return 5 | ((Index & INDEX_MASK) << 3);
                case RefKind.Local: return 4 | ((Index & INDEX_MASK) << 3);
                case RefKind.Cart: return Index >= 1 ? 6 | ((Index & INDEX_MASK) << 3) : 0;
                case RefKind.Shared: return Index >= 1 ? 7 | ((Index & INDEX_MASK) << 3) : 0;
                default:
                    int x = Math.Max(COORD_MIN, Math.Min(COORD_MAX, X)) & COORD_MASK;
                    int y = Math.Max(COORD_MIN, Math.Min(COORD_MAX, Y)) & COORD_MASK;
                    return 3 | (x << 8) | (y << 19);
            }
        }

        public static LogicRef Unpack(int v)
        {
            var r = new LogicRef { Kind = (RefKind)(v & 7) };
            switch (r.Kind)
            {
                case RefKind.Rule:
                    r.Index = (v >> 3) & INDEX_MASK;
                    return r.Index >= 1 ? r : None;
                case RefKind.Local:
                    r.Index = (v >> 3) & INDEX_MASK;
                    return r.Index >= 1 ? r : None;
                case RefKind.Cart:
                    r.Index = (v >> 3) & INDEX_MASK;
                    return r.Index >= 1 ? r : None;
                case RefKind.Shared:
                    r.Index = (v >> 3) & INDEX_MASK;
                    return r.Index >= 1 ? r : None;
                case RefKind.Part:
                    r.X = Sign((v >> 8) & COORD_MASK);
                    r.Y = Sign((v >> 19) & COORD_MASK);
                    return r;
                case RefKind.On:
                    return On;
                default:
                    return None;
            }
        }

        private static int Sign(int u)
        {
            return u >= (1 << (COORD_BITS - 1)) ? u - (1 << COORD_BITS) : u;
        }

        public Part Resolve(Ship ship)
        {
            if (Kind != RefKind.Part || ship == null) return null;
            Part p = ship.Parts[new IntVector2(X, Y), PartRectType.Physical];
            if (p == null || p.IsDestroyed) return null;
            return p.Location.X == X && p.Location.Y == Y ? p : null;
        }

        public bool Equals(LogicRef o) => Kind == o.Kind && Index == o.Index && X == o.X && Y == o.Y;
        public override bool Equals(object obj) => obj is LogicRef o && Equals(o);
        public override int GetHashCode() => Pack();

        public override string ToString()
        {
            switch (Kind)
            {
                case RefKind.On: return "ON";
                case RefKind.Rule: return "R" + Index;
                case RefKind.Local: return "L" + Index;
                case RefKind.Cart: return "C" + Index;
                case RefKind.Shared: return "S" + Index;
                case RefKind.Part: return "(" + X + "," + Y + ")";
                default: return "-";
            }
        }
    }

    internal struct LogicLine
    {
        public const int FORMAT = 5;
        public const int FORMAT_PRESETS = 4;
        public const int OP_COUNT = 30;
        private const int VALUE_SHIFT = 13;
        private const int VALUE_MASK = 2047;
        private const int UNITS_BIT = 4;
        private const int CONN_BIT = 7;
        private const int CLOSE_SHIFT = 5;
        private const int OPEN_SHIFT = 28;
        public const int MAX_BRACKETS = 3;
        private const int NOT_BIT = 30;

        public LogicRef A;
        public LogicOp Op;
        public LogicRef B;
        public int Value;
        public int Rule;
        public int Format;
        public bool Units;
        public bool Or;
        public int Open;
        public int Close;
        public bool NotA;
        public bool NotB;

        public int PackA() => A.Pack() | (NotA ? 1 << NOT_BIT : 0);
        public int PackB() => B.Pack() | (NotB ? 1 << NOT_BIT : 0);

        public bool IsUsed => Format == FORMAT && Rule >= 1;

        public const int OWNER_MASK = 15 << 8;

        public static int OwnerNibble(int rule) => rule <= 0 ? 0 : rule <= 15 ? rule : (rule - 1) % 15 + 1;
        public bool IsActive => IsUsed && Op != LogicOp.None;
        public bool UsesB => LogicRuleEval.UsesB(Op);
        public bool UsesParam => LogicRuleEval.UsesParam(Op);
        public float ParamSeconds => Value / 10f;

        public int PackMisc()
        {
            if (Rule == 0) return 0;
            int units = Units && LogicRuleEval.IsCompare(Op) ? 1 << UNITS_BIT : 0;
            return ((int)Op & 15) | ((((int)Op >> 4) & 1) << 12) | units | (OwnerNibble(Rule) << 8) | ((Value & VALUE_MASK) << VALUE_SHIFT) | (FORMAT << 24)
                | (Or ? 1 << CONN_BIT : 0) | ((Close & 3) << CLOSE_SHIFT) | ((Open & 3) << OPEN_SHIFT);
        }

        public static LogicLine Unpack(int a, int b, int misc)
        {
            var l = new LogicLine();
            if (misc == 0) return l;
            l.Format = (misc >> 24) & 15;
            if (l.Format != FORMAT && l.Format != FORMAT_PRESETS) return l;
            l.Op = (LogicOp)((misc & 15) | (((misc >> 12) & 1) << 4));
            if ((int)l.Op > OP_COUNT) l.Op = LogicOp.None;
            l.Rule = (misc >> 8) & 15;
            if (l.Format == FORMAT_PRESETS)
            {
                l.Value = LogicParams.FromFormat4(l.Op, (misc >> 4) & 15);
                l.Format = FORMAT;
            }
            else
            {
                l.Value = (misc >> VALUE_SHIFT) & VALUE_MASK;
                l.Units = LogicRuleEval.IsCompare(l.Op) && ((misc >> UNITS_BIT) & 1) != 0;
                l.Or = ((misc >> CONN_BIT) & 1) != 0;
                l.Close = (misc >> CLOSE_SHIFT) & 3;
                l.Open = (misc >> OPEN_SHIFT) & 3;
            }
            l.Value = LogicParams.Clamp(l.Op, l.Value, l.Units);
            l.NotA = ((a >> NOT_BIT) & 1) != 0;
            l.NotB = ((b >> NOT_BIT) & 1) != 0;
            l.A = LogicRef.Unpack(a & ~(1 << NOT_BIT));
            l.B = LogicRef.Unpack(b & ~(1 << NOT_BIT));
            return l;
        }

        public override string ToString()
        {
            return (Or ? "OR " : "") + new string('(', Open) + Op + "(" + (NotA ? "NOT " : "") + A + ", " + (NotB ? "NOT " : "") + B + ", " + LogicParams.Text(Op, Value, Units) + ")" + new string(')', Close);
        }
    }

    internal struct LogicRuleDef
    {
        public const int FORMAT = 4;
        public const int NAME_CHARS = 10;
        public const int POSE_MAX = 4095;
        public const int SPEED_MAX = 255;
        public const int SHARED_BIT = 28;
        public const int HEADER_BIT = 29;
        public const int IN_GROUP_BIT = 30;
        public const int CONDITION_BIT = 31;
        private const string ALPHABET = " ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.";

        public bool Exists;
        public bool Disabled;
        public LogicRef Target;
        public string Name;
        public bool PoseSet;
        public int Pose;
        public int Speed;
        public bool Joined;
        public bool Shared;
        public bool IsHeader;
        public bool InGroup;
        public bool IsCondition;

        public int FunctionOf => IsCondition && Target.IsRule ? Target.Index : 0;

        public bool IsMemory => IsCondition && !Target.IsRule;

        public bool MemoryCounter => PoseSet;

        public int MemoryStart => Pose;

        public bool IsRunning => Exists && !Disabled && !IsHeader;

        public int PackMisc() => Exists
            ? 1 | (Disabled ? 2 : 0) | (PoseSet ? 4 : 0) | (Joined ? 8 : 0) | ((Pose & POSE_MAX) << 4) | ((Speed & SPEED_MAX) << 16) | (FORMAT << 24)
              | (Shared ? 1 << SHARED_BIT : 0) | (IsHeader ? 1 << HEADER_BIT : 0) | (InGroup ? 1 << IN_GROUP_BIT : 0) | (IsCondition ? 1 << CONDITION_BIT : 0)
            : 0;

        public static LogicRuleDef Unpack(int target, int misc, int n1, int n2)
        {
            var r = new LogicRuleDef { Name = "" };
            if (((misc >> 24) & 15) != FORMAT) return r;
            r.Exists = (misc & 1) != 0;
            r.Disabled = r.Exists && (misc & 2) != 0;
            r.PoseSet = r.Exists && (misc & 4) != 0;
            r.Joined = r.Exists && (misc & 8) != 0;
            r.Shared = r.Exists && (misc & (1 << SHARED_BIT)) != 0;
            r.IsHeader = r.Exists && (misc & (1 << HEADER_BIT)) != 0;
            r.InGroup = r.Exists && !r.IsHeader && (misc & (1 << IN_GROUP_BIT)) != 0;
            r.IsCondition = r.Exists && !r.IsHeader && (misc & (1 << CONDITION_BIT)) != 0;
            r.Pose = (misc >> 4) & POSE_MAX;
            r.Speed = (misc >> 16) & SPEED_MAX;
            r.Target = LogicRef.Unpack(target);
            r.Name = (UnpackName(n1) + UnpackName(n2)).TrimEnd();
            return r;
        }

        public static int PackName(string s, int offset)
        {
            int v = 0;
            for (int i = 0; i < 5; i++)
            {
                int pos = offset + i;
                char c = pos < s.Length ? char.ToUpperInvariant(s[pos]) : ' ';
                int code = ALPHABET.IndexOf(c);
                if (code < 0) code = ALPHABET.IndexOf('_');
                v |= (code & 63) << (6 * i);
            }
            return v;
        }

        public static string UnpackName(int v)
        {
            var sb = new StringBuilder(5);
            for (int i = 0; i < 5; i++)
            {
                int code = (v >> (6 * i)) & 63;
                sb.Append(code < ALPHABET.Length ? ALPHABET[code] : ' ');
            }
            return sb.ToString();
        }

        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (char ch in s)
            {
                char c = char.ToUpperInvariant(ch);
                sb.Append(ALPHABET.IndexOf(c) >= 0 ? c : '_');
                if (sb.Length >= NAME_CHARS) break;
            }
            return sb.ToString().TrimEnd();
        }
    }

    internal sealed class LogicRuleState
    {
        public float Elapsed;
        public bool PrevA;
        public bool Held;
        public bool Phase;
        public int Count;

        public void Reset()
        {
            Elapsed = 0f;
            PrevA = false;
            Held = false;
            Phase = false;
            Count = 0;
        }

        public const int COUNT_MAX = 2047, SECONDS_MAX = 255;

        public int Pack()
        {
            int count = Math.Max(0, Math.Min(COUNT_MAX, Count));
            int seconds = Math.Max(0, Math.Min(SECONDS_MAX, (int)Math.Floor(Elapsed)));
            return (PrevA ? 1 : 0) | (Held ? 2 : 0) | (Phase ? 4 : 0) | (count << 3) | (seconds << 14);
        }

        public static LogicRuleState Unpack(int v)
        {
            return new LogicRuleState
            {
                PrevA = (v & 1) != 0,
                Held = (v & 2) != 0,
                Phase = (v & 4) != 0,
                Count = (v >> 3) & COUNT_MAX,
                Elapsed = (v >> 14) & SECONDS_MAX,
            };
        }

        public static bool IsSaved(LogicOp op) => op == LogicOp.OnDelay || op == LogicOp.OffDelay || op == LogicOp.Pulse || op == LogicOp.Latch
            || op == LogicOp.Flip || op == LogicOp.Count;
    }

    internal static class LogicRuleEval
    {
        public static bool UsesB(LogicOp op)
        {
            switch (op)
            {
                case LogicOp.And:
                case LogicOp.Or:
                case LogicOp.Xor:
                case LogicOp.AndNot:
                case LogicOp.Nand:
                case LogicOp.Nor:
                case LogicOp.Latch:
                case LogicOp.Above:
                case LogicOp.Below:
                case LogicOp.Equal:
                case LogicOp.Count:
                    return true;
                default:
                    return false;
            }
        }

        public static bool UsesParam(LogicOp op)
        {
            switch (op)
            {
                case LogicOp.OnDelay:
                case LogicOp.OffDelay:
                case LogicOp.Pulse:
                case LogicOp.Blink:
                case LogicOp.Above:
                case LogicOp.Below:
                case LogicOp.Equal:
                case LogicOp.Count:
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsCompare(LogicOp op) => op == LogicOp.Above || op == LogicOp.Below || op == LogicOp.Equal;

        private const float EQUAL_TOLERANCE = 0.5f;

        private const float EQUAL_UNITS_TOLERANCE = 0.05f;

        public static bool Compare(LogicOp op, float a, float threshold) => Compare(op, a, threshold, units: false);

        public static bool Compare(LogicOp op, float a, float threshold, bool units)
        {
            switch (op)
            {
                case LogicOp.Above: return a > threshold;
                case LogicOp.Equal: return Math.Abs(a - threshold) < (units ? EQUAL_UNITS_TOLERANCE : EQUAL_TOLERANCE);
                default: return a < threshold;
            }
        }

        public static bool Hold(LogicOp op, float p, LogicRuleState st)
        {
            switch (op)
            {
                case LogicOp.OnDelay: return st.Elapsed >= p;
                case LogicOp.Count: return st.Count >= (int)p;
                case LogicOp.OffDelay:
                case LogicOp.Pulse:
                case LogicOp.Latch:
                case LogicOp.Flip: return st.Held;
                default: return false;
            }
        }

        public static bool Step(LogicOp op, float p, LogicRuleState st, bool a, bool b, float dt)
        {
            bool rising = a && !st.PrevA;
            bool result;
            switch (op)
            {
                case LogicOp.Pass: result = a; break;
                case LogicOp.And: result = a && b; break;
                case LogicOp.Or: result = a || b; break;
                case LogicOp.Not: result = !a; break;
                case LogicOp.Xor: result = a != b; break;
                case LogicOp.AndNot: result = a && !b; break;
                case LogicOp.Nand: result = !(a && b); break;
                case LogicOp.Nor: result = !(a || b); break;

                case LogicOp.OnDelay:
                    if (a)
                    {
                        st.Elapsed = Math.Min(st.Elapsed + dt, p + 1f);
                        result = st.Elapsed >= p;
                    }
                    else
                    {
                        st.Elapsed = 0f;
                        result = false;
                    }
                    break;

                case LogicOp.OffDelay:
                    if (a)
                    {
                        st.Elapsed = 0f;
                        st.Held = true;
                        result = true;
                    }
                    else if (st.Held)
                    {
                        st.Elapsed += dt;
                        if (st.Elapsed >= p) st.Held = false;
                        result = st.Held;
                    }
                    else
                    {
                        result = false;
                    }
                    break;

                case LogicOp.Pulse:
                    if (rising)
                    {
                        st.Elapsed = 0f;
                        st.Held = true;
                    }
                    if (st.Held)
                    {
                        result = true;
                        st.Elapsed += dt;
                        if (st.Elapsed >= p) st.Held = false;
                    }
                    else
                    {
                        result = false;
                    }
                    break;

                case LogicOp.Latch:
                    if (b) st.Held = false;
                    else if (a) st.Held = true;
                    result = st.Held;
                    break;

                case LogicOp.Flip:
                    if (rising) st.Held = !st.Held;
                    result = st.Held;
                    break;

                case LogicOp.Count:
                    if (b) st.Count = 0;
                    else if (rising) st.Count++;
                    result = st.Count >= (int)p;
                    break;

                case LogicOp.Blink:
                    if (!a)
                    {
                        st.Elapsed = 0f;
                        st.Phase = false;
                        result = false;
                    }
                    else
                    {
                        float half = (p <= 0f ? 0.2f : p) / 2f;
                        if (rising)
                        {
                            st.Phase = true;
                            st.Elapsed = 0f;
                        }
                        else
                        {
                            st.Elapsed += dt;
                            if (st.Elapsed >= half)
                            {
                                st.Elapsed -= half;
                                st.Phase = !st.Phase;
                            }
                        }
                        result = st.Phase;
                    }
                    break;

                default:
                    result = false;
                    break;
            }
            st.PrevA = a;
            return result;
        }
    }
}
