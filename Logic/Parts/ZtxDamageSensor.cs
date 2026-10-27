using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Resources;
using Cosmoteer.Ships.Repairs;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;
using HVec = Halfling.Geometry.Vector2;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxDamageSensor")]
    internal class ZtxDamageSensorRules : PartComponentRules
    {
        [Serialize(Optional = true)]
        [PortableHash]
        public int TickInterval = 5;

        [Serialize(Optional = true)]
        public string[] AmmoResourceTypes = { "bullet", "battery", "missile_part_he", "missile_part_emp", "missile_part_nuke", "mine_part" };

        public override PartComponent CreateComponent()
        {
            return new ZtxDamageSensorComponent(this);
        }
    }

    internal class ZtxDamageSensorComponent : ZtxLogicTickerComponent, ILogicValue
    {
        private static readonly ID<PartToggleGuiRules> s_invertId = new ID<PartToggleGuiRules>("ztx_logic_invert");

        private readonly int _interval;
        private readonly List<IntVector2> _cells = new List<IntVector2>();
        private readonly HashSet<IntVector2> _cellSet = new HashSet<IntVector2>();
        private readonly HashSet<IntVector2> _covered = new HashSet<IntVector2>();
        private readonly List<Ship> _assembly = new List<Ship>();
        private readonly HashSet<Part> _seen = new HashSet<Part>();
        private readonly HashSet<BlueprintPart> _designed = new HashSet<BlueprintPart>();
        private readonly HashSet<DestroyedPart> _destroyed = new HashSet<DestroyedPart>();
        private int _tick;
        private float _percent = 100f;
        private float _units;
        private bool _on;
        private int _otherParts;
        private readonly HashSet<string> _ammoTypes;
        private float _otherPercent;

        public ZtxDamageSensorComponent(ZtxDamageSensorRules rules) : base(rules)
        {
            _interval = Math.Max(1, rules.TickInterval);
            _ammoTypes = new HashSet<string>(rules.AmmoResourceTypes ?? new string[0]);
        }

        public float? Percent => _percent;
        public float? Units => _units;

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

        private bool _shapeCarried;

        public override void Tick()
        {
            Part me = base.Part;
            Ship ship = base.Ship;
            if (me == null || ship == null)
            {
                SetToggle(false);
                return;
            }
            if (!_shapeCarried)
            {
                _shapeCarried = true;
                AreaSensorGeometry.CarryOverShape(me);
            }
            SensorAreaOverlay.Attach(ship.Sim);
            AreaSettings s = AreaSensorGeometry.Read(me);
            AreaSensorGeometry.Cells(me.Location, LogicSource.Facing(me), s, _cells);
            switch (s.Measure)
            {
                case AreaSensorGeometry.MEASURE_PRESENT:
                    MeasurePresent(ship, me);
                    break;
                case AreaSensorGeometry.MEASURE_MISSING:
                    MeasureMissing(ship);
                    break;
                case AreaSensorGeometry.MEASURE_AMMO:
                    MeasureAmmo(ship, me);
                    break;
                case AreaSensorGeometry.MEASURE_OTHER:
                    if (_tick++ % _interval == 0) ScanOther(ship);
                    _percent = _otherPercent;
                    _units = _otherParts;
                    _on = _otherParts > 0;
                    LogRejectOnce(null);
                    break;
                default:
                    MeasureHealth(ship, me);
                    break;
            }
            bool invert = LogicSource.IsOn(me, s_invertId);
            SetToggle(_on != invert);
        }

        private void MeasureHealth(Ship ship, Part me)
        {
            long health = 0, max = 0;
            int damaged = 0;
            _seen.Clear();
            foreach (IntVector2 c in _cells)
            {
                Part p = ship.Parts[c, PartRectType.Physical];
                if (p == null || ReferenceEquals(p, me) || !_seen.Add(p)) continue;
                health += p.Health;
                max += p.Rules.MaxHealth;
                if (p.Health < p.Rules.MaxHealth) damaged++;
            }
            _destroyed.Clear();
            foreach (IntVector2 c in _cells)
            {
                DestroyedPart dp = ship.DestroyedParts[c, PartRectType.Normal];
                if (dp == null || !_destroyed.Add(dp)) continue;
                if (ship.Parts.GetMatchingPart(dp) != null) continue;
                max += dp.Rules.MaxHealth;
                damaged++;
            }
            _percent = max <= 0 ? 100f : (float)health * 100f / max;
            _units = damaged;
            _on = max > 0 && health < max;
            LogRejectOnce(max <= 0 ? "no parts in the area" : null);
        }

        private void MeasureAmmo(Ship ship, Part me)
        {
            long have = 0, max = 0;
            _seen.Clear();
            foreach (IntVector2 c in _cells)
            {
                Part p = ship.Parts[c, PartRectType.Physical];
                if (p == null || ReferenceEquals(p, me) || !_seen.Add(p)) continue;
                foreach (PartComponent comp in p.Components)
                {
                    if (!(comp is ResourceStorage rs) || rs.MaxResources <= 0) continue;
                    if (!_ammoTypes.Contains(rs.ResourceType.ToString())) continue;
                    have += rs.Resources;
                    max += rs.MaxResources;
                }
            }
            _percent = max <= 0 ? 0f : (float)have * 100f / max;
            _units = have;
            _on = max > 0 && have >= max;
            LogRejectOnce(max <= 0 ? "no ammo or charge stores in the area" : null);
        }

        private void MeasurePresent(Ship ship, Part me)
        {
            _seen.Clear();
            int occupied = 0;
            foreach (IntVector2 c in _cells)
            {
                Part p = ship.Parts[c, PartRectType.Physical];
                if (p == null || ReferenceEquals(p, me)) continue;
                occupied++;
                _seen.Add(p);
            }
            _percent = _cells.Count == 0 ? 0f : occupied * 100f / _cells.Count;
            _units = _seen.Count;
            _on = _seen.Count > 0;
            LogRejectOnce(_seen.Count == 0 ? "no parts in the area" : null);
        }

        private void MeasureMissing(Ship ship)
        {
            _designed.Clear();
            int missing = 0;
            IReadOnlySet<BlueprintPart> unmatched = ship.BlueprintParts.UnmatchedParts;
            foreach (IntVector2 c in _cells)
            {
                BlueprintPart bp = ship.BlueprintParts[c, PartRectType.Normal];
                if (bp == null || !_designed.Add(bp)) continue;
                if (unmatched.Contains(bp)) missing++;
            }
            _percent = _designed.Count == 0 ? 0f : missing * 100f / _designed.Count;
            _units = missing;
            _on = missing > 0;
            LogRejectOnce(_designed.Count == 0 ? "no designed parts in the area" : null);
        }

        private void ScanOther(Ship ship)
        {
            _cellSet.Clear();
            _covered.Clear();
            _otherParts = 0;
            _otherPercent = 0f;
            foreach (IntVector2 c in _cells) _cellSet.Add(c);
            if (_cellSet.Count == 0 || ship.Physics?.Body == null || ship.Sim == null) return;
            if (!AreaSensorGeometry.Bounds(_cells, out IntVector2 min, out IntVector2 max)) return;
            float scale = ship.Scale.X > 0f ? ship.Scale.X : 1f;
            float w = max.X - min.X + 1, h = max.Y - min.Y + 1;
            var localCentre = new HVec((min.X + max.X + 1) * 0.5f * scale, (min.Y + max.Y + 1) * 0.5f * scale);
            float radius = (float)Math.Sqrt(w * w + h * h) * 0.5f * scale + scale;
            HVec centre = ship.Physics.Body.GetWorldPoint(localCentre);
            _assembly.Clear();
            HingeJointManager.GetAssemblyShips(ship, _assembly, identity: true);
            foreach (Ship other in ship.Sim.Ships)
            {
                if (other == null || ReferenceEquals(other, ship) || other.Physics?.Body == null) continue;
                if (_assembly.Contains(other)) continue;
                HVec oc = other.DetWorldCenter;
                float dx = oc.X - centre.X, dy = oc.Y - centre.Y;
                float reach = radius + other.WorldBoundingRadius;
                if (dx * dx + dy * dy > reach * reach) continue;
                float os = other.Scale.X > 0f ? other.Scale.X : 1f;
                foreach (Part p in other.Parts)
                {
                    if (p == null || p.IsDestroyed) continue;
                    IntRect r = p.Rect;
                    bool inside = false;
                    for (int y = r.Top; y < r.Bottom; y++)
                    {
                        for (int x = r.Left; x < r.Right; x++)
                        {
                            HVec world = other.Physics.Body.GetWorldPoint(new HVec((x + 0.5f) * os, (y + 0.5f) * os));
                            HVec local = ship.Physics.Body.GetLocalPoint(world);
                            var cell = new IntVector2((int)Math.Floor(local.X / scale), (int)Math.Floor(local.Y / scale));
                            if (_cellSet.Contains(cell))
                            {
                                inside = true;
                                _covered.Add(cell);
                            }
                        }
                    }
                    if (inside) _otherParts++;
                }
            }
            _otherPercent = _covered.Count * 100f / _cellSet.Count;
        }
    }
}
