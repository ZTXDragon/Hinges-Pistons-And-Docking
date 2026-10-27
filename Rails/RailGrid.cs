using System.Collections.Generic;
using Cosmoteer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class RailGrid
    {
        private static readonly HashSet<string> s_passableIds = new HashSet<string>
        {
            "ztx.hinges_pistons_docking.rail_cart",
            "ztx.hinges_pistons_docking.frame",
            "ztx.hinges_pistons_docking.frame_wedge",
            "ztx.hinges_pistons_docking.frame_1x2_wedge",
            "ztx.hinges_pistons_docking.frame_1x3_wedge",
            "ztx.hinges_pistons_docking.frame_tri",
            "ztx.hinges_pistons_docking.logic_wire_frame",
        };

        public static Part PartAt(Ship ship, IntVector2 cell)
        {
            return ship?.Parts[cell, PartRectType.Physical];
        }

        public static List<IntVector2> NeighbourDirs(Ship ship, IntVector2 cell)
        {
            var list = new List<IntVector2>(4);
            foreach (IntVector2 d in RailSides.Dirs)
                if (RailNetwork.IsRailPart(PartAt(ship, cell + d))) list.Add(d);
            return list;
        }

        public static IntVector2 UpOf(Ship ship, IntVector2 cell)
        {
            Part p = PartAt(ship, cell);
            if (p == null) return new IntVector2(0, -1);
            return UpOfPart(p);
        }

        public static IntVector2 UpOfPart(Part p)
        {
            OrthogonalRotation local = ZtxRailTerminalComponent.ForPart(p) != null ? OrthogonalRotation.Down : OrthogonalRotation.Up;
            return local.GetShipRelative(p.Rotation, p.FlipX).ToVector();
        }

        public static bool Blocked(Ship ship, IntVector2 cell)
        {
            Part p = PartAt(ship, cell);
            if (p == null || RailNetwork.IsRailPart(p)) return false;
            return !s_passableIds.Contains(p.Rules.ID.ToString());
        }

        public static bool Passable(Part p)
        {
            return p != null && s_passableIds.Contains(p.Rules.ID.ToString());
        }

        public static bool PassableId(string id)
        {
            return s_passableIds.Contains(id);
        }
    }
}
