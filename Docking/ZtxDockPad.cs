using System;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxDockPad")]
    internal class ZtxDockPadRules : PartComponentRules
    {
        public override PartComponent CreateComponent()
        {
            return new ZtxDockPadComponent(this);
        }
    }

    internal class ZtxDockPadComponent : PartComponent
    {
        public new ZtxDockPadRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxDockPadRules>(base.Rules); }
        }

        public Ship DockedShip;
        public Cosmoteer.Game.ObjectID? LastRefusedShip;

        public ZtxDockPadComponent(ZtxDockPadRules rules) : base(rules) { }

        public static ZtxDockPadComponent ForPart(Part part)
        {
            if (part == null) return null;
            foreach (PartComponent c in part.Components)
                if (c is ZtxDockPadComponent pad) return pad;
            return null;
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            try
            {
                DockShipManager.GetOrCreate(base.Ship)?.RegisterPad(this);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxDockPadComponent.OnPartAttached", ex);
            }
        }

        public override void OnPartDetaching()
        {
            try
            {
                HingeJointManager.ReleaseByTag(this);
                DockedShip = null;
                DockShipManager.TryGet(base.Ship)?.DeregisterPad(this);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxDockPadComponent.OnPartDetaching", ex);
            }
            base.OnPartDetaching();
        }
    }
}
