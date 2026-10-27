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
    [SerialDerivedType(TypeName = "ZtxRailTerminal")]
    internal class ZtxRailTerminalRules : PartComponentRules
    {
        [Serialize(Optional = true)]
        [PortableHash]
        public float RailSpeed = 30f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float CartSpacing = 2f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float BlockedError = 0.5f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float RailClearance = 0.02f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float LaneTolerance = 0.05f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float AccelCap = 4f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float RatedLoad = 20f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float BrakeFactor = 1.5f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float EmergencyFactor = 3f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float GripFactor = 1f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float MaxTipSpeed = 4f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float MinReach = 0.5f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float CornerMinSpeed = 0.3f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float CornerLead = 1f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float FacingBlendCells = 1f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float CutOffMargin = 0.5f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float CornerSampleStep = 0.1f;

        [Serialize(Optional = true)]
        [PortableHash]
        public bool MotorDrive = true;

        [Serialize(Optional = true)]
        [PortableHash]
        public float MotorMargin = 1.5f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float MinThrustAccel = 0.1f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float RollingResistance = 0.25f;

        public override PartComponent CreateComponent()
        {
            return new ZtxRailTerminalComponent(this);
        }
    }

    internal class ZtxRailTerminalComponent : PartComponent
    {
        private static readonly ConditionalWeakTable<Part, ZtxRailTerminalComponent> s_byPart =
            new ConditionalWeakTable<Part, ZtxRailTerminalComponent>();

        public new ZtxRailTerminalRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxRailTerminalRules>(base.Rules); }
        }

        public readonly RailNetwork Network = new RailNetwork();

        public readonly List<ZtxRailCartComponent> Carts = new List<ZtxRailCartComponent>();

        public string LastRejectReason;

        public ZtxRailTerminalComponent(ZtxRailTerminalRules rules) : base(rules) { }

        public static ZtxRailTerminalComponent ForPart(Part part)
        {
            if (part == null) return null;
            s_byPart.TryGetValue(part, out ZtxRailTerminalComponent terminal);
            return terminal;
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            try
            {
                s_byPart.AddOrUpdate(base.Part, this);
                RailShipManager.GetOrCreate(base.Ship)?.RegisterTerminal(this);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxRailTerminalComponent.OnPartAttached", ex);
            }
        }

        public override void OnPartDetaching()
        {
            try
            {
                s_byPart.Remove(base.Part);
                foreach (ZtxRailCartComponent cart in Carts.ToArray())
                {
                    if (!HingeJointManager.SuspendOrRelease(cart, base.Part))
                        cart.ClearPair();
                }
                RailShipManager.TryGet(base.Ship)?.DeregisterTerminal(this);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxRailTerminalComponent.OnPartDetaching", ex);
            }
            base.OnPartDetaching();
        }
    }
}
