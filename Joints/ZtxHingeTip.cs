using System;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships.Parts;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxHingeTip")]
    internal class ZtxHingeTipRules : PartComponentRules
    {
        public override PartComponent CreateComponent()
        {
            return new ZtxHingeTipComponent(this);
        }
    }

    internal class ZtxHingeTipComponent : PartComponent
    {
        private static readonly ConditionalWeakTable<Part, ZtxHingeTipComponent> s_byPart =
            new ConditionalWeakTable<Part, ZtxHingeTipComponent>();

        public new ZtxHingeTipRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxHingeTipRules>(base.Rules); }
        }

        public ZtxHingeBaseComponent PairedBase;

        public ZtxHingeTipComponent(ZtxHingeTipRules rules) : base(rules) { }

        public static ZtxHingeTipComponent ForPart(Part part)
        {
            if (part == null) return null;
            s_byPart.TryGetValue(part, out ZtxHingeTipComponent tip);
            return tip;
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            try
            {
                s_byPart.AddOrUpdate(base.Part, this);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxHingeTipComponent.OnPartAttached", ex);
            }
        }

        public override void OnPartDetaching()
        {
            try
            {
                s_byPart.Remove(base.Part);
                if (PairedBase != null)
                {
                    ZtxHingeBaseComponent pairedBase = PairedBase;
                    if (!HingeJointManager.SuspendOrRelease(pairedBase, base.Part))
                        pairedBase.ClearPair();
                }
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxHingeTipComponent.OnPartDetaching", ex);
            }
            base.OnPartDetaching();
        }
    }
}
