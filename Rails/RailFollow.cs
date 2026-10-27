using System;

namespace ZTX.HingesPistonsDocking
{
    internal static class RailFollow
    {
        public const float DEFAULT_GAP = 1f;
        public const float GAP_MAX = 20f;

        public static float FollowSpeed(float vLeader, float gap, float setting, float brake, float leaderBrake, float dt)
        {
            if (!(gap > 0f)) return 0f;
            float vl = Math.Max(0f, vLeader);
            float share = leaderBrake > brake && leaderBrake > 0f ? brake / leaderBrake : 1f;
            if (setting > 0f && gap < setting) return vl * gap / setting;
            float s = RailMotion.StopSpeed(gap - Math.Max(0f, setting), brake, dt);
            if (s == float.MaxValue) return float.MaxValue;
            return (float)Math.Sqrt(vl * vl * share + s * s);
        }

        public static float FootprintGap(float along, float myFront, float theirNear, float leftReach, float rightReach, float turnLeft, float turnRight)
        {
            float allowance = Math.Max(0f, leftReach) * Math.Max(0f, turnLeft) + Math.Max(0f, rightReach) * Math.Max(0f, turnRight);
            return along - myFront - theirNear - allowance;
        }

        public static float CornerSpeed(float force, float addedSlope)
        {
            if (!(addedSlope > 1e-6f)) return float.MaxValue;
            return (float)Math.Sqrt(2.0 * Math.Max(0f, force) / addedSlope);
        }

        public static float ApproachSpeed(float target, float dist, float brake, float dt)
        {
            if (target == float.MaxValue) return float.MaxValue;
            float t = Math.Max(0f, target);
            if (!(dist > 0f)) return t;
            float s = RailMotion.StopSpeed(dist, brake, dt);
            if (s == float.MaxValue) return float.MaxValue;
            return (float)Math.Sqrt(t * t + s * s);
        }

        public static bool ContactAhead(float myMin, float myMax, float otherMin, float otherMax)
        {
            return (otherMin + otherMax) * 0.5f > (myMin + myMax) * 0.5f + 1e-4f;
        }

        public static int EncodeGap(float cells) => (int)Math.Round(Math.Max(0f, Math.Min(GAP_MAX, cells)) * 10f) + 1;

        public static float DecodeGap(int stored) => stored > 0 ? Math.Min(GAP_MAX, (stored - 1) / 10f) : DEFAULT_GAP;

        public static void TurnSums(float[] headings, int count, out float left, out float right)
        {
            left = right = 0f;
            for (int i = 1; i < count; i++)
            {
                float dh = RailRoute.WrapAngle(headings[i] - headings[i - 1]);
                if (dh > 0f) left += dh;
                else right -= dh;
            }
        }

        public static float NearExtent(float dir, float sign, float front, float rear)
        {
            return dir * sign > 0f ? rear : front;
        }
    }
}
