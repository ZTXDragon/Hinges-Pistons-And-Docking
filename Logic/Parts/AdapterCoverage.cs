using System;
using System.Collections.Generic;
using System.Globalization;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Weapons;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class AdapterCoverage
    {
        public static readonly int RangeHash = AdapterToggles.Hash("coverage:range");
        public static readonly int AimHash = AdapterToggles.Hash("coverage:aim");
        public static readonly int WidthHash = AdapterToggles.Hash("coverage:width");
        public const int FREE = 1000;
        public const int WHICH_AIM = 0, WHICH_WIDTH = 1;
        public const int FULL_RANGE = 100;

        public static readonly int[] RangeChoices = { FULL_RANGE, 90, 80, 70, 60, 50, 40, 30, 20, 10 };
        public static readonly int[] AimChoices = { FREE, 0, 15, -15, 30, -30, 45, -45, 60, -60, 90, -90, 120, -120, 150, -150, 180 };
        public static readonly int[] WidthChoices = { FREE, 0, 15, 30, 45, 60, 90, 120, 180, 270 };

        public static bool IsCoverageHash(int hash) => hash == RangeHash || hash == AimHash || hash == WidthHash;

        public static (bool, float, float) Compose(int which, int value, bool hasArc, float centerDeg, float widthDeg)
        {
            if (value == FREE) return (false, 0f, 0f);
            if (which == WHICH_AIM) return (true, value, hasArc ? widthDeg : 0f);
            return (true, hasArc ? centerDeg : 0f, value);
        }

        public static float? RangeValue(int percent, float physicalRange)
        {
            if (percent >= FULL_RANGE || percent == FREE || percent <= 0) return null;
            return physicalRange * percent / 100f;
        }

        public static List<AdapterToggles.Setting> SettingsOf(Part target)
        {
            var list = new List<AdapterToggles.Setting>();
            if (target == null) return list;
            bool range = false, arc = false;
            foreach (PartComponent c in target.Components)
            {
                if (!(c is Weapon w) || !w.Rules.CanEditCustomCoverage) continue;
                if (w.Rules.AllowCustomCoverageRangeEditing) range = true;
                if (w is TurretWeapon tw && tw.Rules.AllowCustomCoverageArcEditing) arc = true;
            }
            if (range) list.Add(Make(RangeHash, "ZtxAdapterPanel/CoverageRange", RangeChoices, RangeText));
            if (arc)
            {
                list.Add(Make(AimHash, "ZtxAdapterPanel/CoverageAim", AimChoices, AimText));
                list.Add(Make(WidthHash, "ZtxAdapterPanel/CoverageWidth", WidthChoices, WidthText));
            }
            return list;
        }

        private static AdapterToggles.Setting Make(int hash, string nameKey, int[] choices, Func<int, string> text)
        {
            var s = new AdapterToggles.Setting { Hash = hash, Id = nameKey, Name = Strings.GetText(nameKey), IsCoverage = true };
            foreach (int v in choices) s.Choices.Add((v, text(v)));
            return s;
        }

        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);

        private static string RangeText(int v) => v >= FULL_RANGE ? Strings.GetText("ZtxAdapterPanel/RangeFull") : Strings.FormatText("ZtxAdapterPanel/RangePctFmt", N(v));

        private static string AimText(int v)
        {
            if (v == FREE) return Strings.GetText("ZtxAdapterPanel/AimFree");
            if (v == 0) return Strings.GetText("ZtxAdapterPanel/AimAhead");
            if (Math.Abs(v) == 180) return Strings.GetText("ZtxAdapterPanel/AimBehind");
            return Strings.FormatText(v > 0 ? "ZtxAdapterPanel/AimRightFmt" : "ZtxAdapterPanel/AimLeftFmt", N(Math.Abs(v)));
        }

        private static string WidthText(int v)
        {
            if (v == FREE) return Strings.GetText("ZtxAdapterPanel/WidthFull");
            if (v == 0) return Strings.GetText("ZtxAdapterPanel/WidthFixed");
            return Strings.FormatText("ZtxAdapterPanel/WidthFmt", N(v));
        }

        public static string Apply(Part target, int hash, int value)
        {
            Ship ship = target?.Ship;
            if (ship == null || target.IsDestroyed) return "";
            string changed = "";
            foreach (PartComponent c in target.Components)
            {
                if (!(c is Weapon w) || !w.Rules.CanEditCustomCoverage) continue;
                WeaponCoverage cur = w.GetCustomWeaponCoverage(MPValueType.Confirmed, allowMatch: false) ?? default(WeaponCoverage);
                WeaponCoverage next;
                if (hash == RangeHash)
                {
                    if (!w.Rules.AllowCustomCoverageRangeEditing) continue;
                    next = new WeaponCoverage(RangeValue(value, w.GetEffectiveRange(WeaponCoverageMode.Physical)), cur.Arc, cur.RestPosition);
                }
                else
                {
                    if (!(w is TurretWeapon tw) || !tw.Rules.AllowCustomCoverageArcEditing) continue;
                    bool has = cur.Arc.HasValue;
                    float centre = has ? cur.Arc.Value.Center.ToDegrees() : 0f;
                    float width = has ? cur.Arc.Value.Width.ToDegrees() : 0f;
                    var (keep, newCentre, newWidth) = Compose(hash == AimHash ? WHICH_AIM : WHICH_WIDTH, value, has, centre, width);
                    if (tw.Rules.ForceCustomCoverageWidthZero) newWidth = 0f;
                    Arc? arc = keep ? Arc.CenteredOn(Direction.FromDegrees(newCentre), Angle.FromDegrees(newWidth)) : (Arc?)null;
                    next = new WeaponCoverage(cur.Range, arc, keep ? cur.RestPosition : null);
                }
                if (next == cur) continue;
                if (next != default(WeaponCoverage) && !w.CanApplyCustomCoverage(next))
                {
                    LogThrottle.Info("AdapterCoverage|" + target.Location + "|" + hash + "|" + value,
                        "Logic adapter: " + target.Rules.ID + " at " + target.Location + " refuses that range or arc; nothing written.");
                    continue;
                }
                var key = (new PartSaveData(target), w.Rules.ID);
                if (next == default(WeaponCoverage)) ship.Metadata.CustomWeaponCoverages.Remove(key);
                else ship.Metadata.CustomWeaponCoverages[key] = new MPValue<WeaponCoverage>(next);
                changed += (changed.Length > 0 ? ", " : "") + "coverage of " + w.Rules.ID + " on " + target.Rules.ID + " at " + target.Location;
            }
            return changed;
        }
    }
}
