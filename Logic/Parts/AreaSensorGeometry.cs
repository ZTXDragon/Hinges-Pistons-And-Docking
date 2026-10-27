using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal struct AreaSettings
    {
        public int Measure;
        public int Width;
        public int Length;
        public int Position;

        public static readonly AreaSettings Default = new AreaSettings
        {
            Measure = AreaSensorGeometry.MEASURE_HEALTH,
            Width = 5,
            Length = 5,
            Position = AreaSensorGeometry.POSITION_FRONT,
        };
    }

    internal static class AreaSensorGeometry
    {
        public const int MEASURE_HEALTH = 0, MEASURE_PRESENT = 1, MEASURE_MISSING = 2, MEASURE_OTHER = 3, MEASURE_AMMO = 4;
        public const int OLD_LINE = 0;
        public const int POSITION_FRONT = 0, POSITION_CENTRED = 1;
        public const int MAX_SIZE = 15;

        public static readonly ID<PartToggleGuiRules> MeasureId = new ID<PartToggleGuiRules>("ztx_area_measure");
        public static readonly ID<PartToggleGuiRules> OldShapeId = new ID<PartToggleGuiRules>("ztx_area_shape");
        public static readonly ID<PartToggleGuiRules> WidthId = new ID<PartToggleGuiRules>("ztx_area_width");
        public static readonly ID<PartToggleGuiRules> LengthId = new ID<PartToggleGuiRules>("ztx_area_length");
        public static readonly ID<PartToggleGuiRules> PositionId = new ID<PartToggleGuiRules>("ztx_area_position");

        public static AreaSettings Read(Part p)
        {
            AreaSettings d = AreaSettings.Default;
            return Make(LogicSource.Mode(p, MeasureId, d.Measure),
                LogicSource.Mode(p, WidthId, d.Width - 1), LogicSource.Mode(p, LengthId, d.Length - 1),
                LogicSource.Mode(p, PositionId, d.Position));
        }

        public static AreaSettings Read(Dictionary<ID<PartToggleGuiRules>, int> toggles)
        {
            AreaSettings d = AreaSettings.Default;
            return Make(Get(toggles, MeasureId, d.Measure),
                Get(toggles, WidthId, d.Width - 1), Get(toggles, LengthId, d.Length - 1),
                Get(toggles, PositionId, d.Position));
        }

        private static int Get(Dictionary<ID<PartToggleGuiRules>, int> toggles, ID<PartToggleGuiRules> id, int fallback)
        {
            return toggles != null && toggles.TryGetValue(id, out int v) ? v : fallback;
        }

        public static int CarriedWidthMode(int? oldShape, int widthMode) => oldShape == OLD_LINE ? 0 : widthMode;

        public static void CarryOverShape(Part p)
        {
            ShipMetadata meta = p?.Ship?.Metadata;
            if (meta == null) return;
            var psd = new PartSaveData(p);
            int? old = meta.PartUIToggleStates.Get((psd, OldShapeId));
            if (!old.HasValue) return;
            PartUIToggle width = PartUIToggle.GetOnPart(p, WidthId);
            if (width != null)
            {
                int carried = CarriedWidthMode(old, width.Mode);
                if (carried != width.Mode) width.Mode = carried;
            }
            meta.PartUIToggleStates.Set((psd, OldShapeId), null);
            Log.Info($"area sensor at {p.Location}: old shape {old.Value} carried into its width.");
        }

        private static AreaSettings Make(int measure, int widthMode, int lengthMode, int position)
        {
            return new AreaSettings
            {
                Measure = Clamp(measure, MEASURE_HEALTH, MEASURE_AMMO),
                Width = Clamp(widthMode + 1, 1, MAX_SIZE),
                Length = Clamp(lengthMode + 1, 1, MAX_SIZE),
                Position = Clamp(position, POSITION_FRONT, POSITION_CENTRED),
            };
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        public static void Cells(IntVector2 location, IntVector2 f, AreaSettings s, List<IntVector2> into)
        {
            into.Clear();
            var side = new IntVector2(-f.Y, f.X);
            int w = s.Width;
            int wMin = -((w - 1) / 2), wMax = w / 2;
            int dMin, dMax;
            if (s.Position == POSITION_CENTRED)
            {
                dMin = -((s.Length - 1) / 2);
                dMax = s.Length / 2;
            }
            else
            {
                dMin = 1;
                dMax = s.Length;
            }
            for (int d = dMin; d <= dMax; d++)
            {
                for (int a = wMin; a <= wMax; a++)
                {
                    if (d == 0 && a == 0) continue;
                    into.Add(new IntVector2(location.X + f.X * d + side.X * a, location.Y + f.Y * d + side.Y * a));
                }
            }
        }

        public static bool Bounds(List<IntVector2> cells, out IntVector2 min, out IntVector2 max)
        {
            min = max = default;
            if (cells.Count == 0) return false;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
            foreach (IntVector2 c in cells)
            {
                if (c.X < x0) x0 = c.X;
                if (c.X > x1) x1 = c.X;
                if (c.Y < y0) y0 = c.Y;
                if (c.Y > y1) y1 = c.Y;
            }
            min = new IntVector2(x0, y0);
            max = new IntVector2(x1, y1);
            return true;
        }
    }
}
