using System;

namespace ZTX.HingesPistonsDocking
{
    internal static class RailShipHold
    {
        public static int Encode(bool on) => on ? 1 : 2;

        public static bool Decode(int stored) => stored != 2;

        public static float PushAlong(float mass, float rx, float ry, float ax, float ay, float alpha, float omega, float tx, float ty)
        {
            float fx = ax - alpha * ry - omega * omega * rx;
            float fy = ay + alpha * rx - omega * omega * ry;
            return -mass * (fx * tx + fy * ty);
        }

        public static float CompensatedCap(float cap, float push, bool on)
        {
            if (!on || float.IsNaN(push) || float.IsInfinity(push)) return cap;
            return cap + Math.Abs(push);
        }
    }
}
