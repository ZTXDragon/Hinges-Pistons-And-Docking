using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal enum ActPhase { Unknown = 0, Moving = 1, AtOn = 2, AtOff = 3 }

    internal static class LogicActuatorState
    {
        private const float ANGLE_TOL = 0.06f;
        private const float SPIN_TOL = 0.05f;
        private const float SLIDE_TOL_CELLS = 0.1f;
        private const float SLIDE_SPEED_TOL = 0.05f;
        private const float WELD_TOL = 0.03f;
        private const float DEG_TO_RAD = (float)Math.PI / 180f;

        private static readonly ID<PartComponentRules> s_hingeId = new ID<PartComponentRules>("ZtxHingeBase");
        private static readonly ID<PartComponentRules> s_motorId = new ID<PartComponentRules>("ZtxMotorBase");
        private static readonly ID<PartComponentRules> s_pistonId = new ID<PartComponentRules>("ZtxPistonBase");
        private static readonly ID<PartComponentRules> s_padId = new ID<PartComponentRules>("ZtxDockPad");

        private static readonly ID<PartToggleGuiRules> s_dockActive = new ID<PartToggleGuiRules>("ztx_dock_active");

        public static int PoseKind(Part p)
        {
            if (p == null || p.IsDestroyed) return 0;
            string id = p.Rules.ID.ToString();
            return id == "ztx.hinges_pistons_docking.motor_base" || id == "ztx.hinges_pistons_docking.corridor_hinge_base" ? 1 : id == "ztx.hinges_pistons_docking.piston_base" || id == "ztx.hinges_pistons_docking.corridor_piston_base" ? 2 : 0;
        }

        public static float MaxPose(Part p)
        {
            if (PoseKind(p) == 2 && p.TryGetComponent<ZtxHingeBaseComponent>(s_pistonId, out ZtxHingeBaseComponent b) && b is ZtxPistonBaseComponent piston)
                return piston.Rules.MaxExtension;
            return 180f;
        }

        public static bool IsDone(Part p) => IsDone(p, out _);

        public static bool IsDone(Part p, out string why)
        {
            why = "";
            if (p == null || p.IsDestroyed || p.Ship == null) return false;
            try
            {
                switch (p.Rules.ID.ToString())
                {
                    case "ztx.hinges_pistons_docking.hinge_base": return JointDone(p, s_hingeId, out why);
                    case "ztx.hinges_pistons_docking.motor_base":
                    case "ztx.hinges_pistons_docking.corridor_hinge_base": return JointDone(p, s_motorId, out why);
                    case "ztx.hinges_pistons_docking.piston_base":
                    case "ztx.hinges_pistons_docking.corridor_piston_base": return JointDone(p, s_pistonId, out why);
                    case "ztx.hinges_pistons_docking.rail_terminal": return RailShipManager.IsTerminalDone(p, out why);
                    case "ztx.hinges_pistons_docking.rail_cart": return RailShipManager.IsCartDone(p, out why);
                    case "ztx.hinges_pistons_docking.rail_switch":
                    {
                        bool open = RailNetwork.IsOpen(p);
                        why = open ? "open" : "closed";
                        return open;
                    }
                    case "ztx.hinges_pistons_docking.dock_clamp": return ClampDone(p, out why);
                    case "ztx.hinges_pistons_docking.dock_pad": return PadDone(p, out why);
                    case "ztx.hinges_pistons_docking.bay_terminal": return BayDone(p, out why);
                    case "ztx.hinges_pistons_docking.logic_adapter":
                    {
                        Part target = AdapterToggles.TargetOf(p);
                        why = target != null ? "linked to " + target.Rules.NameKey.Text : "no part at the arrow";
                        return target != null;
                    }
                    case "ztx.hinges_pistons_docking.indicator_lamp":
                    {
                        int st = LogicSource.Mode(p, LampToggles.StateId, LampToggles.STATE_OFF);
                        why = st == LampToggles.STATE_BLINK ? "blinking" : st == LampToggles.STATE_ON ? "lit" : "off";
                        return true;
                    }
                    case "ztx.hinges_pistons_docking.bay_station":
                        why = BayManager.IsOn(p, BayManager.StationOnId) ? "on" : "off";
                        return true;
                    default:
                        why = "no read-back";
                        return false;
                }
            }
            catch (Exception ex)
            {
                LogThrottle.Exception("LogicActuatorState.IsDone", ex);
                why = "error";
                return false;
            }
        }

        private static readonly HashSet<string> s_movingWhys = new HashSet<string>
        {
            "moving", "returning", "releasing", "turning to lock", "swinging", "merging", "disconnecting", "building",
        };

        public static ActPhase Phase(Part p)
        {
            if (p == null || p.IsDestroyed || p.Ship == null) return ActPhase.Unknown;
            string id = p.Rules.ID.ToString();
            if (id == "ztx.hinges_pistons_docking.rail_switch") return RailNetwork.IsOpen(p) ? ActPhase.AtOn : ActPhase.AtOff;
            bool done = IsDone(p, out string why);
            if (!done)
                return s_movingWhys.Contains(why) || why.EndsWith(" at rest") ? ActPhase.Moving : ActPhase.Unknown;
            return CommandedOn(p, id) ? ActPhase.AtOn : ActPhase.AtOff;
        }

        private static bool CommandedOn(Part p, string id)
        {
            switch (id)
            {
                case "ztx.hinges_pistons_docking.dock_clamp":
                case "ztx.hinges_pistons_docking.dock_pad": return LogicSource.IsOn(p, DockShipManager.PadActiveId);
                case "ztx.hinges_pistons_docking.bay_terminal": return BayManager.IsOn(p, BayManager.BuildId);
                case "ztx.hinges_pistons_docking.bay_station": return BayManager.IsOn(p, BayManager.StationOnId);
                default: return LogicSource.Pushed(p) ?? false;
            }
        }

        public static bool Position(Part p, out float percent, out float units)
        {
            percent = 0f;
            units = 0f;
            int kind = LogicAction.KindOf(p);
            if (!LogicAction.IsPositionedKind(kind) || p.Ship == null) return false;
            ID<PartComponentRules> cid = kind == 1 ? s_motorId : kind == 2 ? s_pistonId : s_hingeId;
            bool has = p.TryGetComponent<ZtxHingeBaseComponent>(cid, out ZtxHingeBaseComponent b) && b != null;
            if (has && b is ZtxPistonBaseComponent merged && MergeService.TryGetMergedSlide(p, out int mk))
            {
                units = mk;
                float mmax = merged.Rules.MaxExtension;
                percent = mmax > 0f ? mk / mmax * 100f : 0f;
                return true;
            }
            if (!has || !HingeJointManager.HasPinWithTag(b)) return false;
            if (b is ZtxPistonBaseComponent piston)
            {
                if (!HingeJointManager.TryGetSliderState(b, out float slide, out _)) return false;
                units = slide / Scale(p.Ship);
                float max = piston.Rules.MaxExtension;
                percent = max > 0f ? units / max * 100f : 0f;
                return true;
            }
            if (!HingeJointManager.TryGetJointState(b, out float angle, out _)) return false;
            units = angle / DEG_TO_RAD + 90f;
            percent = units / 180f * 100f;
            return true;
        }

        private static bool JointDone(Part p, ID<PartComponentRules> id, out string why)
        {
            if (!p.TryGetComponent<ZtxHingeBaseComponent>(id, out ZtxHingeBaseComponent b) || b == null)
            {
                why = "no component";
                return false;
            }
            bool engaged = JointPower.IsOn(p);
            bool jointed = HingeJointManager.HasPinWithTag(b);
            if (b.Returning) { why = "returning"; return false; }
            if (!engaged)
            {
                if (jointed) { why = "releasing"; return false; }
                why = "disengaged";
                return true;
            }
            if (b is ZtxPistonBaseComponent mergedPiston && MergeService.TryGetMergedSlide(p, out int mergedK))
            {
                float want = HingeShipManager.CommandedCells(mergedPiston);
                bool atK = Math.Abs(want - mergedK) <= SLIDE_TOL_CELLS;
                why = atK ? "merged at " + mergedK + " cells" : "held (merged)";
                return atK;
            }
            if (!jointed) { why = "not formed"; return false; }

            bool? signal = LogicSource.Read(p);

            if (b is ZtxPistonBaseComponent piston)
            {
                if (piston.RodBroken) { why = "rod broken"; return false; }
                float cells = HingeShipManager.CommandedCells(piston);
                float scale = Scale(p.Ship);
                float target = cells * scale;
                if (!HingeJointManager.TryGetSliderState(b, out float slide, out float speed)) { why = "no slider"; return false; }
                bool at = Math.Abs(slide - target) <= SLIDE_TOL_CELLS * scale && Math.Abs(speed) <= SLIDE_SPEED_TOL;
                bool mergeBlocked = false;
                bool mergeWanted = at && MergeService.MergeWanted(piston, out mergeBlocked);
                bool done = MergeMath.StepDone(at, mergeWanted, false, mergeBlocked);
                why = !at ? "moving" : done ? "at " + cells.ToString("0.#") + " cells" : "merging";
                return done;
            }

            if (!HingeJointManager.TryGetJointState(b, out float angle, out float spin)) { why = "no joint"; return false; }

            if (b is ZtxMotorBaseComponent)
            {
                float degrees = Math.Max(0f, Math.Min(180f, ArmToggles.MotorDegrees(p)));
                float target = (degrees - 90f) * DEG_TO_RAD;
                bool at = Math.Abs(angle - target) <= ANGLE_TOL && Math.Abs(spin) <= SPIN_TOL;
                why = at ? "at " + degrees.ToString("0.#") + " deg" : "moving";
                return at;
            }

            bool settled = Math.Abs(spin) <= SPIN_TOL;
            why = settled ? "settled" : "swinging";
            return settled;
        }

        internal static float ScaleOf(Ship ship) => Scale(ship);

        private static float Scale(Ship ship)
        {
            float s = ship?.Scale.X ?? 1f;
            return s > 0f ? s : 1f;
        }

        private static bool ClampDone(Part p, out string why)
        {
            ZtxDockClampComponent c = ZtxDockClampComponent.ForPart(p);
            if (c == null) { why = "no component"; return false; }
            bool? signal = LogicSource.Read(p);
            bool active = signal ?? LogicSource.IsOn(p, s_dockActive);
            bool jointed = HingeJointManager.HasPinWithTag(c)
                || (c.PairedClamp != null && HingeJointManager.HasPinWithTag(c.PairedClamp));
            Part partner = c.PairedClamp?.Part;
            bool merged = partner != null && !partner.IsDestroyed && ReferenceEquals(partner.Ship, p.Ship);
            bool merging = jointed && (c.Merge.Pending || (c.PairedClamp?.Merge.Pending ?? false));
            if (active)
            {
                why = merged ? "merged" : merging ? "merging" : jointed ? "docked" : "waiting for a partner";
                return merged || (jointed && !merging);
            }
            why = merged ? "disconnecting" : jointed ? "releasing" : "free";
            return !jointed && !merged;
        }

        private static bool PadDone(Part p, out string why)
        {
            if (!p.TryGetComponent<ZtxDockPadComponent>(s_padId, out ZtxDockPadComponent pad) || pad == null)
            {
                why = "no component";
                return false;
            }
            bool? signal = LogicSource.Read(p);
            bool active = signal ?? LogicSource.IsOn(p, DockShipManager.PadActiveId);
            bool docked = pad.DockedShip != null && HingeJointManager.HasPinWithTag(pad);
            if (active)
            {
                why = docked ? "ship parked" : "waiting for a ship";
                return docked;
            }
            why = docked ? "releasing" : "free";
            return !docked;
        }

        private static bool BayDone(Part p, out string why)
        {
            ZtxBayTerminalComponent t = ZtxBayTerminalComponent.ForPart(p);
            if (t == null) { why = "no component"; return false; }
            BayState bay = t.Bay;
            bool build = BayManager.IsOn(p, BayManager.BuildId);
            if (build && BayDesign.CanBuild(bay.Pieces, bay.Design.Count) && !bay.Built) { why = "building"; return false; }
            why = bay.Built ? "built" : bay.Held != null ? "holding" : "idle";
            return true;
        }

    }
}
