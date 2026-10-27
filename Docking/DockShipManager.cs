using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Crew;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Crew;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Geometry;
using Halfling.Physics2D.Dynamics;
using Halfling.Scene2D;
using Halfling.Timing;

namespace ZTX.HingesPistonsDocking
{
    internal class DockShipManager : ShipComponent
    {
        private static readonly ConditionalWeakTable<Ship, DockShipManager> s_managers =
            new ConditionalWeakTable<Ship, DockShipManager>();

        private static readonly ID<PartToggleGuiRules> s_activeToggleId =
            new ID<PartToggleGuiRules>("ztx_dock_active");

        public static readonly ID<PartToggleGuiRules> PadActiveId = new ID<PartToggleGuiRules>("ztx_pad_active");

        private const int SERVICE_INTERVAL_TICKS = 30;

        private readonly List<ZtxDockClampComponent> _clamps = new List<ZtxDockClampComponent>();
        private readonly List<ZtxDockPadComponent> _pads = new List<ZtxDockPadComponent>();
        private int _tickCounter;

        public DockShipManager()
        {
            base.FixedUpdateBucket = 10;
            base.FixedUpdatingEnabled = true;
        }

        #region Static access + docking toggle

        public static DockShipManager GetOrCreate(Ship ship)
        {
            if (ship == null) return null;
            if (s_managers.TryGetValue(ship, out DockShipManager manager)) return manager;

            manager = new DockShipManager();
            DockShipManager captured = manager;
            Ship capturedShip = ship;

            if (ship.Sim != null)
            {
                ship.Sim.EnqueueDeterministic(ship.UniqueID, delegate
                {
                    capturedShip.Components.Add(captured);
                });
            }
            else
            {
                ship.Components.Add(manager);
            }

            s_managers.Add(ship, manager);
            return manager;
        }

        public static DockShipManager TryGet(Ship ship)
        {
            if (ship == null) return null;
            s_managers.TryGetValue(ship, out DockShipManager manager);
            return manager;
        }

        public static bool IsDockOn(Part part)
        {
            if (part == null) return false;
            bool? signal = LogicSource.Read(part);
            if (signal != null) return signal.Value;
            PartUIToggle toggle = PartUIToggle.GetOnPart(part, s_activeToggleId) ?? PartUIToggle.GetOnPart(part, PadActiveId);
            return toggle == null || toggle.IsToggleOn;
        }

        #endregion

        #region Registration

        public void RegisterClamp(ZtxDockClampComponent clamp)
        {
            if (!_clamps.Contains(clamp)) _clamps.Add(clamp);
        }

        public void DeregisterClamp(ZtxDockClampComponent clamp)
        {
            _clamps.Remove(clamp);
            DockThrust.Clear(clamp);
        }

        public void RegisterPad(ZtxDockPadComponent pad)
        {
            if (!_pads.Contains(pad)) _pads.Add(pad);
        }

        public void DeregisterPad(ZtxDockPadComponent pad)
        {
            _pads.Remove(pad);
            DockThrust.Clear(pad);
        }

        #endregion

        public override void FixedUpdate(FixedUpdater fixedUpdater, SceneRoot root)
        {
            try
            {
                if (_clamps.Count == 0 && _pads.Count == 0) return;
                AssemblyIndex.Tick(base.Ship);
                bool pending = false;
                foreach (ZtxDockClampComponent clamp in _clamps)
                    if (clamp.Merge.Pending && clamp.OwnsJoint) { pending = true; break; }
                if (pending)
                    foreach (ZtxDockClampComponent clamp in _clamps.ToArray())
                        if (clamp.Merge.Pending && clamp.OwnsJoint) ServicePendingMerge(clamp);
                _tickCounter++;
                if (_tickCounter % SERVICE_INTERVAL_TICKS != 0) return;

                Ship ship = base.Ship;
                if (ship?.Physics?.Body == null) return;

                foreach (ZtxDockClampComponent clamp in _clamps.ToArray())
                {
                    ServiceClamp(ship, clamp);
                    UpdateClampThrust(ship, clamp);
                    UpdateClampJoin(ship, clamp);
                    UpdateClampService(ship, clamp);
                }
                foreach (ZtxDockPadComponent pad in _pads.ToArray())
                {
                    ServicePad(ship, pad);
                    UpdatePadThrust(ship, pad);
                    UpdatePadJoin(pad);
                }
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("DockShipManager.FixedUpdate", ex);
            }
        }

