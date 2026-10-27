using System;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Logic;
using Cosmoteer.Ships.Parts.UI;
using Cosmoteer.Ships.Statuses;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxLogicAdapter")]
    internal class ZtxLogicAdapterRules : PartComponentRules
    {
        [Serialize(Optional = true)]
        [PortableHash]
        public int TickInterval = 5;

        public override PartComponent CreateComponent()
        {
            return new ZtxLogicAdapterComponent(this);
        }
    }

    internal class ZtxLogicAdapterComponent : ZtxLogicTickerComponent, ILogicValue
    {
        public static readonly ID<PartToggleGuiRules> ReadId = new ID<PartToggleGuiRules>("ztx_adapter_read");
        public static readonly ID<PartToggleGuiRules> ToggleId = new ID<PartToggleGuiRules>("ztx_adapter_toggle");
        private static readonly ID<PartComponentRules> s_operationalId = new ID<PartComponentRules>("IsOperational");
        private static readonly ID<StatusType> s_heatStatus = new ID<StatusType>("cosmoteer.heat");

        private readonly int _interval;
        private int _tick;
        private float? _percent;
        private float? _units;
        private bool _on;

        public ZtxLogicAdapterComponent(ZtxLogicAdapterRules rules) : base(rules)
        {
            _interval = Math.Max(1, rules.TickInterval);
        }

        public float? Percent => _percent;
        public float? Units => _units;

        public override void Tick()
        {
            Part me = base.Part;
            if (me == null || me.Ship == null)
            {
                Set(false, null, null);
                SetToggle(false);
                return;
            }
            if (_tick++ % _interval == 0) Read(me);
            SetToggle(_on);
        }

        private void Set(bool on, float? percent, float? units)
        {
            _on = on;
            _percent = percent;
            _units = units;
        }

        private void Read(Part me)
        {
            Part target = AdapterToggles.TargetOf(me);
            if (target == null)
            {
                Set(false, null, null);
                LogRejectOnce("no part at the arrow");
                return;
            }
            string why = null;
            switch (LogicSource.Mode(me, ReadId, AdapterRead.READ_OPERATIONAL))
            {
                case AdapterRead.READ_SETTING:
                {
                    int hash = LogicSource.Mode(me, ToggleId, 0);
                    PartUIToggle toggle = null;
                    foreach (PartComponent c in target.Components)
                        if (c is PartUIToggle t && AdapterToggles.Hash(t.Rules.ToggleID.ToString()) == hash) { toggle = t; break; }
                    if (toggle == null)
                    {
                        why = hash == 0 ? "no setting chosen in its window" : target.Rules.ID + " has no such setting";
                        Set(false, null, null);
                        break;
                    }
                    AdapterToggles.Setting s = AdapterToggles.Find(target, hash);
                    int index = 0, count = s?.Choices.Count ?? 0;
                    for (int i = 0; i < count; i++) if (s.Choices[i].Mode == toggle.Mode) index = i;
                    Set(toggle.IsToggleOn, AdapterRead.SettingPercent(index, count), toggle.Mode);
                    break;
                }
                case AdapterRead.READ_STORAGE:
                {
                    ZtxWeaponSensorComponent.Storage(target, out float? percent, out float? units);
                    if (!percent.HasValue) why = target.Rules.ID + " stores nothing";
                    Set(percent.HasValue && percent.Value >= 100f, percent, units);
                    break;
                }
                case AdapterRead.READ_HEAT:
                {
                    float hottest = Hottest(target);
                    Set(hottest > 0f, ShipSensorMath.HeatPercent(hottest), hottest);
                    break;
                }
                default:
                {
                    bool on = !target.TryGetComponent<IPartComponentToggle>(s_operationalId, out IPartComponentToggle op) || op.IsToggleOn;
                    Set(on, on ? 100f : 0f, on ? 1f : 0f);
                    break;
                }
            }
            LogRejectOnce(why);
        }

        private static float Hottest(Part target)
        {
            Ship ship = target.Ship;
            if (ship == null || GameApp.Rules?.Statuses == null || !GameApp.Rules.Statuses.TryGetStatusType(s_heatStatus, out StatusType heat)) return 0f;
            float max = 0f;
            IntRect r = target.Rect;
            for (int x = r.X; x < r.X + r.Width; x++)
                for (int y = r.Y; y < r.Y + r.Height; y++)
                    if (ship.Statuses.TryGetLocationInfo(heat, new IntVector2(x, y), out IStatusLocationInfo info) && info.CombinedValue > max)
                        max = info.CombinedValue;
            return max;
        }
    }
}
