using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui.Build;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation;
using HarmonyLib;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using HVec = Halfling.Geometry.Vector2;

namespace ZTX.HingesPistonsDocking
{
    internal static class SensorAreaOverlay
    {
        private static SimRoot s_attachedSim;
        private static readonly List<ZtxDamageSensorComponent> s_damage = new List<ZtxDamageSensorComponent>();
        private static readonly List<ZtxProximitySensorComponent> s_proximity = new List<ZtxProximitySensorComponent>();
        private static readonly Color s_damageFill = new IntColor(255, 96, 64, 50);
        private static readonly Color s_damageLine = new IntColor(255, 120, 80, 220);
        private static readonly Color s_proxLine = new IntColor(64, 224, 255, 200);
        private static readonly Color s_reachLine = new IntColor(120, 255, 140, 200);
        private static readonly List<ZtxBayStationComponent> s_stations = new List<ZtxBayStationComponent>();
        private static readonly List<ZtxBayTerminalComponent> s_terminals = new List<ZtxBayTerminalComponent>();
        private static readonly Color s_designFill = new IntColor(120, 180, 255, 40);
        private const int CIRCLE_SEGMENTS = 48;
        private static readonly HashSet<Part> s_shown = new HashSet<Part>();
        private static readonly List<IntVector2> s_cells = new List<IntVector2>();
        private static bool s_drawLogged;

        private static bool Wanted(Part p) => p.IsSelected || ReferenceEquals(LogicConsoleWindow.HoveredPart, p) || AreaSensorWindow.Shows(p);

        private static bool Show(Part p, string what)
        {
            bool want = Wanted(p);
            if (want && s_shown.Add(p)) Log.Info("overlay: drawing the " + what + " of " + p.Rules.ID + " at " + p.Location + " (selected " + p.IsSelected + ").");
            else if (!want) s_shown.Remove(p);
            return want;
        }

        public static void Register(ZtxDamageSensorComponent s) { if (s != null && !s_damage.Contains(s)) s_damage.Add(s); }
        public static void Deregister(ZtxDamageSensorComponent s) { s_damage.Remove(s); }
        public static void Register(ZtxProximitySensorComponent s) { if (s != null && !s_proximity.Contains(s)) s_proximity.Add(s); }
        public static void Deregister(ZtxProximitySensorComponent s) { s_proximity.Remove(s); }
        public static void Register(ZtxBayStationComponent s) { if (s != null && !s_stations.Contains(s)) s_stations.Add(s); }
        public static void Deregister(ZtxBayStationComponent s) { s_stations.Remove(s); }
        public static void Register(ZtxBayTerminalComponent t) { if (t != null && !s_terminals.Contains(t)) s_terminals.Add(t); }
        public static void Deregister(ZtxBayTerminalComponent t) { s_terminals.Remove(t); }

        public static void Attach(SimRoot sim)
        {
            if (sim == null || ReferenceEquals(s_attachedSim, sim)) return;
            sim.Components.Add(new CustomDrawProxy(5050 , delegate { Draw(); }));
            s_attachedSim = sim;
        }

        public static void Reset(SimRoot disposed)
        {
            bool Gone(Part p) => p == null || p.Ship?.Sim == null || ReferenceEquals(p.Ship.Sim, disposed);
            if (ReferenceEquals(s_attachedSim, disposed)) { s_attachedSim = null; s_drawLogged = false; }
            s_damage.RemoveAll(s => Gone(s?.Part));
            s_proximity.RemoveAll(s => Gone(s?.Part));
            s_stations.RemoveAll(s => Gone(s?.Part));
            s_terminals.RemoveAll(t => Gone(t?.Part));
            s_shown.RemoveWhere(Gone);
            s_gripDrawn.Clear();
        }

