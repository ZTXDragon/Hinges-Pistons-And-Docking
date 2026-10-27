using System;
using System.Collections.Generic;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Statuses;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxShipSensor")]
    internal class ZtxShipSensorRules : PartComponentRules
    {
        [Serialize(Optional = true)]
        [PortableHash]
        public int TickInterval = 5;

        public override PartComponent CreateComponent()
        {
            return new ZtxShipSensorComponent(this);
        }
    }

    internal class ZtxShipSensorComponent : ZtxLogicTickerComponent, ILogicValue
    {
        public static readonly ID<PartToggleGuiRules> MeasureId = new ID<PartToggleGuiRules>("ztx_ship_measure");
        public static readonly ID<PartToggleGuiRules> ResourceId = new ID<PartToggleGuiRules>("ztx_ship_resource");
        private static readonly ID<ResourceRules> s_battery = new ID<ResourceRules>("battery");
        private static readonly ID<ResourceRules> s_heat = new ID<ResourceRules>("heat");
        private static readonly ID<StatusType> s_heatStatus = new ID<StatusType>("cosmoteer.heat");

        private readonly int _interval;
        private readonly List<Ship> _assembly = new List<Ship>();
        private int _tick;
        private float? _percent;
        private float? _units;
        private bool _on;
        private int _lastMeasure = -1;
        private float _prevCurrent = float.NaN, _prevDesign = float.NaN;
        private int _lastHitTick = -1;

        public ZtxShipSensorComponent(ZtxShipSensorRules rules) : base(rules)
        {
            _interval = Math.Max(1, rules.TickInterval);
        }

        public float? Percent => _percent;
        public float? Units => _units;

        public static int ResourceHash(string id) => (int)(MergeRecord.Hash(id ?? "") & 0x7FFFFFFF);

        public static ResourceRules ResourceOf(int hash)
        {
            if (hash == 0 || GameApp.Rules?.Resources == null) return null;
            foreach (ResourceRules r in GameApp.Rules.Resources)
                if (r != null && ResourceHash(r.ID.ToString()) == hash) return r;
            return null;
        }

        public override void Tick()
        {
            Part me = base.Part;
            Ship ship = base.Ship;
            if (me == null || ship == null || ship.Physics?.Body == null)
            {
                _percent = null;
                _units = null;
                SetToggle(false);
                return;
            }
            if (_tick++ % _interval == 0) Measure(me, ship);
            SetToggle(_on);
        }

        private void Measure(Part me, Ship ship)
        {
            int measure = LogicSource.Mode(me, MeasureId, ShipSensorMath.MEASURE_HULL);
            if (measure != _lastMeasure)
            {
                _lastMeasure = measure;
                _prevCurrent = _prevDesign = float.NaN;
                _lastHitTick = -1;
            }
            _assembly.Clear();
            HingeJointManager.GetAssemblyShips(ship, _assembly, identity: true);
            float scale = ship.Scale.X > 0f ? ship.Scale.X : 1f;
            string why = null;
            switch (measure)
            {
                case ShipSensorMath.MEASURE_SPEED:
                {
                    Vector2 v = ship.Physics.Body.LinearVelocity;
                    float speed = (float)Math.Sqrt(v.X * v.X + v.Y * v.Y) / scale;
                    Set(speed > ShipSensorMath.MOVING_TILES_PER_SECOND, Math.Min(100f, speed), speed);
                    break;
                }
                case ShipSensorMath.MEASURE_HULL:
                {
                    Hull(out float current, out float design, out int hurt);
                    Set(hurt > 0, ShipSensorMath.Percent(current, design), hurt);
                    break;
                }
                case ShipSensorMath.MEASURE_POWER:
                    Stock(s_battery);
                    break;
                case ShipSensorMath.MEASURE_CREW:
                {
                    int crew = 0, capacity = 0;
                    foreach (Ship s in _assembly)
                    {
                        crew += s.Crew.Assigned.Count;
                        capacity += s.Crew.TotalCrewCapacity;
                    }
                    Set(crew > 0, ShipSensorMath.Percent(crew, capacity), crew);
                    break;
                }
                case ShipSensorMath.MEASURE_RESOURCE:
                {
                    ResourceRules r = ResourceOf(LogicSource.Mode(me, ResourceId, 0));
                    if (r == null)
                    {
                        why = "no resource chosen";
                        Set(false, null, null);
                    }
                    else Stock(r.ID);
                    break;
                }
                case ShipSensorMath.MEASURE_HEAT_STORED:
                    Stock(s_heat);
                    break;
                case ShipSensorMath.MEASURE_HOTTEST:
                {
                    float hottest = Hottest();
                    Set(hottest > 0f, ShipSensorMath.HeatPercent(hottest), hottest);
                    break;
                }
                case ShipSensorMath.MEASURE_DAMAGE:
                {
                    Hull(out float current, out float design, out _);
                    int now = LogicShipManager.TryGet(ship)?.TickCount ?? 0;
                    if (!float.IsNaN(_prevCurrent) && ShipSensorMath.Damaged(current, _prevCurrent, design, _prevDesign)) _lastHitTick = now;
                    _prevCurrent = current;
                    _prevDesign = design;
                    int tps = Math.Max(1, (int)Math.Round(1f / Math.Max(0.001f, LogicShipManager.TickSeconds)));
                    float since = ShipSensorMath.SinceSeconds(_lastHitTick, now, tps);
                    Set(since < ShipSensorMath.DAMAGE_WINDOW_SECONDS, Math.Min(100f, since), since);
                    break;
                }
                default:
                    why = "unknown measure " + measure;
                    Set(false, null, null);
                    break;
            }
            LogRejectOnce(why);
        }

        private void Set(bool on, float? percent, float? units)
        {
            _on = on;
            _percent = percent;
            _units = units;
        }

        private void Stock(ID<ResourceRules> type)
        {
            float stored = 0f, capacity = 0f;
            foreach (Ship s in _assembly)
            {
                stored += s.Resources.GetResourceTotal(type, includeStored: true, includeCarried: false);
                capacity += s.Resources.GetCapacityTotal(type, includeImplicit: true, includeUntyped: false);
            }
            Set(stored > 0f, ShipSensorMath.Percent(stored, capacity), stored);
        }

        private void Hull(out float current, out float design, out int hurt)
        {
            current = 0f;
            design = 0f;
            hurt = 0;
            foreach (Ship s in _assembly)
            {
                foreach (Part p in s.Parts)
                {
                    current += p.Health;
                    design += p.Rules.MaxHealth;
                    if (p.Health < p.Rules.MaxHealth) hurt++;
                }
                foreach (BlueprintPart bp in s.BlueprintParts.UnmatchedParts)
                {
                    design += bp.Rules.MaxHealth;
                    hurt++;
                }
            }
        }

        private float Hottest()
        {
            if (GameApp.Rules?.Statuses == null || !GameApp.Rules.Statuses.TryGetStatusType(s_heatStatus, out StatusType heat)) return 0f;
            float max = 0f;
            foreach (Ship s in _assembly)
            {
                TileStatusHandler handler = s.Statuses.GetTileHandler(heat);
                if (handler == null) continue;
                foreach (var (location, info) in handler.GetActiveStatusLocations())
                    if (info != null && info.CombinedValue > max) max = info.CombinedValue;
            }
            return max;
        }
    }
}
