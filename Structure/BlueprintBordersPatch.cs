using System;
using System.Linq;
using System.Reflection;
using Cosmoteer.Ships.Blueprints;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch]
    internal static class BlueprintBordersPatch
    {
        private static bool Prepare(MethodBase original) => original != null || TargetMethod() != null;

        static MethodBase TargetMethod()
        {
            return typeof(BlueprintPart)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name.EndsWith("UpdateValidity", StringComparison.Ordinal));
        }

        static void Postfix(BlueprintPart __instance)
        {
            try
            {
                if ((__instance.InvalidReasons & InvalidReasons.MaxBordersExceeded) == 0) return;
                if (__instance.MatchingPhysicalPart == null) return;
                __instance.InvalidReasons &= ~InvalidReasons.MaxBordersExceeded;
            }
            catch (Exception ex)
            {
                Log.Exception("BlueprintBordersPatch.Postfix", ex);
            }
        }
    }
}
