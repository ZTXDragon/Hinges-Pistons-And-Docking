using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using Halfling.Serialization;
using Halfling.Serialization.Binary;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class ForwardTable<T> where T : class
    {
        private readonly int _window;
        private readonly Dictionary<(ulong, int, int), (T Item, int Tick)> _map = new Dictionary<(ulong, int, int), (T, int)>();
        private readonly object _lock = new object();

        public ForwardTable(int window) { _window = window; }

        public void Record(ulong ship, int x, int y, T item, int tick)
        {
            lock (_lock)
            {
                _map[(ship, x, y)] = (item, tick);
                if (_map.Count > 4096) Prune(tick);
            }
        }

        public bool TryGet(ulong ship, int x, int y, int now, out T item)
        {
            item = null;
            lock (_lock)
            {
                if (!_map.TryGetValue((ship, x, y), out var e) || !SimTicks.IsFresh(now, e.Tick, _window)) return false;
                item = e.Item;
                return true;
            }
        }

        public void Clear()
        {
            lock (_lock) _map.Clear();
        }

        private void Prune(int now)
        {
            var stale = new List<(ulong, int, int)>();
            foreach (var kv in _map)
                if (!SimTicks.IsFresh(now, kv.Value.Tick, _window)) stale.Add(kv.Key);
            foreach (var k in stale) _map.Remove(k);
        }
    }

    internal static class PartForwarding
    {
        public const int WINDOW_TICKS = 120;
        private static readonly ForwardTable<Part> s_table = new ForwardTable<Part>(WINDOW_TICKS);
        private static SimRoot s_sim;

        public static void Record(Part part, Ship from)
        {
            SimRoot sim = from?.Sim;
            if (part == null || sim == null) return;
            if (!ReferenceEquals(sim, s_sim))
            {
                s_table.Clear();
                s_sim = sim;
            }
            s_table.Record(from.UniqueID._id, part.Location.X, part.Location.Y, part, sim.Tick);
        }

        public static Part Lookup(SimRoot sim, ObjectID ship, IntVector2 cell, ID<PartRules> rules)
        {
            if (sim == null || !ReferenceEquals(sim, s_sim)) return null;
            if (!s_table.TryGet(ship._id, cell.X, cell.Y, sim.Tick, out Part part)) return null;
            if (part?.Ship == null || !ReferenceEquals(part.Ship.Sim, sim) || part.Rules.ID != rules) return null;
            return part;
        }
    }

    [HarmonyPatch(typeof(PartByShipLocSerializer), nameof(PartByShipLocSerializer.Read))]
    internal static class PartForwardingReadPatch
    {
        static bool Prefix(PartByShipLocSerializer __instance, BinaryReader source, ref object __result)
        {
            if (!source.ReadBoolean())
            {
                __result = null;
                return false;
            }
            ObjectID id = ObjectID.ReadFrom(source);
            IntVector2 cell = source.ReadIntVector2();
            var rules = new ID<PartRules>(source.ReadString());
            SimRoot sim = __instance._game?.Sim;
            Part part = sim?.Ships[id]?.Parts[cell, PartRectType.Normal];
            if (part != null && part.Rules.ID == rules)
            {
                __result = part;
                return false;
            }
            Part forwarded = null;
            try
            {
                forwarded = PartForwarding.Lookup(sim, id, cell, rules);
                if (forwarded != null)
                    LogThrottle.Info("PartForwarding", $"Part forwarding: an input named part {rules} at {cell} of ship {id}; it moved to ship {forwarded.Ship?.UniqueID} at {forwarded.Location} -- the input reaches it.");
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("PartForwardingReadPatch", ex);
            }
            __result = forwarded;
            return false;
        }
    }
}
