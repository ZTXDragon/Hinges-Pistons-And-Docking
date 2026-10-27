using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;

namespace ZTX.HingesPistonsDocking
{
    internal struct PistonState
    {
        public bool Engaged;
        public bool AtTarget;
        public bool Merged;
        public bool Holding;
        public bool GripRefused;
        public string Why;
    }

    internal static class PistonSettle
    {
        public static int Check(PistonState st, SeqStep s, out string why)
        {
            why = "";
            if (!st.Engaged) { why = "piston not ACTIVE"; return SeqRunner.RUNNING; }
            if (!st.AtTarget) { why = string.IsNullOrEmpty(st.Why) ? "moving" : st.Why; return SeqRunner.RUNNING; }
            if (s.Grip == GripStore.GRIP_ATTACH && !st.Holding)
            {
                if (st.GripRefused) { why = "attach refused"; return SeqRunner.REFUSED; }
                why = st.Merged ? "merged -- attach waits for an unmerge" : "attaching";
                return SeqRunner.RUNNING;
            }
            if (s.Grip == GripStore.GRIP_RELEASE && st.Holding)
            {
                if (st.GripRefused) { why = "release refused"; return SeqRunner.REFUSED; }
                why = st.Merged ? "merged -- release waits for an unmerge" : "releasing";
                return SeqRunner.RUNNING;
            }
            if (s.Merge == 1 && !st.Merged) { why = "merging"; return SeqRunner.RUNNING; }
            if (s.Merge == 2 && st.Merged) { why = "unmerging"; return SeqRunner.RUNNING; }
            return SeqRunner.DONE;
        }
    }

    internal static class PistonToggles
    {
        public const int EXT = 0, SPEED = 1, STRENGTH = 2, ACCEL = 3, MERGE = 4, GRIP = 5, TYPED_SPEED = 6;

        private static readonly ID<PartToggleGuiRules>[] s_ids =
        {
            new ID<PartToggleGuiRules>("ztx_piston_extension"),
            new ID<PartToggleGuiRules>("ztx_piston_speed"),
            new ID<PartToggleGuiRules>("ztx_motor_strength"),
            new ID<PartToggleGuiRules>("ztx_drive_accel"),
            new ID<PartToggleGuiRules>("ztx_piston_merge"),
            new ID<PartToggleGuiRules>("ztx_piston_grip"),
        };

        private static readonly string[] s_names = { "extension", "speed", "strength", "acceleration", "merge", "grip" };

        public static List<(int, int)> PlanDo(LogicAction a, bool on, float[] speeds, int maxCells)
        {
            var w = new List<(int, int)>();
            bool poseSet = on ? a.OnSet : a.OffSet;
            if (poseSet) w.Add((EXT, Cells(on ? a.OnPose : a.OffPose, maxCells)));
            if (a.Speed > 0) AddSpeed(w, a.Speed, speeds);
            if (a.Strength > 0) w.Add((STRENGTH, a.Strength - 1));
            if (a.Accel > 0) w.Add((ACCEL, a.Accel - 1));
            if (a.MergeAt == LogicAction.MERGE_NEVER) w.Add((MERGE, 0));
            else if (a.MergeAt != 0) w.Add((MERGE, (on ? (a.MergeAt & 1) != 0 : (a.MergeAt & 2) != 0) ? 1 : 0));
            if (a.Grip != 0) w.Add((GRIP, a.Grip == LogicAction.GRIP_SET_OFF ? GripStore.GRIP_OFF : a.Grip));
            return w;
        }

        public static List<(int, int)> PlanStep(SeqStep s, float[] speeds, int maxCells)
        {
            var w = new List<(int, int)>();
            if (s.PoseSet) w.Add((EXT, Cells(s.Pose, maxCells)));
            if (s.Speed > 0) AddSpeed(w, s.Speed, speeds);
            if (s.Strength > 0) w.Add((STRENGTH, s.Strength - 1));
            if (s.Accel > 0) w.Add((ACCEL, s.Accel - 1));
            if (s.Merge == 1 || s.Merge == 2) w.Add((MERGE, s.Merge == 1 ? 1 : 0));
            if (s.Grip == GripStore.GRIP_ATTACH || s.Grip == GripStore.GRIP_RELEASE) w.Add((GRIP, s.Grip));
            else if (s.Grip == LogicAction.GRIP_SET_OFF) w.Add((GRIP, GripStore.GRIP_OFF));
            return w;
        }

        private static void AddSpeed(List<(int, int)> w, int tenths, float[] speeds)
        {
            w.Add((SPEED, NearestSpeed(speeds, tenths / 10f)));
            int min = (int)Math.Round(TypedSettings.PISTON_SPEED_MIN * TypedSettings.PISTON_SPEED_SCALE);
            int max = (int)Math.Round(TypedSettings.PISTON_SPEED_MAX * TypedSettings.PISTON_SPEED_SCALE);
            w.Add((TYPED_SPEED, Math.Max(min, Math.Min(max, tenths * 10))));
        }

