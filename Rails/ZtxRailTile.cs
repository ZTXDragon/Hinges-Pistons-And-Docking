using System;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships.Parts;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxRailTile")]
    internal class ZtxRailTileRules : PartComponentRules
    {
        [Serialize(Optional = true)]
        [PortableHash]
        public bool IsStop = false;

        [Serialize(Optional = true)]
        [PortableHash]
        public bool IsSwitch = false;

        [Serialize(Optional = true)]
        [PortableHash]
        public bool IsCurve = false;

        [Serialize(Optional = true)]
        [PortableHash]
        public int CurveMinDistance = 2;

        [Serialize(Optional = true)]
        [PortableHash]
        public int CurveMaxDistance = 6;

        public override PartComponent CreateComponent()
        {
            return new ZtxRailTileComponent(this);
        }
    }

    internal class ZtxRailTileComponent : PartComponent
    {
        private static readonly ConditionalWeakTable<Part, ZtxRailTileComponent> s_byPart =
            new ConditionalWeakTable<Part, ZtxRailTileComponent>();

        public new ZtxRailTileRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxRailTileRules>(base.Rules); }
        }

        public bool IsStop
        {
            get { return Rules.IsStop; }
        }

        public bool IsSwitch
        {
            get { return Rules.IsSwitch; }
        }

        public bool IsCurve
        {
            get { return Rules.IsCurve; }
        }

        public ZtxRailTileComponent(ZtxRailTileRules rules) : base(rules) { }

        public static ZtxRailTileComponent ForPart(Part part)
        {
            if (part == null) return null;
            s_byPart.TryGetValue(part, out ZtxRailTileComponent tile);
            return tile;
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            try
            {
                s_byPart.AddOrUpdate(base.Part, this);
                if (Rules.IsCurve) RailCurves.Track(base.Part, true);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxRailTileComponent.OnPartAttached", ex);
            }
        }

        public override void OnPartDetaching()
        {
            try
            {
                if (Rules.IsCurve) RailCurves.Track(base.Part, false);
                s_byPart.Remove(base.Part);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxRailTileComponent.OnPartDetaching", ex);
            }
            base.OnPartDetaching();
        }
    }
}
