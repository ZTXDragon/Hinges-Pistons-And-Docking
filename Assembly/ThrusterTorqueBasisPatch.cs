using System;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts.Thrusters;
using Halfling.Geometry;
using Halfling.Physics2D.Dynamics;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(Thruster), "GetSRF")]
    public static class ThrusterTorqueBasisPatch
    {
        public static bool TryGetOffset(Ship ship, out float rx, out float ry)
        {
            rx = 0f; ry = 0f;
            if (!AssemblyFlightFeatures.Enabled) return false;
            Body body = ship?.Physics?.Body;
            if (body == null || !HingeJointManager.IsRigidlyJointed(ship)) return false;
            if (!HingeJointManager.TryGetAssemblyProperties(ship, out _, out _, out Vector2 com)) return false;
            float wx = body.WorldCenter.X - com.X;
            float wy = body.WorldCenter.Y - com.Y;
            double a = 0.0 - (float)ship.DetRotation;
            float sin = (float)DetMath.Sin(a);
            float cos = (float)DetMath.Cos(a);
            rx = wx * cos - wy * sin;
            ry = wx * sin + wy * cos;
            return true;
        }

        static void Postfix(Thruster __instance, ref Vector3 __result)
        {
            try
            {
                if (!TryGetOffset(__instance.Ship, out float rx, out float ry)) return;
                __result.Z += rx * __result.Y - ry * __result.X;
            }
            catch (Exception ex)
            {
                Log.Exception("ThrusterTorqueBasisPatch.Postfix", ex);
            }
        }
    }
}
