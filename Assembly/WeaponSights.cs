using System;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts.Weapons;
using Halfling.Timing;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class WeaponSights
    {
        public static double FirstCheckDelay(bool reformedBySurgery, double engineDelay) => reformedBySurgery ? 0.0 : engineDelay;
    }

    [HarmonyPatch(typeof(FixedWeapon), "OnOperational2")]
    internal static class WeaponSightsPatch
    {
        static void Postfix(FixedWeapon __instance)
        {
            try
            {
                Ship ship = __instance?.Ship;
                if (ship == null) return;
                if (WeaponSights.FirstCheckDelay(SurgeryKin.InFamily(ship), 1.0) == 0.0)
                    __instance._getEnemyInSightsCooldown = Time.Zero;
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("WeaponSightsPatch", ex);
            }
        }
    }
}
