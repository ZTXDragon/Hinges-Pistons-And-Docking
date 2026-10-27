using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogicStoreProbe
    {
        public const string PREFIX = LogicStore.PREFIX;

        public static bool CarriesKey(string key) =>
            key != null && (key.StartsWith(PREFIX, StringComparison.Ordinal) || key.StartsWith(GripStore.PREFIX, StringComparison.Ordinal)
            || key.StartsWith(RailToggles.VALUE_PREFIX, StringComparison.Ordinal) || key == ArmToggles.AngleId.ToString()
            || key.StartsWith("ztx_bay_", StringComparison.Ordinal) || key == DockThrust.ThrustToggleId.ToString());

        private static int s_lastCopyLog;

        [HarmonyPatch(typeof(PartMetadata), MethodType.Constructor,
            new Type[] { typeof(Ship), typeof(BlueprintPart), typeof(Predicate<BlueprintPart>), typeof(bool), typeof(bool), typeof(bool) })]
        internal static class CopyPatch
        {
            [HarmonyPostfix]
            private static void Postfix(PartMetadata __instance, Ship ship, BlueprintPart part, bool isBlueprintMode)
            {
                try
                {
                    if (ship == null || part == null) return;
                    var psd = new PartSaveData(part);
                    int added = 0;
                    foreach (KeyValuePair<(PartSaveData Part, ID<PartToggleGuiRules> ComponentID), (int? State, int? BlueprintState)> kv in ship.Metadata.PartUIToggleStates)
                    {
                        if (!kv.Key.Part.Equals(psd)) continue;
                        string key = kv.Key.ComponentID.ToString();
                        if (!CarriesKey(key)) continue;
                        int? v = isBlueprintMode ? (kv.Value.BlueprintState ?? kv.Value.State) : kv.Value.State;
                        if (v == null) continue;
                        if (__instance.UIToggles == null) __instance.UIToggles = new Dictionary<ID<PartToggleGuiRules>, int>();
                        __instance.UIToggles[kv.Key.ComponentID] = v.Value;
                        added++;
                    }
                    if (added > 0 && Environment.TickCount - s_lastCopyLog > 2000)
                    {
                        s_lastCopyLog = Environment.TickCount;
                        Log.Info("console storage: build-mode copy of " + part.Rules.ID + " at " + psd.Location + " carries " + added + " storage-v2 entr" + (added == 1 ? "y" : "ies") + ".");
                    }
                }
                catch (Exception ex) { Log.Exception("LogicStoreProbe.CopyPatch", ex); }
            }
        }
    }
}
