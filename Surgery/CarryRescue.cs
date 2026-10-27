using System;
using System.Collections.Generic;
using Cosmoteer.Crew;
using Cosmoteer.Data;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Resources;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class CarryRescue
    {
        public const int MAX_RESCUES = 3;

        public const int WINDOW_TICKS = 300;

        private static readonly object s_lock = new object();
        private static readonly Dictionary<CrewSoul, (IResourceSink Sink, int Tick)> s_lastSink = new Dictionary<CrewSoul, (IResourceSink, int)>();
        private static readonly Dictionary<CrewSoul, (int Start, int Count)> s_budget = new Dictionary<CrewSoul, (int, int)>();

        public static bool ShouldRescue(int quantity, bool returnsToStorage, bool inAssembly, int recentRescues, int maxRescues) =>
            quantity > 0 && !returnsToStorage && inAssembly && recentRescues < maxRescues;

        internal static void Remember(Job job, CrewSoul crew)
        {
            if (!(job is ResourceTransferJob rtj) || crew == null || crew.CarriedResource.Quantity <= 0) return;
            if (rtj.Sink == null || rtj.ResourceType != crew.CarriedResource.Type || crew.Sim == null) return;
            int tick = crew.Sim.Tick;
            lock (s_lock)
            {
                if (s_lastSink.Count > 4096)
                {
                    var stale = new List<CrewSoul>();
                    foreach (var kv in s_lastSink)
                        if (tick - kv.Value.Tick >= WINDOW_TICKS) stale.Add(kv.Key);
                    foreach (CrewSoul c in stale) s_lastSink.Remove(c);
                }
                s_lastSink[crew] = (rtj.Sink, tick);
            }
        }

        internal static bool TryRescue(CrewSoul soul, string path)
        {
            if (soul?.Sim == null) return false;
            int qty = soul.CarriedResource.Quantity;
            if (qty <= 0) return false;
            bool toStorage = soul.CarriedResource.TypeRules?.ReturnToStorage ?? true;
            Ship home = soul.HomeShip ?? soul.CurrentShip;
            if (home?.Resources == null) return false;
            int tick = soul.Sim.Tick;
            int recent;
            IResourceSink remembered = null;
            lock (s_lock)
            {
                recent = s_budget.TryGetValue(soul, out var b) && tick - b.Start < WINDOW_TICKS ? b.Count : 0;
                if (s_lastSink.TryGetValue(soul, out var last) && tick - last.Tick < WINDOW_TICKS) remembered = last.Sink;
                s_lastSink.Remove(soul);
            }
            if (!ShouldRescue(qty, toStorage, InAssembly(soul, home), recent, MAX_RESCUES)) return false;

            ID<ResourceRules> type = soul.CarriedResource.Type;
            IResourceSink sink = null;
            int room = 0;
            if (remembered?.Part?.Ship != null && remembered.ResourceType == type && remembered.Part.ExistsInSim(soul.Sim))
            {
                room = remembered.GetRemainingCapacity(type, false);
                if (room > 0) sink = remembered;
            }
            if (sink == null)
                sink = home.Resources.GetBestReceivingSink(soul, qty, MPValueType.Confirmed, out room, allowDifferentShips: true,
                    allowAirlockTraversal: true, excludeDeconstructionQueued: false, ignoreAllowManualReceiveToggles: false);
            if (sink == null || room <= 0) return false;

            int amount = Math.Min(qty, room);
            soul.Job?.UnassignCrew(soul);
            ShipMerger.EnsureJobIdsAbove(home);
            home.Resources.CarriedResourceTransfer(soul, sink, amount);
            lock (s_lock) s_budget[soul] = recent == 0 ? (tick, 1) : (s_budget[soul].Start, recent + 1);
            LogThrottle.Info("CarryRescue|" + home.UniqueID,
                $"Carry rescue: {amount} {type} carried by a crew of ship {home.UniqueID} re-sent to {sink.Part?.Rules.ID} at {sink.Part?.Location} on ship {sink.Part?.Ship?.UniqueID} (its job was lost; the engine would have {path} it).");
            return true;
        }

        private static bool InAssembly(CrewSoul soul, Ship home)
        {
            Ship cur = soul.CurrentShip;
            return (AssemblyIndex.AssemblyOf(home)?.Length ?? 0) > 1 || (cur != null && (AssemblyIndex.AssemblyOf(cur)?.Length ?? 0) > 1)
                || SurgeryKin.InFamily(home) || SurgeryKin.InFamily(cur);
        }

        internal static void Reset()
        {
            lock (s_lock)
            {
                s_lastSink.Clear();
                s_budget.Clear();
            }
        }
    }

    [HarmonyPatch(typeof(Job), nameof(Job.UnassignCrew))]
    internal static class CarryRescueRememberPatch
    {
        static void Prefix(Job __instance, CrewSoul crew)
        {
            try { CarryRescue.Remember(__instance, crew); }
            catch (Exception ex) { LogThrottle.Exception("CarryRescueRememberPatch", ex); }
        }
    }

    [HarmonyPatch(typeof(CrewSoul), "ReturnCarriedResources")]
    internal static class CarryRescueReturnPatch
    {
        static bool Prefix(CrewSoul __instance)
        {
            try { return !CarryRescue.TryRescue(__instance, "deleted (return)"); }
            catch (Exception ex) { LogThrottle.Exception("CarryRescueReturnPatch", ex); return true; }
        }
    }

    [HarmonyPatch(typeof(CrewSoul), "DiscardCarriedResources")]
    internal static class CarryRescueDiscardPatch
    {
        static bool Prefix(CrewSoul __instance)
        {
            try { return !CarryRescue.TryRescue(__instance, "deleted (discard)"); }
            catch (Exception ex) { LogThrottle.Exception("CarryRescueDiscardPatch", ex); return true; }
        }
    }
}
