using System;

namespace ZTX.HingesPistonsDocking
{
    internal static class ShipSensorMath
    {
        public const int MEASURE_SPEED = 0, MEASURE_HULL = 1, MEASURE_POWER = 2, MEASURE_CREW = 3, MEASURE_RESOURCE = 4,
            MEASURE_HEAT_STORED = 5, MEASURE_HOTTEST = 6, MEASURE_DAMAGE = 7;
        public const int MEASURE_COUNT = 8;
        public const float HEAT_DAMAGE_START = 700f;
        public const float MOVING_TILES_PER_SECOND = 0.5f;
        public const float DAMAGE_WINDOW_SECONDS = 5f;
        public const float UNITS_MAX = 2047f;

        public static float Percent(float stored, float capacity)
        {
            if (capacity <= 0f) return 0f;
            return Math.Max(0f, Math.Min(100f, stored / capacity * 100f));
        }

        public static float HeatPercent(float heat) => Math.Max(0f, Math.Min(100f, heat / HEAT_DAMAGE_START * 100f));

        public static bool Damaged(float currentNow, float currentBefore, float designNow, float designBefore) =>
            currentNow < currentBefore && Math.Abs(designNow - designBefore) < 0.5f;

        public static float SinceSeconds(int lastTick, int now, int ticksPerSecond)
        {
            if (lastTick < 0) return UNITS_MAX;
            float s = (now - lastTick) / (float)Math.Max(1, ticksPerSecond);
            return Math.Max(0f, Math.Min(UNITS_MAX, s));
        }
    }
}
