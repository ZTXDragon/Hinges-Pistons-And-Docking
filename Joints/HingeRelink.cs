using System;

namespace ZTX.HingesPistonsDocking
{
    internal static class HingeRelink
    {
        public static readonly float[] PROBE_DEGREES = { 0f, 45f, -45f, 90f, -90f };

        public const float PROBE_RADIUS = 0.3f;

        public const float EDGE_TOLERANCE = 0.15f;

        public static bool AcceptsSwing(float jointAngle, float lower, float upper, float margin)
        {
            return jointAngle >= Math.Min(lower, 0f) - margin && jointAngle <= Math.Max(upper, 0f) + margin;
        }

        public static bool EdgeOnPivot(float dx, float dy)
        {
            return dx * dx + dy * dy <= EDGE_TOLERANCE * EDGE_TOLERANCE;
        }

        public static float[] TipAnchorGrid(int x, int y, int fx, int fy)
        {
            return new[] { x + 0.5f + fx * 0.5f, y + 0.5f + fy * 0.5f };
        }

        public static bool RelinksAtLoad(bool heldByMerge, bool serviced, bool active, bool awaitingRebuild)
        {
            return !heldByMerge && !serviced && active && !awaitingRebuild;
        }
    }
}