        #region Clamp servicing

        public static List<Ship> HeldShips(Ship carrier)
        {
            var result = new List<Ship>();
            foreach (Ship held in BayManager.HeldShips(carrier, finishedOnly: true))
                if (!ReferenceEquals(held, carrier) && !result.Contains(held)) result.Add(held);
            DockShipManager mgr = TryGet(carrier);
            if (mgr == null) return result;
            foreach (ZtxDockClampComponent clamp in mgr._clamps)
            {
                Ship other = clamp.PairedClamp?.Part?.Ship;
                if (other == null || ReferenceEquals(other, carrier) || result.Contains(other)) continue;
                if (HingeJointManager.HasPinWithTag(clamp) || HingeJointManager.HasPinWithTag(clamp.PairedClamp)) result.Add(other);
            }
            foreach (ZtxDockPadComponent pad in mgr._pads)
            {
                Ship other = pad.DockedShip;
                if (other == null || ReferenceEquals(other, carrier) || result.Contains(other)) continue;
                if (HingeJointManager.HasPinWithTag(pad)) result.Add(other);
            }
            return result;
        }

        public static List<Ship> ClampSupplyShips(Ship carrier)
        {
            var result = new List<Ship>();
            DockShipManager mgr = TryGet(carrier);
            if (mgr == null) return result;
            foreach (ZtxDockClampComponent clamp in mgr._clamps)
            {
                Part op = clamp.PairedClamp?.Part;
                Ship other = op?.Ship;
                if (other?.Sim == null || ReferenceEquals(other, carrier) || result.Contains(other)) continue;
                if (!HingeJointManager.HasPinWithTag(clamp) && !HingeJointManager.HasPinWithTag(clamp.PairedClamp)) continue;
                if (DockTransfer.Flows(ToggleMode(clamp.Part, ClampResupplyId), ToggleMode(op, ClampResupplyId))) result.Add(other);
            }
            return result;
        }

        public static List<Ship> PadShips(Ship carrier)
        {
            var result = new List<Ship>();
            DockShipManager mgr = TryGet(carrier);
            if (mgr == null) return result;
            foreach (ZtxDockPadComponent pad in mgr._pads)
            {
                Ship other = pad.DockedShip;
                if (other?.Sim == null || ReferenceEquals(other, carrier) || result.Contains(other)) continue;
                if (HingeJointManager.HasPinWithTag(pad)) result.Add(other);
            }
            return result;
        }

        private static void UpdateClampJoin(Ship ship, ZtxDockClampComponent clamp)
        {
            ZtxDockClampComponent partner = clamp.PairedClamp;
            Ship other = partner?.Part?.Ship;
            if (!clamp.OwnsJoint || other == null || ReferenceEquals(other, ship) || !HingeJointManager.HasPinWithTag(clamp)) return;
            bool joined = DockJoin.Joined(false, DockJoin.IsJoinOn(clamp.Part), DockJoin.IsJoinOn(partner.Part));
            Ship visitor = ReferenceEquals(DockThrust.Carrier(ship, other), ship) ? other : ship;
            HingeJointManager.SetDockSeparate(clamp, !joined, visitor);
        }

        public static readonly ID<PartToggleGuiRules> ClampResupplyId = new ID<PartToggleGuiRules>("ztx_dock_resupply");

