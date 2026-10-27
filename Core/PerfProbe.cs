using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class PerfProbe
    {
        private const bool ENABLED = false;
        private const bool LIGHT = true;
        private static readonly (string Type, string[] Methods)[] s_lightTargets =
        {
            ("ZTX.HingesPistonsDocking.ShipMerger", new[] { "Merge" }),
            ("ZTX.HingesPistonsDocking.ShipSplitter", new[] { "Split" }),
            ("ZTX.HingesPistonsDocking.HingeShipManager", new[] { "SpawnRod", "RemoveRod" }),
            ("ZTX.HingesPistonsDocking.LoadRelinkPatch", new[] { "Postfix" }),
            ("ZTX.HingesPistonsDocking.Log", new[] { "Info" }),
        };
        private const double SPIKE_MS = 50.0;
        private const int MAX_SPIKES_PER_WINDOW = 10;
        private const int TOP_SPIKE = 8;
        private const double WINDOW_SECONDS = 5.0;
        private const int TOP_N = 14;

        private static readonly (string Type, string[] Methods)[] s_engineTargets =
        {
            ("Cosmoteer.Game.GameRoot", new[] { "Update", "Draw" }),
            ("Cosmoteer.Simulation.SimRoot", new[] { "Update", "Draw", "UpdatePhysics" }),
            ("Cosmoteer.Ships.Rendering.ShipRenderer", new[] { "DrawStage", "RefreshIcon", "RefreshGeometry" }),
            ("Cosmoteer.Ships.Blueprints.Graphics.BlueprintRenderer",
                new[] { "DrawBlueprints", "DrawBlueprintsAt", "DrawProhibitedZones" }),
            ("Cosmoteer.Game.Gui.Build.BuildToolbox", new[] { "OnUpdatingUIState", "OnDrawOverlays",
                "OnLiveModeUpdatingUIState", "OnBlueprintModeUpdatingUIState", "RequiresBlueprintsMode" }),
            ("Cosmoteer.Game.Gui.Build.Stats.BuildToolboxStatsGui", new[] { "Update" }),
            ("Cosmoteer.Game.Gui.GameGui", new[] { "Draw", "Input" }),
            ("Halfling.Graphics.RenderTrees.RenderTreeRoot", new[] { "Draw" }),
            ("ZTX.HingesPistonsDocking.HingeShipManager", new[] { "FixedUpdate" }),
            ("ZTX.HingesPistonsDocking.MergeService", new[] { "Service", "TryMapHomeCell", "TryGetMergedTipCells", "IsHeld" }),
            ("ZTX.HingesPistonsDocking.ZtxRodCellGateComponent", new[] { "Tick" }),
            ("ZTX.HingesPistonsDocking.LogicShipManager", new[] { "FixedUpdate" }),
            ("ZTX.HingesPistonsDocking.RailShipManager", new[] { "FixedUpdate" }),
            ("ZTX.HingesPistonsDocking.AssemblyIndex", new[] { "Tick", "Rebuild" }),
            ("ZTX.HingesPistonsDocking.AssemblyVerdict", new[] { "JudgeCuts" }),
            ("ZTX.HingesPistonsDocking.DrivePool", new[] { "Rebalance" }),
            ("ZTX.HingesPistonsDocking.JumpFuel", new[] { "ClearCaches" }),
            ("ZTX.HingesPistonsDocking.SurgeryKin", new[] { "Prune" }),
            ("ZTX.HingesPistonsDocking.CorridorHinge", new[] { "Publish" }),
            ("ZTX.HingesPistonsDocking.AssemblySupply", new[] { "Tick" }),
            ("ZTX.HingesPistonsDocking.CrewTargets", new[] { "Sync" }),
            ("ZTX.HingesPistonsDocking.HingeJointManager", new[] { "SnapChains", "ComputeLoads", "KeepJointAnglesContinuous", "OnAfterPhysics" }),
            ("ZTX.HingesPistonsDocking.HingeShipManager", new[] { "SpawnRod", "RemoveRod" }),
            ("ZTX.HingesPistonsDocking.DockShipManager", new[] { "FixedUpdate" }),
            ("ZTX.HingesPistonsDocking.ZtxCorridorLinkGateComponent", new[] { "Tick" }),
            ("ZTX.HingesPistonsDocking.ShipSplitter", new[] { "Split" }),
            ("ZTX.HingesPistonsDocking.ShipMerger", new[] { "Merge" }),
            ("ZTX.HingesPistonsDocking.LoadRelinkPatch", new[] { "Postfix" }),
            ("ZTX.HingesPistonsDocking.Log", new[] { "Info" }),
            ("ZTX.HingesPistonsDocking.ShipMerger", new[] { "RetargetAttacks", "TransferTileStatuses", "RetargetDestinations", "MergeRolesInto",
                "CopyPartMetadata", "RekeyCollidingManualJobs", "MigrateManualJobs", "InvokeSplitEvent" }),
            ("ZTX.HingesPistonsDocking.ShipSplitter", new[] { "AttachOrphans", "FlushGeometry", "SplitDecals", "InvokeSplitEvent" }),
            ("ZTX.HingesPistonsDocking.ShipRecenter", new[] { "Regrid" }),
            ("ZTX.HingesPistonsDocking.MergeService", new[] { "TryMerge", "TryUnmerge" }),
            ("ZTX.HingesPistonsDocking.SeamHandover", new[] { "Capture", "Apply" }),
            ("ZTX.HingesPistonsDocking.AssemblyCrewHome", new[] { "RebindBunks", "BunkSides" }),
            ("ZTX.HingesPistonsDocking.PendingRepairs", new[] { "Restore", "MatchKeeping", "RemoveWrecks" }),
            ("ZTX.HingesPistonsDocking.CrewTargets", new[] { "Touched" }),
            ("ZTX.HingesPistonsDocking.CorridorHinge", new[] { "CanWalkTo", "Reaches", "HasRealHatch", "PickExit" }),
        };

        private static readonly (string Type, string[] Methods)[] s_modShimTargets =
        {
            ("ZTX.HingesPistonsDocking.AssemblyAi", new[] { "FirepowerOf", "ShipWideOfShip" }),
            ("ZTX.HingesPistonsDocking.AssemblyCrew", new[] { "SearchOrigin" }),
            ("ZTX.HingesPistonsDocking.AssemblyCrewGate", new[] { "CanExecute" }),
            ("ZTX.HingesPistonsDocking.AssemblyCrewPool", new[] { "CurrentShipFor", "ExitAllowed", "HomeFor", "PathOk" }),
            ("ZTX.HingesPistonsDocking.AssemblyJobs", new[] { "IsEnemiesWith", "SalvageJobs", "TargetAttackableBy", "TransferJobs" }),
            ("ZTX.HingesPistonsDocking.AssemblyModes", new[] { "ArenaPoints", "SignificantShip", "ValueForArena", "ValueForCapture" }),
            ("ZTX.HingesPistonsDocking.AssemblyOrders", new[] { "ForWeapons" }),
            ("ZTX.HingesPistonsDocking.AssemblyReach", new[] { "RadiusForCommands", "ShipHasSalvageWeapons" }),
            ("ZTX.HingesPistonsDocking.AssemblyStorage", new[] { "SinkShips" }),
            ("ZTX.HingesPistonsDocking.AssemblyTargetingFanout", new[] { "AnyDisplayedAcross", "ShipWideAcross" }),
            ("ZTX.HingesPistonsDocking.ClaimMap", new[] { "CanApply" }),
            ("ZTX.HingesPistonsDocking.CorridorHinge", new[] { "EntryAirlock", "ExitAirlock", "PickExit", "RealAll", "RealNearest", "RealNearestPart" }),
            ("ZTX.HingesPistonsDocking.CrewTargets", new[] { "TargetUnderCursor" }),
            ("ZTX.HingesPistonsDocking.JumpAnim", new[] { "JumpInDelay" }),
            ("ZTX.HingesPistonsDocking.SeamReach", new[] { "Contig", "ReachPostfix" }),
            ("ZTX.HingesPistonsDocking.SeamSource", new[] { "ManualSourceOk" }),
            ("ZTX.HingesPistonsDocking.UiFanout", new[] { "MatchingAcross" }),
        };
        private static readonly HashSet<MethodBase> s_modTargets = new HashSet<MethodBase>();
        private const int TOP_MOD = 8;

        private static readonly (string BaseType, string Method)[] s_overrideTargets =
        {
            ("Cosmoteer.Modes.GameModeManager", "Draw"),
            ("Cosmoteer.Game.SelectionStates.BaseSelectionState", "DrawGuiOverlay"),
        };

        private static readonly string[] s_frameFields =
        {
            "e_beforeDraw", "e_beforeDrawDescendants", "e_afterDraw", "e_afterDrawDescendants",
            "e_beforeInput", "e_beforeInputDescendants", "e_afterInput", "e_afterInputDescendants",
            "BeforeFrameDraw", "AfterFrameDraw", "BeforeFrameInput", "AfterFrameInput",
        };
        private const int MAX_HANDLERS = 600;

        private sealed class Stat
        {
            public string Name;
            public string Group;
            public long Ticks;
            public long Calls;
            public long MaxTicks;
            public long FrameTicks;
            public long FrameCalls;
            public long AllocBytes;
            public bool Mod;
        }

        private static long s_gcAllocStart;
        private static int s_gc0, s_gc1, s_gc2, s_fgc0, s_fgc1, s_fgc2;
        private static TimeSpan s_gcPause, s_fgcPause;
        private const int TOP_ALLOC = 6;

        private static readonly (string Label, string StatName)[] s_events =
        {
            ("merges", "ShipMerger.Merge/4"),
            ("splits", "ShipSplitter.Split/3"),
            ("rodSpawns", "HingeShipManager.SpawnRod/4"),
            ("rodRemovals", "HingeShipManager.RemoveRod/2"),
            ("loads", "LoadRelinkPatch.Postfix/1"),
            ("logLines", "Log.Info/1"),
        };

        private static int s_spikesThisWindow;
        private static int s_spikesLogged;

        private static readonly ConcurrentDictionary<MethodBase, Stat> s_stats =
            new ConcurrentDictionary<MethodBase, Stat>();
        private static readonly List<Ship> s_scratch = new List<Ship>();

        private static long s_windowStart;
        private static long s_lastFrame;
        private static long s_frames;
        private static long s_frameTicksMax;
        private static bool s_buildMode;
        private static bool s_failed;
        private static Harmony s_harmony;
        private static HarmonyMethod s_pre, s_post;
        private static readonly HashSet<MethodBase> s_wrapped = new HashSet<MethodBase>();
        private static readonly Dictionary<MethodBase, string> s_handlerGroups = new Dictionary<MethodBase, string>();

        public static void Install(Harmony harmony)
        {
            if (!ENABLED) return;
            try
            {
                var targets = LIGHT ? new HashSet<MethodBase>() : new HashSet<MethodBase>(harmony.GetPatchedMethods());
                foreach (var (typeName, methods) in LIGHT ? s_lightTargets : s_engineTargets)
                {
                    Type t = AccessTools.TypeByName(typeName);
                    if (t == null) { Log.Info("PerfProbe: type not found " + typeName); continue; }
                    foreach (string name in methods)
                    {
                        var found = t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
                                                 | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                                     .Where(m => m.Name == name && !m.IsAbstract && !m.ContainsGenericParameters)
                                     .ToList();
                        if (found.Count == 0) Log.Info("PerfProbe: method not found " + typeName + "." + name);
                        foreach (MethodInfo m in found) targets.Add(m);
                    }
                }
                foreach (var (typeName, methods) in LIGHT ? Array.Empty<(string, string[])>() : s_modShimTargets)
                {
                    Type t = AccessTools.TypeByName(typeName);
                    if (t == null) { Log.Info("PerfProbe: type not found " + typeName); continue; }
                    foreach (string name in methods)
                        foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
                                                              | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                                                   .Where(m => m.Name == name && !m.IsAbstract && !m.ContainsGenericParameters))
                        {
                            targets.Add(m);
                            s_modTargets.Add(m);
                        }
                }
                foreach (Type t in LIGHT ? Array.Empty<Type>() : SafeTypes(typeof(PerfProbe).Assembly))
                {
                    if (t == null || t.ContainsGenericParameters || t.DeclaringType == typeof(PerfProbe) || t == typeof(PerfProbe)) continue;
                    if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
                    foreach (MethodInfo m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (m.ContainsGenericParameters || (m.Name != "Prefix" && m.Name != "Postfix" && m.Name != "Finalizer")) continue;
                        targets.Add(m);
                        s_modTargets.Add(m);
                    }
                }
                foreach (var (baseName, name) in LIGHT ? Array.Empty<(string, string)>() : s_overrideTargets)
                {
                    Type bt = AccessTools.TypeByName(baseName);
                    if (bt == null) { Log.Info("PerfProbe: type not found " + baseName); continue; }
                    foreach (Type t in SafeTypes(bt.Assembly))
                    {
                        if (!bt.IsAssignableFrom(t) || t.ContainsGenericParameters) continue;
                        MethodInfo m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                                                          | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                        if (m != null && !m.IsAbstract) targets.Add(m);
                    }
                }
                s_harmony = harmony;
                s_pre = new HarmonyMethod(AccessTools.Method(typeof(PerfProbe), nameof(Begin))) { priority = Priority.First };
                s_post = new HarmonyMethod(AccessTools.Method(typeof(PerfProbe), nameof(End))) { priority = Priority.Last };
                int ok = 0;
                foreach (MethodBase m in targets)
                    if (Wrap(m)) ok++;
                s_windowStart = Stopwatch.GetTimestamp();
                Log.Info($"PerfProbe: timing {ok} of {targets.Count} methods; a PERF line every {WINDOW_SECONDS:0}s while a joint exists or build mode is open.");
            }
            catch (Exception ex)
            {
                Log.Exception("PerfProbe.Install", ex);
            }
        }

        private static bool Wrap(MethodBase m)
        {
            if (!s_wrapped.Add(m)) return false;
            try
            {
                s_harmony.Patch(m, prefix: s_pre, postfix: s_post);
                return true;
            }
            catch (Exception ex)
            {
                Log.Info("PerfProbe: could not wrap " + NameOf(m) + ": " + ex.Message);
                return false;
            }
        }

        private static IEnumerable<Type> SafeTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
        }

        private static void ScanGuiHandlers(object guiRoot)
        {
            if (guiRoot == null) return;
            long t0 = Stopwatch.GetTimestamp();
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var stack = new Stack<object>();
            stack.Push(guiRoot);
            int added = 0, found = 0;
            Assembly ours = typeof(PerfProbe).Assembly;
            while (stack.Count > 0 && s_handlerGroups.Count < MAX_HANDLERS)
            {
                object o = stack.Pop();
                if (o == null || !seen.Add(o)) continue;
                for (Type t = o.GetType(); t != null && t != typeof(object); t = t.BaseType)
                {
                    foreach (string fname in s_frameFields)
                    {
                        FieldInfo f = t.GetField(fname, BindingFlags.Instance | BindingFlags.NonPublic
                                                        | BindingFlags.Public | BindingFlags.DeclaredOnly);
                        if (f == null || !typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;
                        if (!(f.GetValue(o) is Delegate d)) continue;
                        foreach (Delegate h in d.GetInvocationList())
                        {
                            MethodInfo m = h.Method;
                            if (m == null) continue;
                            if (m.Name.StartsWith("OnParent") && m.DeclaringType?.Name == "Widget")
                            {
                                stack.Push(h.Target);
                                continue;
                            }
                            if (h.Target != null) stack.Push(h.Target);
                            found++;
                            if (s_handlerGroups.ContainsKey(m)) continue;
                            Type dt = m.DeclaringType;
                            if (dt == null || dt.ContainsGenericParameters || m.ContainsGenericParameters) continue;
                            string outer = dt.FullName ?? dt.Name;
                            int plus = outer.IndexOf('+');
                            if (plus > 0) outer = outer.Substring(0, plus);
                            outer = outer.Substring(outer.LastIndexOf('.') + 1);
                            s_handlerGroups[m] = (dt.Assembly == ours ? "ZTX " : "vanilla ") + outer;
                            if (Wrap(m)) added++;
                        }
                    }
                }
            }
            if (added > 0)
                Log.Info($"PerfProbe: GUI scan found {found} live per-frame handler subscriptions, timed {added} new methods ({s_handlerGroups.Count} total) in {(Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency:0} ms.");
        }

        private static void Begin(out (long T, long A) __state)
        {
            __state = (Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread());
        }

        private static void End((long T, long A) __state, MethodBase __originalMethod)
        {
            long dt = Stopwatch.GetTimestamp() - __state.T;
            long da = GC.GetAllocatedBytesForCurrentThread() - __state.A;
            Stat s = s_stats.GetOrAdd(__originalMethod, m => new Stat
            {
                Name = NameOf(m),
                Group = s_handlerGroups.TryGetValue(m, out string g) ? g : null,
                Mod = s_modTargets.Contains(m),
            });
            Interlocked.Add(ref s.Ticks, dt);
            Interlocked.Increment(ref s.Calls);
            Interlocked.Add(ref s.FrameTicks, dt);
            Interlocked.Increment(ref s.FrameCalls);
            if (da > 0) Interlocked.Add(ref s.AllocBytes, da);
            long max;
            while (dt > (max = Interlocked.Read(ref s.MaxTicks))
                   && Interlocked.CompareExchange(ref s.MaxTicks, dt, max) != max) { }
        }

        private static string NameOf(MethodBase m)
        {
            int n = m.GetParameters().Length;
            string type = m.DeclaringType?.FullName ?? m.DeclaringType?.Name ?? "?";
            type = type.Substring(type.LastIndexOf('.') + 1);
            return type + "." + m.Name + (n > 0 ? "/" + n : "");
        }

        public static bool SpikeWanted(long frameTicks, long thresholdTicks, int spikesThisWindow, int maxPerWindow) =>
            frameTicks > thresholdTicks && spikesThisWindow < maxPerWindow;

        private static Stat Find(string name)
        {
            foreach (Stat s in s_stats.Values)
                if (s.Name == name) return s;
            return null;
        }

        private static string EventsText(bool frame)
        {
            var sb = new StringBuilder();
            foreach (var (label, statName) in s_events)
            {
                Stat s = Find(statName);
                long n = s == null ? 0 : (frame ? Interlocked.Read(ref s.FrameCalls) : Interlocked.Read(ref s.Calls));
                sb.Append($" {label}={n}");
            }
            return sb.ToString();
        }

        private static void LogSpike(long frameTicks)
        {
            double tickMs = 1000.0 / Stopwatch.Frequency;
            var sb = new StringBuilder();
            sb.Append($"SPIKE frame {frameTicks * tickMs:0.0} ms joints={HingeJointManager.ActivePinCount} |");
            foreach (Stat s in s_stats.Values.Where(x => x.FrameCalls > 0).OrderByDescending(x => x.FrameTicks).Take(TOP_SPIKE))
                sb.Append($" {s.Name} {s.FrameTicks * tickMs:0.00}ms x{s.FrameCalls};");
            sb.Append(" | events this frame:");
            sb.Append(EventsText(frame: true));
            sb.Append($" | gc this frame: gen0 +{GC.CollectionCount(0) - s_fgc0} gen1 +{GC.CollectionCount(1) - s_fgc1} gen2 +{GC.CollectionCount(2) - s_fgc2} pause {(GC.GetTotalPauseDuration() - s_fgcPause).TotalMilliseconds:0} ms");
            Log.Info(sb.ToString());
        }

        private static void ResetFrame()
        {
            s_fgc0 = GC.CollectionCount(0);
            s_fgc1 = GC.CollectionCount(1);
            s_fgc2 = GC.CollectionCount(2);
            s_fgcPause = GC.GetTotalPauseDuration();
            foreach (Stat s in s_stats.Values)
            {
                Interlocked.Exchange(ref s.FrameTicks, 0);
                Interlocked.Exchange(ref s.FrameCalls, 0);
            }
        }

        internal static void OnFrame(GameRoot game)
        {
            if (!ENABLED || s_failed) return;
            try
            {
                long now = Stopwatch.GetTimestamp();
                if (s_lastFrame != 0)
                {
                    long ft = now - s_lastFrame;
                    if (ft > s_frameTicksMax) s_frameTicksMax = ft;
                    s_frames++;
                    long spikeTicks = (long)(SPIKE_MS * Stopwatch.Frequency / 1000.0);
                    if (ft > spikeTicks) s_spikesLogged++;
                    if (SpikeWanted(ft, spikeTicks, s_spikesThisWindow, MAX_SPIKES_PER_WINDOW))
                    {
                        s_spikesThisWindow++;
                        LogSpike(ft);
                    }
                }
                ResetFrame();
                s_lastFrame = now;

                bool build = game?.Gui?.ShipGui?.BuildToolbox?.SelfActive ?? false;
                double secs = (now - s_windowStart) / (double)Stopwatch.Frequency;
                if (build != s_buildMode || secs >= WINDOW_SECONDS)
                {
                    Flush(game, now, secs, s_buildMode, build != s_buildMode);
                    s_buildMode = build;
                    if (!LIGHT && (build || s_handlerGroups.Count == 0))
                    {
                        ScanGuiHandlers(game?.Gui);
                        s_windowStart = s_lastFrame = Stopwatch.GetTimestamp();
                    }
                }
            }
            catch (Exception ex)
            {
                s_failed = true;
                Log.Exception("PerfProbe.OnFrame", ex);
            }
        }

        private static void Flush(GameRoot game, long now, double secs, bool build, bool toggled)
        {
            int joints = HingeJointManager.ActivePinCount;
            if ((joints > 0 || build) && s_frames > 0 && secs > 0.25)
            {
                int assembly = 0;
                Ship sel = game?.Sim?.PlayerInput?.GetSingleSelectedShip();
                if (sel != null)
                {
                    HingeJointManager.GetAssemblyShips(sel, s_scratch, rigidOnly: false);
                    assembly = s_scratch.Count;
                }
                double tickMs = 1000.0 / Stopwatch.Frequency;
                double frames = s_frames;
                var sb = new StringBuilder();
                sb.Append($"PERF {secs:0.0}s build={(build ? "ON" : "off")}{(toggled ? " (until toggle)" : "")} joints={joints} selectedAssembly={assembly}");
                sb.Append($" fps={frames / secs:0.0} frame avg {secs * 1000.0 / frames:0.00} ms max {s_frameTicksMax * tickMs:0.0} ms");
                int ships = 0, crew = 0, assemblies = 0, segments = 0;
                Cosmoteer.Simulation.SimRoot sim = game?.Sim;
                if (sim?.Ships != null)
                    foreach (Ship sh in sim.Ships)
                    {
                        ships++;
                        crew += sh?.Crew?.OnShip.Count ?? 0;
                    }
                if (sim != null)
                    foreach (Ship[] g in AssemblyIndex.FreshGroups(sim) ?? (IReadOnlyList<Ship[]>)Array.Empty<Ship[]>())
                        if (g != null && g.Length > 1) { assemblies++; segments += g.Length; }
                Stat logStat = Find("Log.Info/1");
                double logMs = logStat == null ? 0 : logStat.Ticks * tickMs;
                sb.Append($" ships={ships} assemblies={assemblies} segments={segments} crew={crew} spikes>{SPIKE_MS:0}ms={s_spikesLogged} |");
                sb.Append(EventsText(frame: false));
                sb.Append($" logMs={logMs:0.0} |");
                double allocMb = (GC.GetTotalAllocatedBytes(false) - s_gcAllocStart) / 1048576.0;
                sb.Append($" gc alloc {allocMb:0.0} MB ({allocMb / secs:0.0} MB/s) gen0 +{GC.CollectionCount(0) - s_gc0} gen1 +{GC.CollectionCount(1) - s_gc1} gen2 +{GC.CollectionCount(2) - s_gc2} pause {(GC.GetTotalPauseDuration() - s_gcPause).TotalMilliseconds:0} ms; alloc top:");
                foreach (Stat s in s_stats.Values.Where(x => x.AllocBytes > 0).OrderByDescending(x => x.AllocBytes).Take(TOP_ALLOC))
                    sb.Append($" {s.Name} {s.AllocBytes / 1048576.0:0.00} MB;");
                sb.Append(" |");
                var mods = s_stats.Values.Where(x => x.Mod && x.Calls > 0).ToList();
                double modMs = mods.Sum(x => x.Ticks) * tickMs, modMb = mods.Sum(x => x.AllocBytes) / 1048576.0;
                sb.Append($" mod code {modMs / frames:0.000} ms/f, alloc {modMb:0.00} MB ({modMb / secs:0.00} MB/s); mod alloc top:");
                foreach (Stat s in mods.Where(x => x.AllocBytes > 0).OrderByDescending(x => x.AllocBytes).Take(TOP_MOD))
                    sb.Append($" {s.Name} {s.AllocBytes / 1048576.0:0.00} MB x{s.Calls};");
                sb.Append(" |");
                sb.Append($" churn vitalSuppressed {AssemblyFacade.s_churnVitalSuppressed / frames:0.##}/f vitalChanged {AssemblyFacade.s_churnVitalChanged} statWrites {AssemblyFacade.s_churnStatWrites / frames:0.##}/f |");
                foreach (Stat s in s_stats.Values.Where(x => x.Calls > 0).OrderByDescending(x => x.Ticks).Take(TOP_N))
                    sb.Append($" {s.Name} {s.Ticks * tickMs / frames:0.000}ms/f x{s.Calls / frames:0.#} max {s.MaxTicks * tickMs:0.00};");
                var groups = s_stats.Values.Where(x => x.Group != null && x.Calls > 0)
                    .GroupBy(x => x.Group)
                    .Select(g => (Key: g.Key, Ms: g.Sum(x => x.Ticks) * tickMs / frames,
                                  Calls: g.Sum(x => x.Calls) / frames, N: g.Count()))
                    .OrderByDescending(x => x.Ms).ToList();
                if (groups.Count > 0)
                {
                    sb.Append($" || GUI handlers total {groups.Sum(x => x.Ms):0.000}ms/f:");
                    foreach (var g in groups.Take(TOP_N))
                        sb.Append($" {g.Key} {g.Ms:0.000}ms/f x{g.Calls:0.#} ({g.N} handlers);");
                }
                Log.Info(sb.ToString());
            }
            AssemblyFacade.s_churnVitalSuppressed = AssemblyFacade.s_churnVitalChanged = AssemblyFacade.s_churnStatWrites = 0;
            foreach (Stat s in s_stats.Values)
            {
                Interlocked.Exchange(ref s.Ticks, 0);
                Interlocked.Exchange(ref s.Calls, 0);
                Interlocked.Exchange(ref s.MaxTicks, 0);
                Interlocked.Exchange(ref s.AllocBytes, 0);
            }
            s_spikesThisWindow = 0;
            s_spikesLogged = 0;
            s_gcAllocStart = GC.GetTotalAllocatedBytes(false);
            s_gc0 = GC.CollectionCount(0);
            s_gc1 = GC.CollectionCount(1);
            s_gc2 = GC.CollectionCount(2);
            s_gcPause = GC.GetTotalPauseDuration();
            s_frames = 0;
            s_frameTicksMax = 0;
            s_windowStart = now;
        }

        [HarmonyPatch(typeof(GameRoot), "Draw")]
        private static class FramePatch
        {
            static void Prefix(GameRoot __instance) => OnFrame(__instance);
        }
    }
}
