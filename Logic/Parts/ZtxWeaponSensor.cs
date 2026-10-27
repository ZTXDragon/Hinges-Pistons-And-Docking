using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Logic;
using Cosmoteer.Ships.Parts.Resources;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace ZTX.HingesPistonsDocking
{
    [ReflectiveSerialization(Explicit = true)]
    [ReflectivePortableHashing(Explicit = true)]
    [SerialDerivedType(TypeName = "ZtxWeaponSensor")]
    internal class ZtxWeaponSensorRules : PartComponentRules
    {
        [Serialize(Optional = true)]
        [PortableHash]
        public string ComponentID = "ShellShouldOpen";

        public override PartComponent CreateComponent()
        {
            return new ZtxWeaponSensorComponent(this);
        }
    }

    internal class ZtxWeaponSensorComponent : ZtxLogicTickerComponent, ILogicValue
    {
        public const int MODE_READY = 0, MODE_FIRING = 1, MODE_OPERATIONAL = 2, MODE_AMMO = 3, MODE_CUSTOM = 4;

        private static readonly ID<PartToggleGuiRules> s_invertToggleId = new ID<PartToggleGuiRules>("ztx_logic_invert");
        private static readonly ID<PartToggleGuiRules> s_modeToggleId = new ID<PartToggleGuiRules>("ztx_weapon_mode");

        private static readonly string[][] s_candidates =
        {
            new[] { "ShellShouldOpen", "TargetingAndCanShoot", "FirePermission", "FirePermissionToggle", "IsReadyToFire", "CanFire" },
            new[] { "GunIsFiring", "BeamEmittersToggle", "BeamEmissionToggleLeft", "FireTimerProxy", "IsShellFullyOpen" },
            new[] { "IsOperational" },
            new[] { "HasMaxAmmo" },
            null,
        };

        private readonly Dictionary<string, ID<PartComponentRules>> _ids = new Dictionary<string, ID<PartComponentRules>>();
        private float? _percent;
        private float? _units;

        public new ZtxWeaponSensorRules Rules
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Unsafe.As<ZtxWeaponSensorRules>(base.Rules); }
        }

        public ZtxWeaponSensorComponent(ZtxWeaponSensorRules rules) : base(rules) { }

        public float? Percent => _percent;

        public float? Units => _units;

        private ID<PartComponentRules> IdOf(string name)
        {
            if (!_ids.TryGetValue(name, out ID<PartComponentRules> id))
            {
                id = new ID<PartComponentRules>(name);
                _ids[name] = id;
            }
            return id;
        }

        public override void Tick()
        {
            Part me = base.Part;
            Ship ship = base.Ship;
            if (me == null || ship == null)
            {
                _percent = null;
                _units = null;
                SetToggle(false);
                return;
            }
            IntVector2 cell = me.Location + LogicSource.Facing(me);
            Part target = ship.Parts[cell, PartRectType.Physical];
            bool on = false;
            string reject = null;
            if (target == null)
            {
                _percent = null;
                _units = null;
                reject = "no part at the arrow cell";
            }
            else
            {
                Storage(target, out _percent, out _units);
                int mode = LogicSource.Mode(me, s_modeToggleId, MODE_READY);
                if (mode == MODE_AMMO && !target.TryGetComponent<IPartComponentToggle>(IdOf("HasMaxAmmo"), out _))
                {
                    on = _percent.HasValue && _percent.Value >= 100f;
                    if (!_percent.HasValue) reject = $"part {target.Rules.ID} holds no ammo or charge";
                    LogRejectOnce(reject);
                    SetToggle(on != LogicSource.IsOn(me, s_invertToggleId));
                    return;
                }
                string[] names = mode == MODE_CUSTOM || mode < 0 || mode >= s_candidates.Length
                    ? new[] { string.IsNullOrEmpty(Rules.ComponentID) ? "ShellShouldOpen" : Rules.ComponentID }
                    : s_candidates[mode];
                bool found = false;
                foreach (string name in names)
                {
                    if (!target.TryGetComponent<IPartComponentToggle>(IdOf(name), out IPartComponentToggle toggle)) continue;
                    on = toggle.IsToggleOn;
                    found = true;
                    break;
                }
                if (!found) reject = $"part {target.Rules.ID} has no state for this mode (tried {string.Join(", ", names)})";
            }
            LogRejectOnce(reject);
            bool invert = LogicSource.IsOn(me, s_invertToggleId);
            SetToggle(on != invert);
        }

        private static readonly ID<PartComponentRules> s_totalStorageId = new ID<PartComponentRules>("TotalResourceStorage");

        internal static void Storage(Part target, out float? percent, out float? units)
        {
            percent = null;
            units = null;
            if (target.TryGetComponent<BaseResourceStorage>(s_totalStorageId, out BaseResourceStorage total) && total.MaxResources > 0)
            {
                percent = (float)total.Resources * 100f / total.MaxResources;
                units = total.Resources;
                return;
            }
            long have = 0, max = 0;
            foreach (PartComponent c in target.Components)
            {
                if (!(c is ResourceStorage rs) || rs.MaxResources <= 0) continue;
                have += rs.Resources;
                max += rs.MaxResources;
            }
            if (max <= 0) return;
            percent = (float)have * 100f / max;
            units = have;
        }
    }
}
