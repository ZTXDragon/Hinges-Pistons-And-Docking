using System;
using System.Globalization;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;

namespace ZTX.HingesPistonsDocking
{
    internal static class BayValues
    {
        public static readonly ID<PartToggleGuiRules> FlyOutId = new ID<PartToggleGuiRules>("ztx_bay_flyout");
        public static readonly ID<PartToggleGuiRules> PushTenthsId = new ID<PartToggleGuiRules>("ztx_bay_push");
        public const int FLY_OUT_MIN = 0, FLY_OUT_MAX = 100, PUSH_MIN_TENTHS = 5, PUSH_MAX_TENTHS = 200;

        public static int Resolve(int? stored, int fallback) => stored ?? fallback;

        public static int ClampFlyOut(int cells) => Math.Max(FLY_OUT_MIN, Math.Min(FLY_OUT_MAX, cells));

        public static int ClampPushTenths(int tenths) => Math.Max(PUSH_MIN_TENTHS, Math.Min(PUSH_MAX_TENTHS, tenths));

        public static int ParseFlyOut(string text, int current) =>
            TryNumber(text, out double v) ? ClampFlyOut((int)Math.Round(v)) : current;

        public static int ParsePushTenths(string text, int current) =>
            TryNumber(text, out double v) ? ClampPushTenths((int)Math.Round(v * 10.0)) : current;

        public static string FormatTenths(int tenths) => (tenths / 10.0).ToString("0.0", CultureInfo.InvariantCulture);

        private static bool TryNumber(string text, out double v) =>
            double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)
            && !double.IsNaN(v) && !double.IsInfinity(v);

        public static int Get(Part part, ID<PartToggleGuiRules> id, int fallback)
        {
            var meta = part?.Ship?.Metadata;
            if (meta == null) return fallback;
            return Resolve(meta.PartUIToggleStates.Get((new PartSaveData(part), id)), fallback);
        }

        public static void SetInSim(Part part, ID<PartToggleGuiRules> id, int value)
        {
            var meta = part?.Ship?.Metadata;
            if (meta == null) return;
            meta.PartUIToggleStates.Set((new PartSaveData(part), id), value);
            part.MatchingBlueprintPart?.OnPartUIToggleStatesChanged();
        }

        public static int FlyOutCells(Part terminal, ZtxBayTerminalRules rules) =>
            ClampFlyOut(Get(terminal, FlyOutId, (int)Math.Round(rules?.FlyClearanceCells ?? 3f)));

        public static int PushTenths(Part terminal, ZtxBayTerminalRules rules) =>
            ClampPushTenths(Get(terminal, PushTenthsId, (int)Math.Round((rules?.PushSpeed ?? 5f) * 10f)));
    }
}
