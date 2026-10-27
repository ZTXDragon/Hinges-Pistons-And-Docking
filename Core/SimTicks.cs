using System;

namespace ZTX.HingesPistonsDocking
{
    internal static class SimTicks
    {
        public const int NEVER = int.MinValue;

        public const float DEFAULT_TICK_SECONDS = 1f / 30f;

        public static bool IsFresh(int now, int stamp, int window)
        {
            if (stamp == NEVER || now < stamp) return false;
            return (long)now - stamp <= window;
        }

        public static int WindowFor(float ms, float tickSeconds)
        {
            if (tickSeconds <= 0f) tickSeconds = DEFAULT_TICK_SECONDS;
            double ticks = ms / (tickSeconds * 1000.0);
            return Math.Max(1, (int)Math.Ceiling(ticks - 1e-3));
        }
    }
}
