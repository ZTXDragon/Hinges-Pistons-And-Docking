using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships.Parts;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxPistonBase")]
    internal class ZtxPistonBaseRules : ZtxHingeBaseRules
    {
        [Serialize(Optional = true)]
        [PortableHash]
        public float MaxExtension = 6f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float ExtendSpeed = 1.5f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float[] SpeedModes = { 0.25f, 0.5f, 1f, 1.5f, 2f, 3f, 4f, 6f };

        [Serialize(Optional = true)]
        [PortableHash]
        public float AccelTime = 0.4f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float LaneTolerance = 0.05f;

        [Serialize(Optional = true)]
        [PortableHash]
        public string RodPart = "ztx.hinges_pistons_docking.piston_rod";

        public override PartComponent CreateComponent()
        {
            return new ZtxPistonBaseComponent(this);
        }
    }

    internal class ZtxPistonBaseComponent : ZtxHingeBaseComponent
    {
        public new ZtxPistonBaseRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxPistonBaseRules>(base.Rules); }
        }

        public readonly List<Part> Rods = new List<Part>();

        public bool RodBroken;

        public ZtxPistonBaseComponent(ZtxPistonBaseRules rules) : base(rules) { }

        public override void OnPartDetaching()
        {
            try
            {
                if (!HingeJointManager.IsMigrating(base.Part) && Rods.Count > 0)
                {
                    HingeShipManager manager = HingeShipManager.GetOrCreate(base.Ship);
                    foreach (Part rod in Rods)
                    {
                        ZtxPistonRodComponent rc = ZtxPistonRodComponent.ForPart(rod);
                        if (rc != null) rc.Owner = null;
                        manager?.NoteOrphanRod(rod);
                    }
                    Rods.Clear();
                }
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxPistonBaseComponent.OnPartDetaching", ex);
            }
            base.OnPartDetaching();
        }
    }
}
