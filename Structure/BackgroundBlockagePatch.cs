using System;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(ShipUpdateInfo), nameof(ShipUpdateInfo.TestBlockage), new[] { typeof(Ship), typeof(IntRect), typeof(bool) })]
    internal static class BackgroundBlockagePatch
    {
        private static bool s_errorLogged;

        static bool Prefix(Ship ship, IntRect localRect, bool ignoreJunk, ref bool __result)
        {
            if (ship?.Sim == null) return true;
            try
            {
                RotRect shape = localRect;
                shape = ship.DetTransformShapeToWorld(shape);
                RotRect world = shape;
                __result = ship.Sim.Ships.DetHasIntersectingWith(world, approximate: true, junk: ignoreJunk ? false : (bool?)null,
                    condition: s => s != ship && (!ignoreJunk || !s.IsJunk) &&
                                    s.Parts.DetIntersectsWithWorldShape(world, p => !BackgroundFrame.Is(p.Rules)));
                return false;
            }
            catch (Exception ex)
            {
                if (!s_errorLogged)
                {
                    s_errorLogged = true;
                    Log.Exception("BackgroundBlockagePatch.Prefix", ex);
                }
                return true;
            }
        }
    }
}
