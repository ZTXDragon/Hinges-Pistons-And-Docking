using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Parts.Weapons;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class AssemblyReach
    {
        private sealed class Snap
        {
            public SimRoot Sim;
            public int Tick;
            public readonly Dictionary<Ship, float> Radius = new Dictionary<Ship, float>();
            public readonly HashSet<Ship> SalvageArmed = new HashSet<Ship>();
            public readonly HashSet<Ship> NuggetArmed = new HashSet<Ship>();
            public readonly Dictionary<Ship, SegmentWeapons> Arms = new Dictionary<Ship, SegmentWeapons>();
        }

        internal sealed class SegmentWeapons
        {
            public Weapon[] Ranged;
            public Weapon[] Operational;
            public bool[] HasResources;
        }

        private static volatile Snap s_snap;

        public static float RadiusAbout(float cx, float cy, float[] xs, float[] ys, float[] rs)
        {
            float r = 0f;
            if (xs == null || ys == null || rs == null) return r;
            int n = Math.Min(xs.Length, Math.Min(ys.Length, rs.Length));
            for (int i = 0; i < n; i++)
            {
                float dx = xs[i] - cx, dy = ys[i] - cy;
                float reach = (float)Math.Sqrt(dx * dx + dy * dy) + rs[i];
                if (reach > r) r = reach;
            }
            return r;
        }

        public static void Build(SimRoot sim)
        {
            Snap cur = s_snap;
            if (cur != null && ReferenceEquals(cur.Sim, sim) && cur.Tick == sim.Tick) return;
            var snap = new Snap { Sim = sim, Tick = sim.Tick };
            IReadOnlyList<Ship[]> groups = HingeJointManager.ActivePinCount == 0 ? null : AssemblyIndex.FreshGroups(sim);
            if (groups != null)
            {
                foreach (Ship[] g in groups)
                {
                    Ship anchor = g == null || g.Length == 0 ? null : g[0];
                    Ship[] members = anchor == null ? null : AssemblyIndex.AssemblyOf(anchor);
                    if (members == null || members.Length < 2 || Array.IndexOf(members, anchor) < 0) continue;
                    var xs = new float[members.Length];
                    var ys = new float[members.Length];
                    var rs = new float[members.Length];
                    bool salvage = false, nuggets = false;
                    for (int i = 0; i < members.Length; i++)
                    {
                        Ship m = members[i];
                        if (m == null || !ReferenceEquals(m.Sim, sim)) continue;
                        Circle c = m.DetWorldBoundingCircle;
                        xs[i] = c.Center.X;
                        ys[i] = c.Center.Y;
                        rs[i] = c.Radius;
                        WeaponManager w = m.Weapons;
                        if (w == null) continue;
                        salvage |= w.HasOperationalSalvageWeapons;
                        if (!nuggets)
                            foreach (Weapon wp in w.OperationalWeapons)
                                if (wp is TurretWeapon tw && tw.Rules?.AutoTargets != null && tw.Rules.AutoTargets.Any(a => a != null && a.TargetType == WeaponTargetType.CollectNuggets))
                                {
                                    nuggets = true;
                                    break;
                                }
                    }
                    var ranged = new List<Weapon>();
                    var operational = new List<Weapon>();
                    var hasRes = new List<bool>();
                    foreach (Ship m in members)
                    {
                        if (m == null || ReferenceEquals(m, anchor) || !ReferenceEquals(m.Sim, sim) || m.Weapons == null) continue;
                        foreach (Weapon wp in m.Weapons)
                            if (wp != null && wp.ShouldEstimateFirepower) ranged.Add(wp);
                        foreach (Weapon wp in m.Weapons.OperationalWeapons)
                        {
                            if (wp == null || !wp.ShouldEstimateFirepower) continue;
                            operational.Add(wp);
                            hasRes.Add(wp.HasResources(useAnticipated: true));
                        }
                    }
                    if (ranged.Count > 0 || operational.Count > 0)
                        snap.Arms[anchor] = new SegmentWeapons { Ranged = ranged.ToArray(), Operational = operational.ToArray(), HasResources = hasRes.ToArray() };
                    Vector2 centre = anchor.DetWorldCenter;
                    float r = Math.Max(anchor.WorldBoundingRadius, RadiusAbout(centre.X, centre.Y, xs, ys, rs));
                    snap.Radius[anchor] = r;
                    if (salvage) snap.SalvageArmed.Add(anchor);
                    if (nuggets)
                        foreach (Ship m in members)
                            if (m != null) snap.NuggetArmed.Add(m);
                }
            }
            s_snap = snap;
        }

        private static Snap Current(Ship ship)
        {
            Snap s = s_snap;
            SimRoot sim = ship?.Sim;
            if (s == null || sim == null || !ReferenceEquals(s.Sim, sim) || sim.Tick - s.Tick > 1) return null;
            return s;
        }

        internal static SegmentWeapons ArmsOf(Ship anchor)
        {
            Snap s = Current(anchor);
            return s != null && s.Arms.TryGetValue(anchor, out SegmentWeapons w) ? w : null;
        }

        public static float RadiusForCommands(Ship ship)
        {
            float vanilla = ship.WorldBoundingRadius;
            Snap s = Current(ship);
            return s != null && s.Radius.TryGetValue(ship, out float r) && r > vanilla ? r : vanilla;
        }

        public static bool ShipHasSalvageWeapons(WeaponManager weapons)
        {
            if (weapons.HasOperationalSalvageWeapons) return true;
            Snap s = Current(weapons.Ship);
            return s != null && s.SalvageArmed.Contains(weapons.Ship);
        }

        public static bool ShipHasNuggetCollectors(Ship ship)
        {
            Snap s = Current(ship);
            return s != null && s.NuggetArmed.Contains(ship);
        }
    }

    [HarmonyPatch(typeof(SimRoot), "FixedUpdateForBucket")]
    internal static class AssemblyReachStagePatch
    {
        static void Prefix(SimRoot __instance)
        {
            try
            {
                AssemblyReach.Build(__instance);
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("AssemblyReach.Build", ex);
            }
        }
    }

    [HarmonyPatch]
    internal static class AssemblyReachSitesPatch
    {
        public const int EXPECTED_RADIUS = 28;
        public const int EXPECTED_SALVAGE = 1;
        private static readonly Dictionary<MethodBase, int> s_radius = new Dictionary<MethodBase, int>();
        private static readonly Dictionary<MethodBase, int> s_salvage = new Dictionary<MethodBase, int>();
        public static string Summary =>
            $"AssemblyReach: {s_radius.Values.Sum()}/{EXPECTED_RADIUS} command radius reads, {s_salvage.Values.Sum()}/{EXPECTED_SALVAGE} salvage-weapon read patched";

        private static readonly Type[] s_types = { typeof(BaseFollowCommand), typeof(FollowCommand), typeof(AttackCommand), typeof(SalvageCommand) };

        private static MethodInfo RadiusGetter => AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.WorldBoundingRadius));
        private static MethodInfo SalvageGetter => AccessTools.PropertyGetter(typeof(WeaponManager), nameof(WeaponManager.HasOperationalSalvageWeapons));

        private static int Count(MethodBase m, MethodInfo getter)
        {
            try
            {
                return PatchProcessor.GetOriginalInstructions(m).Count(ci => (ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Equals(ci.operand, getter));
            }
            catch
            {
                return 0;
            }
        }

        private static List<MethodBase> Targets()
        {
            var list = new List<MethodBase>();
            MethodInfo rg = RadiusGetter, sg = SalvageGetter;
            if (rg == null || sg == null) return list;
            const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (Type t in s_types)
            {
                var types = new List<Type> { t };
                types.AddRange(t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
                foreach (Type tt in types)
                {
                    var methods = new List<MethodBase>();
                    methods.AddRange(tt.GetMethods(ALL).Where(m => !m.IsAbstract && !m.IsGenericMethodDefinition && !m.ContainsGenericParameters));
                    methods.AddRange(tt.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
                    foreach (MethodBase m in methods)
                        if (Count(m, rg) > 0 || Count(m, sg) > 0) list.Add(m);
                }
            }
            return list;
        }

        private static bool Prepare(MethodBase original) => original != null || Targets().Count > 0;

        private static IEnumerable<MethodBase> TargetMethods() => Targets();

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var list = new List<CodeInstruction>(instructions);
            MethodInfo rg = RadiusGetter, sg = SalvageGetter;
            MethodInfo rTo = AccessTools.Method(typeof(AssemblyReach), nameof(AssemblyReach.RadiusForCommands));
            MethodInfo sTo = AccessTools.Method(typeof(AssemblyReach), nameof(AssemblyReach.ShipHasSalvageWeapons));
            MethodInfo enemies = AccessTools.Method(typeof(Ship), nameof(Ship.IsEnemiesWith), new[] { typeof(Ship) });
            MethodInfo enemiesShim = AccessTools.Method(typeof(AssemblyJobs), nameof(AssemblyJobs.IsEnemiesWith));
            int r = 0, s = 0;
            foreach (CodeInstruction ci in list)
            {
                if (ci.opcode != OpCodes.Call && ci.opcode != OpCodes.Callvirt) continue;
                if (rTo != null && Equals(ci.operand, rg))
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = rTo;
                    r++;
                }
                else if (sTo != null && Equals(ci.operand, sg))
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = sTo;
                    s++;
                }
                else if (enemies != null && enemiesShim != null && Equals(ci.operand, enemies))
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = enemiesShim;
                }
            }
            if (original != null)
            {
                s_radius[original] = r;
                s_salvage[original] = s;
            }
            return list;
        }
    }
}
