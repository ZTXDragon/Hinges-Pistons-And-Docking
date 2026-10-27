using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Cosmoteer.Crew;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Crew.Jobs;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class AssemblyCrew
    {
        public static bool SearchOrigin(Job job, out Destination? origin, out float distanceToOrigin, List<Ship> additionalSearchShips)
        {
            bool found = job.GetSearchOrigin(out origin, out distanceToOrigin, additionalSearchShips);
            if (!found || additionalSearchShips == null) return found;
            try
            {
                Ship jobShip = job.Ship;
                if (jobShip != null && CorridorHinge.HasWorkGroup(jobShip))
                    foreach (Ship m in CorridorHinge.WorkGroupOf(jobShip))
                        if (ReferenceEquals(m.Sim, jobShip.Sim) && m.Metadata.PlayerIndex == jobShip.Metadata.PlayerIndex && !additionalSearchShips.Contains(m))
                            additionalSearchShips.Add(m);
                if (!(job is ResourceTransferJob rtj) || !(rtj.Source is Nugget) || rtj.Sink == null || rtj.Sink.IsConstructionSink) return found;
                Ship owner = job.Ship;
                if (owner == null || HingeJointManager.ActivePinCount == 0) return found;
                Ship[] members = AssemblyIndex.FreshAssemblyOf(owner);
                if (members == null || members.Length < 2) return found;
                foreach (Ship m in members)
                {
                    if (m == null || ReferenceEquals(m, owner) || !ReferenceEquals(m.Sim, owner.Sim)) continue;
                    if (m.Metadata.PlayerIndex != owner.Metadata.PlayerIndex || additionalSearchShips.Contains(m)) continue;
                    additionalSearchShips.Add(m);
                }
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("AssemblyCrew.SearchOrigin", ex);
            }
            return found;
        }
    }

    [HarmonyPatch(typeof(JobManager), nameof(JobManager.GetCrewForJob))]
    internal static class AssemblyCrewSitePatch
    {
        public const int EXPECTED = 1;
        private static readonly Dictionary<MethodBase, int> s_sites = new Dictionary<MethodBase, int>();
        public static string Summary => $"AssemblyCrew: {s_sites.Values.Sum()}/{EXPECTED} crew search origin call patched";

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var list = new List<CodeInstruction>(instructions);
            MethodInfo from = AccessTools.Method(typeof(Job), nameof(Job.GetSearchOrigin));
            MethodInfo to = AccessTools.Method(typeof(AssemblyCrew), nameof(AssemblyCrew.SearchOrigin));
            int hits = list.Count(ci => (ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Equals(ci.operand, from));
            if (from == null || to == null || hits != EXPECTED)
            {
                if (original != null) s_sites[original] = 0;
                Log.Error($"AssemblyCrew: found {hits} GetSearchOrigin calls in JobManager.GetCrewForJob, expected {EXPECTED} -- left vanilla.");
                return list;
            }
            foreach (CodeInstruction ci in list)
            {
                if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Equals(ci.operand, from))
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = to;
                }
            }
            if (original != null) s_sites[original] = hits;
            return list;
        }
    }
}
