using System;
using System.Collections.Generic;
using Cosmoteer.Ships.Parts;

namespace ZTX.HingesPistonsDocking
{
    internal struct ActSettings
    {
        public float OnPose;
        public float OffPose;
        public int Speed;
        public int Accel;
        public int Strength;
        public int MergeAt;
        public int Grip;

        public static ActSettings None => new ActSettings { OnPose = float.NaN, OffPose = float.NaN };

        public void FillFrom(in ActSettings o)
        {
            if (float.IsNaN(OnPose)) OnPose = o.OnPose;
            if (float.IsNaN(OffPose)) OffPose = o.OffPose;
            if (Speed == 0) Speed = o.Speed;
            if (Accel == 0) Accel = o.Accel;
            if (Strength == 0) Strength = o.Strength;
            if (MergeAt == 0) MergeAt = o.MergeAt;
            if (Grip == 0) Grip = o.Grip;
        }
    }

    internal struct LogicAction
    {
        public const int FORMAT = 1;
        public const int FORMAT_SIDED = 2;
        public const int POSE_MAX = 4095;
        public const int SPEED_MAX = 255;
        public const int STRENGTH_STEPS = 11;

        public LogicRef Target;
        public int Rule;
        public bool Invert;
        public bool Sided;
        public bool WhenOff;
        public int Accel;
        public int Strength;
        public bool OnSet;
        public bool OffSet;
        public int OnPose;
        public int OffPose;
        public int Speed;
        public int MergeAt;
        public int Pos;
        public bool BlankLeaves;
        public bool Hold;
        public const int HOLD_BIT = 31;

        public LogicRef OnStation;
        public LogicRef OffStation;
        public int Drive;

        public const int KIND_MOTOR = 1, KIND_PISTON = 2, KIND_HINGE = 3, KIND_CART = 4, KIND_SWITCH = 5, KIND_DOCK = 6, KIND_BUILD = 7, KIND_BAY = 8, KIND_BAY_STATION = 9, KIND_BUTTON = 10, KIND_ADAPTER = 11, KIND_LAMP = 12;

        public const int MERGE_NEVER = 4;

        public const int GRIP_ATTACH = 1, GRIP_RELEASE = 2;
        public const int GRIP_SET_OFF = 3;
        public int Grip;

        public bool IsUsed => Rule >= 1;

        public int PackMisc()
        {
            if (Rule < 1) return 0;
            bool bit4 = Sided ? WhenOff : Invert;
            return LogicLine.OwnerNibble(Rule) | (bit4 ? 1 << 4 : 0) | ((Accel & 3) << 5) | ((Strength & 15) << 7)
                | (OnSet ? 1 << 11 : 0) | (OffSet ? 1 << 12 : 0) | ((Speed & SPEED_MAX) << 13)
                | (MergeAt == MERGE_NEVER ? 1 << 21 : (MergeAt & 3) << 22) | ((Sided ? FORMAT_SIDED : FORMAT) << 24) | ((Grip & 3) << 28) | (BlankLeaves ? 1 << 30 : 0)
                | (Hold && Sided ? 1 << HOLD_BIT : 0);
        }

        public int PackPose() => (OnPose & POSE_MAX) | ((OffPose & POSE_MAX) << 12);

        public static LogicAction Unpack(int t, int m, int p)
        {
            var a = new LogicAction();
            int format = (m >> 24) & 15;
            if ((format != FORMAT && format != FORMAT_SIDED) || (m & 15) == 0) return a;
            a.Rule = m & 15;
            a.Sided = format == FORMAT_SIDED;
            if (a.Sided) a.WhenOff = (m & (1 << 4)) != 0;
            else a.Invert = (m & (1 << 4)) != 0;
            a.Accel = Math.Min(2, (m >> 5) & 3);
            a.Strength = Math.Min(STRENGTH_STEPS, (m >> 7) & 15);
            a.OnSet = (m & (1 << 11)) != 0;
            a.OffSet = (m & (1 << 12)) != 0;
            a.Speed = (m >> 13) & SPEED_MAX;
            a.MergeAt = (m & (1 << 21)) != 0 ? MERGE_NEVER : (m >> 22) & 3;
            a.Grip = (m >> 28) & 3;
            a.BlankLeaves = (m & (1 << 30)) != 0;
            a.Hold = a.Sided && (m & (1 << HOLD_BIT)) != 0;
            a.OnPose = p & POSE_MAX;
            a.OffPose = (p >> 12) & POSE_MAX;
            a.Target = LogicRef.Unpack(t);
            return a;
        }

