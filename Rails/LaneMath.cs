using System;
using System.Collections.Generic;
using System.Numerics;

namespace ZTX.HingesPistonsDocking
{
    internal static class LaneMath
    {
        public static bool InLane(IList<Vector2> a, IList<Vector2> b, Vector2 axis, float tolerance)
        {
            if (!Across(axis, out Vector2 across)) return true;
            Project(a, across, out float aMin, out float aMax);
            Project(b, across, out float bMin, out float bMax);
            return Overlap(aMin, aMax, bMin, bMax) > tolerance;
        }

        public static float Overlap(float aMin, float aMax, float bMin, float bMax) => Math.Min(aMax, bMax) - Math.Max(aMin, bMin);

        public static bool Across(Vector2 axis, out Vector2 across)
        {
            float len = axis.Length();
            across = len < 1e-6f ? Vector2.Zero : new Vector2(-axis.Y / len, axis.X / len);
            return len >= 1e-6f;
        }

        public static void Project(IList<Vector2> pts, Vector2 dir, out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;
            for (int i = 0; i < pts.Count; i++)
            {
                float d = Vector2.Dot(pts[i], dir);
                if (d < min) min = d;
                if (d > max) max = d;
            }
        }
    }
}
