using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Geometry;
using Halfling.Scene2D;
using Halfling.Timing;

namespace ZTX.HingesPistonsDocking
{
    internal class HingeShipManager : ShipComponent
    {
        private static readonly ConditionalWeakTable<Ship, HingeShipManager> s_managers =
            new ConditionalWeakTable<Ship, HingeShipManager>();

        private const int SERVICE_INTERVAL_TICKS = 30;

        private const float MAX_ALIGN_ANGLE = 0.35f;

        private static readonly ID<PartToggleGuiRules> s_angleLeftToggleId =
            new ID<PartToggleGuiRules>("ztx_hinge_angle_left");
        private static readonly ID<PartToggleGuiRules> s_angleRightToggleId =
            new ID<PartToggleGuiRules>("ztx_hinge_angle_right");
        private static readonly ID<PartToggleGuiRules> s_motorStrengthToggleId =
            new ID<PartToggleGuiRules>("ztx_motor_strength");
        private static readonly ID<PartToggleGuiRules> s_pistonExtensionToggleId =
            new ID<PartToggleGuiRules>("ztx_piston_extension");
        private static readonly ID<PartToggleGuiRules> s_motorSpeedToggleId =
            new ID<PartToggleGuiRules>("ztx_motor_speed");
        private static readonly ID<PartToggleGuiRules> s_pistonSpeedToggleId =
            new ID<PartToggleGuiRules>("ztx_piston_speed");
        private static readonly ID<PartToggleGuiRules> s_driveAccelToggleId =
            new ID<PartToggleGuiRules>("ztx_drive_accel");
        private const int MOTOR_SPEED_DEFAULT_MODE = 4;
        private const int PISTON_SPEED_DEFAULT_MODE = 3;
        private const float MOTOR_RETURN_RATE = 1.2f;

        private const float DEG_TO_RAD = (float)Math.PI / 180f;

        private const float MERGE_MAX_ANGLE = 0.035f;
        private const float MERGE_MAX_SPIN = 0.05f;

        private const float MERGE_MAX_TRANSLATION = 0.03f;
        private const float MERGE_MAX_SLIDE = 0.05f;

        private readonly List<ZtxHingeBaseComponent> _bases = new List<ZtxHingeBaseComponent>();
        internal IReadOnlyList<ZtxHingeBaseComponent> Bases => _bases;
        private readonly List<Part> _orphanRods = new List<Part>();
        private bool _jobCountersChecked;
        private int _tickCounter;

        private const float ROD_SPAWN_LEAD = 0.02f;
        private const float ROD_REMOVE_LAG = 0.3f;
        private const float ROD_REMOVE_AHEAD = 0.05f;
        private const int ROD_WALK_LIMIT = 64;

        public HingeShipManager()
        {
            base.FixedUpdateBucket = 10;
            base.FixedUpdatingEnabled = true;
        }

