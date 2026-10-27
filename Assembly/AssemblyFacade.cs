using System;
using System.Collections.Generic;
using System.Linq;
using Cosmoteer.Game.Gui.Build;
using Cosmoteer.Game.Gui.Build.Stats;
using Cosmoteer;
using Cosmoteer.Modes;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Crew;
using Cosmoteer.Ships.Rendering;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Overlays;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Gui;
using Cosmoteer.Localization;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class AssemblyFacade
    {
        public static bool Enabled = true;

        private static readonly List<Ship> s_scratch = new List<Ship>();

        internal static int s_churnVitalSuppressed, s_churnVitalChanged, s_churnStatWrites;

        [ThreadStatic] private static List<Ship> s_rootScratch;

        private sealed class RootMemo
        {
            public readonly Ship Root;
            public readonly int Tick;
            public readonly int Version;

            public RootMemo(Ship root, int tick, int version)
            {
                Root = root;
                Tick = tick;
                Version = version;
            }
        }

        private sealed class RootMemoBox { public volatile RootMemo Value; }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Ship, RootMemoBox> s_rootMemo =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Ship, RootMemoBox>();

        public static Ship RootOf(Ship ship)
        {
            if (!Enabled || ship == null) return null;
            int now = ship.Sim?.Tick ?? SimTicks.NEVER;
            int version = HingeJointManager.TopologyVersion;
            RootMemoBox box = s_rootMemo.GetValue(ship, _ => new RootMemoBox());
            RootMemo memo = box.Value;
            if (memo != null && memo.Version == version && now != SimTicks.NEVER && SimTicks.IsFresh(now, memo.Tick, 0)) return memo.Root;
            Ship root = ComputeRoot(ship);
            box.Value = new RootMemo(root, now, version);
            return root;
        }

        private static Ship ComputeRoot(Ship ship)
        {
            if (!HingeJointManager.IsJointed(ship)) return null;
            List<Ship> members = s_rootScratch ?? (s_rootScratch = new List<Ship>());
            HingeJointManager.GetAssemblyShips(ship, members, rigidOnly: false, identity: true);
            if (members.Count <= 1) return null;

            AssemblyGraph groups = AssemblyTopology.GroupsOf(ship.Sim);
            if (groups.TryGetAnchor(AssemblyTopology.Id(ship), out ulong anchor))
                foreach (Ship s in members)
                    if (s != null && AssemblyTopology.Id(s) == anchor) return s;
            Ship best = null;
            foreach (Ship s in members)
                if (s != null && (best == null || AssemblyTopology.Id(s) < AssemblyTopology.Id(best))) best = s;
            return best;
        }

        public static Ship Redirect(Ship ship)
        {
            Ship root = RootOf(ship);
            return root ?? ship;
        }

        public static void RefreshCommandGates(IEnumerable<Ship> ships)
        {
            try
            {
                foreach (Ship s in ships)
                {
                    s?.Commands?.RefreshCanExecuteCommands();
                    s?.IndicatorSource?.RefreshActive();
                }
            }
            catch (Exception ex)
            {
                Log.Exception("AssemblyFacade.RefreshCommandGates", ex);
            }
        }

        private static SimRoot s_reconcileSim;
        private static int s_reconcileTick = SimTicks.NEVER;
        private static int s_reconcileBusy;
        private static readonly List<Ship> s_reconcileSeeds = new List<Ship>();
        private static readonly List<Ship> s_reconcileMembers = new List<Ship>();
        private static readonly HashSet<Ship> s_reconcileSeen = new HashSet<Ship>();
        private static readonly HashSet<Ship> s_forcedLogged = new HashSet<Ship>();

        private static bool SameShip(Ship a, Ship b)
        {
            Ship[] ship = AssemblyIndex.FreshAssemblyOf(a);
            return ship != null && Array.IndexOf(ship, b) >= 0;
        }

        public static void ReconcileCommandGates(SimRoot sim)
        {
            if (!Enabled || sim == null) return;
            if (ReferenceEquals(sim, s_reconcileSim) && sim.Tick == s_reconcileTick) return;
            if (System.Threading.Interlocked.CompareExchange(ref s_reconcileBusy, 1, 0) != 0) return;
            s_reconcileSim = sim;
            s_reconcileTick = sim.Tick;
            try
            {
                if (HingeJointManager.ActivePinCount == 0) return;
                s_reconcileSeen.Clear();
                HingeJointManager.CollectJointedShips(s_reconcileSeeds, sim);
                foreach (Ship seed in s_reconcileSeeds.ToArray())
                {
                    if (seed == null || s_reconcileSeen.Contains(seed)) continue;
                    HingeJointManager.GetAssemblyShips(seed, s_reconcileMembers, rigidOnly: false, identity: true);
                    var members = new List<Ship>(s_reconcileMembers);
                    foreach (Ship m in members) s_reconcileSeen.Add(m);
                    if (members.Count <= 1) continue;

                    int provided = 0, used = 0;
                    Ship head = null; int headCp = -1;
                    foreach (Ship m in members)
                    {
                        CommandManager cm = m?.Commands;
                        if (cm == null) continue;
                        provided += cm.CommandPointsProvided;
                        used += cm.CommandPointsUsed;
                        if (!m.IsJunk && cm.CommandPointsProvided > headCp) { head = m; headCp = cm.CommandPointsProvided; }
                    }
                    bool assemblyGate = provided > 0 && provided >= used;

                    foreach (Ship m in members)
                    {
                        CommandManager cm = m?.Commands;
                        if (cm == null || m.Metadata == null) continue;
                        if (m.IsJunk && head != null && headCp > 0 && SameShip(m, head))
                        {
                            int index = head.Metadata.PlayerIndex;
                            m.Metadata.PlayerIndex = index;
                            LogThrottle.Info("junkRestore|" + m.UniqueID, $"Junk member ship {m.UniqueID} restored to player {index}: it is jointed to ship {head.UniqueID}, which provides {headCp} command points.");
                        }
                        bool gate = assemblyGate && !m.IsJunk;
                        if (cm.CanExecuteCommands == gate) continue;
                        cm.RefreshCanExecuteCommands();
                        if (cm.CanExecuteCommands == gate) continue;
                        if (s_forcedLogged.Add(m))
                            Log.Info($"Command gate ship {m.UniqueID}: refresh did not apply the assembly verdict (active={cm.IsActive}, junk={m.IsJunk}, own {cm.CommandPointsProvided}/{cm.CommandPointsUsed}); forcing {(gate ? "CAN" : "CANNOT")}.");
                        cm.CanExecuteCommands = gate;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception("AssemblyFacade.ReconcileCommandGates", ex);
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref s_reconcileBusy, 0);
            }
        }

        private static readonly List<Ship> s_clearedSelection = new List<Ship>();
        private static Halfling.Timing.Time s_clearedAt;

        private static bool BelongsTo(Ship s, Ship root)
        {
            if (s == null) return false;
            return ReferenceEquals(RootOf(s) ?? s, root);
        }

        private static bool AssemblySelected(PlayerInputHandler input, Ship root)
        {
            foreach (Ship s in input._selectedShips.Keys)
                if (BelongsTo(s, root)) return true;
            if (s_clearedSelection.Count > 0 && s_clearedAt == Halfling.App.Clock.Time)
                foreach (Ship s in s_clearedSelection)
                    if (BelongsTo(s, root)) return true;
            return false;
        }

        private static Ship RedirectForSelection(PlayerInputHandler input, Ship ship)
        {
            Ship root = RootOf(ship);
            if (root == null) return ship;
            bool selected = input != null && AssemblySelected(input, root);
            return selected ? ship : root;
        }

        private static bool IsDrilledDown(Ship anyMember, Ship root)
        {
            PlayerInputHandler input = anyMember?.Sim?.PlayerInput;
            if (input == null || root == null) return false;
            foreach (Ship s in input._selectedShips.Keys)
                if (!ReferenceEquals(s, root) && ReferenceEquals(RootOf(s), root)) return true;
            return false;
        }

        [HarmonyPatch(typeof(PlayerInputHandler), "ClearSelectedShips")]
        private static class ClearSelectedShipsPatch
        {
            static void Prefix(PlayerInputHandler __instance)
            {
                try
                {
                    s_clearedSelection.Clear();
                    s_clearedSelection.AddRange(__instance._selectedShips.Keys);
                    s_clearedAt = Halfling.App.Clock.Time;
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(PlayerInputHandler), "SelectShip")]
        private static class SelectShipPatch
        {
            static void Prefix(PlayerInputHandler __instance, ref Ship ship)
            {
                try { ship = RedirectForSelection(__instance, ship); } catch { }
            }
        }

        [HarmonyPatch(typeof(PlayerInputHandler), "SelectSingleShip")]
        private static class SelectSingleShipPatch
        {
            static void Prefix(PlayerInputHandler __instance, ref Ship ship)
            {
                try { if (ship != null) ship = RedirectForSelection(__instance, ship); } catch { }
            }
        }

        [HarmonyPatch(typeof(PlayerInputHandler), "DeselectShip")]
        private static class DeselectShipPatch
        {
            static void Prefix(PlayerInputHandler __instance, ref Ship ship)
            {
                try
                {
                    if (ship != null && __instance._selectedShips.ContainsKey(ship)) return;
                    if (ship != null && ReferenceEquals(ship, s_removingShip))
                    {
                        Ship root = Redirect(ship);
                        if (!ReferenceEquals(root, ship) && __instance._selectedShips.ContainsKey(root))
                            Log.Info($"Facade: ship {ship.UniqueID} left the sim while its root {root.UniqueID} was selected -- the root stays selected (build mode stays open).");
                        return;
                    }
                    ship = Redirect(ship);
                }
                catch { }
            }
        }

        [ThreadStatic] private static Ship s_removingShip;

        [HarmonyPatch(typeof(SimShipsManager), "Remove")]
        private static class RemovingShipPatch
        {
            static void Prefix(Ship ship, out Ship __state)
            {
                __state = s_removingShip;
                s_removingShip = ship;
            }

            static void Finalizer(Ship __state)
            {
                s_removingShip = __state;
            }
        }

        [HarmonyPatch(typeof(PlayerInputHandler), "SelectedCommandExecutableShips", MethodType.Getter)]
        private static class CommandShipsPatch
        {
            static void Postfix(ref IEnumerable<Ship> __result)
            {
                try
                {
                    if (!Enabled || __result == null) return;
                    __result = __result.Select(Redirect).Distinct().ToList();
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(PlayerInputHandler), "SelectShips")]
        private static class SelectShipsPatch
        {
            static void Prefix(ref IEnumerable<Ship> ships)
            {
                try
                {
                    if (ships == null || !Enabled) return;
                    ships = ships.Select(Redirect).Distinct().ToList();
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(PlayerInputHandler), "IsShipSelected")]
        private static class IsShipSelectedPatch
        {
            static void Postfix(PlayerInputHandler __instance, Ship ship, ref bool __result)
            {
                try
                {
                    if (__result || ship == null) return;
                    Ship root = RootOf(ship);
                    if (root == null) return;
                    if (ReferenceEquals(root, ship))
                    {
                        __result = IsDrilledDown(ship, root);
                        return;
                    }
                    __result = __instance._selectedShips.ContainsKey(root);
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(PlayerInputHandler), "GetShipSelectedTime")]
        private static class SelectedTimePatch
        {
            static void Postfix(PlayerInputHandler __instance, Ship ship, ref Halfling.Timing.Time? __result)
            {
                try
                {
                    if (__result.HasValue || ship == null) return;
                    Ship root = RootOf(ship);
                    if (root == null) return;
                    if (ReferenceEquals(root, ship))
                    {
                        foreach (KeyValuePair<Ship, Halfling.Timing.Time> kv in __instance._selectedShips)
                            if (!ReferenceEquals(kv.Key, root) && ReferenceEquals(RootOf(kv.Key), root)) { __result = kv.Value; return; }
                        return;
                    }
                    if (__instance._selectedShips.TryGetValue(root, out Halfling.Timing.Time t))
                        __result = t;
                }
                catch { }
            }
        }

        private static bool AssemblyMemberSelected(Ship ship)
        {
            if (ship == null || ship.IsSelected) return false;
            Ship root = RootOf(ship);
            if (root == null) return false;
            PlayerInputHandler input = ship.Sim?.PlayerInput;
            if (input == null) return false;
            foreach (Ship s in input._selectedShips.Keys)
                if (ReferenceEquals(s, root) || ReferenceEquals(RootOf(s), root)) return true;
            return false;
        }

        [HarmonyPatch(typeof(ShipRenderer), "GetTargetRoofOpacity")]
        private static class RoofOpacityPatch
        {
            static void Postfix(ShipRenderer __instance, ref float __result)
            {
                try
                {
                    if (__result != 1f || !Enabled) return;
                    Ship ship = __instance.Ship;
                    SimRenderManager rendering = __instance.Sim?.Rendering;
                    if (ship == null || rendering == null || rendering.ShowRoofs || rendering.ForceShowRoofs.ContainsKey(ship)) return;
                    if (__instance.Game != null && ship.SpecialSightProvider?.ShowInterior() != null) return;
                    if (AssemblyMemberSelected(ship)) __result = 0f;
                }
                catch (Exception ex)
                {
                    Log.Exception("AssemblyFacade.RoofOpacityPatch", ex);
                }
            }
        }

        [HarmonyPatch(typeof(SimOverlayRenderer), "GetShipColorFactor")]
        private static class ColorFactorPatch
        {
            static void Postfix(SimOverlayRenderer __instance, Ship ship, float alpha, ref Color __result)
            {
                try
                {
                    if (!Enabled || ship == null || __instance.Sim?.Rendering == null) return;
                    if (__instance.Sim.Rendering.ForceShowRoofs.ContainsKey(ship) || !AssemblyMemberSelected(ship)) return;
                    float t = __instance.BlueprintGraphicsMode == BlueprintGraphicsMode.Delta ? 0f : __instance.BlueprintOpacity;
                    Color c = Color.Lerp(in Color.White, in __instance.Rules.ShipDarkenColor, t);
                    c.A *= alpha;
                    __result = c;
                }
                catch (Exception ex)
                {
                    Log.Exception("AssemblyFacade.ColorFactorPatch", ex);
                }
            }
        }

        [HarmonyPatch(typeof(ShipInfoOverlayRenderer), "GetShipSelectionCircle")]
        private static class SelectionCirclePatch
        {
            static void Postfix(Ship ship, bool showBlueprints, bool showRedprints, ref Circle __result)
            {
                try
                {
                    Ship root = RootOf(ship);
                    if (root == null) return;
                    if (IsDrilledDown(ship, root)) return;
                    HingeJointManager.GetAssemblyShips(ship, s_scratch, rigidOnly: false, identity: true);
                    Circle c = __result;
                    foreach (Ship m in s_scratch)
                    {
                        if (ReferenceEquals(m, ship) || m.Parts.Count == 0) continue;
                        Circle mc = m.GetWorldBoundingCircle(1.5f);
                        if (showBlueprints && m.BlueprintParts.Count > 0)
                            mc = Circle.BoundCircles(mc, m.BlueprintParts.GetWorldBoundingCircle(1.5f));
                        if (showRedprints && m.DestroyedParts.Count > 0)
                            mc = Circle.BoundCircles(mc, m.DestroyedParts.GetWorldBoundingCircle(1.5f));
                        c = Circle.BoundCircles(c, mc);
                    }
                    __result = c;
                }
                catch (Exception ex)
                {
                    Log.Exception("AssemblyFacade.SelectionCirclePatch", ex);
                }
            }
        }

        [HarmonyPatch(typeof(ShipRenderer), "DrawShipGhost")]
        private static class GhostPatch
        {
            [ThreadStatic] private static bool s_drawing;

            static void Postfix(ShipRenderer __instance, Vector2 location, Direction rotation, Color color,
                float scale, Vector2? customCenterOffset)
            {
                if (s_drawing || customCenterOffset.HasValue) return;
                try
                {
                    Ship root = __instance.Ship;
                    if (root?.Physics?.Body == null || !ReferenceEquals(RootOf(root), root)) return;
                    HingeJointManager.GetAssemblyShips(root, s_scratch, rigidOnly: false, identity: true);
                    if (s_scratch.Count <= 1) return;

                    s_drawing = true;
                    Vector2 rootC = root.DetWorldCenter;
                    float rootRot = (float)root.DetRotation;
                    float ghostRot = (float)rotation;
                    float dRot = ghostRot - rootRot;
                    float cs = (float)Math.Cos(dRot), sn = (float)Math.Sin(dRot);
                    foreach (Ship m in s_scratch)
                    {
                        if (ReferenceEquals(m, root) || m.Physics?.Body == null || m.Parts.Count == 0) continue;
                        Vector2 mc = m.DetWorldCenter;
                        float dx = mc.X - rootC.X;
                        float dy = mc.Y - rootC.Y;
                        var loc = new Vector2(location.X + (dx * cs - dy * sn) * scale,
                                              location.Y + (dx * sn + dy * cs) * scale);
                        var rot = (Direction)(ghostRot + ((float)m.DetRotation - rootRot));
                        m.Renderer.DrawShipGhost(loc, rot, color, scale);
                    }
                }
                catch (Exception ex)
                {
                    Log.Exception("AssemblyFacade.GhostPatch", ex);
                }
                finally
                {
                    s_drawing = false;
                }
            }
        }

        [HarmonyPatch(typeof(CommandManager), "RefreshCanExecuteCommands")]
        private static class CommandGatePatch
        {
            [ThreadStatic] private static bool s_propagating;
            private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CommandManager, object> s_lastLogged =
                new System.Runtime.CompilerServices.ConditionalWeakTable<CommandManager, object>();

            static bool Prefix(CommandManager __instance)
            {
                try
                {
                    if (!Enabled || !__instance.IsActive) return true;
                    Ship ship = __instance.Ship;
                    if (ship == null || RootOf(ship) == null) return true;
                    if (ship.Sim != null && ship.Sim.IsDoingParallelUpdate)
                    {
                        CommandManager cm = __instance;
                        ship.Sim.EnqueueDeterministic(ship.UniqueID, () => { if (cm.IsActive) cm.RefreshCanExecuteCommands(); });
                        return false;
                    }
                    if (CommandGateBatch.ShouldDefer(CommandGateBatch.Depth, true))
                    {
                        CommandGateBatch.Defer(__instance);
                        return false;
                    }

                    HingeJointManager.GetAssemblyShips(ship, s_scratch, rigidOnly: false, identity: true);
                    int provided = 0, used = 0;
                    foreach (Ship m in s_scratch)
                    {
                        CommandManager cm = m?.Commands;
                        if (cm == null) continue;
                        provided += cm.CommandPointsProvided;
                        used += cm.CommandPointsUsed;
                    }
                    bool gate = provided > 0 && provided >= used && !ship.IsJunk;
                    if (gate != __instance.CanExecuteCommands)
                    {
                        Log.Info($"Command gate ship {ship.UniqueID}: assembly provided={provided} used={used} (own {__instance.CommandPointsProvided}/{__instance.CommandPointsUsed}{(ship.IsJunk ? ", JUNK" : "")}) -> {(gate ? "CAN" : "CANNOT")} execute commands.");
                        __instance.CanExecuteCommands = gate;
                    }

                    if (!s_propagating)
                    {
                        s_propagating = true;
                        try
                        {
                            var others = new List<Ship>(s_scratch);
                            foreach (Ship m in others)
                                if (!ReferenceEquals(m, ship))
                                    m?.Commands?.RefreshCanExecuteCommands();
                        }
                        finally
                        {
                            s_propagating = false;
                        }
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    Log.Exception("AssemblyFacade.CommandGatePatch", ex);
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(ShipIndicatorSource), "RefreshActive")]
        private static class IndicatorPatch
        {
            static void Postfix(ShipIndicatorSource __instance)
            {
                try
                {
                    Ship ship = __instance.Ship;
                    Ship root = RootOf(ship);
                    if (root == null || ReferenceEquals(root, ship)) return;
                    __instance.SelfActive = false;
                }
                catch (Exception ex)
                {
                    Log.Exception("AssemblyFacade.IndicatorPatch", ex);
                }
            }
        }

        [HarmonyPatch(typeof(BuildToolbox), "HasInsufficientCommandPoints")]
        private static class BuildGatePatch
        {
            static void Postfix(BuildToolbox __instance, int netCP, bool blueprintConstruct, ref bool __result)
            {
                try
                {
                    Ship ship = __instance._ship;
                    if (ship == null || RootOf(ship) == null) return;
                    if (!(netCP < 0 && (blueprintConstruct || !__instance.IsEditingBlueprints) && !Settings.BuildAllowInsufficientCommand))
                    {
                        __result = false;
                        return;
                    }
                    HingeJointManager.GetAssemblyShips(ship, s_scratch, rigidOnly: false, identity: true);
                    int remaining = 0, provided = 0;
                    foreach (Ship m in s_scratch)
                    {
                        if (m?.Parts == null) continue;
                        remaining += m.Parts.CommandPointsRemaining;
                        provided += m.Parts.CommandPointsProvided;
                    }
                    __result = -netCP > remaining && provided > 0;
                }
                catch (Exception ex)
                {
                    Log.Exception("AssemblyFacade.BuildGatePatch", ex);
                }
            }
        }

        [HarmonyPatch]
        private static class BuildStatBarPatch
        {
            private static System.Reflection.PropertyInfo s_statBar;

            static System.Reflection.MethodBase TargetMethod()
            {
                Type t = AccessTools.Inner(typeof(BuildToolboxStatsGui), "CommandPointStatBar");
                s_statBar = AccessTools.Property(t, "StatBar");
                return AccessTools.Method(t, "Update");
            }

            static bool Prefix(object __instance, BlueprintPartsManager partsManager, ref bool __result)
            {
                try
                {
                    Ship ship = partsManager?.Ship;
                    if (ship == null || RootOf(ship) == null) return true;
                    HingeJointManager.GetAssemblyShips(ship, s_scratch, rigidOnly: false, identity: true);
                    float used = 0f, provided = 0f;
                    foreach (Ship m in s_scratch)
                    {
                        if (m?.BlueprintParts == null) continue;
                        used += m.BlueprintParts.CommandPointsUsed;
                        provided += m.BlueprintParts.CommandPointsProvided;
                    }
                    var bar = (ShipStatBar)s_statBar.GetValue(__instance);
                    if (bar == null) return true;
                    float free = provided - used;
                    bar.Update(provided, used, provided, free, null, free < 0f);
                    s_churnStatWrites++;
                    __result = true;
                    return false;
                }
                catch (Exception ex)
                {
                    Log.Exception("AssemblyFacade.BuildStatBarPatch", ex);
                    return true;
                }
            }
        }

        [HarmonyPatch]
        private static class StatTotalPatch
        {
            [ThreadStatic] private static bool s_inner;

            static IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                return AccessTools.GetDeclaredMethods(typeof(BlueprintPartsManager))
                    .Where(m => m.Name == "GetStatTotal").Cast<System.Reflection.MethodBase>();
            }

            static void Prefix(out bool __state)
            {
                __state = !s_inner;
                if (__state) s_inner = true;
            }

            static void Postfix(BlueprintPartsManager __instance, object[] __args,
                System.Reflection.MethodBase __originalMethod, bool __state, ref float __result)
            {
                if (!__state) return;
                try
                {
                    Ship ship = __instance.Ship;
                    if (ship == null || RootOf(ship) == null) return;
                    var members = new List<Ship>();
                    HingeJointManager.GetAssemblyShips(ship, members, rigidOnly: false, identity: true);
                    foreach (Ship m in members)
                    {
                        if (ReferenceEquals(m, ship) || m?.BlueprintParts == null) continue;
                        object r = __originalMethod.Invoke(m.BlueprintParts, __args);
                        if (r is float f) __result += f;
                    }
                }
                catch (Exception ex)
                {
                    Log.Exception("AssemblyFacade.StatTotalPatch", ex);
                }
                finally
                {
                    s_inner = false;
                }
            }
        }

        [HarmonyPatch]
        private static class MassLabelPatch
        {
            private static System.Reflection.FieldInfo s_toolbox, s_activeToggle;

            static System.Reflection.MethodBase TargetMethod()
            {
                Type t = AccessTools.Inner(typeof(BuildToolboxStatsGui), "MassStatLabel");
                s_toolbox = AccessTools.Field(t, "_toolbox");
                s_activeToggle = AccessTools.Field(t, "_activeToggle");
                return AccessTools.Method(t, "Update");
            }

            static bool Prefix(object __instance, BlueprintPartsManager partsManager, ref bool __result)
            {
                try
                {
                    Ship ship = partsManager?.Ship;
                    if (ship == null || RootOf(ship) == null) return true;
                    var toolbox = s_toolbox?.GetValue(__instance) as BuildToolbox;
                    if (toolbox == null || toolbox.IsEditingBlueprints) return true;
                    var toggle = s_activeToggle?.GetValue(__instance) as Halfling.Gui.Components.Toggles.SimpleToggle;
                    if (toggle == null) return true;
                    HingeJointManager.GetAssemblyShips(ship, s_scratch, rigidOnly: false, identity: true);
                    float mass = 0f;
                    foreach (Ship m in s_scratch)
                        mass += m?.Physics?.DisplayMass ?? 0f;
                    toggle.IsOn = true;
                    ((Label)__instance).Text = Strings.FormatText("BuildBox/TotalMassFmt", mass);
                    s_churnStatWrites++;
                    __result = true;
                    return false;
                }
                catch (Exception ex)
                {
                    Log.Exception("AssemblyFacade.MassLabelPatch", ex);
                    return true;
                }
            }
        }

        [HarmonyPatch]
        private static class ValueLabelPatch
        {
            private static System.Reflection.FieldInfo s_toolbox;

            static System.Reflection.MethodBase TargetMethod()
            {
                Type t = AccessTools.Inner(typeof(BuildToolboxStatsGui), "ValueStatLabel");
                s_toolbox = AccessTools.Field(t, "_toolbox");
                return AccessTools.Method(t, "Update");
            }

            static bool Prefix(object __instance, BlueprintPartsManager partsManager, ref bool __result)
            {
                try
                {
                    Ship ship = partsManager?.Ship;
                    if (ship == null || RootOf(ship) == null) return true;
                    var toolbox = s_toolbox?.GetValue(__instance) as BuildToolbox;
                    if (toolbox == null || toolbox.IsEditingBlueprints) return true;
                    HingeJointManager.GetAssemblyShips(ship, s_scratch, rigidOnly: false, identity: true);
                    int value = 0;
                    foreach (Ship m in s_scratch)
                        value += m?.TotalPhysicalValue ?? 0;
                    ((Label)__instance).Text = Strings.FormatText("BuildBox/TotalShipValueFmt", value.ToXmlCostString());
                    s_churnStatWrites++;
                    __result = true;
                    return false;
                }
                catch (Exception ex)
                {
                    Log.Exception("AssemblyFacade.ValueLabelPatch", ex);
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(BuildToolbox), "OnUpdatingUIState")]
        private static class VitalWarningPatch
        {
            private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BuildToolbox, Label> s_real =
                new System.Runtime.CompilerServices.ConditionalWeakTable<BuildToolbox, Label>();
            private static readonly System.Reflection.FieldInfo s_field =
                AccessTools.Field(typeof(BuildToolbox), "_vitalWarningLbl");

            static void Postfix(BuildToolbox __instance)
            {
                try
                {
                    Label written = __instance._vitalWarningLbl;
                    if (written == null) return;
                    if (!s_real.TryGetValue(__instance, out Label real))
                    {
                        real = written;
                        var standIn = new Label();
                        standIn.SelfActive = false;
                        s_field.SetValue(__instance, standIn);
                        s_real.Add(__instance, real);
                    }

                    bool active = written.SelfActive;
                    string text = written.Text;
                    Ship ship = __instance._ship;
                    if (active && ship != null && RootOf(ship) != null
                        && ship.Rules.VitalCategories != null && Settings.ShowMissingPartsWarning)
                    {
                        string assemblyText = AssemblyWarning(ship);
                        if (assemblyText == null)
                        {
                            active = false;
                            s_churnVitalSuppressed++;
                        }
                        else text = assemblyText;
                    }

                    if (active && real.Text != text) { real.Text = text; s_churnVitalChanged++; }
                    if (real.SelfActive != active) { real.SelfActive = active; s_churnVitalChanged++; }
                }
                catch (Exception ex)
                {
                    Log.Exception("AssemblyFacade.VitalWarningPatch", ex);
                }
            }

            private static string AssemblyWarning(Ship ship)
            {
                HingeJointManager.GetAssemblyShips(ship, s_scratch, rigidOnly: false, identity: true);
                var members = new List<Ship>(s_scratch);
                var lines = new List<string>();

                bool crewMode = ship.Sim != null &&
                    ship.Sim.Mode.GetConstructionMode(ship) == ConstructionMode.CrewResources;
                if (crewMode && ship.Crew.OnShip.Count > 0)
                {
                    bool airlock = false;
                    foreach (Ship m in members)
                        if (m != null && Airlock.HasOnShip(m)) { airlock = true; break; }
                    if (!airlock) lines.Add(Strings.KeyString("VitalCategories/ExistingAirlock").ToString());
                }
                foreach (VitalCategoryInfo info in ship.Rules.VitalCategories)
                {
                    bool has = false;
                    foreach (Ship m in members)
                        if (m?.BlueprintParts != null && m.BlueprintParts.HasPartsInCategory(info.Category)) { has = true; break; }
                    if (!has) lines.Add(info.WarningKey.ToString());
                }
                if (lines.Count == 0) return null;

                var sb = new System.Text.StringBuilder();
                sb.Append(Strings.GetText("VitalCategories/Header"));
                foreach (string line in lines)
                {
                    sb.AppendLine();
                    sb.Append(line);
                }
                return sb.ToString();
            }
        }

    }
}
