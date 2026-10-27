using System;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts.Thrusters;
using Halfling.Geometry;
using Halfling.Physics2D.Dynamics;
using Halfling.Timing;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(ThrusterManager), "CalculateDesiredSRA")]
    public static class ThrusterAssemblyMassPatchSRA
    {
        static bool Prefix(ThrusterManager __instance, Vector3 desiredSRA, Time interval,
            Vector3 srfFactors, ThrusterManager.ActivationRangeType activationRangeType, ref Vector3 __result)
        {
            try
            {
                if (!AssemblyFlightFeatures.Enabled) return true;
                Ship ship = __instance.Ship;
                Body body = ship?.Physics?.Body;
                if (body == null) return true;
                bool isProbe = ThrustProbeScope.Active;

                if (!HingeJointManager.TryGetAssemblyProperties(ship, out float mass, out float inertia, out Vector2 com) ||
                    mass <= 0f || inertia <= 0f)
                {
                    bool jointed = HingeJointManager.ActivePinCount > 0 && HingeJointManager.IsJointed(ship);
                    if (jointed) FlightProbe.OnSra(ship, desiredSRA, isProbe, !isProbe);
                    return true;
                }

                if (isProbe)
                {
                    float pfx = desiredSRA.X * mass;
                    float pfy = desiredSRA.Y * mass;
                    Vector3 pAchieved = __instance.CalculateDesiredSRF(
                        new Vector3(pfx, pfy, desiredSRA.Z * inertia), interval, srfFactors, activationRangeType);
                    __result = new Vector3(pAchieved.X / mass, pAchieved.Y / mass, pAchieved.Z / inertia);
                    AssemblyFlightDiag.OnSolve(ship, __instance, desiredSRA, __result, mass, inertia, com, true);
                    FlightProbe.OnSra(ship, desiredSRA, true, false);
                    return false;
                }

                float mOwn = body.Mass;
                float tauDen = inertia * (mOwn / mass);
                if (mOwn <= 0f || tauDen <= 0f) return true;

                float fx = desiredSRA.X * mOwn;
                float fy = desiredSRA.Y * mOwn;
                float tauShare = desiredSRA.Z * tauDen;

                Vector3 achieved = __instance.CalculateDesiredSRF(
                    new Vector3(fx, fy, tauShare), interval, srfFactors, activationRangeType);

                __result = new Vector3(achieved.X / mOwn, achieved.Y / mOwn, achieved.Z / tauDen);

                AssemblyEcho.RecordPilotDesire(ship, desiredSRA, interval, srfFactors);
                AssemblyFlightDiag.OnSolve(ship, __instance, desiredSRA, __result, mass, inertia, com, false);
                FlightProbe.OnSra(ship, desiredSRA, false, true);
                return false;
            }
            catch (Exception ex)
            {
                Log.Exception("ThrusterAssemblyMassPatchSRA.Prefix", ex);
                return true;
            }
        }

        static void Postfix(ThrusterManager __instance, Time interval, Vector3 srfFactors, Vector3 __result)
        {
            try
            {
                if (!AssemblyFlightFeatures.Enabled || HingeJointManager.ActivePinCount == 0) return;
                Ship ship = __instance.Ship;
                if (ship?.Physics?.Body == null || !HingeJointManager.IsJointed(ship)) return;
                if (HingeJointManager.TryGetAssemblyProperties(ship, out float mass, out float inertia, out _) && mass > 0f && inertia > 0f) return;
                if (ThrustProbeScope.Active) return;
                AssemblyEcho.RecordPilotDesire(ship, __result, interval, srfFactors);
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("ThrusterAssemblyMassPatchSRA.Postfix", ex);
            }
        }
    }

    internal static class AssemblyEcho
    {
        public sealed class Desire
        {
            public readonly Vector3 SRA;
            public readonly float PilotRotation;
            public readonly Time Interval;
            public readonly Vector3 SrfFactors;
            public readonly int StampTick;

            public Desire(Vector3 sra, float pilotRotation, Time interval, Vector3 srfFactors, int stampTick)
            {
                SRA = sra;
                PilotRotation = pilotRotation;
                Interval = interval;
                SrfFactors = srfFactors;
                StampTick = stampTick;
            }
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Ship, Desire> s_desires =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Ship, Desire>();

        public static void RecordPilotDesire(Ship ship, Vector3 desiredSRA, Time interval, Vector3 srfFactors)
        {
            try
            {
                if (ship?.Physics?.Body == null) return;
                if (!ReferenceEquals(HingeJointManager.GetDesignatedPilot(ship), ship)) return;
                s_desires.AddOrUpdate(ship, new Desire(desiredSRA, ship.Physics.Body.Rotation, interval, srfFactors,
                    ship.Sim?.Tick ?? SimTicks.NEVER));
            }
            catch { }
        }

        public static Desire Latest(Ship ship)
        {
            return ship != null && s_desires.TryGetValue(ship, out Desire d) ? d : null;
        }

        public static Desire GetFresh(Ship pilot)
        {
            return FlightStage.DesireOf(pilot);
        }
    }

    internal static class AssemblyFlightDiag
    {
        public static readonly bool ENABLED = false;

        private sealed class LastLog { public long RealMs; public long ProbeMs; public long DragMs; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Ship, LastLog> s_last =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Ship, LastLog>();

        public static void OnSolve(Ship ship, ThrusterManager tm, Vector3 desired, Vector3 achieved,
            float mass, float inertia, Vector2 com, bool isProbe)
        {
            if (!ENABLED) return;
            try
            {
                float d2 = desired.X * desired.X + desired.Y * desired.Y + desired.Z * desired.Z;
                if (d2 < 0.0001f) return;
                LastLog last = s_last.GetValue(ship, _ => new LastLog());
                long now = Environment.TickCount64;
                if (isProbe)
                {
                    if (now - last.ProbeMs < 1000) return;
                    last.ProbeMs = now;
                }
                else
                {
                    if (now - last.RealMs < 1000) return;
                    last.RealMs = now;
                }

                float dot = desired.X * achieved.X + desired.Y * achieved.Y + desired.Z * achieved.Z;
                float magic = tm._magicSRF.XY.Length;
                string kind = isProbe ? "PROBE" : "SOLVE";
                string flag = dot < 0f ? " FAILSAFE-RISK(dot<0)" : "";
                Log.Info($"FLIGHT {kind} ship {ship.UniqueID}: desired=({desired.X:F2},{desired.Y:F2},{desired.Z:F3}) achieved=({achieved.X:F2},{achieved.Y:F2},{achieved.Z:F3}) M={mass:F0} I={inertia:F0} COM=({com.X:F0},{com.Y:F0}) |magic|={magic:F1}{flag}");
            }
            catch { }
        }

        public static void OnDrag(Ship ship, float speed, float newSpeed, float dt, float dragMoment, float omega)
        {
            if (!ENABLED) return;
            try
            {
                LastLog last = s_last.GetValue(ship, _ => new LastLog());
                long now = Environment.TickCount64;
                if (now - last.DragMs < 1000) return;
                last.DragMs = now;
                float k = speed > 0f ? newSpeed / speed : 1f;
                float impliedCoef = dt > 0f ? (1f - k) / dt : 0f;
                Log.Info($"DRAG ship {ship.UniqueID}: speed={speed:F2} k={k:F5} impliedCoef={impliedCoef:F3} (expect 0.400) dragMoment={dragMoment:F1} omega={omega:F4} [measurement only, no torque applied]");
            }
            catch { }
        }

        private static long s_lastCrabMs;

        public static void OnCrab(Ship pilot, float crabDeg, float latPilot, float latCom, float omega, float speed)
        {
            if (!ENABLED) return;
            try
            {
                long now = Environment.TickCount64;
                if (now - s_lastCrabMs < 1000) return;
                s_lastCrabMs = now;
                Log.Info($"CRAB pilot {pilot.UniqueID}: crabDeg={crabDeg:F2} latPilot={latPilot:F3} latCom={latCom:F3} omega={omega:F4} speed={speed:F2}");
            }
            catch { }
        }

        private static long s_lastAllocMs;

        public static bool WantsAlloc()
        {
            return ENABLED && Environment.TickCount64 - s_lastAllocMs >= 1000;
        }

        public static void OnAlloc(Ship pilot, Vector3 demand, Vector3 achievedAccel,
            float fwdErr, float latErr, int members, bool adjustPosition)
        {
            if (!ENABLED) return;
            try
            {
                long now = Environment.TickCount64;
                if (now - s_lastAllocMs < 1000) return;
                s_lastAllocMs = now;
                Log.Info($"ALLOC pilot {pilot.UniqueID} [{(adjustPosition ? "ARRIVAL" : "CRUISE")}]: demand=({demand.X:F2},{demand.Y:F2},{demand.Z:F3}) achieved=({achievedAccel.X:F2},{achievedAccel.Y:F2},{achievedAccel.Z:F3}) fwdErr={fwdErr:F3} latErr={latErr:F3} members={members}");
            }
            catch { }
        }

        private static long s_lastGeomMs;

        public static void OnGeometry(Ship pilot, Vector2 goal, Vector2 flightRef, Vector2 comWorld,
            bool adjustPosition, float satRadius)
        {
            if (!ENABLED) return;
            try
            {
                long now = Environment.TickCount64;
                if (now - s_lastGeomMs < 1000) return;
                s_lastGeomMs = now;
                float pilotErr = (goal - flightRef).Length;
                float comErr = (goal - comWorld).Length;
                float offset = (flightRef - comWorld).Length;
                string jam = offset > satRadius ? " QUEUE-JAM-RISK(offset>satRadius)" : "";
                Log.Info($"GEOM pilot {pilot.UniqueID} [{(adjustPosition ? "ARRIVAL" : "CRUISE")}]: pilotErr={pilotErr:F2} comErr={comErr:F2} offset={offset:F2} satRadius={satRadius:F2}{jam}");
            }
            catch { }
        }

        private static long s_lastServoMs;

        public static void OnServo(Ship pilot, Vector3 desiredSRA, bool adjustPosition)
        {
            if (!ENABLED) return;
            try
            {
                long now = Environment.TickCount64;
                if (now - s_lastServoMs < 1000) return;
                s_lastServoMs = now;
                string mode = adjustPosition ? "ARRIVAL(strafe)" : "CRUISE(face+burn)";
                Log.Info($"SERVO pilot {pilot.UniqueID} [{mode}]: desired SRA=({desiredSRA.X:F2},{desiredSRA.Y:F2},{desiredSRA.Z:F3})");
            }
            catch { }
        }
    }
}
