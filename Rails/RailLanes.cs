using System;
using System.Collections.Generic;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class RailLanes
    {
        private static readonly IntVector2[] s_dirs =
        {
            new IntVector2(0, -1), new IntVector2(1, 0), new IntVector2(0, 1), new IntVector2(-1, 0),
        };

        private static readonly IntVector2 s_zero = new IntVector2(0, 0);

        public sealed class Links
        {
            public Func<IntVector2, IntVector2, IntVector2> Leave;
            public Func<IntVector2, IntVector2, IntVector2> Arrive;
            public Func<IntVector2, IntVector2, int, bool> Open;
            public Func<IntVector2, IntVector2, int> Cost;
        }

        private static IntVector2 LeaveOf(Links l, IntVector2 cur, IntVector2 n) => l?.Leave != null ? l.Leave(cur, n) : n - cur;
        private static IntVector2 ArriveOf(Links l, IntVector2 cur, IntVector2 n) => l?.Arrive != null ? l.Arrive(cur, n) : n - cur;
        private static bool OpenOf(Links l, IntVector2 cur, IntVector2 n, int side) => l?.Open == null || l.Open(cur, n, side);
        private static int CostOf(Links l, IntVector2 cur, IntVector2 n) => l?.Cost != null ? l.Cost(cur, n) : 1;

        public static IntVector2 Normal(IntVector2 d, int side)
        {
            return new IntVector2(-d.Y * side, d.X * side);
        }

        public static int SideAt(IntVector2 tile, IntVector2 d, Vector2 grid)
        {
            float ox = grid.X - (tile.X + 0.5f), oy = grid.Y - (tile.Y + 0.5f);
            float cross = d.X * oy - d.Y * ox;
            return cross > 0.2f ? 1 : (cross < -0.2f ? -1 : 0);
        }

        public static bool VertexFree(IntVector2 cur, IntVector2 dIn, IntVector2 dOut, int side, Func<IntVector2, bool> free,
            Func<IntVector2, IntVector2, bool> canCross = null, Func<IntVector2, IntVector2, IntVector2, bool> legOk = null)
        {
            bool hasIn = dIn != s_zero, hasOut = dOut != s_zero;
            if (!hasIn && !hasOut) return true;
            if (legOk != null)
            {
                if (hasIn && !legOk(cur, dIn, Normal(dIn, side))) return false;
                if (hasOut && !legOk(cur, dOut, Normal(dOut, side))) return false;
            }
            if (!hasIn) return free(cur + Normal(dOut, side)) || (canCross != null && canCross(cur, Normal(dOut, side)));
            if (!hasOut) return free(cur + Normal(dIn, side));
            IntVector2 nIn = Normal(dIn, side), nOut = Normal(dOut, side);
            if (dIn == dOut) return free(cur + nIn) || (canCross != null && canCross(cur, nIn));
            if (dOut.X == -dIn.X && dOut.Y == -dIn.Y) return false;
            int turn = dIn.X * dOut.Y - dIn.Y * dOut.X;
            if ((turn > 0) == (side > 0))
                return free(cur + nIn + nOut);
            return (free(cur + nIn) || (canCross != null && canCross(cur, nIn)))
                && free(cur + nIn + nOut)
                && (free(cur + nOut) || (canCross != null && canCross(cur, nOut)));
        }

        public const int REVERSE_COST = 4;

        public static List<IntVector2> Route(IntVector2 from, IntVector2? prev, int side, Vector2 grid, IntVector2 goal,
            IntVector2? forbidFirst, Func<IntVector2, IEnumerable<IntVector2>> neighbours, Func<IntVector2, bool> free,
            Func<IntVector2, IntVector2, bool> canCross, Func<IntVector2, IntVector2, IntVector2, bool> legOk, out int sideOut, out List<int> reversals)
        {
            return Route(null, from, prev, side, grid, goal, forbidFirst, neighbours, free, canCross, legOk, out sideOut, out reversals);
        }

        public static List<IntVector2> Route(Links links, IntVector2 from, IntVector2? prev, int side, Vector2 grid, IntVector2 goal,
            IntVector2? forbidFirst, Func<IntVector2, IEnumerable<IntVector2>> neighbours, Func<IntVector2, bool> free,
            Func<IntVector2, IntVector2, bool> canCross, Func<IntVector2, IntVector2, IntVector2, bool> legOk, out int sideOut, out List<int> reversals)
        {
            sideOut = side;
            reversals = new List<int>();
            if (from == goal && !prev.HasValue) return new List<IntVector2> { from };
            bool allowReverse = !prev.HasValue;
            var dist = new Dictionary<(IntVector2, int, int), int>();
            var parent = new Dictionary<(IntVector2, int, int), (IntVector2, int, int)>();
            var startMark = (from, -1, 0);
            var pq = new PriorityQueue<(IntVector2 Tile, int Dir, int Side), int>();
            void Relax((IntVector2, int, int) st, int cost, (IntVector2, int, int) from_)
            {
                if (dist.TryGetValue(st, out int old) && old <= cost) return;
                dist[st] = cost;
                parent[st] = from_;
                pq.Enqueue(st, cost);
            }
            IntVector2 dIn0 = prev.HasValue ? ArriveOf(links, prev.Value, from) : s_zero;
            foreach (IntVector2 n in neighbours(from))
            {
                if (forbidFirst.HasValue && n == forbidFirst.Value) continue;
                IntVector2 d = LeaveOf(links, from, n);
                int s = side != 0 ? side : SideAt(from, d, grid);
                if (s == 0 || !VertexFree(from, dIn0, d, s, free, canCross, legOk)) continue;
                if (!OpenOf(links, from, n, s)) continue;
                Relax((n, DirIndex(ArriveOf(links, from, n)), s), CostOf(links, from, n), startMark);
            }
            while (pq.TryDequeue(out var cur, out int cost))
            {
                if (dist.TryGetValue(cur, out int best) && cost > best) continue;
                if (cur.Side != 0)
                {
                    IntVector2 dIn = s_dirs[cur.Dir & 3];
                    bool freshFromTurnRound = cur.Dir >= 4;
                    bool canStop = VertexFree(cur.Tile, dIn, s_zero, cur.Side, free, null, legOk);
                    if (cur.Tile == goal && canStop)
                    {
                        var states = new List<(IntVector2 Tile, int Dir, int Side)>();
                        for (var k = (cur.Tile, cur.Dir, cur.Side); k != startMark; k = parent[k]) states.Add(k);
                        states.Reverse();
                        var path = new List<IntVector2> { from };
                        foreach (var st in states)
                        {
                            if (st.Side == 0) reversals.Add(path.Count - 1);
                            else path.Add(st.Tile);
                        }
                        sideOut = states.Count > 0 ? states[0].Side : side;
                        return path;
                    }
                    foreach (IntVector2 n in neighbours(cur.Tile))
                    {
                        IntVector2 dOut = LeaveOf(links, cur.Tile, n);
                        if (freshFromTurnRound && dOut != dIn) continue;
                        if (!VertexFree(cur.Tile, dIn, dOut, cur.Side, free, canCross, legOk)) continue;
                        if (!OpenOf(links, cur.Tile, n, cur.Side)) continue;
                        Relax((n, DirIndex(ArriveOf(links, cur.Tile, n)), cur.Side), cost + CostOf(links, cur.Tile, n), cur);
                    }
                    var arrivedFrom = parent[cur];
                    bool straight = arrivedFrom.Item3 == 0 || (arrivedFrom.Item2 & 3) == (cur.Dir & 3);
                    if (allowReverse && canStop && straight)
                        Relax((cur.Tile, DirIndex(Normal(dIn, cur.Side)), 0), cost + REVERSE_COST, cur);
                }
                else
                {
                    IntVector2 off = s_dirs[cur.Dir];
                    foreach (IntVector2 n in neighbours(cur.Tile))
                    {
                        IntVector2 dOut = LeaveOf(links, cur.Tile, n);
                        int s = Math.Sign(dOut.X * off.Y - dOut.Y * off.X);
                        if (s == 0 || !VertexFree(cur.Tile, s_zero, dOut, s, free, canCross, legOk)) continue;
                        if (!OpenOf(links, cur.Tile, n, s)) continue;
                        Relax((n, DirIndex(ArriveOf(links, cur.Tile, n)) + 4, s), cost + CostOf(links, cur.Tile, n), cur);
                    }
                }
            }
            return null;
        }

        public static List<IntVector2> Walk(IntVector2 from, IntVector2? prev, int side, Vector2 grid, int wantSide, bool allowReverse,
            Func<IntVector2, IEnumerable<IntVector2>> neighbours, Func<IntVector2, bool> free, Func<IntVector2, IntVector2, bool> canCross,
            Func<IntVector2, IntVector2, IntVector2, bool> legOk, Func<IntVector2, bool> isStation, int maxSteps, out IntVector2 goal, out int sideOut)
        {
            return Walk(null, from, prev, side, grid, wantSide, allowReverse, neighbours, free, canCross, legOk, isStation, maxSteps, out goal, out sideOut);
        }

        public static List<IntVector2> Walk(Links links, IntVector2 from, IntVector2? prev, int side, Vector2 grid, int wantSide, bool allowReverse,
            Func<IntVector2, IEnumerable<IntVector2>> neighbours, Func<IntVector2, bool> free, Func<IntVector2, IntVector2, bool> canCross,
            Func<IntVector2, IntVector2, IntVector2, bool> legOk, Func<IntVector2, bool> isStation, int maxSteps, out IntVector2 goal, out int sideOut)
        {
            goal = from;
            sideOut = side;
            var tiles = new List<IntVector2> { from };
            IntVector2 cur = from;
            IntVector2 dIn = prev.HasValue ? ArriveOf(links, prev.Value, from) : s_zero;
            int s = side;
            for (int step = 0; step <= maxSteps; step++)
            {
                bool first = step == 0;
                (IntVector2 Tile, int Side)? next = null, turnPick = null, uturn = null, anyFresh = null, wantFresh = null;
                foreach (IntVector2 n in neighbours(cur))
                {
                    IntVector2 d = LeaveOf(links, cur, n);
                    int sc = s != 0 ? s : SideAt(cur, d, grid);
                    if (sc == 0) continue;
                    bool back = dIn != s_zero && d.X == -dIn.X && d.Y == -dIn.Y;
                    if (!VertexFree(cur, dIn, d, sc, free, canCross, legOk)) continue;
                    if (!OpenOf(links, cur, n, sc)) continue;
                    if (dIn == s_zero)
                    {
                        if (anyFresh == null) anyFresh = (n, sc);
                        if (sc == wantSide && wantFresh == null) wantFresh = (n, sc);
                        continue;
                    }
                    if (back) { uturn = (n, sc); continue; }
                    if (d == dIn) { next = (n, sc); break; }
                    int turn = dIn.X * d.Y - dIn.Y * d.X;
                    if (turnPick == null || (turn > 0) == (sc > 0)) turnPick = (n, sc);
                }
                if (dIn == s_zero) next = wantFresh ?? anyFresh;
                else if (next == null) next = turnPick ?? uturn;
                if (next == null && first && allowReverse && prev.HasValue)
                {
                    IntVector2 d = LeaveOf(links, cur, prev.Value);
                    int sc = SideAt(cur, d, grid);
                    if (sc != 0 && VertexFree(cur, s_zero, d, sc, free, canCross, legOk) && OpenOf(links, cur, prev.Value, sc)) next = (prev.Value, sc);
                }
                if (next == null)
                {
                    if (tiles.Count < 2) return null;
                    if (!VertexFree(cur, dIn, s_zero, s, free, null, legOk)) { tiles.RemoveAt(tiles.Count - 1); cur = tiles[tiles.Count - 1]; }
                    if (tiles.Count < 2) return null;
                    goal = cur;
                    sideOut = s;
                    return tiles;
                }
                s = next.Value.Side;
                IntVector2 nv = next.Value.Tile;
                dIn = ArriveOf(links, cur, nv);
                tiles.Add(nv);
                cur = nv;
                if (isStation(nv) || nv == from)
                {
                    if (!VertexFree(nv, dIn, s_zero, s, free, null, legOk)) continue;
                    goal = nv;
                    sideOut = s;
                    return tiles;
                }
            }
            goal = cur;
            sideOut = s;
            return tiles;
        }

        private static int DirIndex(IntVector2 d)
        {
            for (int i = 0; i < 4; i++)
                if (s_dirs[i] == d) return i;
            return 0;
        }

        public sealed class View
        {
            private readonly RailNetwork _net;
            private readonly Ship _carrier;
            private readonly Dictionary<IntVector2, Part> _byCell = new Dictionary<IntVector2, Part>();

            public readonly Links Links;

            public View(RailNetwork net, Ship carrier)
            {
                _net = net;
                _carrier = carrier;
                foreach (Part t in net.Tiles) _byCell[t.Location] = t;
                Links = new Links { Leave = LeaveDir, Arrive = ArriveDir, Open = StepOpen, Cost = StepCost };
            }

            private IntVector2 LeaveDir(IntVector2 cur, IntVector2 n)
            {
                RailArc.Shape a = _net.ArcBetween(cur, n);
                return a != null ? a.OpenOf(cur) : n - cur;
            }

            private IntVector2 ArriveDir(IntVector2 cur, IntVector2 n)
            {
                RailArc.Shape a = _net.ArcBetween(cur, n);
                return a != null ? RailArc.Neg(a.OpenOf(n)) : n - cur;
            }

            private bool StepOpen(IntVector2 cur, IntVector2 n, int side)
            {
                RailArc.Shape a = _net.ArcBetween(cur, n);
                if (a == null) return true;
                foreach (IntVector2 c in a.TrackCells) if (!Free(c)) return false;
                foreach (IntVector2 c in a.LaneCells(a.InsideFor(cur == a.A, side))) if (!Free(c)) return false;
                return true;
            }

            private int StepCost(IntVector2 cur, IntVector2 n)
            {
                RailArc.Shape a = _net.ArcBetween(cur, n);
                return a == null ? 1 : (int)Math.Round(a.TrackLength + 1f);
            }

            public Part PartAt(IntVector2 cell)
            {
                return _byCell.TryGetValue(cell, out Part p) ? p : null;
            }

            public IEnumerable<IntVector2> Neighbours(IntVector2 cell)
            {
                Part p = PartAt(cell);
                if (p == null) yield break;
                foreach (Part n in _net.NeighboursOf(p))
                    if (RailNetwork.IsOpen(n)) yield return n.Location;
            }

            public bool Free(IntVector2 cell)
            {
                if (_byCell.ContainsKey(cell)) return false;
                return !RailGrid.Blocked(_carrier, cell) && !RailNetwork.IsRailPart(RailGrid.PartAt(_carrier, cell));
            }

            public bool LegOk(IntVector2 tile, IntVector2 d, IntVector2 laneOffset)
            {
                if (!_byCell.ContainsKey(tile)) return true;
                if (ZtxRailTerminalComponent.ForPart(_byCell[tile]) != null) return true;
                return RailSides.LegAllowed(RailSides.KindOf(RailGrid.NeighbourDirs(_carrier, tile)), RailGrid.UpOf(_carrier, tile), d, laneOffset);
            }

            public bool CanCross(IntVector2 tile, IntVector2 n)
            {
                Part p = PartAt(tile);
                if (p == null || RailSides.KindOf(RailGrid.NeighbourDirs(_carrier, tile)) != RailSides.Kind.Crossing) return false;
                Part mouth = PartAt(tile + n);
                return mouth != null && _net.NeighboursOf(p).Contains(mouth);
            }

            public bool IsStation(IntVector2 cell)
            {
                return ZtxRailTileComponent.ForPart(PartAt(cell))?.IsStop ?? false;
            }

            public List<Part> ToParts(List<IntVector2> cells)
            {
                if (cells == null) return null;
                var parts = new List<Part>(cells.Count);
                foreach (IntVector2 c in cells)
                {
                    Part p = PartAt(c);
                    if (p == null) return null;
                    parts.Add(p);
                }
                return parts;
            }
        }

        public static List<Part> Route(View view, Part from, Part prev, int side, Vector2 grid, Part goal, Part forbidFirst,
            out int sideOut, out bool toTurnRound)
        {
            sideOut = side;
            toTurnRound = false;
            if (from == null || goal == null || view.PartAt(from.Location) == null || view.PartAt(goal.Location) == null) return null;
            List<IntVector2> cells = Route(view.Links, from.Location, prev?.Location, side, grid, goal.Location, forbidFirst?.Location,
                view.Neighbours, view.Free, view.CanCross, view.LegOk, out sideOut, out List<int> reversals);
            if (cells != null && reversals.Count > 0)
            {
                toTurnRound = true;
                cells = cells.GetRange(0, reversals[0] + 1);
            }
            return view.ToParts(cells);
        }

        public static List<Part> Walk(View view, Part from, Part prev, int side, Vector2 grid, int wantSide, bool allowReverse, int maxSteps,
            out Part goal, out int sideOut)
        {
            goal = null;
            List<IntVector2> cells = Walk(view.Links, from.Location, prev?.Location, side, grid, wantSide, allowReverse,
                view.Neighbours, view.Free, view.CanCross, view.LegOk, view.IsStation, maxSteps, out IntVector2 g, out sideOut);
            List<Part> parts = view.ToParts(cells);
            if (parts != null) goal = view.PartAt(g);
            return parts;
        }

        public static bool PathFree(View view, List<Part> tiles, int side, int fromIndex)
        {
            Links links = view.Links;
            for (int i = Math.Max(0, fromIndex); i < tiles.Count; i++)
            {
                IntVector2 dIn = i > 0 ? ArriveOf(links, tiles[i - 1].Location, tiles[i].Location) : s_zero;
                IntVector2 dOut = i + 1 < tiles.Count ? LeaveOf(links, tiles[i].Location, tiles[i + 1].Location) : s_zero;
                if (!VertexFree(tiles[i].Location, dIn, dOut, side, view.Free, view.CanCross, view.LegOk)) return false;
            }
            for (int i = Math.Max(1, fromIndex); i < tiles.Count; i++)
                if (!OpenOf(links, tiles[i - 1].Location, tiles[i].Location, side)) return false;
            return true;
        }
    }
}
