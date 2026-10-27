using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Cosmoteer.Crew;
using Cosmoteer.Data;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Crew;
using Cosmoteer.Ships.Resources;
using Halfling.Geometry;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class SeamHandover
    {
        internal sealed class Record
        {
            public Ship Owner;
            public IResourceSource Source;
            public IResourceSink Sink;
            public ID<ResourceRules> Type;
            public int Amount;
            public List<CrewSoul> Crew;
        }

        public static int Classify(bool endsAlive, bool unchanged, int carried) =>
            !endsAlive || unchanged ? 0 : carried > 0 ? 1 : 2;

        internal static List<Record> Capture(Ship ship)
        {
            var list = new List<Record>();
            try
            {
                if (ship?.Resources == null) return list;
                foreach (ResourceTransferJob job in ship.Resources.GetTransferJobs(autoJobs: true, manualJobs: false))
                {
                    if (job == null || job.IsManual || job.AssignedCrew.Count == 0 || job.Source?.Part == null || job.Sink?.Part == null) continue;
                    list.Add(new Record
                    {
                        Owner = ship,
                        Source = job.Source,
                        Sink = job.Sink,
                        Type = job.ResourceType,
                        Amount = Math.Max(1, job.ResourcesRequested.Confirmed),
                        Crew = new List<CrewSoul>(job.AssignedCrew),
                    });
                }
                list.Sort((x, y) =>
                {
                    int c = x.Sink.Part.Location.Y.CompareTo(y.Sink.Part.Location.Y);
                    if (c == 0) c = x.Sink.Part.Location.X.CompareTo(y.Sink.Part.Location.X);
                    if (c == 0) c = x.Source.Part.Location.Y.CompareTo(y.Source.Part.Location.Y);
                    if (c == 0) c = x.Source.Part.Location.X.CompareTo(y.Source.Part.Location.X);
                    return c;
                });
            }
            catch (Exception ex) { Log.Exception("SeamHandover.Capture", ex); }
            return list;
        }

        internal static void Apply(List<Record> records, string what)
        {
            if (records == null || records.Count == 0) return;
            int delivered = 0, walkers = 0, jobs = 0;
            foreach (Record r in records)
            {
                try
                {
                    Ship srcShip = r.Source.Part?.Ship, sinkShip = r.Sink.Part?.Ship;
                    bool alive = srcShip != null && sinkShip != null && srcShip.Sim != null;
                    ResourceTransferJob manual = null;
                    foreach (CrewSoul soul in r.Crew)
                    {
                        if (soul?.Sim == null) continue;
                        int carried = soul.CarriedResource.QuantityOfType(r.Type);
                        int action = Classify(alive, ReferenceEquals(srcShip, r.Owner) && ReferenceEquals(sinkShip, r.Owner), carried);
                        if (action == 0) continue;
                        if (action == 1)
                        {
                            Ship home = soul.HomeShip ?? soul.CurrentShip;
                            if (home?.Resources == null) continue;
                            if (soul.Job is ResourceTransferJob cur && cur.IsManual && ReferenceEquals(cur.Sink, r.Sink)) continue;
                            soul.Job?.UnassignCrew(soul);
                            ShipMerger.EnsureJobIdsAbove(home);
                            home.Resources.CarriedResourceTransfer(soul, r.Sink, Math.Min(carried, soul.CarriedResource.Quantity));
                            delivered++;
                            continue;
                        }
                        if (manual == null)
                        {
                            ShipMerger.EnsureJobIdsAbove(sinkShip);
                            manual = sinkShip.Resources.ManualResourceTransfer(r.Source, r.Sink, r.Amount, sticky: false, TradeType.None, 0,
                                autoCancel: false, confirmed: true, sinkShip.Jobs.AllocGlobalJobID());
                            jobs++;
                        }
                        if (manual == null || ReferenceEquals(soul.Job, manual)) continue;
                        soul.Job?.UnassignCrew(soul);
                        manual.AssignCrew(soul);
                        walkers++;
                    }
                }
                catch (Exception ex) { Log.Exception("SeamHandover.Apply", ex); }
            }
            if (delivered > 0 || walkers > 0)
                Log.Info($"{what}: supply jobs that left their ship handed over -- {delivered} carrier(s) deliver to the same part, {walkers} crew on their way to a pickup keep it ({jobs} manual job(s)).");
        }
    }

    internal static class SeamReach
    {
        public static bool Linked(Ship a, Ship b, Part atA, Part atB)
        {
            if (a == null || b == null || ReferenceEquals(a, b)) return false;
            if (SurgeryKin.Are(a, b)) return true;
            return atA != null && atB != null && CorridorHinge.SameWorkGroup(a, b) && CorridorHinge.CanWalkTo(atA, b) && CorridorHinge.CanWalkTo(atB, a);
        }

        public static bool Contig(Part part, IntVector2 cell)
        {
            if (Airlock.HasContiguousWith(part, cell)) return true;
            Ship s = part?.Ship;
            return s != null && (SurgeryKin.GraceApplies(SurgeryKin.InFamily(s), SurgeryKin.t_holding) || CorridorHinge.CanWalkToAnyCorridor(part));
        }

        private static bool s_applied;
        public static string Summary => "SeamReach: cross-ship resource reach " + (s_applied ? "patched" : "NOT patched") + " (IResourceSource.IsSinkReachable postfix: corridor groups and split / merge partners)";

        internal static void TryApply(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(IResourceSource), nameof(IResourceSource.IsSinkReachable));
            MethodInfo post = AccessTools.Method(typeof(SeamReach), nameof(ReachPostfix));
            if (target == null || post == null) return;
            harmony.Patch(target, postfix: new HarmonyMethod(post));
            s_applied = true;
        }

        private static void ReachPostfix(IResourceSource __instance, IResourceSink sink, bool allowDifferentShips, ref bool __result)
        {
            if (__result || !allowDifferentShips) return;
            try
            {
                Part a = __instance?.Part, b = sink?.Part;
                if (Linked(a?.Ship, b?.Ship, a, b)) __result = true;
            }
            catch (Exception ex) { LogThrottle.Exception("SeamReach.ReachPostfix", ex); }
        }
    }

    internal static class SeamSource
    {
        public static bool ManualSourceOk(bool vanilla, bool autoValid, bool priorityOk, bool linked) =>
            vanilla || (autoValid && priorityOk && linked);

        public static bool ManualSourceOk(IResourceSource source, ResourceTransferJob job)
        {
            bool vanilla = source.IsValidManualResourceSource;
            if (vanilla) return true;
            try
            {
                if (job == null || job.TradeType != TradeType.None || job.Sink == null) return false;
                Ship a = source.Part?.Ship, b = job.Sink.Part?.Ship ?? job.Sink.Ship;
                bool linked = a != null && b != null && (ReferenceEquals(a, b) || OneAssembly(a, b) || SurgeryKin.Are(a, b));
                if (!linked) return false;
                return ManualSourceOk(false, source.IsValidResourceSource, job.Sink.CheckPriority(source, false, MPValueType.Confirmed), true);
            }
            catch
            {
                return false;
            }
        }

        private static bool OneAssembly(Ship a, Ship b)
        {
            Ship[] m = AssemblyIndex.AssemblyOf(a);
            return m != null && Array.IndexOf(m, b) >= 0;
        }
    }

    [HarmonyPatch]
    internal static class SeamSourcePatch
    {
        public const int EXPECTED = 1;
        private static readonly Dictionary<MethodBase, int> s_sites = new Dictionary<MethodBase, int>();
        public static string Summary => $"SeamSource: {s_sites.Values.Sum()}/{EXPECTED} manual source test(s) patched (engine supply sources count inside one assembly)";

        static MethodBase TargetMethod() => AccessTools.Method(typeof(ResourceTransferJob), "IsSourceValid");

        private static bool Prepare(MethodBase original) => original != null || TargetMethod() != null;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var list = new List<CodeInstruction>(instructions);
            MethodInfo from = AccessTools.PropertyGetter(typeof(IResourceSource), nameof(IResourceSource.IsValidManualResourceSource));
            MethodInfo to = AccessTools.Method(typeof(SeamSource), nameof(SeamSource.ManualSourceOk), new[] { typeof(IResourceSource), typeof(ResourceTransferJob) });
            int hits = 0;
            for (int i = 0; i < list.Count; i++)
            {
                CodeInstruction ci = list[i];
                if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Equals(ci.operand, from) && to != null)
                {
                    list.Insert(i, new CodeInstruction(OpCodes.Ldarg_0) { labels = ci.labels, blocks = ci.blocks });
                    ci.labels = new List<Label>();
                    ci.blocks = new List<ExceptionBlock>();
                    i++;
                    list[i] = new CodeInstruction(OpCodes.Call, to);
                    hits++;
                }
            }
            if (original != null) s_sites[original] = hits;
            return list;
        }
    }

    [HarmonyPatch(typeof(ResourceTransferJob), nameof(ResourceTransferJob.IsValidFor))]
    internal static class SeamContigPatch
    {
        public const int EXPECTED = 1;
        private static readonly Dictionary<MethodBase, int> s_sites = new Dictionary<MethodBase, int>();
        public static string Summary => $"SeamReach: {s_sites.Values.Sum()}/{EXPECTED} manual delivery airlock test(s) patched (a walkable corridor hinge counts)";

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var list = new List<CodeInstruction>(instructions);
            MethodInfo from = AccessTools.Method(typeof(Airlock), nameof(Airlock.HasContiguousWith), new[] { typeof(Part), typeof(IntVector2) });
            MethodInfo to = AccessTools.Method(typeof(SeamReach), nameof(SeamReach.Contig));
            int hits = 0;
            for (int i = 0; i < list.Count; i++)
            {
                CodeInstruction ci = list[i];
                if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && Equals(ci.operand, from) && to != null)
                {
                    list[i] = new CodeInstruction(OpCodes.Call, to) { labels = ci.labels, blocks = ci.blocks };
                    hits++;
                }
            }
            if (original != null) s_sites[original] = hits;
            return list;
        }
    }
}
