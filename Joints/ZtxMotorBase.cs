using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxMotorBase")]
    internal class ZtxMotorBaseRules : ZtxHingeBaseRules
    {
        [Serialize(Optional = true)]
        [PortableHash]
        public float[] SpeedModesDeg = { 10f, 20f, 35f, 50f, 70f, 100f, 140f, 200f };

        [Serialize(Optional = true)]
        [PortableHash]
        public float AccelTime = 0.4f;

        public override Cosmoteer.Ships.Parts.PartComponent CreateComponent()
        {
            return new ZtxMotorBaseComponent(this);
        }
    }

    internal class ZtxMotorBaseComponent : ZtxHingeBaseComponent
    {
        public ZtxMotorBaseComponent(ZtxMotorBaseRules rules) : base(rules) { }
    }
}
