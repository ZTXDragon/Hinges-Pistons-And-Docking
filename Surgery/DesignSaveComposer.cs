using System;
using System.Collections.Generic;
using System.Linq;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Weapons;
using Cosmoteer.Ships.Paint;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.ObjectBits;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class DesignSaveComposer
    {
        public static bool[] Keep(List<(int Owner, int Left, int Top, int Width, int Height)> parts)
        {
            var keep = new bool[parts.Count];
            var taken = new HashSet<(int, int)>();
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].Owner != 0) continue;
                keep[i] = true;
                Mark(taken, parts[i]);
            }
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].Owner == 0) continue;
                if (Hits(taken, parts[i])) continue;
                keep[i] = true;
                Mark(taken, parts[i]);
            }
            return keep;
        }

        private static void Mark(HashSet<(int, int)> taken, (int Owner, int Left, int Top, int Width, int Height) p)
        {
            for (int x = p.Left; x < p.Left + p.Width; x++)
                for (int y = p.Top; y < p.Top + p.Height; y++)
                    taken.Add((x, y));
        }

        private static bool Hits(HashSet<(int, int)> taken, (int Owner, int Left, int Top, int Width, int Height) p)
        {
            for (int x = p.Left; x < p.Left + p.Width; x++)
                for (int y = p.Top; y < p.Top + p.Height; y++)
                    if (taken.Contains((x, y))) return true;
            return false;
        }

        private static void Put<T>(OBNode node, string name, T value)
        {
            OBNode child = node.GetOrMakeChild(name);
            child.ChangeToUnset();
            App.ObjectBitsSerializer.Write(child, value);
        }

        public static void Compose(Ship saved, OBNode node)
        {
            Ship[] segs = AssemblyAi.SegmentsOf(saved);
            if (segs == null) return;
            var ships = new List<Ship> { saved };
            ships.AddRange(segs.Where(s => s != null && !ReferenceEquals(s, saved)));

            var boxes = new List<(int, int, int, int, int)>();
            var saveData = new List<(int Owner, PartSaveData Psd)>();
            for (int o = 0; o < ships.Count; o++)
            {
                foreach (PartSaveData psd in ships[o].BlueprintParts.GetSaveData())
                {
                    IntRect r;
                    try { r = new PartInfo(psd, ships[o].Rules).Rect; }
                    catch { continue; }
                    boxes.Add((o, r.Left, r.Top, r.Width, r.Height));
                    saveData.Add((o, psd));
                }
            }
            bool[] keep = Keep(boxes);
            var kept = new HashSet<PartSaveData>[ships.Count];
            for (int o = 0; o < ships.Count; o++) kept[o] = new HashSet<PartSaveData>();
            var parts = new List<PartSaveData>();
            int dropped = 0;
            for (int i = 0; i < saveData.Count; i++)
            {
                if (!keep[i]) { dropped++; continue; }
                kept[saveData[i].Owner].Add(saveData[i].Psd);
                parts.Add(saveData[i].Psd);
            }

            var toggles = new Dictionary<(PartSaveData, ID<PartToggleGuiRules>), int>();
            var colors = new Dictionary<(PartSaveData, ID<PartColorGuiRules>), Color>();
            var groups = new Dictionary<int, HashSet<PartSaveData>>();
            var consumption = new Dictionary<(PartSaveData, ID<PartComponentRules>), bool>();
            var supply = new Dictionary<(PartSaveData, ID<PartComponentRules>), bool>();
            var bindings = new Dictionary<(PartSaveData, ID<PartComponentRules>), int?>();
            var coverages = new Dictionary<(PartSaveData, ID<PartComponentRules>), MPValue<WeaponCoverage>>();
            var doors = new List<DoorSaveData>();
            var doorSeen = new HashSet<DoorSaveData>();
            for (int o = 0; o < ships.Count; o++)
            {
                Ship s = ships[o];
                HashSet<PartSaveData> mine = kept[o];
                ShipMetadata meta = s.Metadata;
                foreach (KeyValuePair<(PartSaveData, ID<PartToggleGuiRules>), int> kv in meta.PartUIToggleStates.GetMergedBlueprintStates())
                    if (mine.Contains(kv.Key.Item1)) toggles.TryAdd(kv.Key, kv.Value);
                foreach (KeyValuePair<(PartSaveData, ID<PartColorGuiRules>), Color> kv in meta.PartUIColorValues.GetMergedBlueprintStates())
                    if (mine.Contains(kv.Key.Item1)) colors.TryAdd(kv.Key, kv.Value);
                foreach (KeyValuePair<int, HashSet<PartSaveData>> g in meta.PartControlGroups)
                    foreach (PartSaveData psd in g.Value)
                        if (mine.Contains(psd))
                        {
                            if (!groups.TryGetValue(g.Key, out HashSet<PartSaveData> set)) groups[g.Key] = set = new HashSet<PartSaveData>();
                            set.Add(psd);
                        }
                foreach (KeyValuePair<(PartSaveData, ID<PartComponentRules>), bool> kv in meta.ResourceConsumptionToggles)
                    if (mine.Contains(kv.Key.Item1)) consumption.TryAdd(kv.Key, kv.Value);
                foreach (KeyValuePair<(PartSaveData, ID<PartComponentRules>), bool> kv in meta.ResourceSupplyToggles)
                    if (mine.Contains(kv.Key.Item1)) supply.TryAdd(kv.Key, kv.Value);
                foreach (KeyValuePair<(PartSaveData, ID<PartComponentRules>), int?> kv in meta.WeaponDirectControlBindings)
                    if (mine.Contains(kv.Key.Item1)) bindings.TryAdd(kv.Key, kv.Value);
                foreach (KeyValuePair<(PartSaveData, ID<PartComponentRules>), MPValue<WeaponCoverage>> kv in meta.CustomWeaponCoverages)
                    if (mine.Contains(kv.Key.Item1) && kv.Value.Confirmed != default(WeaponCoverage))
                        coverages.TryAdd(kv.Key, new MPValue<WeaponCoverage>(kv.Value.Confirmed));
                foreach (DoorSaveData d in s.BlueprintDoors.GetSaveData())
                    if (doorSeen.Add(d)) doors.Add(d);
            }

            Put(node, "Parts", parts.ToArray());
            Put(node, "Doors", doors.ToArray());
            Put(node, "PartUIToggleStates", toggles);
            Put(node, "PartUIColorValues", colors);
            Put(node, "PartControlGroups", groups);
            Put(node, "ResourceConsumptionToggles", consumption);
            Put(node, "ResourceSupplyToggles", supply);
            Put(node, "WeaponDirectControlBindings", bindings);
            Put(node, "CustomWeaponCoverages", coverages);
            int layers = saved.DecalLayers.Length;
            for (int j = 0; j < layers; j++)
            {
                var decals = new List<Decal>();
                foreach (Ship s in ships)
                    if (s.DecalLayers.Length > j) decals.AddRange(s.DecalLayers[j].GetSaveData());
                Put(node, $"Decals{j + 1}", decals.ToArray());
            }
            Log.Info($"Ship file: ship {saved.UniqueID} saved with {ships.Count - 1} segment(s) at home ({parts.Count} blueprint part(s)"
                + (dropped > 0 ? $", {dropped} segment part(s) dropped where the main ship holds the cell" : "") + ").");
        }
    }

    [HarmonyPatch(typeof(Ship), nameof(Ship.SaveDesign), new Type[] { typeof(OBNode) })]
    internal static class DesignSaveComposerPatch
    {
        static void Postfix(Ship __instance, OBNode node)
        {
            try
            {
                if (!AssemblyFacade.Enabled || node == null || HingeJointManager.ActivePinCount == 0) return;
                DesignSaveComposer.Compose(__instance, node);
            }
            catch (Exception ex)
            {
                Log.Exception("DesignSaveComposer", ex);
            }
        }
    }
}
