using System;
using System.Collections.Generic;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(ShipUpdateInfo), nameof(ShipUpdateInfo.ApplyBlueprints))]
    internal static class BlueprintApplyGuard
    {
        private const int MAX_LISTED = 8;

        public static bool AddFits(IEnumerable<IntVector2> cells, ISet<IntVector2> occupied, ISet<IntVector2> freed)
        {
            foreach (IntVector2 c in cells)
                if (occupied.Contains(c) && !freed.Contains(c)) return false;
            return true;
        }

        static void Prefix(ShipUpdateInfo __instance)
        {
            try
            {
                if (__instance == null || !__instance.HasShip) return;
                Ship ship = __instance.Ship;
                BlueprintPartsManager planned = ship?.BlueprintParts;
                if (planned == null || (__instance.BlueprintPartsToAdd.Count == 0 && __instance.BlueprintPartsToRemove.Count == 0)) return;
                var gone = new List<string>();
                __instance.BlueprintPartsToRemove.RemoveWhere(pi =>
                {
                    if (pi.GetBlueprintPart(ship) != null) return false;
                    gone.Add(BlueprintAudit.Describe(pi.Rules, pi.Location, pi.Rotation, pi.FlipX));
                    return true;
                });
                var freed = new HashSet<IntVector2>();
                foreach (PartInfo pi in __instance.BlueprintPartsToRemove)
                    foreach (IntVector2 c in pi.Rect) freed.Add(c);
                var clashes = new List<string>();
                __instance.BlueprintPartsToAdd.RemoveWhere(pi =>
                {
                    var cells = new List<IntVector2>();
                    var occupied = new HashSet<IntVector2>();
                    foreach (IntVector2 c in pi.Rect)
                    {
                        cells.Add(c);
                        if (planned[c, PartRectType.Normal] != null) occupied.Add(c);
                    }
                    if (AddFits(cells, occupied, freed)) return false;
                    clashes.Add(BlueprintAudit.Describe(pi.Rules, pi.Location, pi.Rotation, pi.FlipX));
                    return true;
                });
                if (gone.Count == 0 && clashes.Count == 0) return;
                Log.Info("Blueprint apply on ship " + ship.UniqueID + ": " + clashes.Count + " planned part(s) not added, their cells are already planned ["
                    + Listed(clashes) + "]; " + gone.Count + " removal(s) skipped, the part is already gone [" + Listed(gone)
                    + "] -- joint surgery changed the blueprint after this edit was made.");
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("BlueprintApplyGuard.Prefix", ex);
            }
        }

        private static string Listed(List<string> items)
        {
            if (items.Count <= MAX_LISTED) return string.Join("; ", items);
            return string.Join("; ", items.GetRange(0, MAX_LISTED)) + "; ... " + (items.Count - MAX_LISTED) + " more";
        }
    }
}
