using System;
using System.Linq;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Ftl;
using Halfling.Geometry;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class JumpFuel
    {
        private static bool Carries(Ship ship, out Ship[] segs)
        {
            segs = null;
            if (ship == null || !AssemblyFacade.Enabled || HingeJointManager.ActivePinCount == 0) return false;
            segs = AssemblyAi.SegmentsOf(ship);
            if (segs == null || !ReferenceEquals(AssemblyAi.AnchorFor(ship), ship)) return false;
            return segs.Any(s => !ReferenceEquals(s, ship) && s.Ftl != null && !s.Ftl.HasFtlDrives);
        }

        internal static void ClearCaches(Cosmoteer.Simulation.SimRoot sim)
        {
            if (sim == null || !AssemblyFacade.Enabled || HingeJointManager.ActivePinCount == 0) return;
            foreach (Ship s in sim.Ships)
                if (s?.Ftl != null && s.Ftl.HasFtlDrives) ClearCache(s.Ftl);
        }

        internal static void ClearCache(FtlManager ftl)
        {
            if (Carries(ftl?.Ship, out _)) ftl._cachedJumpCostFactor = null;
        }

        internal static bool TryFactor(FtlManager ftl, out float factor, out int segments)
        {
            factor = 0f;
            segments = 0;
            Ship ship = ftl?.Ship;
            if (!Carries(ship, out Ship[] segs) || !ftl.HasFtlDrives) return false;
            float mass = 0f, weighted = 0f;
            void Weigh(Ship s)
            {
                foreach (Part part in s.Parts)
                    foreach (IntVector2 cell in part.PhysicalRect)
                    {
                        Vector2 c = (Vector2)cell + Vector2.Half;
                        float eff = 0f;
                        foreach (FtlDrive drive in ftl) eff += drive.Rules.GetJumpEfficiency(c.DistanceTo(drive.ShipLocation));
                        mass += part.Rules.Density;
                        weighted += Math.Min(eff, 1f) * part.Rules.Density;
                    }
            }
            Weigh(ship);
            foreach (Ship s in segs)
            {
                if (ReferenceEquals(s, ship) || s.Ftl == null || s.Ftl.HasFtlDrives) continue;
                Weigh(s);
                segments++;
            }
            if (weighted <= 0f) return false;
            factor = mass / weighted * mass;
            return true;
        }
    }

    [HarmonyPatch(typeof(FtlManager), nameof(FtlManager.CalculateJumpCostFactor))]
    internal static class JumpFuelFactorPatch
    {
        static void Postfix(FtlManager __instance, ref float __result)
        {
            try
            {
                if (!JumpFuel.TryFactor(__instance, out float whole, out int segments)) return;
                LogThrottle.Info("JumpFuel|" + __instance.Ship.UniqueID,
                    $"FTL fuel: ship {__instance.Ship.UniqueID} jump cost factor {__result:F1} for its own parts -> {whole:F1} with {segments} segment(s) without drives.");
                __result = whole;
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("JumpFuelFactorPatch", ex);
            }
        }
    }

    [HarmonyPatch(typeof(FtlManager), nameof(FtlManager.GetFtlMapJumpFuelCost), new Type[] { typeof(float) })]
    internal static class JumpFuelMapCachePatch
    {
        static void Prefix(FtlManager __instance)
        {
            try { JumpFuel.ClearCache(__instance); }
            catch (Exception ex) { LogThrottle.Exception("JumpFuelMapCachePatch", ex); }
        }
    }

    [HarmonyPatch(typeof(FtlManager), nameof(FtlManager.GetFtlLocalJumpFuelCost), new Type[] { typeof(float) })]
    internal static class JumpFuelLocalCachePatch
    {
        static void Prefix(FtlManager __instance)
        {
            try { JumpFuel.ClearCache(__instance); }
            catch (Exception ex) { LogThrottle.Exception("JumpFuelLocalCachePatch", ex); }
        }
    }
}
