using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class MergeRebuild
    {
        private const int LOST_GRACE_SECONDS = 2;

        private static readonly ConditionalWeakTable<ZtxHingeBaseComponent, StrongBox<int>> s_unjointed =
            new ConditionalWeakTable<ZtxHingeBaseComponent, StrongBox<int>>();

        private static readonly ConditionalWeakTable<object, Dictionary<uint, PartRules>> s_rulesByHash =
            new ConditionalWeakTable<object, Dictionary<uint, PartRules>>();

        public static MergeRecord RecordOf(Part basePart, IEnumerable<Part> sectionParts, GridTransform xf, int k, MergeState state)
        {
            IntVector2 f = HingeShipManager.BaseFacing(basePart);
            var rec = new MergeRecord { State = state, Kind = MergeRecord.KIND_PISTON, Slide = k };
            HashSet<IntVector2> keepRods = null;
            foreach (Part p in sectionParts)
            {
                if (p == null || p == basePart) continue;
                if (!p.Rules.IsBuildable) continue;
                if (PendingRepairs.IsFrameStage(p.Ship, p)) continue;
                if (IsRodRules(p.Rules) && (state != MergeState.Merged || !(keepRods ??= MergedRodCells(p.Ship)).Contains(p.Location))) continue;
                ShipMerger.TransformedPlacement(p, xf, out IntVector2 loc, out int rot);
                IntVector2 home = loc - f * k;
                MergeMath.RelRotation(basePart.Rotation, basePart.FlipX, rot, p.FlipX, out int relRot, out bool relFlip);
                int index = rec.MemberCount;
                rec.AddMember(p.Rules.ID.ToString(), MergeMath.ToBaseFrame(basePart.Location, f, basePart.FlipX, home), relRot, relFlip);
                CaptureSettings(p.Ship, p, index, rec);
            }
            return rec;
        }

        private static readonly ConditionalWeakTable<ZtxHingeBaseComponent, StrongBox<long>> s_lastSig =
            new ConditionalWeakTable<ZtxHingeBaseComponent, StrongBox<long>>();

        public static void RefreshFromLiveSection(ZtxHingeBaseComponent b, Ship section)
        {
            Part bp = b.Part;
            IntVector2 f = HingeShipManager.BaseFacing(bp);
            var rec = new MergeRecord { State = MergeState.Out, Kind = MergeRecord.KIND_PISTON, Slide = 0 };
            var seen = new HashSet<(uint, IntVector2, int, bool)>();
            long sig = 17;
            var nested = new List<(IntVector2 Loc, int Rot, bool Flip, MergeRecord Rec)>();
            var skipCells = new HashSet<IntVector2>();
            void Nest(IntVector2 loc, int rot, bool flip, MergeRecord nr)
            {
                if (nr == null || (nr.State != MergeState.Out && nr.State != MergeState.Merged)) return;
                nested.Add((loc, rot, flip, nr));
                if (nr.State != MergeState.Merged) return;
                IntVector2 nf = HingeShipManager.BaseFacing(rot, flip);
                for (int i = 0; i < nr.MemberCount; i++)
                    skipCells.Add(MergeMath.FromBaseFrame(loc, nf, flip, nr.Member(i).Rel) + nf * nr.Slide);
            }
            HingeShipManager nestedMgr = HingeShipManager.TryGet(section);
            if (nestedMgr != null)
            {
                foreach (ZtxHingeBaseComponent nb in nestedMgr.BasesSnapshot())
                {
                    Part np = nb?.Part;
                    if (np?.Ship != section) continue;
                    MergeState ns = MergeStore.ReadState(np);
                    if (ns != MergeState.Out && ns != MergeState.Merged) continue;
                    Nest(np.Location, np.Rotation, np.FlipX, MergeStore.Read(np));
                }
            }
            if (section.DestroyedParts != null)
            {
                foreach (CommonBasePart dp in section.DestroyedParts)
                {
                    if (dp?.Rules == null) continue;
                    var dpsd = new PartSaveData(dp);
                    MergeState ds = MergeStore.ReadStateAt(section, dpsd);
                    if (ds != MergeState.Out && ds != MergeState.Merged) continue;
                    Nest(dp.Location, dp.Rotation, dp.FlipX, MergeStore.ReadAt(section, dpsd));
                }
            }
            foreach (Part cartPart in RailShipManager.MergedAwayCartParts(section)) skipCells.Add(cartPart.Location);
            var indexAt = new Dictionary<IntVector2, int>();
            int AddRel(uint h, IntVector2 rel, int relRot, bool relFlip)
            {
                if (!seen.Add((h, rel, relRot, relFlip))) return -1;
                rec.AddMemberHashed(h, rel, relRot, relFlip);
                sig = unchecked(sig * 31 + h + rel.X * 7919 + rel.Y * 104729 + relRot * 13 + (relFlip ? 1 : 0));
                return rec.MemberCount - 1;
            }
            void Add(CommonBasePart p)
            {
                if (p?.Rules == null || !p.Rules.IsBuildable) return;
                if (IsRodRules(p.Rules) || skipCells.Contains(p.Location)) return;
                if (PendingRepairs.IsFrameStage(section, p)) return;
                MergeMath.RelRotation(bp.Rotation, bp.FlipX, p.Rotation, p.FlipX, out int relRot, out bool relFlip);
                int index = AddRel(MergeRecord.Hash(p.Rules.ID.ToString()), MergeMath.ToBaseFrame(bp.Location, f, bp.FlipX, p.Location), relRot, relFlip);
                CaptureSettings(section, p, index, rec);
                if (index >= 0 && !indexAt.ContainsKey(p.Location)) indexAt[p.Location] = index;
            }
            foreach (Part p in section.Parts) Add(p);
            if (section.BlueprintParts != null) foreach (CommonBasePart p in section.BlueprintParts) Add(p);
            if (section.DestroyedParts != null) foreach (CommonBasePart p in section.DestroyedParts) Add(p);
            foreach ((IntVector2 nLoc, int nRot, bool nFlip, MergeRecord nr) in nested)
            {
                IntVector2 nf = HingeShipManager.BaseFacing(nRot, nFlip);
                var map = new int[nr.MemberCount];
                for (int i = 0; i < nr.MemberCount; i++)
                {
                    map[i] = -1;
                    MergeMember m = nr.Member(i);
                    if (IsRodHash(section, m.Hash)) continue;
                    MergeMath.Reframe(bp.Location, f, bp.FlipX, bp.Rotation, nLoc, nf, nFlip, nRot,
                        m.Rel, m.RelRot, m.RelFlip, m.HasRotation, out IntVector2 rel, out int relRot, out bool relFlip);
                    map[i] = AddRel(m.Hash, rel, relRot, relFlip);
                }
                int owner = indexAt.TryGetValue(nLoc, out int oi) ? oi : -1;
                for (int i = 0; i < nr.MemberCount; i++)
                {
                    if (map[i] < 0) continue;
                    int pj = nr.Member(i).Parent;
                    int np = pj >= 0 && pj < map.Length && map[pj] >= 0 ? map[pj] : owner;
                    if (np >= 0 && np != map[i]) rec.SetParent(map[i], np);
                }
                for (int j = 0; j < nr.SettingCount; j++)
                {
                    MergeSetting t = nr.Setting(j);
                    if (map[t.Member] >= 0) rec.AddSetting(map[t.Member], t.ToggleHash, t.Value);
                }
            }
            for (int j = 0; j < rec.SettingCount; j++)
            {
                MergeSetting t = rec.Setting(j);
                sig = unchecked(sig * 37 + t.Member * 131 + t.ToggleHash + t.Value * 7);
            }
            for (int i = 0; i < rec.MemberCount; i++)
                sig = unchecked(sig * 41 + rec.Member(i).Parent);
            sig = unchecked(sig * 31 + rec.MemberCount);
            StrongBox<long> last = s_lastSig.GetValue(b, _ => new StrongBox<long>(long.MinValue));
            if (last.Value == sig) return;
            bool first = last.Value == long.MinValue;
            last.Value = sig;
            MergeRecord old = MergeStore.Read(bp);
            MergeStore.Write(bp, rec);
            if (!first || old == null || old.MemberCount != rec.MemberCount)
                Log.Info($"Hinge base at {bp.Location}: section record updated -- {rec.MemberCount} part(s) incl. blueprints and damaged parts (was {old?.MemberCount ?? 0}).");
        }

        private static readonly List<Ship> s_assembly = new List<Ship>();

        public static bool SectionInAssembly(Part basePart)
        {
            Ship carrier = basePart?.Ship;
            if (carrier == null) return false;
            IntVector2 tipHome = basePart.Location + HingeShipManager.BaseFacing(basePart);
            s_assembly.Clear();
            HingeJointManager.GetAssemblyShips(carrier, s_assembly);
            bool found = false;
            foreach (Ship s in s_assembly)
            {
                if (s == carrier || s?.Parts == null) continue;
                if (ZtxHingeTipComponent.ForPart(s.Parts[tipHome, PartRectType.Physical]) != null) { found = true; break; }
            }
            s_assembly.Clear();
            return found;
        }

        private static readonly ConditionalWeakTable<PartRules, StrongBox<bool>> s_isRod =
            new ConditionalWeakTable<PartRules, StrongBox<bool>>();

        internal static bool IsRodRules(PartRules rules)
        {
            if (rules == null) return false;
            return s_isRod.GetValue(rules, r =>
            {
                foreach (PartComponentRules c in r.Components)
                    if (c is ZtxPistonRodRules) return new StrongBox<bool>(true);
                return new StrongBox<bool>(false);
            }).Value;
        }

        private static bool IsRodHash(Ship ship, uint hash) => IsRodRules(RulesOf(ship, hash));

        internal static HashSet<IntVector2> MergedRodCells(Ship ship)
        {
            var cells = new HashSet<IntVector2>();
            HingeShipManager mgr = HingeShipManager.TryGet(ship);
            if (mgr == null) return cells;
            foreach (ZtxHingeBaseComponent b in mgr.BasesSnapshot())
            {
                Part bp = b?.Part;
                if (!(b is ZtxPistonBaseComponent) || bp?.Ship != ship || !MergeService.TryGetMergedSlide(bp, out int k)) continue;
                IntVector2 f = HingeShipManager.BaseFacing(bp);
                for (int i = 1; i <= k; i++) cells.Add(bp.Location + f * i);
            }
            return cells;
        }

        internal static bool RebuildPendingAt(Ship ship, IntVector2 cell)
        {
            HashSet<IntVector2> cells = PendingCells(ship);
            return cells != null && cells.Contains(cell);
        }

        internal static HashSet<IntVector2> PendingCells(Ship ship)
        {
            HingeShipManager mgr = HingeShipManager.TryGet(ship);
            if (mgr == null) return null;
            HashSet<IntVector2> cells = null;
            foreach (ZtxHingeBaseComponent b in mgr.BasesSnapshot())
            {
                Part bp = b?.Part;
                if (bp?.Ship != ship || MergeStore.ReadState(bp) != MergeState.Lost) continue;
                MergeRecord rec = MergeStore.Read(bp);
                if (rec == null) continue;
                IntVector2 f = HingeShipManager.BaseFacing(bp);
                cells ??= new HashSet<IntVector2>();
                for (int i = 0; i < rec.MemberCount; i++)
                    cells.Add(HomeCell(bp, f, rec.Member(i)));
            }
            return cells;
        }

        private static MergeRecord PruneLost(Part bp, MergeRecord rec) => PruneToBlueprint(bp, rec, 0, "lost");

        private static void KeepAsRepairs(Part bp, MergeRecord rec)
        {
            Ship ship = bp?.Ship;
            if (ship?.BlueprintParts == null || ship.DestroyedParts == null || rec == null) return;
            IntVector2 f = HingeShipManager.BaseFacing(bp);
            int wrecks = 0;
            for (int i = 0; i < rec.MemberCount; i++)
            {
                MergeMember m = rec.Member(i);
                if (Built(ship, bp, f, m, out bool taken) || taken) continue;
                PartRules rules = RulesOf(ship, m.Hash);
                if (rules == null || !rules.IsBuildable) continue;
                Placement(bp, f, m, 0, out IntVector2 cell, out int rot, out bool flip);
                IntRect rect = rules.GetRect(cell, rot);
                BlueprintPart blueprint = ship.BlueprintParts.GetExactPart(rules, cell, rot, flip);
                var wreck = ship.DestroyedParts.GetExactPart(rules, cell, rot, flip);
                if (blueprint != null && wreck == null)
                {
                    foreach (IntVector2 c in rect) HingeShipManager.ClearRodLeftover(ship, c);
                    if (ship.DestroyedParts.HasPartAt(rect)) continue;
                    ship.DestroyedParts.CreateDestroyedPart(rules, cell, rot, flip);
                    wrecks++;
                }
            }
            if (wrecks > 0)
                Log.Info($"Hinge base at {bp.Location}: lost arm kept as repairs -- {wrecks} wreck(s) laid under their blueprints.");
        }

        private static MergeRecord PruneToBlueprint(Part bp, MergeRecord rec, int slide, string what, Func<MergeMember, bool> keepAlways = null)
        {
            Ship ship = bp.Ship;
            if (ship?.BlueprintParts == null || rec == null) return rec;
            IntVector2 f = HingeShipManager.BaseFacing(bp);
            MergeRecord kept = rec.Keep(i =>
            {
                MergeMember m = rec.Member(i);
                if (Built(ship, bp, f, m, slide, out _)) return true;
                if (keepAlways != null && keepAlways(m)) return true;
                PartRules rules = RulesOf(ship, m.Hash);
                if (rules == null) return false;
                Placement(bp, f, m, slide, out IntVector2 cell, out int rot, out bool flip);
                return ship.BlueprintParts.GetExactPart(rules, cell, rot, flip) != null;
            });
            int dropped = rec.MemberCount - kept.MemberCount;
            if (dropped == 0) return rec;
            if (BlueprintAudit.ENABLED)
            {
                var keptKeys = new HashSet<(uint, IntVector2)>();
                for (int i = 0; i < kept.MemberCount; i++) keptKeys.Add((kept.Member(i).Hash, kept.Member(i).Rel));
                var gone = new List<(PartRules, IntVector2, int, bool)>();
                for (int i = 0; i < rec.MemberCount; i++)
                {
                    MergeMember m = rec.Member(i);
                    if (keptKeys.Contains((m.Hash, m.Rel))) continue;
                    PartRules rules = RulesOf(ship, m.Hash);
                    if (rules == null) continue;
                    Placement(bp, f, m, slide, out IntVector2 cell, out int rot, out bool flip);
                    gone.Add((rules, cell, rot, flip));
                }
                BlueprintAudit.Dropped(bp, gone);
            }
            if (ship.DestroyedParts != null)
            {
                var keptSet = new HashSet<(uint, IntVector2)>();
                for (int i = 0; i < kept.MemberCount; i++) keptSet.Add((kept.Member(i).Hash, kept.Member(i).Rel));
                for (int i = 0; i < rec.MemberCount; i++)
                {
                    MergeMember m = rec.Member(i);
                    if (keptSet.Contains((m.Hash, m.Rel))) continue;
                    PartRules rules = RulesOf(ship, m.Hash);
                    if (rules == null) continue;
                    Placement(bp, f, m, slide, out IntVector2 cell, out int rot, out bool flip);
                    var lone = ship.DestroyedParts.GetExactPart(rules, cell, rot, flip);
                    if (lone != null && ship.BlueprintParts.GetExactPart(rules, cell, rot, flip) == null)
                        ship.DestroyedParts.RemoveDestroyedPart(lone);
                }
            }
            if (kept.MemberCount == 0)
            {
                kept.State = MergeState.None;
                MergeStore.Write(bp, kept);
                Log.Info($"Hinge base at {bp.Location}: {what} arm forgotten -- every part of it was removed from the blueprint; the base works normally.");
                return kept;
            }
            MergeStore.Write(bp, kept);
            Log.Info($"Hinge base at {bp.Location}: {dropped} part(s) dropped from the {what} arm -- removed from the blueprint or built over; {kept.MemberCount} kept.");
            return kept;
        }

        private static readonly ConditionalWeakTable<ZtxHingeBaseComponent, StrongBox<int>> s_mergedChecked =
            new ConditionalWeakTable<ZtxHingeBaseComponent, StrongBox<int>>();

        private static int s_splitGeneration = 1;

        public static void MarkEngineSplit() => System.Threading.Interlocked.Increment(ref s_splitGeneration);

        public static int SplitGeneration => s_splitGeneration;

        public static void ServiceMerged(HingeShipManager mgr, ZtxHingeBaseComponent b)
        {
            if (!MergeService.REBUILD_ENABLED || b?.Part?.Ship == null) return;
            try
            {
                Part bp = b.Part;
                Ship ship = bp.Ship;
                MergeRecord rec = MergeStore.Read(bp);
                if (rec == null || rec.State != MergeState.Merged) return;
                int k = rec.Slide;
                IntVector2 f = HingeShipManager.BaseFacing(bp);
                if (b is ZtxPistonBaseComponent holder)
                {
                    for (int i = 1; i <= k; i++)
                    {
                        Part rp = ship.Parts[bp.Location + f * i, PartRectType.Normal];
                        ZtxPistonRodComponent rc = ZtxPistonRodComponent.ForPart(rp);
                        if (rc == null || rp.IsUnderConstruction) continue;
                        rc.Owner = holder;
                        if (!holder.Rods.Contains(rp)) holder.Rods.Add(rp);
                    }
                }
                HashSet<IntVector2> innerCells = null;
                var innerAway = new List<HashSet<IntVector2>>();
                Func<MergeMember, bool> innerOwned = m =>
                {
                    innerCells ??= InnerJoints(ship, bp, rec, f, k, innerAway);
                    Placement(bp, f, m, k, out IntVector2 at, out _, out _);
                    return innerCells.Contains(at);
                };
                StrongBox<int> checkedAt = s_mergedChecked.GetValue(b, _ => new StrongBox<int>(0));
                int generation = s_splitGeneration;
                if (checkedAt.Value != generation)
                {
                    checkedAt.Value = generation;
                    IntVector2 head = bp.Location + f * (k + 1);
                    if (b is ZtxPistonBaseComponent cut && mgr != null && ship.Parts[head, PartRectType.Normal] == null
                        && !AnyMemberStands(ship, bp, rec, f, k))
                    {
                        RetractCutArm(mgr, cut, rec, k, f);
                        return;
                    }
                    innerCells ??= InnerJoints(ship, bp, rec, f, k, innerAway);
                    RemoveAwayInnerLeftovers(ship, bp, rec, f, k, innerAway);
                    for (int i = 0; i < rec.MemberCount; i++)
                    {
                        PartRules rules = RulesOf(ship, rec.Member(i).Hash);
                        if (rules == null || innerOwned(rec.Member(i))) continue;
                        Placement(bp, f, rec.Member(i), k, out IntVector2 cell, out int rot, out bool flip);
                        if (ship.Parts[cell, PartRectType.Normal] == null && ship.BlueprintParts.GetExactPart(rules, cell, rot, flip) != null
                            && ship.DestroyedParts != null && !ship.DestroyedParts.HasPartAt(rules.GetRect(cell, rot)))
                            ship.DestroyedParts.CreateDestroyedPart(rules, cell, rot, flip);
                    }
                    var (placed, _, _, settings) = PlaceBlueprints(bp, rec, m => !innerOwned(m) && !Built(ship, bp, f, m, k, out bool taken) && !taken, k);
                    int rods = 0;
                    if (b is ZtxPistonBaseComponent piston && ship.Rules.PartsByID.TryGetValue(new ID<PartRules>(piston.Rules.RodPart), out PartRules rodRules) && rodRules != null)
                    {
                        for (int i = 1; i <= k; i++)
                        {
                            IntVector2 rc = bp.Location + f * i;
                            if (ship.Parts[rc, PartRectType.Normal] != null || ship.BlueprintParts[rc, PartRectType.Normal] != null) continue;
                            ship.BlueprintParts.CreateBlueprintPart(rodRules, rc, bp.Rotation, bp.FlipX);
                            if (ship.DestroyedParts != null && !ship.DestroyedParts.HasPartAt(rodRules.GetRect(rc, bp.Rotation)))
                                ship.DestroyedParts.CreateDestroyedPart(rodRules, rc, bp.Rotation, bp.FlipX);
                            rods++;
                        }
                    }
                    if (placed > 0 || rods > 0)
                        Log.Info($"Merged piston at {bp.Location}: {placed} part(s) of its arm and {rods} rod segment(s) laid as repairs ({settings} setting(s) restored) -- they were gone, not just damaged.");
                }
                PruneToBlueprint(bp, rec, k, "merged", innerOwned);
            }
            catch (Exception ex)
            {
                Log.Exception("MergeRebuild.ServiceMerged", ex);
            }
        }

        private static readonly ID<PartComponentRules>[] s_baseIds =
        {
            new ID<PartComponentRules>("ZtxPistonBase"), new ID<PartComponentRules>("ZtxMotorBase"), new ID<PartComponentRules>("ZtxHingeBase"),
        };

        private static ZtxHingeBaseComponent BaseOf(Part p)
        {
            foreach (ID<PartComponentRules> id in s_baseIds)
                if (p.TryGetComponent<ZtxHingeBaseComponent>(id, out ZtxHingeBaseComponent b) && b != null) return b;
            return null;
        }

        public static HashSet<IntVector2> InnerOwnedCells(IntVector2 innerLoc, IntVector2 facing, IEnumerable<IntVector2> homeCells, int maxSlide)
        {
            var owned = new HashSet<IntVector2>();
            for (int s = 1; s <= maxSlide; s++) owned.Add(innerLoc + facing * s);
            foreach (IntVector2 h in homeCells)
                for (int s = 0; s <= maxSlide; s++) owned.Add(h + facing * s);
            return owned;
        }

        private const int MAX_JOINT_DEPTH = 6;

        private static HashSet<IntVector2> InnerJoints(Ship ship, Part outerBase, MergeRecord rec, IntVector2 f, int k, List<HashSet<IntVector2>> away)
        {
            var owned = new HashSet<IntVector2>();
            var seen = new HashSet<Part> { outerBase };
            for (int i = 0; i < rec.MemberCount; i++)
            {
                Placement(outerBase, f, rec.Member(i), k, out IntVector2 cell, out _, out _);
                Part p = ship.Parts[cell, PartRectType.Normal];
                if (p == null || p.IsUnderConstruction || seen.Contains(p) || BaseOf(p) == null) continue;
                AddJoint(p, new List<IntVector2> { p.Location }, owned, away, false, seen, 1);
            }
            return owned;
        }

        public static HashSet<IntVector2> NestedBaseCells(IEnumerable<IntVector2> parentBases, IntVector2 offset, IntVector2 parentFacing, int parentMax)
        {
            var cells = new HashSet<IntVector2>();
            foreach (IntVector2 b in parentBases)
                for (int s = 0; s <= parentMax; s++) cells.Add(b + offset + parentFacing * s);
            return cells;
        }

        private static void AddJoint(Part joint, IReadOnlyCollection<IntVector2> baseCells, HashSet<IntVector2> owned,
            List<HashSet<IntVector2>> away, bool parentAway, HashSet<Part> seen, int depth)
        {
            if (depth > MAX_JOINT_DEPTH || !seen.Add(joint)) return;
            ZtxHingeBaseComponent j = BaseOf(joint);
            MergeRecord r = j != null ? MergeStore.Read(joint) : null;
            if (r == null || r.MemberCount == 0) return;
            IntVector2 fj = HingeShipManager.BaseFacing(joint);
            int max = j is ZtxPistonBaseComponent piston ? (int)Math.Floor(Math.Max(0f, piston.Rules.MaxExtension)) : 0;
            var offsets = new List<IntVector2>();
            foreach (IntVector2 c in HomeCells(joint, r)) offsets.Add(c - joint.Location);
            var mine = new HashSet<IntVector2>();
            foreach (IntVector2 b in baseCells)
            {
                var homes = new List<IntVector2>(offsets.Count);
                foreach (IntVector2 o in offsets) homes.Add(b + o);
                mine.UnionWith(InnerOwnedCells(b, fj, homes, max));
            }
            owned.UnionWith(mine);
            bool isAway = parentAway || r.State == MergeState.Out;
            if (isAway) away.Add(mine);

            Ship sectionShip = null;
            int slide = 0;
            if (r.State == MergeState.Merged)
            {
                sectionShip = joint.Ship;
                slide = r.Slide;
            }
            else if (r.State == MergeState.Out && HingeJointManager.TryGetPinShips(j, out Ship pinA, out Ship pinB))
            {
                sectionShip = ReferenceEquals(pinA, joint.Ship) ? pinB : pinA;
            }
            if (sectionShip == null) return;
            for (int i = 0; i < r.MemberCount; i++)
            {
                Placement(joint, fj, r.Member(i), slide, out IntVector2 cell, out _, out _);
                Part nested = sectionShip.Parts[cell, PartRectType.Normal];
                if (nested == null || nested.IsUnderConstruction || seen.Contains(nested) || BaseOf(nested) == null) continue;
                IntVector2 offset = cell - joint.Location - fj * slide;
                AddJoint(nested, NestedBaseCells(baseCells, offset, fj, max), owned, away, isAway, seen, depth + 1);
            }
        }

        private static void RemoveAwayInnerLeftovers(Ship ship, Part outerBase, MergeRecord rec, IntVector2 f, int k, List<HashSet<IntVector2>> away)
        {
            if (away.Count == 0) return;
            int removed = 0;
            for (int i = 0; i < rec.MemberCount; i++)
            {
                MergeMember m = rec.Member(i);
                PartRules rules = RulesOf(ship, m.Hash);
                if (rules == null) continue;
                Placement(outerBase, f, m, k, out IntVector2 cell, out int rot, out bool flip);
                if (ship.Parts[cell, PartRectType.Normal] != null) continue;
                bool isAway = false;
                foreach (HashSet<IntVector2> cells in away)
                    if (cells.Contains(cell)) { isAway = true; break; }
                if (!isAway) continue;
                BlueprintPart stale = ship.BlueprintParts.GetExactPart(rules, cell, rot, flip);
                if (stale != null) { ship.BlueprintParts.RemoveBlueprintPart(stale, removeDoors: true); removed++; }
                var wreck = ship.DestroyedParts?.GetExactPart(rules, cell, rot, flip);
                if (wreck != null) ship.DestroyedParts.RemoveDestroyedPart(wreck);
            }
            if (removed > 0)
                Log.Info($"Merged piston at {outerBase.Location}: {removed} stale blueprint part(s) removed where an inner joint's section sat -- that section is out on its own ship.");
        }

        private static void RetractCutArm(HingeShipManager mgr, ZtxPistonBaseComponent piston, MergeRecord rec, int k, IntVector2 f)
        {
            Part bp = piston.Part;
            Ship ship = bp.Ship;
            MergeRecord lost = rec.Keep(i =>
            {
                Placement(bp, f, rec.Member(i), k, out IntVector2 cell, out _, out _);
                return ship.Parts[cell, PartRectType.Normal] == null;
            });
            int stayed = rec.MemberCount - lost.MemberCount;
            for (int i = 0; i < lost.MemberCount; i++)
            {
                PartRules rules = RulesOf(ship, lost.Member(i).Hash);
                if (rules == null) continue;
                Placement(bp, f, lost.Member(i), k, out IntVector2 cell, out int rot, out bool flip);
                BlueprintPart stale = ship.BlueprintParts.GetExactPart(rules, cell, rot, flip);
                if (stale != null) ship.BlueprintParts.RemoveBlueprintPart(stale, removeDoors: true);
                var wreck = ship.DestroyedParts?.GetExactPart(rules, cell, rot, flip);
                if (wreck != null) ship.DestroyedParts.RemoveDestroyedPart(wreck);
            }
            lost.State = MergeState.Lost;
            lost.Slide = 0;
            MergeStore.Write(bp, lost);
            mgr.ClearRodStubForRebuild(piston);
            MergeService.InvalidateCellMap(ship);
            var (placed, occupied, unknown, settings) = PlaceBlueprints(bp, lost, null);
            Log.Info($"Merged piston at {bp.Location}: its arm was cut off -- {placed} part(s) laid as repairs at home, retracted" +
                $" ({stayed} part(s) left as hull, {occupied} cell(s) occupied, {unknown} unknown, {settings} setting(s) restored).");
        }

        internal static IEnumerable<IntVector2> HomeCells(Part basePart, MergeRecord rec)
        {
            Ship ship = basePart?.Ship;
            if (ship == null || rec == null) yield break;
            IntVector2 f = HingeShipManager.BaseFacing(basePart.Rotation, basePart.FlipX);
            for (int i = 0; i < rec.MemberCount; i++)
            {
                MergeMember m = rec.Member(i);
                PartRules rules = RulesOf(ship, m.Hash);
                if (rules == null) continue;
                Placement(basePart, f, m, 0, out IntVector2 cell, out int rot, out bool _);
                foreach (IntVector2 c in rules.GetRect(cell, rot)) yield return c;
            }
        }

        private static void Placement(CommonBasePart basePart, IntVector2 f, MergeMember m, int slide, out IntVector2 cell, out int rot, out bool flip)
        {
            cell = HomeCell(basePart, f, m) + f * slide;
            rot = basePart.Rotation;
            flip = basePart.FlipX;
            if (m.HasRotation) MergeMath.AbsRotation(basePart.Rotation, basePart.FlipX, m.RelRot, m.RelFlip, out rot, out flip);
        }

        private static readonly ConditionalWeakTable<PartRules, Dictionary<uint, ID<PartToggleGuiRules>>> s_toggleIds =
            new ConditionalWeakTable<PartRules, Dictionary<uint, ID<PartToggleGuiRules>>>();

        private static Dictionary<uint, ID<PartToggleGuiRules>> ToggleIdsOf(PartRules rules)
        {
            return s_toggleIds.GetValue(rules, r =>
            {
                var d = new Dictionary<uint, ID<PartToggleGuiRules>>();
                foreach (PartComponentRules c in r.Components)
                    if (c is PartUIToggleRules t) d[MergeRecord.Hash(t.ToggleID.ToString())] = t.ToggleID;
                return d;
            });
        }

        private static void CaptureSettings(Ship ship, CommonBasePart p, int member, MergeRecord rec)
        {
            var meta = ship?.Metadata;
            if (meta == null || member < 0 || p?.Rules == null) return;
            var psd = new PartSaveData(p);
            foreach (KeyValuePair<uint, ID<PartToggleGuiRules>> kv in ToggleIdsOf(p.Rules))
            {
                int? v = meta.PartUIToggleStates.Get((psd, kv.Value)) ?? meta.PartUIToggleStates.Get((psd, kv.Value), blueprintMode: true);
                if (v.HasValue) rec.AddSetting(member, kv.Key, v.Value);
            }
        }

        private static int ApplySettings(Ship ship, BlueprintPart placed, MergeRecord rec, int member)
        {
            var meta = ship?.Metadata;
            if (meta == null || placed == null || rec.SettingCount == 0) return 0;
            Dictionary<uint, ID<PartToggleGuiRules>> ids = ToggleIdsOf(placed.Rules);
            var psd = new PartSaveData(placed);
            int n = 0;
            for (int j = 0; j < rec.SettingCount; j++)
            {
                MergeSetting t = rec.Setting(j);
                if (t.Member != member || !ids.TryGetValue(t.ToggleHash, out ID<PartToggleGuiRules> id)) continue;
                meta.PartUIToggleStates.Set((psd, id), t.Value);
                meta.PartUIToggleStates.Set((psd, id), t.Value, blueprintMode: true);
                n++;
            }
            if (n > 0) placed.OnPartUIToggleStatesChanged();
            return n;
        }

        private static IntVector2 HomeCell(CommonBasePart basePart, IntVector2 f, MergeMember m)
        {
            return MergeMath.FromBaseFrame(basePart.Location, f, basePart.FlipX, m.Rel);
        }

        private static bool AnyMemberStands(Ship ship, CommonBasePart basePart, MergeRecord rec, IntVector2 f, int slide)
        {
            for (int i = 0; i < rec.MemberCount; i++)
            {
                MergeMember m = rec.Member(i);
                PartRules rules = RulesOf(ship, m.Hash);
                if (rules == null || IsRodRules(rules)) continue;
                Placement(basePart, f, m, slide, out IntVector2 cell, out int rot, out bool flip);
                if (ship.Parts.GetExactPart(rules, cell, rot, flip) != null) return true;
            }
            return false;
        }

        private static PartRules RulesOf(Ship ship, uint hash)
        {
            var byId = ship?.Rules?.PartsByID;
            if (byId == null) return null;
            Dictionary<uint, PartRules> map = s_rulesByHash.GetValue(ship.Rules, _ =>
            {
                var d = new Dictionary<uint, PartRules>();
                foreach (var kv in byId)
                    if (kv.Value != null) d[MergeRecord.Hash(kv.Key.ToString())] = kv.Value;
                return d;
            });
            map.TryGetValue(hash, out PartRules rules);
            return rules;
        }

        private static bool Built(Ship ship, CommonBasePart basePart, IntVector2 f, MergeMember m, out bool cellTaken)
            => Built(ship, basePart, f, m, 0, out cellTaken);

        private static bool Built(Ship ship, CommonBasePart basePart, IntVector2 f, MergeMember m, int slide, out bool cellTaken)
        {
            Part p = ship.Parts[HomeCell(basePart, f, m) + f * slide, PartRectType.Normal];
            cellTaken = p != null;
            return p != null && HashMatches(p.Rules, m.Hash) && !p.IsUnderConstruction;
        }

        internal static bool HashMatches(string id, IEnumerable<string> otherIds, uint hash)
        {
            if (MergeRecord.Hash(id) == hash) return true;
            if (otherIds == null) return false;
            foreach (string other in otherIds)
                if (MergeRecord.Hash(other) == hash) return true;
            return false;
        }

        internal static bool HashMatches(PartRules rules, uint hash)
        {
            if (rules == null) return false;
            if (MergeRecord.Hash(rules.ID.ToString()) == hash) return true;
            if (rules.OtherIDs == null) return false;
            foreach (ID<PartRules> other in rules.OtherIDs)
                if (MergeRecord.Hash(other.ToString()) == hash) return true;
            return false;
        }

        public static (int Placed, int Occupied, int Unknown, int Settings) PlaceBlueprints(CommonBasePart basePart, MergeRecord rec, Func<MergeMember, bool> filter, int slide = 0, Ship target = null, Func<int, bool> indexFilter = null)
        {
            int placed = 0, occupied = 0, unknown = 0, settings = 0;
            Ship ship = target ?? (basePart as Part)?.Ship;
            if (ship?.BlueprintParts == null || rec == null || basePart == null) return (0, 0, 0, 0);
            IntVector2 f = HingeShipManager.BaseFacing(basePart.Rotation, basePart.FlipX);
            for (int i = 0; i < rec.MemberCount; i++)
            {
                MergeMember m = rec.Member(i);
                if (filter != null && !filter(m)) continue;
                if (indexFilter != null && !indexFilter(i)) continue;
                PartRules rules = RulesOf(ship, m.Hash);
                if (rules == null || !rules.IsBuildable) { unknown++; continue; }
                Placement(basePart, f, m, slide, out IntVector2 cell, out int rot, out bool flip);
                bool taken = false;
                foreach (IntVector2 c in rules.GetRect(cell, rot))
                {
                    if (ship.Parts[c, PartRectType.Normal] != null || ship.BlueprintParts[c, PartRectType.Normal] != null) { taken = true; break; }
                }
                if (taken) { occupied++; continue; }
                try
                {
                    BlueprintPart made = ship.BlueprintParts.CreateBlueprintPart(rules, cell, rot, flip);
                    placed++;
                    settings += ApplySettings(ship, made, rec, i);
                    if (ship.DestroyedParts != null && !ship.DestroyedParts.HasPartAt(rules.GetRect(cell, rot)))
                        ship.DestroyedParts.CreateDestroyedPart(rules, cell, rot, flip);
                    WriteChildRecord(ship, made, basePart, f, slide, rec, i);
                }
                catch (Exception ex)
                {
                    occupied++;
                    Log.Exception("MergeRebuild.PlaceBlueprints(" + rules.ID + " at " + cell + ")", ex);
                }
            }
            return (placed, occupied, unknown, settings);
        }

        private static void WriteChildRecord(Ship ship, BlueprintPart made, CommonBasePart outerBase, IntVector2 f, int slide, MergeRecord rec, int parent)
        {
            if (made == null) return;
            var map = new Dictionary<int, int>();
            var child = new MergeRecord { State = MergeState.Lost, Kind = MergeRecord.KIND_PISTON, Slide = 0 };
            IntVector2 cf = HingeShipManager.BaseFacing(made.Rotation, made.FlipX);
            IntVector2 innerLoc = outerBase.Location + f * slide;
            for (int i = 0; i < rec.MemberCount; i++)
            {
                if (!DescendsFrom(rec, i, parent)) continue;
                MergeMember m = rec.Member(i);
                MergeMath.Reframe(made.Location, cf, made.FlipX, made.Rotation, innerLoc, f, outerBase.FlipX, outerBase.Rotation,
                    m.Rel, m.RelRot, m.RelFlip, m.HasRotation, out IntVector2 rel, out int relRot, out bool relFlip);
                child.AddMemberHashed(m.Hash, rel, relRot, relFlip);
                map[i] = child.MemberCount - 1;
            }
            if (child.MemberCount == 0) return;
            foreach (KeyValuePair<int, int> kv in map)
            {
                int p = rec.Member(kv.Key).Parent;
                if (p != parent && map.TryGetValue(p, out int cp)) child.SetParent(kv.Value, cp);
            }
            for (int j = 0; j < rec.SettingCount; j++)
            {
                MergeSetting t = rec.Setting(j);
                if (map.TryGetValue(t.Member, out int cm)) child.AddSetting(cm, t.ToggleHash, t.Value);
            }
            MergeStore.WriteAt(ship, new PartSaveData(made), child);
        }

        private static bool DescendsFrom(MergeRecord rec, int member, int ancestor)
        {
            int p = rec.Member(member).Parent;
            for (int guard = 0; p >= 0 && guard < 32; guard++)
            {
                if (p == ancestor) return true;
                p = rec.Member(p).Parent;
            }
            return false;
        }

        private static readonly ConditionalWeakTable<ZtxHingeBaseComponent, StrongBox<int>> s_outChecked =
            new ConditionalWeakTable<ZtxHingeBaseComponent, StrongBox<int>>();

        private static void LayCutOff(ZtxHingeBaseComponent b, Ship section)
        {
            StrongBox<int> at = s_outChecked.GetValue(b, _ => new StrongBox<int>(s_splitGeneration));
            int generation = s_splitGeneration;
            if (at.Value == generation) return;
            at.Value = generation;
            Part bp = b.Part;
            MergeRecord old = MergeStore.Read(bp);
            if (old == null || old.MemberCount == 0 || section?.BlueprintParts == null) return;
            IntVector2 f = HingeShipManager.BaseFacing(bp);
            int wrecked = 0;
            for (int i = 0; i < old.MemberCount; i++)
            {
                MergeMember m = old.Member(i);
                PartRules rules = RulesOf(section, m.Hash);
                if (rules == null) continue;
                Placement(bp, f, m, 0, out IntVector2 cell, out int rot, out bool flip);
                if (section.Parts[cell, PartRectType.Normal] != null) continue;
                if (section.BlueprintParts.GetExactPart(rules, cell, rot, flip) != null && section.DestroyedParts != null
                    && !section.DestroyedParts.HasPartAt(rules.GetRect(cell, rot)))
                {
                    section.DestroyedParts.CreateDestroyedPart(rules, cell, rot, flip);
                    wrecked++;
                }
            }
            var handled = new HashSet<int>();
            for (int i = 0; i < old.MemberCount; i++)
            {
                for (int p = old.Member(i).Parent, guard = 0; p >= 0 && guard < 32; p = old.Member(p).Parent, guard++)
                {
                    Placement(bp, f, old.Member(p), 0, out IntVector2 pc, out _, out _);
                    if (section.Parts[pc, PartRectType.Normal] != null || section.DestroyedParts?[pc, PartRectType.Normal] != null)
                    {
                        handled.Add(i);
                        break;
                    }
                }
            }
            var (placed, _, _, settings) = PlaceBlueprints(bp, old, m =>
            {
                PartRules rules = RulesOf(section, m.Hash);
                if (rules == null) return false;
                Placement(bp, f, m, 0, out IntVector2 cell, out int rot, out bool _);
                foreach (IntVector2 c in rules.GetRect(cell, rot))
                    if (section.Parts[c, PartRectType.Normal] != null || section.DestroyedParts?[c, PartRectType.Normal] != null) return false;
                return true;
            }, 0, section, i => !handled.Contains(i));
            if (placed > 0 || wrecked > 0)
                Log.Info($"Hinge base at {bp.Location}: {placed + wrecked} part(s) cut off its arm by damage laid as repairs on the arm ({settings} setting(s) restored).");
        }

        public static void ServiceWrecks(Ship ship)
        {
            if (!MergeService.REBUILD_ENABLED || ship?.DestroyedParts == null || ship.BlueprintParts == null) return;
            try
            {
                List<(CommonBasePart Wreck, PartSaveData Psd)> todo = null;
                foreach (CommonBasePart dp in ship.DestroyedParts)
                {
                    if (dp?.Rules == null) continue;
                    var psd = new PartSaveData(dp);
                    if (MergeStore.ReadStateAt(ship, psd) != MergeState.Out) continue;
                    (todo ??= new List<(CommonBasePart, PartSaveData)>()).Add((dp, psd));
                }
                if (todo == null) return;
                foreach ((CommonBasePart wreck, PartSaveData psd) in todo)
                {
                    MergeRecord rec = MergeStore.ReadAt(ship, psd);
                    if (rec == null) continue;
                    IntVector2 f = HingeShipManager.BaseFacing(wreck.Rotation, wreck.FlipX);
                    int rodsRemoved = 0;
                    IntVector2 cell = wreck.Location + f;
                    for (int i = 0; i < WRECK_ROD_WALK; i++, cell += f)
                    {
                        Part there = ship.Parts[cell, PartRectType.Normal];
                        if (ZtxPistonRodComponent.ForPart(there) != null)
                        {
                            HingeShipManager.RemoveDeadPistonRod(there);
                            rodsRemoved++;
                            continue;
                        }
                        if (there != null) break;
                        if (HingeShipManager.ClearRodLeftover(ship, cell) > 0) continue;
                        if (ship.BlueprintParts[cell, PartRectType.Normal] != null || ship.DestroyedParts[cell, PartRectType.Normal] != null) break;
                    }
                    if (rodsRemoved > 0)
                        Log.Info($"Wrecked base at {wreck.Location}: {rodsRemoved} rod segment(s) of the wrecked piston removed.");
                    rec.State = MergeState.Lost;
                    rec.Slide = 0;
                    MergeStore.WriteAt(ship, psd, rec);
                    var (placed, occupied, unknown, settings) = PlaceBlueprints(wreck, rec, null, 0, ship);
                    Log.Info($"Wrecked base at {wreck.Location}: its arm laid as repairs with it -- {placed} part(s)" +
                        $" ({occupied} cell(s) occupied, {unknown} unknown, {settings} setting(s) restored).");
                }
            }
            catch (Exception ex)
            {
                Log.Exception("MergeRebuild.ServiceWrecks", ex);
            }
        }

        private const int WRECK_ROD_WALK = 16;

        public static Func<MergeMember, bool> MissingAtHome(Part basePart)
        {
            Ship ship = basePart.Ship;
            IntVector2 f = HingeShipManager.BaseFacing(basePart);
            return m => !Built(ship, basePart, f, m, out bool taken) && !taken;
        }

        public static bool IsComplete(Part basePart, MergeRecord rec, out int built, out int total)
        {
            built = 0;
            total = rec?.MemberCount ?? 0;
            Ship ship = basePart?.Ship;
            if (ship == null || rec == null) return false;
            IntVector2 f = HingeShipManager.BaseFacing(basePart);
            for (int i = 0; i < rec.MemberCount; i++)
                if (Built(ship, basePart, f, rec.Member(i), out _)) built++;
            return built == total;
        }

        public static void Service(HingeShipManager mgr, ZtxHingeBaseComponent b, bool engaged)
        {
            if (!MergeService.REBUILD_ENABLED || b?.Part?.Ship == null) return;
            try
            {
                MergeState state = MergeStore.ReadState(b.Part);
                StrongBox<int> seen = s_unjointed.GetValue(b, _ => new StrongBox<int>(0));
                if (state == MergeState.Lost)
                {
                    seen.Value = 0;
                    KeepAsRepairs(b.Part, MergeStore.Read(b.Part));
                    MergeRecord pruned = PruneLost(b.Part, MergeStore.Read(b.Part));
                    if (pruned == null || pruned.State != MergeState.Lost) return;
                    if (engaged) return;
                    MergeRecord lr = pruned;
                    if (lr != null && IsComplete(b.Part, lr, out _, out _))
                    {
                        lr.State = MergeState.None;
                        MergeStore.Write(b.Part, lr);
                        Log.Info($"Hinge base at {b.Part.Location}: lost section rebuilt -- plain hull while the base is disengaged.");
                    }
                    return;
                }
                if (state == MergeState.Out && HingeJointManager.HasPinWithTag(b))
                {
                    seen.Value = 0;
                    Ship section = mgr?.SectionInHomeGrid(b);
                    if (section != null)
                    {
                        LayCutOff(b, section);
                        RefreshFromLiveSection(b, section);
                    }
                    return;
                }
                if (state != MergeState.Out) { seen.Value = 0; return; }
                if (mgr == null || mgr.TipReachable(b)) { seen.Value = 0; return; }
                if (SectionInAssembly(b.Part)) { seen.Value = 0; return; }
                if (++seen.Value < LOST_GRACE_SECONDS) return;
                seen.Value = 0;
                MergeRecord rec = MergeStore.Read(b.Part);
                if (rec == null) return;
                if (b is ZtxPistonBaseComponent lostPiston) mgr.ClearRodStubForRebuild(lostPiston);
                var (placed, occupied, unknown, settings) = PlaceBlueprints(b.Part, rec, null);
                rec.State = MergeState.Lost;
                rec.Slide = 0;
                MergeStore.Write(b.Part, rec);
                Log.Info($"Hinge base at {b.Part.Location}: section lost -- {placed} part(s) placed as blueprints at home for the crew to rebuild" +
                    $" ({occupied} cell(s) occupied, {unknown} unknown part type(s), {settings} setting(s) restored).");
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("MergeRebuild.Service", ex);
            }
        }
    }
}
