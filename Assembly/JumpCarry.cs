using System;
using System.Collections.Generic;
using System.Linq;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Special;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using HarmonyLib;
using HVec = Halfling.Geometry.Vector2;

namespace ZTX.HingesPistonsDocking
{
    internal static class JumpCarry
    {
        private sealed class Group
        {
            public Ship Root;
            public readonly List<Ship> Members = new List<Ship>();
        }

        private static List<Group> s_groups;
        private static List<Ship> s_jumpList;
        private static Dictionary<Ship, (HVec Pos, float Rot)> s_poses;

        internal static bool Active => s_groups != null;

        public static float[] CarriedPose(float rootX0, float rootY0, float rootR0, float rootX1, float rootY1, float rootR1,
            float memberX0, float memberY0, float memberR0)
        {
            float dR = rootR1 - rootR0;
            float cos = (float)Math.Cos(dR), sin = (float)Math.Sin(dR);
            float rx = memberX0 - rootX0, ry = memberY0 - rootY0;
            return new[] { rootX1 + rx * cos - ry * sin, rootY1 + rx * sin + ry * cos, memberR0 + dR };
        }

        private static List<Ship> AssemblyOf(Ship ship)
        {
            var all = new List<Ship>();
            HingeJointManager.GetAssemblyShips(ship, all);
            int owner = ship.Metadata.PlayerIndex;
            return all.Where(s => s != null && ReferenceEquals(s.Sim, ship.Sim) && s.Metadata.PlayerIndex == owner).ToList();
        }

        private static int Collect(List<Ship> list, List<Group> into)
        {
            var member = new HashSet<Ship>();
            foreach (Group g in into) { member.Add(g.Root); foreach (Ship m in g.Members) member.Add(m); }
            int appended = 0;
            int count = list.Count;
            for (int i = 0; i < count; i++)
            {
                Ship s = list[i];
                if (s == null || s.Sim == null) continue;
                Group g = into.FirstOrDefault(x => ReferenceEquals(x.Root, s) || x.Members.Contains(s));
                List<Ship> asm = AssemblyOf(s);
                if (asm.Count < 2) continue;
                if (g == null)
                {
                    g = new Group { Root = s };
                    into.Add(g);
                    member.Add(s);
                }
                foreach (Ship m in asm)
                {
                    if (ReferenceEquals(m, g.Root) || !member.Add(m)) continue;
                    g.Members.Add(m);
                    if (!list.Contains(m)) { list.Add(m); appended++; }
                }
            }
            return appended;
        }

        internal static void OnJumpToNode(GameRoot game, MapNode node, List<Ship> shipsToJump)
        {
            s_groups = null;
            s_jumpList = null;
            s_poses = null;
            if (shipsToJump == null || shipsToJump.Count == 0 || game == null || node == game.CurrentMapNode) return;
            if (!AssemblyFacade.Enabled || HingeJointManager.ActivePinCount == 0) return;
            var groups = new List<Group>();
            int appended = Collect(shipsToJump, groups);
            if (groups.Count == 0) return;
            s_groups = groups;
            s_jumpList = shipsToJump;
            foreach (Group g in groups) JumpAnim.Join(new[] { g.Root }.Concat(g.Members));
            foreach (Group g in groups)
                Log.Info($"FTL jump: ship {g.Root.UniqueID} carries {g.Members.Count} segment(s) ({string.Join(", ", g.Members.Select(m => m.UniqueID.ToString()))}); {appended} added to the jump list.");
        }

        internal static void OnRemove(Ship ship, bool dispose)
        {
            if (s_groups == null || s_poses != null || dispose || ship == null || s_jumpList == null || !s_jumpList.Contains(ship)) return;
            var groups = s_groups.Select(g => { var c = new Group { Root = g.Root }; c.Members.AddRange(g.Members); return c; }).ToList();
            int late = Collect(s_jumpList, groups);
            if (late > 0) Log.Info($"FTL jump: {late} segment(s) split off during the jump-out -- carried too.");
            var poses = new Dictionary<Ship, (HVec, float)>();
            foreach (Group g in groups)
                foreach (Ship s in new[] { g.Root }.Concat(g.Members))
                {
                    var body = s?.Physics?.Body;
                    if (body == null || !s.ExistsInSim(s.Sim)) continue;
                    poses[s] = ((HVec)body.Position, body.Rotation);
                }
            s_groups = groups;
            s_poses = poses;
        }

        internal static bool TryPlace(SimRoot sim, List<Ship> ships, MetaFtlGate? gate, Direction jumpInDir, Dictionary<Ship, HVec> centers)
        {
            List<Group> groups = s_groups;
            if (groups == null || ships == null || centers == null) return false;
            var memberOf = new Dictionary<Ship, Ship>();
            foreach (Group g in groups) foreach (Ship m in g.Members) memberOf[m] = g.Root;
            var placed = new List<Ship>();
            var circles = new List<Circle>();
            foreach (Ship s in ships)
            {
                if (memberOf.ContainsKey(s)) continue;
                Circle c = s.DetWorldBoundingCircle;
                Group g = groups.FirstOrDefault(x => ReferenceEquals(x.Root, s));
                if (g != null)
                {
                    float r = c.Radius;
                    foreach (Ship m in g.Members)
                    {
                        if (!ships.Contains(m)) continue;
                        Circle mc = m.DetWorldBoundingCircle;
                        float dx = mc.Center.X - c.Center.X, dy = mc.Center.Y - c.Center.Y;
                        r = Math.Max(r, (float)Math.Sqrt(dx * dx + dy * dy) + mc.Radius);
                    }
                    c = new Circle(c.Center, r);
                }
                placed.Add(s);
                circles.Add(c);
            }
            var at = new List<HVec>();
            sim.GetFtlJumpInLocations(circles, gate, jumpInDir, at);
            for (int i = 0; i < placed.Count; i++) centers[placed[i]] = at[i];
            foreach (Ship s in ships)
                if (memberOf.TryGetValue(s, out Ship root) && centers.TryGetValue(root, out HVec rc)) centers[s] = rc;
                else if (!centers.ContainsKey(s)) centers[s] = s.DetWorldCenter;
            return true;
        }

