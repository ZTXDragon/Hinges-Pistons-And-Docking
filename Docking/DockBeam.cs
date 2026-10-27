using System;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation.MediaEffects;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class DockBeam
    {
        internal static void UpdateBeam(DockBuildState st, Part dockPart, Part target, MultiMediaEffectRules beamRules, MultiMediaEffectRules hitRules)
        {
            Ship ship = dockPart?.Ship;
            if (target == null || target.Ship == null || beamRules == null || ship?.Sim == null || ship.Physics?.Body == null || !dockPart.IsWithinSightOfLocalPlayer)
            {
                EndBeam(st);
                return;
            }
            Vector2 from = dockPart.WorldPhysicalCenter;
            Vector2 to = target.WorldPhysicalCenter;
            Direction rot = from.DirectionTo(to);
            float len = (float)Math.Sqrt((to - from).LengthSquared);
            if ((st.Beam != null && !ReferenceEquals(st.BeamShip, ship)) || (st.BeamHit != null && !ReferenceEquals(st.BeamHitShip, target.Ship)))
                EndBeam(st);

            if (st.Beam == null)
            {
                st.Beam = MultiMediaEffectNode.Alloc(beamRules, ship, ship.TransformPointFromWorld(from), ship.TransformRotationFromWorld(rot));
                st.BeamShip = ship;
                st.Beam.PlayContinuous();
            }
            else
            {
                st.Beam.Location = ship.TransformPointFromWorld(from);
                st.Beam.Rotation = ship.TransformRotationFromWorld(rot);
            }
            st.Beam.Parameters.BeamLength = len;
            st.Beam.Parameters.FrameOfReference = ship.Physics.Body.GetLinearVelocityFromWorldPoint(from);
            st.Beam.Parameters.HitPart = target;
            st.Beam.Parameters.HitShip = target.Ship;

            if (hitRules != null)
            {
                Ship hitShip = target.Ship;
                if (st.BeamHit == null)
                {
                    st.BeamHit = MultiMediaEffectNode.Alloc(hitRules, target.Ship, hitShip.TransformPointFromWorld(to), hitShip.TransformRotationFromWorld(rot));
                    st.BeamHitShip = hitShip;
                    st.BeamHit.PlayContinuous();
                }
                else
                {
                    st.BeamHit.Location = hitShip.TransformPointFromWorld(to);
                    st.BeamHit.Rotation = hitShip.TransformRotationFromWorld(rot);
                }
                st.BeamHit.Parameters.FrameOfReference = ship.Physics.Body.GetLinearVelocityFromWorldPoint(to);
                st.BeamHit.Parameters.HitPart = target;
                st.BeamHit.Parameters.HitShip = target.Ship;
            }
        }

        internal static void EndBeam(DockBuildState st)
        {
            if (st == null) return;
            try
            {
                st.Beam?.EndContinuous();
                st.BeamHit?.EndContinuous();
            }
            catch (Exception ex)
            {
                Log.Exception("DockBeam.EndBeam", ex);
            }
            st.Beam = null;
            st.BeamHit = null;
        }
    }
}
