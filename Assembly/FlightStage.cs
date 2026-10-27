using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Simulation;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class FlightStage
    {
        public const int THRUST_BUCKET = -10;

        public static readonly int SERVO_HOLD_TICKS = SimTicks.WindowFor(250f, SimTicks.DEFAULT_TICK_SECONDS);

        public static readonly int DESIRE_FRESH_TICKS = SimTicks.WindowFor(350f, SimTicks.DEFAULT_TICK_SECONDS);

        internal sealed class Plan
        {
            public int Tick = SimTicks.NEVER;
            public int Version = -1;
            public readonly Dictionary<Ship, Ship> PilotOf = new Dictionary<Ship, Ship>();
            public readonly HashSet<Ship> ServoOwned = new HashSet<Ship>();
            public readonly Dictionary<Ship, AssemblyEcho.Desire> Desire = new Dictionary<Ship, AssemblyEcho.Desire>();
            public readonly Dictionary<Ship, Ship> TrailerHead = new Dictionary<Ship, Ship>();
        }

        private sealed class Holder
        {
            public volatile Plan Plan = new Plan();
        }

        private static readonly ConditionalWeakTable<SimRoot, Holder> s_holders = new ConditionalWeakTable<SimRoot, Holder>();

        public static bool Owned(int now, int lastServoRun, int window)
        {
            return SimTicks.IsFresh(now, lastServoRun, window);
        }

        public static void Build(SimRoot sim)
        {
            if (sim == null) return;
            long started = Stopwatch.GetTimestamp();
            Holder holder = s_holders.GetValue(sim, _ => new Holder());
            Plan prev = holder.Plan;
            var plan = new Plan { Tick = sim.Tick, Version = HingeJointManager.TopologyVersion };
            if (HingeJointManager.ActivePinCount > 0)
            {
                var seeds = new List<Ship>();
                HingeJointManager.CollectJointedShips(seeds, sim);
                var cluster = new List<Ship>();
                AssemblyGraph groups = AssemblyTopology.GroupsOf(sim);
                foreach (Ship seed in seeds)
                {
                    if (seed == null) continue;
                    AssemblyEcho.Desire d = AssemblyEcho.Latest(seed);
                    if (d != null) plan.Desire[seed] = d;
                    if (plan.PilotOf.ContainsKey(seed)) continue;
                    HingeJointManager.GetRigidClusterSorted(seed, cluster);
                    if (cluster.Count <= 1) continue;
                    Ship pilot = HingeJointManager.PilotOfCluster(cluster, groups) ?? cluster[0];
                    foreach (Ship m in cluster) plan.PilotOf[m] = pilot;
                    if (!prev.PilotOf.TryGetValue(pilot, out Ship before) || !ReferenceEquals(before, pilot))
                        Log.Info($"Assembly pilot -> ship {pilot.UniqueID} ({cluster.Count}-ship assembly).");

                    if (ServoEligible(pilot) && Owned(plan.Tick, AssemblyServo.LastRunTick(pilot), SERVO_HOLD_TICKS))
                        foreach (Ship m in cluster)
                            if (!ReferenceEquals(m, pilot)) plan.ServoOwned.Add(m);
                }
                PickTrailerHeads(plan, seeds);
                RememberTops(groups, seeds);
                if (plan.Tick % DOCK_PROBE_TICKS == 0) ProbeDocks(sim, plan);
            }
            holder.Plan = plan;
            if (plan.TrailerHead.Count > 0) FlightProbe.OnPlan(Stopwatch.GetTimestamp() - started, plan.TrailerHead.Count);
            FlightProbe.Emit(sim);
        }

        private static void PickTrailerHeads(Plan plan, List<Ship> seeds)
        {
            var members = new List<Ship>();
            var done = new HashSet<Ship>();
            var fliers = new List<(ulong Id, bool HasDesire)>();
            var byId = new Dictionary<ulong, Ship>();
            foreach (Ship seed in seeds)
            {
                if (seed == null || done.Contains(seed)) continue;
                HingeJointManager.GetAssemblyShips(seed, members, rigidOnly: false);
                fliers.Clear();
                byId.Clear();
                foreach (Ship m in members)
                {
                    if (m == null) continue;
                    done.Add(m);
                    if (m.Physics?.Body == null || !FliesOwnOrder(m, m.Commands?.CurConfirmedCommand)) continue;
                    bool fresh = plan.Desire.TryGetValue(m, out AssemblyEcho.Desire d) && SimTicks.IsFresh(plan.Tick, d.StampTick, DESIRE_FRESH_TICKS);
                    ulong id = AssemblyTopology.Id(m);
                    fliers.Add((id, fresh));
                    byId[id] = m;
                }
                if (fliers.Count == 0) continue;
                foreach (Ship m in members)
                {
                    if (m == null) continue;
                    ulong head = PickHead(fliers, AssemblyTopology.Id(m));
                    if (head != 0 && byId.TryGetValue(head, out Ship h)) plan.TrailerHead[m] = h;
                }
            }
        }

        public static ulong PickHead(IReadOnlyList<(ulong Id, bool HasDesire)> fliers, ulong self)
        {
            ulong first = 0;
            foreach ((ulong id, bool hasDesire) in fliers)
            {
                if (id == self) continue;
                if (hasDesire) return id;
                if (first == 0) first = id;
            }
            return first;
        }

        public static bool TryGetTrailerHead(Ship trailer, out Ship head, out AssemblyEcho.Desire desire)
        {
            head = null;
            desire = null;
            Plan p = Current(trailer);
            if (p == null) return false;
            if (p.TrailerHead.TryGetValue(trailer, out head)) desire = DesireOf(head);
            return true;
        }

        private static void RememberTops(AssemblyGraph groups, List<Ship> seeds)
        {
            foreach (ulong anchor in groups.Anchors)
            {
                if (groups.TopsOf(anchor).Count < 2) continue;
                Ship top = null;
                foreach (Ship s in seeds)
                    if (s != null && AssemblyTopology.Id(s) == anchor) { top = s; break; }
                if (top == null || HingeJointManager.HasTopMark(top)) continue;
                if (HingeJointManager.MarkTop(top))
                    Log.Info($"Assembly top remembered: ship {top.UniqueID} of {groups.TopsOf(anchor).Count} tops holds the orders.");
            }
        }

        private const int DOCK_PROBE_TICKS = 60;
        private static readonly List<(object Tag, Ship A, Ship B, bool Separate)> s_dockPins = new List<(object, Ship, Ship, bool)>();

        private static void ProbeDocks(SimRoot sim, Plan plan)
        {
            try
            {
                HingeJointManager.CollectDockPins(s_dockPins, sim);
                foreach (var (tag, a, b, separate) in s_dockPins)
                {
                    if (a == null || b == null) continue;
                    plan.PilotOf.TryGetValue(a, out Ship pilot);
                    string where = (tag as Cosmoteer.Ships.Parts.PartComponent)?.Part?.Location.ToString() ?? "?";
                    Log.Info($"Dock flight probe at {where}: {(separate ? "separate" : "joined")}, pilot {pilot?.UniqueID.ToString() ?? "none"}"
                        + $" (servo eligible {(pilot != null && ServoEligible(pilot))}) | A {Describe(a, pilot, plan)} | B {Describe(b, pilot, plan)}.");
                }
            }
            catch (Exception ex)
            {
                Log.Exception("FlightStage.ProbeDocks", ex);
            }
        }

        private static string Describe(Ship s, Ship pilot, Plan plan)
        {
            int total = 0, soft = 0;
            float level = 0f;
            if (s.Thrusters != null)
                foreach (Cosmoteer.Ships.Parts.Thrusters.Thruster t in s.Thrusters)
                {
                    total++;
                    if (t.IsSoftOperational) soft++;
                    level += Math.Abs(t.ActivationLevel);
                }
            Command cmd = s.Commands?.CurConfirmedCommand;
            return $"{s.UniqueID}{(ReferenceEquals(s, pilot) ? " (pilot)" : "")} servo-owned {plan.ServoOwned.Contains(s)}, order {cmd?.GetType().Name ?? "none"},"
                + $" can command {s.Commands?.CanExecuteCommands}, thrusters {soft}/{total} usable, total level {level:0.00}";
        }

        private static bool ServoEligible(Ship pilot)
        {
            if (!AssemblyFlightFeatures.Enabled || pilot?.Physics?.Body == null) return false;
            CommandManager cm = pilot.Commands;
            if (cm == null || !cm.CanExecuteCommands) return false;
            Command cmd = cm.CurConfirmedCommand;
            return cmd != null && cmd.IsValid() && (cmd is MoveCommand || cmd is BaseFollowCommand) && FliesOwnOrder(pilot, cmd);
        }

        public static bool FliesOwnOrder(Ship ship, Command cmd)
        {
            if (cmd == null) return false;
            if (!(cmd is IAttackCommand) || cmd is DirectControlCommand) return true;
            AssemblyGraph groups = AssemblyTopology.GroupsOf(ship?.Sim);
            ulong id = AssemblyTopology.Id(ship);
            return !groups.TryGetAnchor(id, out ulong anchor) || anchor == id;
        }

        private static Plan Current(Ship s)
        {
            SimRoot sim = s?.Sim;
            if (sim == null || !s_holders.TryGetValue(sim, out Holder holder)) return null;
            Plan p = holder.Plan;
            return p.Tick == sim.Tick ? p : null;
        }

        public static bool TryGetPilot(Ship member, out Ship pilot)
        {
            pilot = null;
            Plan p = Current(member);
            if (p == null || member.Sim.FixedUpdater.CurrentBucket != THRUST_BUCKET) return false;
            return p.Version == HingeJointManager.TopologyVersion && p.PilotOf.TryGetValue(member, out pilot) && pilot != null;
        }

        public static bool IsServoOwned(Ship member)
        {
            Plan p = Current(member);
            return p != null && p.ServoOwned.Contains(member);
        }

        public static AssemblyEcho.Desire DesireOf(Ship ship)
        {
            Plan p = Current(ship);
            if (p == null || !p.Desire.TryGetValue(ship, out AssemblyEcho.Desire d)) return null;
            return SimTicks.IsFresh(p.Tick, d.StampTick, DESIRE_FRESH_TICKS) ? d : null;
        }
    }

    [HarmonyPatch(typeof(SimRoot), "FixedUpdateForBucket")]
    internal static class FlightStagePatch
    {
        static void Prefix(SimRoot __instance, int bucket)
        {
            if (bucket != FlightStage.THRUST_BUCKET) return;
            try
            {
                FlightStage.Build(__instance);
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("FlightStage.Build", ex);
            }
        }
    }

    internal static class ThrusterKeepAlive
    {
        public static void Enable(Ship ship)
        {
            var tm = ship?.Thrusters;
            if (tm == null || tm.FixedUpdatingEnabled) return;
            SimRoot sim = ship.Sim;
            if (sim == null)
            {
                tm.FixedUpdatingEnabled = true;
                return;
            }
            sim.EnqueueDeterministic(ship.UniqueID, () =>
            {
                if (ship.Thrusters != null) ship.Thrusters.FixedUpdatingEnabled = true;
            });
        }
    }
}
