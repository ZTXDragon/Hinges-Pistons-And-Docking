using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(FtlLocalJumpCommand), "IsValid")]
    public static class FtlLocalJumpJointGuardPatch
    {
        private static readonly ConditionalWeakTable<Ship, object> s_warned =
            new ConditionalWeakTable<Ship, object>();

        static void Postfix(FtlLocalJumpCommand __instance, ref bool __result)
        {
            try
            {
                if (!__result) return;
                Ship ship = __instance.Ship;
                if (ship == null || !HingeJointManager.IsJointed(ship)) return;
                List<Ship> foreign = LocalJumpCarry.ForeignPartners(ship);
                if (foreign.Count == 0) return;

                __result = false;
                if (!s_warned.TryGetValue(ship, out _))
                {
                    s_warned.Add(ship, null);
                    Log.Info($"FTL local jump REFUSED for ship {ship.UniqueID}: it is joined to {foreign.Count} ship(s) of another owner ({string.Join(", ", foreign.Select(s => s.UniqueID.ToString()))}) by a dock or joint -- they cannot be taken along. Undock them first, then jump.");
                }
            }
            catch (Exception ex)
            {
                Log.Exception("FtlLocalJumpJointGuardPatch.Postfix", ex);
            }
        }

    }
}
