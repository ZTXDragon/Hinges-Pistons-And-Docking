using System;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts.Thrusters;
using Halfling.Geometry;
using Halfling.Timing;

namespace ZTX.HingesPistonsDocking
{
    internal static class RailThrustDrive
    {
        public static float Authority(Ship cartShip, Vector2 axis, float loadMass, float scale)
        {
            ThrusterManager tm = cartShip?.Thrusters;
            if (tm == null || !(loadMass > 0f)) return 0f;
            return Total(tm, axis.X, axis.Y) / loadMass / (scale > 0f ? scale : 1f);
        }

        public static float Command(Ship cartShip, Vector2 axis, float force, float interval)
        {
            ThrusterManager tm = cartShip?.Thrusters;
            if (tm == null || tm.Count == 0) return 0f;
            float ax = force >= 0f ? axis.X : -axis.X, ay = force >= 0f ? axis.Y : -axis.Y;
            float level = RailThrustMath.PushLevel(Math.Abs(force), Total(tm, ax, ay));
            foreach (Thruster t in tm._orderedSoftOpThrusters)
            {
                Vector3 f = t.GetSRF();
                bool pushes = RailThrustMath.AxisPush(f.X, f.Y, ax, ay) > 0f;
                t.UncomittedActivationLevel = pushes ? level * Math.Max(0f, t.MaxActivation) : 0f;
            }
            tm._magicSRF = Vector3.Zero;
            Time dt = interval;
            tm.CommitActivationLevels(dt);
            if (cartShip.Sim != null) tm._lastCommandedTime = cartShip.Sim.LogicalTime;
            tm.FixedUpdatingEnabled = true;
            float actual = 0f;
            foreach (Thruster t in tm._orderedSoftOpThrusters)
            {
                Vector3 f = t.GetSRF();
                actual += (f.X * axis.X + f.Y * axis.Y) * t.ActivationLevel;
            }
            return actual;
        }

        private static float Total(ThrusterManager tm, float ax, float ay)
        {
            float total = 0f;
            foreach (Thruster t in tm._orderedSoftOpThrusters)
            {
                Vector3 f = t.GetSRF();
                total += RailThrustMath.AxisPush(f.X, f.Y, ax, ay) * Math.Max(0f, t.MaxActivation);
            }
            return total;
        }
    }
}