        public ActSettings Settings => new ActSettings
        {
            OnPose = OnSet ? OnPose : float.NaN,
            OffPose = OffSet ? OffPose : float.NaN,
            Speed = Speed,
            Accel = Accel,
            Strength = Strength,
            MergeAt = MergeAt,
            Grip = Grip,
        };

        public bool AnySet => OnSet || OffSet || Speed > 0 || Accel > 0 || Strength > 0 || Invert || MergeAt != 0 || Grip != 0
            || OnPose > 0 || OffPose > 0 || !OnStation.IsNone || !OffStation.IsNone || Drive != 0;

        public const int SPLIT_NEVER = -1;
        public const int OLD_PISTON_ON_DEFAULT = 3, OLD_PISTON_OFF_DEFAULT = 0, OLD_MOTOR_POS_DEFAULT = 9;
        public static readonly Cosmoteer.Data.ID<Cosmoteer.Game.PartToggleGuiRules> OldPistonOnId = new Cosmoteer.Data.ID<Cosmoteer.Game.PartToggleGuiRules>("ztx_piston_on_pos");
        public static readonly Cosmoteer.Data.ID<Cosmoteer.Game.PartToggleGuiRules> OldPistonOffId = new Cosmoteer.Data.ID<Cosmoteer.Game.PartToggleGuiRules>("ztx_piston_off_pos");
        public static readonly Cosmoteer.Data.ID<Cosmoteer.Game.PartToggleGuiRules> OldMotorOnId = new Cosmoteer.Data.ID<Cosmoteer.Game.PartToggleGuiRules>("ztx_motor_on_pos");
        public static readonly Cosmoteer.Data.ID<Cosmoteer.Game.PartToggleGuiRules> OldMotorOffId = new Cosmoteer.Data.ID<Cosmoteer.Game.PartToggleGuiRules>("ztx_motor_off_pos");

        public static LogicAction FillOldPopouts(LogicAction a, int kind, int? onStored, int? offStored)
        {
            if (kind == KIND_PISTON || kind == KIND_MOTOR)
            {
                int onDefault = kind == KIND_PISTON ? OLD_PISTON_ON_DEFAULT : OLD_MOTOR_POS_DEFAULT;
                int offDefault = kind == KIND_PISTON ? OLD_PISTON_OFF_DEFAULT : OLD_MOTOR_POS_DEFAULT;
                if (!a.OnSet) { a.OnSet = true; a.OnPose = (onStored ?? onDefault) * 10; }
                if (!a.OffSet) { a.OffSet = true; a.OffPose = (offStored ?? offDefault) * 10; }
            }
            a.BlankLeaves = true;
            return a;
        }

        public static bool SplitDue(int tick, int last, int interval) => last == SPLIT_NEVER || (long)tick - last >= interval;

        public static bool StatementOn(bool sided, bool whenOff)
        {
            SideGate(sided, whenOff, !sided || !whenOff, out bool planOn);
            return planOn;
        }

        public static bool SideGate(bool sided, bool whenOff, bool ruleOn, out bool planOn)
        {
            if (!sided)
            {
                planOn = ruleOn;
                return true;
            }
            bool mine = ruleOn == !whenOff;
            planOn = mine;
            return mine;
        }

