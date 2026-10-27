using System;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships.Parts;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxDockClamp")]
    internal class ZtxDockClampRules : PartComponentRules
    {
        [Serialize(Optional = true)]
        [PortableHash]
        public float ResupplyPerSecond = 4f;

        public override bool CanBeCrewSourceTarget => true;

        public override PartComponent CreateComponent()
        {
            return new ZtxDockClampComponent(this);
        }
    }

    internal class ZtxDockClampComponent : PartComponent
    {
        private static readonly ConditionalWeakTable<Part, ZtxDockClampComponent> s_byPart =
            new ConditionalWeakTable<Part, ZtxDockClampComponent>();

        public new ZtxDockClampRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxDockClampRules>(base.Rules); }
        }

        public ZtxDockClampComponent PairedClamp;

        public bool OwnsJoint;

        public string LastRejectReason;

        public float SupplyCarry;

        public string ServiceStatus = "";

        public readonly DockMergeState Merge = new DockMergeState();

        public ZtxDockClampComponent(ZtxDockClampRules rules) : base(rules) { }

        public static ZtxDockClampComponent ForPart(Part part)
        {
            if (part == null) return null;
            s_byPart.TryGetValue(part, out ZtxDockClampComponent clamp);
            return clamp;
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            try
            {
                s_byPart.AddOrUpdate(base.Part, this);
                DockShipManager.GetOrCreate(base.Ship)?.RegisterClamp(this);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxDockClampComponent.OnPartAttached", ex);
            }
        }

        public override void OnPartDetaching()
        {
            try
            {
                s_byPart.Remove(base.Part);
                bool suspended = false;
                if (OwnsJoint)
                    suspended = HingeJointManager.SuspendOrRelease(this, base.Part);
                else if (PairedClamp != null && PairedClamp.OwnsJoint)
                    suspended = HingeJointManager.SuspendOrRelease(PairedClamp, base.Part);
                if (!suspended)
                    ClearPair();
                DockShipManager.TryGet(base.Ship)?.DeregisterClamp(this);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxDockClampComponent.OnPartDetaching", ex);
            }
            base.OnPartDetaching();
        }

        public void ClearPair()
        {
            if (PairedClamp != null)
            {
                if (ReferenceEquals(PairedClamp.PairedClamp, this))
                {
                    PairedClamp.PairedClamp = null;
                    PairedClamp.OwnsJoint = false;
                    PairedClamp.Merge.Pending = false;
                    PairedClamp.Merge.MergedAwayIdentity = null;
                    PairedClamp.Merge.FromMergedAwayShip = false;
                }
                PairedClamp = null;
            }
            OwnsJoint = false;
            Merge.Pending = false;
            Merge.StayWelded = false;
            Merge.MergedAwayIdentity = null;
            Merge.FromMergedAwayShip = false;
        }
    }
}
