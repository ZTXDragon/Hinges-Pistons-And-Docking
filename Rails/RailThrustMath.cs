using System;

namespace ZTX.HingesPistonsDocking
{
    internal static class RailThrustMath
    {
        public const int STALL_TICKS = 15;
        public const float STALL_CMD = 0.1f;
        public const float STALL_SPEED = 0.05f;

        public static float IntegrateS(float s, float translation, float scale, float length)
        {
            float r = s + translation / (scale > 0f ? scale : 1f);
            if (r < 0f) return 0f;
            return r > length ? length : r;
        }

        public static float MotorForce(float mass, float accel, float emergency, float margin, float scale)
        {
            return Math.Max(0f, mass) * Math.Max(Math.Max(0f, accel), Math.Max(0f, emergency)) * Math.Max(0f, margin) * (scale > 0f ? scale : 1f);
        }

        public static int StallStep(int ticks, float planned, float speed)
        {
            return planned > STALL_CMD && Math.Abs(speed) < STALL_SPEED ? ticks + 1 : 0;
        }

        public static bool Stalled(int ticks)
        {
            return ticks >= STALL_TICKS;
        }

        public const float ENGINE_DRAG = 0.4f;
        public const int UNDERPOWERED_TICKS = 30;
        public const float COAST_BAND = 0.3f;

        public static float RawThrustAccel(float planned, float speed, float dt, float drag, float dragSpeed)
        {
            return (planned - speed) / (dt > 0f ? dt : 1f) + drag * dragSpeed;
        }

        public static float ThrustAccel(float raw, float aFwd, float aBack)
        {
            if (raw < 0f && raw > -COAST_BAND) return 0f;
            float hi = Math.Max(0f, aFwd), lo = -Math.Max(0f, aBack);
            return raw > hi ? hi : (raw < lo ? lo : raw);
        }

        public static bool GripBrake(float raw, float aBack)
        {
            return raw < -Math.Max(0f, aBack) - 1e-3f;
        }

        public static bool Saturated(float raw, float aFwd)
        {
            return aFwd > 0f && raw >= aFwd - 1e-4f;
        }

        public static int UnderpoweredStep(int ticks, bool saturated, float speed)
        {
            return saturated && Math.Abs(speed) < STALL_SPEED ? ticks + 1 : 0;
        }

        public static bool Underpowered(int ticks)
        {
            return ticks >= UNDERPOWERED_TICKS;
        }

        public const float ALIGN_COS = 0.985f;

        public static float AxisPush(float tx, float ty, float ax, float ay)
        {
            float len = (float)Math.Sqrt(tx * tx + ty * ty);
            if (len < 1e-6f) return 0f;
            float along = tx * ax + ty * ay;
            return along >= ALIGN_COS * len ? along : 0f;
        }

        public static float PushLevel(float force, float total)
        {
            return total > 0f && force > 0f ? Math.Min(1f, force / total) : 0f;
        }

        public static void SwingForces(float rx, float ry, float omega, float alpha, float mass, float inertiaCm, float tx, float ty,
            out float fx, out float fy, out float alongT, out float torque)
        {
            float ax = -alpha * ry - omega * omega * rx, ay = alpha * rx - omega * omega * ry;
            fx = mass * ax;
            fy = mass * ay;
            alongT = fx * tx + fy * ty;
            torque = inertiaCm * alpha + (rx * fy - ry * fx);
        }

        public static float AddedMass(float tx, float ty, float turnPerUnit, float rx, float ry, float mass, float inertiaCm)
        {
            float kr = -tx * ry + ty * rx;
            return 2f * mass * turnPerUnit * kr + (mass * (rx * rx + ry * ry) + inertiaCm) * turnPerUnit * turnPerUnit;
        }

        public static float TurnForce(float addedMass, float plannedAccel)
        {
            return -addedMass * plannedAccel;
        }

        public const float ALIGN_MIN = 0.05f;

        public static float ThrustOnRail(float thrust, float align)
        {
            return thrust * Math.Max(0f, align);
        }

        public static float NetDrive(float drive, float friction, float speed)
        {
            float f = Math.Max(0f, friction);
            if (Math.Abs(speed) > STALL_SPEED) return drive - (speed > 0f ? f : -f);
            if (Math.Abs(drive) <= f) return 0f;
            return drive > 0f ? drive - f : drive + f;
        }

        public static float ThrustDemand(float alongForce, float align)
        {
            return align >= ALIGN_MIN ? alongForce / align : 0f;
        }

        public static float AchievedAccel(float want, float dirS, float driveLimit, float brakeLimit)
        {
            if (want * dirS >= 0f) return dirS * Math.Min(Math.Abs(want), Math.Max(0f, driveLimit));
            return -dirS * Math.Min(Math.Abs(want), Math.Max(0f, brakeLimit));
        }

        public static float TurnSpeed(float speed, float mass, float addedBefore, float addedAfter)
        {
            float before = Math.Max(0.2f * mass, mass + addedBefore), after = Math.Max(0.2f * mass, mass + addedAfter);
            return after > 0f ? speed * (float)Math.Sqrt(before / after) : speed;
        }

        public static float FinalApproach(float planned, float planMove, float dt)
        {
            float m = Math.Abs(planMove);
            return planned <= 0.01f && m > 1e-4f && dt > 0f ? m / dt : planned;
        }

        public static bool GripOn(float raw, float aBack, float planned)
        {
            return planned <= 0.01f || raw <= -COAST_BAND || GripBrake(raw, aBack);
        }

        public static float RollingAccel(float speed, float rolling)
        {
            return Math.Abs(speed) > STALL_SPEED ? Math.Max(0f, rolling) : 0f;
        }

        public static bool UseThrust(bool thrustMode, float aToward, float minAccel)
        {
            return thrustMode && aToward >= minAccel;
        }
    }
}