        public static LogicAction[] Split(LogicAction old, int kind)
        {
            LogicAction on = SidedCopy(old, old.Invert);
            LogicAction off = SidedCopy(old, !old.Invert);
            on.OnSet = old.OnSet; on.OnPose = old.OnPose; on.OnStation = old.OnStation;
            off.OnSet = old.OffSet; off.OnPose = old.OffPose; off.OnStation = old.OffStation;
            if (kind == KIND_PISTON && old.MergeAt != 0 && old.MergeAt != MERGE_NEVER)
            {
                on.MergeAt = (old.MergeAt & 1) != 0 ? 1 : MERGE_NEVER;
                off.MergeAt = (old.MergeAt & 2) != 0 ? 1 : MERGE_NEVER;
            }
            bool offNeeded;
            switch (kind)
            {
                case KIND_DOCK:
                    DockToggles.Effective(kind, old.OnSet, old.OnPose, true, out int a1, out int b1);
                    DockToggles.Effective(kind, old.OffSet, old.OffPose, false, out int a2, out int b2);
                    on.OnSet = true; on.OnPose = DockToggles.Code(a1, b1);
                    off.OnSet = true; off.OnPose = DockToggles.Code(a2, b2);
                    offNeeded = true;
                    break;
                case KIND_SWITCH:
                    on.OnSet = true; on.OnPose = 1;
                    off.OnSet = true; off.OnPose = 2;
                    offNeeded = true;
                    break;
                case KIND_CART:
                    offNeeded = off.OnPose > 0 || !off.OnStation.IsNone || off.Drive != 0 || off.MergeAt != 0 || off.Grip != 0;
                    break;
                case KIND_HINGE:
                    offNeeded = false;
                    break;
                case KIND_MOTOR:
                case KIND_PISTON:
                    offNeeded = off.OnSet || off.Speed > 0 || off.Accel > 0 || off.Strength > 0 || off.Grip != 0 || off.MergeAt != 0;
                    break;
                default:
                    offNeeded = true;
                    break;
            }
            return offNeeded ? new[] { on, off } : new[] { on };
        }

        private static LogicAction SidedCopy(LogicAction old, bool whenOff)
        {
            LogicAction a = old;
            a.Sided = true;
            a.WhenOff = whenOff;
            a.Invert = false;
            a.OffSet = false;
            a.OffPose = 0;
            a.OffStation = LogicRef.None;
            a.BlankLeaves = true;
            return a;
        }

        public static LogicAction UnpackFull(int t, int m, int p, int s1, int s2, int drive = 0)
        {
            LogicAction a = Unpack(t, m, p);
            if (!a.IsUsed) return a;
            a.OnStation = LogicRef.Unpack(s1);
            a.OffStation = LogicRef.Unpack(s2);
            a.Drive = drive;
            return a;
        }

        public static int KindOf(Part p)
        {
            if (p == null || p.IsDestroyed) return 0;
            return KindOfId(p.Rules.ID.ToString());
        }

        public static int KindOfId(string id)
        {
            switch (id)
            {
                case "ztx.hinges_pistons_docking.motor_base":
                case "ztx.hinges_pistons_docking.corridor_hinge_base": return KIND_MOTOR;
                case "ztx.hinges_pistons_docking.piston_base":
                case "ztx.hinges_pistons_docking.corridor_piston_base": return KIND_PISTON;
                case "ztx.hinges_pistons_docking.hinge_base": return KIND_HINGE;
                case "ztx.hinges_pistons_docking.rail_cart": return KIND_CART;
                case "ztx.hinges_pistons_docking.rail_switch": return KIND_SWITCH;
                case "ztx.hinges_pistons_docking.dock_clamp":
                case "ztx.hinges_pistons_docking.dock_pad":
                case "ztx.hinges_pistons_docking.bay_terminal": return KIND_BAY;
                case "ztx.hinges_pistons_docking.bay_station": return KIND_BAY_STATION;
                case "ztx.hinges_pistons_docking.logic_button": return KIND_BUTTON;
                case "ztx.hinges_pistons_docking.logic_adapter": return KIND_ADAPTER;
                case "ztx.hinges_pistons_docking.indicator_lamp": return KIND_LAMP;
                default: return 0;
            }
        }

        public static bool IsPositionedKind(int kind) => kind >= KIND_MOTOR && kind <= KIND_HINGE;
        public static bool IsPositioned(Part p) => IsPositionedKind(KindOf(p));

