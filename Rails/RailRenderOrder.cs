using System;
using System.Collections.Generic;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Rendering;
using Cosmoteer.Simulation;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class RailRenderOrder
    {
        private static bool s_failed;

        private static readonly List<int> s_slots = new List<int>();
        private static readonly List<ShipRenderer> s_members = new List<ShipRenderer>();
        private static readonly Dictionary<Ship, int> s_rank = new Dictionary<Ship, int>();
        private static readonly Dictionary<Ship, int> s_group = new Dictionary<Ship, int>();
        private static readonly Dictionary<ShipRenderer, long> s_key = new Dictionary<ShipRenderer, long>();
        private static readonly List<Ship> s_assembly = new List<Ship>();

        public static bool TryApply(Harmony harmony)
        {
            var draw = AccessTools.Method(typeof(ShipStageRenderer), "Draw");
            if (draw == null)
            {
                Log.Info("rail render order NOT applied: ShipStageRenderer.Draw not found; carts may draw under their rail.");
                return false;
            }
            harmony.Patch(draw, prefix: new HarmonyMethod(typeof(RailRenderOrder), nameof(Prefix)));
            Log.Info("rail render order applied.");
            return true;
        }

        public static void Reset()
        {
            s_slots.Clear();
            s_members.Clear();
            s_rank.Clear();
            s_group.Clear();
            s_key.Clear();
            s_assembly.Clear();
        }

        private static void Prefix(ShipStageRenderer __instance)
        {
            if (s_failed) return;
            try
            {
                Reorder(__instance._toRender);
            }
            catch (Exception ex)
            {
                s_failed = true;
                Log.Exception("RailRenderOrder", ex);
            }
        }

        private static void Reorder(List<ShipRenderer> queue)
        {
            if (queue == null || queue.Count < 2) return;
            s_rank.Clear();
            s_group.Clear();
            int groups = 0;
            for (int i = 0; i < queue.Count; i++)
            {
                Ship ship = queue[i]?.Ship;
                if (ship == null || s_rank.ContainsKey(ship)) continue;
                RailShipManager manager = RailShipManager.TryGet(ship);
                if (manager == null || !manager.HasPairedCarts) continue;
                HingeJointManager.GetAssemblyShips(ship, s_assembly);
                if (s_assembly.Count < 2) continue;
                for (int k = 0; k < s_assembly.Count; k++)
                {
                    Ship member = s_assembly[k];
                    if (member == null || s_rank.ContainsKey(member)) continue;
                    s_rank[member] = k;
                    s_group[member] = groups;
                }
                groups++;
            }
            if (s_rank.Count < 2) return;

            s_slots.Clear();
            s_members.Clear();
            s_key.Clear();
            for (int i = 0; i < queue.Count; i++)
            {
                ShipRenderer r = queue[i];
                Ship ship = r?.Ship;
                if (ship == null || !s_rank.TryGetValue(ship, out int rank)) continue;
                s_slots.Add(i);
                s_members.Add(r);
                s_key[r] = ((long)Math.Min(s_group[ship], 0x7FFF) << 48) | ((long)Math.Min(rank, 0xFFFF) << 32) | (uint)i;
            }
            if (s_members.Count < 2) return;
            s_members.Sort((a, b) => s_key[a].CompareTo(s_key[b]));
            for (int i = 0; i < s_slots.Count; i++) queue[s_slots[i]] = s_members[i];
        }
    }
}
