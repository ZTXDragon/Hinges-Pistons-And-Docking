using System;
using System.Collections.Generic;
using Cosmoteer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Blueprints.Graphics;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation.Overlays;
using Halfling.Geometry;
using Halfling.Graphics;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class RailArcOverlay : IBlueprintOverlayRenderer
    {
        private struct Seg
        {
            public Vector2 A, B;
            public Color Color;
        }

        private static readonly Color s_open = new Color(0.35f, 0.85f, 0.35f, 0.9f);
        private static readonly Color s_blocked = new Color(0.95f, 0.70f, 0.20f, 0.9f);
        private static readonly Color s_miss = new Color(0.90f, 0.25f, 0.25f, 0.9f);
        private static readonly IntVector2[] s_dirs = { new IntVector2(0, -1), new IntVector2(1, 0), new IntVector2(0, 1), new IntVector2(-1, 0) };
        private static bool s_failed;

        private readonly List<Seg> _segs = new List<Seg>();
        private CappedLine _line;

        public void RefreshData(Ship ship, IReadOnlyList<PartOverlayRenderData> parts, IBlueprintOverlaySettingsProvider settings, BlueprintOverlayFlags flags)
        {
            _segs.Clear();
            if (s_failed || ship == null || parts == null) return;
            try
            {
                var focus = new HashSet<IntVector2>();
                ZtxRailTileRules tileRules = null;
                _line = null;
                foreach (PartOverlayRenderData data in parts)
                {
                    PartRules r = data.Info.Rules;
                    ZtxRailArcRules arc = Find<ZtxRailArcRules>(r);
                    ZtxRailTileRules tile = Find<ZtxRailTileRules>(r);
                    if (arc == null || tile == null || !tile.IsCurve) continue;
                    focus.Add(data.Info.Location);
                    tileRules ??= tile;
                    _line ??= arc.PreviewLine;
                }
                if (focus.Count == 0 || _line == null) return;

                var rail = new Dictionary<IntVector2, IntVector2>();
                var curves = new HashSet<IntVector2>();
                var blocked = new HashSet<IntVector2>();
                void Add(PartRules r, IntVector2 loc, int rot, bool flip)
                {
                    if (r == null) return;
                    ZtxRailTileRules t = Find<ZtxRailTileRules>(r);
                    bool terminal = Find<ZtxRailTerminalRules>(r) != null;
                    if (t != null || terminal)
                    {
                        rail[loc] = (terminal ? OrthogonalRotation.Down : OrthogonalRotation.Up).GetShipRelative(rot, flip).ToVector();
                        if (t != null && t.IsCurve) curves.Add(loc);
                        return;
                    }
                    if (RailGrid.PassableId(r.ID.ToString())) return;
                    IntRect rect = r.GetRect(loc, rot);
                    for (int x = rect.Left; x < rect.Right; x++)
                        for (int y = rect.Top; y < rect.Bottom; y++)
                            blocked.Add(new IntVector2(x, y));
                }
                foreach (Part p in ship.Parts) Add(p.Rules, p.Location, p.Rotation, p.FlipX);
                foreach (BlueprintPart bp in ship.BlueprintParts) Add(bp.Rules, bp.Location, bp.Rotation, bp.FlipX);
                foreach (PartOverlayRenderData data in parts) if (data.Part == null) Add(data.Info.Rules, data.Info.Location, data.Info.Rotation, data.Info.FlipX);

                IReadOnlyList<IntVector2> Nb(IntVector2 c)
                {
                    var list = new List<IntVector2>(4);
                    foreach (IntVector2 d in s_dirs) if (rail.ContainsKey(c + d)) list.Add(d);
                    return list;
                }
                List<RailArc.Shape> pairs = RailArc.FindPairs(curves, Nb, c => rail[c], tileRules.CurveMinDistance, tileRules.CurveMaxDistance, out _, true);
                var paired = new HashSet<IntVector2>();
                foreach (RailArc.Shape s in pairs)
                {
                    paired.Add(s.A);
                    paired.Add(s.B);
                    if (!focus.Contains(s.A) && !focus.Contains(s.B)) continue;
                    var bad = new List<IntVector2>();
                    foreach (IntVector2 c in s.TrackCells) if (blocked.Contains(c) || rail.ContainsKey(c)) bad.Add(c);
                    foreach (IntVector2 c in s.LaneCells(s.Inside)) if ((blocked.Contains(c) || rail.ContainsKey(c)) && !bad.Contains(c)) bad.Add(c);
                    Color col = bad.Count == 0 ? s_open : s_blocked;
                    List<Vector2> pts = RailArc.TrackPolyline(s, 0.25f);
                    for (int i = 0; i + 1 < pts.Count; i++) _segs.Add(new Seg { A = pts[i], B = pts[i + 1], Color = col });
                    foreach (IntVector2 c in bad)
                    {
                        Vector2 a = new Vector2(c.X, c.Y), b = new Vector2(c.X + 1, c.Y), cc = new Vector2(c.X + 1, c.Y + 1), d = new Vector2(c.X, c.Y + 1);
                        _segs.Add(new Seg { A = a, B = b, Color = s_blocked });
                        _segs.Add(new Seg { A = b, B = cc, Color = s_blocked });
                        _segs.Add(new Seg { A = cc, B = d, Color = s_blocked });
                        _segs.Add(new Seg { A = d, B = a, Color = s_blocked });
                    }
                }
                foreach (IntVector2 f in focus)
                {
                    if (paired.Contains(f)) continue;
                    IntVector2? near = null;
                    int best = int.MaxValue;
                    foreach (IntVector2 c in curves)
                    {
                        if (c == f) continue;
                        int dx = Math.Abs(c.X - f.X), dy = Math.Abs(c.Y - f.Y);
                        if (dx > tileRules.CurveMaxDistance + 1 || dy > tileRules.CurveMaxDistance + 1 || dx + dy >= best) continue;
                        best = dx + dy;
                        near = c;
                    }
                    if (!near.HasValue) continue;
                    Vector2 p0 = new Vector2(f.X + 0.5f, f.Y + 0.5f), p1 = new Vector2(near.Value.X + 0.5f, near.Value.Y + 0.5f);
                    const int DASHES = 8;
                    for (int i = 0; i < DASHES; i += 2)
                    {
                        float t0 = (float)i / DASHES, t1 = (float)(i + 1) / DASHES;
                        _segs.Add(new Seg { A = p0 + (p1 - p0) * t0, B = p0 + (p1 - p0) * t1, Color = s_miss });
                    }
                }
            }
            catch (Exception ex)
            {
                s_failed = true;
                _segs.Clear();
                Log.Exception("RailArcOverlay.RefreshData", ex);
            }
        }

        private static T Find<T>(PartRules rules) where T : PartComponentRules
        {
            if (rules == null) return null;
            foreach (PartComponentRules c in rules.Components)
                if (c is T t) return t;
            return null;
        }

        public void DrawEarly(Ship ship, IBlueprintOverlaySettingsProvider settings) { }

        public void Draw(Ship ship, IBlueprintOverlaySettingsProvider settings) { }

        public void DrawLate(Ship ship, IBlueprintOverlaySettingsProvider settings)
        {
            if (_line == null) return;
            foreach (Seg s in _segs) _line.Draw(s.A, s.B, s.Color);
        }

        public void DrawLimited(Ship ship, IBlueprintOverlaySettingsProvider settings) { }

        public void Dispose() { }
    }
}
