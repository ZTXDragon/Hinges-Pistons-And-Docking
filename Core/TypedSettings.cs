using System;
using System.Globalization;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;

namespace ZTX.HingesPistonsDocking
{
    internal static class TypedSettings
    {
        public const int PISTON_SPEED_SCALE = 100, MOTOR_SPEED_SCALE = 10, LIMIT_SCALE = 10;
        public const float PISTON_SPEED_MIN = 0.1f, PISTON_SPEED_MAX = 10f;
        public const float MOTOR_SPEED_MIN = 1f, MOTOR_SPEED_MAX = 360f;
        public const float LIMIT_MIN = 0f, LIMIT_MAX = 90f, LIMIT_STEP = 10f;
        public const int LIMIT_MAX_MODE = 9;

        public static readonly ID<PartToggleGuiRules> PistonSpeedId = new ID<PartToggleGuiRules>("ztx_piston_speed_v");
        public static readonly ID<PartToggleGuiRules> MotorSpeedId = new ID<PartToggleGuiRules>("ztx_motor_speed_v");
        public static readonly ID<PartToggleGuiRules> LeftLimitId = new ID<PartToggleGuiRules>("ztx_hinge_left_v");
        public static readonly ID<PartToggleGuiRules> RightLimitId = new ID<PartToggleGuiRules>("ztx_hinge_right_v");
        public const float PROX_RANGE_MIN = 1f, PROX_RANGE_MAX = 500f, PROX_SIZE_MAX = 500f;
        public static readonly ID<PartToggleGuiRules> ProxRangeId = new ID<PartToggleGuiRules>("ztx_prox_range_v");
        public static readonly ID<PartToggleGuiRules> ProxSizeMinId = new ID<PartToggleGuiRules>("ztx_prox_size_min_v");
        public static readonly ID<PartToggleGuiRules> ProxSizeMaxId = new ID<PartToggleGuiRules>("ztx_prox_size_max_v");

        public static int NearestIndex(float[] table, float value)
        {
            int best = 0;
            float bestDiff = float.MaxValue;
            for (int i = 0; i < table.Length; i++)
            {
                float d = Math.Abs(table[i] - value);
                if (d < bestDiff) { bestDiff = d; best = i; }
            }
            return best;
        }

        public static float ProxRange(int stored, int mode, float[] table)
        {
            if (table == null || table.Length == 0) return 0f;
            int m = Math.Max(0, Math.Min(table.Length - 1, mode));
            if (stored <= 0) return table[m];
            float typed = Decode(stored, 1);
            return NearestIndex(table, typed) == m ? typed : table[m];
        }

        public static bool ProxSizeCustom(int storedMin, int storedMax, int mode) => mode == ProximityMath.SIZE_ANY && (storedMin > 0 || storedMax > 0);

        public static float ProxSizeValue(int stored) => stored > 0 ? Decode(stored, 1) : 0f;

        public static int Encode(float value, int scale, float min, float max)
        {
            float v = Math.Max(min, Math.Min(max, value));
            return (int)Math.Round(v * scale, MidpointRounding.AwayFromZero) + 1;
        }

        public static float Decode(int stored, int scale) => (stored - 1) / (float)scale;

        public static float SpeedEffective(int stored, int scale, int mode, float[] table)
        {
            if (table == null || table.Length == 0) return 0f;
            int m = Math.Max(0, Math.Min(table.Length - 1, mode));
            if (stored <= 0) return table[m];
            float typed = Decode(stored, scale);
            return PistonToggles.NearestSpeed(table, typed) == m ? typed : table[m];
        }

        public static int LimitMode(float degrees)
        {
            int m = (int)Math.Round(degrees / LIMIT_STEP, MidpointRounding.AwayFromZero);
            return Math.Max(0, Math.Min(LIMIT_MAX_MODE, m));
        }

        public static float LimitEffective(int stored, int mode)
        {
            int m = Math.Max(0, Math.Min(LIMIT_MAX_MODE, mode));
            if (stored <= 0) return m * LIMIT_STEP;
            float typed = Decode(stored, LIMIT_SCALE);
            return LimitMode(typed) == m ? typed : m * LIMIT_STEP;
        }

        public static bool TryParse(string text, out float value)
        {
            value = 0f;
            string t = (text ?? "").Trim().Replace(',', '.');
            if (t.Length == 0) return false;
            return float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        public static int Stored(Part part, ID<PartToggleGuiRules> id)
        {
            ShipMetadata meta = part?.Ship?.Metadata;
            if (meta == null) return 0;
            return meta.PartUIToggleStates.Get((new PartSaveData(part), id)) ?? 0;
        }

        public static void Store(Part part, ID<PartToggleGuiRules> id, int stored)
        {
            ShipMetadata meta = part?.Ship?.Metadata;
            if (meta == null) return;
            meta.PartUIToggleStates.Set((new PartSaveData(part), id), stored);
        }

        public static float PistonSpeed(Part part, float[] table, int mode) => SpeedEffective(Stored(part, PistonSpeedId), PISTON_SPEED_SCALE, mode, table);

        public static float MotorSpeed(Part part, float[] table, int mode) => SpeedEffective(Stored(part, MotorSpeedId), MOTOR_SPEED_SCALE, mode, table);

        public static float LeftLimit(Part part, int mode) => LimitEffective(Stored(part, LeftLimitId), mode);

        public static float RightLimit(Part part, int mode) => LimitEffective(Stored(part, RightLimitId), mode);
    }
}
