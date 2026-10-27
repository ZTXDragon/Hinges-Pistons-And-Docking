using System;
using System.Collections.Generic;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Doors;
using Cosmoteer.Ships.Parts;
using HarmonyLib;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(MPUpdateShipInput), nameof(MPUpdateShipInput.Execute))]
    internal static class BayInstantBuildExecutePatch
    {
        [ThreadStatic] internal static MPUpdateShipInput t_executing;
        [ThreadStatic] internal static Ship t_capture;

        static void Prefix(MPUpdateShipInput __instance, out MPUpdateShipInput __state)
        {
            __state = t_executing;
            t_executing = __instance;
            t_capture = null;
        }

        static void Postfix()
        {
            Ship ship = t_capture;
            t_capture = null;
            if (ship == null) return;
            try { BayManager.CaptureAfterBuild(ship); }
            catch (Exception ex) { LogThrottle.Exception("BayInstantBuildExecutePatch.Postfix", ex); }
        }

        static void Finalizer(MPUpdateShipInput __state)
        {
            t_executing = __state;
            t_capture = null;
        }
    }

    [HarmonyPatch(typeof(ShipUpdateInfo), nameof(ShipUpdateInfo.GetTotalPhysicalCost))]
    internal static class BayInstantBuildCostPatch
    {
        static void Prefix(ShipUpdateInfo __instance)
        {
            MPUpdateShipInput input = BayInstantBuildExecutePatch.t_executing;
            if (input == null || !ReferenceEquals(input.UpdateInfo, __instance)) return;
            try
            {
                if ((input.Flags & UpdateShipFlags.Build) == 0 || !__instance.HasShip) return;
                Ship ship = __instance.Ship;
                if (ship?.Sim == null || __instance.PartsToAdd.Count == 0) return;
                HashSet<PartInfo> keep = BayManager.InstantDesignParts(ship, __instance.PartsToAdd);
                if (keep.Count == 0) return;
                var cells = new HashSet<IntVector2>();
                foreach (PartInfo pi in keep)
                    foreach (IntVector2 c in pi.Rect) cells.Add(c);
                int parts = __instance.PartsToAdd.RemoveWhere(keep.Contains);
                int doors = __instance.DoorsToAdd.RemoveWhere(d => cells.Contains(d.Location.Cell) || cells.Contains(d.Location.OppositeCell));
                BayInstantBuildExecutePatch.t_capture = ship;
                Log.Info($"Bay: {parts} parts and {doors} doors placed as a bay design on ship {ship.UniqueID} go to the bay (the game neither builds nor charges them).");
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("BayInstantBuildCostPatch", ex);
            }
        }
    }
}
