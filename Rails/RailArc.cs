using System;
using System.Collections.Generic;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class RailArc
    {
        public const float TRACK_HALF = 0.5f;
        public const float LANE_HALF = 0.5f;
        private const float EDGE_EPS = 0.01f;
        private const int SAMPLE_ALONG = 2000;
        private const int SAMPLE_ACROSS = 21;

        public static IntVector2 Neg(IntVector2 a) => new IntVector2(-a.X, -a.Y);
        public static int Dot(IntVector2 a, IntVector2 b) => a.X * b.X + a.Y * b.Y;
        public static IntVector2 Normal(IntVector2 d, int side) => new IntVector2(-d.Y * side, d.X * side);
        private static Vector2 Vf(IntVector2 a) => new Vector2(a.X, a.Y);
        private static Vector2 Center(IntVector2 c) => new Vector2(c.X + 0.5f, c.Y + 0.5f);
        private static Vector2 Dir(double a) => new Vector2((float)DetMath.Cos(a), (float)DetMath.Sin(a));
        private static float Dist(Vector2 a, Vector2 b) { float dx = a.X - b.X, dy = a.Y - b.Y; return (float)Math.Sqrt(dx * dx + dy * dy); }
        private static bool Before(IntVector2 a, IntVector2 b) => a.Y < b.Y || (a.Y == b.Y && a.X < b.X);

        public sealed class Shape
        {
            public IntVector2 A, B, OA, OB;
            public int M, N;
            public bool Inside;

            public int K => Math.Min(M, N);
            public float TrackRadius => K - 0.5f;
            public IntVector2 P => Neg(OB);
            public int StraightLength => Math.Abs(M - N);
            public bool StraightAtA => M > N;
            public Vector2 EdgeA => Center(A) + Vf(OA) * 0.5f;
            public Vector2 EdgeB => Center(B) + Vf(OB) * 0.5f;
            public Vector2 ArcStart => StraightAtA ? EdgeA + Vf(OA) * (M - N) : EdgeA;
            public Vector2 ArcCentre => ArcStart + Vf(P) * TrackRadius;
            public Vector2 ArcEnd => ArcCentre + Vf(OA) * TrackRadius;
            public double StartAngle => DetMath.Atan2(-P.Y, -P.X);
            public double Sweep => (-P.X * OA.Y - (-P.Y) * OA.X) > 0 ? Math.PI / 2 : -Math.PI / 2;
            public float TrackLength => StraightLength + (float)(Math.Abs(Sweep) * TrackRadius);

            public bool Has(IntVector2 t) => t == A || t == B;
            public IntVector2 Other(IntVector2 t) => t == A ? B : A;
            public IntVector2 OpenOf(IntVector2 t) => t == A ? OA : OB;

            public bool InsideFor(bool fromA, int side) => fromA ? Normal(OA, side) == P : Normal(OB, side) == Neg(OA);

            public float LaneLength(bool inside, float offset) =>
                StraightLength + (float)(Math.Abs(Sweep) * (TrackRadius + (inside ? -offset : offset)));

            private HashSet<IntVector2> _track, _inside, _outside;
            public HashSet<IntVector2> TrackCells => _track ?? (_track = SweepCells(this, 0f, TRACK_HALF));
            public HashSet<IntVector2> InsideCells => _inside ?? (_inside = SweepCells(this, -1f, LANE_HALF));
            public HashSet<IntVector2> OutsideCells => _outside ?? (_outside = SweepCells(this, 1f, LANE_HALF));
            public HashSet<IntVector2> LaneCells(bool inside) => inside ? InsideCells : OutsideCells;

            public float Distance(Vector2 point)
            {
                Vector2 c = ArcCentre;
                double ang = DetMath.Atan2(point.Y - c.Y, point.X - c.X);
                double rel = Math.IEEERemainder(ang - StartAngle, 2 * Math.PI);
                bool inSector = Sweep > 0 ? rel >= 0 && rel <= Sweep : rel <= 0 && rel >= Sweep;
                float best = inSector ? Math.Abs(Dist(point, c) - TrackRadius) : Math.Min(Dist(point, ArcStart), Dist(point, ArcEnd));
                if (StraightLength > 0)
                {
                    Vector2 s0 = StraightAtA ? EdgeA : ArcEnd, s1 = StraightAtA ? ArcStart : EdgeB;
                    best = Math.Min(best, SegmentDistance(point, s0, s1));
                }
                return best;
            }
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            float abx = b.X - a.X, aby = b.Y - a.Y, l2 = abx * abx + aby * aby;
            float t = l2 > 0f ? ((p.X - a.X) * abx + (p.Y - a.Y) * aby) / l2 : 0f;
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return Dist(p, new Vector2(a.X + abx * t, a.Y + aby * t));
        }

        public static bool TryPair(IntVector2 a, IntVector2 oA, IntVector2 upA, IntVector2 b, IntVector2 oB, IntVector2 upB,
            int min, int max, out Shape shape, out string why)
        {
            shape = null;
            why = null;
            if (Dot(oA, oB) != 0) { why = "not at right angles"; return false; }
            IntVector2 p = Neg(oB), d = b - a;
            int m = Dot(d, oA), n = Dot(d, p);
            if (m <= 0 || n <= 0) { why = "not ahead of both"; return false; }
            if (m < min || n < min) { why = $"too close ({Math.Min(m, n)} apart)"; return false; }
            if (m > max || n > max) { why = $"too far ({Math.Max(m, n)} apart)"; return false; }
            bool inside = upA == p && upB == Neg(oA);
            bool outside = upA == Neg(p) && upB == oA;
            if (!inside && !outside) { why = "sides disagree"; return false; }
            shape = new Shape { A = a, B = b, OA = oA, OB = oB, M = m, N = n, Inside = inside };
            return true;
        }

        public static List<Shape> FindPairs(IEnumerable<IntVector2> curves, Func<IntVector2, IReadOnlyList<IntVector2>> neighbourDirs,
            Func<IntVector2, IntVector2> upOf, int min, int max, out Dictionary<IntVector2, string> why, bool previewLone = false)
        {
            List<Shape> strict = PairPass(curves, neighbourDirs, upOf, min, max, out why, false);
            if (!previewLone) return strict;
            var taken = new HashSet<IntVector2>();
            foreach (Shape s in strict) { taken.Add(s.A); taken.Add(s.B); }
            var rest = new List<IntVector2>();
            foreach (IntVector2 c in curves) if (!taken.Contains(c)) rest.Add(c);
            List<Shape> lone = PairPass(rest, neighbourDirs, upOf, min, max, out why, true);
            foreach (Shape s in lone)
            {
                bool crosses = false;
                foreach (Shape t in strict) if (s.TrackCells.Overlaps(t.TrackCells)) { crosses = true; break; }
                if (crosses)
                {
                    why[s.A] = "crosses another curve";
                    why[s.B] = "crosses another curve";
                }
                else strict.Add(s);
            }
            return strict;
        }

        private static List<Shape> PairPass(IEnumerable<IntVector2> curves, Func<IntVector2, IReadOnlyList<IntVector2>> neighbourDirs,
            Func<IntVector2, IntVector2> upOf, int min, int max, out Dictionary<IntVector2, string> why, bool previewLone)
        {
            why = new Dictionary<IntVector2, string>();
            var opens = new Dictionary<IntVector2, List<IntVector2>>();
            var order = new List<IntVector2>();
            foreach (IntVector2 c in curves)
            {
                IReadOnlyList<IntVector2> nb = neighbourDirs(c);
                if (nb.Count == 1) { opens[c] = new List<IntVector2> { Neg(nb[0]) }; order.Add(c); }
                else if (nb.Count == 0 && previewLone)
                {
                    IntVector2 up = upOf(c);
                    opens[c] = new List<IntVector2> { new IntVector2(-up.Y, up.X), new IntVector2(up.Y, -up.X) };
                    order.Add(c);
                }
                else why[c] = nb.Count == 0 ? "no rail line attached" : "not at the end of a line";
            }
            order.Sort((x, y) => x.Y != y.Y ? x.Y.CompareTo(y.Y) : x.X.CompareTo(y.X));
            var choice = new Dictionary<IntVector2, IntVector2>();
            var choiceShape = new Dictionary<IntVector2, Shape>();
            foreach (IntVector2 c in order)
            {
                int best = int.MaxValue, missSum = int.MaxValue;
                IntVector2? pick = null;
                Shape pickShape = null;
                bool tie = false;
                string miss = null;
                foreach (IntVector2 b in order)
                {
                    if (b == c) continue;
                    foreach (IntVector2 oc in opens[c])
                    {
                        foreach (IntVector2 ob in opens[b])
                        {
                            if (TryPair(c, oc, upOf(c), b, ob, upOf(b), min, max, out Shape s, out string w))
                            {
                                int sum = s.M + s.N;
                                if (sum < best) { best = sum; pick = b; pickShape = s; tie = false; }
                                else if (sum == best && b != pick) tie = true;
                            }
                            else if (w != "not at right angles" && w != "not ahead of both")
                            {
                                int sum = Math.Abs(b.X - c.X) + Math.Abs(b.Y - c.Y);
                                if (sum < missSum) { missSum = sum; miss = $"{w} with {b.X},{b.Y}"; }
                            }
                        }
                    }
                }
                if (tie) why[c] = "two partners at the same distance";
                else if (pick.HasValue) { choice[c] = pick.Value; choiceShape[c] = pickShape; }
                else why[c] = miss ?? "no partner in range";
            }
            var shapes = new List<Shape>();
            foreach (IntVector2 c in order)
            {
                if (!choice.TryGetValue(c, out IntVector2 b)) continue;
                if (choice.TryGetValue(b, out IntVector2 back) && back == c)
                {
                    if (!Before(c, b)) continue;
                    shapes.Add(choiceShape[c]);
                }
                else
                {
                    why[c] = choice.TryGetValue(b, out back) ? $"partner {b.X},{b.Y} chose {back.X},{back.Y}" : $"partner {b.X},{b.Y} links to none";
                }
            }
            var crossed = new HashSet<Shape>();
            for (int i = 0; i < shapes.Count; i++)
                for (int j = i + 1; j < shapes.Count; j++)
                    if (shapes[i].TrackCells.Overlaps(shapes[j].TrackCells)) { crossed.Add(shapes[i]); crossed.Add(shapes[j]); }
            foreach (Shape s in crossed)
            {
                why[s.A] = "crosses another curve";
                why[s.B] = "crosses another curve";
                shapes.Remove(s);
            }
            return shapes;
        }

        public static List<Vector2> LanePolyline(Shape s, bool fromA, int side, float offset, float maxChord)
        {
            bool inside = s.InsideFor(fromA, side);
            var pts = new List<Vector2>();
            Vector2 latA = Vf(inside ? s.P : Neg(s.P)), latB = Vf(inside ? Neg(s.OA) : s.OA);
            if (s.StraightAtA) pts.Add(s.EdgeA + latA * offset);
            float rl = s.TrackRadius + (inside ? -offset : offset);
            int steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(s.Sweep) * rl / maxChord));
            for (int i = 0; i <= steps; i++) pts.Add(s.ArcCentre + Dir(s.StartAngle + s.Sweep * i / steps) * rl);
            if (!s.StraightAtA && s.StraightLength > 0) pts.Add(s.EdgeB + latB * offset);
            if (!fromA) pts.Reverse();
            return pts;
        }

        public static (Vector2 Start, Vector2 End) LaneArcEnds(Shape s, bool fromA, int side, float offset)
        {
            float rl = s.TrackRadius + (s.InsideFor(fromA, side) ? -offset : offset);
            Vector2 a = s.ArcCentre + Dir(s.StartAngle) * rl, b = s.ArcCentre + Dir(s.StartAngle + s.Sweep) * rl;
            return fromA ? (a, b) : (b, a);
        }

        public static List<Vector2> TrackPolyline(Shape s, float maxChord)
        {
            var pts = new List<Vector2>();
            if (s.StraightAtA) pts.Add(s.EdgeA);
            int steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(s.Sweep) * s.TrackRadius / maxChord));
            for (int i = 0; i <= steps; i++) pts.Add(s.ArcCentre + Dir(s.StartAngle + s.Sweep * i / steps) * s.TrackRadius);
            if (!s.StraightAtA && s.StraightLength > 0) pts.Add(s.EdgeB);
            return pts;
        }

        private static HashSet<IntVector2> SweepCells(Shape s, float radial, float half)
        {
            var cells = new HashSet<IntVector2>();
            float r = s.TrackRadius + radial;
            Vector2 c = s.ArcCentre;
            for (int i = 0; i <= SAMPLE_ALONG; i++)
            {
                Vector2 d = Dir(s.StartAngle + s.Sweep * i / SAMPLE_ALONG);
                for (int j = 0; j < SAMPLE_ACROSS; j++)
                {
                    float rr = r - half + 2f * half * j / (SAMPLE_ACROSS - 1);
                    AddCell(cells, c.X + d.X * rr, c.Y + d.Y * rr);
                }
            }
            if (s.StraightLength > 0)
            {
                Vector2 s0 = s.StraightAtA ? s.EdgeA : s.ArcEnd, s1 = s.StraightAtA ? s.ArcStart : s.EdgeB;
                Vector2 outward = Vf(s.StraightAtA ? Neg(s.P) : s.OA);
                for (int i = 0; i <= SAMPLE_ALONG; i++)
                {
                    float t = (float)i / SAMPLE_ALONG;
                    Vector2 bp = new Vector2(s0.X + (s1.X - s0.X) * t, s0.Y + (s1.Y - s0.Y) * t);
                    for (int j = 0; j < SAMPLE_ACROSS; j++)
                    {
                        float off = radial - half + 2f * half * j / (SAMPLE_ACROSS - 1);
                        AddCell(cells, bp.X + outward.X * off, bp.Y + outward.Y * off);
                    }
                }
            }
            cells.Remove(s.A);
            cells.Remove(s.B);
            return cells;
        }

        private static void AddCell(HashSet<IntVector2> cells, float x, float y)
        {
            int fx = (int)Math.Floor(x), fy = (int)Math.Floor(y);
            if (x - fx < EDGE_EPS || fx + 1 - x < EDGE_EPS || y - fy < EDGE_EPS || fy + 1 - y < EDGE_EPS) return;
            cells.Add(new IntVector2(fx, fy));
        }

        public static void BandAt(Shape s, float t, out Vector2 centre, out Vector2 outward)
        {
            float sl = s.StraightLength, arcLen = (float)(Math.Abs(s.Sweep) * s.TrackRadius);
            float arcFrom = s.StraightAtA ? sl : 0f, arcTo = arcFrom + arcLen;
            if (s.StraightAtA && t <= arcFrom) { centre = s.EdgeA + Vf(s.OA) * t; outward = Vf(Neg(s.P)); return; }
            if (t <= arcTo || s.StraightLength == 0)
            {
                Vector2 d = Dir(s.StartAngle + s.Sweep * Math.Min(1.0, (t - arcFrom) / arcLen));
                centre = s.ArcCentre + d * s.TrackRadius;
                outward = d;
                return;
            }
            centre = s.ArcEnd + Vf(s.P) * (t - arcTo);
            outward = Vf(s.OA);
        }

        public struct ArtQuad
        {
            public Vector2 P1, P2, P3, P4;
            public float U0, U1;
        }

        public static List<ArtQuad> ArtQuads(Shape s, float half, float maxSeg)
        {
            float sl = s.StraightLength, arcLen = (float)(Math.Abs(s.Sweep) * s.TrackRadius), total = sl + arcLen;
            float arcFrom = s.StraightAtA ? sl : 0f, arcTo = arcFrom + arcLen;
            var marks = new List<float>();
            for (float m = 0f; m < total - 1e-4f; m += maxSeg) marks.Add(m);
            if (arcFrom > 1e-4f && !marks.Contains(arcFrom)) marks.Add(arcFrom);
            if (arcTo < total - 1e-4f) marks.Add(arcTo);
            marks.Add(total);
            marks.Sort();
            float top = s.Inside ? -half : half, bot = -top;
            var quads = new List<ArtQuad>();
            for (int i = 0; i + 1 < marks.Count; i++)
            {
                float t0 = marks[i], t1 = marks[i + 1];
                if (t1 - t0 < 1e-4f) continue;
                BandAt(s, t0 + 1e-5f, out Vector2 c0, out Vector2 o0);
                BandAt(s, t1 - 1e-5f, out Vector2 c1, out Vector2 o1);
                float u0 = t0 - (float)Math.Floor(t0 + 1e-5f), u1 = u0 + (t1 - t0);
                var q = new ArtQuad
                {
                    P1 = c0 + o0 * top, P2 = c1 + o1 * top, P3 = c0 + o0 * bot, P4 = c1 + o1 * bot,
                    U0 = u0, U1 = u1,
                };
                float cross = (q.P2.X - q.P1.X) * (q.P3.Y - q.P1.Y) - (q.P2.Y - q.P1.Y) * (q.P3.X - q.P1.X);
                if (cross < 0f)
                {
                    q = new ArtQuad { P1 = q.P2, P2 = q.P1, P3 = q.P4, P4 = q.P3, U0 = q.U1, U1 = q.U0 };
                }
                quads.Add(q);
            }
            return quads;
        }

        public struct HitPiece
        {
            public Vector2 P1, P2, P3, P4;
            public float T0, T1;
            public bool OwnerA;
        }

        public static List<HitPiece> HitPieces(Shape s, float half, float maxSeg)
        {
            float sl = s.StraightLength, arcLen = (float)(Math.Abs(s.Sweep) * s.TrackRadius), total = sl + arcLen;
            float arcFrom = s.StraightAtA ? sl : 0f, arcTo = arcFrom + arcLen, mid = total * 0.5f;
            var marks = new List<float>();
            for (float m = 0f; m < total - 1e-4f; m += maxSeg) marks.Add(m);
            foreach (float extra in new[] { arcFrom, arcTo, mid })
                if (extra > 1e-4f && extra < total - 1e-4f && !marks.Exists(x => Math.Abs(x - extra) < 1e-4f)) marks.Add(extra);
            marks.Add(total);
            marks.Sort();
            var pieces = new List<HitPiece>();
            for (int i = 0; i + 1 < marks.Count; i++)
            {
                float t0 = marks[i], t1 = marks[i + 1];
                if (t1 - t0 < 1e-4f) continue;
                BandAt(s, t0 + 1e-5f, out Vector2 c0, out Vector2 o0);
                BandAt(s, t1 - 1e-5f, out Vector2 c1, out Vector2 o1);
                pieces.Add(new HitPiece
                {
                    P1 = c0 + o0 * half, P2 = c1 + o1 * half, P3 = c0 + o0 * (-half), P4 = c1 + o1 * (-half),
                    T0 = t0, T1 = t1, OwnerA = t1 <= mid + 1e-4f,
                });
            }
            return pieces;
        }
    }
}
