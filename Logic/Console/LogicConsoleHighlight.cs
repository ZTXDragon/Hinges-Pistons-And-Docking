using System;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using HVec = Halfling.Geometry.Vector2;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogicConsoleHighlight
    {
        private static SimRoot s_attachedSim;

        public static void Attach(SimRoot sim)
        {
            if (sim == null || ReferenceEquals(s_attachedSim, sim)) return;
            sim.Components.Add(new CustomDrawProxy(5050 , delegate
            {
                Draw();
            }));
            s_attachedSim = sim;
        }

        public static void Reset()
        {
            s_attachedSim = null;
        }

        private static void Draw()
        {
            try
            {
                if (!LogicConsoleWindow.IsShowing && !RailCartWindow.IsShowing && !RailStopWindow.IsShowing && !RemoteLinkWindow.IsShowing && !RailTerminalWindow.IsShowing) return;
                Part p = LogicConsoleWindow.HoveredPart;
                if (p == null || p.IsDestroyed) return;
                Ship ship = p.Ship;
                if (ship == null || ship.Physics == null || ship.Physics.Body == null) return;

                IntRect r = p.Rect;
                HVec scale = ship.Scale;
                HVec localCenter = new HVec((r.X + r.Width * 0.5f) * scale.X, (r.Y + r.Height * 0.5f) * scale.Y);
                HVec size = new HVec((r.Width + 0.2f) * scale.X, (r.Height + 0.2f) * scale.Y);
                HVec half = size * 0.5f;
                HVec c0 = localCenter + new HVec(-half.X, -half.Y);
                HVec c1 = localCenter + new HVec(half.X, -half.Y);
                HVec c2 = localCenter + new HVec(half.X, half.Y);
                HVec c3 = localCenter + new HVec(-half.X, half.Y);
                HVec center = ship.Physics.Body.GetWorldPoint(localCenter);
                HVec w0 = ship.Physics.Body.GetWorldPoint(c0);
                HVec w1 = ship.Physics.Body.GetWorldPoint(c1);
                HVec w2 = ship.Physics.Body.GetWorldPoint(c2);
                HVec w3 = ship.Physics.Body.GetWorldPoint(c3);

                var direction = new Direction(ship.Physics.Body.Rotation);
                RotRect rect = Rect.CenteredOn(in center, size).RotateAround(in center, direction);
                App.Graphics.SetToMaterial(null);
                var fill = new Quad(rect, new IntColor(64, 224, 255, 70));
                App.Graphics.Draw(in fill);

                Color border = new IntColor(64, 224, 255, 230);
                SkeletonOverlay.DrawLine(w0, w1, border, 0.08f);
                SkeletonOverlay.DrawLine(w1, w2, border, 0.08f);
                SkeletonOverlay.DrawLine(w2, w3, border, 0.08f);
                SkeletonOverlay.DrawLine(w3, w0, border, 0.08f);
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("LogicConsoleHighlight.Draw", ex);
                LogicConsoleWindow.HoveredPart = null;
            }
        }
    }
}
