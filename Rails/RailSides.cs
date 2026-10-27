using System;
using System.Collections.Generic;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class RailSides
    {
        public enum Kind { Straight, Corner, Crossing }

        public static readonly IntVector2[] Dirs =
        {
            new IntVector2(0, -1), new IntVector2(1, 0), new IntVector2(0, 1), new IntVector2(-1, 0),
        };

        private const int CORNER_WALK = 8;

        public static int Dot(IntVector2 a, IntVector2 b)
        {
            return a.X * b.X + a.Y * b.Y;
        }

        public static IntVector2 Neg(IntVector2 a)
        {
            return new IntVector2(-a.X, -a.Y);
        }

        public static Kind KindOf(IReadOnlyList<IntVector2> neighbourDirs)
        {
            if (neighbourDirs.Count >= 3) return Kind.Crossing;
            if (neighbourDirs.Count == 2 && Dot(neighbourDirs[0], neighbourDirs[1]) == 0) return Kind.Corner;
            return Kind.Straight;
        }

        public static bool LegAllowed(Kind kind, IntVector2 up, IntVector2 d, IntVector2 laneOffset)
        {
            if (kind == Kind.Corner) return true;
            bool alongLine = Dot(d, up) == 0;
            if (kind == Kind.Crossing && !alongLine) return true;
            return alongLine && laneOffset == up;
        }

        public static bool CornerKind(IntVector2 a, IntVector2 b, IntVector2? sideA, IntVector2? sideB, out bool outside)
        {
            outside = false;
            int ra = Verdict(sideA, b), rb = Verdict(sideB, a);
            if (ra < 0 || rb < 0) return false;
            if (ra == 0 && rb == 0) return false;
            if (ra > 0 && rb > 0 && ra != rb) return false;
            outside = (ra > 0 ? ra : rb) == 1;
            return true;
        }

        private static int Verdict(IntVector2? side, IntVector2 otherLeg)
        {
            if (!side.HasValue) return 0;
            if (side.Value == Neg(otherLeg)) return 1;
            if (side.Value == otherLeg) return 2;
            return -1;
        }

        public static IntVector2 CornerSideAlong(IntVector2 toward, IntVector2 other, bool outside)
        {
            return outside ? Neg(other) : other;
        }

        public static IntVector2? SideAlong(IntVector2 cell, IntVector2 d, Func<IntVector2, IReadOnlyList<IntVector2>> neighbours,
            Func<IntVector2, IntVector2> upOf, int depth)
        {
            IReadOnlyList<IntVector2> nb = neighbours(cell);
            Kind k = KindOf(nb);
            IntVector2 up = upOf(cell);
            if (k == Kind.Straight) return up;
            if (k == Kind.Crossing) return Dot(d, up) == 0 ? up : (IntVector2?)null;
            if (depth <= 0) return null;
            IntVector2 other = nb[0] == d ? nb[1] : nb[0];
            IntVector2? sideOther = SideAlong(cell + other, Neg(other), neighbours, upOf, depth - 1);
            if (!CornerKind(d, other, null, sideOther, out bool outside)) return null;
            return CornerSideAlong(d, other, outside);
        }

        public struct Span
        {
            public IntVector2 Side;
            public IntVector2? Toward;
            public IntVector2 Cross;
        }

        public static List<Span> Spans(IntVector2 cell, Func<IntVector2, IReadOnlyList<IntVector2>> neighbours,
            Func<IntVector2, IntVector2> upOf, out (IntVector2 A, IntVector2 B, bool Outside)? corner)
        {
            corner = null;
            var spans = new List<Span>(3);
            IReadOnlyList<IntVector2> nb = neighbours(cell);
            Kind kind = KindOf(nb);
            if (kind != Kind.Corner)
            {
                IntVector2 up = upOf(cell);
                spans.Add(new Span { Side = up });
                if (kind != Kind.Crossing) return spans;
                foreach (IntVector2 leg in nb)
                {
                    if (Dot(leg, up) == 0) continue;
                    IntVector2? side = SideAlong(cell + leg, Neg(leg), neighbours, upOf, CORNER_WALK);
                    if (!side.HasValue || Dot(side.Value, leg) != 0) continue;
                    spans.Add(new Span { Side = side.Value, Toward = leg, Cross = up });
                }
                return spans;
            }
            IntVector2 a = nb[0], b = nb[1];
            IntVector2? sa = SideAlong(cell + a, Neg(a), neighbours, upOf, CORNER_WALK);
            IntVector2? sb = SideAlong(cell + b, Neg(b), neighbours, upOf, CORNER_WALK);
            if (!CornerKind(a, b, sa, sb, out bool outside)) return spans;
            corner = (a, b, outside);
            IntVector2 lineA = CornerSideAlong(a, b, outside), lineB = CornerSideAlong(b, a, outside);
            spans.Add(new Span { Side = lineA, Toward = a, Cross = lineB });
            spans.Add(new Span { Side = lineB, Toward = b, Cross = lineA });
            return spans;
        }

        public static int PieceBits(IntVector2 cell, List<Span> spans, Func<IntVector2, int> toLocal,
            Func<IntVector2, bool> blocked)
        {
            int result = 0;
            foreach (Span sp in spans)
            {
                if (!sp.Toward.HasValue && blocked(cell + sp.Side)) continue;
                int line = toLocal(sp.Side);
                if (line < 0) continue;
                int lo = 0, hi = 4;
                if (sp.Toward.HasValue)
                {
                    int crossLine = toLocal(sp.Cross);
                    int ci = crossLine == 0 || crossLine == 3 ? 1 : 3;
                    int toward = toLocal(sp.Toward.Value);
                    if (toward == 0 || toward == 3) hi = ci;
                    else lo = ci;
                }
                for (int seg = lo; seg <= hi; seg++) result |= 1 << (line * 5 + seg);
            }
            return result;
        }

        public static IntVector2? AutoUp(IntVector2 cell, Func<IntVector2, IReadOnlyList<IntVector2>> neighbours,
            Func<IntVector2, IntVector2> upOf)
        {
            IReadOnlyList<IntVector2> nb = neighbours(cell);
            if (nb.Count != 1) return null;
            IntVector2 r = cell + nb[0];
            IntVector2 e = Neg(nb[0]);
            IReadOnlyList<IntVector2> rn = neighbours(r);
            IntVector2 upR = upOf(r);
            if (rn.Count == 0) return Dot(e, upR) == 0 ? upR : (IntVector2?)null;
            if (rn.Count != 1) return null;
            IntVector2 line = rn[0];
            if (e == Neg(line)) return upR;
            if (!CornerKind(line, e, upR, null, out bool outside)) return null;
            return CornerSideAlong(e, line, outside);
        }
    }
}