        public override string ToString()
        {
            return "act(" + Target + ", rule " + Rule + (Sided ? (WhenOff ? ", when OFF" : ", when ON") : "") + (Invert ? ", invert" : "") + (OnSet ? ", on " + OnPose : "") + (OffSet ? ", off " + OffPose : "")
                + (Speed > 0 ? ", speed " + Speed : "") + (Accel > 0 ? ", accel " + (Accel == 1 ? "instant" : "smooth") : "")
                + (Strength > 0 ? ", strength " + (Strength - 1) * 10 + "%" : "")
                + (MergeAt != 0 ? ", merge at " + MergeAt : "") + (!OnStation.IsNone ? ", on station " + OnStation : "")
                + (!OffStation.IsNone ? ", off station " + OffStation : "") + ")";
        }

        public static int SelfTest(out string first)
        {
            var fails = new List<string>();
            var a = new LogicAction { Rule = 15, Invert = true, Accel = 2, Strength = 11, OnSet = true, OffSet = true, OnPose = 4095, OffPose = 1234, Speed = 255,
                Target = new LogicRef { Kind = RefKind.Part, X = -7, Y = 300 } };
            LogicAction b = Unpack(a.Target.Pack(), a.PackMisc(), a.PackPose());
            Check(fails, b.Rule == 15 && b.Invert && b.Accel == 2 && b.Strength == 11 && b.OnSet && b.OffSet && b.OnPose == 4095 && b.OffPose == 1234
                && b.Speed == 255 && b.Target.Equals(a.Target), "full round trip");
            var mg = new LogicAction { Rule = 2, MergeAt = 3, Target = new LogicRef { Kind = RefKind.Part, X = 1, Y = 2 } };
            LogicAction mg2 = Unpack(mg.Target.Pack(), mg.PackMisc(), mg.PackPose());
            Check(fails, mg2.MergeAt == 3 && mg2.Rule == 2 && mg2.Target.Equals(mg.Target), "merge-at round trip");
            Check(fails, b.MergeAt == 0, "an old slot has no merge-at");
            var c = new LogicAction { Rule = 1 };
            LogicAction d = Unpack(0, c.PackMisc(), c.PackPose());
            Check(fails, d.IsUsed && !d.AnySet && d.Target.IsNone, "empty slot round trip");
            Check(fails, !Unpack(0, 0, 0).IsUsed, "zero is free");
            Check(fails, PackFree() == 0, "free packs to 0");
            Check(fails, float.IsNaN(d.Settings.OnPose) && float.IsNaN(d.Settings.OffPose), "unset positions are NaN");
            ActSettings s = ActSettings.None;
            s.FillFrom(new ActSettings { OnPose = 10f, OffPose = float.NaN, Speed = 5 });
            s.FillFrom(new ActSettings { OnPose = 20f, OffPose = 30f, Speed = 9, Strength = 4 });
            Check(fails, s.OnPose == 10f && s.OffPose == 30f && s.Speed == 5 && s.Strength == 4 && s.Accel == 0, "first push wins");
            var line = new LogicLine { Rule = 3, Op = LogicOp.And, Format = LogicLine.FORMAT, NotA = true, NotB = true,
                A = new LogicRef { Kind = RefKind.Part, X = -1024, Y = 1023 }, B = LogicRef.Local(7) };
            LogicLine back = LogicLine.Unpack(line.PackA(), line.PackB(), line.PackMisc());
            Check(fails, back.NotA && back.NotB && back.A.Equals(line.A) && back.B.Equals(line.B) && back.Op == LogicOp.And, "NOT round trip");
            line.NotA = false;
            line.NotB = false;
            back = LogicLine.Unpack(line.PackA(), line.PackB(), line.PackMisc());
            Check(fails, !back.NotA && !back.NotB && back.A.Equals(line.A), "no NOT round trip");
            first = fails.Count > 0 ? fails[0] : "";
            return fails.Count;
        }

        private static int PackFree() => new LogicAction().PackMisc();

        private static void Check(List<string> fails, bool ok, string what)
        {
            if (!ok) fails.Add(what);
        }
    }
}
