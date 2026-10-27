using System;
using System.Collections.Generic;
using System.Diagnostics;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Parts.Thrusters;
using Halfling.Geometry;
using Halfling.Physics2D.Dynamics;
using Halfling.Physics2D.Dynamics.Joints;
using Halfling.Scene2D;
using Halfling.Timing;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    [HarmonyPatch(typeof(CommandManager), "SetThrusterActivations")]
    public static class CommandSuppressionPatch
    {
        static bool Prefix(CommandManager __instance, FixedUpdater fixedUpdater)
        {
            try
            {
                Ship railShip = __instance.Ship;
                if (railShip?.Physics?.Body != null && HingeJointManager.IsRailDriven(railShip) &&
                    !ReferenceEquals(railShip, HingeJointManager.GetDesignatedPilot(railShip)))
                {
                    if (HingeJointManager.IsRailThrusting(railShip)) return false;
                    ThrusterManager railThrusters = railShip.Thrusters;
                    if (railThrusters != null && railThrusters.Count > 0)
                    {
                        railThrusters.CalculateDesiredSRA(Vector3.Zero, fixedUpdater.Interval, new Vector3(1f, 1f, 1f));
                        railThrusters.CommitActivationLevels(fixedUpdater.Interval);
                    }
                    return false;
                }
                if (!AssemblyFlightFeatures.Enabled) return true;
                Ship ship = __instance.Ship;
                if (ship?.Physics?.Body == null) return true;
                if (!HingeJointManager.IsRigidlyJointed(ship))
                {
                    if (!HingeJointManager.IsJointed(ship) || FlightStage.FliesOwnOrder(ship, __instance.CurConfirmedCommand)) return true;
                    return !TrailerEcho(__instance, ship, fixedUpdater);
                }

                if (AssemblyFlightDiag.ENABLED) DragMomentMeasurement(ship, fixedUpdater);

                Ship pilot = HingeJointManager.GetDesignatedPilot(ship);
                if (ReferenceEquals(ship, pilot))
                {
                    if (FlightStage.FliesOwnOrder(ship, __instance.CurConfirmedCommand)) return true;
                    return !TrailerEcho(__instance, ship, fixedUpdater);
                }

                ThrusterManager thrusters = ship.Thrusters;
                if (thrusters == null || thrusters.Count == 0) return false;
                Time interval = fixedUpdater.Interval;

                if (FlightStage.IsServoOwned(ship)) return false;

                AssemblyEcho.Desire d = (pilot?.Physics?.Body != null) ? AssemblyEcho.GetFresh(pilot) : null;
                if (d != null)
                {
                    if (ship.Sim != null)
                        thrusters._lastCommandedTime = ship.Sim.LogicalTime;
                    ThrusterKeepAlive.Enable(ship);

                    double delta = pilot.Physics.Body.Rotation - ship.Physics.Body.Rotation;
                    float sin = (float)DetMath.Sin(delta);
                    float cos = (float)DetMath.Cos(delta);
                    var sra = new Vector3(
                        d.SRA.X * cos - d.SRA.Y * sin,
                        d.SRA.X * sin + d.SRA.Y * cos,
                        d.SRA.Z);
                    thrusters.CalculateDesiredSRA(sra, interval, d.SrfFactors);
                    thrusters.CommitActivationLevels(interval);
                }
                else
                {
                    thrusters.CalculateDesiredSRA(Vector3.Zero, interval, new Vector3(1f, 1f, 1f));
                    thrusters.CommitActivationLevels(interval);
                }
                return false;
            }
            catch (Exception ex)
            {
                Log.Exception("CommandSuppressionPatch.Prefix", ex);
                return true;
            }
        }

        private static bool TrailerEcho(CommandManager cm, Ship ship, FixedUpdater fixedUpdater)
        {
            ThrusterManager thrusters = ship.Thrusters;
            if (thrusters == null || thrusters.Count == 0) return false;
            Body body = ship.Physics.Body;
            long started = FlightProbe.ENABLED ? Stopwatch.GetTimestamp() : 0;

            bool walked = !FlightStage.TryGetTrailerHead(ship, out Ship head, out AssemblyEcho.Desire d);
            if (walked) WalkForHead(ship, out head, out d);
            if (head == null || head.Physics?.Body == null)
            {
                FlightProbe.OnTrailer(ship, null, false, walked, Vector3.Zero, Vector3.Zero, FlightProbe.ENABLED ? Stopwatch.GetTimestamp() - started : 0);
                return false;
            }

            Time interval = fixedUpdater.Interval;
            if (ship.Sim != null)
                thrusters._lastCommandedTime = ship.Sim.LogicalTime;
            ThrusterKeepAlive.Enable(ship);

            Vector3 srfFactors = new Vector3(1f, 1f, 1f);
            float fxW = 0f, fyW = 0f;
            if (d != null)
            {
                Body hb = head.Physics.Body;
                float hr = hb.Rotation;
                float hc = (float)DetMath.Cos(hr), hs = (float)DetMath.Sin(hr);
                float ax = d.SRA.X * hc - d.SRA.Y * hs;
                float ay = d.SRA.X * hs + d.SRA.Y * hc;
                float alpha = d.SRA.Z;
                float omega = hb.AngularVelocity;
                Vector2 r = body.WorldCenter - hb.WorldCenter;
                ax += -alpha * r.Y - omega * omega * r.X;
                ay += alpha * r.X - omega * omega * r.Y;
                float m = body.Mass;
                fxW = m * ax;
                fyW = m * ay;
                srfFactors = d.SrfFactors;
            }

            float tau = 0f;
            if ((fxW != 0f || fyW != 0f) && HingeJointManager.TryGetUpstreamRevolute(head, ship, out RevoluteJoint rj))
            {
                Vector2 q = rj.WorldAnchorA;
                Vector2 c = body.WorldCenter;
                tau = (q.X - c.X) * fyW - (q.Y - c.Y) * fxW;
            }

            float br = body.Rotation;
            float bc = (float)DetMath.Cos(br), bs = (float)DetMath.Sin(br);
            Vector3 demanded = new Vector3(fxW * bc + fyW * bs, -fxW * bs + fyW * bc, tau);
            Vector3 achieved = thrusters.CalculateDesiredSRF(demanded, interval, srfFactors);
            thrusters.CommitActivationLevels(interval);
            FlightProbe.OnTrailer(ship, head, d != null, walked, demanded, achieved, FlightProbe.ENABLED ? Stopwatch.GetTimestamp() - started : 0);
            return true;
        }

        private static void DragMomentMeasurement(Ship ship, FixedUpdater fixedUpdater)
        {
            try
            {
                var body = ship.Physics.Body;
                var solver = body.LinearDragSolver;
                if (solver == null) return;
                if (!HingeJointManager.TryGetAssemblyProperties(ship, out _, out _, out Vector2 com)) return;

                Vector2 v = body.LinearVelocity;
                float speed = v.Length;
                if (speed < 0.01f) return;
                float dt = fixedUpdater.Interval;
                if (dt <= 0f) return;
                float newSpeed = solver.SolveScalarDrag(dt, speed, ship.Nebulas.LinearDampingFactor);
                float dragForce = body.Mass * (speed - newSpeed) / dt;
                if (dragForce <= 0f) return;
                float fdx = (0f - v.X / speed) * dragForce;
                float fdy = (0f - v.Y / speed) * dragForce;
                float rx = body.WorldCenter.X - com.X;
                float ry = body.WorldCenter.Y - com.Y;
                float dragMoment = rx * fdy - ry * fdx;

                AssemblyFlightDiag.OnDrag(ship, speed, newSpeed, dt, dragMoment, body.AngularVelocity);
            }
            catch (Exception ex)
            {
                Log.Exception("CommandSuppressionPatch.DragMomentMeasurement", ex);
            }
        }

        private static void WalkForHead(Ship ship, out Ship head, out AssemblyEcho.Desire d)
        {
            var members = new List<Ship>();
            HingeJointManager.GetAssemblyShips(ship, members, rigidOnly: false);
            head = null;
            d = null;
            foreach (Ship m in members)
            {
                if (m == null || ReferenceEquals(m, ship) || m.Physics?.Body == null) continue;
                if (!FlightStage.FliesOwnOrder(m, m.Commands?.CurConfirmedCommand)) continue;
                AssemblyEcho.Desire cand = AssemblyEcho.GetFresh(m);
                if (head == null || cand != null) { head = m; d = cand; }
                if (cand != null) break;
            }
        }
    }
}
