using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxSignal")]
    internal class ZtxSignalRules : PartComponentRules
    {
        public override PartComponent CreateComponent()
        {
            return new ZtxSignalComponent(this);
        }
    }

    internal class ZtxSignalComponent : PartComponent
    {
        private int _stampTick = int.MinValue;
        private bool _on;
        private int _writers;
        private ActSettings _onSet = ActSettings.None;
        private ActSettings _anySet = ActSettings.None;

        public ZtxSignalComponent(ZtxSignalRules rules) : base(rules) { }

        public static int Clock(Ship ship) => ship?.Sim?.Tick ?? 0;

        public void Push(int tick, bool on, in ActSettings s)
        {
            if (tick != _stampTick)
            {
                _stampTick = tick;
                _on = on;
                _writers = 1;
                _onSet = ActSettings.None;
                _anySet = ActSettings.None;
            }
            else
            {
                _on |= on;
                _writers++;
            }
            _anySet.FillFrom(s);
            if (on) _onSet.FillFrom(s);
        }

        public ActSettings Settings(int nowTick)
        {
            bool? cur = Current(nowTick);
            if (cur == null) return ActSettings.None;
            return cur.Value ? _onSet : _anySet;
        }

        public bool? Current(int nowTick)
        {
            if (_stampTick == int.MinValue || nowTick - _stampTick > 1 || nowTick < _stampTick) return null;
            return _on;
        }

        public int Writers(int nowTick)
        {
            return Current(nowTick) == null ? 0 : _writers;
        }
    }
}
