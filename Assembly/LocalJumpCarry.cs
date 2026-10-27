using System;
using System.Collections.Generic;
using System.Linq;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using Halfling.Random;
using HarmonyLib;
using HVec = Halfling.Geometry.Vector2;

namespace ZTX.HingesPistonsDocking
{
    internal static class LocalJumpCarry
    {
        private sealed class Trip
        {
            public SimRoot Sim;
            public readonly List<Ship> Members = new List<Ship>();
            public readonly Dictionary<Ship, (HVec Pos, float Rot)> Poses = new Dictionary<Ship, (HVec, float)>();
            public float Radius;
            public HVec From;
        }

        private static readonly Dictionary<Ship, Trip> s_trips = new Dictionary<Ship, Trip>();
        private static bool s_inner;

        internal static bool Active => s_trips.Count > 0;

        internal static List<Ship> ForeignPartners(Ship ship)
        {
            var all = new List<Ship>();
            HingeJointManager.GetAssemblyShips(ship, all);
            int owner = ship.Metadata.PlayerIndex;
            return all.Where(s => s != null && !ReferenceEquals(s, ship) && s.Metadata.PlayerIndex != owner).ToList();
        }

        internal static List<Ship> SegmentsFor(Ship ship) => ship?.Sim == null || HingeJointManager.ActivePinCount == 0 ? new List<Ship>() : SegmentsOf(ship);

        private static List<Ship> SegmentsOf(Ship ship)
        {
            var all = new List<Ship>();
            HingeJointManager.GetAssemblyShips(ship, all);
            int owner = ship.Metadata.PlayerIndex;
            return all.Where(s => s != null && !ReferenceEquals(s, ship) && ReferenceEquals(s.Sim, ship.Sim) && s.Metadata.PlayerIndex == owner).ToList();
        }

        internal static void OnRemoving(Ship ship)
        {
            if (s_inner || ship?.Sim == null || s_trips.ContainsKey(ship) || HingeJointManager.ActivePinCount == 0) return;
            List<Ship> segs = SegmentsOf(ship);
            if (segs.Count == 0) return;
            var trip = new Trip { Sim = ship.Sim };
            HVec c0 = ship.DetWorldCenter;
            float r = ship.DetWorldBoundingCircle.Radius;
            foreach (Ship s in new[] { ship }.Concat(segs))
            {
                var body = s.Physics?.Body;
                if (body == null) continue;
                trip.Poses[s] = ((HVec)body.Position, body.Rotation);
                if (ReferenceEquals(s, ship)) continue;
                trip.Members.Add(s);
                Circle mc = s.DetWorldBoundingCircle;
                float dx = mc.Center.X - c0.X, dy = mc.Center.Y - c0.Y;
                r = Math.Max(r, (float)Math.Sqrt(dx * dx + dy * dy) + mc.Radius);
            }
            if (!trip.Poses.ContainsKey(ship) || trip.Members.Count == 0) return;
            trip.Radius = r;
            trip.From = c0;
            s_trips[ship] = trip;
        }

        internal static void OnRemoved(Ship ship)
        {
            if (s_inner || ship == null || !s_trips.TryGetValue(ship, out Trip trip)) return;
            s_inner = true;
            try
            {
                foreach (Ship m in trip.Members)
                    if (m.ExistsInSim(trip.Sim)) trip.Sim.Ships.Remove(m, dispose: false, handleLostShip: false, doingLocalJump: true);
            }
            finally { s_inner = false; }
            Log.Info($"FTL local jump: ship {ship.UniqueID} takes {trip.Members.Count} segment(s) with it ({string.Join(", ", trip.Members.Select(m => m.UniqueID.ToString()))}); room kept for a radius of {trip.Radius:F1}.");
        }

        internal static bool TryRadius(Ship ship, out float radius)
        {
            radius = 0f;
            if (ship == null || !s_trips.TryGetValue(ship, out Trip trip)) return false;
            radius = trip.Radius;
            return true;
        }

