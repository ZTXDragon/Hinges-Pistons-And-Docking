using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Cosmoteer.Crew;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class AssemblyCrewPool
    {
        public static Ship HomeFor(CrewSoul crew, Job job)
        {
            Ship home = crew.HomeShip;
            Ship js = job?.Ship;
            if (home != null && js != null && !ReferenceEquals(home, js) && home.Metadata.PlayerIndex == js.Metadata.PlayerIndex)
            {
                if (SurgeryKin.GraceApplies(SurgeryKin.Are(home, js), ReferenceEquals(crew.Job, job))) return js;
                Part at = crew.ShipCrew?.CurrentPart;
                bool on = at != null && ReferenceEquals(at.Ship, js);
                if (CorridorHinge.CountsAsHome(CorridorHinge.SameWorkGroup(home, js), on, !on && at != null && CorridorHinge.CanWalkTo(at, js)))
                    return js;
            }
            return home;
        }

        public static bool PathOk(Part jobPart, Part crewPart, IntVector2 cell, bool allowAirlockTraversal)
        {
            if (jobPart.HasPathTo(crewPart, cell, allowAirlockTraversal)) return true;
            Ship a = jobPart?.Ship, b = crewPart?.Ship;
            return a != null && b != null && !ReferenceEquals(a, b)
                && (SurgeryKin.GraceApplies(SurgeryKin.Are(a, b), SurgeryKin.t_holding) || (CorridorHinge.SameWorkGroup(a, b) && CorridorHinge.CanWalkTo(crewPart, a)));
        }

        public static bool ExitAllowed(Ship ship) => ship.AllowAutoCrewExit || CorridorHinge.HasWorkGroup(ship);

        public static Ship CurrentShipFor(CrewSoul soul)
        {
            Ship cur = soul.CurrentShip;
            Ship bunk = soul.CrewSource?.Ship;
            if (cur != null && bunk != null && !ReferenceEquals(cur, bunk) && (CorridorHinge.SameGroup(cur, bunk) || SurgeryKin.Are(cur, bunk))) return bunk;
            return cur;
        }
    }

    [HarmonyPatch]
    internal static class AssemblyCrewPoolPatch
    {
        public const int EXPECTED = 5;
        private static readonly Dictionary<MethodBase, int> s_sites = new Dictionary<MethodBase, int>();
        public static string Summary => $"AssemblyCrewPool: {s_sites.Values.Sum()}/{EXPECTED} crew job sites patched (crew work segments joined by open corridor hinges)";

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Job), nameof(Job.IsValidFor));
            yield return AccessTools.Method(typeof(PartCrewJob), nameof(PartCrewJob.IsValidFor));
            yield return AccessTools.PropertyGetter(typeof(PartCrewJob), nameof(PartCrewJob.AllowNonContiguousCrewSearch));
            yield return AccessTools.PropertyGetter(typeof(ResourceTransferJob), nameof(ResourceTransferJob.AllowNonContiguousCrewSearch));
            yield return AccessTools.Method(typeof(CrewSoul), nameof(CrewSoul.RefreshSource));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var list = new List<CodeInstruction>(instructions);
            int hits = 0;
            MethodInfo homeShip = AccessTools.PropertyGetter(typeof(CrewSoul), nameof(CrewSoul.HomeShip));
            MethodInfo curShip = AccessTools.PropertyGetter(typeof(CrewSoul), nameof(CrewSoul.CurrentShip));
            MethodInfo exit = AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.AllowAutoCrewExit));
            for (int i = 0; i < list.Count; i++)
            {
                CodeInstruction ci = list[i];
                if ((ci.opcode != OpCodes.Call && ci.opcode != OpCodes.Callvirt) || !(ci.operand is MethodInfo mi)) continue;
                if (original.Name == nameof(Job.IsValidFor) && original.DeclaringType == typeof(Job) && Equals(mi, homeShip))
                {
                    list.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                    i++;
                    list[i] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AssemblyCrewPool), nameof(AssemblyCrewPool.HomeFor))) { labels = ci.labels, blocks = ci.blocks };
                    hits++;
                }
                else if (original.DeclaringType == typeof(PartCrewJob) && original.Name == nameof(PartCrewJob.IsValidFor) && mi.Name == "HasPathTo" && mi.DeclaringType == typeof(Part)
                         && mi.GetParameters().Length == 3)
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = AccessTools.Method(typeof(AssemblyCrewPool), nameof(AssemblyCrewPool.PathOk));
                    hits++;
                }
                else if (Equals(mi, exit) && original.Name.Contains("AllowNonContiguousCrewSearch"))
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = AccessTools.Method(typeof(AssemblyCrewPool), nameof(AssemblyCrewPool.ExitAllowed));
                    hits++;
                }
                else if (Equals(mi, curShip) && original.DeclaringType == typeof(CrewSoul))
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = AccessTools.Method(typeof(AssemblyCrewPool), nameof(AssemblyCrewPool.CurrentShipFor));
                    hits++;
                }
            }
            if (original != null) s_sites[original] = hits;
            return list;
        }
    }
}
