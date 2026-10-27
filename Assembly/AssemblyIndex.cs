using System;
using System.Collections.Generic;
using System.Linq;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Simulation;

namespace ZTX.HingesPistonsDocking
{
    internal static class AssemblyIndex
    {
        private sealed class Snapshot
        {
            public readonly Dictionary<Ship, Ship[]> AssemblyByShip = new Dictionary<Ship, Ship[]>();
            public readonly List<Ship[]> Groups = new List<Ship[]>();
            public readonly Dictionary<Ship, Ship> GroupAnchor = new Dictionary<Ship, Ship>();
            public string Text = "";
        }

        private static readonly Snapshot s_empty = new Snapshot();
        private static readonly Dictionary<Ship, Ship> s_noHolders = new Dictionary<Ship, Ship>();

        private static volatile Snapshot s_snap = s_empty;
        private static volatile Dictionary<Ship, Ship> s_holders = s_noHolders;
        private static volatile int s_holdersTick = int.MinValue;

        private static SimRoot s_lastSim;
        private static int s_lastTick = int.MinValue;
        private static int s_builtVersion = -1;
        private static readonly List<(Ship A, Ship B, bool SameShip)> s_edges = new List<(Ship, Ship, bool)>();
        private static readonly List<(ulong, ulong, int)> s_idEdges = new List<(ulong, ulong, int)>();
        private static readonly Dictionary<ulong, Ship> s_byId = new Dictionary<ulong, Ship>();
        private static readonly Dictionary<Ship, Command> s_lastCmd = new Dictionary<Ship, Command>();
        private static readonly Dictionary<Ship, List<Command>> s_lastQueue = new Dictionary<Ship, List<Command>>();
        private static readonly Dictionary<Ship, int> s_stamp = new Dictionary<Ship, int>();
        private static readonly HashSet<(Ship, int)> s_liveKeys = new HashSet<(Ship, int)>();
        private static readonly Dictionary<Ship, Ship> s_nextHolders = new Dictionary<Ship, Ship>();
        private static readonly List<Ship> s_part = new List<Ship>();
        private static readonly List<int> s_owners = new List<int>();
        private static readonly Dictionary<(Ship, int), Ship> s_lastTargetOf = new Dictionary<(Ship, int), Ship>();
        private static Dictionary<ulong, ulong> s_prevAnchorOf = new Dictionary<ulong, ulong>();
        private static Dictionary<ulong, Ship> s_prevById = new Dictionary<ulong, Ship>();

        public static Ship[] AssemblyOf(Ship ship)
        {
            if (ship == null) return null;
            return s_snap.AssemblyByShip.TryGetValue(ship, out Ship[] m) ? m : null;
        }

        public static Ship[] FreshAssemblyOf(Ship ship)
        {
            SimRoot sim = ship?.Sim;
            if (sim == null || !ReferenceEquals(sim, s_lastSim) || sim.Tick - s_holdersTick > 1) return null;
            return AssemblyOf(ship);
        }

        public static Ship FreshGroupAnchorOf(Ship ship)
        {
            SimRoot sim = ship?.Sim;
            if (sim == null || !ReferenceEquals(sim, s_lastSim) || sim.Tick - s_holdersTick > 1) return null;
            return s_snap.GroupAnchor.TryGetValue(ship, out Ship a) ? a : null;
        }

        public static IReadOnlyList<Ship[]> FreshGroups(SimRoot sim)
        {
            if (sim == null || !ReferenceEquals(sim, s_lastSim) || sim.Tick - s_holdersTick > 1) return null;
            return s_snap.Groups;
        }

        public static bool TryGetHolder(Ship ship, out Ship holder)
        {
            holder = null;
            Dictionary<Ship, Ship> h = s_holders;
            if (ship == null || h.Count == 0) return false;
            SimRoot sim = ship.Sim;
            if (sim == null || sim.Tick - s_holdersTick > 1) return false;
            return h.TryGetValue(ship, out holder);
        }