        internal static void OnAdded(Ship ship)
        {
            if (s_inner || ship == null || !s_trips.TryGetValue(ship, out Trip trip)) return;
            s_trips.Remove(ship);
            SimRoot sim = ship.Sim;
            var rootBody = ship.Physics?.Body;
            if (sim == null || rootBody == null || !trip.Poses.TryGetValue(ship, out var root0)) return;
            HVec rootNow = (HVec)rootBody.Position;
            float rootRot = rootBody.Rotation;
            int placed = 0;
            s_inner = true;
            try
            {
                foreach (Ship m in trip.Members)
                {
                    if (m.ExistsInSim(sim) || !trip.Poses.TryGetValue(m, out var m0)) continue;
                    sim.Ships.Add(m, giveDefaultCommand: false, reseedShipRand: false, didLocalJump: true);
                    var body = m.Physics?.Body;
                    if (body == null) continue;
                    float[] at = JumpCarry.CarriedPose(root0.Pos.X, root0.Pos.Y, root0.Rot, rootNow.X, rootNow.Y, rootRot, m0.Pos.X, m0.Pos.Y, m0.Rot);
                    body.SetTransform(new System.Numerics.Vector2(at[0], at[1]), at[2]);
                    body.LinearVelocity = rootBody.LinearVelocity;
                    body.AngularVelocity = 0f;
                    sim.Ships.RefreshShipPosition(m);
                    placed++;
                }
            }
            finally { s_inner = false; }
            var ships = new[] { ship }.Concat(trip.Members).ToList();
            int cleared = JumpCarry.ClearDeadPairings(ships, sim);
            int tried = 0, paired = 0, carts = 0;
            foreach (Ship s in ships)
            {
                HingeShipManager hm = HingeShipManager.TryGet(s);
                if (hm != null) { paired += hm.RelinkAtLoad(out int t); tried += t; }
            }
            foreach (Ship s in ships)
            {
                RailShipManager rm = RailShipManager.TryGet(s);
                if (rm != null) carts += rm.RelinkCartsAtLoad();
            }
            try
            {
                HVec to = ship.DetWorldCenter;
                JumpAnim.AnimateSegmentsIn(trip.From.DirectionTo(in to), trip.Members.Where(m => ReferenceEquals(m.Sim, sim)));
            }
            catch (Exception ex) { LogThrottle.Exception("LocalJumpCarry jump-in animation", ex); }
            Log.Info($"FTL local jump: ship {ship.UniqueID} arrived with {placed} of {trip.Members.Count} segment(s) on their pose; {cleared} dead pairing(s) cleared, {paired} of {tried} joint(s) and {carts} cart(s) re-formed at once.");
        }
    }

    [HarmonyPatch(typeof(SimShipsManager), nameof(SimShipsManager.Remove))]
    internal static class LocalJumpCarryRemovePatch
    {
        static void Prefix(Ship ship, bool dispose, bool doingLocalJump)
        {
            if (!doingLocalJump || dispose) return;
            try { LocalJumpCarry.OnRemoving(ship); }
            catch (Exception ex) { Log.Exception("LocalJumpCarryRemovePatch.Prefix (segments stay)", ex); }
        }

        static void Postfix(Ship ship, bool dispose, bool doingLocalJump)
        {
            if (!doingLocalJump || dispose || !LocalJumpCarry.Active) return;
            try { LocalJumpCarry.OnRemoved(ship); }
            catch (Exception ex) { Log.Exception("LocalJumpCarryRemovePatch.Postfix (segments stay)", ex); }
        }
    }

    [HarmonyPatch(typeof(SimShipsManager), nameof(SimShipsManager.Add))]
    internal static class LocalJumpCarryAddPatch
    {
        static void Postfix(Ship ship, bool didLocalJump)
        {
            if (!didLocalJump || !LocalJumpCarry.Active) return;
            try { LocalJumpCarry.OnAdded(ship); }
            catch (Exception ex) { Log.Exception("LocalJumpCarryAddPatch (segments may be left in limbo)", ex); }
        }
    }

    [HarmonyPatch(typeof(FtlLocalJumpCommand), "GetJumpInLoc")]
    internal static class LocalJumpCarryRoomPatch
    {
        static bool Prefix(FtlLocalJumpCommand __instance, Circle destCircle, HVec jumpFrom, SimRoot sim, Rand rand, ref HVec __result)
        {
            try
            {
                if (!LocalJumpCarry.TryRadius(__instance.Ship, out float radius)) return true;
                destCircle.Radius += __instance.Rules.DestinationDistance;
                HVec desired = destCircle.ClampPoint(in jumpFrom);
                __result = Cosmoteer.Generators.Simulation.ShipSpawner.GetSpawnLocation(sim, desired, radius, rand);
                return false;
            }
            catch (Exception ex)
            {
                Log.Exception("LocalJumpCarryRoomPatch (vanilla room)", ex);
                return true;
            }
        }
    }
}
