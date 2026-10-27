using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;
using HVec = Halfling.Geometry.Vector2;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxProximitySensor")]
    internal class ZtxProximitySensorRules : PartComponentRules
    {
        [Serialize(Optional = true)]
        [PortableHash]
        public int TickInterval = 5;

        public override PartComponent CreateComponent()
        {
            return new ZtxProximitySensorComponent(this);
        }
    }

    internal class ZtxProximitySensorComponent : ZtxLogicTickerComponent, ILogicValue
    {
        public static readonly float[] RangeTiles = { 5f, 10f, 15f, 20f, 30f, 40f, 50f, 75f, 100f, 150f };
        public const int RELATION_ENEMY = 0, RELATION_ALLY = 1, RELATION_NEUTRAL = 2, RELATION_JUNK = 3, RELATION_ANY = 4;
        private const int JUNK_PLAYER = -3;

        private static readonly ID<PartToggleGuiRules> s_invertId = new ID<PartToggleGuiRules>("ztx_logic_invert");
        private static readonly ID<PartToggleGuiRules> s_rangeId = new ID<PartToggleGuiRules>("ztx_prox_range");
        private static readonly ID<PartToggleGuiRules> s_relationId = new ID<PartToggleGuiRules>("ztx_prox_relation");
        public static readonly ID<PartToggleGuiRules> MeasureId = new ID<PartToggleGuiRules>("ztx_prox_measure");
        public static readonly ID<PartToggleGuiRules> SectorId = new ID<PartToggleGuiRules>("ztx_prox_sector");
        public static readonly ID<PartToggleGuiRules> SizeId = new ID<PartToggleGuiRules>("ztx_prox_size");
        public static readonly ID<PartToggleGuiRules> DockedId = new ID<PartToggleGuiRules>("ztx_prox_docked");
        public static ID<PartToggleGuiRules> RangeId => s_rangeId;
        public static ID<PartToggleGuiRules> RelationId => s_relationId;

        private readonly int _interval;
        private readonly List<Ship> _assembly = new List<Ship>();
        private readonly List<Ship> _joined = new List<Ship>();
        private int _tick;
        private bool _inRange;
        private float _percent = 100f;
        private float? _units;
        private Ship _nearest;

        public ZtxProximitySensorComponent(ZtxProximitySensorRules rules) : base(rules)
        {
            _interval = Math.Max(1, rules.TickInterval);
        }

        public float? Percent => _percent;
        public float? Units => _units;
        public Ship Nearest => _nearest;
        public float RangeWorld { get; private set; }
        public int InRangeCount { get; private set; }
        public float NearestBearing { get; private set; } = float.NaN;

        public override void OnPartAttached()
        {
            base.OnPartAttached();
            SensorAreaOverlay.Register(this);
        }

        public override void OnPartDetaching()
        {
            SensorAreaOverlay.Deregister(this);
            base.OnPartDetaching();
        }

        public override void Tick()
        {
            Part me = base.Part;
            Ship ship = base.Ship;
            if (me == null || ship == null || ship.Physics?.Body == null)
            {
                _inRange = false;
                _percent = 100f;
                _nearest = null;
                SetToggle(false);
                return;
            }
            SensorAreaOverlay.Attach(ship.Sim);
            if (_tick++ % _interval == 0) Scan(me, ship);
            bool invert = LogicSource.IsOn(me, s_invertId);
            SetToggle(_inRange != invert);
        }

        private void Scan(Part me, Ship ship)
        {
            int rangeMode = LogicSource.Mode(me, s_rangeId, 3);
            if (rangeMode < 0) rangeMode = 0;
            if (rangeMode >= RangeTiles.Length) rangeMode = RangeTiles.Length - 1;
            float scale = ship.Scale.X > 0f ? ship.Scale.X : 1f;
            float rangeWorld = TypedSettings.ProxRange(TypedSettings.Stored(me, TypedSettings.ProxRangeId), rangeMode, RangeTiles) * scale;
            RangeWorld = rangeWorld;
            int relation = LogicSource.Mode(me, s_relationId, RELATION_ENEMY);
            int measure = LogicSource.Mode(me, MeasureId, ProximityMath.MEASURE_DISTANCE);
            int sector = LogicSource.Mode(me, SectorId, ProximityMath.SECTOR_ALL);
            int size = LogicSource.Mode(me, SizeId, ProximityMath.SIZE_ANY);
            int sizeMin = TypedSettings.Stored(me, TypedSettings.ProxSizeMinId), sizeMax = TypedSettings.Stored(me, TypedSettings.ProxSizeMaxId);
            bool customSize = TypedSettings.ProxSizeCustom(sizeMin, sizeMax, size);
            float minTiles = TypedSettings.ProxSizeValue(sizeMin), maxTiles = TypedSettings.ProxSizeValue(sizeMax);
            bool countDocked = LogicSource.Mode(me, DockedId, ProximityMath.DOCKED_IGNORE) == ProximityMath.DOCKED_COUNT;

            IntRect r = me.Rect;
            float cx = r.X + r.Width * 0.5f, cy = r.Y + r.Height * 0.5f;
            HVec here = ship.Physics.Body.GetWorldPoint(new HVec(cx * scale, cy * scale));
            IntVector2 facing = DockGeometry.FacingDir(me);

            _assembly.Clear();
            HingeJointManager.GetAssemblyShips(ship, _assembly, identity: true);
            _joined.Clear();
            if (!countDocked) HingeJointManager.GetAssemblyShips(ship, _joined, identity: false);

            float best = float.MaxValue;
            float bestBearing = float.NaN;
            Ship bestShip = null;
            int count = 0;
            foreach (Ship other in ship.Sim.Ships)
            {
                if (other == null || ReferenceEquals(other, ship) || other.Physics?.Body == null) continue;
                if (_assembly.Contains(other) || _joined.Contains(other)) continue;
                if (!Matches(ship, other, relation)) continue;
                float across = 2f * other.WorldBoundingRadius / scale;
                if (customSize ? !ProximityMath.SizeInRange(across, minTiles, maxTiles) : !ProximityMath.SizeMatches(across, size)) continue;
                HVec c = other.Physics.Body.WorldCenter;
                HVec g = ship.Physics.Body.GetLocalPoint(c) / scale;
                float bearing = ProximityMath.Bearing(g.X - cx, g.Y - cy, facing.X, facing.Y);
                if (!ProximityMath.InSector(bearing, sector)) continue;
                float dx = c.X - here.X, dy = c.Y - here.Y;
                float d = (float)Math.Sqrt(dx * dx + dy * dy) - other.WorldBoundingRadius;
                if (d < 0f) d = 0f;
                if (d <= rangeWorld) count++;
                if (d < best) { best = d; bestShip = other; bestBearing = bearing; }
            }
            _nearest = bestShip;
            _inRange = bestShip != null && best <= rangeWorld;
            InRangeCount = count;
            NearestBearing = _inRange ? bestBearing : float.NaN;
            switch (measure)
            {
                case ProximityMath.MEASURE_COUNT:
                    _percent = Math.Min(100f, count);
                    _units = count;
                    break;
                case ProximityMath.MEASURE_BEARING:
                    _percent = _inRange ? bestBearing / 3.6f : float.NaN;
                    _units = _inRange ? bestBearing : float.NaN;
                    break;
                default:
                    _percent = bestShip == null || rangeWorld <= 0f ? 100f : Math.Min(100f, best / rangeWorld * 100f);
                    _units = bestShip == null ? RangeTiles[rangeMode] : best / scale;
                    break;
            }
        }

        private static bool Matches(Ship me, Ship other, int relation)
        {
            bool junk = other.Metadata != null && other.Metadata.PlayerIndex == JUNK_PLAYER;
            switch (relation)
            {
                case RELATION_ENEMY: return !junk && me.IsEnemiesWith(other);
                case RELATION_ALLY: return !junk && me.IsAlliesWith(other);
                case RELATION_NEUTRAL: return !junk && !me.IsEnemiesWith(other) && !me.IsAlliesWith(other);
                case RELATION_JUNK: return junk;
                default: return true;
            }
        }
    }
}
