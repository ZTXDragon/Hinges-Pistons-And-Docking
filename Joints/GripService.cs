using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static partial class GripService
    {
        private const int RETRY_TICKS = 30;
        private const float MOVING_CELLS_S = 0.05f;

        private sealed class Runtime
        {
            public int RetryTick;
            public string Status = "";
            public string Logged;
            public int RefusedTick = int.MinValue;
            public string RefusedWhy = "";
        }

        private static readonly ConditionalWeakTable<object, Runtime> s_rt = new ConditionalWeakTable<object, Runtime>();

        private static Runtime Rt(object owner) => s_rt.GetValue(owner, _ => new Runtime());


        public static string Status(ZtxPistonBaseComponent piston)
        {
            if (piston?.Part == null) return "";
            Part tip = piston.PairedTip?.Part;
            bool engaged = tip?.Ship != null && tip.Ship != piston.Part.Ship && HingeJointManager.HasPinWithTag(piston);
            if (!engaged)
            {
                if (!MergeService.IsHeld(piston)) return "not engaged (switch the piston ON)";
                int members = MergeStore.Read(piston.Part)?.MemberCount ?? 1;
                return members > 1 ? $"merged into the hull, holding {members - 1} part{(members == 2 ? "" : "s")}" : "merged into the hull, empty";
            }
            int held = HeldCount(tip);
            string state = held > 0 ? $"holding {held} part{(held == 1 ? "" : "s")}" : "empty";
            if (GripStore.Desired(piston.Part) == GripStore.GRIP_OFF) return state;
            string last = s_rt.TryGetValue(piston, out Runtime rt) ? rt.Status : "";
            return string.IsNullOrEmpty(last) || last == state ? state : state + " -- " + last;
        }

        public static bool IsHolding(ZtxPistonBaseComponent piston)
        {
            Part tip = piston?.PairedTip?.Part;
            return tip?.Ship != null && tip.Ship != piston.Part?.Ship && HasOthers(tip);
        }

        public static bool RefusedSince(object owner, int tick, out string why)
        {
            why = "";
            if (owner == null || !s_rt.TryGetValue(owner, out Runtime rt) || rt.RefusedTick < tick) return false;
            why = rt.RefusedWhy;
            return true;
        }

        public static ZtxPistonBaseComponent PistonOf(Part p)
        {
            if (p == null) return null;
            foreach (PartComponent c in p.Components)
                if (c is ZtxPistonBaseComponent pb) return pb;
            return null;
        }

        public static bool AreaOnHead(ZtxPistonBaseComponent piston, List<IntVector2> into, out Ship ship)
        {
            ship = null;
            into.Clear();
            Part bp = piston?.Part;
            if (bp?.Ship == null) return false;
            GripArea area = GripStore.Read(bp);
            if (!area.On) return false;
            Part tip = piston.PairedTip?.Part;
            if (tip?.Ship == null)
            {
                IntVector2 f = HingeShipManager.BaseFacing(bp);
                Part home = bp.Ship.Parts[bp.Location + f, PartRectType.Physical];
                tip = ZtxHingeTipComponent.ForPart(home) != null ? home : null;
                if (tip == null)
                {
                    ship = bp.Ship;
                    GripMath.AreaCells(bp.Location + f, f, area.InLine, area.Width, area.Length, area.Offset, into);
                    return true;
                }
            }
            ship = tip.Ship;
            IntVector2 front = LogicSource.Facing(tip);
            GripMath.AreaCells(Anchor(tip, front), front, area.InLine, area.Width, area.Length, area.Offset, into);
            return true;
        }


        public static List<Part> EngageSet(Ship ship, Part basePart, Part tipPart, GripArea area, out int cut)
        {
            IntVector2 front = LogicSource.Facing(tipPart);
            var cells = new List<IntVector2>();
            GripMath.AreaCells(Anchor(tipPart, front), front, area.InLine, area.Width, area.Length, area.Offset, cells);
            var inArea = new HashSet<IntVector2>(cells);
            List<Part> taken = GripMath.Select(StructuralAdjacency.Neighbours(ship, tipPart),
                p => Inside(p, inArea),
                p => StructuralAdjacency.Neighbours(ship, p),
                p => p == tipPart || Excluded(ship, basePart, p), out cut);
            taken.Insert(0, tipPart);
            return taken;
        }


        internal struct Segment
        {
            public Ship Ship;
            public GridTransform Xf;
            public bool IsCarrier;
        }

        public static void Service(HingeShipManager mgr, ZtxPistonBaseComponent piston, int tick)
        {
            try
            {
                Part bp = piston?.Part;
                Ship carrier = bp?.Ship;
                if (carrier?.Physics?.Body == null || mgr == null) return;
                if (tick % RETRY_TICKS == 0) SensorAreaOverlay.Attach(carrier.Sim);
                int desired = GripStore.Desired(bp);
                if (desired == GripStore.GRIP_OFF) return;
                if (MergeService.IsMerged(bp))
                {
                    ServiceMerged(piston, carrier, desired, tick);
                    return;
                }
                Part tip = piston.PairedTip?.Part;
                if (tip?.Ship == null || tip.Ship == carrier || piston.Returning || !HingeJointManager.HasPinWithTag(piston)) return;
                bool holding = HasOthers(tip);
                if ((desired == GripStore.GRIP_ATTACH) == holding)
                {
                    if (s_rt.TryGetValue(piston, out Runtime done) && done.Status.Length > 0) done.Status = "";
                    return;
                }
                Runtime rt = Rt(piston);
                if (tick < rt.RetryTick) return;
                rt.RetryTick = tick + RETRY_TICKS;
                string what = desired == GripStore.GRIP_ATTACH ? "attach" : "release";
                if (!AtRest(mgr, piston, out string wait))
                {
                    Note(rt, bp, what + " waits -- " + wait, log: !wait.StartsWith("moving", StringComparison.Ordinal));
                    return;
                }
                List<Segment> segments = Segments(mgr, piston, carrier, tip, out string carrierWait);
                if (desired == GripStore.GRIP_ATTACH) Attach(piston, carrier, tip, segments, carrierWait, rt);
                else Release(piston, carrier, tip, segments, carrierWait, rt);
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("GripService.Service", ex);
            }
        }

        private static bool AtRest(HingeShipManager mgr, ZtxPistonBaseComponent piston, out string wait)
        {
            wait = "";
            if (!HingeJointManager.TryGetSliderState(piston, out _, out float speed)) { wait = "not formed"; return false; }
            if (Math.Abs(speed / mgr.ShipScaleValue) > MOVING_CELLS_S) { wait = "moving"; return false; }
            return true;
        }

        private static List<Segment> Segments(HingeShipManager mgr, ZtxPistonBaseComponent piston, Ship carrier, Part tip, out string carrierWait)
        {
            var list = new List<Segment>();
            Ship head = tip.Ship;
            if (Aligned(mgr, piston, carrier, tip, out GridTransform cxf, out carrierWait))
                list.Add(new Segment { Ship = carrier, Xf = cxf, IsCarrier = true });
            var ships = new List<Ship>();
            HingeJointManager.GetAssemblyShips(carrier, ships);
            Vector2 world = head.Physics.Body.GetWorldPoint(new Vector2(0.5f, 0.5f) * head.Scale);
            foreach (Ship s in ships)
            {
                if (s == carrier || s == head || s?.Physics?.Body == null) continue;
                Vector2 g = s.Physics.Body.GetLocalPoint(world) / s.Scale;
                if (GripMath.TryGrid(head.Physics.Body.Rotation - s.Physics.Body.Rotation, g.X, g.Y, out GridTransform xf))
                    list.Add(new Segment { Ship = s, Xf = xf, IsCarrier = false });
            }
            return list;
        }

        private static void Attach(ZtxPistonBaseComponent piston, Ship carrier, Part tip, List<Segment> segments, string carrierWait, Runtime rt)
        {
            Part bp = piston.Part;
            GripArea area = GripStore.Read(bp);
            if (!area.On)
            {
                Note(rt, bp, "attach refused -- set a move area first (Grip window)", log: true);
                return;
            }
            IntVector2 front = LogicSource.Facing(tip);
            var cells = new List<IntVector2>();
            GripMath.AreaCells(Anchor(tip, front), front, area.InLine, area.Width, area.Length, area.Offset, cells);
            bool ok = TakeFromSegments(tip, cells, segments,
                (seg, p) => Excluded(seg.Ship, bp, p) || (!seg.IsCarrier && IsJointPart(p)),
                (seg, taken) => seg.IsCarrier ? bp : AnchorOf(seg.Ship, taken),
                seg => { if (seg.IsCarrier) MergeService.InvalidateCellMap(carrier); },
                "the base's ship", out int total, out int cuts, out int extra, out string from, out string error);
            if (!ok)
            {
                Note(rt, bp, "attach refused -- " + error, log: true);
                return;
            }
            if (total == 0)
            {
                Segment carrierSeg = segments.Find(x => x.IsCarrier);
                string report = carrierSeg.Ship != null
                    ? $"at {SlideOf(carrierSeg.Xf, piston)} cell(s) -- nothing connects to the head inside the area; " + FrontReport(tip, carrierSeg.Xf, carrier, bp, AreaOn(cells, carrierSeg.Xf))
                    : "nothing connects to the head inside the area" + (segments.Count == 0 ? "; no segment lines up with the head (base's ship: " + carrierWait + ")" : " on " + segments.Count + " lined-up segment(s)");
                Note(rt, bp, "attach refused " + report, log: true);
                return;
            }
            rt.Status = "";
            rt.Logged = null;
            Log.Info($"Piston base at {bp.Location}: attached {total} part(s) to the head from {from} ({cuts} link(s) cut at the area edge" +
                (extra > 0 ? $", {extra} outside the area came along -- they hung only off the taken parts" : "") + ").");
        }

        private static void Release(ZtxPistonBaseComponent piston, Ship carrier, Part tip, List<Segment> segments, string carrierWait, Runtime rt)
        {
            Part bp = piston.Part;
            LogReleaseCandidates(bp, tip, segments, carrierWait);
            if (!GiveToBest(tip, segments, out int count, out Segment best, out int bestLinks, out Ship moved, out string error))
            {
                if (error != null) Note(rt, bp, "release refused -- " + error, log: true);
                return;
            }
            rt.Status = "";
            rt.Logged = null;
            if (best.Ship == null)
            {
                Log.Info($"Piston base at {bp.Location}: released {count} part(s); nothing lined up connects to them, so they drop free (ship {moved.UniqueID}).");
                return;
            }
            if (best.IsCarrier) MergeService.InvalidateCellMap(carrier);
            Log.Info($"Piston base at {bp.Location}: released {count} part(s) into " + (best.IsCarrier ? "the base's ship" : "segment " + best.Ship.UniqueID)
                + $" ({bestLinks} connection(s)); the head stays out, empty.");
        }

        private static void LogReleaseCandidates(Part bp, Part tip, List<Segment> segments, string carrierWait)
        {
            try
            {
                Ship headShip = tip?.Ship;
                if (headShip == null) return;
                var held = new List<Part>();
                foreach (Part p in headShip.Parts)
                    if (p != tip) held.Add(p);
                if (held.Count == 0) return;
                var sb = new System.Text.StringBuilder();
                bool carrierIn = false;
                foreach (Segment seg in segments)
                {
                    if (seg.IsCarrier) carrierIn = true;
                    sb.Append(seg.IsCarrier ? "base's ship " : "segment " + seg.Ship?.UniqueID + " ");
                    sb.Append(Links(held, headShip, seg)).Append(" link(s); ");
                }
                if (!carrierIn) sb.Insert(0, "base's ship NOT a candidate (" + (string.IsNullOrEmpty(carrierWait) ? "?" : carrierWait) + "); ");
                Log.Info($"Piston base at {bp.Location}: release candidates for {held.Count} held part(s) -- {sb}(most links wins, the base's ship on a tie).");
            }
            catch (Exception ex)
            {
                Log.Exception("GripService.LogReleaseCandidates", ex);
            }
        }


        internal static bool TakeFromSegments(Part head, List<IntVector2> cells, List<Segment> segments, Func<Segment, Part, bool> excluded,
            Func<Segment, List<Part>, Part> anchorOf, Action<Segment> tookFrom, string carrierName,
            out int total, out int cuts, out int extra, out string from, out string error)
        {
            total = cuts = extra = 0;
            from = "";
            error = null;
            Ship headShip = head.Ship;
            foreach (Segment seg in segments)
            {
                var inArea = new HashSet<IntVector2>();
                foreach (IntVector2 c in cells) inArea.Add(seg.Xf.TransformCell(c));
                Segment cs = seg;
                List<Part> taken = GripMath.Select(SeedsAcross(head, seg.Xf, seg.Ship),
                    p => Inside(p, inArea),
                    p => StructuralAdjacency.Neighbours(cs.Ship, p),
                    p => excluded(cs, p), out int cut);
                if (taken.Count == 0) continue;
                int asked = taken.Count;
                Ship moved = ShipSplitter.Split(seg.Ship, taken, anchorOf(seg, taken));
                if (moved == null)
                {
                    error = "the split failed (see the line above)";
                    return false;
                }
                if (!ShipMerger.Merge(headShip, moved, new object(), seg.Xf.Inverse()))
                {
                    bool back = ShipMerger.Merge(seg.Ship, moved, new object(), GridTransform.Identity);
                    error = "a cell on the head's side is taken (see the Merge line above)" + (back ? "" : "; the parts could not go back and float free");
                    return false;
                }
                tookFrom?.Invoke(seg);
                total += taken.Count;
                cuts += cut;
                extra += taken.Count - asked;
                from += (from.Length > 0 ? ", " : "") + (seg.IsCarrier ? carrierName : "segment " + seg.Ship.UniqueID);
            }
            return true;
        }

        internal static bool GiveToBest(Part head, List<Segment> segments, out int count, out Segment best, out int bestLinks, out Ship moved, out string error)
        {
            Ship headShip = head.Ship;
            var held = new List<Part>();
            foreach (Part p in headShip.Parts)
                if (p != head) held.Add(p);
            count = held.Count;
            best = default;
            bestLinks = 0;
            moved = null;
            error = null;
            if (held.Count == 0) return false;
            foreach (Segment seg in segments)
            {
                int links = Links(held, headShip, seg);
                if (links > bestLinks || (links > 0 && links == bestLinks && seg.IsCarrier))
                {
                    bestLinks = links;
                    best = seg;
                }
            }
            moved = ShipSplitter.Split(headShip, held, head);
            if (moved == null)
            {
                error = "the split failed (see the line above)";
                return false;
            }
            if (best.Ship == null) return true;
            if (!ShipMerger.Merge(best.Ship, moved, new object(), best.Xf))
            {
                bool back = ShipMerger.Merge(headShip, moved, new object(), GridTransform.Identity);
                error = "a cell of the receiving segment is taken where the parts would go (see the Merge line above)" + (back ? "" : "; the parts could not go back and float free");
                return false;
            }
            return true;
        }

        private static void ServiceMerged(ZtxPistonBaseComponent piston, Ship carrier, int desired, int tick)
        {
            Part bp = piston.Part;
            MergeRecord rec = MergeStore.Read(bp);
            if (rec == null || rec.State != MergeState.Merged) return;
            bool holding = rec.MemberCount > 1;
            if ((desired == GripStore.GRIP_ATTACH) == holding)
            {
                if (s_rt.TryGetValue(piston, out Runtime done) && done.Status.Length > 0) done.Status = "";
                return;
            }
            Runtime rt = Rt(piston);
            if (tick < rt.RetryTick) return;
            rt.RetryTick = tick + RETRY_TICKS;
            IntVector2 f = HingeShipManager.BaseFacing(bp);
            Part tip = carrier.Parts[bp.Location + f * (1 + rec.Slide), PartRectType.Normal];
            if (ZtxHingeTipComponent.ForPart(tip) == null)
            {
                Note(rt, bp, (desired == GripStore.GRIP_ATTACH ? "attach" : "release") + " waits -- no head at the end of the merged rod", log: true);
                return;
            }
            List<(int Index, Part Part)> members = MergeService.MemberParts(bp, rec);
            var memberSet = new HashSet<Part>();
            int tipIndex = -1;
            foreach ((int i, Part p) in members)
            {
                memberSet.Add(p);
                if (p == tip) tipIndex = i;
            }
            if (desired == GripStore.GRIP_RELEASE)
            {
                if (tipIndex < 0)
                {
                    Note(rt, bp, "release refused -- the head is not in the merged section's record", log: true);
                    return;
                }
                var held = new List<Part>();
                foreach ((int _, Part p) in members) if (p != tip) held.Add(p);
                bool joined = false;
                foreach (Part h in held)
                {
                    foreach (Part n in StructuralAdjacency.Neighbours(carrier, h))
                        if (n != tip && n != bp && !memberSet.Contains(n) && ZtxPistonRodComponent.ForPart(n) == null) { joined = true; break; }
                    if (joined) break;
                }
                int keep = tipIndex;
                MergeStore.Write(bp, rec.Keep(i => i == keep));
                MergeService.InvalidateCellMap(carrier);
                rt.Status = "";
                rt.Logged = null;
                if (!joined && held.Count > 0)
                {
                    Ship free = ShipSplitter.Split(carrier, held, bp);
                    Log.Info($"Piston base at {bp.Location}: released {held.Count} part(s) while merged; they hung only off the head, so they drop free"
                        + (free != null ? $" (ship {free.UniqueID})." : " -- the split failed (see the line above); they stay as hull."));
                    return;
                }
                Log.Info($"Piston base at {bp.Location}: released {held.Count} part(s) while merged -- they stay as hull when the arm unmerges.");
                return;
            }
            GripArea area = GripStore.Read(bp);
            if (!area.On)
            {
                Note(rt, bp, "attach refused -- set a move area first (Grip window)", log: true);
                return;
            }
            IntVector2 front = LogicSource.Facing(tip);
            var cells = new List<IntVector2>();
            GripMath.AreaCells(Anchor(tip, front), front, area.InLine, area.Width, area.Length, area.Offset, cells);
            var inArea = new HashSet<IntVector2>(cells);
            List<Part> taken = GripMath.Select(StructuralAdjacency.Neighbours(carrier, tip),
                p => Inside(p, inArea),
                p => StructuralAdjacency.Neighbours(carrier, p),
                p => p == tip || p == bp || memberSet.Contains(p) || ZtxPistonRodComponent.ForPart(p) != null || MergeService.TryMapLiveCell(carrier, p.Location, out _),
                out int cut);
            if (taken.Count == 0)
            {
                Note(rt, bp, "attach refused while merged -- nothing connects to the head inside the area", log: true);
                return;
            }
            rec.Append(MergeRebuild.RecordOf(bp, taken, GridTransform.Identity, rec.Slide, MergeState.Merged));
            MergeStore.Write(bp, rec);
            MergeService.InvalidateCellMap(carrier);
            rt.Status = "";
            rt.Logged = null;
            Log.Info($"Piston base at {bp.Location}: attached {taken.Count} part(s) while merged ({cut} link(s) cut at the area edge) -- they leave with the head when the arm unmerges.");
        }


        private static bool Aligned(HingeShipManager mgr, ZtxPistonBaseComponent piston, Ship carrier, Part tip, out GridTransform xf, out string wait)
        {
            xf = GridTransform.Identity;
            wait = "";
            if (!HingeJointManager.TryGetSliderState(piston, out float slide, out float speed)) { wait = "not formed"; return false; }
            float scale = mgr.ShipScaleValue;
            if (!MergeMath.TryAlignedSlide(slide / scale, speed / scale, piston.Rules.MaxExtension, out int k))
            {
                wait = Math.Abs(speed / scale) > MOVING_CELLS_S ? "moving" : $"not at a whole cell ({slide / scale:0.00} cells)";
                return false;
            }
            if (piston.Rods.Count != k) { wait = $"rods not settled ({piston.Rods.Count} rods at {k} cells)"; return false; }
            int quarter = MergeMath.QuarterTurns(tip.Ship.Physics.Body.Rotation - carrier.Physics.Body.Rotation);
            if (quarter != 0) { wait = "the head's grid is turned"; return false; }
            xf = MergeMath.PistonTransform(piston.Part.Location, HingeShipManager.BaseFacing(piston.Part), k, tip.Location, 0);
            return true;
        }

        private static HashSet<IntVector2> AreaOn(List<IntVector2> cells, GridTransform xf)
        {
            var set = new HashSet<IntVector2>();
            foreach (IntVector2 c in cells) set.Add(xf.TransformCell(c));
            return set;
        }

        private static bool IsJointPart(Part p)
        {
            if (ZtxHingeTipComponent.ForPart(p) != null) return true;
            foreach (PartComponent c in p.Components)
                if (c is ZtxHingeBaseComponent) return true;
            return false;
        }

        private static Part AnchorOf(Ship ship, List<Part> taken)
        {
            var set = new HashSet<Part>(taken);
            Part any = null;
            foreach (Part p in ship.Parts)
            {
                if (set.Contains(p)) continue;
                if (IsJointPart(p)) return p;
                any ??= p;
            }
            return any;
        }

        private static int Links(List<Part> held, Ship headShip, Segment seg)
        {
            int links = 0;
            foreach (Part h in held)
            {
                AdjacencyFlags mine = h.Rules.AllowedContiguity.RotateFlip(h.Rotation, h.FlipX);
                foreach ((IntVector2 cell, AdjacencyFlags edge) in h.PhysicalRect.GetAdjacentCellsWithAdjacency(mine))
                {
                    if (headShip.Parts[cell, PartRectType.Physical] != null) continue;
                    if (Connects(seg.Ship.Parts[seg.Xf.TransformCell(cell), PartRectType.Physical], edge, seg.Xf)) links++;
                }
            }
            return links;
        }

        private static bool Connects(Part other, AdjacencyFlags edge, GridTransform xf)
        {
            if (other == null) return false;
            AdjacencyFlags theirs = other.Rules.AllowedContiguity.RotateFlip(Mathx.Wrap(other.Rotation - xf.K + 2, 4), other.FlipX);
            return (edge & theirs) != AdjacencyFlags.None;
        }

        private static int SlideOf(GridTransform xf, ZtxPistonBaseComponent piston)
        {
            IntVector2 f = HingeShipManager.BaseFacing(piston.Part);
            Part tip = piston.PairedTip?.Part;
            if (tip == null) return 0;
            IntVector2 d = xf.TransformCell(tip.Location) - (piston.Part.Location + f);
            return d.X * f.X + d.Y * f.Y;
        }

        private static string FrontReport(Part tip, GridTransform xf, Ship carrier, Part basePart, HashSet<IntVector2> inArea)
        {
            IntVector2 front = LogicSource.Facing(tip);
            IntVector2 cell = xf.TransformCell(Anchor(tip, front) + front);
            Part p = carrier.Parts[cell, PartRectType.Physical];
            if (p == null) return $"the cell in front of the head ({cell}) is empty -- stop the head touching the parts";
            string id = p.Rules.ID.ToString();
            if (Excluded(carrier, basePart, p)) return $"in front ({cell}): {id}, which is never taken (base, rod or another piston's merged section)";
            if (!Inside(p, inArea)) return $"in front ({cell}): {id}, but it is not wholly inside the move area";
            if (!SeedsAcross(tip, xf, carrier).Contains(p)) return $"in front ({cell}): {id}, but its edge does not connect to the head";
            return $"in front ({cell}): {id}";
        }

        private static IntVector2 Anchor(Part tip, IntVector2 front)
        {
            IntVector2 best = tip.Location;
            int bestDot = int.MinValue;
            foreach (IntVector2 c in tip.GetCells(PartRectType.Physical))
            {
                int dot = c.X * front.X + c.Y * front.Y;
                if (dot > bestDot) { bestDot = dot; best = c; }
            }
            return best;
        }

        private static bool Inside(Part p, HashSet<IntVector2> area)
        {
            foreach (IntVector2 c in p.GetCells(PartRectType.Physical))
                if (!area.Contains(c)) return false;
            return true;
        }

        private static bool Excluded(Ship ship, Part basePart, Part p)
        {
            return p == basePart || ZtxPistonRodComponent.ForPart(p) != null || MergeService.TryMapLiveCell(ship, p.Location, out _);
        }

        private static List<Part> SeedsAcross(Part tip, GridTransform xf, Ship segment)
        {
            var seeds = new List<Part>();
            AdjacencyFlags mine = tip.Rules.AllowedContiguity.RotateFlip(tip.Rotation, tip.FlipX);
            foreach ((IntVector2 cell, AdjacencyFlags edge) in tip.PhysicalRect.GetAdjacentCellsWithAdjacency(mine))
            {
                Part other = segment.Parts[xf.TransformCell(cell), PartRectType.Physical];
                if (other == null || seeds.Contains(other) || !Connects(other, edge, xf)) continue;
                seeds.Add(other);
            }
            return seeds;
        }

        private static bool HasOthers(Part tip)
        {
            foreach (Part p in tip.Ship.Parts)
                if (p != tip) return true;
            return false;
        }

        private static int HeldCount(Part tip)
        {
            int n = 0;
            foreach (Part p in tip.Ship.Parts)
                if (p != tip) n++;
            return n;
        }

        private static void Note(Runtime rt, Part bp, string status, bool log) => Note(rt, bp, "Piston base", status, log);

        private static void Note(Runtime rt, Part bp, string who, string status, bool log)
        {
            rt.Status = status;
            if (status.IndexOf(" refused", StringComparison.Ordinal) >= 0)
            {
                rt.RefusedTick = ZtxSignalComponent.Clock(bp.Ship);
                rt.RefusedWhy = status;
            }
            if (!log || rt.Logged == status) return;
            rt.Logged = status;
            Log.Info($"{who} at {bp.Location}: {status}.");
        }
    }
}
