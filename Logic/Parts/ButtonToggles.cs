using System;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    internal static class ButtonToggles
    {
        public static readonly ID<PartToggleGuiRules> StateId = new ID<PartToggleGuiRules>("ztx_logic_button");
        public static readonly ID<PartToggleGuiRules> ModeId = new ID<PartToggleGuiRules>("ztx_button_mode");
        public const int MODE_SWITCH = 0, MODE_PUSH = 1;
        public const int CELL_LEAVE = 0, CELL_ON = 1, CELL_OFF = 2;

        public static int? Target(bool set, int pose)
        {
            if (!set) return null;
            if (pose == CELL_ON) return 1;
            if (pose == CELL_OFF) return 0;
            return null;
        }

        public static bool ReleaseDue(int onTick, int now, int ticksPerSecond)
        {
            if (onTick < 0) return false;
            return now - onTick >= Math.Max(1, ticksPerSecond / 2);
        }

        public static string Write(Part part, int value)
        {
            if (part == null || part.IsDestroyed || part.Ship == null) return "";
            PartUIToggle t = PartUIToggle.GetOnPart(part, StateId);
            if (t == null || t.Mode == value) return "";
            t.Mode = value;
            t.Part.MatchingBlueprintPart?.OnPartUIToggleStatesChanged();
            return value != 0 ? "button on" : "button off";
        }

        public static string ApplyDo(Part part, LogicAction a, bool on)
        {
            int? v = a.Sided ? Target(a.OnSet, a.OnPose) : Target(on ? a.OnSet : a.OffSet, on ? a.OnPose : a.OffPose);
            return v.HasValue ? Write(part, v.Value) : "";
        }
    }

    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxButtonPush")]
    internal class ZtxButtonPushRules : PartComponentRules
    {
        public override PartComponent CreateComponent()
        {
            return new ZtxButtonPushComponent(this);
        }
    }

    internal class ZtxButtonPushComponent : ZtxLogicTickerComponent
    {
        private int _onTick = -1;

        public ZtxButtonPushComponent(ZtxButtonPushRules rules) : base(rules) { }

        public override void Tick()
        {
            Part me = base.Part;
            if (me == null || me.Ship == null) return;
            LogicShipManager manager = LogicShipManager.TryGet(me.Ship);
            int now = manager != null ? manager.TickCount : 0;
            bool push = LogicSource.Mode(me, ButtonToggles.ModeId, ButtonToggles.MODE_SWITCH) == ButtonToggles.MODE_PUSH;
            bool on = LogicSource.IsOn(me, ButtonToggles.StateId);
            if (!push || !on)
            {
                _onTick = -1;
                return;
            }
            if (_onTick < 0) _onTick = now;
            int tps = Math.Max(1, (int)Math.Round(1f / Math.Max(0.001f, LogicShipManager.TickSeconds)));
            if (!ButtonToggles.ReleaseDue(_onTick, now, tps)) return;
            ButtonToggles.Write(me, 0);
            _onTick = -1;
        }
    }
}
