using System;
using System.Collections.Generic;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static partial class GripService
    {
        private const float CART_REST_SPEED = 0.05f;

        public static int CartDesired(Part cartPart) => LogicSource.Mode(cartPart, RailToggles.GripId, GripStore.GRIP_OFF);

        public static bool IsCartHolding(Part cartPart)
        {
            ZtxRailCartComponent cart = ZtxRailCartComponent.ForPart(cartPart);
            if (cart == null || cartPart.Ship == null) return false;
            if (cart.PairedTerminal != null) return HasOthers(cartPart);
            CartMergeRecord rec = RailShipManager.ReadCartMerge(cartPart);
            if (rec != null) return rec.MemberCount > 1;
            foreach (Part n in StructuralAdjacency.Neighbours(cartPart.Ship, cartPart))
                if (!RailNetwork.IsRailPart(n)) return true;
            return false;
        }

        public static string StatusOf(Part p)
        {
            if (ZtxRailCartComponent.ForPart(p) != null) return CartStatus(p);
            return Status(PistonOf(p));
        }

        private static string CartStatus(Part cp)
        {
            ZtxRailCartComponent cart = ZtxRailCartComponent.ForPart(cp);
            if (cart == null) return "";
            string state;
            if (cart.PairedTerminal != null)
            {
                int held = HeldCount(cp);
                state = held > 0 ? $"holding {held} part{(held == 1 ? "" : "s")}" : "empty";
            }
            else
            {
                CartMergeRecord rec = RailShipManager.ReadCartMerge(cp);
                if (rec == null) return RailShipManager.TerminalFor(cp) == null ? "not on a rail" : "merged at home -- the grip works riding, or merged at a station / on HOLD";
                state = rec.MemberCount > 1 ? $"merged, holding {rec.MemberCount - 1} part{(rec.MemberCount == 2 ? "" : "s")}" : "merged, empty";
            }
            if (CartDesired(cp) == GripStore.GRIP_OFF) return state;
            string last = s_rt.TryGetValue(cart, out Runtime rt) ? rt.Status : "";
            return string.IsNullOrEmpty(last) || last == state ? state : state + " -- " + last;
        }

        public static bool AreaOnCart(Part cp, List<IntVector2> into, out Ship ship)
        {
            ship = cp?.Ship;
            into.Clear();
            if (ship == null) return false;
            GripArea area = GripStore.Read(cp);
            if (!area.On) return false;
            CartArea(cp, area, into);
            return true;
        }

        private static void CartArea(Part cp, GripArea area, List<IntVector2> into)
        {
            IntVector2 arrow = RailShipManager.CartFacing(cp);
            IntVector2 anchor = Anchor(cp, new IntVector2(-arrow.X, -arrow.Y));
            GripMath.CartAreaCells(anchor, arrow, area.InLine, area.Width, area.Length, area.Offset, into);
        }

        private static bool CartExcluded(Ship ship, Part p)
        {
            return RailNetwork.IsRailPart(p) || ZtxRailCartComponent.ForPart(p) != null || IsJointPart(p)
                || ZtxPistonRodComponent.ForPart(p) != null || MergeService.TryMapLiveCell(ship, p.Location, out _)
                || StructuralAdjacency.TouchesRail(ship, p);
        }

        internal static List<Part> CartEngageSet(Ship ship, Part cp, GripArea area, out int cut)
        {
            var cells = new List<IntVector2>();
            CartArea(cp, area, cells);
            var inArea = new HashSet<IntVector2>(cells);
            return GripMath.Select(StructuralAdjacency.Neighbours(ship, cp),
                p => Inside(p, inArea),
                p => StructuralAdjacency.Neighbours(ship, p),
                p => p == cp || CartExcluded(ship, p), out cut);
        }

        public static void ServiceCart(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart, int tick)
        {
            try
            {
                Part cp = cart?.Part;
                Ship carrier = terminal?.Part?.Ship;
                if (cp?.Ship == null || carrier?.Physics?.Body == null) return;
                if (tick % RETRY_TICKS == 0) SensorAreaOverlay.Attach(carrier.Sim);
                int desired = CartDesired(cp);
                if (desired == GripStore.GRIP_OFF) return;
                if (cp.Ship == carrier)
                {
                    if (RailShipManager.IsMergedAway(cp)) ServiceCartMerged(terminal, cart, carrier, desired, tick);
                    return;
                }
                if (cart.PairedTerminal == null) return;
                bool holding = HasOthers(cp);
                if ((desired == GripStore.GRIP_ATTACH) == holding)
                {
                    if (s_rt.TryGetValue(cart, out Runtime done) && done.Status.Length > 0) done.Status = "";
                    return;
                }
                Runtime rt = Rt(cart);
                if (tick < rt.RetryTick) return;
                rt.RetryTick = tick + RETRY_TICKS;
                string what = desired == GripStore.GRIP_ATTACH ? "attach" : "release";
                if (cart.Phase == RailPhase.Moving || !HingeJointManager.TryGetWeldState(cart, out _, out float speed) || speed > CART_REST_SPEED)
                {
                    Note(rt, cp, "Rail cart", what + " waits -- moving", log: false);
                    return;
                }
                List<Segment> segments = CartSegments(carrier, cp.Ship);
                if (desired == GripStore.GRIP_ATTACH) CartAttach(terminal, cp, carrier, segments, rt);
                else CartRelease(cp, carrier, segments, rt);
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("GripService.ServiceCart", ex);
            }
        }

        private static List<Segment> CartSegments(Ship carrier, Ship head)
        {
            var list = new List<Segment>();
            var ships = new List<Ship>();
            HingeJointManager.GetAssemblyShips(carrier, ships);
            if (!ships.Contains(carrier)) ships.Add(carrier);
            Vector2 world = head.Physics.Body.GetWorldPoint(new Vector2(0.5f, 0.5f) * head.Scale);
            foreach (Ship s in ships)
            {
                if (s == head || s?.Physics?.Body == null) continue;
                Vector2 g = s.Physics.Body.GetLocalPoint(world) / s.Scale;
                if (GripMath.TryGrid(head.Physics.Body.Rotation - s.Physics.Body.Rotation, g.X, g.Y, out GridTransform xf))
                    list.Add(new Segment { Ship = s, Xf = xf, IsCarrier = s == carrier });
            }
            return list;
        }

        private static void CartAttach(ZtxRailTerminalComponent terminal, Part cp, Ship carrier, List<Segment> segments, Runtime rt)
        {
            GripArea area = GripStore.Read(cp);
            if (!area.On)
            {
                Note(rt, cp, "Rail cart", "attach refused -- set a grip area first (the cart's Grip window)", log: true);
                return;
            }
            var cells = new List<IntVector2>();
            CartArea(cp, area, cells);
            Part tp = terminal.Part;
            bool ok = TakeFromSegments(cp, cells, segments,
                (seg, p) => CartExcluded(seg.Ship, p),
                (seg, taken) => seg.IsCarrier ? tp : AnchorOf(seg.Ship, taken),
                seg => { if (seg.IsCarrier) MergeService.InvalidateCellMap(carrier); },
                "the ship", out int total, out int cuts, out int extra, out string from, out string error);
            if (!ok)
            {
                Note(rt, cp, "Rail cart", "attach refused -- " + error, log: true);
                return;
            }
            if (total == 0)
            {
                Note(rt, cp, "Rail cart", "attach refused -- nothing connects to the cart's back inside the area" +
                    (segments.Count == 0 ? "; nothing lines up with the cart here" : $" ({segments.Count} lined-up segment(s))"), log: true);
                return;
            }
            rt.Status = "";
            rt.Logged = null;
            Log.Info($"Rail cart at {cp.Location}: attached {total} part(s) from {from} ({cuts} link(s) cut at the area edge" +
                (extra > 0 ? $", {extra} outside the area came along -- they hung only off the taken parts" : "") + ").");
        }

        private static void CartRelease(Part cp, Ship carrier, List<Segment> segments, Runtime rt)
        {
            if (!GiveToBest(cp, segments, out int count, out Segment best, out int bestLinks, out Ship moved, out string error))
            {
                if (error != null) Note(rt, cp, "Rail cart", "release refused -- " + error, log: true);
                return;
            }
            rt.Status = "";
            rt.Logged = null;
            if (best.Ship == null)
            {
                Log.Info($"Rail cart at {cp.Location}: released {count} part(s); nothing lined up connects to them, so they drop free (ship {moved.UniqueID}).");
                return;
            }
            if (best.IsCarrier) MergeService.InvalidateCellMap(carrier);
            Log.Info($"Rail cart at {cp.Location}: released {count} part(s) into " + (best.IsCarrier ? "the ship" : "segment " + best.Ship.UniqueID) + $" ({bestLinks} connection(s)).");
        }

        private static void ServiceCartMerged(ZtxRailTerminalComponent terminal, ZtxRailCartComponent cart, Ship carrier, int desired, int tick)
        {
            Part cp = cart.Part;
            CartMergeRecord rec = RailShipManager.ReadCartMerge(cp);
            if (rec == null) return;
            rec = rec.RebasedTo(cp);
            bool holding = rec.MemberCount > 1;
            if ((desired == GripStore.GRIP_ATTACH) == holding)
            {
                if (s_rt.TryGetValue(cart, out Runtime done) && done.Status.Length > 0) done.Status = "";
                return;
            }
            Runtime rt = Rt(cart);
            if (tick < rt.RetryTick) return;
            rt.RetryTick = tick + RETRY_TICKS;
            List<(int Index, Part Part)> members = RailShipManager.CartMembers(cp, rec);
            var memberSet = new HashSet<Part>();
            foreach ((int _, Part p) in members) memberSet.Add(p);
            var kept = new CartMergeRecord { Away = true, K = rec.K, T = rec.T, Home = rec.Home, MergeRot = rec.MergeRot };
            kept.AddMember(IntVector2.Zero, MergeRecord.Hash(cp.Rules.ID.ToString()));
            for (int fi = 0; fi < rec.FrameCount; fi++) { var fr = rec.Frame(fi); kept.AddFrame(fr.Kind, fr.Cell, fr.RotFlip); }
            if (desired == GripStore.GRIP_RELEASE)
            {
                var held = new List<Part>();
                foreach ((int _, Part p) in members) if (p != cp) held.Add(p);
                bool joined = false;
                foreach (Part h in held)
                {
                    foreach (Part n in StructuralAdjacency.Neighbours(carrier, h))
                        if (n != cp && !memberSet.Contains(n) && !RailNetwork.IsRailPart(n)) { joined = true; break; }
                    if (joined) break;
                }
                RailShipManager.WriteCartMerge(cp, kept, rec.MemberCount, rec.FrameCount);
                rt.Status = "";
                rt.Logged = null;
                if (!joined && held.Count > 0)
                {
                    Ship free = ShipSplitter.Split(carrier, held, terminal.Part);
                    Log.Info($"Rail cart at {cp.Location}: released {held.Count} part(s) while merged; they hung only off the cart, so they drop free"
                        + (free != null ? $" (ship {free.UniqueID})." : " -- the split failed (see the line above); they stay as hull."));
                    return;
                }
                Log.Info($"Rail cart at {cp.Location}: released {held.Count} part(s) while merged -- they stay as hull when the cart leaves.");
                return;
            }
            GripArea area = GripStore.Read(cp);
            if (!area.On)
            {
                Note(rt, cp, "Rail cart", "attach refused -- set a grip area first (the cart's Grip window)", log: true);
                return;
            }
            var cells = new List<IntVector2>();
            CartArea(cp, area, cells);
            var inArea = new HashSet<IntVector2>(cells);
            List<Part> taken = GripMath.Select(StructuralAdjacency.Neighbours(carrier, cp),
                p => Inside(p, inArea),
                p => StructuralAdjacency.Neighbours(carrier, p),
                p => p == cp || memberSet.Contains(p) || CartExcluded(carrier, p),
                out int cut);
            if (taken.Count == 0)
            {
                bool onSegment = !ReferenceEquals(AssemblyAi.AnchorFor(carrier), carrier);
                Note(rt, cp, "Rail cart", "attach refused while merged -- nothing connects to the cart's back inside the area" +
                    (onSegment ? " (the cart is merged into a jointed segment, not the main hull: the hull's parts are on another ship until that segment merges)" : ""), log: true);
                return;
            }
            GridTransform back = rec.Xf.Inverse();
            foreach (Part p in taken) rec.AddMember(back.TransformCell(p.Location) - rec.Home, MergeRecord.Hash(p.Rules.ID.ToString()));
            RailShipManager.WriteCartMerge(cp, rec, 0);
            rt.Status = "";
            rt.Logged = null;
            Log.Info($"Rail cart at {cp.Location}: attached {taken.Count} part(s) while merged ({cut} link(s) cut at the area edge) -- they leave with the cart.");
        }
    }
}
