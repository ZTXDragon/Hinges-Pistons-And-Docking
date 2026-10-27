using System;
using System.Collections.Generic;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts.Thrusters;
using Halfling;
using Halfling.Geometry;
using Halfling.Timing;

namespace ZTX.HingesPistonsDocking
{
    internal static class AssemblyThrustSolver
    {
        private struct Entry
        {
            public Thruster T;
            public float Sx, Sy, Sz;
            public float Fx, Fy, Fz;
            public float Nx, Ny, Nz;
            public float Min, Max;
            public float Dist;
        }

        private static readonly List<Entry> s_entries = new List<Entry>();
        private static readonly Comparison<Entry> s_byDist = (a, b) => a.Dist.CompareTo(b.Dist);

        public static bool LastWasFailsafe { get; private set; }

        public static Vector3 Solve(Ship pilot, List<AssemblyServo.Member> members, Vector3 desiredSRF,
            Time interval, Vector3 srfFactors, ThrusterManager.ActivationRangeType rangeType,
            float massAsm, float inertiaAsm)
        {
            LastWasFailsafe = false;
            Mathx.SinCos((float)pilot.FlightDirection, out float sinRot, out float cosRot);
            float dt = (float)interval;
            ShipRules rules = pilot.Rules;

            foreach (AssemblyServo.Member m in members)
            {
                List<Thruster> soft = m.Thrusters._orderedSoftOpThrusters;
                for (int i = 0; i < soft.Count; i++) soft[i].UncomittedActivationLevel = 0f;
                m.Thrusters._magicSRF = Vector3.Zero;
            }
            if (desiredSRF.X == 0f && desiredSRF.Y == 0f && desiredSRF.Z == 0f)
                return Vector3.Zero;

            Vector3 v = desiredSRF;
            float remX = desiredSRF.X, remY = desiredSRF.Y, remZ = desiredSRF.Z;

            s_entries.Clear();
            foreach (AssemblyServo.Member m in members)
            {
                List<Thruster> soft = m.Thrusters._orderedSoftOpThrusters;
                for (int i = 0; i < soft.Count; i++)
                {
                    Thruster t = soft[i];
                    Vector3 srf = t.GetSRF();
                    Entry e = default;
                    e.T = t;
                    e.Sx = srf.X * m.Cos + srf.Y * m.Sin;
                    e.Sy = (0f - srf.X) * m.Sin + srf.Y * m.Cos;
                    e.Sz = srf.Z;
                    Factor(e.Sx, e.Sy, e.Sz, false, out e.Fx, out e.Fy, out e.Fz);
                    float fl = (float)Math.Sqrt(e.Fx * e.Fx + e.Fy * e.Fy + e.Fz * e.Fz);
                    if (fl > 0f) { e.Nx = e.Fx / fl; e.Ny = e.Fy / fl; e.Nz = e.Fz / fl; }

                    e.Min = t.MinActivation;
                    e.Max = t.MaxActivation;
                    switch (rangeType)
                    {
                        case ThrusterManager.ActivationRangeType.Normal:
                        {
                            float incT = (float)t.ActivationIncreaseTime;
                            if (e.Max > 0f)
                                e.Max = Mathx.Min(e.Max, Mathx.Max(Mathx.Max(t.ActivationLevel, 0f) + dt / incT, t.ActivationRecoveryLevel));
                            if (e.Min < 0f)
                                e.Min = Mathx.Max(e.Min, Mathx.Min(Mathx.Min(t.ActivationLevel, 0f) - dt / incT, t.ActivationRecoveryLevel));
                            break;
                        }
                        case ThrusterManager.ActivationRangeType.Deceleration:
                        {
                            float num = (float)t.ActivationIncreaseTime;
                            if (num > 0f)
                            {
                                if (e.Max > 0f)
                                    e.Max = Mathx.Min(e.Max, Mathx.Min(e.Max * num, rules.MaxRampUpTimeForDeceleration) / num);
                                if (e.Min < 0f)
                                    e.Min = Mathx.Max(e.Min, Mathx.Max(e.Min * num, 0f - rules.MaxRampUpTimeForDeceleration) / num);
                            }
                            break;
                        }
                    }
                    s_entries.Add(e);
                }
            }

            for (int k = 0; k < rules.ThrusterSolverIterations; k++)
            {
                bool first = k == 0;
                Factor(remX, remY, remZ, first, out float vx, out float vy, out float vz);
                float vl = (float)Math.Sqrt(vx * vx + vy * vy + vz * vz);
                float ux = 0f, uy = 0f, uz = 0f;
                if (vl > 0f) { ux = vx / vl; uy = vy / vl; uz = vz / vl; }

                for (int i = 0; i < s_entries.Count; i++)
                {
                    Entry e = s_entries[i];
                    float lvl = e.T.UncomittedActivationLevel;
                    float d = float.MaxValue;
                    if (lvl < e.Max)
                        d = Sq(e.Nx - ux) + Sq(e.Ny - uy) + Sq(e.Nz - uz);
                    if (lvl > e.Min)
                        d = Mathx.Min(d, Sq(0f - e.Nx - ux) + Sq(0f - e.Ny - uy) + Sq(0f - e.Nz - uz));
                    e.Dist = d;
                    s_entries[i] = e;
                }
                s_entries.Sort(s_byDist);

                for (int i = 0; i < s_entries.Count; i++)
                {
                    Entry e = s_entries[i];
                    float fx = e.Fx, fy = e.Fy, fz = first ? 0f : e.Fz;
                    float f2 = fx * fx + fy * fy + fz * fz;
                    if (f2 <= 0f) continue;
                    float lvl = e.T.UncomittedActivationLevel;
                    float step = (vx * fx + vy * fy + vz * fz) / f2;
                    float next = Mathx.Clamp(lvl + step, e.Min, e.Max);
                    float delta = next - lvl;
                    e.T.UncomittedActivationLevel = next;
                    remX -= delta * e.Sx; remY -= delta * e.Sy; remZ -= delta * e.Sz;
                    vx -= delta * fx; vy -= delta * fy; vz -= delta * fz;
                }
            }

            float achX = v.X - remX, achY = v.Y - remY, achZ = v.Z - remZ;

            Vector3 magic = Vector3.Zero;
            if (rules.MaxLinearMagicAcceleration > 0f && (remX != 0f || remY != 0f))
            {
                float cap = rules.MaxLinearMagicAcceleration * massAsm;
                float achLen2 = achX * achX + achY * achY;
                if (achLen2 < cap * cap)
                {
                    float remLen = (float)Math.Sqrt(remX * remX + remY * remY);
                    float amount = Mathx.Min(cap - (float)Math.Sqrt(achLen2), remLen);
                    if (amount > 0f)
                    {
                        magic.X = amount * remX / remLen;
                        magic.Y = amount * remY / remLen;
                        achX += magic.X; achY += magic.Y;
                    }
                }
            }
            if (rules.MaxAngularMagicAcceleration > 0f && remZ != 0f)
            {
                float cap = rules.MaxAngularMagicAcceleration * inertiaAsm;
                if (Math.Abs(achZ) < cap)
                {
                    float amount = Mathx.Min(cap - Math.Abs(achZ), Math.Abs(remZ));
                    if (amount > 0f)
                    {
                        magic.Z = amount * Math.Sign(remZ);
                        achZ += magic.Z;
                    }
                }
            }
            pilot.Thrusters._magicSRF = magic;

            Factor(achX, achY, achZ, false, out float ax, out float ay, out float az);
            Factor(v.X, v.Y, v.Z, false, out float dx, out float dy, out float dz);
            if (ax * dx + ay * dy + az * dz < 0f)
            {
                LastWasFailsafe = true;
                foreach (AssemblyServo.Member m in members)
                {
                    List<Thruster> soft = m.Thrusters._orderedSoftOpThrusters;
                    for (int i = 0; i < soft.Count; i++) soft[i].UncomittedActivationLevel = 0f;
                    m.Thrusters._magicSRF = Vector3.Zero;
                }
                return Vector3.Zero;
            }
            return new Vector3(achX, achY, achZ);

            void Factor(float x, float y, float z, bool zeroTorque, out float ox, out float oy, out float oz)
            {
                ox = (x * cosRot - y * sinRot) * srfFactors.X;
                oy = (x * sinRot + y * cosRot) * srfFactors.Y;
                oz = zeroTorque ? 0f : z * srfFactors.Z;
            }
        }

        private static float Sq(float a) => a * a;
    }
}
