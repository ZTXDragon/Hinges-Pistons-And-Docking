using Cosmoteer.Simulation;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(SimRoot), "UpdatePhysics")]
    internal static class SolverStepPatch
    {
        static void Prefix(SimRoot __instance) => HingeJointManager.ApplySolverIterationsFor(__instance);
    }
}
