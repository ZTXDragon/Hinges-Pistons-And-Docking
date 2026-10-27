using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Logic;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogicSource
    {
        private static readonly ID<PartRules> s_pistonPartId = new ID<PartRules>("ztx.hinges_pistons_docking.piston_base");
        private static readonly ID<PartRules> s_corridorPistonPartId = new ID<PartRules>("ztx.hinges_pistons_docking.corridor_piston_base");
        private static readonly ID<PartRules> s_motorPartId = new ID<PartRules>("ztx.hinges_pistons_docking.motor_base");
        private static readonly ID<PartRules> s_hingePartId = new ID<PartRules>("ztx.hinges_pistons_docking.hinge_base");
        private static readonly ID<PartRules> s_corridorHingePartId = new ID<PartRules>("ztx.hinges_pistons_docking.corridor_hinge_base");
        private static readonly ID<PartRules> s_cartPartId = new ID<PartRules>("ztx.hinges_pistons_docking.rail_cart");
        private static readonly ID<PartRules> s_switchPartId = new ID<PartRules>("ztx.hinges_pistons_docking.rail_switch");
        private static readonly ID<PartRules> s_clampPartId = new ID<PartRules>("ztx.hinges_pistons_docking.dock_clamp");
        private static readonly ID<PartRules> s_padPartId = new ID<PartRules>("ztx.hinges_pistons_docking.dock_pad");

        public static bool IsPiston(Part part) => part?.Rules != null && part.Rules.ID.Equals(s_pistonPartId);

        public static bool IsSettingsDriven(Part part)
        {
            if (part?.Rules == null) return false;
            ID<PartRules> id = part.Rules.ID;
            return id.Equals(s_pistonPartId) || id.Equals(s_corridorPistonPartId) || id.Equals(s_motorPartId) || id.Equals(s_hingePartId) || id.Equals(s_corridorHingePartId)
                || id.Equals(s_cartPartId) || id.Equals(s_switchPartId)
                || id.Equals(s_clampPartId) || id.Equals(s_padPartId);
        }

        public static bool? Pushed(Part actuator)
        {
            if (actuator == null || actuator.Ship == null) return null;
            ZtxSignalComponent signal = LogicNet.SignalOf(actuator);
            if (signal == null) return null;
            return signal.Current(ZtxSignalComponent.Clock(actuator.Ship));
        }

        private static readonly ID<PartComponentRules>[] s_portIds =
        {
            new ID<PartComponentRules>("Port_Up"), new ID<PartComponentRules>("Port_Right"),
            new ID<PartComponentRules>("Port_Down"), new ID<PartComponentRules>("Port_Left"),
        };

        public static bool IsWired(Part part)
        {
            if (part == null) return false;
            foreach (ID<PartComponentRules> id in s_portIds)
            {
                if (part.TryGetComponent<IPartComponentToggle>(id, out IPartComponentToggle t) && t != null && t.IsToggleOn) return true;
            }
            return false;
        }

        public static ActSettings Settings(Part actuator)
        {
            if (actuator == null || actuator.Ship == null || IsSettingsDriven(actuator)) return ActSettings.None;
            ZtxSignalComponent signal = LogicNet.SignalOf(actuator);
            if (signal == null) return ActSettings.None;
            return signal.Settings(ZtxSignalComponent.Clock(actuator.Ship));
        }

        public static int Speed(Part actuator) => Settings(actuator).Speed;

        public static int StrengthMode(Part actuator, int popoutMode)
        {
            int s = Settings(actuator).Strength;
            return s > 0 ? s - 1 : popoutMode;
        }

        public static bool SmoothAccel(Part actuator, bool popoutSmooth)
        {
            int a = Settings(actuator).Accel;
            return a == 2 || (a == 0 && popoutSmooth);
        }

        public static float Commanded(Part part, bool? signal, ID<PartToggleGuiRules> manualId, ID<PartToggleGuiRules> onId,
            ID<PartToggleGuiRules> offId, int fallbackMode, float modeUnit, float poseUnit)
        {
            if (signal != null)
            {
                ActSettings s = Settings(part);
                float pose = signal.Value ? s.OnPose : s.OffPose;
                if (!float.IsNaN(pose)) return pose * poseUnit;
            }
            int mode = signal == null
                ? Mode(part, IsWired(part) ? offId : manualId, fallbackMode)
                : Mode(part, signal.Value ? onId : offId, fallbackMode);
            return mode * modeUnit;
        }

        public static bool? Read(Part actuator)
        {
            if (actuator == null || actuator.Ship == null || IsSettingsDriven(actuator)) return null;
            ZtxSignalComponent signal = LogicNet.SignalOf(actuator);
            if (signal == null) return null;
            return signal.Current(ZtxSignalComponent.Clock(actuator.Ship));
        }

        public static int MergeAt(Part actuator) => Settings(actuator).MergeAt;

        public static int Mode(Part part, ID<PartToggleGuiRules> toggleId, int fallback)
        {
            if (part == null) return fallback;
            return PartUIToggle.GetOnPart(part, toggleId)?.Mode ?? fallback;
        }

        public static bool IsOn(Part part, ID<PartToggleGuiRules> toggleId)
        {
            if (part == null) return false;
            PartUIToggle t = PartUIToggle.GetOnPart(part, toggleId);
            return t != null && t.IsToggleOn;
        }

        public static IntVector2 Facing(Part p)
        {
            return Facing(p.Rotation, p.FlipX);
        }

        public static IntVector2 Facing(int rotation, bool flipX)
        {
            return OrthogonalRotation.Up.GetShipRelative(rotation, flipX).ToVector();
        }
    }
}