        public static void Reset(SimRoot disposed)
        {
            if (disposed == null || !ReferenceEquals(disposed, s_lastSim)) return;
            s_snap = s_empty;
            s_holders = s_noHolders;
            s_holdersTick = int.MinValue;
            s_lastSim = null;
            s_lastTick = int.MinValue;
            s_builtVersion = -1;
            s_edges.Clear();
            s_idEdges.Clear();
            s_byId.Clear();
            s_lastCmd.Clear();
            s_lastQueue.Clear();
            s_stamp.Clear();
            s_lastTargetOf.Clear();
        }

        public static void BuildAtLoad(SimRoot sim)
        {
            Ship any = sim?.Ships.FirstOrDefault();
            if (any == null) return;
            Tick(any);
            if (ReferenceEquals(s_lastSim, sim)) s_lastTick = int.MinValue;
        }

        public static void Tick(Ship caller)
        {
            try
            {
                SimRoot sim = caller?.Sim;
                if (sim == null) return;
                int tick = sim.Tick;
                if (ReferenceEquals(sim, s_lastSim) && tick == s_lastTick) return;
                bool simChanged = !ReferenceEquals(sim, s_lastSim);
                if (simChanged)
                {
                    s_prevAnchorOf = new Dictionary<ulong, ulong>();
                    s_prevById = new Dictionary<ulong, Ship>();
                    s_lastCmd.Clear();
                    s_lastQueue.Clear();
                    s_stamp.Clear();
                    s_lastTargetOf.Clear();
                    CarryRescue.Reset();
                }
                s_lastSim = sim;
                s_lastTick = tick;

                bool suspended = HingeJointManager.CollectAssemblyEdges(s_edges, sim);
                int version = HingeJointManager.TopologyVersion;
                if (simChanged || suspended || version != s_builtVersion)
                {
                    s_builtVersion = version;
                    Rebuild(sim);
                }
                UpdateHolders(tick);
                AssemblyVerdict.JudgeCuts(sim);
                DrivePool.Rebalance(sim);
                JumpFuel.ClearCaches(sim);
                SurgeryKin.Prune(sim);
                CorridorHinge.Publish(sim);
                AssemblySupply.Tick(sim);
                CrewTargets.Sync(sim);
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("AssemblyIndex.Tick", ex);
            }
        }

        private static ulong Id(Ship s) => s.UniqueID._id;

        private static void Rebuild(SimRoot sim)
        {
            s_idEdges.Clear();
            s_byId.Clear();
            foreach ((Ship a, Ship b, bool same) in s_edges)
            {
                if (!ReferenceEquals(a.Sim, sim) || !ReferenceEquals(b.Sim, sim)) continue;
                ulong ia = Id(a), ib = Id(b);
                s_byId[ia] = a;
                s_byId[ib] = b;
                s_idEdges.Add((ia, ib, same ? AssemblyGraph.KIND_SAME_SHIP : AssemblyGraph.KIND_ATTACHED));
            }
            AssemblyGraph assemblies = AssemblyGraph.Build(s_idEdges);
            AssemblyGraph groups = AssemblyTopology.GroupsOf(sim);
            InheritOrders(sim, groups);
            var snap = new Snapshot();
            foreach (ulong anchor in assemblies.Anchors)
            {
                Ship[] members = Ships(assemblies.MembersOf(anchor));
                foreach (Ship m in members) snap.AssemblyByShip[m] = members;
            }
            var inGroup = new HashSet<Ship>();
            foreach (ulong anchor in groups.Anchors)
            {
                Ship[] members = Ships(groups.MembersOf(anchor));
                snap.Groups.Add(members);
                if (members.Length > 0)
                    foreach (Ship gm in members)
                        if (gm != null) snap.GroupAnchor[gm] = members[0];
                foreach (Ship m in members) inGroup.Add(m);
            }
            if (s_lastCmd.Count > 0)
            {
                var gone = new List<Ship>();
                foreach (Ship k in s_lastCmd.Keys) if (!inGroup.Contains(k)) gone.Add(k);
                foreach (Ship k in gone)
                {
                    s_lastCmd.Remove(k);
                    s_lastQueue.Remove(k);
                    s_stamp.Remove(k);
                }
            }
            snap.Text = "assemblies " + (assemblies.Anchors.Count == 0 ? "none" : assemblies.Describe()) + " | order groups " + (groups.Anchors.Count == 0 ? "none" : groups.Describe());
            if (snap.Text != s_snap.Text)
                Log.Info("Assembly index: " + snap.Text + " (anchor: members, by ship id).");
            s_snap = snap;
        }

