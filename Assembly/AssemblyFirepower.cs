using System;
using System.Collections.Generic;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Weapons;
using Halfling.Collections;
using Halfling.Geometry;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class AssemblyFirepower
    {
        public static float CombinedRange(float anchorRange, float anchorBoundingRadius, float[] ranges, float[] offsets)
        {
            float best = anchorRange;
            if (ranges == null || offsets == null) return best;
            int n = Math.Min(ranges.Length, offsets.Length);
            for (int i = 0; i < n; i++)
                best = Math.Max(best, ranges[i] + offsets[i] - anchorBoundingRadius);
            return best;
        }

        public static bool CountsForFirepower(bool shouldEstimate, bool onlyWithResources, bool hasResources)
        {
            return shouldEstimate && (!onlyWithResources || hasResources);
        }

        [HarmonyPatch(typeof(AttackCommand), "GetMaxWeaponRange")]
        private static class MaxRangePatch
        {
            static void Postfix(AttackCommand __instance, bool useIdealRange, ref float __result)
            {
                try
                {
                    Ship anchor = __instance.Ship;
                    AssemblyReach.SegmentWeapons arms = AssemblyReach.ArmsOf(anchor);
                    if (arms?.Ranged == null || arms.Ranged.Length == 0 || __instance.Target == null) return;
                    float targetRadius = AssemblyReach.RadiusForCommands(__instance.Target);
                    Vector2 centre = anchor.DetWorldCenter;
                    var ranges = new float[arms.Ranged.Length];
                    var offsets = new float[arms.Ranged.Length];
                    for (int i = 0; i < arms.Ranged.Length; i++)
                    {
                        Weapon w = arms.Ranged[i];
                        if (w?.Ship == null) continue;
                        ranges[i] = w.GetEffectiveRange(WeaponCoverageMode.Confirmed, allowStretchedRange: false, useIdealRange ? targetRadius : (float?)null);
                        offsets[i] = centre.DistanceTo(w.DetWorldLocation);
                    }
                    __result = CombinedRange(__result, anchor.Parts.BoundingRadius, ranges, offsets);
                }
                catch (Exception ex)
                {
                    LogThrottle.Exception("AssemblyFirepower.MaxRange", ex);
                }
            }
        }

        [HarmonyPatch(typeof(AttackCommand), "EstimateFirepowerTowards")]
        private static class FirepowerPatch
        {
            static void Postfix(AttackCommand __instance, Vector2 testPoint, bool useIdealRange, bool onlyWithResources,
                ReversibleDictionary<Weapon, Part> selfTargetMap, Dictionary<Weapon, float> aiFirepowerRatingCache, ref float __result)
            {
                try
                {
                    Ship anchor = __instance.Ship;
                    AssemblyReach.SegmentWeapons arms = AssemblyReach.ArmsOf(anchor);
                    if (arms?.Operational == null || arms.Operational.Length == 0 || __instance.Target == null) return;
                    float? idealRadius = useIdealRange ? AssemblyReach.RadiusForCommands(__instance.Target) : (float?)null;
                    Vector2 world = anchor.DetTransformPointToWorld(testPoint);
                    float added = 0f;
                    for (int i = 0; i < arms.Operational.Length; i++)
                    {
                        Weapon w = arms.Operational[i];
                        Ship seg = w?.Ship;
                        if (seg == null || !ReferenceEquals(seg.Sim, anchor.Sim)) continue;
                        if (!CountsForFirepower(true, onlyWithResources, arms.HasResources[i])) continue;
                        if (w.IsInCoverage(seg.DetTransformPointFromWorld(world), WeaponCoverageMode.Confirmed, checkLOS: true, idealRadius))
                            added += w.GetAIFirepowerRating(selfTargetMap, aiFirepowerRatingCache);
                    }
                    __result += added;
                }
                catch (Exception ex)
                {
                    LogThrottle.Exception("AssemblyFirepower.Estimate", ex);
                }
            }
        }
    }
}
