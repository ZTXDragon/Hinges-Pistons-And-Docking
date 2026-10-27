using System;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogicPartWatch
    {
        private static readonly ID<PartToggleGuiRules> s_weaponModeId = new ID<PartToggleGuiRules>("ztx_weapon_mode");

        public static string SizeText(int width, int length) => width + "x" + length;

        private static string Low(string s) => string.IsNullOrEmpty(s) ? "" : char.ToLowerInvariant(s[0]) + s.Substring(1);

        public static string Describe(Part p)
        {
            if (p == null || p.IsDestroyed || p.Ship == null) return "";
            try
            {
                switch (p.Rules.ID.ToString())
                {
                    case "ztx.hinges_pistons_docking.damage_sensor": return Area(p);
                    case "ztx.hinges_pistons_docking.proximity_sensor": return Proximity(p);
                    case "ztx.hinges_pistons_docking.ship_sensor": return ShipSensor(p);
                    case "ztx.hinges_pistons_docking.weapon_sensor": return Weapon(p);
                    case "ztx.hinges_pistons_docking.logic_adapter": return Adapter(p);
                    default: return "";
                }
            }
            catch (Exception ex)
            {
                Log.Exception("LogicPartWatch.Describe", ex);
                return "";
            }
        }

        private static string Area(Part p)
        {
            AreaSettings s = AreaSensorGeometry.Read(p);
            string measure = Strings.GetText(s.Measure == AreaSensorGeometry.MEASURE_PRESENT ? "ZtxAreaPanel/MeasurePresent"
                : s.Measure == AreaSensorGeometry.MEASURE_MISSING ? "ZtxAreaPanel/MeasureMissing"
                : s.Measure == AreaSensorGeometry.MEASURE_OTHER ? "ZtxAreaPanel/MeasureOther"
                : s.Measure == AreaSensorGeometry.MEASURE_AMMO ? "ZtxAreaPanel/MeasureAmmo" : "ZtxAreaPanel/MeasureHealth");
            string where = Strings.GetText(s.Position == AreaSensorGeometry.POSITION_CENTRED ? "ZtxAreaPanel/PosCentred" : "ZtxAreaPanel/PosFront");
            return Strings.FormatText("ZtxLogicPanel/WatchAreaFmt", measure, SizeText(s.Width, s.Length), Low(where));
        }

        private static string Proximity(Part p)
        {
            int relation = LogicSource.Mode(p, ZtxProximitySensorComponent.RelationId, 0);
            float range = TypedSettings.ProxRange(TypedSettings.Stored(p, TypedSettings.ProxRangeId), LogicSource.Mode(p, ZtxProximitySensorComponent.RangeId, 3),
                ZtxProximitySensorComponent.RangeTiles);
            int sector = LogicSource.Mode(p, ZtxProximitySensorComponent.SectorId, 0);
            int size = LogicSource.Mode(p, ZtxProximitySensorComponent.SizeId, 0);
            int measure = LogicSource.Mode(p, ZtxProximitySensorComponent.MeasureId, 0);
            return Strings.FormatText("ZtxLogicPanel/WatchProxFmt", Strings.GetText("ZtxProxPanel/Rel_" + relation),
                TypedSettings.Format(range), Low(Strings.GetText("ZtxProxPanel/Sector_" + sector)),
                ProxSizeText(p, size), Low(Strings.GetText("ZtxProxPanel/Measure_" + measure)));
        }

        private static string ProxSizeText(Part p, int size)
        {
            int storedMin = TypedSettings.Stored(p, TypedSettings.ProxSizeMinId), storedMax = TypedSettings.Stored(p, TypedSettings.ProxSizeMaxId);
            if (!TypedSettings.ProxSizeCustom(storedMin, storedMax, size)) return Low(Strings.GetText("ZtxProxPanel/Size_" + size));
            float min = TypedSettings.ProxSizeValue(storedMin), max = TypedSettings.ProxSizeValue(storedMax);
            if (min > 0f && max > 0f) return Strings.FormatText("ZtxLogicPanel/WatchSizeRangeFmt", TypedSettings.Format(min), TypedSettings.Format(max));
            if (min > 0f) return Strings.FormatText("ZtxLogicPanel/WatchSizeMinFmt", TypedSettings.Format(min));
            if (max > 0f) return Strings.FormatText("ZtxLogicPanel/WatchSizeMaxFmt", TypedSettings.Format(max));
            return Low(Strings.GetText("ZtxProxPanel/Size_0"));
        }

        private static string ShipSensor(Part p)
        {
            int measure = LogicSource.Mode(p, ZtxShipSensorComponent.MeasureId, 0);
            string text = Strings.GetText("ZtxShipPanel/Measure_" + measure);
            if (measure != ShipSensorMath.MEASURE_RESOURCE) return text;
            string resource = ZtxShipSensorComponent.ResourceOf(LogicSource.Mode(p, ZtxShipSensorComponent.ResourceId, 0))?.NameKey.Text ?? "?";
            return Strings.FormatText("ZtxLogicPanel/WatchShipResourceFmt", text, resource);
        }

        private static string Weapon(Part p)
        {
            int mode = LogicSource.Mode(p, s_weaponModeId, 0);
            string what = AdapterToggles.LabelOf(Strings.GetText("PartToggles/ZtxWeaponMode_" + mode));
            if (what.StartsWith("Mode: ", StringComparison.Ordinal)) what = what.Substring(6);
            Part weapon = p.Ship.Parts[p.Location + LogicSource.Facing(p), PartRectType.Physical];
            string name = weapon != null && !ReferenceEquals(weapon, p) ? weapon.Rules.NameKey.Text : Strings.GetText("ZtxLogicPanel/WatchNoWeapon");
            return Strings.FormatText("ZtxLogicPanel/WatchWeaponFmt", what, name);
        }

        private static string Adapter(Part p)
        {
            Part target = AdapterToggles.TargetOf(p);
            string name = target != null ? target.Rules.NameKey.Text : Strings.GetText("ZtxAdapterPanel/NoTarget");
            int read = LogicSource.Mode(p, ZtxLogicAdapterComponent.ReadId, AdapterRead.READ_OPERATIONAL);
            string what = Strings.GetText("ZtxAdapterPanel/Read_" + read);
            if (read == AdapterRead.READ_SETTING)
            {
                AdapterToggles.Setting s = AdapterToggles.Find(target, LogicSource.Mode(p, ZtxLogicAdapterComponent.ToggleId, 0));
                if (s != null) what = Strings.FormatText("ZtxLogicPanel/WatchAdapterSettingFmt", what, s.Name);
            }
            return Strings.FormatText("ZtxLogicPanel/WatchAdapterFmt", what, name);
        }
    }
}
