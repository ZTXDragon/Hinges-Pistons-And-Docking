using System;
using Cosmoteer.Ships;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class ShipRecenter
    {
        private static readonly object s_tag = new object();

        public static Ship RecenterIfOutside(Ship ship)
        {
            try
            {
                IntRect? br = ship?.Parts?.BoundingRect;
                if (ship?.Sim == null || !br.HasValue) return ship;
                IntRect borders = ship.Rules.MaxBorders;
                if (borders.Contains(br.Value)) return ship;
                if (HingeJointManager.IsJointed(ship))
                {
                    Log.Info($"Ship {ship.UniqueID}: parts extend outside the build borders but the ship is jointed -- left as is.");
                    return ship;
                }
                var bounds = br.Value;
                var offset = new IntVector2(
                    (borders.Left + borders.Right) / 2 - (bounds.Left + bounds.Right) / 2,
                    (borders.Top + borders.Bottom) / 2 - (bounds.Top + bounds.Bottom) / 2);
                if (offset == IntVector2.Zero) return ship;

                int count = ship.Parts.Count;
                Ship moved = Regrid(ship, new GridTransform(0, offset));
                if (moved != ship)
                    Log.Info($"Ship {moved.UniqueID}: grid recentred by {offset} ({count} parts, was outside the +-{borders.Right} build borders).");
                return moved;
            }
            catch (Exception ex)
            {
                Log.Exception("ShipRecenter.RecenterIfOutside", ex);
                return ship;
            }
        }

        public static Ship Regrid(Ship ship, GridTransform xf)
        {
            try
            {
                if (ship?.Sim == null || xf.IsIdentity) return ship;
                var body = ship.Physics?.Body;
                if (body == null) return ship;

                Vector2 off = xf.ContinuousOffset * ship.Scale;
                float c = (float)DetMath.Cos(-xf.Theta), s = (float)DetMath.Sin(-xf.Theta);
                var local = new Vector2(-(off.X * c - off.Y * s), -(off.X * s + off.Y * c));

                Ship fresh = Ship.CloneEmptyFrom(ship, ship.Rand.UInt64());
                fresh.Rotation = (Direction)(body.Rotation - xf.Theta);
                fresh.Location = body.GetWorldPoint(local);
                ship.Sim.Ships.Add(fresh, giveDefaultCommand: false, reseedShipRand: false);
                if (fresh.Physics?.Body == null)
                {
                    ship.Sim.Ships.Remove(fresh, dispose: true, handleLostShip: false);
                    Log.Error($"Ship {ship.UniqueID}: regrid aborted -- the empty clone has no physics body.");
                    return ship;
                }
                fresh.Roles.CopyRolesFrom(ship.Roles);
                fresh.Metadata.CopyPartMetadataFrom(ship.Metadata);
                fresh.Metadata.CopyAestheticsFrom(ship.Metadata);
                fresh.SelfInvulnerable = ship.SelfInvulnerable;
                var pw = fresh.Physics.PhysicsWrapper;
                pw.LinearVelocity = body.LinearVelocity;
                pw.AngularVelocity = body.AngularVelocity;
                ShipIdentity identity = ShipIdentity.Capture(ship);

                if (!ShipMerger.Merge(fresh, ship, s_tag, xf))
                {
                    Log.Error($"Ship {ship.UniqueID}: regrid by {xf} failed (see line above).");
                    if (fresh.Sim != null && fresh.Parts.Count == 0)
                        ship.Sim.Ships.Remove(fresh, dispose: true, handleLostShip: false);
                    return ship;
                }
                fresh.Metadata.CullPartMetadata();
                identity?.ApplyTo(fresh);
                return fresh;
            }
            catch (Exception ex)
            {
                Log.Exception("ShipRecenter.Regrid", ex);
                return ship;
            }
        }
    }
}
