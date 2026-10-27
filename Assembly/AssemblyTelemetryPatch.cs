using System;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts.Thrusters;
using Halfling.Geometry;
using Halfling.Physics2D.Dynamics;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(ThrusterManager), "ApplyThrusterForces")]
    public static class AssemblyTelemetryPatch
    {
        static void Postfix(ThrusterManager __instance)
        {
            try
            {
                if (!AssemblyFlightFeatures.Enabled) return;
                Ship ship = __instance.Ship;
                Body body = ship?.Physics?.Body;
                if (body == null) return;
                if (!HingeJointManager.TryGetAssemblyProperties(ship, out float mass, out float inertia, out Vector2 com) ||
                    mass <= 0f || inertia <= 0f)
                    return;

                bool magicApplied = __instance._orderedSoftOpThrusters.Count > 0;
                Vector2 magicW = magicApplied
                    ? __instance._magicSRF.XY.RotateAroundOrigin(ship.DetRotation)
                    : Vector2.Zero;

                float cx = com.X - body.WorldCenter.X;
                float cy = com.Y - body.WorldCenter.Y;
                float corrective = cx * magicW.Y - cy * magicW.X;
                if (corrective != 0f)
                    body.ApplyTorque(corrective);

                float rx = body.WorldCenter.X - com.X;
                float ry = body.WorldCenter.Y - com.Y;
                float fx = __instance.WorldLinearAcceleration.X * body.Mass;
                float fy = __instance.WorldLinearAcceleration.Y * body.Mass;
                float tauOwn = (float)__instance.AngularAcceleration * body.Inertia;
                float tauAsm = tauOwn + (rx * (fy - magicW.Y) - ry * (fx - magicW.X));
                __instance.AngularAcceleration = tauAsm / inertia;
            }
            catch (Exception ex)
            {
                Log.Exception("AssemblyTelemetryPatch.Postfix", ex);
            }
        }
    }
}
