using System;
using System.Collections.Generic;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Halfling.Network;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(MPEnqueueCommandsInput), nameof(MPEnqueueCommandsInput.Execute))]
    internal static class OrderRedirectPatch
    {
        static bool Prefix(MPEnqueueCommandsInput __instance, GameRoot game, int playerIndex, MessengerID playerID, bool force)
        {
            try
            {
                if (!AssemblyFlightFeatures.Enabled || HingeJointManager.ActivePinCount == 0) return true;
                List<Command> commands = __instance.Commands;
                var ordered = new HashSet<Ship>();
                foreach (Command c in commands)
                    if (c.HasShip && ReferenceEquals(AssemblyFacade.RootOf(c.Ship) ?? c.Ship, c.Ship)) ordered.Add(c.Ship);
                bool any = false;
                foreach (Command c in commands)
                    if (Target(c, ordered) != null || AgainstOwnSegment(c)) { any = true; break; }
                if (!any) return true;

                for (int i = 0; i < commands.Count; i++)
                {
                    Command command = commands[i];
                    if (AgainstOwnSegment(command))
                    {
                        Command preview = command.Ship.Commands[command.UniqueID];
                        if (preview != null) command.Ship.Commands.RemoveCommand(preview);
                        LogThrottle.Info("OwnSegment|" + command.Ship.UniqueID, $"Assembly orders: a {command.GetType().Name} from ship {command.Ship.UniqueID} against its own segment was dropped.");
                        continue;
                    }
                    Ship anchor = Target(command, ordered);
                    if (anchor != null && (force || (anchor.Commands.CanExecuteCommandsFrom(playerIndex) && anchor.ExistsInSim(game.Sim))))
                    {
                        Command rebuilt = OrderRebuild.For(command, anchor, anchor.Commands.AllocGlobalCommandID());
                        if (rebuilt != null)
                        {
                            Command tentative = command.Ship.Commands[command.UniqueID];
                            if (tentative != null) command.Ship.Commands.RemoveCommand(tentative);
                            if (!__instance.Append) anchor.Commands.ClearConfirmed();
                            if (__instance.AutoCancelSalvageJobs) anchor.Resources.AutoCancelJobs(confirmed: true);
                            if (force || rebuilt.IsValid()) anchor.Commands.EnqueueCommand(rebuilt, append: true, confirmed: true);
                            ordered.Add(anchor);
                            LogThrottle.Info("OrderRedirect|" + command.Ship.UniqueID, $"Assembly orders: a {rebuilt.GetType().Name} given to segment {command.Ship.UniqueID} goes to the anchor {anchor.UniqueID}.");
                            continue;
                        }
                    }
                    VanillaOne(__instance, command, game, playerIndex, force);
                }
                return false;
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("OrderRedirectPatch", ex);
                return true;
            }
        }

        static void Postfix(MPEnqueueCommandsInput __instance, GameRoot game, int playerIndex)
        {
            try
            {
                if (__instance.Append || HingeJointManager.ActivePinCount == 0) return;
                var members = new List<Ship>();
                foreach (Command c in __instance.Commands)
                {
                    if (!c.HasShip || !ReferenceEquals(AssemblyFacade.RootOf(c.Ship), c.Ship)) continue;
                    if (!c.Ship.Commands.CanExecuteCommandsFrom(playerIndex)) continue;
                    HingeJointManager.GetAssemblyShips(c.Ship, members, rigidOnly: false, identity: true);
                    int owner = c.Ship.Metadata?.PlayerIndex ?? int.MinValue;
                    foreach (Ship m in members)
                    {
                        if (ReferenceEquals(m, c.Ship) || m.Commands == null) continue;
                        if ((m.Metadata?.PlayerIndex ?? int.MaxValue) != owner) continue;
                        if (m.Commands.CurConfirmedCommand != null) m.Commands.ClearConfirmed();
                    }
                }
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("OrderRedirectPatch.Postfix", ex);
            }
        }

        private static bool AgainstOwnSegment(Command c)
        {
            return c != null && c.HasShip && c is BaseFollowCommand bf && bf._target != null && UiFanout.IsOwnSegmentShip(c.Ship, bf._target);
        }

        private static Ship Target(Command c, HashSet<Ship> ordered)
        {
            if (c == null || !c.HasShip) return null;
            Ship anchor = AssemblyFacade.RootOf(c.Ship);
            if (anchor == null || anchor.Commands == null) return null;
            bool isAnchor = ReferenceEquals(anchor, c.Ship);
            return AssemblyGraph.RedirectsToAnchor(OrderRebuild.KindOf(c), isAnchor, ordered.Contains(anchor)) ? anchor : null;
        }

        private static void VanillaOne(MPEnqueueCommandsInput input, Command command, GameRoot game, int playerIndex, bool force)
        {
            if (force || (command.HasShip && command.Ship.Commands.CanExecuteCommandsFrom(playerIndex) && command.Ship.ExistsInSim(game.Sim)))
            {
                if (!input.Append)
                    command.Ship.Commands.ClearConfirmed();
                if (input.AutoCancelSalvageJobs)
                    command.Ship.Resources.AutoCancelJobs(confirmed: true);
                Command existing = command.Ship.Commands[command.UniqueID];
                if (existing != null)
                {
                    if (force || (existing.IsValid() && command.IsValid()))
                        command.Ship.Commands.SetConfirmed(existing, confirmed: true);
                    else
                        command.Ship.Commands.RemoveCommand(existing);
                }
                else if (force || command.IsValid())
                {
                    command.Ship.Commands.EnqueueCommand(command, append: true, confirmed: true);
                }
            }
            else
            {
                Command stale = command.HasShip ? command.Ship.Commands[command.UniqueID] : null;
                if (stale != null)
                    command.Ship.Commands.RemoveCommand(stale);
            }
        }
    }
}
