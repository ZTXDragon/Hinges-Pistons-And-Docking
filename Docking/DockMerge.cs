using System;
using System.Collections.Generic;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class DockMergeState
    {
        public bool Pending;
        public float RefAngle;
        public Vector2 AnchorGridA;
        public Vector2 AnchorGridB;
        public int Ticks;
        public ShipIdentity MergedAwayIdentity;
        public bool FromMergedAwayShip;
        public bool Blocked;
        public bool StayWelded;
    }

    internal enum MergeOutcome
    {
        Merged,
        StayWelded,
        Failed,
    }

    internal static class DockMerge
    {
        public const float MAX_ANGLE_ERR = 0.02f;
        public const float MAX_GAP_CELLS = 0.06f;
        public const int MIN_TICKS = 5;

        public static bool Settled(int ticks, float angErr, float gapCells) =>
            ticks >= MIN_TICKS && angErr <= MAX_ANGLE_ERR && gapCells <= MAX_GAP_CELLS;

        public static void Measure(DockMergeState st, Ship shipA, Ship shipB, out float angErr, out float gapCells)
        {
            var bodyA = shipA.Physics.Body;
            var bodyB = shipB.Physics.Body;
            angErr = Math.Abs(DockGeometry.WrapAngle((bodyB.Rotation - bodyA.Rotation) - st.RefAngle));
            Vector2 wA = bodyA.GetWorldPoint(st.AnchorGridA * shipA.Scale);
            Vector2 wB = bodyB.GetWorldPoint(st.AnchorGridB * shipB.Scale);
            gapCells = (wA - wB).Length / shipA.Scale.X;
        }

        public static MergeOutcome Merge(object tag, string what, Part part, Part otherPart, Func<Part, DockMergeState> stateOf)
        {
            Ship shipA = part.Ship;
            Ship shipB = otherPart.Ship;
            Ship survivor;
            Ship victim;
            Part dockS;
            Part dockV;
            if (shipA.Physics.Body.Mass >= shipB.Physics.Body.Mass)
            {
                survivor = shipA; victim = shipB; dockS = part; dockV = otherPart;
            }
            else
            {
                survivor = shipB; victim = shipA; dockS = otherPart; dockV = part;
            }

            if (!GridTransform.TryFromDockPair(dockS.Location, DockGeometry.FacingDir(dockS),
                    dockV.Location, DockGeometry.FacingDir(dockV), out GridTransform xf))
            {
                Log.Error($"{what}: no 90-degree transform opposes the facings (should be impossible) -- staying welded.");
                return MergeOutcome.StayWelded;
            }

            float bodyDelta = DockGeometry.WrapAngle(victim.Physics.Body.Rotation - survivor.Physics.Body.Rotation - xf.Theta);
            if (Math.Abs(bodyDelta) > 0.3f)
            {
                Log.Error($"{what}: transform rotation {xf.Theta:F2} disagrees with settled body delta by {bodyDelta:F2} rad -- staying welded.");
                return MergeOutcome.StayWelded;
            }

            ShipIdentity mergedAway = ShipIdentity.Capture(victim);
            List<IntVector2> memberCells = DockThrust.MemberCells(victim, xf);
            DockMergeState stS = stateOf(dockS);
            DockMergeState stV = stateOf(dockV);

            if (!ShipMerger.Merge(survivor, victim, tag, xf))
                return HingeJointManager.HasPinWithTag(tag) ? MergeOutcome.StayWelded : MergeOutcome.Failed;

            DockThrust.RecordMembers(survivor, dockS, memberCells);
            if (stS != null) { stS.MergedAwayIdentity = mergedAway; stS.FromMergedAwayShip = false; }
            if (stV != null) { stV.MergedAwayIdentity = mergedAway; stV.FromMergedAwayShip = true; }
            Log.Info($"{what}: ships merged into {survivor.UniqueID} -- crew can walk across. Switch either dock off to disconnect; the detached half gets back the identity of '{mergedAway?.Name}' (remembered until save/load).");
            return MergeOutcome.Merged;
        }

        public static Ship SplitSeam(Ship ship, Dictionary<Part, Part> cutPartner, Part seed, Part keep,
            ShipIdentity mergedAway, bool farFromMergedAway, string what, out bool bridged)
        {
            bridged = false;
            var component = new HashSet<Part> { seed };
            var queue = new Queue<Part>();
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                Part current = queue.Dequeue();
                foreach (Part next in StructuralAdjacency.Neighbours(ship, current, ignoreJointSeams: false))
                {
                    if (component.Contains(next)) continue;
                    if (cutPartner.TryGetValue(current, out Part blocked) && blocked == next) continue;
                    component.Add(next);
                    queue.Enqueue(next);
                }
            }

            if (component.Contains(keep))
            {
                bridged = true;
                return null;
            }

            var farParts = new List<Part>(component);
            var memberKeys = new HashSet<int>();
            foreach (KeyValuePair<Part, Part> kv in cutPartner)
            {
                memberKeys.Add(DockThrust.KeyOf(kv.Key));
                if (kv.Value != null) memberKeys.Add(DockThrust.KeyOf(kv.Value));
            }
            Ship shipB = ShipSplitter.Split(ship, farParts, keep);
            if (shipB == null)
            {
                Log.Error($"{what}: seam split failed -- ships remain merged.");
                return null;
            }
            shipB = ShipRecenter.RecenterIfOutside(shipB);
            RestoreIdentities(ship, shipB, mergedAway, farFromMergedAway, what);
            DockThrust.ClearMembers(ship, memberKeys);
            DockThrust.ClearMembers(shipB, memberKeys);
            Log.Info($"{what}: ship {ship.UniqueID} disconnected -- {farParts.Count} parts detached as ship {shipB.UniqueID}.");
            return shipB;
        }

        private static void RestoreIdentities(Ship remaining, Ship detached, ShipIdentity mergedAway, bool detachedIsMergedAway, string what)
        {
            try
            {
                if (mergedAway == null)
                {
                    Log.Info($"{what}: no remembered identity for this pair (merged before a save/load?) -- both halves keep the current name.");
                    return;
                }
                if (detachedIsMergedAway)
                {
                    mergedAway.ApplyTo(detached);
                    Log.Info($"{what}: detached ship {detached.UniqueID} is '{mergedAway.Name}' again.");
                }
                else
                {
                    ShipIdentity current = ShipIdentity.Capture(remaining);
                    current?.ApplyTo(detached);
                    mergedAway.ApplyTo(remaining);
                    Log.Info($"{what}: detached ship {detached.UniqueID} keeps '{current?.Name}'; remaining ship {remaining.UniqueID} is '{mergedAway.Name}' again.");
                }
            }
            catch (Exception ex)
            {
                Log.Exception("DockMerge.RestoreIdentities", ex);
            }
        }
    }
}
