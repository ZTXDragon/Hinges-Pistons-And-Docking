using System;
using System.Collections.Generic;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints.Graphics;
using Cosmoteer.Simulation.Overlays;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class BayZoneOverlay : IBlueprintOverlayRenderer
    {
        private static readonly Color s_frameTint = new IntColor(120, 220, 255, 36);
        private static readonly Color s_designTint = new IntColor(120, 180, 255, 70);
        private static bool s_failed;
        private readonly List<IntVector2> _frame = new List<IntVector2>();
        private readonly List<IntVector2> _design = new List<IntVector2>();

        public static void Register()
        {
            try
            {
                BlueprintOverlayManager.RegisterOverlay(1160, () => new BayZoneOverlay());
            }
            catch (Exception ex)
            {
                Log.Exception("BayZoneOverlay registration", ex);
            }
        }

        public void RefreshData(Ship ship, IReadOnlyList<PartOverlayRenderData> parts, IBlueprintOverlaySettingsProvider settings, BlueprintOverlayFlags flags)
        {
            _frame.Clear();
            _design.Clear();
            if (s_failed || ship == null) return;
            try
            {
                foreach (ZtxBayTerminalComponent t in BayManager.TerminalsOf(ship))
                {
                    if (t.Part?.Ship != ship || !t.Bay.InCharge || !IsBayPick(t, parts)) continue;
                    if (t.Bay.StructureCells != null) _frame.AddRange(t.Bay.StructureCells);
                    if (t.Bay.Held == null) _design.AddRange(t.Bay.DesignCells);
                }
            }
            catch (Exception ex)
            {
                s_failed = true;
                Log.Exception("BayZoneOverlay.RefreshData (overlay off for this session)", ex);
            }
        }

        private static bool IsBayPick(ZtxBayTerminalComponent t, IReadOnlyList<PartOverlayRenderData> parts)
        {
            if (parts == null) return false;
            HashSet<IntVector2> frame = t.Bay.StructureCells;
            foreach (PartOverlayRenderData d in parts)
            {
                string id = d.Info.Rules?.ID.ToString();
                if (id == "ztx.hinges_pistons_docking.bay_terminal" || id == "ztx.hinges_pistons_docking.bay_station") return true;
                if (frame != null && frame.Contains(d.Info.Location)) return true;
            }
            return false;
        }

        public void DrawEarly(Ship ship, IBlueprintOverlaySettingsProvider settings) { }

        public void Draw(Ship ship, IBlueprintOverlaySettingsProvider settings)
        {
            if (s_failed || (_frame.Count == 0 && _design.Count == 0)) return;
            try
            {
                App.Graphics.SetToMaterial(null);
                foreach (IntVector2 c in _frame)
                    App.Graphics.Draw(new Quad(new Rect(c.X, c.Y, 1f, 1f), s_frameTint));
                foreach (IntVector2 c in _design)
                    App.Graphics.Draw(new Quad(new Rect(c.X, c.Y, 1f, 1f), s_designTint));
            }
            catch (Exception ex)
            {
                s_failed = true;
                Log.Exception("BayZoneOverlay.Draw (overlay off for this session)", ex);
            }
        }

        public void DrawLate(Ship ship, IBlueprintOverlaySettingsProvider settings) { }

        public void DrawLimited(Ship ship, IBlueprintOverlaySettingsProvider settings) { }

        public void Dispose() { }
    }
}
