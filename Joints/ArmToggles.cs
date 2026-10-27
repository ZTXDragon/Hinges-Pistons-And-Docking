using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;

namespace ZTX.HingesPistonsDocking
{
    internal static class ArmToggles
    {
        public const int ANGLE = 0, SPEED = 1, STRENGTH = 2, ACCEL = 3, LOCK = 4, TYPED_SPEED = 5;
        public const int ANGLE_MAX_TENTHS = 1800;

        public static readonly ID<PartToggleGuiRules> AngleId = new ID<PartToggleGuiRules>("ztx_motor_angle");
        public static readonly ID<PartToggleGuiRules> OldPositionId = new ID<PartToggleGuiRules>("ztx_motor_position");
        public const int OLD_POSITION_DEFAULT = 9;

        public static readonly ID<PartToggleGuiRules> LockId = new ID<PartToggleGuiRules>("ztx_hinge_lock");

        private static readonly ID<PartToggleGuiRules>[] s_ids =
        {
            AngleId,
            new ID<PartToggleGuiRules>("ztx_motor_speed"),
            new ID<PartToggleGuiRules>("ztx_motor_strength"),
            new ID<PartToggleGuiRules>("ztx_drive_accel"),
            LockId,
        };

        private static readonly string[] s_names = { "angle", "speed", "strength", "acceleration", "lock" };

        public static int DecodeAngle(int stored, int oldMode)
        {
            if (stored > 0) return Math.Min(ANGLE_MAX_TENTHS, stored - 1);
            return Math.Max(0, Math.Min(18, oldMode)) * 100;
        }

        public static int EncodeAngle(int tenths) => Math.Max(0, Math.Min(ANGLE_MAX_TENTHS, tenths)) + 1;

        public static List<(int, int)> PlanMotorDo(LogicAction a, bool on, float[] speedsDeg)
        {
            var w = new List<(int, int)>();
            bool set = on ? a.OnSet : a.OffSet;
            if (set) w.Add((ANGLE, Math.Max(0, Math.Min(ANGLE_MAX_TENTHS, (on ? a.OnPose : a.OffPose) * 10))));
            AddDrive(w, a.Speed, a.Strength, a.Accel, speedsDeg);
            return w;
        }

        public static List<(int, int)> PlanMotorStep(SeqStep s, float[] speedsDeg)
        {
            var w = new List<(int, int)>();
            if (s.PoseSet) w.Add((ANGLE, Math.Max(0, Math.Min(ANGLE_MAX_TENTHS, s.Pose))));
            AddDrive(w, s.Speed, s.Strength, s.Accel, speedsDeg);
            return w;
        }

        private static void AddDrive(List<(int, int)> w, int speedDeg, int strength, int accel, float[] speedsDeg)
        {
            if (speedDeg > 0)
            {
                w.Add((SPEED, PistonToggles.NearestSpeed(speedsDeg, speedDeg)));
                int min = (int)Math.Round(TypedSettings.MOTOR_SPEED_MIN * TypedSettings.MOTOR_SPEED_SCALE);
                int max = (int)Math.Round(TypedSettings.MOTOR_SPEED_MAX * TypedSettings.MOTOR_SPEED_SCALE);
                w.Add((TYPED_SPEED, Math.Max(min, Math.Min(max, speedDeg * TypedSettings.MOTOR_SPEED_SCALE))));
            }
            if (strength > 0) w.Add((STRENGTH, strength - 1));
            if (accel > 0) w.Add((ACCEL, accel - 1));
        }

        public static List<(int, int)> PlanHingeDo(bool on) => new List<(int, int)>();

        public static List<(int, int)> PlanHingeStep(SeqStep s) => new List<(int, int)>();

        public static int Settle(int kind, bool engaged, bool atTarget, SeqStep s, out string why)
        {
            why = "";
            if (kind == 3) return SeqRunner.DONE;
            if (!engaged) { why = "motor not ACTIVE"; return SeqRunner.RUNNING; }
            if (!atTarget) { why = "moving"; return SeqRunner.RUNNING; }
            return SeqRunner.DONE;
        }

        public static float MotorDegrees(Part motor)
        {
            var meta = motor?.Ship?.Metadata;
            if (meta == null) return 90f;
            var psd = new PartSaveData(motor);
            int stored = meta.PartUIToggleStates.Get((psd, AngleId)) ?? 0;
            int oldMode = meta.PartUIToggleStates.Get((psd, OldPositionId)) ?? OLD_POSITION_DEFAULT;
            return DecodeAngle(stored, oldMode) / 10f;
        }

        public static string Apply(Part part, List<(int Kind, int Value)> plan)
        {
            if (part == null || part.IsDestroyed || part.Ship == null || plan == null) return "";
            string changed = "";
            foreach (var (kind, value) in plan)
            {
                if (kind == TYPED_SPEED)
                {
                    int typed = TypedSettings.Encode(value / (float)TypedSettings.MOTOR_SPEED_SCALE, TypedSettings.MOTOR_SPEED_SCALE,
                        TypedSettings.MOTOR_SPEED_MIN, TypedSettings.MOTOR_SPEED_MAX);
                    if (TypedSettings.Stored(part, TypedSettings.MotorSpeedId) == typed) continue;
                    TypedSettings.Store(part, TypedSettings.MotorSpeedId, typed);
                    changed += (changed.Length > 0 ? ", " : "") + "typed speed " + TypedSettings.Format(value / (float)TypedSettings.MOTOR_SPEED_SCALE);
                    continue;
                }
                if (kind == ANGLE)
                {
                    var key = (new PartSaveData(part), AngleId);
                    int stored = EncodeAngle(value);
                    if ((part.Ship.Metadata.PartUIToggleStates.Get(key) ?? 0) == stored) continue;
                    part.Ship.Metadata.PartUIToggleStates.Set(key, stored);
                    changed += (changed.Length > 0 ? ", " : "") + "angle " + (value / 10f).ToString("0.#");
                    continue;
                }
                PartUIToggle t = PartUIToggle.GetOnPart(part, s_ids[kind]);
                if (t == null || t.Mode == value) continue;
                t.Mode = value;
                t.Part.MatchingBlueprintPart?.OnPartUIToggleStatesChanged();
                changed += (changed.Length > 0 ? ", " : "") + s_names[kind] + " " + value;
            }
            return changed;
        }

        public static float[] MotorSpeeds(Part motor)
        {
            foreach (PartComponent c in motor.Components)
                if (c is ZtxMotorBaseComponent m) return (m.Rules as ZtxMotorBaseRules)?.SpeedModesDeg;
            return null;
        }

        public static string ApplyDo(Part part, int kind, LogicAction a, bool on)
        {
            if (kind == 1) return Apply(part, PlanMotorDo(a, on, MotorSpeeds(part)));
            if (kind == 3) return Apply(part, PlanHingeDo(on));
            return "";
        }

        public static string ApplyStep(Part part, int kind, SeqStep s)
        {
            if (kind == 1) return Apply(part, PlanMotorStep(s, MotorSpeeds(part)));
            if (kind == 3) return Apply(part, PlanHingeStep(s));
            return "";
        }

        public static int SettleStep(Part part, int kind, SeqStep s, out string why)
        {
            bool engaged = HingeShipManager.IsHingeActive(part);
            bool at = LogicActuatorState.IsDone(part, out string state);
            int r = Settle(kind, engaged, at, s, out why);
            if (r == SeqRunner.RUNNING && state.Length > 0 && why != "motor not ACTIVE" && why != "hinge not ACTIVE") why = state;
            return r;
        }
    }
}
