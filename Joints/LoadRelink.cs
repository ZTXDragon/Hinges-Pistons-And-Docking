using System;
using System.Collections.Generic;
using System.Linq;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(GameRoot), nameof(GameRoot.SwitchSimulation))]
    internal static class LoadRelinkPatch
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SimRoot, object> s_done =
            new System.Runtime.CompilerServices.ConditionalWeakTable<SimRoot, object>();

        static void Postfix(GameRoot __instance)
        {
            SimRoot sim = __instance?.Sim;
            if (sim == null) return;
            try
            {
                JumpCarry.Restore(sim);
                if (s_done.TryGetValue(sim, out _)) return;
                s_done.Add(sim, new object());
                List<Ship> ships = sim.Ships.ToList();
                int tried = 0, paired = 0;
                foreach (Ship ship in ships)
                {
                    HingeShipManager m = HingeShipManager.TryGet(ship);
                    if (m == null) continue;
                    paired += m.RelinkAtLoad(out int t);
                    tried += t;
                }
                if (tried > 0)
                    Log.Info($"Load relink: {paired} of {tried} engaged joint(s) re-formed before the first tick (paused); the rest wait for the 1 s service.");
                int carts = 0;
                foreach (Ship ship in ships)
                {
                    RailShipManager rm = RailShipManager.TryGet(ship);
                    if (rm != null) carts += rm.RelinkCartsAtLoad();
                }
                if (carts > 0) Log.Info($"Load relink: {carts} rail cart(s) rejoined their rail before the first tick (paused).");
                if (paired + carts > 0)
                {
                    AssemblyIndex.BuildAtLoad(sim);
                    AssemblyReach.Build(sim);
                    Log.Info("Load relink: assembly tables built before the first tick (paused).");
                    foreach (Ship ship in ships)
                    {
                        List<string> conflicts = ClaimMap.Conflicts(ship);
                        if (conflicts.Count > 0)
                            Log.Info($"Home cells: ship {ship.UniqueID} has {conflicts.Count} part(s) built on cells another segment of its ship needs to come home -- that arm cannot return until they are removed: {string.Join("; ", conflicts.Take(6))}{(conflicts.Count > 6 ? "; ..." : "")}.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception("LoadRelinkPatch", ex);
            }
        }
    }
}
