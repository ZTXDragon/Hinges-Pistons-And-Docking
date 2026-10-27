using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.Physics2D.Dynamics;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxSlickCollider")]
    internal class ZtxSlickColliderRules : PartComponentRules
    {
        [Serialize(Optional = true)]
        [PortableHash]
        public float Friction = 0f;

        public override PartComponent CreateComponent()
        {
            return new ZtxSlickColliderComponent(this);
        }
    }

    internal class ZtxSlickColliderComponent : PartComponent, ILogicTicker
    {
        private const int APPLY_INTERVAL_TICKS = 30;
        private int _tick;

        public new ZtxSlickColliderRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxSlickColliderRules>(base.Rules); }
        }

        public ZtxSlickColliderComponent(ZtxSlickColliderRules rules) : base(rules) { }

        public void Tick()
        {
            if (++_tick % APPLY_INTERVAL_TICKS != 1) return;
            Apply();
        }

        private void Apply()
        {
            Part part = base.Part;
            ShipPhysics phys = part?.Ship?.Physics;
            if (phys == null) return;
            IReadOnlyList<Fixture> fixtures = phys.GetCustomFixtures(part);
            if (fixtures == null) return;
            for (int i = 0; i < fixtures.Count; i++)
            {
                Fixture f = fixtures[i];
                if (f != null && f.Friction != Rules.Friction)
                    f.Friction = Rules.Friction;
            }
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            try
            {
                LogicShipManager.GetOrCreate(base.Ship)?.Register(this);
                Apply();
            }
            catch (System.Exception ex)
            {
                Log.Exception("ZtxSlickColliderComponent.OnPartAttached", ex);
            }
        }

        public override void OnPartDetaching()
        {
            try
            {
                LogicShipManager.TryGet(base.Ship)?.Deregister(this);
            }
            catch (System.Exception ex)
            {
                Log.Exception("ZtxSlickColliderComponent.OnPartDetaching", ex);
            }
            base.OnPartDetaching();
        }
    }
}
