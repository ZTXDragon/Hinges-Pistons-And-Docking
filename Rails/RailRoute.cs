using System;
using System.Collections.Generic;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class RailRoute
    {
        private struct Sample
        {
            public Vector2 P;
            public float S;
            public float Cum;
        }

        private const float ARC_STEP = (float)(Math.PI / 12);
        private const float MIN_CHORD = 0.1f;
        private const float INSIDE_FILLET = 0.5f;
        private const float TURN_HALF_WIDTH = 0.5f;
        private const float START_TURN_LENGTH = 1f;
        private const float MIN_CORNER_LEAD = 0.05f;

        public float CornerLead;

        public Func<IntVector2, IntVector2, RailArc.Shape> ArcBetween;
        private const float ARC_CHORD = 0.25f;
        private readonly List<(Vector2 Start, Vector2 End)> _arcSpans = new List<(Vector2 Start, Vector2 End)>();

        private struct Turn
        {
            public float Lo;
            public float Hi;
            public float Delta;
            public bool Ease;
        }

        private readonly List<Turn> _turns = new List<Turn>();
        private float _cum0;
        private float _startOff;
        private float _startLen;
        private float _fadeStart;

        public readonly List<Part> Tiles = new List<Part>();

        public Cosmoteer.Ships.Ship PlannedOn;
        public Halfling.Geometry.IntVector2 TerminalAt;

        private readonly List<Sample> _samples = new List<Sample>();
        private readonly List<float> _tileS = new List<float>();

        public int Side;
        public float Length;
        private float? _startHeading;

        public bool CompleteEndTurn;
        private float? _trackEndHeading;

        private float _clearance;
        private Vector2? _startPoint;
        private Vector2? _prevDir;
        private float? _startHeadingIn;

        public bool IsEmpty
        {
            get { return _samples.Count < 2; }
        }

        public void Build(List<Part> tiles, int side, float clearance)
        {
            Build(tiles, side, clearance, null, null, null);
        }

        public void Build(List<Part> tiles, int side, float clearance, Vector2? startPoint, Vector2? prevDir, float? startHeading)
        {
            Tiles.Clear();
            Tiles.AddRange(tiles);
            var cells = new List<IntVector2>(tiles.Count);
            foreach (Part t in tiles) cells.Add(t.Location);
            BuildCore(cells, side, clearance, startPoint, prevDir, startHeading);
        }

        private void BuildCore(List<IntVector2> cells, int side, float clearance, Vector2? startPoint, Vector2? prevDir, float? startHeading)
        {
            _clearance = clearance;
            _startPoint = startPoint;
            _prevDir = prevDir;
            _startHeadingIn = startHeading;
            _samples.Clear();
            _tileS.Clear();
            Side = side >= 0 ? 1 : -1;
            Length = 0f;
            float offset = 1f + Math.Max(0f, clearance);
            int n = cells.Count;
            var pts = new List<Vector2>();
            if (n == 0) return;
            _startHeading = null;
            if (n == 1)
            {
                pts.Add(Center(cells[0]));
                Finish(pts);
                _tileS.Add(0f);
                return;
            }

            var verts = new List<Vector2>();
            var vertTile = new List<int>();
            for (int i = 0; i < n; i++)
            {
                verts.Add(Center(cells[i]));
                vertTile.Add(i);
            }

            int m = verts.Count;
            int segCount = m - 1;
            var dir = new Vector2[segCount];
            var nrm = new Vector2[segCount];
            var dirIn = new Vector2[segCount];
            var nrmIn = new Vector2[segCount];
            var arcPts = new List<Vector2>[segCount];
            var segExtra = new float[segCount];
            _arcSpans.Clear();
            for (int i = 0; i < segCount; i++)
            {
                RailArc.Shape arc = ArcBetween?.Invoke(cells[i], cells[i + 1]);
                if (arc != null)
                {
                    IntVector2 o = arc.OpenOf(cells[i]), oi = RailArc.Neg(arc.OpenOf(cells[i + 1]));
                    bool fromA = cells[i] == arc.A;
                    dir[i] = new Vector2(o.X, o.Y);
                    dirIn[i] = new Vector2(oi.X, oi.Y);
                    arcPts[i] = RailArc.LanePolyline(arc, fromA, Side, offset, ARC_CHORD);
                    _arcSpans.Add(RailArc.LaneArcEnds(arc, fromA, Side, offset));
                    segExtra[i] = arc.LaneLength(arc.InsideFor(fromA, Side), offset);
                }
                else
                {
                    Vector2 a = verts[i], b = verts[i + 1];
                    float dx = b.X - a.X, dy = b.Y - a.Y;
                    float len = (float)Math.Sqrt(dx * dx + dy * dy);
                    dir[i] = new Vector2(dx / len, dy / len);
                    dirIn[i] = dir[i];
                }
                nrm[i] = new Vector2(-dir[i].Y * Side * offset, dir[i].X * Side * offset);
                nrmIn[i] = new Vector2(-dirIn[i].Y * Side * offset, dirIn[i].X * Side * offset);
            }

            if (prevDir.HasValue && (Math.Abs(prevDir.Value.X) > 0.001f || Math.Abs(prevDir.Value.Y) > 0.001f))
            {
                float pl = (float)Math.Sqrt(prevDir.Value.X * prevDir.Value.X + prevDir.Value.Y * prevDir.Value.Y);
                var dPrev = new Vector2(prevDir.Value.X / pl, prevDir.Value.Y / pl);
                var nPrev = new Vector2(-dPrev.Y * Side * offset, dPrev.X * Side * offset);
                pts.Add(startPoint ?? new Vector2(verts[0].X + nPrev.X, verts[0].Y + nPrev.Y));
                Corner(pts, verts[0], dPrev, nPrev, dir[0], nrm[0], offset,
                    m == 2 ? new Vector2(verts[1].X + nrmIn[0].X, verts[1].Y + nrmIn[0].Y) : (Vector2?)null);
                _startHeading = startHeading ?? (float)DetMath.Atan2(dPrev.Y, dPrev.X);
            }
            else
            {
                if (startPoint.HasValue) pts.Add(startPoint.Value);
                pts.Add(new Vector2(verts[0].X + nrm[0].X, verts[0].Y + nrm[0].Y));
                _startHeading = null;
            }
            if (arcPts[0] != null) pts.AddRange(arcPts[0]);
            Vector2 endPoint = new Vector2(verts[m - 1].X + nrmIn[segCount - 1].X, verts[m - 1].Y + nrmIn[segCount - 1].Y);
            for (int j = 1; j <= m - 2; j++)
            {
                Corner(pts, verts[j], dirIn[j - 1], nrmIn[j - 1], dir[j], nrm[j], offset, j == m - 2 ? endPoint : (Vector2?)null);
                if (arcPts[j] != null) pts.AddRange(arcPts[j]);
            }
            pts.Add(endPoint);
            _trackEndHeading = (float)DetMath.Atan2(dirIn[segCount - 1].Y, dirIn[segCount - 1].X);
            Finish(pts);

            float prevS = 0f;
            for (int i = 0; i < n; i++)
            {
                int v = vertTile.IndexOf(i);
                Vector2 np = nrmIn[Math.Max(0, Math.Min(v - 1, segCount - 1))];
                Vector2 nn = nrm[Math.Max(0, Math.Min(v, segCount - 1))];
                float ax = np.X + nn.X, ay = np.Y + nn.Y;
                float al = (float)Math.Sqrt(ax * ax + ay * ay);
                if (al < 0.001f)
                {
                    Vector2 dp = dirIn[Math.Max(0, Math.Min(v - 1, segCount - 1))];
                    ax = dp.X * offset; ay = dp.Y * offset; al = offset;
                }
                Vector2 c = verts[v];
                float extra = v >= 1 ? segExtra[Math.Min(v - 1, segCount - 1)] : 0f;
                float ts = Project(new Vector2(c.X + ax / al * offset, c.Y + ay / al * offset), prevS - 0.05f, prevS + 4f + extra, out _);
                if (ts < prevS) ts = prevS;
                _tileS.Add(ts);
                prevS = ts;
            }
        }

        public void Rebuild(List<Part> tiles, bool keepStart)
        {
            var copy = new List<Part>(tiles);
            float fadeStart = _fadeStart, startOff = _startOff, startLen = _startLen;
            if (keepStart) Build(copy, Side, _clearance, _startPoint, _prevDir, _startHeadingIn);
            else Build(copy, Side, _clearance, null, null, null);
            if (keepStart)
            {
                _fadeStart = fadeStart;
                _startOff = startOff;
                _startLen = startLen;
            }
        }

        public void AnchorStart(float s0, float? heading)
        {
            _fadeStart = s0;
            _startOff = 0f;
            _startLen = 0f;
            if (!heading.HasValue || _samples.Count < 2) return;
            float pathAt = CumAt(s0);
            float off = WrapAngle(heading.Value - pathAt);
            _startOff = Math.Abs(off) <= 0.5f * Math.PI + 0.01f ? off : WrapAngle(off - Math.Sign(off) * Math.PI);
            _startLen = Math.Min(START_TURN_LENGTH, Math.Max(0f, Length - s0));
        }

        public int TileIndexAt(float s)
        {
            int idx = 0;
            for (int i = 0; i < _tileS.Count; i++)
                if (_tileS[i] <= s) idx = i;
            return idx;
        }

        private void Corner(List<Vector2> pts, Vector2 v, Vector2 dPrev, Vector2 nPrev, Vector2 dNext, Vector2 nNext, float offset,
            Vector2? nextFixed = null)
        {
            if (dPrev.X * dNext.X + dPrev.Y * dNext.Y < -0.5f)
            {
                float u0 = (float)DetMath.Atan2(nPrev.Y, nPrev.X);
                float half = WrapAngle(DetMath.Atan2(dPrev.Y, dPrev.X) - u0);
                int uSteps = Math.Max(2, (int)Math.Round(Math.PI / ARC_STEP));
                for (int i = 0; i <= uSteps; i++)
                {
                    float a = u0 + 2f * half * i / uSteps;
                    pts.Add(new Vector2(v.X + (float)DetMath.Cos(a) * offset, v.Y + (float)DetMath.Sin(a) * offset));
                }
                return;
            }
            float turn = dPrev.X * dNext.Y - dPrev.Y * dNext.X;
            if (Math.Abs(turn) < 0.001f)
            {
                pts.Add(new Vector2(v.X + nNext.X, v.Y + nNext.Y));
                return;
            }
            if ((turn > 0f) == (Side > 0))
            {
                float k = 1f + (nPrev.X * nNext.X + nPrev.Y * nNext.Y) / (offset * offset);
                var p = new Vector2(v.X + (nPrev.X + nNext.X) / k, v.Y + (nPrev.Y + nNext.Y) / k);
                float r = INSIDE_FILLET;
                if (pts.Count > 0) r = Math.Min(r, 0.9f * Dist(pts[pts.Count - 1], p));
                if (nextFixed.HasValue) r = Math.Min(r, 0.9f * Dist(nextFixed.Value, p));
                if (r < 0.05f)
                {
                    pts.Add(p);
                    return;
                }
                float ox = p.X - dPrev.X * r + dNext.X * r, oy = p.Y - dPrev.Y * r + dNext.Y * r;
                float b0 = (float)DetMath.Atan2(-dNext.Y, -dNext.X);
                float sw = WrapAngle(DetMath.Atan2(dPrev.Y, dPrev.X) - b0);
                int fSteps = Math.Max(2, (int)Math.Round(Math.Abs(sw) / ARC_STEP));
                for (int i = 0; i <= fSteps; i++)
                {
                    float a = b0 + sw * i / fSteps;
                    pts.Add(new Vector2(ox + (float)DetMath.Cos(a) * r, oy + (float)DetMath.Sin(a) * r));
                }
                return;
            }
            float a0 = (float)DetMath.Atan2(nPrev.Y, nPrev.X);
            float a1 = (float)DetMath.Atan2(nNext.Y, nNext.X);
            float sweep = WrapAngle(a1 - a0);
            int steps = Math.Max(2, (int)Math.Round(Math.Abs(sweep) / ARC_STEP));
            for (int i = 0; i <= steps; i++)
            {
                float a = a0 + sweep * i / steps;
                pts.Add(new Vector2(v.X + (float)DetMath.Cos(a) * offset, v.Y + (float)DetMath.Sin(a) * offset));
            }
        }

        private void Finish(List<Vector2> pts)
        {
            var clean = new List<Vector2>();
            foreach (Vector2 p in pts)
            {
                if (clean.Count > 0)
                {
                    Vector2 q = clean[clean.Count - 1];
                    float dx = p.X - q.X, dy = p.Y - q.Y;
                    if (dx * dx + dy * dy < 1e-8f) continue;
                }
                clean.Add(p);
            }
            for (int guard = 0; guard < clean.Count; guard++)
            {
                int folded = -1;
                for (int i = 1; i + 1 < clean.Count; i++)
                {
                    float ax = clean[i].X - clean[i - 1].X, ay = clean[i].Y - clean[i - 1].Y;
                    float bx = clean[i + 1].X - clean[i].X, by = clean[i + 1].Y - clean[i].Y;
                    float la = (float)Math.Sqrt(ax * ax + ay * ay), lb = (float)Math.Sqrt(bx * bx + by * by);
                    if (la < 1e-6f || lb < 1e-6f) continue;
                    if ((ax * bx + ay * by) / (la * lb) < -0.7071f) { folded = i; break; }
                }
                if (folded < 0) break;
                clean.RemoveAt(folded);
            }
            int m = clean.Count - 1;
            var s = new float[clean.Count];
            var h = new float[Math.Max(0, m)];
            for (int i = 0; i < m; i++)
            {
                float dx = clean[i + 1].X - clean[i].X, dy = clean[i + 1].Y - clean[i].Y;
                s[i + 1] = s[i] + (float)Math.Sqrt(dx * dx + dy * dy);
                h[i] = (float)DetMath.Atan2(dy, dx);
            }
            Length = clean.Count > 0 ? s[clean.Count - 1] : 0f;
            _turns.Clear();
            _cum0 = 0f;
            _startOff = 0f;
            _startLen = 0f;
            if (m >= 1)
            {
                var hv = new float[m];
                int firstValid = -1;
                for (int i = 0; i < m; i++)
                    if (s[i + 1] - s[i] >= MIN_CHORD) { firstValid = i; break; }
                for (int i = 0; i < m; i++)
                {
                    if (firstValid < 0 || s[i + 1] - s[i] >= MIN_CHORD) hv[i] = h[i];
                    else hv[i] = i < firstValid ? h[firstValid] : hv[i - 1];
                }
                if (CompleteEndTurn && _trackEndHeading.HasValue && m >= 2 && s[m] - s[m - 1] < MIN_CHORD)
                    hv[m - 1] = _trackEndHeading.Value;
                var u = new float[m];
                u[0] = hv[0];
                for (int i = 1; i < m; i++) u[i] = u[i - 1] + WrapAngle(hv[i] - hv[i - 1]);
                _cum0 = u[0];
                var onArc = new bool[m];
                var arcTurns = new List<Turn>();
                int spanFrom = 0;
                foreach (var span in _arcSpans)
                {
                    int i0 = NearestIndex(clean, span.Start, spanFrom);
                    int i1 = i0 < 0 ? -1 : NearestIndex(clean, span.End, i0 + 1);
                    if (i0 < 0 || i1 <= i0) continue;
                    spanFrom = i1;
                    float delta = 0f;
                    for (int i = Math.Max(1, i0); i <= Math.Min(m - 1, i1); i++)
                    {
                        delta += u[i] - u[i - 1];
                        onArc[i] = true;
                    }
                    arcTurns.Add(new Turn { Lo = s[i0], Hi = s[i1], Delta = delta, Ease = false });
                }
                if (CornerLead > 0f)
                {
                    AddEasedTurns(s, u, m, onArc);
                }
                else
                {
                    for (int i = 1; i < m; i++)
                    {
                        if (onArc[i]) continue;
                        float d = u[i] - u[i - 1];
                        if (Math.Abs(d) < 1e-5f) continue;
                        float lo = s[i] - TURN_HALF_WIDTH, hi = s[i] + TURN_HALF_WIDTH;
                        if (lo < 0f) { hi = Math.Min(Length, hi - lo); lo = 0f; }
                        if (hi > Length) { lo = Math.Max(0f, lo - (hi - Length)); hi = Length; }
                        _turns.Add(new Turn { Lo = lo, Hi = hi, Delta = d });
                    }
                }
                _turns.AddRange(arcTurns);
                float startOff = _startHeading.HasValue ? WrapAngle(_startHeading.Value - u[0]) : 0f;
                if (_startHeading.HasValue && Math.Abs(startOff) <= 0.5f * Math.PI + 0.01f)
                {
                    _startOff = startOff;
                    _startLen = Math.Min(START_TURN_LENGTH, Length);
                }
                else if (_startHeading.HasValue)
                {
                    _startOff = WrapAngle(startOff - Math.Sign(startOff) * Math.PI);
                    _startLen = Math.Min(START_TURN_LENGTH, Length);
                }
            }
            for (int i = 0; i < clean.Count; i++)
                _samples.Add(new Sample { P = clean[i], S = s[i], Cum = CumAt(s[i]) });
        }

        private static int NearestIndex(List<Vector2> pts, Vector2 p, int from)
        {
            int best = -1;
            float bestD = 0.01f;
            for (int i = Math.Max(0, from); i < pts.Count; i++)
            {
                float d = Dist(pts[i], p);
                if (d < bestD) { bestD = d; best = i; }
                else if (best >= 0) break;
            }
            return best;
        }

        private void AddEasedTurns(float[] s, float[] u, int m, bool[] onArc)
        {
            var lo = new List<float>();
            var hi = new List<float>();
            var delta = new List<float>();
            for (int i = 1; i < m; i++)
            {
                if (onArc[i]) continue;
                float d = u[i] - u[i - 1];
                if (Math.Abs(d) < 1e-5f) continue;
                int k = delta.Count - 1;
                if (k >= 0 && Math.Sign(d) == Math.Sign(delta[k]) && s[i] - hi[k] <= 2f * TURN_HALF_WIDTH)
                {
                    hi[k] = s[i];
                    delta[k] += d;
                }
                else
                {
                    lo.Add(s[i]);
                    hi.Add(s[i]);
                    delta.Add(d);
                }
            }
            for (int k = 0; k < delta.Count; k++)
            {
                float lead = CornerLead;
                if (k > 0) lead = Math.Min(lead, 0.5f * (lo[k] - hi[k - 1]));
                if (k + 1 < delta.Count) lead = Math.Min(lead, 0.5f * (lo[k + 1] - hi[k]));
                lead = Math.Max(lead, MIN_CORNER_LEAD);
                float a = lo[k] - lead, b = hi[k] + lead;
                if (a < 0f) { b = Math.Min(Length, b - a); a = 0f; }
                if (b > Length) { a = Math.Max(0f, a - (b - Length)); b = Length; }
                _turns.Add(new Turn { Lo = a, Hi = b, Delta = delta[k], Ease = true });
            }
        }

        public float TileS(int index)
        {
            if (index < 0 || index >= _tileS.Count) return Length;
            return _tileS[index];
        }

        public Vector2 Point(float s)
        {
            if (_samples.Count == 0) return new Vector2(0f, 0f);
            if (_samples.Count == 1) return _samples[0].P;
            s = Clamp(s);
            int i = SegmentAt(s);
            Sample a = _samples[i], b = _samples[i + 1];
            float len = b.S - a.S;
            float t = len > 0f ? (s - a.S) / len : 0f;
            return new Vector2(a.P.X + (b.P.X - a.P.X) * t, a.P.Y + (b.P.Y - a.P.Y) * t);
        }

        public Vector2 Tangent(float s, float e)
        {
            Vector2 a = Point(Clamp(s - e)), b = Point(Clamp(s + e));
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float l = (float)Math.Sqrt(dx * dx + dy * dy);
            return l > 1e-6f ? new Vector2(dx / l, dy / l) : new Vector2(1f, 0f);
        }

        public float Cum(float s)
        {
            if (_samples.Count < 2) return 0f;
            return CumAt(Clamp(s));
        }

        public float PathHeading(float s)
        {
            if (_samples.Count < 2) return 0f;
            return PathAt(Clamp(s));
        }

        private float PathAt(float s)
        {
            float c = _cum0;
            foreach (Turn t in _turns)
            {
                if (s >= t.Hi) c += t.Delta;
                else if (s > t.Lo)
                {
                    if (!(t.Hi > t.Lo)) c += t.Delta;
                    else if (t.Ease)
                    {
                        float x = (s - t.Lo) / (t.Hi - t.Lo);
                        c += t.Delta * x * x * (3f - 2f * x);
                    }
                    else c += t.Delta * (s - t.Lo) / (t.Hi - t.Lo);
                }
            }
            return c;
        }

        public float TurnRate(float s)
        {
            if (_samples.Count < 2) return 0f;
            s = Clamp(s);
            float r = 0f;
            foreach (Turn t in _turns)
            {
                if (!(s > t.Lo) || !(s < t.Hi) || !(t.Hi > t.Lo)) continue;
                float w = t.Hi - t.Lo;
                float x = (s - t.Lo) / w;
                r += t.Delta * (t.Ease ? 6f * x * (1f - x) : 1f) / w;
            }
            return Math.Abs(r);
        }

        public float Curvature(float s, float h)
        {
            if (_samples.Count < 2 || !(h > 0f) || Length < 2f * h) return 0f;
            float m = Math.Max(h, Math.Min(Length - h, s));
            Vector2 a = Point(m - h), b = Point(m), c = Point(m + h);
            double a1 = DetMath.Atan2(b.Y - a.Y, b.X - a.X), a2 = DetMath.Atan2(c.Y - b.Y, c.X - b.X);
            return Math.Abs(WrapAngle(a2 - a1)) / h;
        }

        private float CumAt(float s)
        {
            float c = PathAt(s);
            if (_startOff != 0f)
            {
                float into = s - _fadeStart;
                c += into <= 0f ? _startOff : (_startLen > 0f ? _startOff * Math.Max(0f, 1f - into / _startLen) : 0f);
            }
            return c;
        }

        private int SegmentAt(float s)
        {
            int last = _samples.Count - 2;
            for (int i = 0; i < last; i++)
                if (s < _samples[i + 1].S) return i;
            return last;
        }

        public float Project(Vector2 grid, out float distance)
        {
            return Project(grid, float.NegativeInfinity, float.PositiveInfinity, out distance);
        }

        public float Project(Vector2 grid, float lo, float hi, out float distance)
        {
            distance = float.MaxValue;
            float best = lo > 0f && lo < Length ? lo : 0f;
            if (_samples.Count == 0) return 0f;
            if (_samples.Count == 1)
            {
                distance = Dist(_samples[0].P, grid);
                return 0f;
            }
            for (int i = 0; i + 1 < _samples.Count; i++)
            {
                float sa = _samples[i].S, sb = _samples[i + 1].S;
                if (sb < lo || sa > hi) continue;
                Vector2 a = _samples[i].P, b = _samples[i + 1].P;
                float abx = b.X - a.X, aby = b.Y - a.Y;
                float l2 = abx * abx + aby * aby;
                float t = l2 > 0f ? ((grid.X - a.X) * abx + (grid.Y - a.Y) * aby) / l2 : 0f;
                float tmin = 0f, tmax = 1f, len = sb - sa;
                if (len > 0f)
                {
                    if (lo > sa) tmin = (lo - sa) / len;
                    if (hi < sb) tmax = (hi - sa) / len;
                    if (tmin > tmax) continue;
                }
                t = t < tmin ? tmin : (t > tmax ? tmax : t);
                var p = new Vector2(a.X + abx * t, a.Y + aby * t);
                float d = Dist(p, grid);
                if (d < distance)
                {
                    distance = d;
                    best = _samples[i].S + (_samples[i + 1].S - _samples[i].S) * t;
                }
            }
            return best;
        }

        public float Clamp(float s)
        {
            return s < 0f ? 0f : (s > Length ? Length : s);
        }

        public static Vector2 Center(IntVector2 cell)
        {
            return new Vector2(cell.X + 0.5f, cell.Y + 0.5f);
        }

        public static float WrapAngle(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return (float)a;
        }

        public static float Dist(Vector2 a, Vector2 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
