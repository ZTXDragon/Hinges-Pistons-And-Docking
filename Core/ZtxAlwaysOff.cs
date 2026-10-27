using System;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Logic;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxAlwaysOff")]
    internal class ZtxAlwaysOffRules : PartComponentRules
    {
        public override PartComponent CreateComponent()
        {
            return new ZtxAlwaysOffComponent(this);
        }
    }

    internal class ZtxAlwaysOffComponent : PartComponent, IPartComponentToggle
    {
        public bool IsToggleOn => false;
        public bool IsToggleOnAnticipated => false;

        public event EventHandler<EventArgs> ToggleChanged { add { } remove { } }

        public ZtxAlwaysOffComponent(ZtxAlwaysOffRules rules) : base(rules) { }
    }
}
