using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Doors;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation.MediaEffects;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxBayStation")]
    internal class ZtxBayStationRules : PartComponentRules
    {
        [Serialize(Optional = true)]
        [PortableHash]
        public float WorkPerSecond = 2f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float DeliveryPerSecond = 4f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float ChargePerSecond = 1500f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float MaxBuildSpeed = 25f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float ReachCells = 15f;

        [Serialize(Optional = true)]
        public MultiMediaEffectRules BeamMediaEffects;

        [Serialize(Optional = true)]
        public MultiMediaEffectRules BeamHitMediaEffects;

        public override bool CanBeCrewSourceTarget => true;

        public override PartComponent CreateComponent()
        {
            return new ZtxBayStationComponent(this);
        }
    }

    internal class ZtxBayStationComponent : PartComponent
    {
        private static readonly ConditionalWeakTable<Part, ZtxBayStationComponent> s_byPart = new ConditionalWeakTable<Part, ZtxBayStationComponent>();

        public readonly DockBuildState Beam = new DockBuildState();
        public PartInfo? Target;
        public ResourceStock Stock;
        public int StockTick = int.MinValue;
        public float DeliveryCarry;
        public float ChargeCarry;
        public int SupplySet = DockSupply.ALL;
        public int ScanCooldown;
        public bool SpeedPaused;
        public DockRepairState Repair;
        public Ship SupplyShip;
        public Cosmoteer.Ships.Resources.IResourceSink SupplySink;
        public string Status = "idle";

        public new ZtxBayStationRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxBayStationRules>(base.Rules); }
        }

        public ZtxBayStationComponent(ZtxBayStationRules rules) : base(rules) { }

        public static ZtxBayStationComponent ForPart(Part part)
        {
            if (part == null) return null;
            s_byPart.TryGetValue(part, out ZtxBayStationComponent c);
            return c;
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            try
            {
                s_byPart.AddOrUpdate(base.Part, this);
                BayManager.GetOrCreate(base.Ship)?.RegisterStation(this);
                SensorAreaOverlay.Register(this);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxBayStationComponent.OnPartAttached", ex);
            }
        }

        public override void OnPartDetaching()
        {
            try
            {
                s_byPart.Remove(base.Part);
                BayManager.TryGet(base.Ship)?.DeregisterStation(this);
                SensorAreaOverlay.Deregister(this);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxBayStationComponent.OnPartDetaching", ex);
            }
            base.OnPartDetaching();
        }
    }

    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxBayTerminal")]
    internal class ZtxBayTerminalRules : PartComponentRules
    {
        static ZtxBayTerminalRules() => BayZoneOverlay.Register();

        [Serialize(Optional = true)]
        [PortableHash]
        public float PushSpeed = 5f;

        [Serialize(Optional = true)]
        [PortableHash]
        public float FlyClearanceCells = 3f;

        public override PartComponent CreateComponent()
        {
            return new ZtxBayTerminalComponent(this);
        }
    }

    internal sealed class BayState
    {
        public bool InCharge;
        public HashSet<IntVector2> StructureCells = new HashSet<IntVector2>();
        public List<ZtxBayStationComponent> Stations = new List<ZtxBayStationComponent>();
        public readonly List<PartInfo> Design = new List<PartInfo>();
        public readonly List<PartInfo> BlueprintDesign = new List<PartInfo>();
        public readonly List<DoorInfo> BlueprintDoors = new List<DoorInfo>();
        public readonly Dictionary<PartInfo, List<BayDesignCodec.ToggleRec>> StoredToggles = new Dictionary<PartInfo, List<BayDesignCodec.ToggleRec>>();
        public int StoredGen = int.MinValue;
        public readonly List<DoorInfo> Doors = new List<DoorInfo>();
        public readonly HashSet<IntVector2> DesignCells = new HashSet<IntVector2>();
        public IntRect DesignBounds;
        public int Pieces;
        public int Done;
        public int OutOfReach;
        public bool Built;
        public bool Complete;
        public int BlueprintedDone = -1;
        public int DoorsDone;
        public int BlueprintedDoors = -1;
        public Ship BlueprintedShip;
        public bool Repaired;
        public bool Crewed = true;
        public bool Supplied = true;
        public bool Charged = true;
        public Ship Held;
        public Ship LastReleased;
        public string Waiting = "";
        public string Missing;
        public string CrewNote = "";
        public string LastLogged;
    }

    internal class ZtxBayTerminalComponent : PartComponent
    {
        private static readonly ConditionalWeakTable<Part, ZtxBayTerminalComponent> s_byPart = new ConditionalWeakTable<Part, ZtxBayTerminalComponent>();

        public readonly BayState Bay = new BayState();

        public new ZtxBayTerminalRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxBayTerminalRules>(base.Rules); }
        }

        public ZtxBayTerminalComponent(ZtxBayTerminalRules rules) : base(rules) { }

        public static ZtxBayTerminalComponent ForPart(Part part)
        {
            if (part == null) return null;
            s_byPart.TryGetValue(part, out ZtxBayTerminalComponent c);
            return c;
        }

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            try
            {
                s_byPart.AddOrUpdate(base.Part, this);
                BayManager.GetOrCreate(base.Ship)?.RegisterTerminal(this);
                SensorAreaOverlay.Register(this);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxBayTerminalComponent.OnPartAttached", ex);
            }
        }

        public override void OnPartDetaching()
        {
            try
            {
                s_byPart.Remove(base.Part);
                BayManager.TryGet(base.Ship)?.OnTerminalLost(this);
                SensorAreaOverlay.Deregister(this);
            }
            catch (Exception ex)
            {
                Log.Exception("ZtxBayTerminalComponent.OnPartDetaching", ex);
            }
            base.OnPartDetaching();
        }
    }
}
