using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Cosmoteer.Crew;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Crew;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Crew;
using Cosmoteer.Ships.Parts.Logic;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class CorridorHinge
    {
        internal sealed class Link
        {
            public Airlock Partner;
            public Ship PartnerShip;
            public Ship Ship;
            public Part Anchor;
        }

        private static readonly object s_lock = new object();
        private static readonly HashSet<ZtxCorridorLinkGateComponent> s_gates = new HashSet<ZtxCorridorLinkGateComponent>();

        public static void Reset()
        {
            lock (s_lock) s_gates.Clear();
        }
        private static volatile Dictionary<Airlock, Link> s_links = new Dictionary<Airlock, Link>();
        private static volatile List<(Ship, Ship)> s_pairs = new List<(Ship, Ship)>();
        private static volatile Dictionary<Ship, int> s_group = new Dictionary<Ship, int>();
        private static volatile Dictionary<Ship, int> s_workGroup = new Dictionary<Ship, int>();

        internal static bool IsWorkLink(bool dockPartA, bool dockPartB) => !dockPartA && !dockPartB;

        private static bool IsDockPart(Part p) => p != null && (ZtxDockClampComponent.ForPart(p) != null || ZtxBayStationComponent.ForPart(p) != null);

        internal static bool SameWorkGroup(Ship a, Ship b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            Dictionary<Ship, int> g = s_workGroup;
            return g.TryGetValue(a, out int ga) && g.TryGetValue(b, out int gb) && ga == gb;
        }

        internal static bool HasWorkGroup(Ship ship) => ship != null && s_workGroup.ContainsKey(ship);

        internal static List<Ship> WorkGroupOf(Ship ship)
        {
            var list = new List<Ship>();
            Dictionary<Ship, int> g = s_workGroup;
            if (ship == null || !g.TryGetValue(ship, out int id)) return list;
            foreach (KeyValuePair<Ship, int> kv in g)
                if (kv.Value == id && !ReferenceEquals(kv.Key, ship)) list.Add(kv.Key);
            return list;
        }

        public static Dictionary<T, int> Groups<T>(List<(T, T)> links)
        {
            var parent = new Dictionary<T, T>();
            T Find(T x)
            {
                while (!EqualityComparer<T>.Default.Equals(parent[x], x)) x = parent[x] = parent[parent[x]];
                return x;
            }
            foreach ((T a, T b) in links)
            {
                if (!parent.ContainsKey(a)) parent[a] = a;
                if (!parent.ContainsKey(b)) parent[b] = b;
                T ra = Find(a), rb = Find(b);
                if (!EqualityComparer<T>.Default.Equals(ra, rb)) parent[rb] = ra;
            }
            var ids = new Dictionary<T, int>();
            var groups = new Dictionary<T, int>();
            foreach ((T a, T b) in links)
                foreach (T x in new[] { a, b })
                {
                    T r = Find(x);
                    if (!ids.TryGetValue(r, out int id)) ids[r] = id = ids.Count;
                    groups[x] = id;
                }
            return groups;
        }

        public static T NextHop<T>(List<(T, T)> links, T from, T to)
        {
            var cmp = EqualityComparer<T>.Default;
            if (cmp.Equals(from, to)) return default;
            var firstHop = new Dictionary<T, T>();
            var queue = new Queue<T>();
            var seen = new HashSet<T> { from };
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                T cur = queue.Dequeue();
                foreach ((T a, T b) in links)
                {
                    T next = cmp.Equals(a, cur) ? b : cmp.Equals(b, cur) ? a : default;
                    if (!cmp.Equals(a, cur) && !cmp.Equals(b, cur)) continue;
                    if (!seen.Add(next)) continue;
                    T hop = cmp.Equals(cur, from) ? next : firstHop[cur];
                    if (cmp.Equals(next, to)) return hop;
                    firstHop[next] = hop;
                    queue.Enqueue(next);
                }
            }
            return default;
        }

        public static Dictionary<(T, T), T> NextHopTable<T>(List<(T, T)> links)
        {
            var cmp = EqualityComparer<T>.Default;
            var table = new Dictionary<(T, T), T>();
            var sources = new List<T>();
            var known = new HashSet<T>();
            foreach ((T a, T b) in links)
            {
                if (known.Add(a)) sources.Add(a);
                if (known.Add(b)) sources.Add(b);
            }
            foreach (T from in sources)
            {
                var firstHop = new Dictionary<T, T>();
                var queue = new Queue<T>();
                var seen = new HashSet<T> { from };
                queue.Enqueue(from);
                while (queue.Count > 0)
                {
                    T cur = queue.Dequeue();
                    foreach ((T a, T b) in links)
                    {
                        if (!cmp.Equals(a, cur) && !cmp.Equals(b, cur)) continue;
                        T next = cmp.Equals(a, cur) ? b : a;
                        if (!seen.Add(next)) continue;
                        T hop = cmp.Equals(cur, from) ? next : firstHop[cur];
                        firstHop[next] = hop;
                        table[(from, next)] = hop;
                        queue.Enqueue(next);
                    }
                }
            }
            return table;
        }

        private static Dictionary<(Ship, Ship), Ship> s_nextHop = new Dictionary<(Ship, Ship), Ship>();

        internal static bool SameGroup(Ship a, Ship b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            Dictionary<Ship, int> g = s_group;
            return g.TryGetValue(a, out int ga) && g.TryGetValue(b, out int gb) && ga == gb;
        }

        internal static bool HasGroup(Ship ship) => ship != null && s_group.ContainsKey(ship);

        internal static List<Ship> GroupOf(Ship ship)
        {
            var list = new List<Ship>();
            Dictionary<Ship, int> g = s_group;
            if (ship == null || !g.TryGetValue(ship, out int id)) return list;
            foreach (KeyValuePair<Ship, int> kv in g)
                if (kv.Value == id && !ReferenceEquals(kv.Key, ship)) list.Add(kv.Key);
            return list;
        }

        internal static Ship NextShip(Ship from, Ship to) =>
            from == null || to == null || !SameGroup(from, to) ? null : s_nextHop.TryGetValue((from, to), out Ship hop) ? hop : null;
        private static int s_crossings;

        public static bool Crosses(bool linked, bool partnerOperational, bool destinationOnPartner, bool destinationIsSpacePoint) =>
            linked && partnerOperational && destinationOnPartner && !destinationIsSpacePoint;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Part, ZtxCorridorLinkGateComponent> s_byPart =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Part, ZtxCorridorLinkGateComponent>();

        internal static void Register(ZtxCorridorLinkGateComponent g)
        {
            lock (s_lock) s_gates.Add(g);
            if (g.Part != null) s_byPart.AddOrUpdate(g.Part, g);
        }

        internal static void Deregister(ZtxCorridorLinkGateComponent g)
        {
            lock (s_lock) s_gates.Remove(g);
            if (g.Part != null && s_byPart.TryGetValue(g.Part, out ZtxCorridorLinkGateComponent cur) && ReferenceEquals(cur, g)) s_byPart.Remove(g.Part);
        }

        internal static bool IsCorridorHalf(Part part) => part != null && s_byPart.TryGetValue(part, out _);

        public static bool IsCrossingCell(bool baseSide, int rodsOut, bool isBase, bool outermostRod) =>
            !baseSide || (isBase ? rodsOut == 0 : outermostRod);

        public static int EvacuationStep(int max, Func<int, bool> walkableAt)
        {
            for (int k = 1; k <= max; k++)
                if (walkableAt(k)) return k;
            return 0;
        }

        private static List<Part> RodsOut(ZtxHingeBaseComponent b) =>
            b is ZtxPistonBaseComponent p ? p.Rods.Where(r => r?.Ship != null && ReferenceEquals(r.Ship, b.Part?.Ship)).ToList() : new List<Part>();

        private static Part OuterOf(ZtxHingeBaseComponent b)
        {
            List<Part> rods = RodsOut(b);
            if (rods.Count == 0 || b.Part == null) return b.Part;
            IntVector2 at = b.Part.Location;
            return rods.OrderByDescending(r => Math.Abs(r.Location.X - at.X) + Math.Abs(r.Location.Y - at.Y))
                       .ThenBy(r => r.Location.Y).ThenBy(r => r.Location.X).First();
        }

        internal static Part PartnerOf(Part part)
        {
            if (part?.Ship == null) return null;
            if (HatchLinks.IsHatch(part)) return HatchLinks.PartnerOf(part);
            ZtxHingeTipComponent tip = ZtxHingeTipComponent.ForPart(part);
            if (tip != null) return tip.PairedBase?.Serviced == true ? OuterOf(tip.PairedBase) : null;
            HingeShipManager hm = HingeShipManager.TryGet(part.Ship);
            if (hm == null) return null;
            foreach (ZtxHingeBaseComponent b in hm.Bases)
                if (ReferenceEquals(b.Part, part))
                    return b.Serviced && IsCrossingCell(true, RodsOut(b).Count, true, false) ? b.PairedTip?.Part : null;
            if (!MergeRebuild.IsRodRules(part.Rules)) return null;
            foreach (ZtxHingeBaseComponent b in hm.Bases)
                if (b is ZtxPistonBaseComponent pb && pb.Rods.Contains(part))
                    return pb.Serviced && IsCrossingCell(true, RodsOut(pb).Count, false, ReferenceEquals(OuterOf(pb), part)) ? pb.PairedTip?.Part : null;
            return null;
        }

        internal static void Publish(SimRoot sim)
        {
            List<ZtxCorridorLinkGateComponent> gates;
            lock (s_lock) gates = new List<ZtxCorridorLinkGateComponent>(s_gates);
            gates.RemoveAll(g => g?.Part?.Ship == null || !ReferenceEquals(g.Part.Ship.Sim, sim));
            gates.Sort((x, y) =>
            {
                int c = AssemblyTopology.Id(x.Part.Ship).CompareTo(AssemblyTopology.Id(y.Part.Ship));
                if (c == 0) c = x.Part.Location.Y.CompareTo(y.Part.Location.Y);
                if (c == 0) c = x.Part.Location.X.CompareTo(y.Part.Location.X);
                return c;
            });
            foreach (ZtxCorridorLinkGateComponent g in gates)
            {
                try { g.Tick(); }
                catch (Exception ex) { LogThrottle.Exception("CorridorHinge.Publish(gate)", ex); }
            }
            var links = new Dictionary<Airlock, Link>();
            lock (s_lock)
            {
                foreach (ZtxCorridorLinkGateComponent g in s_gates)
                {
                    Part p = g.Part;
                    if (p?.Ship == null || !ReferenceEquals(p.Ship.Sim, sim) || !g.IsToggleOn) continue;
                    Part q = g.PartnerPart;
                    Airlock a = Airlock.GetOnPart(p), b = q == null ? null : Airlock.GetOnPart(q);
                    if (a == null || b == null || q.Ship == null || ReferenceEquals(q.Ship, p.Ship)) continue;
                    links[a] = new Link { Partner = b, PartnerShip = q.Ship, Ship = p.Ship, Anchor = AnchorOf(p) };
                    if (!IsCorridorHalf(q) && !links.ContainsKey(b))
                        links[b] = new Link { Partner = a, PartnerShip = p.Ship, Ship = q.Ship, Anchor = q };
                }
            }
            var pairs = new List<(Ship, Ship)>();
            var workPairs = new List<(Ship, Ship)>();
            foreach (KeyValuePair<Airlock, Link> kv in links)
            {
                Ship a = kv.Key.Part?.Ship, b = kv.Value.PartnerShip;
                if (a == null || b == null) continue;
                if (!pairs.Contains((a, b)) && !pairs.Contains((b, a))) pairs.Add((a, b));
                if (IsWorkLink(IsDockPart(kv.Key.Part), IsDockPart(kv.Value.Partner?.Part)) && !workPairs.Contains((a, b)) && !workPairs.Contains((b, a)))
                    workPairs.Add((a, b));
            }
            Comparison<(Ship, Ship)> byId = (x, y) =>
            {
                int c = AssemblyTopology.Id(x.Item1).CompareTo(AssemblyTopology.Id(y.Item1));
                return c != 0 ? c : AssemblyTopology.Id(x.Item2).CompareTo(AssemblyTopology.Id(y.Item2));
            };
            pairs.Sort(byId);
            workPairs.Sort(byId);
            s_links = links;
            s_pairs = pairs;
            s_group = Groups(pairs);
            s_workGroup = Groups(workPairs);
            s_nextHop = NextHopTable(pairs);
            HealOffPart(sim);
        }

        public static void ExitAirlock(ShipCrew sc, Airlock a)
        {
            try
            {
                Destination dest = sc.Soul.Destination;
                Ship target = CorridorTarget(sc, sc.Ship);
                bool space = dest.SpacePoint.HasValue;
                bool linked = s_links.TryGetValue(a, out Link link);
                if (linked && link.Partner.IsOperational && target != null)
                {
                    bool viaPartner = ReferenceEquals(target, link.PartnerShip) || ReferenceEquals(NextShip(sc.Ship, target), link.PartnerShip);
                    if (!CrossesToward(!space && viaPartner, space, space && viaPartner)) target = null;
                }
                else target = null;
                if (target != null)
                {
                    sc.Sim.EnqueueDeterministic(sc.Ship.UniqueID, delegate { Cross(sc, a, link.Partner, link.PartnerShip); });
                    return;
                }
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("CorridorHinge.ExitAirlock", ex);
            }
            if (IsCorridorAirlock(a))
            {
                sc.Soul.Destination = default(Destination);
                return;
            }
            sc.Sim.EnqueueDeterministic(sc.Ship.UniqueID, delegate
            {
                sc.ConvertToSpaceCrew();
                a.OnCrewLeave();
            });
        }

        private static void Cross(ShipCrew sc, Airlock a, Airlock y, Ship b)
        {
            Ship shipA = sc.Ship;
            bool ok = sc.Soul?.Form == sc && shipA != null && ReferenceEquals(sc.CurrentPart, a.Part) && a.IsOperational && y.IsOperational
                      && y.Part != null && ReferenceEquals(y.Part.Ship, b) && !ReferenceEquals(b, shipA) && b.ExistsInSim(shipA.Sim);
            if (!ok)
            {
                if (sc.Ship != null && sc.Soul?.Form == sc)
                {
                    sc.ConvertToSpaceCrew();
                    a.OnCrewLeave();
                }
                return;
            }
            Direction worldRot = shipA.DetTransformRotationToWorld(sc.Rotation);
            shipA.Crew.RemoveCrew(sc, unregisterWithSim: false);
            sc.SetLocation(y.LocalEnterExitPoint, lerpFromPrevious: false, queueGeometryRefresh: false, clearPath: false);
            sc.SetRotation(b.DetTransformRotationFromWorld(worldRot), lerpFromPrevious: false, queueGeometryRefresh: false);
            sc.OverrideApparentLocation = null;
            sc.OverrideApparentRotation = null;
            b.Crew.AddCrew(sc, registerWithSim: false);
            a.OnCrewLeave();
            y.OnCrewEnter();
            s_crossings++;
            LogThrottle.Info("CorridorCross|" + shipA.UniqueID + "|" + b.UniqueID,
                $"Corridor hinge: crew crossed from ship {shipA.UniqueID} at {a.Part.Location} to ship {b.UniqueID} at {y.Part.Location} ({s_crossings} crossing(s) so far).");
        }


        public static bool CrossesToward(bool destinationViaPartner, bool destinationIsSpacePoint, bool homeViaPartner) =>
            destinationIsSpacePoint ? homeViaPartner : destinationViaPartner;

        internal static Ship CorridorTarget(ShipCrew sc, Ship ship)
        {
            if (sc?.Soul == null || ship == null) return null;
            Destination dest = sc.Soul.Destination;
            Ship target = dest.SpacePoint.HasValue ? sc.Soul.HomeShip : dest.Ship;
            if (target == null || ReferenceEquals(target, ship) || !SameGroup(ship, target)) return null;
            return target;
        }

        public static bool CountsAsHome(bool sameGroup, bool onJobShip, bool canWalkToCorridor) => sameGroup && (onJobShip || canWalkToCorridor);

        internal static bool CanWalkTo(Part from, Ship target)
        {
            Ship ship = from?.Ship;
            if (ship == null || target == null) return false;
            if (ReferenceEquals(ship, target)) return true;
            Ship hop = NextShip(ship, target);
            if (hop == null) return false;
            foreach (KeyValuePair<Airlock, Link> kv in s_links)
            {
                if (!ReferenceEquals(kv.Value.Ship, ship) || !ReferenceEquals(kv.Value.PartnerShip, hop)) continue;
                Part to = WalkPart(kv.Key, kv.Value, ship);
                if (to != null && from.HasPathTo(to)) return true;
            }
            return false;
        }

        public static int WalkTarget(bool partAlive, bool anchorAlive) => partAlive ? 1 : anchorAlive ? 2 : 0;

        private static Part WalkPart(Airlock a, Link l, Ship ship)
        {
            Part p = a?.Part;
            switch (WalkTarget(p != null && ReferenceEquals(p.Ship, ship), l.Anchor != null && ReferenceEquals(l.Anchor.Ship, ship)))
            {
                case 1: return p;
                case 2: return l.Anchor;
                default: return null;
            }
        }

        private static Part AnchorOf(Part part)
        {
            if (part?.Ship == null || !MergeRebuild.IsRodRules(part.Rules)) return part;
            HingeShipManager hm = HingeShipManager.TryGet(part.Ship);
            if (hm == null) return part;
            foreach (ZtxHingeBaseComponent b in hm.Bases)
                if (b is ZtxPistonBaseComponent pb && pb.Rods.Contains(part)) return pb.Part ?? part;
            return part;
        }

        internal static bool CanWalkToAnyCorridor(Part from)
        {
            Ship ship = from?.Ship;
            if (ship == null) return false;
            foreach (KeyValuePair<Airlock, Link> kv in s_links)
            {
                if (!ReferenceEquals(kv.Value.Ship, ship)) continue;
                Part to = WalkPart(kv.Key, kv.Value, ship);
                if (to != null && from.HasPathTo(to)) return true;
            }
            return false;
        }

        public static bool CanReach(bool walk, bool corridor, bool targetHatch, bool crewHatch) => walk || corridor || (targetHatch && crewHatch);

        internal static bool HasRealHatch(Part part)
        {
            if (part?.Ship == null) return false;
            foreach (Airlock a in Airlock.GetAllOnShip(part.Ship))
                if (a?.Part != null && !IsCorridorAirlock(a) && a.IsOperational && a.Part.HasPathTo(part)) return true;
            return false;
        }

        internal static bool Reaches(Part from, Part target)
        {
            if (from?.Ship == null || target?.Ship == null) return true;
            if (from.HasPathTo(target)) return true;
            bool corridor = !ReferenceEquals(from.Ship, target.Ship) && CanWalkTo(from, target.Ship);
            return CanReach(false, corridor, !corridor && HasRealHatch(target), !corridor && HasRealHatch(from));
        }

        internal static bool HasRealAirlock(Ship ship) => ship != null && Airlock.GetAllOnShip(ship).Any(a => !IsCorridorAirlock(a));

        public static bool IsNotHatch(bool linked, bool onGatePart) => linked || onGatePart;

        public static bool IsGateAirlock(bool linked, bool hatchPart, bool corridorHalf) => corridorHalf || (linked && hatchPart);

        internal static bool IsCorridorAirlock(Airlock a) =>
            a != null && IsGateAirlock(s_links.ContainsKey(a), HatchLinks.IsHatch(a.Part), IsCorridorHalf(a.Part));

        public static Airlock EntryAirlock(Ship ship, Vector2 worldPoint, Part contiguousWithPart, IntVector2? contiguousWithCell)
        {
            Airlock vanilla = Airlock.GetNearestOnShip(ship, worldPoint, contiguousWithPart, contiguousWithCell);
            try
            {
                if (vanilla == null || !IsCorridorAirlock(vanilla)) return vanilla;
                Airlock best = null;
                float bestD = float.PositiveInfinity;
                foreach (Airlock a in Airlock.GetAllOnShip(ship))
                {
                    if (a?.Part == null || IsCorridorAirlock(a)) continue;
                    if (contiguousWithPart != null && !(contiguousWithCell.HasValue ? a.Part.HasPathTo(contiguousWithPart, contiguousWithCell.Value) : a.Part.HasPathTo(contiguousWithPart))) continue;
                    float d = a.Part.DetWorldCenter.DistanceSquaredTo(in worldPoint);
                    if (d < bestD) { best = a; bestD = d; }
                }
                return best;
            }
            catch
            {
                return vanilla;
            }
        }

        public static Airlock PickExit(Ship ship, IntVector2 cell, ShipCrew sc)
        {
            if (OffPart(ship, cell)) return null;
            Airlock vanilla = Airlock.GetNearestContiguousWith(ship, cell);
            try
            {
                Dictionary<Airlock, Link> links = s_links;
                if (links.Count == 0 || sc?.Soul == null) return vanilla;
                Part from = sc.CurrentPart;
                if (from == null) return vanilla;
                Ship target = CorridorTarget(sc, ship);
                if (target == null)
                {
                    if (vanilla == null || !IsCorridorAirlock(vanilla)) return vanilla;
                    Airlock alt = null;
                    int altD = int.MaxValue;
                    foreach (Airlock x in Airlock.GetAllOnShip(ship))
                    {
                        if (x?.Part == null || IsCorridorAirlock(x) || !x.IsOperational || !from.HasPathTo(x.Part)) continue;
                        IntVector2 xl = x.Part.Location;
                        int d = Math.Abs(xl.X - cell.X) + Math.Abs(xl.Y - cell.Y);
                        if (alt == null || d < altD || (d == altD && (xl.Y < alt.Part.Location.Y || (xl.Y == alt.Part.Location.Y && xl.X < alt.Part.Location.X))))
                        {
                            alt = x;
                            altD = d;
                        }
                    }
                    return alt;
                }
                Ship finalTarget = target;
                target = NextShip(ship, target) ?? target;
                Airlock best = null;
                int bestD = int.MaxValue;
                foreach (KeyValuePair<Airlock, Link> kv in links)
                {
                    Airlock a = kv.Key;
                    if (a.Part == null || !ReferenceEquals(a.Part.Ship, ship) || !ReferenceEquals(kv.Value.PartnerShip, target)) continue;
                    if (!a.IsOperational || !from.HasPathTo(a.Part)) continue;
                    IntVector2 at = a.Part.Location;
                    int d = Math.Abs(at.X - cell.X) + Math.Abs(at.Y - cell.Y);
                    if (best == null || d < bestD || (d == bestD && (at.Y < best.Part.Location.Y || (at.Y == best.Part.Location.Y && at.X < best.Part.Location.X))))
                    {
                        best = a;
                        bestD = d;
                    }
                }
                if (best != null) return best;
                return HasRealAirlock(finalTarget) ? (IsCorridorAirlock(vanilla) ? RealNearest(ship, cell) : vanilla) : null;
            }
            catch
            {
                return vanilla;
            }
        }

        private static int s_offPartSearches;
        private static int s_offPartReports;

        private static bool OffPart(Ship ship, IntVector2 cell)
        {
            if (ship == null || ship.Parts[cell, PartRectType.Physical] != null) return false;
            System.Threading.Interlocked.Increment(ref s_offPartSearches);
            return true;
        }

        private static void HealOffPart(SimRoot sim)
        {
            if (System.Threading.Interlocked.Exchange(ref s_offPartSearches, 0) == 0 || sim == null) return;
            int reset = 0, deleted = 0;
            string first = null;
            foreach (Ship ship in sim.Ships)
            {
                if (ship?.Crew == null) continue;
                foreach (ShipCrew c in ship.Crew.OnShip.ToList())
                {
                    if (c?.Soul == null || ship.Parts[c.CurrentCell, PartRectType.Normal] != null) continue;
                    if (first == null) first = $"ship {ship.UniqueID} cell {c.CurrentCell}";
                    try
                    {
                        if (c.Soul.ResetToSource()) reset++;
                        else { c.Soul.Delete(killed: false); deleted++; }
                    }
                    catch (Exception ex) { Log.Exception("CorridorHinge.HealOffPart", ex); }
                }
            }
            if ((reset > 0 || deleted > 0) && s_offPartReports++ < 20)
                Log.Info($"Corridor guard: {reset + deleted} crew stood on a cell with no part (first: {first}); {reset} sent to their bunk, {deleted} removed.");
        }

        public static Airlock RealNearest(Ship ship, IntVector2 cell)
        {
            if (OffPart(ship, cell)) return null;
            Airlock v = Airlock.GetNearestContiguousWith(ship, cell);
            if (!IsCorridorAirlock(v)) return v;
            Part from = ship?.Parts[cell, PartRectType.Normal];
            return from == null ? null : NearestReal(ship, from, cell);
        }

        public static Airlock RealNearestPart(Part part)
        {
            Airlock v = Airlock.GetNearestContiguousWith(part);
            if (!IsCorridorAirlock(v)) return v;
            return part?.Ship == null ? null : NearestReal(part.Ship, part, part.Location);
        }

        public static IEnumerable<Airlock> RealAll(Ship ship) => Airlock.GetAllOnShip(ship).Where(a => !IsCorridorAirlock(a)).ToList();

        private static Airlock NearestReal(Ship ship, Part from, IntVector2 cell)
        {
            Airlock best = null;
            int bestD = int.MaxValue;
            foreach (Airlock x in Airlock.GetAllOnShip(ship))
            {
                if (x?.Part == null || IsCorridorAirlock(x) || !x.IsOperational || !from.HasPathTo(x.Part)) continue;
                IntVector2 xl = x.Part.Location;
                int d = Math.Abs(xl.X - cell.X) + Math.Abs(xl.Y - cell.Y);
                if (best == null || d < bestD || (d == bestD && (xl.Y < best.Part.Location.Y || (xl.Y == best.Part.Location.Y && xl.X < best.Part.Location.X))))
                {
                    best = x;
                    bestD = d;
                }
            }
            return best;
        }
    }

    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxCorridorLinkGate")]
    internal class ZtxCorridorLinkGateRules : PartComponentRules
    {
        public override PartComponent CreateComponent() => new ZtxCorridorLinkGateComponent(this);
    }

    internal class ZtxCorridorLinkGateComponent : PartComponent, IPartComponentToggle, ILogicTicker
    {
        private bool _on;
        private Part _partner;

        public bool IsToggleOn => _on;
        public bool IsToggleOnAnticipated => _on;
        public event EventHandler<EventArgs> ToggleChanged;
        internal Part PartnerPart => _partner;

        public ZtxCorridorLinkGateComponent(ZtxCorridorLinkGateRules rules) : base(rules) { }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            try
            {
                LogicShipManager.GetOrCreate(base.Ship)?.Register(this);
                CorridorHinge.Register(this);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxCorridorLinkGateComponent.OnPartAttached", ex);
            }
        }

        public override void OnPartDetaching()
        {
            try
            {
                LogicShipManager.TryGet(base.Ship)?.Deregister(this);
                CorridorHinge.Deregister(this);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxCorridorLinkGateComponent.OnPartDetaching", ex);
            }
            base.OnPartDetaching();
        }

        public void Tick()
        {
            Part partner = CorridorHinge.PartnerOf(base.Part);
            bool on = partner?.Ship != null && !ReferenceEquals(partner.Ship, base.Ship)
                      && (CorridorHinge.IsCorridorHalf(partner) || HatchLinks.IsHatch(base.Part));
            _partner = on ? partner : null;
            if (on == _on) return;
            _on = on;
            ToggleChanged?.Invoke(this, EventArgs.Empty);
            if (Log.VERBOSE) Log.Info($"Corridor hinge at {base.Part?.Location} on ship {base.Ship?.UniqueID}: crew passage {(on ? "OPEN to ship " + partner.Ship.UniqueID : "closed")}.");
        }
    }

    [HarmonyPatch]
    internal static class CorridorExitPatch
    {
        public const int EXPECTED = 1;
        private static readonly Dictionary<MethodBase, int> s_sites = new Dictionary<MethodBase, int>();
        public static string Summary => $"CorridorHinge: {s_sites.Values.Sum()}/{EXPECTED} airlock exit(s) patched (crew walk through corridor hinges)";

        static MethodBase TargetMethod() => CrewMembers("UpdatePostMovement");

        private static bool Prepare(MethodBase original) => original != null || TargetMethod() != null;

        internal static MethodInfo CrewMembers(string name)
        {
            InterfaceMapping map = typeof(ShipCrew).GetInterfaceMap(typeof(ShipCrewManager.IShipCrewMembers));
            for (int i = 0; i < map.InterfaceMethods.Length; i++)
                if (map.InterfaceMethods[i].Name == name) return map.TargetMethods[i];
            return null;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var list = new List<CodeInstruction>(instructions);
            MethodInfo to = AccessTools.Method(typeof(CorridorHinge), nameof(CorridorHinge.ExitAirlock));
            int hits = 0;
            foreach (CodeInstruction ci in list)
                if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && ci.operand is MethodInfo mi && mi.Name.Contains("_ExitAirlock")
                    && mi.GetParameters().Length == 2 && to != null)
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = to;
                    hits++;
                }
            if (original != null) s_sites[original] = hits;
            return list;
        }
    }

    [HarmonyPatch]
    internal static class CorridorRoutePatch
    {
        public const int EXPECTED = 1;
        private static readonly Dictionary<MethodBase, int> s_sites = new Dictionary<MethodBase, int>();
        public static string Summary => $"CorridorHinge: {s_sites.Values.Sum()}/{EXPECTED} airlock choice(s) patched (crew prefer a corridor hinge toward its partner)";

        static MethodBase TargetMethod() => CorridorExitPatch.CrewMembers("UpdateDestinationAsync");

        private static bool Prepare(MethodBase original) => original != null || TargetMethod() != null;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var list = new List<CodeInstruction>(instructions);
            MethodInfo from = AccessTools.Method(typeof(Airlock), nameof(Airlock.GetNearestContiguousWith), new[] { typeof(Ship), typeof(IntVector2) });
            MethodInfo to = AccessTools.Method(typeof(CorridorHinge), nameof(CorridorHinge.PickExit));
            int hits = 0;
            for (int i = 0; i < list.Count; i++)
            {
                CodeInstruction ci = list[i];
                if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Equals(ci.operand, from) && to != null)
                {
                    list.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                    i++;
                    list[i] = new CodeInstruction(OpCodes.Call, to) { labels = ci.labels, blocks = ci.blocks };
                    hits++;
                }
            }
            if (original != null) s_sites[original] = hits;
            return list;
        }
    }

    [HarmonyPatch]
    internal static class CorridorEntryPatch
    {
        public const int EXPECTED = 1;
        private static readonly Dictionary<MethodBase, int> s_sites = new Dictionary<MethodBase, int>();
        public static string Summary => $"CorridorHinge: {s_sites.Values.Sum()}/{EXPECTED} space-entry airlock choice(s) patched (crew never enter through a corridor hinge from space)";

        private static bool Prepare(MethodBase original) => original != null || TargetMethod() != null;

        static MethodBase TargetMethod()
        {
            InterfaceMapping map = typeof(SpaceCrew).GetInterfaceMap(typeof(SimCrewManager.ISpaceCrewMembers));
            for (int i = 0; i < map.InterfaceMethods.Length; i++)
                if (map.InterfaceMethods[i].Name == "UpdateDestinationAsync") return map.TargetMethods[i];
            return null;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var list = new List<CodeInstruction>(instructions);
            MethodInfo from = AccessTools.Method(typeof(Airlock), nameof(Airlock.GetNearestOnShip));
            MethodInfo to = AccessTools.Method(typeof(CorridorHinge), nameof(CorridorHinge.EntryAirlock));
            int hits = 0;
            foreach (CodeInstruction ci in list)
                if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Equals(ci.operand, from) && to != null)
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = to;
                    hits++;
                }
            if (original != null) s_sites[original] = hits;
            return list;
        }
    }

    [HarmonyPatch(typeof(PartCrewJob), nameof(PartCrewJob.AllowNonContiguousCrewSearch), MethodType.Getter)]
    internal static class CorridorPartCrewSearchPatch
    {
        static void Postfix(PartCrewJob __instance, ref NonContiguousCrewSearchMode __result)
        {
            if (__result == NonContiguousCrewSearchMode.Disabled && CorridorHinge.HasWorkGroup(__instance.Ship))
                __result = NonContiguousCrewSearchMode.EnabledRequireAirlock;
        }
    }

    [HarmonyPatch(typeof(ResourceTransferJob), nameof(ResourceTransferJob.AllowNonContiguousCrewSearch), MethodType.Getter)]
    internal static class CorridorTransferSearchPatch
    {
        static void Postfix(ResourceTransferJob __instance, ref NonContiguousCrewSearchMode __result)
        {
            if (__result == NonContiguousCrewSearchMode.Disabled && __instance.Source != null && CorridorHinge.HasWorkGroup(__instance.Ship))
                __result = NonContiguousCrewSearchMode.EnabledRequireAirlock;
        }
    }

    [HarmonyPatch]
    internal static class CorridorNoHatchPatch
    {
        public const int EXPECTED = 8;
        private static readonly Dictionary<MethodBase, int> s_sites = new Dictionary<MethodBase, int>();
        public static string Summary => $"CorridorHinge: {s_sites.Values.Sum()}/{EXPECTED} hatch use(s) patched (crew and cargo never go through a corridor hinge to space)";

        private static bool Prepare(MethodBase original) => original != null || TargetMethods().Any();

        private static readonly string[] Types = { "Cosmoteer.Crew.CrewSoul", "Cosmoteer.Ships.Crew.Jobs.IdleJob", "Cosmoteer.Ships.Crew.Jobs.RefillO2Job",
            "Cosmoteer.Game.Gui.Resources.MoveResourcesState", "Cosmoteer.Game.Gui.Resources.ResourcesToolbox" };

        private static bool IsHatchCall(MethodInfo mi) => mi.DeclaringType == typeof(Airlock)
            && (mi.Name == nameof(Airlock.GetNearestContiguousWith) || mi.Name == nameof(Airlock.GetNearestOnShip) || mi.Name == nameof(Airlock.GetAllOnShip));

        private static IEnumerable<MethodBase> TargetMethods()
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            Assembly cos = typeof(Airlock).Assembly;
            foreach (string tn in Types)
            {
                Type t = cos.GetType(tn);
                if (t == null) continue;
                var types = new List<Type> { t };
                types.AddRange(t.GetNestedTypes(all));
                foreach (Type tt in types)
                    foreach (MethodInfo m in tt.GetMethods(all))
                    {
                        if (m.IsAbstract || m.ContainsGenericParameters) continue;
                        bool hit;
                        try { hit = PatchProcessor.GetOriginalInstructions(m).Any(ci => ci.operand is MethodInfo mi && IsHatchCall(mi)); }
                        catch { continue; }
                        if (hit) yield return m;
                    }
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var list = new List<CodeInstruction>(instructions);
            int hits = 0;
            foreach (CodeInstruction ci in list)
            {
                if ((ci.opcode != OpCodes.Call && ci.opcode != OpCodes.Callvirt) || !(ci.operand is MethodInfo mi) || !IsHatchCall(mi)) continue;
                Type[] ps = mi.GetParameters().Select(x => x.ParameterType).ToArray();
                MethodInfo to = null;
                if (mi.Name == nameof(Airlock.GetNearestContiguousWith))
                    to = ps.Length == 2 ? AccessTools.Method(typeof(CorridorHinge), nameof(CorridorHinge.RealNearest)) : AccessTools.Method(typeof(CorridorHinge), nameof(CorridorHinge.RealNearestPart));
                else if (mi.Name == nameof(Airlock.GetNearestOnShip) && ps.Length == 4)
                    to = AccessTools.Method(typeof(CorridorHinge), nameof(CorridorHinge.EntryAirlock));
                else if (mi.Name == nameof(Airlock.GetAllOnShip))
                    to = AccessTools.Method(typeof(CorridorHinge), nameof(CorridorHinge.RealAll));
                if (to == null) continue;
                ci.opcode = OpCodes.Call;
                ci.operand = to;
                hits++;
            }
            if (original != null) s_sites[original] = hits;
            return list;
        }
    }

    [HarmonyPatch(typeof(PartCrewJob), nameof(PartCrewJob.IsValidFor))]
    internal static class CorridorReachOperatorPatch
    {
        static void Postfix(PartCrewJob __instance, Cosmoteer.Crew.CrewSoul crew, ref bool __result)
        {
            if (!__result) return;
            try
            {
                Part at = crew?.ShipCrew?.CurrentPart;
                if (at == null || !CorridorHinge.HasGroup(__instance.Ship)) return;
                if (SurgeryKin.GraceApplies(SurgeryKin.Are(at.Ship, __instance.Part?.Ship), ReferenceEquals(crew.Job, __instance))) return;
                if (!CorridorHinge.Reaches(at, __instance.Part)) __result = false;
            }
            catch (Exception ex) { LogThrottle.Exception("CorridorReachOperatorPatch", ex); }
        }
    }

    [HarmonyPatch(typeof(ResourceTransferJob), nameof(ResourceTransferJob.IsValidFor))]
    internal static class CorridorReachTransferPatch
    {
        static void Postfix(ResourceTransferJob __instance, Cosmoteer.Crew.CrewSoul crew, ref bool __result)
        {
            if (!__result) return;
            try
            {
                Part at = crew?.ShipCrew?.CurrentPart;
                if (at == null || !CorridorHinge.HasGroup(__instance.Ship)) return;
                Part target = crew.CarriedResource.Quantity > 0 ? __instance.Sink?.Part : (__instance.Source?.Part ?? __instance.Sink?.Part);
                if (target != null && !SurgeryKin.GraceApplies(SurgeryKin.Are(at.Ship, target.Ship), ReferenceEquals(crew.Job, __instance)) && !CorridorHinge.Reaches(at, target)) __result = false;
            }
            catch (Exception ex) { LogThrottle.Exception("CorridorReachTransferPatch", ex); }
        }
    }
}
