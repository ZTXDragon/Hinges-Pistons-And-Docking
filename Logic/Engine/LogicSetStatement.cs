using System;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogicSetStatement
    {
        public static bool CanWait(int kind) =>
            kind == LogicAction.KIND_PISTON || kind == LogicAction.KIND_MOTOR || kind == LogicAction.KIND_CART
            || kind == LogicAction.KIND_DOCK;

        public static bool FromCommand(SeqStep s, int kind, int rule, out LogicAction a)
        {
            a = new LogicAction { Rule = rule, Sided = true, BlankLeaves = true, Target = s.Target };
            if (kind == LogicAction.KIND_PISTON)
            {
                a.OnSet = s.PoseSet;
                a.OnPose = s.Pose;
                a.Speed = s.Speed;
                a.Strength = s.Strength;
                a.Accel = s.Accel;
                a.Grip = s.Grip;
                a.MergeAt = s.Merge == 1 ? 1 : s.Merge == 2 ? LogicAction.MERGE_NEVER : 0;
                return true;
            }
            if (kind == LogicAction.KIND_MOTOR)
            {
                a.OnSet = s.PoseSet;
                a.OnPose = (int)Math.Round(s.Pose / 10f);
                a.Speed = s.Speed;
                a.Strength = s.Strength;
                a.Accel = s.Accel;
                return true;
            }
            return kind == LogicAction.KIND_HINGE;
        }

        public static SeqStep SettleStepOf(LogicAction a, int kind)
        {
            var s = new SeqStep { Kind = SeqKind.Command, Target = a.Target, Grip = a.Grip, Station = a.OnStation };
            if (kind == LogicAction.KIND_PISTON)
            {
                s.PoseSet = a.OnSet;
                s.Pose = a.OnPose;
                s.Merge = a.MergeAt == 1 ? 1 : a.MergeAt == LogicAction.MERGE_NEVER ? 2 : 0;
            }
            else if (kind == LogicAction.KIND_MOTOR)
            {
                s.PoseSet = a.OnSet;
                s.Pose = a.OnPose * 10;
            }
            else if (kind == LogicAction.KIND_DOCK)
            {
                DockToggles.Effective(kind, a.OnSet, a.OnPose, true, out int first, out int second);
                s.Lock = first;
                s.Merge = second;
            }
            return s;
        }
    }
}
