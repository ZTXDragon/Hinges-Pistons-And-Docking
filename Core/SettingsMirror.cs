using System;
using System.Collections.Generic;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Ships;
using Halfling.Network;
using Cosmoteer.Ships.Roles;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class SettingsMirror
    {
        [ThreadStatic] private static bool s_running;

        public static List<ulong> Others(ulong[] members, HashSet<ulong> named)
        {
            var list = new List<ulong>();
            if (members == null) return list;
            var seen = new HashSet<ulong>();
            foreach (ulong m in members)
                if ((named == null || !named.Contains(m)) && seen.Add(m)) list.Add(m);
            return list;
        }

        public static List<Ship> OthersOf(IEnumerable<Ship> namedShips)
        {
            var result = new List<Ship>();
            if (namedShips == null || HingeJointManager.ActivePinCount == 0) return result;
            var named = new HashSet<ulong>();
            var groups = new List<Ship[]>();
            foreach (Ship s in namedShips)
            {
                if (s == null) continue;
                named.Add(AssemblyTopology.Id(s));
                Ship[] m = AssemblyIndex.FreshAssemblyOf(s);
                if (m != null && !groups.Contains(m)) groups.Add(m);
            }
            foreach (Ship[] g in groups)
            {
                var byId = new Dictionary<ulong, Ship>();
                var ids = new ulong[g.Length];
                for (int i = 0; i < g.Length; i++)
                {
                    ids[i] = g[i] == null ? 0UL : AssemblyTopology.Id(g[i]);
                    if (g[i] != null) byId[ids[i]] = g[i];
                }
                foreach (ulong id in Others(ids, named))
                {
                    if (byId.TryGetValue(id, out Ship ship))
                    {
                        result.Add(ship);
                        named.Add(id);
                    }
                }
            }
            return result;
        }

        public static void Mirror(string what, Ship ship, Action<Ship> copyFor)
        {
            if (s_running || ship == null || !AssemblyFacade.Enabled) return;
            s_running = true;
            try
            {
                List<Ship> others = OthersOf(new[] { ship });
                foreach (Ship m in others) copyFor(m);
                if (others.Count > 0)
                    LogThrottle.Info("SettingsMirror|" + what, $"Settings mirror: {what} from ship {ship.UniqueID} copied to {others.Count} other segment(s).");
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("SettingsMirror." + what, ex);
            }
            finally
            {
                s_running = false;
            }
        }

        [HarmonyPatch(typeof(MPSetAttackDefaultsInput), nameof(MPSetAttackDefaultsInput.Execute))]
        private static class AttackDefaultsPatch
        {
            static void Postfix(MPSetAttackDefaultsInput __instance, GameRoot game, int playerIndex, MessengerID playerID, bool force)
            {
                Mirror("attack defaults", __instance.Ship,
                    m => new MPSetAttackDefaultsInput(m, __instance.DefaultAttackInfo).Execute(game, playerIndex, playerID, force));
            }
        }

        [HarmonyPatch(typeof(MPSetConstructionAssistanceInput), nameof(MPSetConstructionAssistanceInput.Execute))]
        private static class ConstructionAssistancePatch
        {
            static void Postfix(MPSetConstructionAssistanceInput __instance, GameRoot game, int playerIndex, MessengerID playerID, bool force)
            {
                Mirror("construction assistance", __instance.Ship,
                    m => new MPSetConstructionAssistanceInput(m, __instance.Mode).Execute(game, playerIndex, playerID, force));
            }
        }

        [HarmonyPatch(typeof(MPUpdateRoleInput), nameof(MPUpdateRoleInput.Execute))]
        private static class RolesPatch
        {
            static void Postfix(MPUpdateRoleInput __instance, GameRoot game, int playerIndex, MessengerID playerID, bool force)
            {
                RoleUpdateInfo ui = __instance.UpdateInfo;
                if (ui == null || !ui.HasShip || !ui.Ship.Commands.IsCommandableBy(playerIndex)) return;
                Mirror("crew roles", ui.Ship, m =>
                {
                    RoleUpdateInfo c = RoleUpdateInfo.Alloc(m);
                    try
                    {
                        c.RolesToAdd.AddRange(ui.RolesToAdd);
                        c.RolesToRemove.AddRange(ui.RolesToRemove);
                        c.RolesToMove.AddRange(ui.RolesToMove);
                        c.NameChanges.UnionWith(ui.NameChanges);
                        c.ColorChanges.UnionWith(ui.ColorChanges);
                        c.PriorityChanges.UnionWith(ui.PriorityChanges);
                        c.AssignmentPriorityChanges.UnionWith(ui.AssignmentPriorityChanges);
                        c.AutoFillChanges.UnionWith(ui.AutoFillChanges);
                        if (c.HasModifications && c.CanApply()) c.Apply();
                        else if (c.HasModifications) LogThrottle.Info("RolesMirror|" + m.UniqueID, $"Settings mirror: crew roles of ship {m.UniqueID} differ from ship {ui.Ship.UniqueID}'s -- the edit was not copied there.");
                    }
                    finally { c.Release(); }
                });
            }
        }

        [HarmonyPatch(typeof(MPSetStripSalvageInput), nameof(MPSetStripSalvageInput.Execute))]
        private static class StripSalvagePatch
        {
            static void Postfix(MPSetStripSalvageInput __instance, GameRoot game, int playerIndex, MessengerID playerID, bool force)
            {
                Mirror("strip salvage", __instance.Ship,
                    m => new MPSetStripSalvageInput(m, __instance.StripSalvage).Execute(game, playerIndex, playerID, force));
            }
        }

        [HarmonyPatch(typeof(MPSetResourceBannedInput), nameof(MPSetResourceBannedInput.Execute))]
        private static class ResourceBannedPatch
        {
            static void Postfix(MPSetResourceBannedInput __instance, GameRoot game, int playerIndex, MessengerID playerID, bool force)
            {
                Mirror("resource ban", __instance.Ship,
                    m => new MPSetResourceBannedInput(m, __instance.ResourceType, __instance.Banned).Execute(game, playerIndex, playerID, force));
            }
        }

        [HarmonyPatch(typeof(MPSetFlightDirectionInput), nameof(MPSetFlightDirectionInput.Execute))]
        private static class FlightDirectionPatch
        {
            static void Postfix(MPSetFlightDirectionInput __instance, GameRoot game, int playerIndex, MessengerID playerID, bool force)
            {
                Mirror("flight direction", __instance.Ship,
                    m => new MPSetFlightDirectionInput(m, __instance.FlightDirection).Execute(game, playerIndex, playerID, force));
            }
        }

        [HarmonyPatch(typeof(MPSelfDestructShipsInput), nameof(MPSelfDestructShipsInput.Execute))]
        private static class SelfDestructPatch
        {
            static void Postfix(MPSelfDestructShipsInput __instance, GameRoot game, int playerIndex, MessengerID playerID, bool force)
            {
                if (s_running || !AssemblyFacade.Enabled || __instance.Ships.Count == 0) return;
                s_running = true;
                try
                {
                    List<Ship> others = OthersOf(__instance.Ships);
                    if (others.Count == 0) return;
                    var copy = new MPSelfDestructShipsInput(others) { Duration = __instance.Duration };
                    copy.Execute(game, playerIndex, playerID, force);
                    LogThrottle.Info("SettingsMirror|self-destruct", $"Settings mirror: self-destruct reaches {others.Count} other segment(s).");
                }
                catch (Exception ex)
                {
                    LogThrottle.Exception("SettingsMirror.SelfDestruct", ex);
                }
                finally
                {
                    s_running = false;
                }
            }
        }
    }
}
