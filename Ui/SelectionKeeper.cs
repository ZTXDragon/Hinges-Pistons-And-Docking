using System;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;

namespace ZTX.HingesPistonsDocking
{
    internal static class SelectionKeeper
    {
        public static bool IsDirectlySelected(Ship ship)
        {
            try
            {
                PlayerInputHandler input = ship?.Sim?.PlayerInput;
                return input != null && input._selectedShips.ContainsKey(ship);
            }
            catch
            {
                return false;
            }
        }

        public static void MoveTo(Ship survivor, Ship removed)
        {
            try
            {
                PlayerInputHandler input = survivor?.Sim?.PlayerInput;
                if (input == null) return;
                Ship root = AssemblyFacade.Redirect(survivor);
                foreach (Ship s in input._selectedShips.Keys)
                    if (ReferenceEquals(AssemblyFacade.Redirect(s), root)) return;
                input.SelectShips(new[] { survivor });
                Log.Info($"Selection: ship {removed?.UniqueID} merged into {survivor.UniqueID} -- the selection moved to ship {root?.UniqueID}.");
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("SelectionKeeper.MoveTo", ex);
            }
        }
    }
}
