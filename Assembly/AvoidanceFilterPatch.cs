using System;
using System.Collections.Generic;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(Command), "GetAvoidanceObjects")]
    public static class AvoidanceFilterPatch
    {
        [ThreadStatic] private static List<Ship> t_members;

        static void Postfix(Command __instance, List<Command.AvoidanceObstacle> found)
        {
            try
            {
                if (!AssemblyFlightFeatures.Enabled || found == null || found.Count == 0) return;
                Ship ship = __instance.Ship;
                if (ship == null || !HingeJointManager.IsJointed(ship)) return;
                List<Ship> s_members = t_members ??= new List<Ship>();
                HingeJointManager.GetAssemblyShips(ship, s_members);
                for (int i = found.Count - 1; i >= 0; i--)
                {
                    Ship other = found[i].TheirShip;
                    if (other != null && s_members.Contains(other))
                        found.RemoveAt(i);
                }
            }
            catch (Exception ex)
            {
                Log.Exception("AvoidanceFilterPatch.Postfix", ex);
            }
        }
    }
}
