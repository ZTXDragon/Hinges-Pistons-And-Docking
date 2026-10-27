using System;
using System.Collections.Generic;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(StructureContiguityManager), "SplitIntoNewShips", new Type[0])]
    internal static class EngineSplitResumePatch
    {
        static void Postfix(StructureContiguityManager __instance)
        {
            try
            {
                HingeJointManager.ResumeSuspended();
                RemoveLeftoverBlueprints(__instance?.Ship);
                MergeRebuild.MarkEngineSplit();
            }
            catch (Exception ex)
            {
                Log.Exception("EngineSplitResumePatch.Postfix", ex);
            }
        }

        private static void RemoveLeftoverBlueprints(Ship ship)
        {
            if (ship == null || !HingeJointManager.IsJointed(ship)) return;
            var members = new List<Ship>();
            HingeJointManager.GetAssemblyShips(ship, members, rigidOnly: false);
            if (members.Count <= 1) return;
            var stale = new List<BlueprintPart>();
            foreach (BlueprintPart bp in ship.BlueprintParts.UnmatchedParts)
            {
                foreach (Ship m in members)
                {
                    if (ReferenceEquals(m, ship) || m?.Parts == null) continue;
                    Part p = m.Parts[bp.Location, PartRectType.Normal];
                    if (p != null && p.Location == bp.Location && ReferenceEquals(p.Rules, bp.Rules)) { stale.Add(bp); break; }
                }
            }
            if (stale.Count == 0) return;
            using (ShipUpdateInfo info = ShipUpdateInfo.Alloc(ship))
            {
                foreach (BlueprintPart bp in stale) info.BlueprintPartsToRemove.Add(bp);
                info.Apply();
            }
            Log.Info($"Engine split: removed {stale.Count} stale blueprint(s) from ship {ship.UniqueID} for parts now on jointed segments.");
        }
    }
}
