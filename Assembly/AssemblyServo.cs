using System;
using System.Collections.Generic;
using System.Reflection;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Parts.Thrusters;
using Halfling;
using Halfling.Geometry;
using Halfling.Timing;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch]
    public static class AssemblyServoPatch
    {
        private static readonly object s_servoLock = new object();

        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Command), "SetThrusterActivations", new[]
            {
                typeof(Time), typeof(Vector2), typeof(Vector2), typeof(Direction?),
                typeof(bool).MakeByRefType(), typeof(bool).MakeByRefType(),
                typeof(Vector2), typeof(float), typeof(Vector2), typeof(bool), typeof(bool),
            });
        }

        static bool Prefix(Command __instance, Time interval, Vector2 originalDesiredLoc,
            Vector2 adjustedDesiredLoc, Direction? desiredRot, ref bool adjustPosition,
            ref bool adjustRotation, Vector2 overrideSRV, float overrideFactor,
            Vector2 extraSRV, bool allowOvershoot, bool orbiting)
        {
            try
            {
                if (!AssemblyFlightFeatures.Enabled) return true;
                Ship ship = __instance.Ship;
                if (ship?.Physics?.Body == null || !HingeJointManager.IsRigidlyJointed(ship)) return true;
                if (!ReferenceEquals(HingeJointManager.GetDesignatedPilot(ship), ship)) return true;

                lock (s_servoLock)
                {
                    AssemblyServo.Run(__instance, ship, interval, originalDesiredLoc, adjustedDesiredLoc,
                        desiredRot, ref adjustPosition, ref adjustRotation, overrideSRV, overrideFactor,
                        extraSRV, allowOvershoot, orbiting);
                }
                return false;
            }
            catch (Exception ex)
            {
                Log.Exception("AssemblyServoPatch.Prefix", ex);
                return true;
            }
        }
    }

    internal static class AssemblyServo
    {
        internal struct Member
        {
            public Ship Ship;
            public ThrusterManager Thrusters;
            public float Mass;
            public float Frac;
            public float Sin;
            public float Cos;
        }

        private static readonly List<Ship> s_ships = new List<Ship>();
        private static readonly List<Member> s_members = new List<Member>();
        private static Ship s_pilot;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Ship, System.Runtime.CompilerServices.StrongBox<int>> s_lastRun =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Ship, System.Runtime.CompilerServices.StrongBox<int>>();

        public static int LastRunTick(Ship pilot)
        {
            return pilot != null && s_lastRun.TryGetValue(pilot, out var box) ? box.Value : SimTicks.NEVER;
        }

        public static void Run(Command cmd, Ship pilot, Time interval, Vector2 originalDesiredLoc,
            Vector2 adjustedDesiredLoc, Direction? desiredRot, ref bool adjustPosition,
            ref bool adjustRotation, Vector2 overrideSRV, float overrideFactor, Vector2 extraSRV,
            bool allowOvershoot, bool orbiting)
        {
            if (!HingeJointManager.TryGetAssemblyProperties(pilot, out float massAsm, out float inertiaAsm, out Vector2 comWorld) ||
                massAsm <= 0f || inertiaAsm <= 0f)
                return;
            s_lastRun.GetValue(pilot, _ => new System.Runtime.CompilerServices.StrongBox<int>(SimTicks.NEVER)).Value = pilot.Sim?.Tick ?? SimTicks.NEVER;
            BuildMembers(pilot, comWorld);
            if (s_members.Count == 0) return;

            Vector2 flightRef = pilot.DetWorldCenter;
            Vector2 vector = originalDesiredLoc - flightRef;
            Vector2 v = adjustedDesiredLoc - flightRef;
            float satRadius = pilot.WorldBoundingRadius;
            if (cmd is MoveCommand moveCmd)
                satRadius *= moveCmd.Rules.SatisfiedDistanceAsRadiusFraction;
            AssemblyFlightDiag.OnGeometry(pilot, originalDesiredLoc, flightRef, comWorld, adjustPosition, satRadius);
            Direction? desiredRot2 = (adjustPosition
                ? ((!adjustRotation) ? ((Direction?)null) : desiredRot)
                : ((!(v != Vector2.Zero))
                    ? new Direction?(vector.Direction - (Angle)pilot.FlightDirection)
                    : new Direction?(v.Direction - (Angle)pilot.FlightDirection)));

            Angle smartRotOffset;
            float rotSRA = GetDesiredRotationalSRA(cmd, pilot, interval, desiredRot2, massAsm, inertiaAsm, out smartRotOffset);
            Vector2 curSRV;
            Vector2 linSRA = GetDesiredLinearSRA(cmd, pilot, interval, v, smartRotOffset, adjustPosition,
                massAsm, inertiaAsm, out curSRV, overrideSRV, overrideFactor, extraSRV, allowOvershoot,
                rotSRA != 0f, orbiting);

            if (AssemblyFlightDiag.ENABLED) LogCrab(pilot);

            Allocate(pilot, new Vector3(linSRA.X, linSRA.Y, rotSRA), interval,
                adjustPosition ? cmd.Rules.ThrusterSRFStrafeFactors : cmd.Rules.ThrusterSRFFactors, adjustPosition);

            float num = pilot.WorldBoundingRadius * 2f;
            float lengthSquared = vector.LengthSquared;
            if (!adjustPosition)
            {
                if (lengthSquared <= (num * pilot.Rules.TargetReachedDistanceSizeRatio).Squared())
                {
                    adjustPosition = true;
                    if (curSRV.Length <= pilot.Rules.AdjustRotationVelocity)
                        adjustRotation = true;
                }
            }
            else if (lengthSquared >= (num * pilot.Rules.TargetResetDistanceSizeRatio).Squared())
            {
                adjustPosition = false;
                adjustRotation = false;
            }
            else if (curSRV.Length <= pilot.Rules.AdjustRotationVelocity)
            {
                adjustRotation = true;
            }
        }

        private static void LogCrab(Ship pilot)
        {
            try
            {
                var body = pilot.Physics?.Body;
                if (body == null) return;
                Vector2 pilotVelW = body.LinearVelocity;
                if (!HingeJointManager.TryGetAssemblyVelocity(pilot, out Vector2 comVelW))
                    comVelW = pilotVelW;

                float a = 0f - (float)pilot.DetRotation;
                Mathx.SinCos(a, out var sin, out var cos);
                Vector2 vPilot = new Vector2(pilotVelW.X * cos - pilotVelW.Y * sin, pilotVelW.X * sin + pilotVelW.Y * cos);
                Vector2 vCom = new Vector2(comVelW.X * cos - comVelW.Y * sin, comVelW.X * sin + comVelW.Y * cos);

                Vector2 fwd = Vector2.FromAngle(pilot.FlightDirection);
                Vector2 lat = fwd.Perp();
                float latPilot = vPilot.ScalarProjectOnto(in lat);
                float latCom = vCom.ScalarProjectOnto(in lat);
                float fwdCom = vCom.ScalarProjectOnto(in fwd);
                float crabDeg = (float)(DetMath.Atan2(latCom, fwdCom) * 180.0 / Math.PI);
                AssemblyFlightDiag.OnCrab(pilot, crabDeg, latPilot, latCom, body.AngularVelocity, vCom.Length);
            }
            catch { }
        }

        private static float GetDesiredRotationalSRA(Command cmd, Ship pilot, Time interval,
            Direction? desiredRot, float massAsm, float inertiaAsm, out Angle smartRotOffset)
        {
            float angularVelocity = pilot.Physics.Body.AngularVelocity;
            float num5;
            if (desiredRot.HasValue)
            {
                Direction target = desiredRot.GetValueOrDefault();
                Angle angle = pilot.DetRotation.PositiveAngleTo(target);
                Angle angle2 = (float)angle - (float)Angle.ThreeSixty;
                float decel = 0f, decelRampUp = 0f, accelRampDown = 0f;
                if (!angularVelocity.Equals(0f))
                {
                    decel = Probe(new Vector3(0f, 0f, 0f - Mathx.Sign(angularVelocity)), interval,
                        cmd.Rules.ThrusterSRFStrafeFactors, ThrusterManager.ActivationRangeType.Deceleration, massAsm, inertiaAsm);
                    AssemblyRampTimes(out decelRampUp, out accelRampDown);
                }
                float num2 = Mathx.Sign(angularVelocity);
                decel *= 0f - num2;
                var dragSolver = pilot.Physics.Body?.AngularDragSolver;
                float dampingFactor = pilot.Nebulas.AngularDampingFactor;
                float curAccel = pilot.Thrusters.AngularAcceleration;
                float stopDist = cmd.CalculateDecelerationDistance(angularVelocity * num2, decel * num2,
                    decelRampUp, curAccel * num2, accelRampDown, dragSolver, dampingFactor, 0f) * num2;
                Angle angle3 = (float)angle - stopDist;
                Angle angle4 = (float)angle2 - stopDist;
                smartRotOffset = ((Mathx.Abs(angle3) <= Mathx.Abs(angle4)) ? angle3 : angle4);
                num5 = (float)smartRotOffset * pilot.Rules.AngularVelocityCalculateFactor;
            }
            else
            {
                num5 = 0f;
                smartRotOffset = Angle.Zero;
            }
            float value = num5 - angularVelocity * pilot.Rules.RetroAngularVelocityCalculateFactor;
            float maxNeg = Probe(new Vector3(0f, 0f, -1f), interval, cmd.Rules.ThrusterSRFStrafeFactors, ThrusterManager.ActivationRangeType.Unclamped, massAsm, inertiaAsm);
            float maxPos = Probe(new Vector3(0f, 0f, 1f), interval, cmd.Rules.ThrusterSRFStrafeFactors, ThrusterManager.ActivationRangeType.Unclamped, massAsm, inertiaAsm);
            return Mathx.Clamp(value, 0f - maxNeg, maxPos);
        }

        private static Vector2 GetDesiredLinearSRA(Command cmd, Ship pilot, Time interval, Vector2 wrpOffset,
            Angle smartRotOffset, bool adjustPosition, float massAsm, float inertiaAsm, out Vector2 curSRV,
            Vector2 overrideSRV, float overrideFactor, Vector2 extraSRV, bool allowOvershoot,
            bool prioritizeRotation, bool orbiting)
        {
            float value = 0f - (float)pilot.DetRotation;
            Mathx.SinCos(value, out var sin, out var cos);
            Vector2 vector = new Vector2(wrpOffset.X * cos - wrpOffset.Y * sin, wrpOffset.X * sin + wrpOffset.Y * cos);
            Vector2 velWorld = pilot.Physics.Body.LinearVelocity;
            curSRV = new Vector2(velWorld.X * cos - velWorld.Y * sin, velWorld.X * sin + velWorld.Y * cos);
            curSRV -= extraSRV;
            Vector2 v = ((!adjustPosition)
                ? Vector2.FromAngle(pilot.FlightDirection)
                : ((!orbiting && vector.LengthSquared != 0f) ? vector.Normalize() : new Vector2(0f, -1f)));
            Vector2 v2 = v.Perp();
            float num = curSRV.ScalarProjectOnto(in v);
            float num2 = curSRV.ScalarProjectOnto(in v2);
            float num3 = vector.ScalarProjectOnto(in v);
            float num4 = vector.ScalarProjectOnto(in v2);
            if (!allowOvershoot)
            {
                float num5 = 0f, dru1 = 0f, ard1 = 0f;
                if (!num.Equals(0f))
                {
                    Vector2 pd1 = (float)(0f - Mathx.Sign(num)) * v;
                    num5 = Probe(new Vector3(pd1.X, pd1.Y, 0f), interval, cmd.Rules.ThrusterSRFStrafeFactors,
                        ThrusterManager.ActivationRangeType.Deceleration, massAsm, inertiaAsm);
                    AssemblyRampTimes(out dru1, out ard1);
                }
                num5 *= (float)(0f - Mathx.Sign(num));
                float num6 = 0f, dru2 = 0f, ard2 = 0f;
                if (!num2.Equals(0f))
                {
                    Vector2 pd2 = (float)(0f - Mathx.Sign(num2)) * v2;
                    num6 = Probe(new Vector3(pd2.X, pd2.Y, 0f), interval, cmd.Rules.ThrusterSRFStrafeFactors,
                        ThrusterManager.ActivationRangeType.Deceleration, massAsm, inertiaAsm);
                    AssemblyRampTimes(out dru2, out ard2);
                }
                num6 *= (float)(0f - Mathx.Sign(num2));
                var dragSolver = pilot.Physics.Body?.LinearDragSolver;
                float damping = pilot.Nebulas.LinearDampingFactor;
                Vector2 telem = pilot.Thrusters.WorldLinearAcceleration.RotateAroundOrigin(0f - (float)pilot.DetRotation);
                float acc1 = telem.ScalarProjectOnto(in v);
                float acc2 = telem.ScalarProjectOnto(in v2);
                float tv1 = Mathx.Min(extraSRV.ScalarProjectOnto(in v), 0f);
                float tv2 = Mathx.Min(extraSRV.ScalarProjectOnto(in v2), 0f);
                num3 -= cmd.CalculateDecelerationDistance(num, num5, dru1, acc1, ard1, dragSolver, damping, tv1);
                num4 -= cmd.CalculateDecelerationDistance(num2, num6, dru2, acc2, ard2, dragSolver, damping, tv2);
            }
            float to = overrideSRV.ScalarProjectOnto(in v);
            float to2 = overrideSRV.ScalarProjectOnto(in v2);
            float num7 = num3 * pilot.Rules.VelocityCalculateFactor + extraSRV.ScalarProjectOnto(in v);
            float num8 = num4 * pilot.Rules.VelocityCalculateFactor + extraSRV.ScalarProjectOnto(in v2);
            float num9 = Mathx.Lerp(num7, to, overrideFactor);
            float num10 = Mathx.Lerp(num8, to2, overrideFactor);
            float num11 = (prioritizeRotation
                ? Mathx.InverseLerp(pilot.Rules.AngleSatisfiedThreshold.Max, pilot.Rules.AngleSatisfiedThreshold.Min, Mathx.Abs(smartRotOffset))
                : 1f);
            float num12;
            if (!adjustPosition)
            {
                float lat = curSRV.ScalarProjectOnto(in v2);
                num12 = Mathx.InverseLerp(pilot.Rules.HorizontalSatisfiedThreshold.Max, pilot.Rules.HorizontalSatisfiedThreshold.Min, Mathx.Abs(lat) / Mathx.Abs(num3));
                if (float.IsNaN(num12)) num12 = 1f;
            }
            else num12 = 1f;
            num9 *= Mathx.Min(num11, num12);
            num10 *= num11 * (adjustPosition ? 1f : (1f - num12));
            float min = 0f - Probe(new Vector3(0f - v.X, 0f - v.Y, 0f), interval, cmd.Rules.ThrusterSRFStrafeFactors, ThrusterManager.ActivationRangeType.Unclamped, massAsm, inertiaAsm);
            float item = Probe(new Vector3(v.X, v.Y, 0f), interval, cmd.Rules.ThrusterSRFStrafeFactors, ThrusterManager.ActivationRangeType.Unclamped, massAsm, inertiaAsm);
            float min2 = 0f - Probe(new Vector3(0f - v2.X, 0f - v2.Y, 0f), interval, cmd.Rules.ThrusterSRFStrafeFactors, ThrusterManager.ActivationRangeType.Unclamped, massAsm, inertiaAsm);
            float item2 = Probe(new Vector3(v2.X, v2.Y, 0f), interval, cmd.Rules.ThrusterSRFStrafeFactors, ThrusterManager.ActivationRangeType.Unclamped, massAsm, inertiaAsm);
            float num13 = Mathx.Clamp(num9 - num * pilot.Rules.RetroVelocityCalculateFactor, min, item);
            float num14 = Mathx.Clamp(num10 - num2 * pilot.Rules.RetroVelocityCalculateFactor, min2, item2);
            if (!adjustPosition && num13 > 0f && !float.IsNaN(cmd._avgForwardThrustRatio))
            {
                float match = cmd.GetArrivalTimeMatchFactor();
                num13 *= Mathx.Max(match, cmd._avgForwardThrustRatio - cmd.Sim.FixedUpdater.Interval / pilot.Rules.ArrivalTimeRampDown);
            }
            cmd.RecordForwardThrustRatio(num13, item);
            return num13 * v + num14 * v2;
        }

        #region Assembly probing + allocation

        private static void BuildMembers(Ship pilot, Vector2 comWorld)
        {
            s_members.Clear();
            s_pilot = pilot;
            HingeJointManager.GetAssemblyShips(pilot, s_ships, rigidOnly: true);
            float pilotRot = pilot.Physics.Body.Rotation;
            float mPart = 0f;
            foreach (Ship s in s_ships)
            {
                if (!ReferenceEquals(s, pilot) && HingeJointManager.IsRailDriven(s)) continue;
                if (!ReferenceEquals(s, pilot) && !FlightStage.IsServoOwned(s)) continue;
                var body = s.Physics?.Body;
                var tm = s.Thrusters;
                if (body == null || tm == null || tm.Count == 0) continue;
                double delta = pilotRot - body.Rotation;
                s_members.Add(new Member
                {
                    Ship = s,
                    Thrusters = tm,
                    Mass = body.Mass,
                    Sin = (float)DetMath.Sin(delta),
                    Cos = (float)DetMath.Cos(delta),
                });
                mPart += body.Mass;
            }
            if (mPart <= 0f) { s_members.Clear(); return; }
            for (int i = 0; i < s_members.Count; i++)
            {
                Member m = s_members[i];
                m.Frac = m.Mass / mPart;
                s_members[i] = m;
            }
        }

        private static Vector3 Solve(Vector3 desired, Time interval, Vector3 srfFactors,
            ThrusterManager.ActivationRangeType rangeType, float massAsm, float inertiaAsm,
            bool commit, Ship pilot)
        {
            Ship p = pilot ?? s_pilot;
            if (p == null) return Vector3.Zero;
            var demand = new Vector3(desired.X * massAsm, desired.Y * massAsm, desired.Z * inertiaAsm);
            Vector3 ach = AssemblyThrustSolver.Solve(p, s_members, demand, interval, srfFactors,
                rangeType, massAsm, inertiaAsm);

            if (commit)
            {
                foreach (Member m in s_members)
                {
                    m.Thrusters.CommitActivationLevels(interval);
                    if (!ReferenceEquals(m.Ship, p))
                    {
                        if (m.Ship.Sim != null)
                            m.Thrusters._lastCommandedTime = m.Ship.Sim.LogicalTime;
                        ThrusterKeepAlive.Enable(m.Ship);
                    }
                }
                ThrusterFireDiag.NoteUnionSolve(s_members, demand, ach, interval, srfFactors,
                    AssemblyThrustSolver.LastWasFailsafe);
            }
            return ach;
        }

        private static void AssemblyRampTimes(out float rampUpTime, out float rampDownTime)
        {
            rampUpTime = 0f;
            rampDownTime = 0f;
            foreach (Member m in s_members)
            {
                m.Thrusters.CalculateRampTimes(out float up, out float down);
                rampUpTime += m.Frac * up;
                rampDownTime += m.Frac * down;
            }
        }

        private static float Probe(Vector3 dirSRA, Time interval, Vector3 srfFactors,
            ThrusterManager.ActivationRangeType rangeType, float massAsm, float inertiaAsm)
        {
            Vector3 ach = Solve(dirSRA * 1000f, interval, srfFactors, rangeType, massAsm, inertiaAsm, false, null);
            float proj;
            if (dirSRA.Z != 0f && dirSRA.X == 0f && dirSRA.Y == 0f)
                proj = Mathx.Sign(dirSRA.Z) * ach.Z / inertiaAsm;
            else
                proj = (ach.X * dirSRA.X + ach.Y * dirSRA.Y) / massAsm;
            return Mathx.Max(proj, 0f);
        }

        private static void Allocate(Ship pilot, Vector3 desiredSRA, Time interval, Vector3 srfFactors, bool adjustPosition)
        {
            if (HingeJointManager.TryGetAssemblyProperties(pilot, out float massAsm, out float inertiaAsm, out _))
            {
                if (AssemblyFlightDiag.WantsAlloc())
                {
                    Vector3 ach = Solve(desiredSRA, interval, srfFactors,
                        ThrusterManager.ActivationRangeType.Normal, massAsm, inertiaAsm, false, null);
                    var achAccel = new Vector3(ach.X / massAsm, ach.Y / massAsm, ach.Z / inertiaAsm);
                    Vector2 fwd = Vector2.FromAngle(pilot.FlightDirection);
                    Vector2 lat = fwd.Perp();
                    float ex = achAccel.X - desiredSRA.X;
                    float ey = achAccel.Y - desiredSRA.Y;
                    AssemblyFlightDiag.OnAlloc(pilot, desiredSRA, achAccel,
                        ex * fwd.X + ey * fwd.Y, ex * lat.X + ey * lat.Y,
                        s_members.Count, adjustPosition);
                }

                Solve(desiredSRA, interval, srfFactors, ThrusterManager.ActivationRangeType.Normal, massAsm, inertiaAsm, true, pilot);
            }
            AssemblyFlightDiag.OnServo(pilot, desiredSRA, adjustPosition);
        }

        #endregion
    }
}
