using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class CutOrders
    {
        public static float[] OffsetDestination(float destX, float destY, float finalAnchorRot,
            float anchorX, float anchorY, float anchorRot, float segX, float segY, float segRot)
        {
            double dx = segX - anchorX, dy = segY - anchorY;
            double back = -anchorRot;
            double lx = dx * DetMath.Cos(back) - dy * DetMath.Sin(back);
            double ly = dx * DetMath.Sin(back) + dy * DetMath.Cos(back);
            double c = DetMath.Cos(finalAnchorRot), s = DetMath.Sin(finalAnchorRot);
            return new[]
            {
                (float)(destX + lx * c - ly * s),
                (float)(destY + lx * s + ly * c),
                finalAnchorRot + (segRot - anchorRot),
            };
        }

        public static Command MoveForCutPiece(MoveCommand m, Ship oldAnchor, Ship piece, CommandID id)
        {
            if (m?.Location == null || oldAnchor?.Physics?.Body == null || piece?.Physics?.Body == null) return null;
            Vector2 dest = m.Location.Value;
            Vector2 anchorPos = oldAnchor.DetWorldCenter;
            float anchorRot = oldAnchor.DetRotation;
            float finalRot;
            Direction? world = m.GetWorldRotation();
            if (world.HasValue)
            {
                finalRot = world.Value;
            }
            else
            {
                Vector2 travel = dest - anchorPos;
                finalRot = travel.Length > oldAnchor.WorldBoundingRadius
                    ? (float)DetMath.Atan2(travel.Y, travel.X) - (float)oldAnchor.FlightDirection
                    : anchorRot;
            }
            Vector2 segPos = piece.DetWorldCenter;
            float[] r = OffsetDestination(dest.X, dest.Y, finalRot, anchorPos.X, anchorPos.Y, anchorRot, segPos.X, segPos.Y, piece.DetRotation);
            return new MoveCommand(piece, id, new Vector2(r[0], r[1]), (Direction)r[2], m.CollisionAvoidanceFlags, isTentative: false, forceDestinationRotation: false)
            {
                CollisionAvoidanceFlags = m.CollisionAvoidanceFlags,
                WaitForCrew = m.WaitForCrew,
            };
        }
    }
}
