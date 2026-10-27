using System;
using Cosmoteer.Ships.Parts.Thrusters;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(ThrusterManager), "CalculateMaximumAcceleration")]
    internal static class ThrustProbeScope
    {
        [ThreadStatic] private static int t_depth;

        public static bool Active => t_depth > 0;

        static void Prefix()
        {
            t_depth++;
        }

        static Exception Finalizer(Exception __exception)
        {
            if (t_depth > 0) t_depth--;
            return __exception;
        }
    }
}
