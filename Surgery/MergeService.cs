using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class MergeService
    {
        public const bool ENABLED = true;

        public const bool REBUILD_ENABLED = true;

        private const int RETRY_TICKS = 30;
        private const int MAX_FAILURES = 3;
        private const int SETTLE_TICKS = 0;
        private const int GRIP_FIRST_TICKS = 2 * RETRY_TICKS;

        public const int POPOUT_OFF = 0;
        private static readonly ID<PartToggleGuiRules> s_mergeToggleId = new ID<PartToggleGuiRules>("ztx_piston_merge");

        private sealed class Runtime
        {
            public int RetryTick;
            public int Failures;
            public bool Blocked;
            public string Why = "";
            public int LastTrigger = -1;
            public int AlignedSince = -1;
        }

        private static readonly ConditionalWeakTable<ZtxPistonBaseComponent, Runtime> s_rt =
            new ConditionalWeakTable<ZtxPistonBaseComponent, Runtime>();

        private static Runtime Rt(ZtxPistonBaseComponent p) => s_rt.GetValue(p, _ => new Runtime());

        public static bool IsMerged(Part basePart)
        {
            return ENABLED && basePart?.Ship != null && MergeStore.ReadState(basePart) == MergeState.Merged;
        }

        public static bool IsHeld(ZtxHingeBaseComponent b)
        {
            return ENABLED && b is ZtxPistonBaseComponent && IsMerged(b.Part);
        }

        public static bool MergeWanted(ZtxPistonBaseComponent piston, out bool blocked)
        {
            blocked = false;
            if (!ENABLED || piston?.Part == null) return false;
            if (!HingeShipManager.IsHingeActive(piston.Part)) return false;
            bool want = Wanted(piston, false, 0, out _, out _);
            if (want && s_rt.TryGetValue(piston, out Runtime rt)) blocked = rt.Blocked;
            return want;
        }

        public static bool TryGetMergedSlide(Part basePart, out int k)
        {
            k = 0;
            if (!IsMerged(basePart)) return false;
            k = MergeStore.ReadSlide(basePart);
            return true;
        }

        private static bool Wanted(ZtxPistonBaseComponent piston, bool merged, int mergedSlide, out string waitWhy, out int needSlide)
        {
            Part part = piston.Part;
            float commanded = HingeShipManager.CommandedCells(piston);
            bool want = MergeMath.WantMerged(LogicSource.MergeAt(part), LogicSource.Read(part),
                LogicSource.Mode(part, s_mergeToggleId, POPOUT_OFF), commanded, merged, mergedSlide, out needSlide);
            waitWhy = Math.Abs(commanded - (float)Math.Round(commanded)) > MergeMath.ALIGN_TOL_CELLS ? "target is not a whole cell" : "lining up";
            return want;
        }

        public static void Service(HingeShipManager mgr, ZtxPistonBaseComponent piston, int tick)
        {
            if (!ENABLED) return;
            try
            {
                Part bp = piston?.Part;
                Ship carrier = bp?.Ship;
                if (carrier?.Physics?.Body == null || mgr == null) return;
                Runtime rt = Rt(piston);
                if (tick % RETRY_TICKS == 0) InvalidateCellMap(carrier);
                bool merged = MergeStore.ReadState(bp) == MergeState.Merged;
                int slide = merged ? MergeStore.ReadSlide(bp) : 0;
                bool engaged = HingeShipManager.IsHingeActive(bp);
                if (!merged && !engaged) { rt.Why = ""; rt.Blocked = false; rt.Failures = 0; return; }
                bool wanted = Wanted(piston, merged, slide, out string waitWhy, out int needSlide);

                int trigger = (wanted ? 1 : 0) | (engaged ? 2 : 0);
                if (trigger != rt.LastTrigger) { rt.LastTrigger = trigger; rt.Blocked = false; rt.Failures = 0; }

                if (merged)
                {
                    rt.AlignedSince = -1;
                    if (engaged && wanted)
                    {
                        rt.Why = "";
                        return;
                    }
                    MergeRecord rec;
                    if (!engaged && slide == 0)
                    {
                        rec = MergeStore.Read(bp);
                        if (rec == null) return;
                        rec.State = MergeState.None;
                        MergeStore.Write(bp, rec);
                        InvalidateCellMap(carrier);
                        rt.Why = "";
                        Log.Info($"Piston base at {bp.Location}: disengaged while merged at home -- the section stays as hull.");
                        return;
                    }
                    if (rt.Blocked || tick < rt.RetryTick) return;
                    rec = MergeStore.Read(bp);
                    if (rec == null) return;
                    TryUnmerge(mgr, piston, rec, rt, tick);
                    return;
                }

                if (!engaged || !wanted) { rt.Why = ""; return; }
                if (rt.Blocked || tick < rt.RetryTick) return;
                TryMerge(mgr, piston, rt, tick, waitWhy, needSlide);
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("MergeService.Service", ex);
            }
        }

        private static void Refused(ZtxPistonBaseComponent piston, Runtime rt, int tick, string why)
        {
            bool fresh = rt.Why != why;
            rt.Why = why;
            rt.RetryTick = tick + RETRY_TICKS;
            if (++rt.Failures >= MAX_FAILURES)
            {
                rt.Blocked = true;
                Log.Error($"Piston base at {piston.Part?.Location}: {why} -- {MAX_FAILURES} attempts, waiting until the merge trigger (or the piston's ACTIVE switch) changes.");
            }
            else if (fresh)
                Log.Info($"Piston base at {piston.Part?.Location}: {why}.");
        }

        private static void TryMerge(HingeShipManager mgr, ZtxPistonBaseComponent piston, Runtime rt, int tick, string waitWhy, int needSlide)
        {
            Part bp = piston.Part;
            Ship carrier = bp.Ship;
            if (piston.Returning || !HingeJointManager.HasPinWithTag(piston)) { rt.Why = "not formed"; rt.AlignedSince = -1; return; }
            if (!HingeJointManager.TryGetSliderState(piston, out float slide, out float speed)) { rt.Why = "not formed"; rt.AlignedSince = -1; return; }
            float scale = mgr.ShipScaleValue;
            if (!MergeMath.TryAlignedSlide(slide / scale, speed / scale, piston.Rules.MaxExtension, out int k) || piston.Rods.Count != k
                || (needSlide >= 0 && k != needSlide))
            {
                rt.Why = waitWhy;
                rt.AlignedSince = -1;
                return;
            }
            if (rt.AlignedSince < 0) rt.AlignedSince = tick;
            if (!MergeMath.Settled(rt.AlignedSince, tick, SETTLE_TICKS))
            {
                rt.Why = "settling";
                return;
            }
            Part tip = piston.PairedTip?.Part;
            Ship section = tip?.Ship;
            if (section?.Physics?.Body == null || section == carrier) { rt.Why = "not formed"; return; }
            if (MergeMath.GripFirst(GripStore.Desired(bp), GripService.IsHolding(piston), GripService.RefusedSince(piston, ZtxSignalComponent.Clock(carrier) - GRIP_FIRST_TICKS, out _)))
            {
                rt.Why = "grip first";
                return;
            }

            IntVector2 f = HingeShipManager.BaseFacing(bp);
            int quarter = MergeMath.QuarterTurns(section.Physics.Body.Rotation - carrier.Physics.Body.Rotation);
            GridTransform xf = MergeMath.PistonTransform(bp.Location, f, k, tip.Location, quarter);

            var others = new List<object>();
            HingeJointManager.GetTagsLinking(section, carrier, piston, others);
            var coHolders = new List<ZtxPistonBaseComponent>();
            foreach (object tag in others)
            {
                if (tag is ZtxPistonBaseComponent op) { coHolders.Add(op); continue; }
                if (tag is ZtxHingeBaseComponent)
                {
                    Refused(piston, rt, tick, "merge refused: the section is also held by a hinge or motor");
                    return;
                }
            }
            foreach (ZtxPistonBaseComponent h in coHolders)
            {
                string notReady = CoHolderNotReady(mgr, h, carrier, section, xf, f, k);
                if (notReady != null) { rt.Why = notReady; return; }
            }

            coHolders.Insert(0, piston);
            var sectionParts = new List<Part>();
            foreach (Part p in section.Parts) sectionParts.Add(p);
            var records = new List<MergeRecord>();
            foreach (ZtxPistonBaseComponent h in coHolders)
                records.Add(MergeRebuild.RecordOf(h.Part, sectionParts, xf, k, MergeState.Merged));

            if (!ShipMerger.Merge(carrier, section, piston, xf))
            {
                Refused(piston, rt, tick, "merge refused: see the Merge line above (a target cell is taken, or the move failed)");
                return;
            }
            for (int i = 0; i < coHolders.Count; i++)
            {
                ZtxPistonBaseComponent h = coHolders[i];
                h.Returning = false;
                h.ClearPair();
                MergeStore.Write(h.Part, records[i]);
                Runtime hr = Rt(h);
                hr.Why = "";
                hr.Failures = 0;
            }
            InvalidateCellMap(carrier);
            Log.Info($"Piston base at {bp.Location}: section merged at {k} cells ({sectionParts.Count} parts" +
                (coHolders.Count > 1 ? $", {coHolders.Count} pistons" : "") + ") -- hull until unmerged.");
        }

        private static string CoHolderNotReady(HingeShipManager mgr, ZtxPistonBaseComponent h, Ship carrier, Ship section, GridTransform xf, IntVector2 f, int k)
        {
            Part hp = h.Part;
            string who = "waiting for the piston at " + hp?.Location;
            if (hp?.Ship != carrier) return "merge refused: a piston on another ship holds the section";
            if (!HingeShipManager.IsHingeActive(hp) || h.Returning || !HingeJointManager.HasPinWithTag(h)) return who;
            if (!Wanted(h, false, 0, out _, out int need)) return who;
            if (!HingeJointManager.TryGetSliderState(h, out float slide, out float speed)) return who;
            float scale = mgr.ShipScaleValue;
            if (!MergeMath.TryAlignedSlide(slide / scale, speed / scale, h.Rules.MaxExtension, out int kh) || h.Rods.Count != kh
                || (need >= 0 && kh != need) || kh != k) return who;
            Part tip = h.PairedTip?.Part;
            if (tip?.Ship != section) return who;
            if (!MergeMath.HolderAgrees(xf, f, k, hp.Location, HingeShipManager.BaseFacing(hp), kh, tip.Location))
                return "merge refused: the pistons holding the section are not at the same slide or facing";
            return null;
        }

        internal static List<(int Index, Part Part)> MemberParts(Part bp, MergeRecord rec)
        {
            var found = new List<(int, Part)>();
            Ship carrier = bp?.Ship;
            if (carrier == null || rec == null) return found;
            IntVector2 f = HingeShipManager.BaseFacing(bp);
            var seen = new HashSet<Part>();
            for (int i = 0; i < rec.MemberCount; i++)
            {
                MergeMember m = rec.Member(i);
                IntVector2 cell = MergeMath.FromBaseFrame(bp.Location, f, bp.FlipX, m.Rel) + f * rec.Slide;
                Part p = carrier.Parts[cell, PartRectType.Normal];
                if (p == null || p == bp || seen.Contains(p)) continue;
                if (!MergeRebuild.HashMatches(p.Rules, m.Hash)) continue;
                seen.Add(p);
                found.Add((i, p));
            }
            return found;
        }

        private static IEnumerable<Part> NestedMergedParts(Ship carrier, Part p, Part lead)
        {
            var add = new List<Part>();
            if (p == null || p == lead || p.Ship != carrier || GripService.PistonOf(p) == null) return add;
            MergeRecord r = MergeStore.Read(p);
            if (r == null || r.State != MergeState.Merged) return add;
            IntVector2 f = HingeShipManager.BaseFacing(p);
            for (int i = 1; i <= r.Slide; i++)
            {
                Part rod = carrier.Parts[p.Location + f * i, PartRectType.Normal];
                if (ZtxPistonRodComponent.ForPart(rod) != null) add.Add(rod);
            }
            foreach ((int _, Part q) in MemberParts(p, r)) add.Add(q);
            return add;
        }

        private static void TryUnmerge(HingeShipManager mgr, ZtxPistonBaseComponent piston, MergeRecord rec, Runtime rt, int tick)
        {
            Part bp = piston.Part;
            Ship carrier = bp.Ship;
            IntVector2 f = HingeShipManager.BaseFacing(bp);
            int k = rec.Slide;

            var parts = new List<Part>();
            var seen = new HashSet<Part>();
            foreach ((int _, Part p) in MemberParts(bp, rec))
            {
                seen.Add(p);
                parts.Add(p);
            }

            for (int i = 1; i <= k; i++)
            {
                Part rod = carrier.Parts[bp.Location + f * i, PartRectType.Normal];
                if (ZtxPistonRodComponent.ForPart(rod) == null || rod.IsUnderConstruction)
                {
                    IntVector2 rc = bp.Location + f * i;
                    if (rod == null && carrier.BlueprintParts != null && carrier.BlueprintParts[rc, PartRectType.Normal] == null
                        && carrier.Rules.PartsByID.TryGetValue(new ID<PartRules>(piston.Rules.RodPart), out PartRules rodRules) && rodRules != null)
                    {
                        try { carrier.BlueprintParts.CreateBlueprintPart(rodRules, rc, bp.Rotation, bp.FlipX); }
                        catch (Exception ex) { Log.Exception("MergeService.TryUnmerge(rod blueprint)", ex); }
                    }
                    string wait = $"unmerge waits: rod segment {i} of {k} missing (crew rebuilding)";
                    if (rt.Why != wait) Log.Info($"Piston base at {bp.Location}: {wait}.");
                    rt.Why = wait;
                    rt.RetryTick = tick + RETRY_TICKS;
                    return;
                }
            }

            IntVector2 tipCell = bp.Location + f * (1 + k);
            Part tipPart = carrier.Parts[tipCell, PartRectType.Normal];
            if (parts.Count == 0 || tipPart == null || !seen.Contains(tipPart) || ZtxHingeTipComponent.ForPart(tipPart) == null)
            {
                Refused(piston, rt, tick, "unmerge refused: no Hinge Tip of the section at the end of the rod");
                return;
            }

            int own = parts.Count;
            List<Part> all = MergeMath.NestedClosure(parts, p => NestedMergedParts(carrier, p, bp));
            for (int i = own; i < all.Count; i++)
            {
                parts.Add(all[i]);
                seen.Add(all[i]);
            }
            int beforeCarts = parts.Count;
            foreach (Part cartPart in RailShipManager.MergedCartsOn(carrier, seen))
            {
                parts.Add(cartPart);
                seen.Add(cartPart);
            }
            int cartParts = parts.Count - beforeCarts;

            var coHolders = new List<ZtxPistonBaseComponent>();
            foreach (ZtxHingeBaseComponent b in mgr.BasesSnapshot())
            {
                if (b == piston || !(b is ZtxPistonBaseComponent pb) || pb.Part?.Ship != carrier) continue;
                if (MergeStore.ReadState(pb.Part) != MergeState.Merged) continue;
                IntVector2 fb = HingeShipManager.BaseFacing(pb.Part);
                Part t = carrier.Parts[pb.Part.Location + fb * (1 + MergeStore.ReadSlide(pb.Part)), PartRectType.Normal];
                if (t != null && seen.Contains(t) && !seen.Contains(pb.Part)) coHolders.Add(pb);
            }

            Ship split = ShipSplitter.Split(carrier, parts, bp);
            if (split == null)
            {
                Refused(piston, rt, tick, "unmerge failed: the split was rolled back (see the Split lines above)");
                return;
            }
            Ship home = k != 0 ? ShipRecenter.Regrid(split, new GridTransform(0, -(f * k))) : split;
            if (home == split && k != 0)
                Log.Error($"Piston base at {bp.Location}: the unmerged section kept the merged grid (regrid refused); it relinks, but cannot merge back home until re-split.");

            rec.State = MergeState.Out;
            MergeStore.Write(bp, rec);
            InvalidateCellMap(carrier);
            bool linked = mgr.RelinkPiston(piston, home);
            rt.Why = linked ? "" : "unmerged, joint not re-formed yet";
            rt.Failures = 0;
            int coLinked = 0;
            foreach (ZtxPistonBaseComponent h in coHolders)
            {
                MergeRecord hr = MergeStore.Read(h.Part);
                if (hr != null) { hr.State = MergeState.Out; MergeStore.Write(h.Part, hr); }
                bool ok = mgr.RelinkPiston(h, home);
                if (ok) coLinked++;
                Runtime hrt = Rt(h);
                hrt.Why = ok ? "" : "unmerged, joint not re-formed yet";
                hrt.Failures = 0;
            }
            Log.Info($"Piston base at {bp.Location}: section unmerged at {k} cells ({parts.Count} parts" +
                (parts.Count - cartParts > own ? $", {parts.Count - cartParts - own} of them a nested merged piston's" : "") +
                (cartParts > 0 ? $", {cartParts} of a cart merged on its rail" : "") +
                (coHolders.Count > 0 ? $", {coLinked} of {coHolders.Count} other piston(s) relinked" : "") + ") -- " +
                (linked ? "driving again." : "the joint re-forms on the next service tick."));
        }

        private sealed class CellMap
        {
            public bool Built;
            public int Generation;
            public readonly Dictionary<IntVector2, IntVector2> Map = new Dictionary<IntVector2, IntVector2>();
            public readonly Dictionary<IntVector2, IntVector2> Home = new Dictionary<IntVector2, IntVector2>();
            public readonly Dictionary<Part, int> Tips = new Dictionary<Part, int>();
        }

        private static CellMap MapOf(Ship ship)
        {
            CellMap map = s_maps.GetValue(ship, _ => new CellMap());
            if (map.Built && map.Generation == MergeRebuild.SplitGeneration) return map;
            map.Generation = MergeRebuild.SplitGeneration;
            map.Map.Clear();
            map.Home.Clear();
            map.Tips.Clear();
            HingeShipManager mgr = HingeShipManager.TryGet(ship);
            if (mgr != null)
            {
                var merged = new List<(Part Base, IntVector2 F, MergeRecord Rec)>();
                foreach (ZtxHingeBaseComponent b in mgr.BasesSnapshot())
                {
                    if (!(b is ZtxPistonBaseComponent) || b.Part?.Ship != ship) continue;
                    MergeRecord r = MergeStore.Read(b.Part);
                    if (r == null || r.State != MergeState.Merged) continue;
                    IntVector2 f = HingeShipManager.BaseFacing(b.Part);
                    Part tip = ship.Parts[b.Part.Location + f * (1 + r.Slide), PartRectType.Normal];
                    if (tip != null) map.Tips[tip] = r.Slide;
                    merged.Add((b.Part, f, r));
                }
                Dictionary<IntVector2, IntVector2> composed = ComposeHomeMap(merged.Count,
                    i => merged[i].Base.Location, i => merged[i].F, i => merged[i].Rec.Slide, i => merged[i].Rec.MemberCount,
                    (i, baseHome, m) => MergeMath.FromBaseFrame(baseHome, merged[i].F, merged[i].Base.FlipX, merged[i].Rec.Member(m).Rel));
                foreach (KeyValuePair<IntVector2, IntVector2> kv in composed)
                {
                    map.Map[kv.Key] = kv.Value;
                    map.Home[kv.Value] = kv.Key;
                }
            }
            map.Built = true;
            return map;
        }

        internal static Dictionary<IntVector2, IntVector2> ComposeHomeMap(int count, Func<int, IntVector2> baseLive, Func<int, IntVector2> facing,
            Func<int, int> slide, Func<int, int> memberCount, Func<int, IntVector2, int, IntVector2> homeOfMember)
        {
            var map = new Dictionary<IntVector2, IntVector2>();
            var homeOfLive = new Dictionary<IntVector2, IntVector2>();
            var ownerOfLive = new Dictionary<IntVector2, int>();
            var depth = new int[count];
            for (int pass = 0; pass < 6; pass++)
            {
                var nextMap = new Dictionary<IntVector2, IntVector2>();
                var nextHome = new Dictionary<IntVector2, IntVector2>();
                var nextOwner = new Dictionary<IntVector2, int>();
                var entryDepth = new Dictionary<IntVector2, int>();
                var nextDepth = new int[count];
                for (int b = 0; b < count; b++)
                {
                    IntVector2 live = baseLive(b);
                    bool nested = homeOfLive.TryGetValue(live, out IntVector2 baseHome);
                    if (!nested) baseHome = live;
                    nextDepth[b] = nested && ownerOfLive.TryGetValue(live, out int parent) ? depth[parent] + 1 : 0;
                    IntVector2 shift = live - baseHome + facing(b) * slide(b);
                    for (int m = 0; m < memberCount(b); m++)
                    {
                        IntVector2 h = homeOfMember(b, baseHome, m);
                        IntVector2 lv = h + shift;
                        if (lv == h) continue;
                        if (entryDepth.TryGetValue(h, out int d) && d > nextDepth[b]) continue;
                        if (nextMap.TryGetValue(h, out IntVector2 old)) { nextHome.Remove(old); nextOwner.Remove(old); }
                        nextMap[h] = lv;
                        nextHome[lv] = h;
                        nextOwner[lv] = b;
                        entryDepth[h] = nextDepth[b];
                    }
                }
                bool settled = nextHome.Count == homeOfLive.Count && nextHome.All(kv => homeOfLive.TryGetValue(kv.Key, out IntVector2 v) && v == kv.Value)
                    && nextDepth.SequenceEqual(depth);
                map = nextMap;
                homeOfLive = nextHome;
                ownerOfLive = nextOwner;
                depth = nextDepth;
                if (settled) break;
            }
            return map;
        }

        public static bool TryGetMergedTipCells(Part tip, out float cells)
        {
            cells = 0f;
            if (!ENABLED || tip?.Ship == null) return false;
            if (!MapOf(tip.Ship).Tips.TryGetValue(tip, out int k)) return false;
            cells = k;
            return true;
        }

        private static readonly ConditionalWeakTable<Ship, CellMap> s_maps = new ConditionalWeakTable<Ship, CellMap>();

        public static void InvalidateCellMap(Ship ship)
        {
            if (ship != null && s_maps.TryGetValue(ship, out CellMap m)) m.Built = false;
        }

        public static bool TryMapLiveCell(Ship ship, IntVector2 liveCell, out IntVector2 homeCell)
        {
            homeCell = liveCell;
            if (!ENABLED || ship == null) return false;
            return MapOf(ship).Home.TryGetValue(liveCell, out homeCell);
        }

        public static bool TryMapHomeCell(Ship ship, IntVector2 homeCell, out IntVector2 liveCell)
        {
            liveCell = homeCell;
            if (!ENABLED || ship == null) return false;
            return MapOf(ship).Map.TryGetValue(homeCell, out liveCell);
        }
    }
}
