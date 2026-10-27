using System;
using System.Collections.Generic;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using HVec = Halfling.Geometry.Vector2;

namespace ZTX.HingesPistonsDocking
{
    internal static class SkeletonOverlay
    {
        private static bool s_enabled;
        private static SimRoot s_attachedSim;

        private static readonly List<HingeJointManager.SkeletonLink> s_links =
            new List<HingeJointManager.SkeletonLink>();
        private static readonly List<(HVec Com, float Mass)> s_assemblyComs =
            new List<(HVec, float)>();

        public static void Toggle(SimRoot sim)
        {
            s_enabled = !s_enabled;
            if (s_enabled && sim != null && !ReferenceEquals(s_attachedSim, sim))
            {
                sim.Components.Add(new CustomDrawProxy(5050 , delegate
                {
                    Draw();
                }));
                s_attachedSim = sim;
            }
            Log.Info("Skeleton overlay " + (s_enabled ? "ON" : "OFF") + ".");
        }

        public static void Reset()
        {
            s_attachedSim = null;
            s_enabled = false;
        }

        private static void Draw()
        {
            try
            {
                if (!s_enabled) return;
                HingeJointManager.GetOverlayData(s_links, s_assemblyComs);
                if (s_links.Count == 0) return;

                foreach (HingeJointManager.SkeletonLink link in s_links)
                {
                    Color lineColor = link.IsWeld
                        ? new IntColor(255, 160, 48, 200)
                        : new IntColor(64, 160, 255, 200);
                    DrawLine(link.ComA, link.Anchor, lineColor, 0.14f);
                    DrawLine(link.Anchor, link.ComB, lineColor, 0.14f);
                    DrawMarker(link.ComA, 0.55f, new IntColor(64, 255, 96, 220), rotated: false);
                    DrawMarker(link.ComB, 0.55f, new IntColor(64, 255, 96, 220), rotated: false);
                    DrawMarker(link.Anchor, 0.45f, new IntColor(64, 224, 255, 230), rotated: true);
                }
                foreach ((HVec com, float mass) in s_assemblyComs)
                {
                    DrawMarker(com, 1.0f, new IntColor(255, 224, 32, 235), rotated: false);
                    DrawMarker(com, 0.4f, new IntColor(40, 40, 40, 235), rotated: false);
                }
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("SkeletonOverlay.Draw", ex);
                s_enabled = false;
            }
        }

        internal static void DrawLine(HVec a, HVec b, Color color, float thickness)
        {
            float len = a.DistanceTo(in b);
            if (len < 0.01f) return;
            HVec mid = (a + b) / 2f;
            Direction direction = a.DirectionTo(in b);
            RotRect rect = Rect.CenteredOn(in mid, new HVec(len, thickness)).RotateAround(in mid, direction);
            App.Graphics.SetToMaterial(null);
            var quad = new Quad(rect, color);
            App.Graphics.Draw(in quad);
        }

        private static void DrawMarker(HVec p, float size, Color color, bool rotated)
        {
            App.Graphics.SetToMaterial(null);
            if (rotated)
            {
                RotRect rect = Rect.CenteredOn(in p, new HVec(size, size)).RotateAround(in p, new Direction(0.785398f));
                var quad = new Quad(rect, color);
                App.Graphics.Draw(in quad);
            }
            else
            {
                var quad = new Quad(Rect.CenteredOn(in p, new HVec(size, size)), color);
                App.Graphics.Draw(in quad);
            }
        }
    }
}
