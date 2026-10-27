using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class RailCurves
    {
        private static readonly ConditionalWeakTable<Ship, HashSet<Part>> s_byShip = new ConditionalWeakTable<Ship, HashSet<Part>>();

        public static void Track(Part part, bool add)
        {
            Ship ship = part?.Ship;
            if (ship == null) return;
            HashSet<Part> set = s_byShip.GetOrCreateValue(ship);
            if (add) set.Add(part);
            else set.Remove(part);
        }

        public static List<Part> Parts(Ship ship)
        {
            var list = new List<Part>();
            if (ship != null && s_byShip.TryGetValue(ship, out HashSet<Part> set))
                foreach (Part p in set)
                    if (p.Ship == ship) list.Add(p);
            return list;
        }

        public static ZtxRailTileRules RulesOf(Ship ship)
        {
            foreach (Part p in Parts(ship))
            {
                ZtxRailTileComponent t = ZtxRailTileComponent.ForPart(p);
                if (t != null) return t.Rules;
            }
            return null;
        }

        public static List<RailArc.Shape> Pairs(Ship ship, out Dictionary<IntVector2, string> why, bool drawn = false)
        {
            var cells = new List<IntVector2>();
            foreach (Part p in Parts(ship)) cells.Add(p.Location);
            ZtxRailTileRules r = RulesOf(ship);
            return RailArc.FindPairs(cells, c => RailGrid.NeighbourDirs(ship, c), c => RailGrid.UpOf(ship, c),
                r?.CurveMinDistance ?? 2, r?.CurveMaxDistance ?? 6, out why, drawn);
        }
    }
}