        private static void Draw()
        {
            try
            {
                if (!s_drawLogged)
                {
                    s_drawLogged = true;
                    Log.Info("overlay: draw callback active (" + s_damage.Count + " damage, " + s_proximity.Count + " proximity sensors registered).");
                }
                for (int i = s_damage.Count - 1; i >= 0; i--)
                {
                    ZtxDamageSensorComponent s = s_damage[i];
                    Part p = s?.Part;
                    if (p == null || p.IsDestroyed || p.Ship == null) { s_damage.RemoveAt(i); continue; }
                    if (!Show(p, "area")) continue;
                    AreaSensorGeometry.Cells(p.Location, LogicSource.Facing(p), AreaSensorGeometry.Read(p), s_cells);
                    DrawArea(p.Ship, s_cells);
                }
                for (int i = s_proximity.Count - 1; i >= 0; i--)
                {
                    ZtxProximitySensorComponent s = s_proximity[i];
                    Part p = s?.Part;
                    if (p == null || p.IsDestroyed || p.Ship == null) { s_proximity.RemoveAt(i); continue; }
                    if (!Show(p, "range circle")) continue;
                    DrawRangeCircle(p.Ship, p.Rect, s.RangeWorld);
                }
                for (int i = s_stations.Count - 1; i >= 0; i--)
                {
                    ZtxBayStationComponent s = s_stations[i];
                    Part p = s?.Part;
                    if (p == null || p.IsDestroyed || p.Ship == null) { s_stations.RemoveAt(i); continue; }
                    if (!ReachWanted(s)) continue;
                    float scale = p.Ship.Scale.X > 0f ? p.Ship.Scale.X : 1f;
                    DrawRangeCircle(p.Ship, p.Rect, s.Rules.ReachCells * scale, s_reachLine);
                }
                for (int i = s_terminals.Count - 1; i >= 0; i--)
                {
                    ZtxBayTerminalComponent t = s_terminals[i];
                    Part p = t?.Part;
                    if (p == null || p.IsDestroyed || p.Ship == null) { s_terminals.RemoveAt(i); continue; }
                    if (!p.IsSelected && !ReferenceEquals(BayTerminalWindow.Current, p)) continue;
                    DrawCells(p.Ship, t.Bay.DesignCells, s_designFill);
                }
                DrawGripAreas();
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("SensorAreaOverlay.Draw", ex);
            }
        }

        private const int MAX_SELECTED_SCANNED = 512;
        private static readonly List<IntVector2> s_gripCells = new List<IntVector2>();
        private static readonly HashSet<ZtxPistonBaseComponent> s_gripDrawn = new HashSet<ZtxPistonBaseComponent>();
        private static readonly HashSet<Part> s_cartGripDrawn = new HashSet<Part>();

        private static void DrawGripAreas()
        {
            s_gripDrawn.Clear();
            s_cartGripDrawn.Clear();
            Part open = PistonGripWindow.Current;
            if (open != null) DrawGrip(GripService.PistonOf(open));
            if (open != null) DrawCartGrip(open);
            Part cartOpen = RailCartWindow.Current;
            if (cartOpen != null) DrawCartGrip(cartOpen);
            var selected = s_attachedSim?.PlayerInput?.SelectedParts;
            if (selected == null) return;
            int scanned = 0;
            foreach (Part p in selected)
            {
                if (++scanned > MAX_SELECTED_SCANNED) break;
                ZtxPistonBaseComponent pb = GripService.PistonOf(p) ?? (ZtxHingeTipComponent.ForPart(p)?.PairedBase as ZtxPistonBaseComponent);
                if (pb != null) DrawGrip(pb);
                DrawCartGrip(p);
            }
        }

        private static void DrawGrip(ZtxPistonBaseComponent pb)
        {
            if (pb == null || !s_gripDrawn.Add(pb)) return;
            if (GripService.AreaOnHead(pb, s_gripCells, out Ship ship)) DrawArea(ship, s_gripCells);
        }

        private static void DrawCartGrip(Part p)
        {
            if (ZtxRailCartComponent.ForPart(p) == null || !s_cartGripDrawn.Add(p)) return;
            if (GripService.AreaOnCart(p, s_gripCells, out Ship ship)) DrawArea(ship, s_gripCells);
        }

        private static void DrawArea(Ship ship, List<IntVector2> cells)
        {
            if (ship?.Physics?.Body == null || !AreaSensorGeometry.Bounds(cells, out IntVector2 min, out IntVector2 max)) return;
            float scale = ship.Scale.X > 0f ? ship.Scale.X : 1f;
            var localCenter = new HVec((min.X + max.X + 1) * 0.5f * scale, (min.Y + max.Y + 1) * 0.5f * scale);
            var size = new HVec((max.X - min.X + 1) * scale, (max.Y - min.Y + 1) * scale);
            DrawBox(ship, localCenter, size, s_damageFill, s_damageLine);
        }

        private static void DrawBox(Ship ship, HVec localCenter, HVec size, Color fill, Color line)
        {
            HVec half = size * 0.5f;
            HVec center = ship.Physics.Body.GetWorldPoint(localCenter);
            HVec w0 = ship.Physics.Body.GetWorldPoint(localCenter + new HVec(-half.X, -half.Y));
            HVec w1 = ship.Physics.Body.GetWorldPoint(localCenter + new HVec(half.X, -half.Y));
            HVec w2 = ship.Physics.Body.GetWorldPoint(localCenter + new HVec(half.X, half.Y));
            HVec w3 = ship.Physics.Body.GetWorldPoint(localCenter + new HVec(-half.X, half.Y));
            var direction = new Direction(ship.Physics.Body.Rotation);
            RotRect rect = Rect.CenteredOn(in center, size).RotateAround(in center, direction);
            App.Graphics.SetToMaterial(null);
            var quad = new Quad(rect, fill);
            App.Graphics.Draw(in quad);
            SkeletonOverlay.DrawLine(w0, w1, line, 0.08f);
            SkeletonOverlay.DrawLine(w1, w2, line, 0.08f);
            SkeletonOverlay.DrawLine(w2, w3, line, 0.08f);
            SkeletonOverlay.DrawLine(w3, w0, line, 0.08f);
        }

