using System;

namespace ZTX.HingesPistonsDocking
{
    internal static class RailMotion
    {
        public static float Accel(float accelCap, float ratedLoad, float mass)
        {
            if (!(accelCap > 0f)) return 0f;
            if (!(mass > 0f) || !(ratedLoad > 0f)) return accelCap;
            return accelCap * Math.Min(1f, ratedLoad / mass);
        }

        public static float DriveAccel(float accelCap, float strength)
        {
            float a = accelCap * strength;
            return a > 0f ? a : 0f;
        }

        public static float StopSpeed(float d, float b, float dt)
        {
            if (!(d > 0f)) return 0f;
            if (!(b > 0f) || float.IsPositiveInfinity(d)) return float.MaxValue;
            float h = 0.5f * b * dt;
            return (float)Math.Sqrt(2.0 * b * d + h * h) - h;
        }

        public static float StopDistance(float v, float b)
        {
            if (!(v > 0f) || !(b > 0f)) return 0f;
            return v * v / (2f * b);
        }

        public static float NextSpeed(float v, float soft, float hard, float a, float b, float e, float dt)
        {
            if (!(v > 0f)) v = 0f;
            float target = Math.Min(soft, hard);
            if (!(target > 0f)) target = 0f;
            if (target >= v) return Math.Min(v + Math.Max(0f, a) * dt, target);
            float next = Math.Max(target, v - Math.Max(0f, b) * dt);
            if (next > hard) next = Math.Max(hard, v - Math.Max(0f, e) * dt);
            return next > 0f ? next : 0f;
        }

        public static float Advance(float v, float dist, float soft, float follow, float a, float b, float e, float dt,
            out float move, out bool deadStop)
        {
            float hard = Math.Min(follow, StopSpeed(dist, b, dt));
            float next = NextSpeed(v, soft, hard, a, b, e, dt);
            move = next * dt;
            deadStop = false;
            if (move >= dist)
            {
                deadStop = next > Math.Max(0f, e) * dt + 1e-4f;
                move = dist > 0f ? dist : 0f;
                next = 0f;
            }
            return next;
        }

        public static float BlendFactor(float left, float total)
        {
            if (!(total > 0f) || !(left > 1e-4f)) return 0f;
            return left >= total ? 1f : left / total;
        }
    }
}
