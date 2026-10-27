using System.Collections.Generic;
using Cosmoteer;
using Halfling;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class StructuralAdjacency
    {
        public static List<Part> Neighbours(Ship ship, Part part, bool ignoreJointSeams = true, bool ignoreRailEdges = true)
        {
            var result = new List<Part>();
            NeighboursInto(ship, part, result, ignoreJointSeams, ignoreRailEdges);
            return result;
        }

        public static void NeighboursInto(Ship ship, Part part, List<Part> result, bool ignoreJointSeams = true, bool ignoreRailEdges = true)
        {
            result.Clear();
            if (ship == null || part == null) return;
            AdjacencyFlags mine = part.Rules.AllowedContiguity.RotateFlip(part.Rotation, part.FlipX);
            foreach ((IntVector2 cell, AdjacencyFlags edge) in part.PhysicalRect.GetAdjacentCellsWithAdjacency(mine))
            {
                Part other = ship.Parts[cell, PartRectType.Physical];
                if (other == null || other == part || result.Contains(other)) continue;
                if (ignoreRailEdges && IsRailEdge(part, other)) continue;
                AdjacencyFlags theirs = other.Rules.AllowedContiguity.RotateFlip(Mathx.Wrap(other.Rotation + 2, 4), other.FlipX);
                if ((edge & theirs) == AdjacencyFlags.None) continue;
                if (ignoreJointSeams && IsJointSeam(part, other)) continue;
                result.Add(other);
            }
        }

        public static bool TouchesRail(Ship ship, Part part)
        {
            if (ship == null || part == null || RailNetwork.IsRailPart(part)) return false;
            foreach (Part n in Neighbours(ship, part, ignoreJointSeams: true, ignoreRailEdges: false))
                if (RailNetwork.IsRailPart(n)) return true;
            return false;
        }

        public static bool IsRailEdge(Part p, Part q)
        {
            return RailNetwork.IsRailPart(p) != RailNetwork.IsRailPart(q);
        }

        public static bool IsJointSeam(Part p, Part q)
        {
            return FacesAsJoint(p, q) || FacesAsJoint(q, p);
        }

        private static bool FacesAsJoint(Part p, Part q)
        {
            if (p == null || q == null) return false;
            foreach (PartComponent c in p.Components)
            {
                if (c is ZtxHingeBaseComponent)
                {
                    IntVector2 baseArrow = OrthogonalRotation.Up.GetShipRelative(p.Rotation, p.FlipX).ToVector();
                    if (p.Location + baseArrow != q.Location) return false;
                    if (ZtxHingeTipComponent.ForPart(q) == null) return false;
                    IntVector2 tipArrow = OrthogonalRotation.Down.GetShipRelative(q.Rotation, q.FlipX).ToVector();
                    return q.Location + tipArrow == p.Location;
                }
                if (c is ZtxDockClampComponent)
                {
                    return p.Location + DockGeometry.FacingDir(p) == q.Location;
                }
            }
            return false;
        }
    }
}
