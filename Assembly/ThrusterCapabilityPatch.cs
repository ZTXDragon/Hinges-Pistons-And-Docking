using System;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts.Thrusters;
using Halfling.Geometry;
using Halfling.Timing;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(ThrusterManager), "CalculateMaximumAccelerationAndRampTimeCached")]
    public static class ThrusterCapabilityCacheBypassPatch
    {
        static bool Prefix(ThrusterManager __instance, Vector3 direction, Time interval,
            Vector3 srfFactors, ThrusterManager.ActivationRangeType activationRangeType,
            ref (float Accel, float RampUpTime, float RampDownTime) __result)
        {
            try
            {
                if (!AssemblyFlightFeatures.Enabled) return true;
                Ship ship = __instance.Ship;
                if (ship == null || !HingeJointManager.IsRigidlyJointed(ship)) return true;
                float accel = __instance.CalculateMaximumAcceleration(direction, interval, srfFactors, activationRangeType);
                __instance.CalculateRampTimes(out float rampUp, out float rampDown);
                __result = (accel, rampUp, rampDown);
                return false;
            }
            catch (Exception ex)
            {
                Log.Exception("ThrusterCapabilityCacheBypassPatch.Prefix", ex);
                return true;
            }
        }
    }
}
