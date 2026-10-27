using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts.Ftl;
using Cosmoteer.Simulation;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class DrivePool
    {
        private static readonly object s_lock = new object();
        private static readonly Dictionary<FtlDrive, FtlManager> s_away = new Dictionary<FtlDrive, FtlManager>();

        public static void Reset()
        {
            lock (s_lock) s_away.Clear();
        }

        public static List<(D Drive, H From, H To)> PlanMoves<D, H>(List<(D Drive, H Own, H Holder)> held, Func<H, H> anchorOf)
            where H : class
        {
            var moves = new List<(D, H, H)>();
            var seen = new HashSet<D>();
            foreach ((D drive, H own, H _) in held)
            {
                if (!seen.Add(drive)) continue;
                H desired = anchorOf(own) ?? own;
                var copies = held.Where(h => EqualityComparer<D>.Default.Equals(h.Drive, drive)).Select(h => h.Holder).ToList();
                bool placed = copies.Any(c => ReferenceEquals(c, desired));
                foreach (H c in copies)
                {
                    if (ReferenceEquals(c, desired)) continue;
                    if (!placed) { moves.Add((drive, c, desired)); placed = true; }
                    else moves.Add((drive, c, null));
                }
            }
            return moves;
        }

        internal static void Rebalance(SimRoot sim)
        {
            if (sim == null) return;
            bool pooling = AssemblyFacade.Enabled && HingeJointManager.ActivePinCount > 0;
            lock (s_lock)
            {
                if (!pooling && s_away.Count == 0) return;
                var held = new List<(FtlDrive, FtlManager, FtlManager)>();
                foreach (Ship s in sim.Ships)
                {
                    FtlManager f = s?.Ftl;
                    if (f == null) continue;
                    foreach (FtlDrive d in f._ftlDrives)
                    {
                        FtlManager own = d.Ship?.Ftl;
                        if (own != null) held.Add((d, own, f));
                    }
                }
                if (held.Count == 0) { s_away.Clear(); return; }
                FtlManager AnchorOf(FtlManager own)
                {
                    if (!pooling) return own;
                    Ship s = own.Ship;
                    if (s == null || AssemblyAi.SegmentsOf(s) == null) return own;
                    Ship a = AssemblyAi.AnchorFor(s);
                    return a != null && ReferenceEquals(a.Sim, sim) && a.Ftl != null ? a.Ftl : own;
                }
                List<(FtlDrive Drive, FtlManager From, FtlManager To)> moves = PlanMoves(held, (Func<FtlManager, FtlManager>)AnchorOf);
                if (moves.Count == 0) return;
                int pooled = 0, home = 0;
                foreach ((FtlDrive d, FtlManager from, FtlManager to) in moves)
                {
                    from._ftlDrives.Remove(d);
                    from._cachedJumpCostFactor = null;
                    if (to == null) continue;
                    to._ftlDrives.Add(d);
                    to._cachedJumpCostFactor = null;
                    if (ReferenceEquals(to, d.Ship?.Ftl)) { s_away.Remove(d); home++; }
                    else { s_away[d] = to; pooled++; }
                }
                foreach (FtlDrive gone in s_away.Where(kv => !kv.Value._ftlDrives.Contains(kv.Key)).Select(kv => kv.Key).ToList()) s_away.Remove(gone);
                LogThrottle.Info("DrivePool|" + moves.Count + "|" + pooled + "|" + home,
                    $"FTL drives: {pooled} drive(s) on segments now serve their ship's anchor, {home} back on their own ship ({s_away.Count} pooled in all).");
            }
        }

        internal static bool OnNonOperational(FtlManager manager, FtlDrive drive)
        {
            lock (s_lock)
            {
                if (drive == null || !s_away.TryGetValue(drive, out FtlManager holder)) return true;
                holder._ftlDrives.Remove(drive);
                holder._cachedJumpCostFactor = null;
                s_away.Remove(drive);
                return manager._ftlDrives.Contains(drive);
            }
        }
    }

    [HarmonyPatch]
    internal static class DrivePoolRoutePatch
    {
        private static bool Prepare(MethodBase original) => original != null || TargetMethod() != null;

        static MethodBase TargetMethod()
        {
            InterfaceMapping map = typeof(FtlManager).GetInterfaceMap(typeof(FtlDrive.IFtlManagerMembers));
            for (int i = 0; i < map.InterfaceMethods.Length; i++)
                if (map.InterfaceMethods[i].Name == nameof(FtlDrive.IFtlManagerMembers.OnFtlDriveNonOperational)) return map.TargetMethods[i];
            return null;
        }

        static bool Prefix(FtlManager __instance, FtlDrive drive)
        {
            try { return DrivePool.OnNonOperational(__instance, drive); }
            catch (Exception ex) { LogThrottle.Exception("DrivePoolRoutePatch", ex); return true; }
        }
    }
}