        internal static int ClearDeadPairings(IEnumerable<Ship> ships, SimRoot sim)
        {
            int cleared = 0;
            foreach (Ship s in ships)
            {
                if (s == null || !ReferenceEquals(s.Sim, sim)) continue;
                HingeShipManager hm = HingeShipManager.TryGet(s);
                if (hm != null)
                    foreach (ZtxHingeBaseComponent b in hm.Bases.ToArray())
                        if (b.Serviced && !HingeJointManager.HasPinWithTag(b)) { b.Returning = false; b.ClearPair(); cleared++; }
                foreach (Cosmoteer.Ships.Parts.Part p in s.Parts)
                {
                    ZtxRailCartComponent cart = ZtxRailCartComponent.ForPart(p);
                    if (cart != null && cart.PairedTerminal != null && !HingeJointManager.HasPinWithTag(cart)) { cart.ClearPair(); cleared++; }
                }
            }
            return cleared;
        }

        internal static void Restore(SimRoot sim)
        {
            List<Group> groups = s_groups;
            Dictionary<Ship, (HVec Pos, float Rot)> poses = s_poses;
            s_groups = null;
            s_jumpList = null;
            s_poses = null;
            if (groups == null || sim == null) return;
            if (poses == null)
            {
                Log.Info("FTL jump: no pose was recorded (the jump list was never removed) -- nothing to restore.");
                return;
            }
            foreach (Group g in groups)
            {
                var rootBody = g.Root?.Physics?.Body;
                if (rootBody == null || !ReferenceEquals(g.Root.Sim, sim) || !poses.TryGetValue(g.Root, out var root0)) continue;
                HVec rootNow = (HVec)rootBody.Position;
                float rootRot = rootBody.Rotation;
                int placed = 0, missing = 0;
                foreach (Ship m in g.Members)
                {
                    var body = m?.Physics?.Body;
                    if (body == null || !ReferenceEquals(m.Sim, sim) || !poses.TryGetValue(m, out var m0)) { missing++; continue; }
                    float[] at = CarriedPose(root0.Pos.X, root0.Pos.Y, root0.Rot, rootNow.X, rootNow.Y, rootRot, m0.Pos.X, m0.Pos.Y, m0.Rot);
                    body.SetTransform(new System.Numerics.Vector2(at[0], at[1]), at[2]);
                    body.LinearVelocity = System.Numerics.Vector2.Zero;
                    body.AngularVelocity = 0f;
                    sim.Ships.RefreshShipPosition(m);
                    placed++;
                }
                int cleared = ClearDeadPairings(new[] { g.Root }.Concat(g.Members), sim);
                Log.Info($"FTL jump: ship {g.Root.UniqueID} arrived with {placed} segment(s) on their pose{(missing > 0 ? $", {missing} missing (not carried or gone)" : "")}; {cleared} dead pairing(s) cleared for the relink.");
            }
        }
    }

    [HarmonyPatch(typeof(GameRoot), nameof(GameRoot.JumpToNode))]
    internal static class JumpCarryListPatch
    {
        static void Prefix(GameRoot __instance, MapNode node, List<Ship> shipsToJump)
        {
            try { JumpCarry.OnJumpToNode(__instance, node, shipsToJump); }
            catch (Exception ex) { Log.Exception("JumpCarryListPatch (segments may stay behind)", ex); }
        }
    }

    [HarmonyPatch(typeof(SimShipsManager), nameof(SimShipsManager.Remove))]
    internal static class JumpCarryRemovePatch
    {
        static void Prefix(Ship ship, bool dispose)
        {
            if (!JumpCarry.Active) return;
            try { JumpCarry.OnRemove(ship, dispose); }
            catch (Exception ex) { Log.Exception("JumpCarryRemovePatch (poses not recorded)", ex); }
        }
    }

    [HarmonyPatch(typeof(SimRoot), nameof(SimRoot.GetFtlJumpInLocations), new Type[] { typeof(List<Ship>), typeof(MetaFtlGate?), typeof(Direction), typeof(Dictionary<Ship, HVec>) })]
    internal static class JumpCarryPlacePatch
    {
        static bool Prefix(SimRoot __instance, List<Ship> ships, MetaFtlGate? gate, Direction jumpInDir, Dictionary<Ship, HVec> shipCenters)
        {
            if (!JumpCarry.Active) return true;
            try { return !JumpCarry.TryPlace(__instance, ships, gate, jumpInDir, shipCenters); }
            catch (Exception ex)
            {
                Log.Exception("JumpCarryPlacePatch (vanilla placement)", ex);
                shipCenters?.Clear();
                return true;
            }
        }
    }
}
