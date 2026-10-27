using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships;
using Halfling.Physics2D.Collision;
using Halfling.Physics2D.Collision.Shapes;
using Halfling.Physics2D.Dynamics;
using Halfling.Physics2D.Dynamics.Contacts;

namespace ZTX.HingesPistonsDocking
{
    internal static class LaneGuard
    {
        private sealed class Lane
        {
            public Vector2 Axis;
            public float Tolerance;
            public int Tick;
        }

        private const int STALE_TICKS = 2;

        private static readonly ConditionalWeakTable<Body, Lane> s_lanes = new ConditionalWeakTable<Body, Lane>();
        private static readonly ConditionalWeakTable<World, object> s_installed = new ConditionalWeakTable<World, object>();
        private static readonly object s_lock = new object();
        private static volatile int s_lastTick = int.MinValue / 2;
        [ThreadStatic] private static List<Vector2> s_a;
        [ThreadStatic] private static List<Vector2> s_b;

        public static void Register(Ship section, Vector2 worldAxis, float toleranceCells)
        {
            Body body = section?.Physics?.Body;
            if (body?.World == null) return;
            Install(body.World);
            int tick = ZtxSignalComponent.Clock(section);
            s_lastTick = tick;
            s_lanes.AddOrUpdate(body, new Lane
            {
                Axis = worldAxis,
                Tolerance = Math.Max(0f, toleranceCells) * section.WorldUniformScale,
                Tick = tick,
            });
        }

        private static void Install(World world)
        {
            if (s_installed.TryGetValue(world, out _)) return;
            lock (s_lock)
            {
                if (s_installed.TryGetValue(world, out _)) return;
                PreSolveDelegate previous = world.ContactManager.PreSolve;
                world.ContactManager.PreSolve = (Contact c, ref Manifold old) =>
                {
                    previous?.Invoke(c, ref old);
                    try { Filter(c); }
                    catch (Exception ex) { LogThrottle.Exception("LaneGuard.PreSolve", ex); }
                };
                s_installed.Add(world, new object());
                Log.Info("Lane guard on the physics world: contacts beside a sliding cart's or piston's lane (flush, within the tolerance) no longer hold it.");
            }
        }

        private static void Filter(Contact c)
        {
            if (c == null || !c.Enabled) return;
            Fixture fa = c.FixtureA, fb = c.FixtureB;
            Body ba = fa?.Body, bb = fb?.Body;
            if (ba == null || bb == null || ba == bb || fa.IsSensor || fb.IsSensor) return;
            if (ba.UserData is Ship any && ZtxSignalComponent.Clock(any) - s_lastTick > STALE_TICKS) return;
            Lane la = Live(ba), lb = Live(bb);
            if (la == null && lb == null) return;
            if ((la != null && !InLane(fa, fb, la.Axis, la.Tolerance)) || (lb != null && !InLane(fa, fb, lb.Axis, lb.Tolerance)))
                c.Enabled = false;
        }

        private static Lane Live(Body body)
        {
            if (!s_lanes.TryGetValue(body, out Lane lane)) return null;
            if (body.UserData is Ship ship && ZtxSignalComponent.Clock(ship) - lane.Tick > STALE_TICKS) return null;
            return lane;
        }

        public static bool ContactInLane(Contact c, Vector2 worldAxis, float toleranceWorld)
        {
            if (c?.FixtureA?.Body == null || c.FixtureB?.Body == null) return true;
            return InLane(c.FixtureA, c.FixtureB, worldAxis, toleranceWorld);
        }

        private static bool InLane(Fixture fa, Fixture fb, Vector2 axis, float tolerance)
        {
            if (!LaneMath.Across(axis, out Vector2 across)) return true;
            if (!Span(fa, across, ref s_a, out float aMin, out float aMax)) return true;
            if (!Span(fb, across, ref s_b, out float bMin, out float bMax)) return true;
            return LaneMath.Overlap(aMin, aMax, bMin, bMax) > tolerance;
        }

        public static bool FixtureSpan(Fixture f, Vector2 dir, out float min, out float max)
        {
            min = max = 0f;
            if (f?.Body == null) return false;
            return Span(f, dir, ref s_a, out min, out max);
        }

        private static bool Span(Fixture f, Vector2 dir, ref List<Vector2> scratch, out float min, out float max)
        {
            min = max = 0f;
            if (f.Shape is PolygonShape p)
            {
                scratch ??= new List<Vector2>(8);
                scratch.Clear();
                foreach (Vector2 v in p.Vertices) scratch.Add(f.Body.GetWorldPoint(v));
                if (scratch.Count == 0) return false;
                LaneMath.Project(scratch, dir, out min, out max);
                return true;
            }
            if (f.Shape is CircleShape cs)
            {
                float d = Vector2.Dot(f.Body.GetWorldPoint(cs.Position), dir);
                min = d - cs.Radius;
                max = d + cs.Radius;
                return true;
            }
            return false;
        }
    }
}
