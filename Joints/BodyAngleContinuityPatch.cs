using System;
using Halfling.Physics2D.Dynamics;
using Halfling.Scene2D.Physics;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(ScenePhysicsWrapper), "OnLocalTransformChanged")]
    internal static class BodyAngleContinuityPatch
    {
        private const double TWO_PI = Math.PI * 2.0;
        private const int LOG_CAP = 30;
        private static int s_logged;

        static void Prefix(ScenePhysicsWrapper __instance, out float __state)
        {
            __state = float.NaN;
            Body body = __instance.Body;
            if (body != null) __state = body.Rotation;
        }

        static void Postfix(ScenePhysicsWrapper __instance, float __state)
        {
            if (float.IsNaN(__state)) return;
            try
            {
                Body body = __instance.Body;
                if (body == null) return;
                float now = body.Rotation;
                double turns = Math.Round((__state - now) / TWO_PI);
                if (turns == 0.0) return;
                body.Rotation = (float)(now + turns * TWO_PI);
                if (s_logged < LOG_CAP)
                {
                    s_logged++;
                    Log.Info($"Body angle kept continuous: a scene transform write would have turned a body by {-turns:F0} full turn(s) ({__state:F2} -> {now:F2} rad){(s_logged == LOG_CAP ? " (last such line)" : "")}.");
                }
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("BodyAngleContinuityPatch", ex);
            }
        }
    }
}
