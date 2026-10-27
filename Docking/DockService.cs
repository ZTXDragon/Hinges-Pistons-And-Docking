using System;
using System.Collections.Generic;
using System.Linq;
using Cosmoteer.Crew;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Crew;
using Cosmoteer.Ships.Parts.UI;
using Cosmoteer.Ships.Resources;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class HatchLinks
    {
        public const int CREW_OFF = 0, CREW_BOARD = 1, CREW_RETURN = 2;

        public static readonly ID<PartToggleGuiRules> BayCrewId = new ID<PartToggleGuiRules>("ztx_bay_crew");
        public static readonly ID<PartToggleGuiRules> ClampCrewId = new ID<PartToggleGuiRules>("ztx_dock_crew");

        public static bool IsHatch(Part p) =>
            p != null && (ZtxBayStationComponent.ForPart(p) != null || ZtxDockClampComponent.ForPart(p) != null);

        public static int CrewMode(Part holder, ID<PartToggleGuiRules> id)
        {
            PartUIToggle t = holder != null ? PartUIToggle.GetOnPart(holder, id) : null;
            return t != null ? t.Mode : BayValues.Get(holder, id, CREW_OFF);
        }

        public static Part PartnerOf(Part p)
        {
            ZtxBayStationComponent s = ZtxBayStationComponent.ForPart(p);
            if (s != null)
            {
                ZtxBayTerminalComponent t = BayManager.TerminalOfStation(s);
                Ship held = t?.Bay.Held;
                if (held?.Sim == null || !HingeJointManager.HasPinWithTag(t)) return null;
                if (CrewMode(t.Part, BayCrewId) == CREW_OFF) return null;
                return NearestHatchPart(held, p);
            }
            ZtxDockClampComponent c = ZtxDockClampComponent.ForPart(p);
            if (c != null)
            {
                ZtxDockClampComponent other = c.PairedClamp;
                Part op = other?.Part;
                if (op?.Ship == null || ReferenceEquals(op.Ship, p.Ship)) return null;
                if (!HingeJointManager.HasPinWithTag(c) && !HingeJointManager.HasPinWithTag(other)) return null;
                if (CrewMode(p, ClampCrewId) == CREW_OFF && CrewMode(op, ClampCrewId) == CREW_OFF) return null;
                return op;
            }
            return null;
        }

        public static Part NearestHatchPart(Ship ship, Part from)
        {
            if (ship == null || from == null) return null;
            Vector2 at = from.WorldPhysicalCenter;
            Part best = null;
            float bestD = float.MaxValue;
            foreach (Airlock a in Airlock.GetAllOnShip(ship))
            {
                Part ap = a?.Part;
                if (ap == null || CorridorHinge.IsCorridorHalf(ap)) continue;
                float d = (ap.WorldPhysicalCenter - at).LengthSquared;
                if (best == null || d < bestD || (d == bestD && (ap.Location.Y < best.Location.Y || (ap.Location.Y == best.Location.Y && ap.Location.X < best.Location.X))))
                {
                    best = ap;
                    bestD = d;
                }
            }
            return best;
        }
    }

    internal static class DockTransfer
    {
        public const int NONE = 0, GIVE = 1, RECEIVE = 2;

        public static bool Flows(int mine, int theirs) => mine == GIVE && theirs == RECEIVE;
    }

    internal static class BayDevotion
    {
        public static int TakeAll(Ship ship, Func<Vector2, bool> inReach)
        {
            Cosmoteer.Ships.Construction.ConstructionManager cm = ship?.OptionalConstructionManager;
            if (cm == null || ship.Physics?.Body == null) return 0;
            int taken = 0;
            foreach (Cosmoteer.Ships.Construction.QueuedPartConstruction q in cm.QueuedPartConstructions.ToList())
                if (inReach(ship.Physics.Body.GetWorldPoint(q.Part.LocalCenter * ship.Scale)) && cm.TryRemovePartConstruct(q.Part)) taken++;
            foreach (Cosmoteer.Ships.Construction.QueuedDoorConstruction q in cm.QueuedDoorConstructions.ToList())
                if (q.Door != null && inReach(ship.Physics.Body.GetWorldPoint(q.Door.Location.Center * ship.Scale)) && cm.TryRemoveDoorConstruct(q.Door)) taken++;
            foreach (Cosmoteer.Ships.Construction.QueuedPartRepair q in cm.QueuedPartRepairs.ToList())
            {
                if (q.Part == null || !inReach(q.Part.WorldPhysicalCenter)) continue;
                try { cm.CancelRepairPart(q.Part); taken++; }
                catch (Exception ex) { LogThrottle.Exception("BayDevotion.TakeAll(repair)", ex); }
            }
            return taken;
        }
    }

    internal static class DockCrew
    {
        public static List<CrewSoul> AssignedTo(Ship ship, IEnumerable<Part> hatches)
        {
            var souls = new List<CrewSoul>();
            var seen = new HashSet<CrewSource>();
            foreach (Part h in hatches)
            {
                if (h == null || !CrewSource.s_targetedParts.TryGetValue(h, out List<CrewSource> sources)) continue;
                foreach (CrewSource cs in sources.OrderBy(x => x.Part.Location.Y).ThenBy(x => x.Part.Location.X))
                {
                    if (cs?.Part?.Ship == null || !ReferenceEquals(cs.Part.Ship, ship) || !seen.Add(cs)) continue;
                    foreach (CrewSoul soul in cs.AssignedCrew)
                        if (soul != null && ReferenceEquals(soul.HomeShip, ship) && !souls.Contains(soul)) souls.Add(soul);
                }
            }
            return souls;
        }

        public static List<CrewSoul> HomedOn(Ship ship)
        {
            var souls = new List<CrewSoul>();
            if (ship?.Crew == null) return souls;
            foreach (CrewSoul soul in ship.Crew.Assigned) if (soul != null) souls.Add(soul);
            return souls;
        }

        public static List<CrewSource> FreeQuarters(Ship ship, Vector2? nearWorld)
        {
            var list = new List<CrewSource>();
            if (ship == null) return list;
            foreach (CrewSource cs in CrewSource.GetAllOnShip(ship))
                if (cs?.Part != null && !cs.Part.IsUnderConstruction && cs.RemainingCapacity > 0) list.Add(cs);
            if (nearWorld.HasValue)
            {
                Vector2 at = nearWorld.Value;
                return list.OrderBy(cs => (cs.Part.WorldPhysicalCenter - at).LengthSquared).ThenBy(cs => cs.Part.Location.Y).ThenBy(cs => cs.Part.Location.X).ToList();
            }
            return list.OrderBy(cs => cs.Part.Location.Y).ThenBy(cs => cs.Part.Location.X).ToList();
        }

        public static int Move(List<CrewSoul> souls, Ship from, Ship to, Vector2? nearWorld, out int left)
        {
            left = 0;
            if (souls == null || from?.Crew == null || to?.Crew == null || ReferenceEquals(from, to)) return 0;
            List<CrewSource> free = FreeQuarters(to, nearWorld);
            int moved = 0;
            foreach (CrewSoul soul in souls)
            {
                CrewSource q = free.FirstOrDefault(x => x.RemainingCapacity > 0);
                if (q == null) { left++; continue; }
                try
                {
                    from.Crew.UnassignCrew(soul, killed: false, removeFromJob: true);
                    to.Crew.AssignCrew(soul);
                    ((CrewSource.ICrewSoulMembers)soul).CrewSourceLoc = q.Part.Location;
                    if (soul.CrewSource == null && q.RemainingCapacity > 0) q.AssignCrew(soul);
                    moved++;
                }
                catch (Exception ex)
                {
                    LogThrottle.Exception("DockCrew.Move", ex);
                }
            }
            return moved;
        }

        public static bool CrewedState(int freeBunks, int homed, int aboard) => freeBunks == 0 && aboard >= homed;

        public static int FreeBunks(Ship ship)
        {
            int free = 0;
            if (ship == null) return 0;
            foreach (CrewSource cs in CrewSource.GetAllOnShip(ship))
                if (cs?.Part != null && !cs.Part.IsUnderConstruction && cs.RemainingCapacity > 0) free += cs.RemainingCapacity;
            return free;
        }

        public static int Homed(Ship ship) => ship?.Crew == null ? 0 : ship.Crew.Assigned.Count;

        public static int Aboard(Ship ship)
        {
            int n = 0;
            if (ship?.Crew == null) return 0;
            foreach (CrewSoul soul in ship.Crew.Assigned)
                if (soul != null && ReferenceEquals(soul.CurrentShip, ship)) n++;
            return n;
        }

        public static List<CrewSoul> HomedButOn(Ship home, Ship standingOn)
        {
            var souls = new List<CrewSoul>();
            if (home?.Crew == null) return souls;
            foreach (CrewSoul soul in home.Crew.Assigned)
                if (soul != null && ReferenceEquals(soul.CurrentShip, standingOn)) souls.Add(soul);
            return souls;
        }

        public static bool Crewed(Ship ship) => ship != null && CrewedState(FreeBunks(ship), Homed(ship), Aboard(ship));
    }

    internal static class DockSupply
    {
        public const int MATERIALS = 1, ENERGY = 2, ALL = 3;
        public static readonly ID<ResourceRules> BatteryId = new ID<ResourceRules>("battery");

        public static bool SetIncludes(int set, bool battery) => battery ? (set & ENERGY) != 0 : (set & MATERIALS) != 0;

        public static bool IsBatterySink(IResourceSink sink) => sink != null && sink.ResourceType == BatteryId;

        public static bool Wants(int capacity, int requested, int threshold) => capacity - requested >= Math.Max(1, threshold);

        public static int Plan(int want, int budget, int stock) => Math.Max(0, Math.Min(want, Math.Min(budget, stock)));

        private static List<IResourceSink> SinksOf(Ship ship, int set)
        {
            if (ship?.Resources == null) return new List<IResourceSink>();
            return ship.Resources.Sinks
                .Where(k => k?.Part != null && !k.IsConstructionSink && k.ResourceType != ResourceRules.Stackable && !k.Part.IsUnderConstruction
                            && SetIncludes(set, IsBatterySink(k)))
                .OrderBy(k => k.Part.Location.Y).ThenBy(k => k.Part.Location.X).ThenBy(k => k.ResourceType.ToString(), StringComparer.Ordinal)
                .ToList();
        }

        private static int Want(Ship ship, IResourceSink sink)
        {
            int capacity = sink.GetRemainingCapacity(sink.ResourceType, false);
            int requested = ship.Resources.GetRequestedDelivery(sink, MPValueType.Confirmed);
            return Wants(capacity, requested, IsBatterySink(sink) ? 1 : sink.ResupplyThreshold) ? capacity - requested : 0;
        }

        public static bool Supplied(Ship ship, out string missing, int set = ALL)
        {
            missing = null;
            if (ship?.Resources == null) return false;
            foreach (IResourceSink sink in SinksOf(ship, set))
                if (Want(ship, sink) > 0)
                {
                    missing = sink.ResourceType.ToString();
                    return false;
                }
            return true;
        }

        public static IResourceSink NextSink(Ship target, ResourceStock store, Vector2 nearWorld, float reachCells, float scale, out string missing, int set = ALL)
        {
            missing = null;
            IResourceSink best = null;
            float bestD = float.MaxValue;
            foreach (IResourceSink sink in SinksOf(target, set))
            {
                if (Want(target, sink) <= 0) continue;
                if (store.Total(sink.ResourceType) <= 0)
                {
                    missing = missing ?? sink.ResourceType.ToString();
                    continue;
                }
                float d = (sink.Part.WorldPhysicalCenter - nearWorld).Length / scale;
                if (reachCells > 0f && d > reachCells) continue;
                if (d < bestD) { bestD = d; best = sink; }
            }
            return best;
        }

        public static int Deliver(ResourceStock store, Ship target, IResourceSink sink, ref int budget)
        {
            if (sink?.Part == null || budget <= 0) return 0;
            ID<ResourceRules> type = sink.ResourceType;
            int take = Plan(Want(target, sink), budget, store.Total(type));
            if (take <= 0) return 0;
            int got = sink.ReceiveResources(type, take, isCrewDelivery: false, sticky: false, playSound: true);
            if (got <= 0) return 0;
            int removed = store.Remove(type, got);
            budget -= got;
            return removed;
        }
    }
}