        private static void UpdateClampService(Ship ship, ZtxDockClampComponent clamp)
        {
            Part part = clamp.Part;
            Part op = clamp.PairedClamp?.Part;
            Ship other = op?.Ship;
            if (part == null || other == null || ReferenceEquals(other, ship)) { clamp.ServiceStatus = ""; return; }
            if (!HingeJointManager.HasPinWithTag(clamp) && !HingeJointManager.HasPinWithTag(clamp.PairedClamp)) { clamp.ServiceStatus = ""; return; }
            try
            {
                string crew = "";
                int crewMine = HatchLinks.CrewMode(part, HatchLinks.ClampCrewId), crewTheirs = HatchLinks.CrewMode(op, HatchLinks.ClampCrewId);
                if (DockJoin.Merges(DockJoin.ModeOf(part), DockJoin.ModeOf(op)))
                {
                    if (crewMine != DockTransfer.NONE) crew = "crew: none moved -- the clamps merge the ships into one crew";
                }
                else if (DockTransfer.Flows(crewMine, crewTheirs))
                {
                    bool assigned = CrewSource.s_targetedParts.TryGetValue(part, out List<CrewSource> sources)
                                    && sources.Any(cs => ReferenceEquals(cs?.Part?.Ship, ship));
                    List<CrewSoul> souls = assigned ? DockCrew.AssignedTo(ship, new[] { part }) : DockCrew.HomedOn(ship);
                    int moved = DockCrew.Move(souls, ship, other, op.WorldPhysicalCenter, out int left);
                    if (moved > 0) Log.Info($"Dock clamp at {part.Location}: gave {moved} crew ({(assigned ? "of quarters assigned to it" : "all of this ship's crew")}) to ship {other.UniqueID}" + (left > 0 ? $"; {left} found no free quarters there." : "."));
                    crew = left > 0 ? $"{left} crew wait for free quarters on ship {other.UniqueID}" : "";
                }
                else crew = WaitText("crew", crewMine, crewTheirs);
                string supply = "";
                int supplyMine = ToggleMode(part, ClampResupplyId), supplyTheirs = ToggleMode(op, ClampResupplyId);
                if (!DockTransfer.Flows(supplyMine, supplyTheirs)) supply = WaitText("supplies", supplyMine, supplyTheirs);
                else supply = BayManager.AnyStationReaches(ship, other)
                    ? "supplies: given by this ship's bay stations in reach"
                    : $"supplies: no bay station on this ship reaches ship {other.UniqueID} -- nothing moves";
                clamp.ServiceStatus = crew.Length > 0 && supply.Length > 0 ? crew + "; " + supply : crew + supply;
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("DockShipManager.UpdateClampService", ex);
            }
        }

        private static string WaitText(string what, int mine, int theirs)
        {
            if (mine == DockTransfer.GIVE && !DockTransfer.Flows(mine, theirs)) return $"{what}: Give waits for the other clamp to Receive";
            if (mine == DockTransfer.RECEIVE && !DockTransfer.Flows(theirs, mine)) return $"{what}: Receive waits for the other clamp to Give";
            return "";
        }

        private static int ToggleMode(Part part, ID<PartToggleGuiRules> id)
        {
            PartUIToggle t = part == null ? null : PartUIToggle.GetOnPart(part, id);
            return t != null ? t.Mode : DockTransfer.NONE;
        }

        private static void UpdatePadJoin(ZtxDockPadComponent pad)
        {
            if (pad.DockedShip == null || !HingeJointManager.HasPinWithTag(pad)) return;
            bool joined = DockJoin.Joined(true, DockJoin.IsJoinOn(pad.Part), false);
            HingeJointManager.SetDockSeparate(pad, !joined, pad.DockedShip);
        }

        private static void UpdateClampThrust(Ship ship, ZtxDockClampComponent clamp)
        {
            Ship other = clamp.PairedClamp?.Part?.Ship;
            if (ReferenceEquals(other, ship)) { DockThrust.UpdateMerged(clamp, clamp.Part); return; }
            bool welded = other != null && !ReferenceEquals(other, ship)
                && (HingeJointManager.HasPinWithTag(clamp) || HingeJointManager.HasPinWithTag(clamp.PairedClamp));
            if (!welded || !ReferenceEquals(DockThrust.Carrier(ship, other), ship)) { DockThrust.Clear(clamp); return; }
            DockThrust.UpdateWelded(clamp, clamp.Part, ship, other);
        }

        private static void UpdatePadThrust(Ship ship, ZtxDockPadComponent pad)
        {
            Ship visitor = pad.DockedShip;
            if (visitor == null || !HingeJointManager.HasPinWithTag(pad)) { DockThrust.Clear(pad); return; }
            DockThrust.UpdateWelded(pad, pad.Part, ship, visitor);
        }

        private void ServiceClamp(Ship ship, ZtxDockClampComponent clamp)
        {
            Part part = clamp.Part;
            if (part == null || part.Ship != ship) return;

            if (clamp.PairedClamp != null)
            {
                Part partnerPart = clamp.PairedClamp.Part;
                bool partnerAlive = partnerPart != null && partnerPart.Ship != null;
                if (partnerAlive && ReferenceEquals(partnerPart.Ship, ship))
                {
                    if (!IsLeader(part, partnerPart)) return;
                    if (!IsDockOn(part) || !IsDockOn(partnerPart) || !DockJoin.Merges(DockJoin.ModeOf(part), DockJoin.ModeOf(partnerPart)))
                        TrySeamSplit(ship, clamp);
                    else
                        clamp.LastRejectReason = null;
                    return;
                }
                if (clamp.OwnsJoint)
                {
                    if (!HingeJointManager.HasPinWithTag(clamp))
                    {
                        clamp.ClearPair();
                        return;
                    }
                    if (!IsDockOn(part) || !partnerAlive || !IsDockOn(partnerPart))
                    {
                        HingeJointManager.ReleaseByTag(clamp);
                        clamp.ClearPair();
                        Log.Info($"Dock clamp at {part.Location}: docking switched OFF -- weld released.");
                        return;
                    }
                    HingeJointManager.ReassertFilter(clamp);
                    FollowMerge(clamp, part, partnerPart);
                }
                else if (!ReferenceEquals(clamp.PairedClamp.PairedClamp, clamp))
                {
                    clamp.PairedClamp = null;
                    clamp.OwnsJoint = false;
                }
                else if (!IsDockOn(part))
                {
                    HingeJointManager.ReleaseByTag(clamp.PairedClamp);
                    Log.Info($"Dock clamp at {part.Location}: docking switched OFF -- weld released.");
                    clamp.ClearPair();
                }
                return;
            }

            IntVector2 nc = part.Location + DockGeometry.FacingDir(part);
            Part front = ship.Parts[nc, PartRectType.Physical];
            if (front != null && TryAdopt(clamp, part, front)) return;
            if (!IsDockOn(part)) { clamp.LastRejectReason = null; clamp.Merge.Blocked = false; return; }
            if (front != null)
            {
                LogRejectOnce(clamp, "facing edge blocked by own hull -- rotate the clamp so the arrow points off the ship");
                return;
            }
            if (clamp.Merge.Blocked)
            {
                LogRejectOnce(clamp, "an earlier merge failed on this clamp -- switch its docking off and on to allow another attempt (save and reload first if the ships look wrong)");
                return;
            }

            Vector2 eAWorld = DockGeometry.EdgeMidWorld(ship, part);
            ZtxDockClampComponent otherClamp = null;
            Ship other = null;
            float bestDist = float.MaxValue;
            float refAngle = 0f;
            string reject = null;
            foreach (Ship cand in ship.Sim.Ships)
            {
                if (cand == ship || cand.Physics?.Body == null || !DockGeometry.MightReach(cand, eAWorld)) continue;
                if (!DockThrustMath.SameOwner(ship.Metadata.PlayerIndex, cand.Metadata.PlayerIndex)) continue;
                DockShipManager mgr = TryGet(cand);
                if (mgr == null) continue;
                foreach (ZtxDockClampComponent c in mgr._clamps)
                {
                    Part cp = c.Part;
                    if (cp == null || cp.Ship != cand) continue;
                    float dist = (DockGeometry.EdgeMidWorld(cand, cp) - eAWorld).Length / ship.Scale.X;
                    if (dist > DockGeometry.MAX_FACE_DISTANCE_CELLS || dist >= bestDist) continue;
                    if (c.PairedClamp != null)
                    {
                        reject = $"clamp on ship {cand.UniqueID} is within reach but already docked elsewhere";
                        continue;
                    }
                    if (!IsDockOn(cp))
                    {
                        reject = $"clamp on ship {cand.UniqueID} is within reach but has its docking switch OFF";
                        continue;
                    }
                    float ra = DockGeometry.OpposingRefAngle(ship, part, cand, cp, out float angErr);
                    if (angErr > DockGeometry.MAX_FACE_ANGLE)
                    {
                        reject = $"clamp on ship {cand.UniqueID} is within reach but misaligned by {angErr * 57.3f:F0} deg (arrows must oppose)";
                        continue;
                    }
                    bestDist = dist;
                    otherClamp = c;
                    other = cand;
                    refAngle = ra;
                }
            }
            if (otherClamp == null)
            {
                if (reject != null) LogRejectOnce(clamp, reject);
                else clamp.LastRejectReason = null;
                return;
            }
            if (!DockGeometry.IsRelativeMotionLow(ship, other, eAWorld))
            {
                LogRejectOnce(clamp, $"clamp on ship {other.UniqueID} within reach but relative motion too high -- slow down");
                return;
            }
            clamp.LastRejectReason = null;

            Part otherPart = otherClamp.Part;
            Vector2 anchorLocalA = DockGeometry.EdgeMidGrid(part) * ship.Scale;
            Vector2 anchorLocalB = DockGeometry.EdgeMidGrid(otherPart) * other.Scale;
            HingeJointManager.CreateAlignedWeld(clamp, ship, other,
                anchorLocalA, anchorLocalB, refAngle,
                collideConnected: true, part, otherPart);
            clamp.PairedClamp = otherClamp;
            clamp.OwnsJoint = true;
            otherClamp.PairedClamp = clamp;
            otherClamp.OwnsJoint = false;
            clamp.Merge.RefAngle = refAngle;
            clamp.Merge.AnchorGridA = DockGeometry.EdgeMidGrid(part);
            clamp.Merge.AnchorGridB = DockGeometry.EdgeMidGrid(otherPart);
            clamp.Merge.Ticks = 0;
            clamp.Merge.Pending = DockJoin.Merges(DockJoin.ModeOf(part), DockJoin.ModeOf(otherPart));
            Log.Info($"Dock clamp at {part.Location} engaged clamp on ship {other.UniqueID} at {bestDist:F2} tiles -- pulling into alignment"
                     + (clamp.Merge.Pending ? "; both say Merge, merging once the snap settles." : "."));
        }

        private bool TryAdopt(ZtxDockClampComponent clamp, Part part, Part front)
        {
            ZtxDockClampComponent fc = ZtxDockClampComponent.ForPart(front);
            if (fc == null || fc.PairedClamp != null) return false;
            bool facesBack = front.Location + DockGeometry.FacingDir(front) == part.Location;
            if (!DockJoin.AdoptsMergedPair(ReferenceEquals(front.Ship, part.Ship), facesBack, DockJoin.ModeOf(part), DockJoin.ModeOf(front))) return false;
            clamp.PairedClamp = fc;
            fc.PairedClamp = clamp;
            clamp.OwnsJoint = false;
            fc.OwnsJoint = false;
            clamp.LastRejectReason = null;
            Log.Info($"Dock clamp at {part.Location}: adopted merged pair with clamp at {front.Location} (post-merge or post-load relink). Switch either off, or set either below Merge, to split the ships.");
            return true;
        }

        private static void FollowMerge(ZtxDockClampComponent clamp, Part part, Part partnerPart)
        {
            if (partnerPart?.Ship == null) return;
            if (DockJoin.Merges(DockJoin.ModeOf(part), DockJoin.ModeOf(partnerPart)))
            {
                if (clamp.Merge.Pending || clamp.Merge.StayWelded) return;
                clamp.Merge.Pending = true;
                clamp.Merge.Ticks = 0;
                Log.Info($"Dock clamp at {part.Location}: both clamps say Merge -- merging with ship {partnerPart.Ship.UniqueID} once the weld settles.");
                return;
            }
            if (clamp.Merge.Pending) Log.Info($"Dock clamp at {part.Location}: Merge is off -- staying docked to ship {partnerPart.Ship.UniqueID} without merging.");
            clamp.Merge.Pending = false;
        }

        private void ServicePendingMerge(ZtxDockClampComponent clamp)
        {
            if (!HingeJointManager.HasPinWithTag(clamp))
            {
                clamp.ClearPair();
                return;
            }
            Part part = clamp.Part;
            Part otherPart = clamp.PairedClamp?.Part;
            Ship shipA = part?.Ship;
            Ship shipB = otherPart?.Ship;
            if (shipA?.Physics?.Body == null || shipB?.Physics?.Body == null || shipA == shipB)
            {
                clamp.Merge.Pending = false;
                return;
            }

            clamp.Merge.Ticks++;
            DockMerge.Measure(clamp.Merge, shipA, shipB, out float angErr, out float gapCells);
            if (!DockMerge.Settled(clamp.Merge.Ticks, angErr, gapCells))
            {
                if (clamp.Merge.Ticks >= DockMerge.MIN_TICKS && clamp.Merge.Ticks % 90 == 0)
                    Log.Info($"Dock clamp at {part.Location}: weld still settling (angle err {angErr:F3} rad, gap {gapCells:F2} cells).");
                return;
            }

            ZtxDockClampComponent other = clamp.PairedClamp;
            MergeOutcome outcome = DockMerge.Merge(clamp, "Dock clamp", part, otherPart, p => ZtxDockClampComponent.ForPart(p)?.Merge);
            clamp.Merge.Pending = false;
            if (outcome == MergeOutcome.Merged)
            {
                clamp.OwnsJoint = false;
                if (clamp.PairedClamp != null)
                {
                    clamp.PairedClamp.OwnsJoint = false;
                    clamp.PairedClamp.Merge.Pending = false;
                }
            }
            else if (outcome == MergeOutcome.StayWelded)
            {
                clamp.Merge.StayWelded = true;
                Log.Info($"Dock clamp at {part.Location}: merge blocked (see line above) -- staying docked without merging.");
            }
            else
            {
                clamp.ClearPair();
                clamp.Merge.Blocked = true;
                if (other != null) other.Merge.Blocked = true;
                Log.Error($"Dock clamp at {part.Location}: merge FAILED after the weld was released (see line above) -- pair cleared, both clamps blocked until their docking is switched off and on.");
            }
        }

        private void TrySeamSplit(Ship ship, ZtxDockClampComponent clamp)
        {
            Part partA = clamp.Part;
            Part partB = clamp.PairedClamp?.Part;
            if (partA == null || partB == null || partB.Ship != ship) return;

            var cutPartner = new Dictionary<Part, Part>();
            foreach (ZtxDockClampComponent c in _clamps)
            {
                Part p = c.Part;
                Part q = c.PairedClamp?.Part;
                if (p == null || q == null || p.Ship != ship || q.Ship != ship) continue;
                if (IsDockOn(p) && IsDockOn(q) && DockJoin.Merges(DockJoin.ModeOf(p), DockJoin.ModeOf(q))) continue;
                cutPartner[p] = q;
                cutPartner[q] = p;
            }
            cutPartner[partA] = partB;
            cutPartner[partB] = partA;

            Part seed = !IsDockOn(partA) ? partA : partB;
            ZtxDockClampComponent partner = clamp.PairedClamp;
            if (clamp.Merge.MergedAwayIdentity != null || partner?.Merge.MergedAwayIdentity != null)
            {
                if (clamp.Merge.FromMergedAwayShip) seed = partA;
                else if (partner != null && partner.Merge.FromMergedAwayShip) seed = partB;
            }
            Part keep = seed == partA ? partB : partA;

            ShipIdentity mergedAway = clamp.Merge.MergedAwayIdentity ?? partner?.Merge.MergedAwayIdentity;
            ZtxDockClampComponent far = seed == partA ? clamp : partner;
            bool farFromMergedAway = far != null && far.Merge.FromMergedAwayShip;
            Ship shipB = DockMerge.SplitSeam(ship, cutPartner, seed, keep, mergedAway, farFromMergedAway, "Dock clamp", out bool bridged);
            if (bridged)
            {
                LogRejectOnce(clamp, "cannot split -- the two halves are still connected around the clamps (structure built across the seam, or another merged clamp pair that still says Merge)");
                return;
            }
            clamp.LastRejectReason = null;
            if (shipB == null) return;

            foreach (ZtxDockClampComponent c in _clamps.ToArray())
            {
                Part p = c.Part;
                Part q = c.PairedClamp?.Part;
                if (p != null && q != null && p.Ship != q.Ship)
                    c.ClearPair();
            }
            clamp.ClearPair();
        }

        private static bool IsLeader(Part part, Part partnerPart)
        {
            IntVector2 a = part.Location;
            IntVector2 b = partnerPart.Location;
            if (a.Y != b.Y) return a.Y < b.Y;
            return a.X <= b.X;
        }

        private static void LogRejectOnce(ZtxDockClampComponent clamp, string reason)
        {
            if (clamp.LastRejectReason == reason) return;
            clamp.LastRejectReason = reason;
            Log.Info($"Dock clamp at {clamp.Part?.Location}: NOT docking -- {reason}.");
        }

        #endregion

        #region Pad servicing

        private void ServicePad(Ship ship, ZtxDockPadComponent pad)
        {
            Part part = pad.Part;
            if (part == null || part.Ship != ship) return;

            if (pad.DockedShip != null)
            {
                if (!HingeJointManager.HasPinWithTag(pad))
                {
                    pad.DockedShip = null;
                }
                else if (!IsDockOn(part))
                {
                    HingeJointManager.ReleaseByTag(pad);
                    pad.DockedShip = null;
                    Log.Info($"Dock pad at {part.Location}: docking switched OFF -- ship released.");
                }
                return;
            }

            if (!IsDockOn(part)) return;

            Vector2 padWorld = ship.Physics.Body.GetWorldPoint(DockGeometry.CellCenter(part.Location) * ship.Scale);
            Ship other = ship.Sim?.Ships.DetFindUnderPoint(padWorld, approximate: false, junk: null, condition: s => s != ship);
            if (other?.Physics?.Body == null) return;
            if (BayManager.IsHeldByAnyBay(other)) return;
            if (!DockThrustMath.SameOwner(ship.Metadata.PlayerIndex, other.Metadata.PlayerIndex))
            {
                if (pad.LastRefusedShip != other.UniqueID)
                {
                    pad.LastRefusedShip = other.UniqueID;
                    Log.Info($"Dock pad at {part.Location}: NOT docking ship {other.UniqueID} -- it belongs to another player (docking is for your own ships only).");
                }
                return;
            }
            if (!DockGeometry.IsRelativeMotionLow(ship, other, padWorld)) return;

            HingeJointManager.CreateTaggedWeld(pad, ship, other, padWorld, collideConnected: true);
            pad.DockedShip = other;
            Log.Info($"Dock pad at {part.Location} welded ship {other.UniqueID} in place.");
        }

        #endregion

    }
}