        public static HingeShipManager GetOrCreate(Ship ship)
        {
            if (ship == null) return null;
            if (s_managers.TryGetValue(ship, out HingeShipManager manager)) return manager;

            manager = new HingeShipManager();
            HingeShipManager captured = manager;
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

        public static HingeShipManager TryGet(Ship ship)
        {
            if (ship == null) return null;
            s_managers.TryGetValue(ship, out HingeShipManager manager);
            return manager;
        }

        public void Register(ZtxHingeBaseComponent hingeBase)
        {
            if (!_bases.Contains(hingeBase))
                _bases.Add(hingeBase);
        }

        public void Deregister(ZtxHingeBaseComponent hingeBase)
        {
            _bases.Remove(hingeBase);
        }

        public void NoteOrphanRod(Part rod)
        {
            if (rod != null && !_orphanRods.Contains(rod))
                _orphanRods.Add(rod);
        }

        public override void FixedUpdate(FixedUpdater fixedUpdater, SceneRoot root)
        {
            try
            {
                HingeJointManager.SyncSolverIterations();
                HingeJointManager.EnsureChainSnap(root);
                AssemblyIndex.Tick(base.Ship);
                HingeJointManager.ResumeSuspended();
                HingeJointManager.UpdateSaveGuard(base.Ship?.Sim);
                AssemblyFacade.ReconcileCommandGates(base.Ship.Sim);
                if (_orphanRods.Count > 0 && _tickCounter % SERVICE_INTERVAL_TICKS == 0)
                    SweepOrphanRods();
                if (!_jobCountersChecked && _tickCounter % SERVICE_INTERVAL_TICKS == 0)
                {
                    _jobCountersChecked = true;
                    int moved = ShipMerger.IdCounters.AboveLiveJobs(base.Ship);
                    if (moved > 0) Log.Info($"Ship {base.Ship?.UniqueID}: job ID counter moved {moved} past live manual jobs (an earlier merge had rewound it).");
                }
                if (s_shotRodCells.Count > 0 && _tickCounter % SERVICE_INTERVAL_TICKS == 0)
                    ClearShotRods();
                if (_tickCounter % SERVICE_INTERVAL_TICKS == 0)
                    MergeRebuild.ServiceWrecks(base.Ship);
                if (_bases.Count == 0)
                {
                    _tickCounter++;
                    return;
                }

                foreach (ZtxHingeBaseComponent hingeBase in _bases.ToArray())
                {
                    JointPower.CarryOver(hingeBase);
                    if (hingeBase is ZtxPistonBaseComponent mergePiston)
                    {
                        MergeService.Service(this, mergePiston, _tickCounter);
                        if (MergeService.IsHeld(hingeBase))
                            GripService.Service(this, mergePiston, _tickCounter);
                    }
                    if (MergeService.IsHeld(hingeBase)) continue;
                    if (!hingeBase.Serviced) continue;
                    if (hingeBase is ZtxPistonBaseComponent lanePiston && lanePiston.Part != null)
                    {
                        Ship arm = lanePiston.PairedTip?.Part?.Ship;
                        Ship carrierShip = lanePiston.Part.Ship;
                        if (arm != null && carrierShip?.Physics?.Body != null && arm != carrierShip && HingeJointManager.HasPinWithTag(lanePiston))
                        {
                            Halfling.Geometry.IntVector2 lf = BaseFacing(lanePiston.Part);
                            LaneGuard.Register(arm, carrierShip.Physics.Body.GetWorldVector(new System.Numerics.Vector2(lf.X, lf.Y)), lanePiston.Rules.LaneTolerance);
                        }
                    }
                    if (hingeBase is ZtxPistonBaseComponent gripPiston && !hingeBase.Returning)
                        GripService.Service(this, gripPiston, _tickCounter);
                    if (hingeBase.Returning)
                    {
                        if (!HingeJointManager.HasPinWithTag(hingeBase))
                        {
                            hingeBase.Returning = false;
                            continue;
                        }
                        if (hingeBase is ZtxPistonBaseComponent returningPiston)
                        {
                            if (returningPiston.RodBroken) { HandleRodBroken(returningPiston); continue; }
                            float retractRate = PistonRate(returningPiston,
                                LogicSource.Mode(returningPiston.Part, s_pistonSpeedToggleId, PISTON_SPEED_DEFAULT_MODE));
                            HingeJointManager.UpdateSliderPositionHold(hingeBase, 0f,
                                PistonMaxTranslation(returningPiston), 1f,
                                retractRate, DriveAccel(returningPiston.Part, retractRate, returningPiston.Rules.AccelTime));
                            if (HingeJointManager.TryGetSliderState(hingeBase, out float slide, out float slideSpeed))
                            {
                                SyncRods(returningPiston, slide / ShipScale(), 0f);
                                if (returningPiston.Rods.Count == 0 &&
                                    Math.Abs(slide) <= MERGE_MAX_TRANSLATION && Math.Abs(slideSpeed) <= MERGE_MAX_SLIDE)
                                {
                                    TryMergeBack(hingeBase);
                                }
                            }
                            continue;
                        }
                        HingeJointManager.UpdateMotorPositionHold(hingeBase, 0f, 1f, MOTOR_RETURN_RATE, float.PositiveInfinity);
                        if (HingeJointManager.TryGetJointState(hingeBase, out float angle, out float spin) &&
                            Math.Abs(angle) <= MERGE_MAX_ANGLE && Math.Abs(spin) <= MERGE_MAX_SPIN)
                        {
                            TryMergeBack(hingeBase);
                        }
                    }
                    else if (hingeBase is ZtxPistonBaseComponent piston && HingeJointManager.HasPinWithTag(hingeBase))
                    {
                        if (piston.RodBroken) HandleRodBroken(piston);
                        else DrivePiston(piston);
                    }
                    else if (hingeBase is ZtxMotorBaseComponent && HingeJointManager.HasPinWithTag(hingeBase))
                    {
                        DriveMotor(hingeBase);
                    }
                    else if (HingeJointManager.HasPinWithTag(hingeBase))
                    {
                        ApplyAngleLimits(hingeBase);
                    }
                }

                _tickCounter++;
                if (_tickCounter % SERVICE_INTERVAL_TICKS != 0) return;

                HashSet<IntVector2> pendingRebuild = MergeRebuild.PendingCells(base.Ship);

                foreach (ZtxHingeBaseComponent hingeBase in _bases.ToArray())
                {
                    if (MergeService.IsHeld(hingeBase))
                    {
                        MergeRebuild.ServiceMerged(this, hingeBase);
                        continue;
                    }
                    if (pendingRebuild != null && hingeBase.Part != null && pendingRebuild.Contains(hingeBase.Part.Location))
                    {
                        LogRejectOnce(hingeBase, "waiting for the arm it stands on to be rebuilt");
                        continue;
                    }
                    bool active = IsHingeActive(hingeBase.Part);
                    MergeRebuild.Service(this, hingeBase, active);
                    if (!hingeBase.Serviced && hingeBase is ZtxPistonBaseComponent adoptee && TryAdoptMergedPose(adoptee))
                        continue;

                    if (hingeBase.Serviced)
                    {
                        if (!HingeJointManager.HasPinWithTag(hingeBase))
                        {
                            hingeBase.Returning = false;
                            hingeBase.ClearPair();
                        }
                        else if (!active)
                        {
                            if (!hingeBase.Returning)
                            {
                                if (IsMergeEligible(hingeBase))
                                {
                                    hingeBase.Returning = true;
                                    HingeJointManager.ResetMotorBand(hingeBase);
                                    Log.Info($"Hinge base at {hingeBase.Part?.Location}: disengaged -- returning arm to straight for merge.");
                                }
                                else
                                {
                                    HingeJointManager.ReleaseByTagDeliberately(hingeBase);
                                    hingeBase.ClearPair();
                                    Log.Info($"Hinge base at {hingeBase.Part?.Location}: disengaged -- joint released (pair not merge-compatible; grids never shared coordinates).");
                                }
                            }
                            continue;
                        }
                        else
                        {
                            if (hingeBase.Returning)
                            {
                                hingeBase.Returning = false;
                                HingeJointManager.ResetMotorBand(hingeBase);
                                if (!(hingeBase is ZtxMotorBaseComponent))
                                    HingeJointManager.DisableMotor(hingeBase);
                                Log.Info($"Hinge base at {hingeBase.Part?.Location}: re-engaged -- merge return cancelled.");
                            }
                            HingeJointManager.ReassertFilter(hingeBase);
                            if (!(hingeBase is ZtxMotorBaseComponent) && !(hingeBase is ZtxPistonBaseComponent))
                                ApplyAngleLimits(hingeBase);
                            continue;
                        }
                    }

                    if (!active)
                    {
                        if (hingeBase is ZtxPistonBaseComponent dormant && !RodLineAwaitsRebuild(dormant))
                            RemoveAllRods(dormant, includeUnadopted: true);
                        continue;
                    }
                    TryService(hingeBase);
                }
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("HingeShipManager.FixedUpdate", ex);
            }
        }

        private void TryService(ZtxHingeBaseComponent hingeBase)
        {
            Ship ship = base.Ship;
            Part basePart = hingeBase.Part;
            if (ship == null || basePart == null || basePart.Ship != ship || ship.Physics?.Body == null)
                return;

            ZtxHingeTipComponent tip = FindAdjacentTip(ship, basePart);
            if (tip != null)
            {
                TrySplitPair(hingeBase, basePart, tip);
                return;
            }

            TryFlushPair(hingeBase, basePart);
        }

        private ZtxHingeTipComponent FindAdjacentTip(Ship ship, Part basePart)
        {
            IntVector2 cell = basePart.Location + BaseFacing(basePart);
            Part p = ship.Parts[cell, PartRectType.Physical];
            if (p == null || p == basePart) return null;
            ZtxHingeTipComponent tip = ZtxHingeTipComponent.ForPart(p);
            if (tip == null || tip.PairedBase != null) return null;
            if (p.Location + TipFacing(p) != basePart.Location)
            {
                LogRejectOnceBase(ship, basePart, "adjacent tip's arrow faces away -- rotate the tip so both arrows touch");
                return null;
            }
            return tip;
        }

        internal static IntVector2 BaseFacing(Part p)
        {
            return OrthogonalRotation.Up.GetShipRelative(p.Rotation, p.FlipX).ToVector();
        }

        internal static IntVector2 BaseFacing(int rotation, bool flipX)
        {
            return OrthogonalRotation.Up.GetShipRelative(rotation, flipX).ToVector();
        }

        private static IntVector2 TipFacing(Part p)
        {
            return OrthogonalRotation.Down.GetShipRelative(p.Rotation, p.FlipX).ToVector();
        }

        internal static bool IsHingeActive(Part basePart)
        {
            if (basePart == null) return false;
            return JointPower.IsOn(basePart);
        }

        private static void GetAngleLimits(Part basePart, out float lower, out float upper)
        {
            int left = PartUIToggle.GetOnPart(basePart, s_angleLeftToggleId)?.Mode ?? 9;
            int right = PartUIToggle.GetOnPart(basePart, s_angleRightToggleId)?.Mode ?? 9;
            lower = -TypedSettings.LeftLimit(basePart, left) * DEG_TO_RAD;
            upper = TypedSettings.RightLimit(basePart, right) * DEG_TO_RAD;
        }

        private void ApplyAngleLimits(ZtxHingeBaseComponent hingeBase)
        {
            if (hingeBase.Part == null) return;
            GetAngleLimits(hingeBase.Part, out float lower, out float upper);
            HingeJointManager.UpdateAngleLimits(hingeBase, lower, upper);
        }

        private static float ComputeStraightAngle(Ship shipA, Part basePart, Ship shipB, Part tipPart)
        {
            IntVector2 f = BaseFacing(basePart);
            IntVector2 t = TipFacing(tipPart);
            float angF = (float)DetMath.Atan2(f.Y, f.X);
            float angT = (float)DetMath.Atan2(t.Y, t.X);
            float target = angF - angT + (float)Math.PI;
            float current = shipB.Physics.Body.Rotation - shipA.Physics.Body.Rotation;
            return target + (float)(Math.Round((current - target) / (2.0 * Math.PI)) * 2.0 * Math.PI);
        }

        private static void LogRejectOnce(ZtxHingeBaseComponent hingeBase, string reason)
        {
            if (hingeBase == null) return;
            if (hingeBase.LastRejectReason == reason) return;
            hingeBase.LastRejectReason = reason;
            if (reason != null)
                Log.Info($"Hinge base at {hingeBase.Part?.Location}: engaged but NOT forming -- {reason}.");
        }

        private void LogRejectOnceBase(Ship ship, Part basePart, string reason)
        {
            foreach (ZtxHingeBaseComponent b in _bases)
                if (b.Part == basePart) { LogRejectOnce(b, reason); return; }
        }

        private void DriveMotor(ZtxHingeBaseComponent motorBase)
        {
            Part part = motorBase.Part;
            if (part == null) return;
            float degrees = ArmToggles.MotorDegrees(part);
            int strMode = LogicSource.StrengthMode(part, PartUIToggle.GetOnPart(part, s_motorStrengthToggleId)?.Mode ?? 10);
            float target = (Math.Max(0f, Math.Min(180f, degrees)) - 90f) * DEG_TO_RAD;
            int ruleSpeed = LogicSource.Speed(part);
            float rate = ruleSpeed > 0 ? ruleSpeed * DEG_TO_RAD : MotorRate(motorBase, LogicSource.Mode(part, s_motorSpeedToggleId, MOTOR_SPEED_DEFAULT_MODE));
            float accel = DriveAccel(part, rate, (motorBase.Rules as ZtxMotorBaseRules)?.AccelTime ?? 0.4f);
            HingeJointManager.UpdateMotorPositionHold(motorBase, target, strMode / 10f, rate, accel);
        }

        private void DrivePiston(ZtxPistonBaseComponent piston)
        {
            Part part = piston.Part;
            if (part == null) return;
            int strMode = LogicSource.StrengthMode(part, LogicSource.Mode(part, s_motorStrengthToggleId, 10));
            float cells = CommandedCells(piston);
            int ruleSpeed = LogicSource.Speed(part);
            float rate = ruleSpeed > 0 ? Math.Max(0.05f, ruleSpeed / 10f) * ShipScale() : PistonRate(piston, LogicSource.Mode(part, s_pistonSpeedToggleId, PISTON_SPEED_DEFAULT_MODE));
            float accel = DriveAccel(part, rate, piston.Rules.AccelTime);
            HingeJointManager.UpdateSliderPositionHold(piston, cells * ShipScale(),
                PistonMaxTranslation(piston), strMode / 10f, rate, accel);
            if (HingeJointManager.TryGetSliderState(piston, out float slide, out _))
            {
                float tCells = slide / ShipScale();
                SyncRods(piston, tCells, cells);
                HingeJointManager.ReassertFilter(piston);
            }
        }

        private void SyncRods(ZtxPistonBaseComponent piston, float tCells, float targetCells)
        {
            Ship ship = base.Ship;
            Part bp = piston.Part;
            if (ship == null || bp == null || bp.Ship != ship) return;
            if (string.IsNullOrEmpty(piston.Rules.RodPart)) return;

            while (piston.Rods.Count > 0)
            {
                int count = piston.Rods.Count;
                bool retracting = targetCells < count - 0.5f;
                float removeAt = retracting ? count + ROD_REMOVE_AHEAD : count - ROD_REMOVE_LAG;
                if (tCells >= removeAt) break;
                RemoveRod(piston, piston.Rods[count - 1]);
            }

            int max = (int)Math.Floor(Math.Max(0f, piston.Rules.MaxExtension));
            while (piston.Rods.Count < max)
            {
                int k = piston.Rods.Count + 1;
                if (tCells < k - ROD_SPAWN_LEAD || targetCells < k - 0.5f) break;
                if (!SpawnRod(piston, ship, bp, k)) break;
            }
        }

        private bool SpawnRod(ZtxPistonBaseComponent piston, Ship ship, Part bp, int k)
        {
            IntVector2 cell = bp.Location + BaseFacing(bp) * k;
            if (ship.Parts[cell, PartRectType.Physical] != null) return false;
            if (!ship.Rules.PartsByID.TryGetValue(new ID<PartRules>(piston.Rules.RodPart), out PartRules rules) || rules == null)
            {
                LogRejectOnce(piston, $"rod part '{piston.Rules.RodPart}' is not a registered part -- no physical rod");
                return false;
            }
            try
            {
                Part rod = ship.Parts.CreatePart(rules, cell, bp.Rotation, bp.FlipX);
                MirrorRodBlueprint(ship, rod);
                ZtxPistonRodComponent rc = ZtxPistonRodComponent.ForPart(rod);
                if (rc != null) rc.Owner = piston;
                piston.Rods.Add(rod);
                HingeJointManager.AddFilteredPart(piston, rod);
                return true;
            }
            catch (Exception ex)
            {
                Log.Exception("HingeShipManager.SpawnRod", ex);
                return false;
            }
        }

        private static void RemoveRod(ZtxPistonBaseComponent piston, Part rod)
        {
            if (piston != null)
            {
                HingeJointManager.RemoveFilteredPart(piston, rod);
                piston.Rods.Remove(rod);
            }
            Ship s = rod?.Ship;
            if (s == null) return;
            ZtxPistonRodComponent.Removing = true;
            try
            {
                BlueprintPart mirror = s.BlueprintParts.GetMatchingPart(rod);
                if (mirror != null) s.BlueprintParts.RemoveBlueprintPart(mirror, removeDoors: true);
                EvacuateRod(s, rod);
                s.Parts.RemovePart(rod, PartsManager.CrewAction.Reset, destroyed: false, removeDoors: true, null);
            }
            catch (Exception ex)
            {
                Log.Exception("HingeShipManager.RemoveRod", ex);
            }
            finally
            {
                ZtxPistonRodComponent.Removing = false;
            }
        }

        private static void EvacuateRod(Ship s, Part rod)
        {
            IReadOnlyList<Cosmoteer.Ships.Crew.ShipCrew> here = rod.CrewPresent;
            if (here == null || here.Count == 0) return;
            IntVector2 back = BaseFacing(rod) * -1;
            int k = CorridorHinge.EvacuationStep(ROD_WALK_LIMIT, i =>
            {
                Part p = s.Parts[rod.Location + back * i, PartRectType.Normal];
                return p != null && !ReferenceEquals(p, rod) && p.Rules.CrewSpeedFactor.IsNonZero;
            });
            if (k == 0) return;
            IntVector2 d = back * k;
            int moved = 0;
            foreach (Cosmoteer.Ships.Crew.ShipCrew c in new List<Cosmoteer.Ships.Crew.ShipCrew>(here))
            {
                try
                {
                    c.SetLocation(c.Location + new Vector2(d.X, d.Y), lerpFromPrevious: false, queueGeometryRefresh: true, clearPath: true);
                    moved++;
                }
                catch (Exception ex) { Log.Exception("HingeShipManager.EvacuateRod", ex); }
            }
            if (moved > 0) Log.Info($"Corridor rod at {rod.Location} on ship {s.UniqueID} removed: {moved} crew on it stepped {k} cell(s) inward.");
        }

        private void RemoveAllRods(ZtxPistonBaseComponent piston, bool includeUnadopted)
        {
            foreach (Part rod in piston.Rods.ToArray())
                RemoveRod(piston, rod);
            piston.Rods.Clear();
            if (!includeUnadopted) return;
            Part bp = piston.Part;
            Ship ship = bp?.Ship;
            if (ship == null) return;
            IntVector2 f = BaseFacing(bp);
            IntVector2 cell = bp.Location + f;
            for (int i = 0; i < ROD_WALK_LIMIT; i++, cell += f)
            {
                Part p = ship.Parts[cell, PartRectType.Physical];
                if (p == null || ZtxPistonRodComponent.ForPart(p) == null) break;
                RemoveRod(null, p);
            }
        }

        private static IntVector2 AdoptRods(ZtxPistonBaseComponent piston, Ship ship, Part bp, out int rodCount)
        {
            rodCount = 0;
            IntVector2 f = BaseFacing(bp);
            IntVector2 cell = bp.Location + f;
            for (int i = 0; i < ROD_WALK_LIMIT; i++)
            {
                Part p = ship.Parts[cell, PartRectType.Physical];
                ZtxPistonRodComponent rc = ZtxPistonRodComponent.ForPart(p);
                if (rc == null) break;
                rc.Owner = piston;
                if (!piston.Rods.Contains(p)) piston.Rods.Add(p);
                MirrorRodBlueprint(ship, p);
                rodCount++;
                cell += f;
            }
            return cell;
        }

        private static void MirrorRodBlueprint(Ship ship, Part rod)
        {
            try
            {
                if (ship?.BlueprintParts == null || rod == null) return;
                if (ship.BlueprintParts.GetMatchingPart(rod) != null) return;
                if (ship.BlueprintParts[rod.Location, PartRectType.Normal] != null) return;
                ship.BlueprintParts.CreateBlueprintPart(rod.Rules, rod.Location, rod.Rotation, rod.FlipX);
            }
            catch (Exception ex)
            {
                Log.Exception("HingeShipManager.MirrorRodBlueprint", ex);
            }
        }

        private bool RodLineAwaitsRebuild(ZtxPistonBaseComponent piston)
        {
            Part bp = piston?.Part;
            Ship ship = bp?.Ship;
            if (ship == null) return false;
            IntVector2 f = BaseFacing(bp);
            IntVector2 cell = bp.Location + f;
            for (int i = 0; i < ROD_WALK_LIMIT; i++, cell += f)
            {
                if (ZtxPistonRodComponent.ForPart(ship.Parts[cell, PartRectType.Physical]) == null) return false;
                if (MergeRebuild.RebuildPendingAt(ship, cell)) return true;
            }
            return false;
        }

        internal void ClearRodStubForRebuild(ZtxPistonBaseComponent piston)
        {
            Part bp = piston?.Part;
            Ship ship = bp?.Ship;
            if (ship == null || bp.Ship != base.Ship) return;
            IntVector2 f = BaseFacing(bp);
            int count = 0;
            for (IntVector2 cell = bp.Location + f; count < ROD_WALK_LIMIT && ZtxPistonRodComponent.ForPart(ship.Parts[cell, PartRectType.Physical]) != null; cell += f)
                count++;
            piston.RodBroken = false;
            int leftovers = ClearRodGhosts(piston);
            if (count == 0 && piston.Rods.Count == 0)
            {
                if (leftovers > 0)
                    Log.Info($"Piston base at {bp.Location}: {leftovers} rod blueprint(s) / wreck(s) cleared -- its arm is lost and goes back as blueprints.");
                return;
            }
            HingeJointManager.ReleaseByTag(piston);
            piston.Returning = false;
            piston.ClearPair();
            RemoveAllRods(piston, includeUnadopted: true);
            Log.Info($"Piston base at {bp.Location}: rod stub cleared ({count} segment(s), {leftovers} blueprint(s) / wreck(s)) -- its arm is lost and goes back as blueprints.");
        }

        private void HandleRodBroken(ZtxPistonBaseComponent piston)
        {
            piston.RodBroken = false;
            Part bp = piston.Part;
            HingeJointManager.ReleaseByTag(piston);
            piston.Returning = false;
            piston.ClearPair();
            RemoveAllRods(piston, includeUnadopted: false);
            ClearRodGhosts(piston);
            Log.Info($"Piston base at {bp?.Location}: a rod segment was destroyed -- the extended section is released and the rod stub removed.");
        }

        private int ClearRodGhosts(ZtxPistonBaseComponent piston)
        {
            int cleared = 0;
            Part bp = piston.Part;
            Ship ship = bp?.Ship;
            if (ship == null) return 0;
            IntVector2 f = BaseFacing(bp);
            IntVector2 cell = bp.Location + f;
            int max = Math.Max(1, (int)Math.Ceiling(piston.Rules.MaxExtension));
            for (int i = 0; i < max; i++, cell += f)
                cleared += ClearRodLeftoverAt(ship, cell);
            return cleared;
        }

        internal static void RemoveDeadPistonRod(Part rod) => RemoveRod(null, rod);

        internal static int ClearRodLeftover(Ship ship, IntVector2 cell) => ClearRodLeftoverAt(ship, cell);

        private static int ClearRodLeftoverAt(Ship ship, IntVector2 cell)
        {
            int cleared = 0;
            try
            {
                if (ship?.Parts == null || ship.Parts[cell, PartRectType.Physical] != null) return 0;
                var ghost = ship.DestroyedParts?[cell, PartRectType.Physical];
                if (ghost != null && MergeRebuild.IsRodRules(ghost.Rules))
                {
                    ship.DestroyedParts.RemoveDestroyedPart(ghost);
                    cleared++;
                }
                BlueprintPart mirror = ship.BlueprintParts?[cell, PartRectType.Physical];
                if (mirror != null && MergeRebuild.IsRodRules(mirror.Rules))
                {
                    ship.BlueprintParts.RemoveBlueprintPart(mirror, removeDoors: true);
                    cleared++;
                }
            }
            catch (Exception ex)
            {
                Log.Exception("HingeShipManager.ClearRodLeftoverAt", ex);
            }
            return cleared;
        }

        private static readonly List<(Ship Ship, IntVector2 Cell)> s_shotRodCells = new List<(Ship, IntVector2)>();
        private static readonly object s_shotRodLock = new object();

        internal static void QueueShotRod(Ship ship, IntVector2 cell)
        {
            if (ship == null) return;
            lock (s_shotRodLock) s_shotRodCells.Add((ship, cell));
        }

        private void ClearShotRods()
        {
            Ship ship = base.Ship;
            if (ship == null) return;
            List<IntVector2> mine = null;
            lock (s_shotRodLock)
            {
                for (int i = s_shotRodCells.Count - 1; i >= 0; i--)
                {
                    Ship s = s_shotRodCells[i].Ship;
                    if (s != ship && s?.Parts != null && !s.IsDisposed) continue;
                    if (s == ship) (mine ??= new List<IntVector2>()).Add(s_shotRodCells[i].Cell);
                    s_shotRodCells.RemoveAt(i);
                }
            }
            if (mine == null) return;
            int cleared = 0;
            foreach (IntVector2 c in mine) cleared += ClearRodLeftoverAt(ship, c);
            if (cleared > 0)
                Log.Info($"Ship {ship.UniqueID}: {cleared} rod blueprint(s) / wreck(s) of shot rods cleared -- a piston makes its rods itself.");
        }

        private void SweepOrphanRods()
        {
            foreach (Part rod in _orphanRods.ToArray())
            {
                if (rod.Ship != null && ZtxPistonRodComponent.ForPart(rod)?.Owner == null)
                    RemoveRod(null, rod);
            }
            _orphanRods.Clear();
        }

        private float ShipScale()
        {
            float s = base.Ship?.Scale.X ?? 1f;
            return s > 0f ? s : 1f;
        }

        internal float ShipScaleValue => ShipScale();

        private static void AfterMergeBack(ZtxHingeBaseComponent hingeBase)
        {
            try
            {
                Part bp = hingeBase?.Part;
                if (!MergeService.REBUILD_ENABLED || bp?.Ship == null) return;
                MergeRecord rec = MergeStore.Read(bp);
                if (rec == null || rec.State == MergeState.None) return;
                var (placed, occupied, unknown, settings) = MergeRebuild.PlaceBlueprints(bp, rec, MergeRebuild.MissingAtHome(bp));
                rec.State = placed > 0 ? MergeState.Lost : MergeState.None;
                MergeStore.Write(bp, rec);
                if (placed > 0 || occupied > 0 || unknown > 0)
                    Log.Info($"Hinge base at {bp.Location}: merged back with {placed} part(s) missing -- placed as blueprints for the crew to rebuild" +
                        $" ({occupied} cell(s) occupied, {unknown} unknown part type(s), {settings} setting(s) restored).");
            }
            catch (Exception ex)
            {
                Log.Exception("HingeShipManager.AfterMergeBack", ex);
            }
        }

        internal ZtxHingeBaseComponent[] BasesSnapshot() => _bases.ToArray();

        private bool TryAdoptMergedPose(ZtxPistonBaseComponent piston)
        {
            if (!MergeService.ENABLED) return false;
            Ship ship = base.Ship;
            Part bp = piston?.Part;
            if (ship == null || bp == null || bp.Ship != ship) return false;
            if (MergeStore.ReadState(bp) == MergeState.Merged) return false;

            IntVector2 f = BaseFacing(bp);
            IntVector2 cell = bp.Location + f;
            var rods = new List<Part>();
            while (rods.Count < ROD_WALK_LIMIT)
            {
                Part r = ship.Parts[cell, PartRectType.Physical];
                if (ZtxPistonRodComponent.ForPart(r) == null) break;
                rods.Add(r);
                cell += f;
            }
            int k = rods.Count;
            if (k == 0 || k > piston.Rules.MaxExtension + 0.5f) return false;
            Part tipPart = ship.Parts[cell, PartRectType.Physical];
            ZtxHingeTipComponent tip = ZtxHingeTipComponent.ForPart(tipPart);
            if (tip == null || tip.PairedBase != null) return false;
            if (tipPart.Location + TipFacing(tipPart) != cell - f) return false;

            var excluded = new HashSet<Part>(rods) { bp };
            var section = new HashSet<Part> { tipPart };
            var queue = new Queue<Part>();
            queue.Enqueue(tipPart);
            while (queue.Count > 0)
            {
                Part cur = queue.Dequeue();
                foreach (Part n in StructuralAdjacency.Neighbours(ship, cur))
                {
                    if (excluded.Contains(n) || !section.Add(n)) continue;
                    queue.Enqueue(n);
                }
            }
            foreach (Part n in StructuralAdjacency.Neighbours(ship, bp))
            {
                if (section.Contains(n))
                {
                    LogRejectOnce(piston, "merged pose found (rods and head) but the head's side also touches the hull around the base -- not adopted; switch Merge or the base off and on after fixing the build");
                    return false;
                }
            }

            foreach (Part r in rods)
            {
                ZtxPistonRodComponent rc = ZtxPistonRodComponent.ForPart(r);
                if (rc != null) rc.Owner = piston;
                if (!piston.Rods.Contains(r)) piston.Rods.Add(r);
                MirrorRodBlueprint(ship, r);
            }
            MergeStore.Write(bp, MergeRebuild.RecordOf(bp, section, GridTransform.Identity, k, MergeState.Merged));
            MergeService.InvalidateCellMap(ship);
            LogRejectOnce(piston, null);
            Log.Info($"Piston base at {bp.Location}: merged pose adopted at {k} cells ({section.Count} parts, {k} rod segment(s)) -- its record was missing (rebuilt from blueprints).");
            return true;
        }

        internal Ship SectionInHomeGrid(ZtxHingeBaseComponent hingeBase)
        {
            Part tipPart = hingeBase?.PairedTip?.Part;
            Part basePart = hingeBase?.Part;
            if (tipPart?.Ship == null || basePart == null || tipPart.Ship == basePart.Ship) return null;
            return tipPart.Location + TipFacing(tipPart) == basePart.Location ? tipPart.Ship : null;
        }

        internal bool TipReachable(ZtxHingeBaseComponent hingeBase)
        {
            Ship ship = base.Ship;
            Part basePart = hingeBase?.Part;
            if (ship?.Physics?.Body == null || basePart == null) return false;
            IntVector2 f = BaseFacing(basePart);
            IntVector2 nc = basePart.Location + f;
            if (hingeBase is ZtxPistonBaseComponent)
            {
                for (int i = 0; i < ROD_WALK_LIMIT && ZtxPistonRodComponent.ForPart(ship.Parts[nc, PartRectType.Physical]) != null; i++)
                    nc += f;
            }
            Part own = ship.Parts[nc, PartRectType.Physical];
            if (own != null) return ZtxHingeTipComponent.ForPart(own) != null;
            Vector2 probeWorld = ship.Physics.Body.GetWorldPoint(CellCenter(nc) * ship.Scale);
            Ship other = FindShipAtWorldPoint(ship, probeWorld);
            if (other?.Physics?.Body == null) return false;
            Vector2 g = other.Physics.Body.GetLocalPoint(probeWorld) / other.Scale;
            Part otherPart = other.Parts[new IntVector2((int)Math.Floor(g.X), (int)Math.Floor(g.Y)), PartRectType.Physical];
            return ZtxHingeTipComponent.ForPart(otherPart) != null;
        }

        internal bool RelinkPiston(ZtxPistonBaseComponent piston, Ship section)
        {
            if (piston?.Part == null || piston.Part.Ship != base.Ship) return false;
            TryFlushPair(piston, piston.Part, section);
            return HingeJointManager.HasPinWithTag(piston);
        }

        internal static float CommandedCells(ZtxPistonBaseComponent piston)
        {
            float commanded = LogicSource.Mode(piston.Part, s_pistonExtensionToggleId, 0);
            return Math.Max(0f, Math.Min(commanded, piston.Rules.MaxExtension));
        }

        private float PistonMaxTranslation(ZtxPistonBaseComponent piston)
        {
            return Math.Max(0f, piston.Rules.MaxExtension) * ShipScale();
        }

        private float PistonRate(ZtxPistonBaseComponent piston, int mode)
        {
            float[] modes = piston.Rules.SpeedModes;
            float cells = modes != null && mode >= 0 && mode < modes.Length ? TypedSettings.PistonSpeed(piston.Part, modes, mode) : piston.Rules.ExtendSpeed;
            return Math.Max(0.05f, cells) * ShipScale();
        }

        private static float MotorRate(ZtxHingeBaseComponent motorBase, int mode)
        {
            float[] modes = (motorBase.Rules as ZtxMotorBaseRules)?.SpeedModesDeg;
            float deg = modes != null && mode >= 0 && mode < modes.Length ? TypedSettings.MotorSpeed(motorBase.Part, modes, mode) : MOTOR_RETURN_RATE / DEG_TO_RAD;
            return Math.Max(1f, deg) * DEG_TO_RAD;
        }

        private static float DriveAccel(Part part, float rate, float accelTime)
        {
            if (!LogicSource.SmoothAccel(part, LogicSource.Mode(part, s_driveAccelToggleId, 0) != 0)) return float.PositiveInfinity;
            return rate / Math.Max(0.05f, accelTime);
        }

        private void CreateJointFor(ZtxHingeBaseComponent hingeBase, Ship shipA, Ship shipB, Part basePart, Part tipPart,
            Vector2 pivotWorld, float refAngle, float lower, float upper)
        {
            if (hingeBase is ZtxPistonBaseComponent pistonBase)
            {
                IntVector2 f = BaseFacing(basePart);
                System.Numerics.Vector2 axisWorld = shipA.Physics.Body.GetWorldVector(new System.Numerics.Vector2(f.X, f.Y));
                Vector2 anchorB = TipEdgeWorld(shipB, tipPart);
                HingeJointManager.CreateTaggedSlider(hingeBase, shipA, shipB, pivotWorld, anchorB, axisWorld,
                    collideConnected: true, basePart, tipPart, refAngle);
                foreach (Part rod in pistonBase.Rods)
                    HingeJointManager.AddFilteredPart(pistonBase, rod);
                PistonRodRenderer.Attach(shipA.Sim);
                return;
            }
            HingeJointManager.CreateTaggedPin(hingeBase, shipA, shipB, pivotWorld,
                collideConnected: true, basePart, tipPart, refAngle, lower, upper, TipEdgeWorld(shipB, tipPart));
        }

        private bool IsMergeEligible(ZtxHingeBaseComponent hingeBase)
        {
            Part basePart = hingeBase.Part;
            Part tipPart = hingeBase.PairedTip?.Part;
            if (basePart == null || tipPart == null) return false;
            Ship shipB = tipPart.Ship;
            if (shipB == null || shipB == base.Ship) return false;
            return tipPart.Location + TipFacing(tipPart) == basePart.Location;
        }

        private void TryMergeBack(ZtxHingeBaseComponent hingeBase)
        {
            Ship shipA = base.Ship;
            Ship shipB = hingeBase.PairedTip?.Part?.Ship;
            if (shipA == null || shipB == null || shipB == shipA)
            {
                hingeBase.Returning = false;
                return;
            }

            var others = new List<object>();
            HingeJointManager.GetTagsLinking(shipB, shipA, hingeBase, others);
            foreach (object tag in others)
            {
                if (!(tag is ZtxHingeBaseComponent other)) continue;
                bool flush;
                if (other is ZtxPistonBaseComponent)
                    flush = HingeJointManager.TryGetSliderState(other, out float t, out _) && Math.Abs(t) <= MERGE_MAX_TRANSLATION;
                else
                    flush = HingeJointManager.TryGetJointState(other, out float ang, out _) && Math.Abs(ang) <= MERGE_MAX_ANGLE;
                if (!other.Returning || !flush)
                {
                    LogRejectOnce(hingeBase, "section is also held by the joint base at " + other.Part?.Location + " -- waiting for it to disengage and return to flush before merging");
                    return;
                }
            }
            hingeBase.LastRejectReason = null;

            if (ShipMerger.Merge(shipA, shipB, hingeBase))
            {
                hingeBase.Returning = false;
                hingeBase.ClearPair();
                Log.Info($"Hinge base at {hingeBase.Part?.Location}: segment merged back -- ship is whole again. Re-engage the hinge to split it off anew.");
                AfterMergeBack(hingeBase);
            }
            else
            {
                hingeBase.Returning = false;
                HingeJointManager.ReleaseByTag(hingeBase);
                hingeBase.ClearPair();
                Log.Info($"Hinge base at {hingeBase.Part?.Location}: merge blocked -- joint released instead (see merge log line above).");
            }
        }

        private void TrySplitPair(ZtxHingeBaseComponent hingeBase, Part basePart, ZtxHingeTipComponent tip)
        {
            Ship ship = base.Ship;
            Part tipPart = tip.Part;
            if (tipPart == null || tipPart.Ship != ship) return;

            if (MergeService.REBUILD_ENABLED && MergeStore.ReadState(basePart) == MergeState.Lost)
            {
                MergeRecord lost = MergeStore.Read(basePart);
                if (lost != null && !MergeRebuild.IsComplete(basePart, lost, out int built, out int total))
                {
                    LogRejectOnce(hingeBase, $"section being rebuilt ({built} of {total} parts)");
                    return;
                }
            }

            List<Part> neighbours = StructuralAdjacency.Neighbours(ship, basePart);
            if (!neighbours.Contains(tipPart)) neighbours.Add(tipPart);
            if (neighbours.Count < 2)
            {
                LogRejectOnce(hingeBase, "base is only connected to the tip -- it needs its own attachment to the ship");
                return;
            }

            List<Part> farParts;
            GripArea gripArea = hingeBase is ZtxPistonBaseComponent ? GripStore.Read(basePart) : default;
            if (gripArea.On)
            {
                farParts = GripService.EngageSet(ship, basePart, tipPart, gripArea, out int cutLinks);
                foreach (Part n in neighbours)
                {
                    if (n != tipPart && farParts.Contains(n))
                    {
                        LogRejectOnce(hingeBase, "tip side loops back to the base inside the move area -- cannot articulate");
                        return;
                    }
                }
                Log.Info($"Piston base at {basePart.Location}: move area -- the section is {farParts.Count} part(s), {cutLinks} link(s) cut at the area edge.");
            }
            else
            {
                var componentOf = new Dictionary<Part, int>();
                int componentCount = 0;
                foreach (Part seed in neighbours)
                {
                    if (componentOf.ContainsKey(seed)) continue;
                    int id = componentCount++;
                    var queue = new Queue<Part>();
                    queue.Enqueue(seed);
                    componentOf[seed] = id;
                    while (queue.Count > 0)
                    {
                        Part current = queue.Dequeue();
                        foreach (Part next in StructuralAdjacency.Neighbours(ship, current))
                        {
                            if (next == basePart || componentOf.ContainsKey(next))
                                continue;
                            componentOf[next] = id;
                            queue.Enqueue(next);
                        }
                    }
                }

                int tipComponent = componentOf[tipPart];

                foreach (Part n in neighbours)
                {
                    if (n != tipPart && componentOf[n] == tipComponent)
                    {
                        LogRejectOnce(hingeBase, "tip side loops back to the base through other structure -- cannot articulate");
                        return;
                    }
                }

                farParts = new List<Part>();
                foreach (var kv in componentOf)
                    if (kv.Value == tipComponent)
                        farParts.Add(kv.Key);
            }

            Vector2 pivotGrid = (CellCenter(basePart.Location) + CellCenter(tipPart.Location)) * 0.5f;

            Ship shipB = ShipSplitter.Split(ship, farParts, basePart);
            if (shipB == null || shipB.Physics?.Body == null)
            {
                Log.Error("Hinge split failed or far segment has no body -- no joint created.");
                return;
            }

            Vector2 pivotWorld = ship.Physics.Body.GetWorldPoint(pivotGrid * ship.Scale);
            GetAngleLimits(basePart, out float lower, out float upper);
            float refAngle = ComputeStraightAngle(ship, basePart, shipB, tipPart);
            CreateJointFor(hingeBase, ship, shipB, basePart, tipPart, pivotWorld, refAngle, lower, upper);
            hingeBase.PairedTip = tip;
            tip.PairedBase = hingeBase;
            hingeBase.Serviced = true;
            LogRejectOnce(hingeBase, null);
            if (MergeService.REBUILD_ENABLED)
                MergeStore.Write(basePart, MergeRebuild.RecordOf(basePart, farParts, GridTransform.Identity, 0, MergeState.Out));
        }

        private static readonly List<Ship> s_assemblyScratch = new List<Ship>();

        private static Ship FindShipAtWorldPoint(Ship ship, Vector2 worldPoint)
        {
            s_assemblyScratch.Clear();
            HingeJointManager.GetAssemblyShips(ship, s_assemblyScratch);
            foreach (Ship m in s_assemblyScratch)
            {
                if (m == null || m == ship || m.Physics?.Body == null) continue;
                if (PartAtWorldPoint(m, worldPoint) != null) return m;
            }
            return ship.Sim.Ships.DetFindUnderPoint(worldPoint, approximate: false, junk: null, condition: s => s != ship);
        }

        private static Ship FindTipShipAtWorldPoint(Ship ship, Vector2 worldPoint)
        {
            s_assemblyScratch.Clear();
            HingeJointManager.GetAssemblyShips(ship, s_assemblyScratch);
            foreach (Ship m in s_assemblyScratch)
            {
                if (m == null || m == ship || m.Physics?.Body == null) continue;
                if (HoldsFreeTip(m, worldPoint)) return m;
            }
            return ship.Sim.Ships.DetFindUnderPoint(worldPoint, approximate: true, junk: null,
                condition: s => s != ship && s.Physics?.Body != null && HoldsFreeTip(s, worldPoint));
        }

        private static bool HoldsFreeTip(Ship other, Vector2 worldPoint)
        {
            ZtxHingeTipComponent t = ZtxHingeTipComponent.ForPart(PartAtWorldPoint(other, worldPoint));
            return t != null && t.PairedBase == null;
        }

        private static Part PartAtWorldPoint(Ship other, Vector2 worldPoint)
        {
            Vector2 grid = other.Physics.Body.GetLocalPoint(worldPoint) / other.Scale;
            var cell = new IntVector2((int)Math.Floor(grid.X), (int)Math.Floor(grid.Y));
            return other.Parts[cell, PartRectType.Physical];
        }

        private void TryFlushPair(ZtxHingeBaseComponent hingeBase, Part basePart, Ship knownOther = null, bool atLoad = false)
        {
            Ship ship = base.Ship;
            if (ship?.Sim == null) return;

            IntVector2 hc = basePart.Location;
            IntVector2 nc = hc + BaseFacing(basePart);
            int rodCount = 0;
            var pist = hingeBase as ZtxPistonBaseComponent;
            if (pist != null)
                nc = AdoptRods(pist, ship, basePart, out rodCount);
            if (ship.Parts[nc, PartRectType.Physical] != null)
            {
                LogRejectOnce(hingeBase, "arrow edge blocked by own hull and no adjacent tip -- rotate the base or build the tip at the arrow");
                return;
            }

            if (pist == null && TryFindSwungTip(hingeBase, basePart, out Ship swung, out Part swungPart, out ZtxHingeTipComponent swungTip,
                    out float swungRef, out Vector2 swungPivot, out float swungAngle))
            {
                FinishFlushPair(hingeBase, ship, basePart, swung, swungPart, swungTip, swungPivot, swungRef, 0,
                    Math.Abs(swungAngle) > MAX_ALIGN_ANGLE ? $" at {swungAngle * 57.3f:F0} deg (relinked mid-swing)" : "");
                return;
            }

            Vector2 probeWorld = ship.Physics.Body.GetWorldPoint(CellCenter(nc) * ship.Scale);
            Ship other = knownOther ?? FindShipAtWorldPoint(ship, probeWorld);
            if (other?.Physics?.Body == null)
            {
                if (atLoad) return;
                if (pist != null && rodCount > 0)
                {
                    if (RodLineAwaitsRebuild(pist))
                    {
                        LogRejectOnce(hingeBase, $"{rodCount} rod segment(s) rebuilt ahead of their head -- waiting for the rebuild to finish");
                        return;
                    }
                    RemoveAllRods(pist, includeUnadopted: true);
                    Log.Info($"Piston base at {basePart.Location}: {rodCount} rod segment(s) led to no tip -- stub removed.");
                    return;
                }
                LogRejectOnce(hingeBase, "no tip at the arrow edge (build a Hinge Tip against the arrow, or bring a ship's tip flush)");
                return;
            }

            Vector2 otherLocal = other.Physics.Body.GetLocalPoint(probeWorld);
            Vector2 otherGrid = otherLocal / other.Scale;
            var otherCell = new IntVector2(
                (int)Math.Floor(otherGrid.X),
                (int)Math.Floor(otherGrid.Y));
            Part otherPart = other.Parts[otherCell, PartRectType.Physical];
            ZtxHingeTipComponent tip = ZtxHingeTipComponent.ForPart(otherPart);
            if (tip == null)
            {
                LogRejectOnce(hingeBase, $"ship {other.UniqueID} is at the arrow edge but that cell holds no Hinge Tip");
                return;
            }
            if (tip.PairedBase != null)
            {
                LogRejectOnce(hingeBase, $"tip on ship {other.UniqueID} is already paired with another base");
                return;
            }

            float refAngle = ComputeStraightAngle(ship, basePart, other, otherPart);
            float currentRel = other.Physics.Body.Rotation - ship.Physics.Body.Rotation;
            float angleError = Math.Abs(refAngle - currentRel);
            if (angleError > MAX_ALIGN_ANGLE)
            {
                if (pist == null)
                {
                    IntVector2 sf = BaseFacing(basePart);
                    Vector2 seamWorld = ship.Physics.Body.GetWorldPoint((CellCenter(hc) + new Vector2(sf.X * 0.5f, sf.Y * 0.5f)) * ship.Scale);
                    Vector2 edgeWorld = TipEdgeWorld(other, otherPart);
                    float sc = ShipScale();
                    float ex = (edgeWorld.X - seamWorld.X) / sc, ey = (edgeWorld.Y - seamWorld.Y) / sc;
                    float joint = currentRel - refAngle;
                    SwingLimits(hingeBase, basePart, out float sLower, out float sUpper);
                    if (HingeRelink.EdgeOnPivot(ex, ey) && HingeRelink.AcceptsSwing(joint, sLower, sUpper, MAX_ALIGN_ANGLE))
                    {
                        FinishFlushPair(hingeBase, ship, basePart, other, otherPart, tip, seamWorld, refAngle, 0,
                            $" at {joint * 57.3f:F0} deg (relinked mid-swing)");
                        return;
                    }
                }
                LogRejectOnce(hingeBase, $"tip on ship {other.UniqueID} found but misaligned by {angleError * 57.3f:F0} deg (arrows must oppose)");
                return;
            }

            IntVector2 bf = BaseFacing(basePart);
            Vector2 pivotGrid = CellCenter(hc) + new Vector2(bf.X * 0.5f, bf.Y * 0.5f);
            Vector2 pivotWorld = ship.Physics.Body.GetWorldPoint(pivotGrid * ship.Scale);
            if (pist != null)
            {
                Vector2 tipEdge = TipEdgeWorld(other, otherPart);
                System.Numerics.Vector2 axisN = ship.Physics.Body.GetWorldVector(new System.Numerics.Vector2(bf.X, bf.Y));
                float dx = tipEdge.X - pivotWorld.X, dy = tipEdge.Y - pivotWorld.Y;
                float scale = ShipScale();
                float axial = (dx * axisN.X + dy * axisN.Y) / scale;
                float lateral = Math.Abs(dx * axisN.Y - dy * axisN.X) / scale;
                if (lateral > 0.5f || axial < -0.3f || axial > pist.Rules.MaxExtension + 0.5f)
                {
                    LogRejectOnce(hingeBase, $"tip on ship {other.UniqueID} found but off the piston axis (along {axial:F2}, across {lateral:F2} cells)");
                    return;
                }
            }
            FinishFlushPair(hingeBase, ship, basePart, other, otherPart, tip, pivotWorld, refAngle, rodCount, "");
        }

        private void FinishFlushPair(ZtxHingeBaseComponent hingeBase, Ship ship, Part basePart, Ship other, Part otherPart,
            ZtxHingeTipComponent tip, Vector2 pivotWorld, float refAngle, int rodCount, string note)
        {
            GetAngleLimits(basePart, out float lower, out float upper);
            CreateJointFor(hingeBase, ship, other, basePart, otherPart, pivotWorld, refAngle, lower, upper);
            hingeBase.PairedTip = tip;
            tip.PairedBase = hingeBase;
            hingeBase.Serviced = true;
            LogRejectOnce(hingeBase, null);
            Log.Info($"Hinge base at {basePart.Location} paired with flush tip on ship {other.UniqueID}" + note + (rodCount > 0 ? $" past {rodCount} rod segment(s)." : "."));
        }

        private bool TryFindSwungTip(ZtxHingeBaseComponent hingeBase, Part basePart, out Ship other, out Part otherPart,
            out ZtxHingeTipComponent tip, out float refAngle, out Vector2 pivotWorld, out float jointAngle)
        {
            Ship ship = base.Ship;
            other = null;
            otherPart = null;
            tip = null;
            refAngle = 0f;
            jointAngle = 0f;
            IntVector2 bf = BaseFacing(basePart);
            Vector2 pivotGrid = CellCenter(basePart.Location) + new Vector2(bf.X * 0.5f, bf.Y * 0.5f);
            pivotWorld = ship.Physics.Body.GetWorldPoint(pivotGrid * ship.Scale);
            float scale = ShipScale();
            SwingLimits(hingeBase, basePart, out float lower, out float upper);
            double arrow = DetMath.Atan2(bf.Y, bf.X);
            foreach (float deg in HingeRelink.PROBE_DEGREES)
            {
                double a = arrow + deg * DEG_TO_RAD;
                Vector2 probeGrid = pivotGrid + new Vector2((float)DetMath.Cos(a) * HingeRelink.PROBE_RADIUS, (float)DetMath.Sin(a) * HingeRelink.PROBE_RADIUS);
                Vector2 probeWorld = ship.Physics.Body.GetWorldPoint(probeGrid * ship.Scale);
                Ship s = FindTipShipAtWorldPoint(ship, probeWorld);
                if (s?.Physics?.Body == null) continue;
                Part p = PartAtWorldPoint(s, probeWorld);
                ZtxHingeTipComponent t = ZtxHingeTipComponent.ForPart(p);
                if (p == null || t == null || t.PairedBase != null) continue;
                Vector2 edge = TipEdgeWorld(s, p);
                float r = ComputeStraightAngle(ship, basePart, s, p);
                float j = s.Physics.Body.Rotation - ship.Physics.Body.Rotation - r;
                if (!HingeRelink.EdgeOnPivot((edge.X - pivotWorld.X) / scale, (edge.Y - pivotWorld.Y) / scale)) continue;
                if (!HingeRelink.AcceptsSwing(j, lower, upper, MAX_ALIGN_ANGLE)) continue;
                other = s;
                otherPart = p;
                tip = t;
                refAngle = r;
                jointAngle = j;
                return true;
            }
            return false;
        }

        private static void SwingLimits(ZtxHingeBaseComponent hingeBase, Part basePart, out float lower, out float upper)
        {
            if (hingeBase is ZtxMotorBaseComponent)
            {
                lower = -90f * DEG_TO_RAD;
                upper = 90f * DEG_TO_RAD;
                return;
            }
            GetAngleLimits(basePart, out lower, out upper);
        }

        private static Vector2 CellCenter(IntVector2 cell)
        {
            return new Vector2(cell.X + 0.5f, cell.Y + 0.5f);
        }

        private static Vector2 TipEdgeWorld(Ship shipB, Part tipPart)
        {
            IntVector2 tf = TipFacing(tipPart);
            float[] g = HingeRelink.TipAnchorGrid(tipPart.Location.X, tipPart.Location.Y, tf.X, tf.Y);
            return shipB.Physics.Body.GetWorldPoint(new Vector2(g[0], g[1]) * shipB.Scale);
        }

        internal int RelinkAtLoad(out int tried)
        {
            tried = 0;
            int paired = 0;
            Ship ship = base.Ship;
            if (ship?.Sim == null || ship.Physics?.Body == null || _bases.Count == 0) return 0;
            HashSet<IntVector2> pendingRebuild = MergeRebuild.PendingCells(ship);
            foreach (ZtxHingeBaseComponent hingeBase in _bases.ToArray())
            {
                Part basePart = hingeBase.Part;
                if (basePart == null || basePart.Ship != ship) continue;
                bool awaiting = pendingRebuild != null && pendingRebuild.Contains(basePart.Location);
                if (!HingeRelink.RelinksAtLoad(MergeService.IsHeld(hingeBase), hingeBase.Serviced, IsHingeActive(basePart), awaiting)) continue;
                if (FindAdjacentTip(ship, basePart) != null) continue;
                tried++;
                TryFlushPair(hingeBase, basePart, atLoad: true);
                if (hingeBase.Serviced) paired++;
            }
            return paired;
        }
    }
}
