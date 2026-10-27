using System;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships.Parts;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxHingeBase")]
    internal class ZtxHingeBaseRules : PartComponentRules
    {

        public override PartComponent CreateComponent()
        {
            return new ZtxHingeBaseComponent(this);
        }
    }

    internal class ZtxHingeBaseComponent : PartComponent
    {
        public new ZtxHingeBaseRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxHingeBaseRules>(base.Rules); }
        }

        public bool Serviced;

        public ZtxHingeTipComponent PairedTip;

        public string LastRejectReason;

        public bool Returning;

        public bool PowerCarried;

        public ZtxHingeBaseComponent(ZtxHingeBaseRules rules) : base(rules) { }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            try
            {
                HingeShipManager.GetOrCreate(base.Ship)?.Register(this);
                ThrusterFireDiag.Watch(base.Ship);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxHingeBaseComponent.OnPartAttached", ex);
            }
        }

        public override void OnPartDetaching()
        {
            try
            {
                if (!HingeJointManager.SuspendOrRelease(this, base.Part))
                    ClearPair();
                HingeShipManager.TryGet(base.Ship)?.Deregister(this);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxHingeBaseComponent.OnPartDetaching", ex);
            }
            base.OnPartDetaching();
        }

        public void ClearPair()
        {
            if (PairedTip != null)
            {
                if (ReferenceEquals(PairedTip.PairedBase, this))
                    PairedTip.PairedBase = null;
                PairedTip = null;
            }
            Serviced = false;
        }
    }
}
