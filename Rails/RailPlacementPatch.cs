using System;
using System.Collections.Generic;
using System.Linq;
using Cosmoteer;
using Cosmoteer.Game.Gui.Build;
using Cosmoteer.Game.SelectionStates;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using HarmonyLib;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(PartBrushState), "UpdateAddedParts")]
    internal static class RailPlacementPatch
    {
        private static PartBrushState s_state;
        private static IntVector2? s_lastCell;
        private static bool s_failed;

        public static void Reset()
        {
            s_state = null;
            s_lastCell = null;
        }

        static void Prefix(PartBrushState __instance, Ship ship, Vector2 shipMouseLoc)
        {
            if (s_failed || __instance == null || ship == null) return;
            try
            {
                PartRules rules = __instance.Part;
                if (rules == null || !rules.IsRotateable || !rules.Components.OfType<ZtxRailTileRules>().Any()) return;
                IntVector2 cell = BaseSelectionState.GetPartLoc(shipMouseLoc + __instance._mouseLocOffset, new IntVector2(1, 1));
                if (!ReferenceEquals(s_state, __instance)) { s_state = __instance; s_lastCell = null; }
                if (s_lastCell.HasValue && s_lastCell.Value == cell) return;
                s_lastCell = cell;
                List<IntVector2> nb = RailGrid.NeighbourDirs(ship, cell);
                if (nb.Count == 1 && ZtxRailTerminalComponent.ForPart(RailGrid.PartAt(ship, cell + nb[0])) != null) return;
                IntVector2? up = RailSides.AutoUp(cell, c => RailGrid.NeighbourDirs(ship, c), c => RailGrid.UpOf(ship, c));
                if (!up.HasValue) return;
                int rot = RotationFor(up.Value);
                if (rot < 0 || rot == __instance._partRot) return;
                __instance._partRot = rot;
                __instance._prevPartLoc = null;
            }
            catch (Exception ex)
            {
                s_failed = true;
                Log.Exception("RailPlacementPatch", ex);
            }
        }

        private static int RotationFor(IntVector2 up)
        {
            for (int r = 0; r < 4; r++)
                if (OrthogonalRotation.Up.GetShipRelative(r, false).ToVector() == up) return r;
            return -1;
        }
    }
}
