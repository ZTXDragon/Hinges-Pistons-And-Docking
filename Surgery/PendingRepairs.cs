using System;
using System.Collections.Generic;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Halfling;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class PendingRepairs
    {
        private static readonly IntVector2 s_zero = IntVector2.Zero;

        internal struct Entry
        {
            public PartRules Rules;
            public IntVector2 Loc;
            public int Rot;
            public bool Flip;
            public bool Blueprint;
            public bool Wreck;
            public PartSaveData Key => new PartSaveData(Rules.ID, Loc + Rules.GetSaveLocationOffset(Rot, Flip), Rot, Flip);
        }

        public static List<Entry> Collect(Ship ship)
        {
            var list = new List<Entry>();
            if (ship?.BlueprintParts == null) return list;
            var index = new Dictionary<(PartRules, IntVector2, int, bool), int>();
            void Add(CommonBasePart p, bool blueprint)
            {
                if (p?.Rules == null) return;
                if (MergeRebuild.IsRodRules(p.Rules)) return;
                var k = (p.Rules, p.Location, p.Rotation, p.FlipX);
                if (index.TryGetValue(k, out int i))
                {
                    Entry e = list[i];
                    if (blueprint) e.Blueprint = true; else e.Wreck = true;
                    list[i] = e;
                    return;
                }
                index[k] = list.Count;
                list.Add(new Entry { Rules = p.Rules, Loc = p.Location, Rot = p.Rotation, Flip = p.FlipX, Blueprint = blueprint, Wreck = !blueprint });
            }
            foreach (BlueprintPart bp in ship.BlueprintParts.UnmatchedParts) Add(bp, true);
            if (ship.DestroyedParts != null)
            {
                foreach (CommonBasePart dp in ship.DestroyedParts)
                {
                    if (dp?.Rules != null && IsBuilt(ship, dp.Rules, dp.Location, dp.Rotation, dp.FlipX)) continue;
                    Add(dp, false);
                }
            }
            return list;
        }

        public static Entry Transformed(Entry e, GridTransform xf)
        {
            if (xf.IsIdentity) return e;
            IntRect r = e.Rules.GetRect(e.Loc, e.Rot);
            r.Size -= IntVector2.One;
            r = r.Rotate90Around(in s_zero, xf.K);
            e.Rot = e.Rules.IsRotateable ? Mathx.Wrap(e.Rot + xf.K, 0, 3) : e.Rot;
            e.Loc = r.Location + xf.T;
            return e;
        }

        public static void AddKeys(List<Entry> entries, GridTransform xf, Dictionary<PartSaveData, PartSaveData> keyMap)
        {
            foreach (Entry e in entries)
            {
                PartSaveData oldKey = e.Key;
                if (!keyMap.ContainsKey(oldKey)) keyMap[oldKey] = Transformed(e, xf).Key;
            }
        }

        public static (int Restored, int Skipped) Restore(Ship ship, List<Entry> entries, GridTransform xf)
        {
            int restored = 0, skipped = 0;
            if (ship?.BlueprintParts == null || entries == null) return (0, 0);
            foreach (Entry src in entries)
            {
                Entry e = Transformed(src, xf);
                try
                {
                    IntRect rect = e.Rules.GetRect(e.Loc, e.Rot);
                    if (IsBuilt(ship, e.Rules, e.Loc, e.Rot, e.Flip)) continue;
                    bool done = false;
                    if (e.Blueprint)
                    {
                        if (ship.BlueprintParts.GetExactPart(e.Rules, e.Loc, e.Rot, e.Flip) != null) done = true;
                        else if (BlueprintCellsFree(ship, rect))
                        {
                            ship.BlueprintParts.CreateBlueprintPart(e.Rules, e.Loc, e.Rot, e.Flip);
                            done = true;
                        }
                    }
                    if (e.Wreck && ship.DestroyedParts != null)
                    {
                        if (!ship.DestroyedParts.HasPartAt(rect))
                        {
                            ship.DestroyedParts.CreateDestroyedPart(e.Rules, e.Loc, e.Rot, e.Flip);
                            done = true;
                        }
                        else if (ship.DestroyedParts.GetExactPart(e.Rules, e.Loc, e.Rot, e.Flip) != null) done = true;
                    }
                    if (done) restored++; else skipped++;
                }
                catch (Exception ex)
                {
                    skipped++;
                    Log.Exception("PendingRepairs.Restore(" + e.Rules.ID + " at " + e.Loc + ")", ex);
                }
            }
            return (restored, skipped);
        }

        private static bool IsBuilt(Ship ship, PartRules rules, IntVector2 loc, int rot, bool flip)
        {
            return ship.Parts.GetExactPart(rules, loc, rot, flip) != null;
        }

        private static bool BlueprintCellsFree(Ship ship, IntRect rect)
        {
            foreach (IntVector2 c in rect)
                if (ship.BlueprintParts[c, PartRectType.Normal] != null) return false;
            return true;
        }

        public static void MatchKeeping(Ship ship, Func<PartInfo, bool> mayRemove, HashSet<IntVector2> reserved = null)
        {
            using (ShipUpdateInfo info = ShipUpdateInfo.Alloc(ship))
            {
                info.MatchBlueprintsToPhysical(matchDestroyed: false);
                info.BlueprintPartsToRemove.RemoveWhere(pi => !mayRemove(pi));
                var removed = new HashSet<IntVector2>();
                foreach (PartInfo pi in info.BlueprintPartsToRemove)
                    foreach (IntVector2 c in pi.Rect) removed.Add(c);
                info.BlueprintPartsToAdd.RemoveWhere(pi =>
                {
                    foreach (IntVector2 c in pi.Rect)
                    {
                        if (reserved != null && reserved.Contains(c)) return true;
                        if (ship.BlueprintParts[c, PartRectType.Normal] != null && !removed.Contains(c)) return true;
                    }
                    return false;
                });
                info.Apply();
            }
        }

        public static HashSet<IntVector2> BlueprintCells(List<Entry> entries, GridTransform xf)
        {
            var cells = new HashSet<IntVector2>();
            foreach (Entry src in entries)
            {
                if (!src.Blueprint) continue;
                Entry e = Transformed(src, xf);
                foreach (IntVector2 c in e.Rules.GetRect(e.Loc, e.Rot)) cells.Add(c);
            }
            return cells;
        }

        public static bool IsFrameStage(Ship ship, CommonBasePart p)
        {
            if (!(p is Part part) || ship?.BlueprintParts == null) return false;
            if (ship.BlueprintParts.GetMatchingPart(part) != null) return false;
            foreach (IntVector2 c in part.Rect)
                if (ship.BlueprintParts[c, PartRectType.Normal] != null) return true;
            return false;
        }

        public static (PartRules, IntVector2, int, bool) KeyOf(PartRules rules, IntVector2 loc, int rot, bool flip) => (rules, loc, rot, flip);

        public static int RemoveStaleWrecks(Ship ship)
        {
            if (ship?.DestroyedParts == null) return 0;
            List<CommonBasePart> stale = null;
            foreach (CommonBasePart dp in ship.DestroyedParts)
                if (dp?.Rules != null && IsBuilt(ship, dp.Rules, dp.Location, dp.Rotation, dp.FlipX)) (stale ??= new List<CommonBasePart>()).Add(dp);
            if (stale == null) return 0;
            foreach (CommonBasePart dp in stale)
            {
                var w = ship.DestroyedParts.GetExactPart(dp.Rules, dp.Location, dp.Rotation, dp.FlipX);
                if (w != null) ship.DestroyedParts.RemoveDestroyedPart(w);
            }
            Log.Info($"Ship {ship.UniqueID}: {stale.Count} stale wreck(s) under their rebuilt part removed.");
            return stale.Count;
        }

        public static void RemoveWrecks(Ship ship, List<Entry> entries)
        {
            if (ship?.DestroyedParts == null) return;
            foreach (Entry e in entries)
            {
                if (!e.Wreck) continue;
                var w = ship.DestroyedParts.GetExactPart(e.Rules, e.Loc, e.Rot, e.Flip);
                if (w != null) ship.DestroyedParts.RemoveDestroyedPart(w);
            }
        }

        public static void Partition(Ship ship, List<Entry> entries, ICollection<Part> farParts, out List<Entry> near, out List<Entry> far)
        {
            near = new List<Entry>();
            far = new List<Entry>();
            var farSet = new HashSet<Part>(farParts);
            var farCells = new HashSet<IntVector2>();
            foreach (Part p in farParts)
                foreach (IntVector2 c in p.GetCells(PartRectType.Normal)) farCells.Add(c);
            int n = entries.Count;
            var cells = new List<IntVector2>[n];
            var touchesNear = new bool[n];
            var isFar = new bool[n];
            for (int i = 0; i < n; i++)
            {
                cells[i] = new List<IntVector2>();
                foreach (IntVector2 c in entries[i].Rules.GetRect(entries[i].Loc, entries[i].Rot)) cells[i].Add(c);
                foreach (IntVector2 c in cells[i])
                {
                    foreach (IntVector2 nb in Neighbours(c))
                    {
                        Part p = ship.Parts[nb, PartRectType.Normal];
                        if (p != null && !farSet.Contains(p)) touchesNear[i] = true;
                        if (farCells.Contains(nb)) isFar[i] = true;
                    }
                }
                if (touchesNear[i]) isFar[i] = false;
            }
            bool grew = true;
            for (int guard = 0; grew && guard < n + 1; guard++)
            {
                grew = false;
                var farEntryCells = new HashSet<IntVector2>();
                for (int i = 0; i < n; i++) if (isFar[i]) foreach (IntVector2 c in cells[i]) farEntryCells.Add(c);
                for (int i = 0; i < n; i++)
                {
                    if (isFar[i] || touchesNear[i]) continue;
                    foreach (IntVector2 c in cells[i])
                    {
                        foreach (IntVector2 nb in Neighbours(c))
                            if (farEntryCells.Contains(nb)) { isFar[i] = true; break; }
                        if (isFar[i]) break;
                    }
                    if (isFar[i]) grew = true;
                }
            }
            for (int i = 0; i < n; i++) (isFar[i] ? far : near).Add(entries[i]);
        }

        private static IEnumerable<IntVector2> Neighbours(IntVector2 c)
        {
            yield return new IntVector2(c.X + 1, c.Y);
            yield return new IntVector2(c.X - 1, c.Y);
            yield return new IntVector2(c.X, c.Y + 1);
            yield return new IntVector2(c.X, c.Y - 1);
        }
    }
}