        public static bool ShouldWrite(bool primed, int lastOn, bool on, bool edited)
        {
            if (!primed) return false;
            if (edited) return true;
            return lastOn != (on ? 1 : 0);
        }

        public static bool ShouldWriteHeld(bool primed, int lastOn, bool on, bool edited, bool hold) =>
            ShouldWrite(primed, lastOn, on, edited) || (primed && hold);

        public static int Cells(int tenths, int maxCells) => Math.Max(0, Math.Min(Math.Max(0, maxCells), (int)Math.Round(tenths / 10f, MidpointRounding.AwayFromZero)));

        public static int NearestSpeed(float[] speeds, float cellsPerSecond)
        {
            if (speeds == null || speeds.Length == 0) return 0;
            int best = 0;
            for (int i = 1; i < speeds.Length; i++)
                if (Math.Abs(speeds[i] - cellsPerSecond) < Math.Abs(speeds[best] - cellsPerSecond)) best = i;
            return best;
        }

        public static ID<PartToggleGuiRules> IdOf(int kind) => s_ids[kind];

        public static string Apply(Part part, List<(int Kind, int Mode)> plan)
        {
            if (part == null || part.IsDestroyed || part.Ship == null || plan == null) return "";
            string changed = "";
            foreach (var (kind, mode) in plan)
            {
                if (kind == TYPED_SPEED)
                {
                    int stored = TypedSettings.Encode(mode / (float)TypedSettings.PISTON_SPEED_SCALE, TypedSettings.PISTON_SPEED_SCALE,
                        TypedSettings.PISTON_SPEED_MIN, TypedSettings.PISTON_SPEED_MAX);
                    if (TypedSettings.Stored(part, TypedSettings.PistonSpeedId) == stored) continue;
                    TypedSettings.Store(part, TypedSettings.PistonSpeedId, stored);
                    changed += (changed.Length > 0 ? ", " : "") + "typed speed " + TypedSettings.Format(mode / (float)TypedSettings.PISTON_SPEED_SCALE);
                    continue;
                }
                PartUIToggle t = PartUIToggle.GetOnPart(part, s_ids[kind]);
                if (t == null || t.Mode == mode) continue;
                t.Mode = mode;
                t.Part.MatchingBlueprintPart?.OnPartUIToggleStatesChanged();
                changed += (changed.Length > 0 ? ", " : "") + s_names[kind] + " " + mode;
            }
            return changed;
        }

        public static void Limits(ZtxPistonBaseComponent piston, out float[] speeds, out int maxCells)
        {
            speeds = piston?.Rules.SpeedModes;
            maxCells = piston != null ? (int)Math.Floor(piston.Rules.MaxExtension + 0.001f) : 0;
        }

        public static string ApplyStep(Part part, SeqStep s)
        {
            ZtxPistonBaseComponent piston = GripService.PistonOf(part);
            if (piston == null)
            {
                int k = LogicAction.KindOf(part);
                if (k == LogicAction.KIND_CART || k == LogicAction.KIND_SWITCH) return RailToggles.ApplyStep(part, k, s);
                if (k == LogicAction.KIND_DOCK) return DockToggles.ApplyStep(part, k, s);
                if (k == LogicAction.KIND_BAY || k == LogicAction.KIND_BAY_STATION) return "";
                return ArmToggles.ApplyStep(part, k, s);
            }
            Limits(piston, out float[] speeds, out int max);
            return Apply(part, PlanStep(s, speeds, max));
        }

        public static int Settle(Part part, SeqStep s, int startTick, out string why)
        {
            ZtxPistonBaseComponent piston = GripService.PistonOf(part);
            if (piston == null)
            {
                int kind = LogicAction.KindOf(part);
                if (kind == 1 || kind == 3) return ArmToggles.SettleStep(part, kind, s, out why);
                if (kind == LogicAction.KIND_CART || kind == LogicAction.KIND_SWITCH) return RailToggles.SettleStep(part, kind, s, startTick, out why);
                if (kind == LogicAction.KIND_DOCK) return DockToggles.SettleStep(part, kind, s, out why);
                why = "not a piston, motor, hinge, cart or switch";
                return SeqRunner.REFUSED;
            }
            var st = new PistonState
            {
                Engaged = HingeShipManager.IsHingeActive(part),
                Merged = MergeService.IsMerged(part),
                GripRefused = GripService.RefusedSince(piston, startTick, out _),
            };
            st.AtTarget = LogicActuatorState.IsDone(part, out st.Why);
            st.Holding = st.Merged ? (MergeStore.Read(part)?.MemberCount ?? 1) > 1 : GripService.IsHolding(piston);
            return PistonSettle.Check(st, s, out why);
        }
    }
}