        private static void InheritOrders(SimRoot sim, AssemblyGraph groups)
        {
            var inherit = AssemblyGraph.Inheritors(s_prevAnchorOf, groups);
            foreach ((ulong newId, ulong fromId, bool transfer) in inherit)
            {
                if (!s_byId.TryGetValue(newId, out Ship to) && !s_prevById.TryGetValue(newId, out to)) continue;
                if (!s_prevById.TryGetValue(fromId, out Ship from)) continue;
                if (to.Physics?.Body == null || !to.ExistsInSim(sim)) continue;
                CommandManager cm = to.Commands;
                if (cm == null || !cm.CanExecuteCommands || cm.CurConfirmedCommand != null) continue;
                if ((to.Metadata?.PlayerIndex ?? int.MinValue) != (from.Metadata?.PlayerIndex ?? int.MaxValue)) continue;
                Command order = from.Commands?.CurConfirmedCommand;
                if (order == null) s_lastCmd.TryGetValue(from, out order);
                if (order == null) continue;
                Command rebuilt = !transfer && order is MoveCommand move
                    ? CutOrders.MoveForCutPiece(move, from, to, cm.AllocGlobalCommandID())
                    : OrderRebuild.For(order, to, cm.AllocGlobalCommandID());
                if (rebuilt == null)
                {
                    Log.Info($"Assembly orders: ship {to.UniqueID} is a new top (from {from.UniqueID}) but its {order.GetType().Name} cannot be copied -- left as it was.");
                    continue;
                }
                cm.EnqueueCommand(rebuilt, append: true, confirmed: true);
                if (transfer && ReferenceEquals(from.Sim, sim)) from.Commands?.ClearConfirmed();
                Log.Info($"Assembly orders: ship {to.UniqueID} is a new top -- {(transfer ? "took over" : "got a copy of")} ship {from.UniqueID}'s {order.GetType().Name}.");
            }
            var prev = new Dictionary<ulong, ulong>();
            foreach (ulong anchor in groups.Anchors)
                foreach (ulong m in groups.MembersOf(anchor)) prev[m] = anchor;
            s_prevAnchorOf = prev;
            s_prevById = new Dictionary<ulong, Ship>(s_byId);
        }

        private static Ship[] Ships(IReadOnlyList<ulong> ids)
        {
            var members = new List<Ship>(ids.Count);
            foreach (ulong id in ids)
                if (s_byId.TryGetValue(id, out Ship s)) members.Add(s);
            return members.ToArray();
        }

