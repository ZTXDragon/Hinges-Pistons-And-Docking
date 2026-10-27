using System;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships.Parts;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxPistonRod")]
    internal class ZtxPistonRodRules : PartComponentRules
    {
        public override PartComponent CreateComponent()
        {
            return new ZtxPistonRodComponent(this);
        }
    }

    internal class ZtxPistonRodComponent : PartComponent
    {
        private static readonly ConditionalWeakTable<Part, ZtxPistonRodComponent> s_byPart =
            new ConditionalWeakTable<Part, ZtxPistonRodComponent>();

        public static bool Removing;

        public ZtxPistonBaseComponent Owner;

        public ZtxPistonRodComponent(ZtxPistonRodRules rules) : base(rules) { }

        public static ZtxPistonRodComponent ForPart(Part part)
        {
            if (part == null) return null;
            s_byPart.TryGetValue(part, out ZtxPistonRodComponent rod);
            return rod;
        }

        private Cosmoteer.Ships.Ship _ship;

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            try
            {
                _ship = base.Part.Ship;
                s_byPart.AddOrUpdate(base.Part, this);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxPistonRodComponent.OnPartAttached", ex);
            }
        }

        public override void OnPartDetaching()
        {
            try
            {
                s_byPart.Remove(base.Part);
                if (!Removing && !HingeJointManager.IsMigrating(base.Part))
                {
                    bool mergedHull = Owner != null && Owner.Part?.Ship != null && MergeService.IsMerged(Owner.Part);
                    Cosmoteer.Ships.Ship from = base.Part.Ship ?? _ship;
                    if (!mergedHull && Owner == null && from != null)
                        mergedHull = MergeRebuild.MergedRodCells(from).Contains(base.Part.Location);
                    if (Owner != null)
                    {
                        Owner.Rods.Remove(base.Part);
                        if (!mergedHull) Owner.RodBroken = true;
                    }
                    if (!mergedHull)
                        HingeShipManager.QueueShotRod(base.Part.Ship ?? _ship, base.Part.Location);
                }
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxPistonRodComponent.OnPartDetaching", ex);
            }
            base.OnPartDetaching();
        }
    }
}
