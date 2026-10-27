using System;
using System.Collections.Generic;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Parts;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class AssemblyAllegiancePatch
    {
        [ThreadStatic] private static bool s_propagating;

        public static bool AreAssemblyMates(Ship a, Ship b)
        {
            if (a == null || b == null || ReferenceEquals(a, b)) return false;
            if (!HingeJointManager.IsJointed(a) || !HingeJointManager.IsJointed(b)) return false;
            var members = new List<Ship>();
            HingeJointManager.GetAssemblyShips(a, members, rigidOnly: false, identity: true);
            return members.Contains(b);
        }

        private static bool AssemblyAttacks(Ship attacker, Ship target)
        {
            var membersA = new List<Ship>();
            var membersB = new List<Ship>();
            HingeJointManager.GetAssemblyShips(attacker, membersA, rigidOnly: false, identity: true);
            HingeJointManager.GetAssemblyShips(target, membersB, rigidOnly: false, identity: true);
            foreach (Ship m in membersA)
            {
                if (m?.Commands?.CurConfirmedCommand is AttackCommand ac && ac.Target != null && membersB.Contains(ac.Target))
                    return true;
            }
            return false;
        }

        [HarmonyPatch(typeof(ShipMetadata), "PlayerIndex", MethodType.Setter)]
        private static class IndexUnityPatch
        {
            static void Postfix(ShipMetadata __instance, int value)
            {
                if (s_propagating || !AssemblyFacade.Enabled) return;
                try
                {
                    Ship ship = __instance.Ship;
                    if (ship == null || value == -3 || !HingeJointManager.IsJointed(ship)) return;
                    s_propagating = true;
                    try
                    {
                        var members = new List<Ship>();
                        HingeJointManager.GetAssemblyShips(ship, members, rigidOnly: false, identity: true);
                        int changed = 0;
                        foreach (Ship m in members)
                        {
                            if (ReferenceEquals(m, ship) || m?.Metadata == null) continue;
                            if (m.Metadata.PlayerIndex == value || m.Metadata.PlayerIndex == -3) continue;
                            m.Metadata.PlayerIndex = value;
                            changed++;
                        }
                        if (changed > 0)
                            Log.Info($"Allegiance of ship {ship.UniqueID} -> {value}: mirrored to {changed} assembly member(s).");
                    }
                    finally
                    {
                        s_propagating = false;
                    }
                }
                catch (Exception ex)
                {
                    Log.Exception("AssemblyAllegiancePatch.IndexUnityPatch", ex);
                }
            }
        }

        [HarmonyPatch(typeof(Ship), "CanBeDamagedBy", new Type[] { typeof(Ship), typeof(Ship), typeof(HealthType?) })]
        private static class DamageUnityShipPatch
        {
            static void Postfix(Ship __instance, Ship ship, Ship target, HealthType? healthType, ref bool __result)
            {
                try
                {
                    if (__result || !AssemblyFacade.Enabled || ship == null || target == null || __instance.Sim == null) return;
                    if (ReferenceEquals(target, __instance) || !AreAssemblyMates(__instance, target)) return;
                    __result = __instance.Sim.Mode.CanDamage(ship.Metadata.PlayerIndex, __instance.Metadata.PlayerIndex,
                        healthType, isExplicitlyTargeted: true, isSameShip: ReferenceEquals(ship, __instance));
                }
                catch (Exception ex)
                {
                    Log.Exception("AssemblyAllegiancePatch.DamageUnityShipPatch", ex);
                }
            }
        }

        [HarmonyPatch(typeof(Ship), "CanBeDamagedBy", new Type[] { typeof(int), typeof(Ship), typeof(HealthType?) })]
        private static class DamageUnityIndexPatch
        {
            static void Postfix(Ship __instance, int playerIndex, Ship target, HealthType? healthType, ref bool __result)
            {
                try
                {
                    if (__result || !AssemblyFacade.Enabled || target == null || __instance.Sim == null) return;
                    if (ReferenceEquals(target, __instance) || !AreAssemblyMates(__instance, target)) return;
                    __result = __instance.Sim.Mode.CanDamage(playerIndex, __instance.Metadata.PlayerIndex,
                        healthType, isExplicitlyTargeted: true, isSameShip: false);
                }
                catch (Exception ex)
                {
                    Log.Exception("AssemblyAllegiancePatch.DamageUnityIndexPatch", ex);
                }
            }
        }

        [HarmonyPatch(typeof(Ship), "IsEnemiesWith", new Type[] { typeof(Ship) })]
        private static class EnemyUnityPatch
        {
            static void Postfix(Ship __instance, Ship ship, ref bool __result)
            {
                try
                {
                    if (__result || !AssemblyFacade.Enabled || ship == null || ReferenceEquals(ship, __instance)) return;
                    bool jointedA = HingeJointManager.IsJointed(__instance);
                    bool jointedB = HingeJointManager.IsJointed(ship);
                    if (!jointedA && !jointedB) return;
                    if (__instance.Metadata.PlayerIndex == -3 || ship.Metadata.PlayerIndex == -3) return;
                    if (__instance.Sim != null && __instance.Sim.Mode.AreAllies(__instance.Metadata.PlayerIndex, ship.Metadata.PlayerIndex)) return;
                    if (AssemblyAttacks(ship, __instance) || AssemblyAttacks(__instance, ship))
                        __result = true;
                }
                catch (Exception ex)
                {
                    Log.Exception("AssemblyAllegiancePatch.EnemyUnityPatch", ex);
                }
            }
        }
    }
}
