using System.Collections.Generic;
using Cosmoteer.Crew;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Crew;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class AssemblyCrewHome
    {
        public static bool GoesFar(bool? bunkFar, bool standingFar) => bunkFar ?? standingFar;

        internal static Part BunkOf(CrewSoul soul, Ship ship)
        {
            Part p = soul?.CrewSource?.Part;
            if (p != null) return ReferenceEquals(p.Ship, ship) ? p : null;
            if (soul == null || !ReferenceEquals(soul.HomeShip, ship) || soul.CrewSourceLoc.X == int.MinValue) return null;
            Part at = ship.Parts[soul.CrewSourceLoc, PartRectType.Normal];
            return at != null && CrewSource.GetOnPart(at) != null ? at : null;
        }

        internal static Dictionary<CrewSoul, bool?> BunkSides(Ship shipA, HashSet<Part> far)
        {
            var sides = new Dictionary<CrewSoul, bool?>();
            foreach (CrewSoul soul in shipA.Crew.Assigned)
            {
                Part bunk = BunkOf(soul, shipA);
                sides[soul] = bunk == null ? (bool?)null : far.Contains(bunk);
            }
            return sides;
        }

        internal static int RebindBunks(IEnumerable<CrewSoul> souls)
        {
            int n = 0;
            foreach (CrewSoul soul in souls)
            {
                if (soul == null || soul.CrewSource != null || soul.HomeShip == null || soul.CrewSourceLoc.X == int.MinValue) continue;
                Part at = soul.HomeShip.Parts[soul.CrewSourceLoc, PartRectType.Normal];
                CrewSource cs = at == null ? null : CrewSource.GetOnPart(at);
                if (cs == null || cs.RemainingCapacity <= 0) continue;
                cs.AssignCrew(soul);
                n++;
            }
            return n;
        }

        internal static void MoveBunkCell(CrewSoul soul, GridTransform xf)
        {
            if (soul == null || xf.IsIdentity || soul.CrewSourceLoc.X == int.MinValue) return;
            ((CrewSource.ICrewSoulMembers)soul).CrewSourceLoc = xf.TransformCell(soul.CrewSourceLoc);
        }
    }
}
