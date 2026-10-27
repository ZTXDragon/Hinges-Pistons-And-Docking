using System;
using System.Collections.Generic;
using Cosmoteer;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using HVec = Halfling.Geometry.Vector2;

namespace ZTX.HingesPistonsDocking
{
    internal static class PistonRodRenderer
    {
        private static SimRoot s_attachedSim;
        private static readonly List<HingeJointManager.RodSegment> s_rods =
            new List<HingeJointManager.RodSegment>();

        private static readonly float[] s_bandWidths = { 34f / 64f, 32f / 64f, 24f / 64f, 10f / 64f, 2f / 64f };
        private static readonly Color[] s_bandColors =
        {
            new IntColor(28, 28, 28, 255),
            new IntColor(45, 45, 45, 255),
            new IntColor(67, 67, 67, 255),
            new IntColor(97, 97, 97, 255),
            new IntColor(131, 131, 131, 255),
        };
        private static readonly Color s_groove = new IntColor(28, 28, 28, 255);
        private const float GROOVE_PERIOD = 16f / 64f;
        private const float GROOVE_START = 3f / 64f;
        private const float GROOVE_THICKNESS = 2f / 64f;
        private const float GROOVE_MID_WIDTH = 16f / 64f;
        private const float GROOVE_SIDE_START = 1f / 64f;
        private const float GROOVE_SIDE_WIDTH = 8f / 64f;
        private const float GROOVE_SIDE_OFFSET = 12f / 64f;
        private const float HALF_PART = 0.5f;

        public static void Attach(SimRoot sim)
        {
            if (sim == null || ReferenceEquals(s_attachedSim, sim)) return;
            try
            {
                sim.Components.Add(new CustomDrawProxy(RenderBuckets.Overlays, delegate
                {
                    Draw();
                }));
                s_attachedSim = sim;
            }
            catch (Exception ex)
            {
                Log.Exception("PistonRodRenderer.Attach", ex);
            }
        }

        public static void Reset()
        {
            s_attachedSim = null;
        }

        private static void Draw()
        {
            try
            {
                HingeJointManager.GetSliderRods(s_rods);
                if (s_rods.Count == 0) return;
                foreach (HingeJointManager.RodSegment rod in s_rods)
                {
                    float len = rod.A.DistanceTo(in rod.B);
                    if (len < 0.01f) continue;
                    HVec along = (rod.B - rod.A) / len;
                    float scale = rod.Scale > 0f ? rod.Scale : 1f;
                    HVec a = rod.A + along * ((HALF_PART + rod.Rods) * scale);
                    HVec b = rod.B - along * ((HALF_PART + rod.TailCells) * scale);
                    if ((b.X - a.X) * along.X + (b.Y - a.Y) * along.Y <= 0.01f * scale) continue;
                    for (int i = 0; i < s_bandWidths.Length; i++)
                        DrawLine(a, b, s_bandColors[i], s_bandWidths[i] * scale, 0f);
                    float open = a.DistanceTo(in b);
                    HVec normal = new HVec(-along.Y, along.X);
                    HVec mid = normal * (GROOVE_MID_WIDTH * scale / 2f);
                    HVec side = normal * (GROOVE_SIDE_WIDTH * scale / 2f);
                    HVec sideShift = normal * (GROOVE_SIDE_OFFSET * scale);
                    float t = GROOVE_THICKNESS * scale;
                    for (float p = 0f; p + GROOVE_START * scale + t <= open; p += GROOVE_PERIOD * scale)
                    {
                        HVec c = a + along * (p + GROOVE_START * scale + t / 2f);
                        DrawLine(c - mid, c + mid, s_groove, t, 0f);
                        HVec s = a + along * (p + GROOVE_SIDE_START * scale + t / 2f);
                        DrawLine(s + sideShift - side, s + sideShift + side, s_groove, t, 0f);
                        DrawLine(s - sideShift - side, s - sideShift + side, s_groove, t, 0f);
                    }
                }
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("PistonRodRenderer.Draw", ex);
            }
        }

        private static void DrawLine(HVec a, HVec b, Color color, float thickness, float sideOffset)
        {
            float len = a.DistanceTo(in b);
            if (len < 0.01f) return;
            HVec mid = (a + b) / 2f;
            Direction direction = a.DirectionTo(in b);
            if (sideOffset != 0f)
            {
                HVec along = (b - a) / len;
                var normal = new HVec(-along.Y, along.X);
                mid += normal * sideOffset;
            }
            RotRect rect = Rect.CenteredOn(in mid, new HVec(len, thickness)).RotateAround(in mid, direction);
            App.Graphics.SetToMaterial(null);
            var quad = new Quad(rect, color);
            App.Graphics.Draw(in quad);
        }
    }
}
