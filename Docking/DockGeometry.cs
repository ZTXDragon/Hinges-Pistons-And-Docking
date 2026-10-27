using System;
using Cosmoteer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class DockGeometry
    {
        public const float MAX_FACE_DISTANCE_CELLS = 0.7f;
        public const float MAX_FACE_ANGLE = 0.5f;
        public const float MAX_RELATIVE_SPEED = 1.5f;
        public const float MAX_RELATIVE_SPIN = 0.5f;

        public static IntVector2 FacingDir(Part part)
        {
            return OrthogonalRotation.Up.GetShipRelative(part.Rotation, part.FlipX).ToVector();
        }

        public static Vector2 CellCenter(IntVector2 cell)
        {
            return new Vector2(cell.X + 0.5f, cell.Y + 0.5f);
        }

        public static Vector2 EdgeMidGrid(Part part)
        {
            IntVector2 d = FacingDir(part);
            return CellCenter(part.Location) + new Vector2(d.X, d.Y) * 0.5f;
        }

        public static Vector2 EdgeMidWorld(Ship ship, Part part)
        {
            return ship.Physics.Body.GetWorldPoint(EdgeMidGrid(part) * ship.Scale);
        }

        public static float OpposingRefAngle(Ship a, Part partA, Ship b, Part partB, out float angleError)
        {
            IntVector2 dA = FacingDir(partA);
            IntVector2 dB = FacingDir(partB);
            float angA = (float)DetMath.Atan2(dA.Y, dA.X);
            float angB = (float)DetMath.Atan2(dB.Y, dB.X);
            float targetRel = angA - angB + (float)Math.PI;
            float currentRel = b.Physics.Body.Rotation - a.Physics.Body.Rotation;
            float refAngle = targetRel + (float)(Math.Round((currentRel - targetRel) / (2.0 * Math.PI)) * 2.0 * Math.PI);
            angleError = Math.Abs(refAngle - currentRel);
            return refAngle;
        }

        public static bool IsRelativeMotionLow(Ship a, Ship b, Vector2 worldPoint)
        {
            var bodyA = a.Physics.Body;
            var bodyB = b.Physics.Body;
            Vector2 relVel = bodyA.GetLinearVelocityFromWorldPoint(worldPoint)
                           - bodyB.GetLinearVelocityFromWorldPoint(worldPoint);
            if (relVel.Length > MAX_RELATIVE_SPEED) return false;
            return Math.Abs(bodyA.AngularVelocity - bodyB.AngularVelocity) <= MAX_RELATIVE_SPIN;
        }

        public static bool MightReach(Ship other, Vector2 worldPoint)
        {
            Vector2 c = other.Physics.Body.WorldCenter;
            return (c - worldPoint).Length <= other.WorldBoundingRadius + 2f;
        }

        public static float WrapAngle(float a)
        {
            return a - (float)(Math.Round(a / (2.0 * Math.PI)) * 2.0 * Math.PI);
        }
    }
}
