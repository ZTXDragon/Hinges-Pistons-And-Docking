using System;

namespace ZTX.HingesPistonsDocking
{
    internal static class ProximityMath
    {
        public const int MEASURE_DISTANCE = 0, MEASURE_COUNT = 1, MEASURE_BEARING = 2;
        public const int SECTOR_ALL = 0, SECTOR_AHEAD = 1, SECTOR_RIGHT = 2, SECTOR_BEHIND = 3, SECTOR_LEFT = 4;
        public const int SIZE_ANY = 0, SIZE_SMALL = 1, SIZE_MEDIUM = 2, SIZE_LARGE = 3, SIZE_NOT_SMALL = 4;
        public const int DOCKED_IGNORE = 0, DOCKED_COUNT = 1;
        public const float SMALL_BELOW = 10f, LARGE_ABOVE = 30f;

        public static float Bearing(float dx, float dy, int fx, int fy)
        {
            float ahead = dx * fx + dy * fy;
            float right = dx * -fy + dy * fx;
            if (ahead == 0f && right == 0f) return 0f;
            double deg = Math.Atan2(right, ahead) * 180.0 / Math.PI;
            if (deg < 0) deg += 360.0;
            return deg >= 360.0 ? 0f : (float)deg;
        }

        public static bool InSector(float bearing, int sector)
        {
            switch (sector)
            {
                case SECTOR_AHEAD: return bearing >= 315f || bearing < 45f;
                case SECTOR_RIGHT: return bearing >= 45f && bearing < 135f;
                case SECTOR_BEHIND: return bearing >= 135f && bearing < 225f;
                case SECTOR_LEFT: return bearing >= 225f && bearing < 315f;
                default: return true;
            }
        }

        public static bool SizeInRange(float diameterTiles, float minTiles, float maxTiles) =>
            diameterTiles >= minTiles && (maxTiles <= 0f || diameterTiles <= maxTiles);

        public static bool SizeMatches(float diameterTiles, int size)
        {
            switch (size)
            {
                case SIZE_SMALL: return diameterTiles < SMALL_BELOW;
                case SIZE_MEDIUM: return diameterTiles >= SMALL_BELOW && diameterTiles <= LARGE_ABOVE;
                case SIZE_LARGE: return diameterTiles > LARGE_ABOVE;
                case SIZE_NOT_SMALL: return diameterTiles >= SMALL_BELOW;
                default: return true;
            }
        }
    }
}
