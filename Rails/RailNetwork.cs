using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class RailNetwork
    {
        private static readonly IntVector2[] s_dirs =
        {
            new IntVector2(0, -1), new IntVector2(1, 0), new IntVector2(0, 1), new IntVector2(-1, 0),
        };

        private static readonly ID<PartToggleGuiRules> s_name1Id = new ID<PartToggleGuiRules>("ztx_rail_name_1");
        private static readonly ID<PartToggleGuiRules> s_name2Id = new ID<PartToggleGuiRules>("ztx_rail_name_2");
        private static readonly ID<PartToggleGuiRules> s_switchId = new ID<PartToggleGuiRules>("ztx_rail_switch");

        public Part Terminal;

        public readonly List<Part> Tiles = new List<Part>();

        public readonly List<Part> Stations = new List<Part>();

        private readonly Dictionary<Part, List<Part>> _adj = new Dictionary<Part, List<Part>>();

        private readonly Dictionary<IntVector2, RailArc.Shape> _arcByCell = new Dictionary<IntVector2, RailArc.Shape>();
        private readonly List<RailArc.Shape> _arcs = new List<RailArc.Shape>();
        private readonly Dictionary<IntVector2, Part> _byCell = new Dictionary<IntVector2, Part>();
        public IReadOnlyList<RailArc.Shape> Arcs => _arcs;
        public Dictionary<IntVector2, string> CurveReasons { get; private set; } = new Dictionary<IntVector2, string>();

        public Part OtherTerminal;

        public bool IsEmpty
        {
            get { return Tiles.Count < 2; }
        }

        public void Scan(Ship ship, Part terminal)
        {
            Tiles.Clear();
            Stations.Clear();
            _adj.Clear();
            OtherTerminal = null;
            Terminal = terminal;
            _arcByCell.Clear();
            _arcs.Clear();
            _byCell.Clear();
            if (ship != null)
            {
                List<RailArc.Shape> pairs = RailCurves.Pairs(ship, out Dictionary<IntVector2, string> why);
                foreach (RailArc.Shape s in pairs)
                {
                    _arcByCell[s.A] = s;
                    _arcByCell[s.B] = s;
                }
                CurveReasons = why;
            }
            if (ship == null || terminal == null) return;

            var queue = new Queue<Part>();
            var seen = new HashSet<Part> { terminal };
            queue.Enqueue(terminal);
            while (queue.Count > 0)
            {
                Part cur = queue.Dequeue();
                Tiles.Add(cur);
                var list = new List<Part>(4);
                foreach (IntVector2 d in s_dirs)
                {
                    Part n = ship.Parts[cur.Location + d, PartRectType.Physical];
                    if (!IsRailPart(n)) continue;
                    list.Add(n);
                    if (seen.Add(n)) queue.Enqueue(n);
                }
                if (_arcByCell.TryGetValue(cur.Location, out RailArc.Shape arc))
                {
                    Part partner = ship.Parts[arc.Other(cur.Location), PartRectType.Physical];
                    if (IsRailPart(partner))
                    {
                        list.Add(partner);
                        if (seen.Add(partner)) queue.Enqueue(partner);
                    }
                }
                _byCell[cur.Location] = cur;
                _adj[cur] = list;
                if (cur != terminal && ZtxRailTerminalComponent.ForPart(cur) != null && OtherTerminal == null)
                    OtherTerminal = cur;
                if (ZtxRailTileComponent.ForPart(cur)?.IsStop ?? false)
                    Stations.Add(cur);
            }
            foreach (RailArc.Shape s in new HashSet<RailArc.Shape>(_arcByCell.Values))
                if (_byCell.ContainsKey(s.A) && _byCell.ContainsKey(s.B)) _arcs.Add(s);
            Stations.Sort((a, b) => RailCartRules.CompareNames(DisplayName(a), DisplayName(b)));
        }

        public bool Contains(Part p)
        {
            return p != null && _adj.ContainsKey(p);
        }

        public RailArc.Shape ArcBetween(IntVector2 a, IntVector2 b)
        {
            return _arcByCell.TryGetValue(a, out RailArc.Shape s) && s.Other(a) == b && s.Has(b) ? s : null;
        }

        public IntVector2 LeaveDir(Part a, Part b)
        {
            RailArc.Shape s = ArcBetween(a.Location, b.Location);
            return s != null ? s.OpenOf(a.Location) : b.Location - a.Location;
        }

        public IntVector2 ArriveDir(Part a, Part b)
        {
            RailArc.Shape s = ArcBetween(a.Location, b.Location);
            return s != null ? RailArc.Neg(s.OpenOf(b.Location)) : b.Location - a.Location;
        }

        public List<Part> NeighboursOf(Part p)
        {
            return p != null && _adj.TryGetValue(p, out List<Part> list) ? list : s_none;
        }

        private static readonly List<Part> s_none = new List<Part>();

        public static bool IsRailPart(Part p)
        {
            return p != null && (ZtxRailTileComponent.ForPart(p) != null || ZtxRailTerminalComponent.ForPart(p) != null);
        }

        public static bool IsOpen(Part p)
        {
            if (p == null) return false;
            if (!(ZtxRailTileComponent.ForPart(p)?.IsSwitch ?? false)) return true;
            bool? signal = LogicSource.Read(p);
            if (signal != null) return signal.Value;
            return LogicSource.IsOn(p, s_switchId);
        }

        public static string StationName(Part stop)
        {
            if (stop == null) return "";
            int n1 = LogicSource.Mode(stop, s_name1Id, 0);
            int n2 = LogicSource.Mode(stop, s_name2Id, 0);
            return (LogicRuleDef.UnpackName(n1) + LogicRuleDef.UnpackName(n2)).TrimEnd();
        }

        public static string DisplayName(Part stop)
        {
            string n = StationName(stop);
            if (n.Length > 0) return n;
            IntVector2 at = stop.Location;
            if (stop.Ship != null && MergeService.TryMapLiveCell(stop.Ship, at, out IntVector2 home)) at = home;
            return "STOP " + at.X + "," + at.Y;
        }

        public Part FindStation(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string key = Key(name);
            foreach (Part s in Stations)
                if (Key(DisplayName(s)) == key) return s;
            return null;
        }

        public static string Key(string name)
        {
            name = name ?? "";
            if (name.StartsWith("STOP ", StringComparison.OrdinalIgnoreCase)) name = "S" + name.Substring(5);
            return LogicRuleDef.Clean(name);
        }

        public Part NearestTile(Vector2 grid, out float dist)
        {
            Part best = null;
            dist = float.MaxValue;
            foreach (Part t in Tiles)
            {
                Vector2 c = RailRoute.Center(t.Location);
                float d = RailRoute.Dist(c, grid);
                if (d < dist) { dist = d; best = t; }
            }
            foreach (RailArc.Shape a in _arcs)
            {
                float d = a.Distance(grid);
                if (d >= dist) continue;
                IntVector2 end = RailRoute.Dist(RailRoute.Center(a.A), grid) <= RailRoute.Dist(RailRoute.Center(a.B), grid) ? a.A : a.B;
                if (_byCell.TryGetValue(end, out Part p)) { dist = d; best = p; }
            }
            return best;
        }

        public List<Part> Route(Part from, Part to, Part forbidFirst)
        {
            if (from == null || to == null || !Contains(from) || !Contains(to)) return null;
            if (from == to) return new List<Part> { from };
            var prev = new Dictionary<Part, Part> { [from] = null };
            var queue = new Queue<Part>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                Part cur = queue.Dequeue();
                foreach (Part n in NeighboursOf(cur))
                {
                    if (prev.ContainsKey(n)) continue;
                    if (cur == from && n == forbidFirst) continue;
                    if (n != to && !IsOpen(n)) continue;
                    if (n == to && !IsOpen(n)) continue;
                    prev[n] = cur;
                    if (n == to)
                    {
                        var path = new List<Part>();
                        for (Part p = to; p != null; p = prev[p]) path.Add(p);
                        path.Reverse();
                        return path;
                    }
                    queue.Enqueue(n);
                }
            }
            return null;
        }
    }
}
