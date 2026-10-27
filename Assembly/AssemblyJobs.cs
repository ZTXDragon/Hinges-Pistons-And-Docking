using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Parts.Weapons;
using Cosmoteer.Ships.Resources;
using Cosmoteer.Simulation;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class AssemblyJobs
    {
        public static List<T> Merge<T>(IEnumerable<T> own, IEnumerable<T> anchors, Func<T, bool> excluded)
        {
            var list = new List<T>();
            var seen = new HashSet<T>();
            if (own != null)
                foreach (T j in own)
                    if (!excluded(j) && seen.Add(j)) list.Add(j);
            if (anchors != null)
                foreach (T j in anchors)
                    if (!excluded(j) && seen.Add(j)) list.Add(j);
            return list;
        }

        public static Ship[] SegmentsOf(Ship ship)
        {
            if (ship == null || HingeJointManager.ActivePinCount == 0) return null;
            Ship[] m = AssemblyIndex.FreshAssemblyOf(ship);
            return m == null || m.Length < 2 ? null : m;
        }

        private static IEnumerable<T> OthersJobs<T>(Ship ship, Ship[] members, Func<Ship, IEnumerable<T>> jobsOf)
        {
            foreach (Ship m in members)
            {
                if (m == null || ReferenceEquals(m, ship) || !ReferenceEquals(m.Sim, ship.Sim)) continue;
                IEnumerable<T> jobs = jobsOf(m);
                if (jobs == null) continue;
                foreach (T j in jobs) yield return j;
            }
        }

        public static IEnumerable<SalvageJob> SalvageJobs(ResourceManager resources, MPValueType valueType, bool onlyUnfinished)
        {
            try
            {
                Ship ship = resources?.Ship;
                Ship[] members = SegmentsOf(ship);
                if (members == null) return resources.GetSalvageJobs(valueType, onlyUnfinished);
                return Merge(resources.GetSalvageJobs(valueType, onlyUnfinished), OthersJobs(ship, members, m => m.Resources.GetSalvageJobs(valueType, onlyUnfinished)),
                    j => j?.SalvagePart == null || Array.IndexOf(members, j.SalvagePart.Ship) >= 0);
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("AssemblyJobs.SalvageJobs", ex);
                return resources.GetSalvageJobs(valueType, onlyUnfinished);
            }
        }

        public static IReadOnlyList<ResourceTransferJob> TransferJobs(ResourceManager resources)
        {
            IReadOnlyList<ResourceTransferJob> own = resources.ManualTransferJobs;
            try
            {
                Ship ship = resources.Ship;
                Ship[] members = SegmentsOf(ship);
                if (members == null) return own;
                bool any = false;
                foreach (Ship m in members)
                    if (m != null && !ReferenceEquals(m, ship) && m.Resources.ManualTransferJobs.Count > 0) { any = true; break; }
                if (!any) return own;
                return Merge(own, OthersJobs(ship, members, m => m.Resources.ManualTransferJobs), j => j == null);
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("AssemblyJobs.TransferJobs", ex);
                return own;
            }
        }

        public static bool TargetAttackableBy(ITarget target, Ship ship, bool allowSelfTarget)
        {
            bool vanilla = target.CanBeAttackedBy(ship, allowSelfTarget);
            if (vanilla || !(target is CollectNuggetTarget nt)) return vanilla;
            try
            {
                Ship sinkShip = nt.Sink?.Ship;
                if (sinkShip == null || ship == null || ReferenceEquals(sinkShip, ship) || HingeJointManager.ActivePinCount == 0) return false;
                Ship[] members = AssemblyIndex.FreshAssemblyOf(ship);
                if (members == null || Array.IndexOf(members, sinkShip) < 0) return false;
                IResourceSink sink = nt.Sink;
                return ResourceTransferJob.GetTransferJobsFor(nt.Nugget, MPValueType.Confirmed).Any(rtj => rtj.Sink == sink);
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("AssemblyJobs.TargetAttackableBy", ex);
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool IsEnemiesWith(Ship ship, Ship other) => ship.IsEnemiesWith(other);
    }

    [HarmonyPatch]
    internal static class AssemblyJobsSitesPatch
    {
        public const int EXPECTED = 4;
        private static readonly Dictionary<MethodBase, int> s_sites = new Dictionary<MethodBase, int>();
        public static string Summary => $"AssemblyJobs: {s_sites.Values.Sum()}/{EXPECTED} salvage / nugget beam reads patched";

        private static List<MethodBase> Targets()
        {
            var list = new List<MethodBase>();
            foreach (string n in new[] { "TryAcquireSalvageTarget", "TryAcquireCollectNuggetsTarget", "IsTargetValid" })
            {
                MethodBase m = AccessTools.Method(typeof(TurretWeapon), n);
                if (m != null) list.Add(m);
                else Log.Error("AssemblyJobs: TurretWeapon." + n + " not found -- left vanilla.");
            }
            return list;
        }

        private static bool Prepare(MethodBase original) => original != null || Targets().Count > 0;

        private static IEnumerable<MethodBase> TargetMethods() => Targets();

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var list = new List<CodeInstruction>(instructions);
            MethodInfo from, to;
            int expected;
            switch (original?.Name)
            {
                case "TryAcquireSalvageTarget":
                    from = AccessTools.Method(typeof(ResourceManager), nameof(ResourceManager.GetSalvageJobs));
                    to = AccessTools.Method(typeof(AssemblyJobs), nameof(AssemblyJobs.SalvageJobs));
                    expected = 1;
                    break;
                case "TryAcquireCollectNuggetsTarget":
                    from = AccessTools.PropertyGetter(typeof(ResourceManager), nameof(ResourceManager.ManualTransferJobs));
                    to = AccessTools.Method(typeof(AssemblyJobs), nameof(AssemblyJobs.TransferJobs));
                    expected = 2;
                    break;
                case "IsTargetValid":
                    from = AccessTools.Method(typeof(ITarget), nameof(ITarget.CanBeAttackedBy));
                    to = AccessTools.Method(typeof(AssemblyJobs), nameof(AssemblyJobs.TargetAttackableBy));
                    expected = 1;
                    break;
                default:
                    return list;
            }
            int hits = list.Count(ci => (ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Equals(ci.operand, from));
            if (from == null || to == null || hits != expected)
            {
                s_sites[original] = 0;
                Log.Error($"AssemblyJobs: found {hits} reads in TurretWeapon.{original.Name}, expected {expected} -- left vanilla.");
                return list;
            }
            MethodInfo enemies = AccessTools.Method(typeof(Ship), nameof(Ship.IsEnemiesWith), new[] { typeof(Ship) });
            MethodInfo enemiesShim = AccessTools.Method(typeof(AssemblyJobs), nameof(AssemblyJobs.IsEnemiesWith));
            foreach (CodeInstruction ci in list)
            {
                if (ci.opcode != OpCodes.Call && ci.opcode != OpCodes.Callvirt) continue;
                if (Equals(ci.operand, from))
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = to;
                }
                else if (enemies != null && enemiesShim != null && Equals(ci.operand, enemies))
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = enemiesShim;
                }
            }
            s_sites[original] = hits;
            return list;
        }
    }
}