        private static void DrawCells(Ship ship, HashSet<IntVector2> cells, Color fill)
        {
            if (ship?.Physics?.Body == null || cells == null || cells.Count == 0) return;
            float scale = ship.Scale.X > 0f ? ship.Scale.X : 1f;
            var size = new HVec(scale, scale);
            var direction = new Direction(ship.Physics.Body.Rotation);
            App.Graphics.SetToMaterial(null);
            foreach (IntVector2 c in cells)
            {
                HVec center = ship.Physics.Body.GetWorldPoint(new HVec((c.X + 0.5f) * scale, (c.Y + 0.5f) * scale));
                RotRect rect = Rect.CenteredOn(in center, size * 0.92f).RotateAround(in center, direction);
                var quad = new Quad(rect, fill);
                App.Graphics.Draw(in quad);
            }
        }

        private static bool ReachWanted(ZtxBayStationComponent s)
        {
            Part p = s.Part;
            if (Show(p, "reach")) return true;
            if (ReferenceEquals(BayStationWindow.Current, p)) return true;
            Part terminal = BayManager.TerminalOfStation(s)?.Part;
            return terminal != null && (terminal.IsSelected || ReferenceEquals(BayTerminalWindow.Current, terminal));
        }

        private static void DrawRangeCircle(Ship ship, IntRect r, float radius) => DrawRangeCircle(ship, r, radius, s_proxLine);

        private static void DrawRangeCircle(Ship ship, IntRect r, float radius, Color line)
        {
            if (ship?.Physics?.Body == null || radius <= 0f) return;
            float scale = ship.Scale.X > 0f ? ship.Scale.X : 1f;
            HVec local = new HVec((r.X + r.Width * 0.5f) * scale, (r.Y + r.Height * 0.5f) * scale);
            HVec c = ship.Physics.Body.GetWorldPoint(local);
            App.Graphics.SetToMaterial(null);
            HVec prev = new HVec(c.X + radius, c.Y);
            for (int i = 1; i <= CIRCLE_SEGMENTS; i++)
            {
                double a = i * (2.0 * Math.PI / CIRCLE_SEGMENTS);
                HVec next = new HVec(c.X + radius * (float)Math.Cos(a), c.Y + radius * (float)Math.Sin(a));
                SkeletonOverlay.DrawLine(prev, next, line, 0.06f);
                prev = next;
            }
        }

        private static readonly ID<PartToggleGuiRules> s_rangeId = new ID<PartToggleGuiRules>("ztx_prox_range");
        private const int DEFAULT_RANGE_MODE = 3;

        [HarmonyPatch(typeof(BaseAddPartsState), "DrawOverlayImpl")]
        private static class PlacementPreviewPatch
        {
            private static bool s_failed;

            static void Postfix(BaseAddPartsState __instance)
            {
                if (s_failed) return;
                try
                {
                    var parts = __instance._parts;
                    if (parts == null || parts.Count == 0) return;
                    if (__instance.Game?.Gui?.Reacted == true) return;
                    Ship ship = __instance.Sim?.PlayerInput?.GetSingleSelectedShip();
                    if (ship?.Physics?.Body == null) return;
                    foreach (var kv in parts)
                    {
                        PartInfo info = kv.Key;
                        if (info.Rules == null) continue;
                        foreach (PartComponentRules c in info.Rules.Components)
                        {
                            if (c is ZtxDamageSensorRules)
                            {
                                AreaSettings s = AreaSensorGeometry.Read(kv.Value?.UIToggles);
                                AreaSensorGeometry.Cells(info.Location, LogicSource.Facing(info.Rotation, info.FlipX), s, s_cells);
                                DrawArea(ship, s_cells);
                            }
                            else if (c is ZtxProximitySensorRules)
                            {
                                int mode = DEFAULT_RANGE_MODE;
                                if (kv.Value?.UIToggles != null && kv.Value.UIToggles.TryGetValue(s_rangeId, out int m)) mode = m;
                                float[] tiles = ZtxProximitySensorComponent.RangeTiles;
                                mode = Math.Max(0, Math.Min(tiles.Length - 1, mode));
                                float scale = ship.Scale.X > 0f ? ship.Scale.X : 1f;
                                DrawRangeCircle(ship, info.Rect, tiles[mode] * scale);
                            }
                            else if (c is ZtxBayStationRules station)
                            {
                                float scale = ship.Scale.X > 0f ? ship.Scale.X : 1f;
                                DrawRangeCircle(ship, info.Rect, station.ReachCells * scale, s_reachLine);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    s_failed = true;
                    Log.Exception("SensorAreaOverlay.PlacementPreviewPatch", ex);
                }
            }
        }
    }
}
