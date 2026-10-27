using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class AssemblyCrewGate
    {
        public static bool CanExecute(CommandManager cm, int playerIndex)
        {
            if (cm.CanExecuteCrewCommandsFrom(playerIndex)) return true;
            try
            {
                Ship ship = cm.Ship;
                if (ship == null || !AssemblyFacade.Enabled || HingeJointManager.ActivePinCount == 0) return false;
                if (ship.Crew.Assigned.Count > 0 || !cm.IsCommandableBy(playerIndex)) return false;
                Ship[] segs = AssemblyAi.SegmentsOf(ship);
                if (segs == null) return false;
                foreach (Ship s in segs)
                    if (!ReferenceEquals(s, ship) && s.Crew.Assigned.Count > 0 && s.Commands.IsCommandableBy(playerIndex)) return true;
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("AssemblyCrewGate.CanExecute", ex);
            }
            return false;
        }
    }

    [HarmonyPatch]
    internal static class AssemblyCrewGatePatch
    {
        public const int EXPECTED = 12;
        private static readonly Dictionary<MethodBase, int> s_sites = new Dictionary<MethodBase, int>();
        public static string Summary => $"AssemblyCrewGate: {s_sites.Values.Sum()}/{EXPECTED} crew-order gates patched (a crewless arm takes crew orders)";

        private static readonly string[] Inputs = { "MPMoveResourcesInput", "MPSetCrewSourceInput", "MPCancelMoveResourcesInput", "MPSalvagePartInput",
            "MPCancelSalvagePartInput", "MPSetConstructionAssistanceInput", "MPSetCrewHomeShipInput" };

        private static IEnumerable<MethodBase> TargetMethods()
        {
            Assembly cos = typeof(CommandManager).Assembly;
            foreach (string n in Inputs)
            {
                MethodInfo m = AccessTools.Method(cos.GetType("Cosmoteer.Game.Multiplayer.MPInputs." + n), "Execute");
                if (m != null) yield return m;
            }
            MethodInfo g = AccessTools.PropertyGetter(typeof(CommandManager), nameof(CommandManager.CanExecuteCrewCommandsFromLocalPlayer));
            if (g != null) yield return g;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var list = new List<CodeInstruction>(instructions);
            MethodInfo from = AccessTools.Method(typeof(CommandManager), nameof(CommandManager.CanExecuteCrewCommandsFrom));
            MethodInfo to = AccessTools.Method(typeof(AssemblyCrewGate), nameof(AssemblyCrewGate.CanExecute));
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
}