        private static void UpdateHolders(int tick)
        {
            Snapshot snap = s_snap;
            s_nextHolders.Clear();
            s_liveKeys.Clear();
            foreach (Ship[] members in snap.Groups)
            {
                foreach (Ship m in members)
                {
                    CommandManager cm = m.Commands;
                    if (cm == null) continue;
                    Command cmd = cm.CurConfirmedCommand;
                    if (!s_lastQueue.TryGetValue(m, out List<Command> queue))
                    {
                        s_lastQueue[m] = queue = new List<Command>();
                        s_stamp[m] = -1;
                        s_lastCmd[m] = cmd is DirectControlCommand ? null : cmd;
                    }
                    else if (!(cmd is DirectControlCommand))
                    {
                        s_lastCmd.TryGetValue(m, out Command before);
                        bool changed = !ReferenceEquals(cmd, before);
                        if (AssemblyGraph.IsNewPlayerOrder(changed, cmd == null, cmd != null && queue.Contains(cmd), cmd != null && cmd.UniqueID.PlayerID.HasValue))
                            s_stamp[m] = tick;
                        s_lastCmd[m] = cmd;
                    }
                    queue.Clear();
                    foreach (Command c in cm)
                        if (c.IsConfirmed) queue.Add(c);
                }
                s_owners.Clear();
                foreach (Ship m in members)
                {
                    int owner = m.Metadata?.PlayerIndex ?? int.MinValue;
                    if (!s_owners.Contains(owner)) s_owners.Add(owner);
                }
                foreach (int owner in s_owners)
                {
                    s_part.Clear();
                    foreach (Ship m in members)
                        if ((m.Metadata?.PlayerIndex ?? int.MinValue) == owner) s_part.Add(m);
                    if (s_part.Count < 2) continue;
                    Ship lead = s_part[0];
                    Command leadCmd = lead.Commands?.CurConfirmedCommand;
                    bool leadAttack = leadCmd is IAttackCommand && !(leadCmd is DirectControlCommand);
                    int leadStamp = s_stamp.TryGetValue(lead, out int ls) ? ls : -1;
                    Ship target = leadAttack ? ((IAttackCommand)leadCmd).Target : null;
                    var key = (members[0], owner);
                    s_liveKeys.Add(key);
                    bool had = s_lastTargetOf.TryGetValue(key, out Ship wasTarget);
                    if ((had || leadAttack) && (!had || !ReferenceEquals(wasTarget, target)))
                    {
                        s_lastTargetOf[key] = target;
                        Log.Info(leadAttack
                            ? $"Assembly orders: {s_part.Count} ships of player {owner} follow the lead ship {lead.UniqueID}'s attack order (target {target?.UniqueID}), except segments given their own newer attack."
                            : $"Assembly orders: {s_part.Count} ships of player {owner} (lead {lead.UniqueID}) have no shared attack order -- each weapon reads its own ship's command.");
                    }
                    if (!leadAttack) continue;
                    for (int i = 1; i < s_part.Count; i++)
                    {
                        Ship m = s_part[i];
                        Command own = m.Commands?.CurConfirmedCommand;
                        bool ownAttack = own is IAttackCommand && !(own is DirectControlCommand);
                        int ownStamp = s_stamp.TryGetValue(m, out int os) ? os : -1;
                        if (AssemblyGraph.FollowsLead(true, leadStamp, ownAttack, ownStamp)) s_nextHolders[m] = lead;
                    }
                }
            }
            if (s_lastTargetOf.Count > s_liveKeys.Count)
            {
                var gone = new List<(Ship, int)>();
                foreach (var k in s_lastTargetOf.Keys) if (!s_liveKeys.Contains(k)) gone.Add(k);
                foreach (var k in gone) s_lastTargetOf.Remove(k);
            }
            Dictionary<Ship, Ship> cur = s_holders;
            bool same = cur.Count == s_nextHolders.Count;
            if (same)
                foreach (KeyValuePair<Ship, Ship> kv in s_nextHolders)
                    if (!cur.TryGetValue(kv.Key, out Ship h) || !ReferenceEquals(h, kv.Value)) { same = false; break; }
            if (!same) s_holders = s_nextHolders.Count == 0 ? s_noHolders : new Dictionary<Ship, Ship>(s_nextHolders);
            s_holdersTick = tick;
        }
    }

    internal static class AssemblyOrders
    {
        public static Command ForWeapons(CommandManager commands)
        {
            Command own = commands?.CurConfirmedCommand;
            if (own is DirectControlCommand) return own;
            Ship ship = commands?.Ship;
            if (!AssemblyIndex.TryGetHolder(ship, out Ship holder) || ReferenceEquals(holder, ship)) return own;
            Command theirs = holder.Commands?.CurConfirmedCommand;
            return AssemblyGraph.PickCommand(own, own is IAttackCommand, theirs, theirs is IAttackCommand);
        }
    }
}
