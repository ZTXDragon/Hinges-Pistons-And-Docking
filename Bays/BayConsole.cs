using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;

namespace ZTX.HingesPistonsDocking
{
    internal static class BayConsole
    {
        public const int TERMINAL_CELLS = 8, STATION_CELLS = 1;
        public const int BUILD = 0, RELEASE = 1, REPEAT = 2, WHEN_BUILT = 3, WHEN_CREWED = 4, WHEN_SUPPLIED = 5, WHEN_REPAIRED = 6, WHEN_CHARGED = 7;
        public const int POSE_SPAN = 4096;
        public const int LEAVE = 0, ON = 1, OFF = 2;

        public static readonly string[] TerminalCellKeys =
            { "BayCellBuild", "BayCellRelease", "BayCellRepeat", "BayCellBuilt", "BayCellCrewed", "BayCellSupplied", "BayCellRepaired", "BayCellCharged" };
        public const string StationCellKey = "BayCellStation";

        public static int CodeOf(int onPose, int offPose) => Math.Max(0, onPose) + POSE_SPAN * Math.Max(0, offPose);

        public static void SplitCode(int code, out int onPose, out int offPose)
        {
            code = Math.Max(0, code);
            onPose = code % POSE_SPAN;
            offPose = code / POSE_SPAN;
        }

        public static int CellCount(int kind) =>
            kind == LogicAction.KIND_BAY ? TERMINAL_CELLS : kind == LogicAction.KIND_BAY_STATION ? STATION_CELLS : 0;

        public static int Digit(int code, int cell)
        {
            if (cell < 0 || cell >= TERMINAL_CELLS || code < 0) return LEAVE;
            for (int i = 0; i < cell; i++) code /= 3;
            return code % 3;
        }

        public static int With(int code, int cell, int value)
        {
            if (cell < 0 || cell >= TERMINAL_CELLS) return code;
            code = Math.Max(0, code);
            int place = 1;
            for (int i = 0; i < cell; i++) place *= 3;
            int v = Math.Max(LEAVE, Math.Min(OFF, value));
            return code - Digit(code, cell) * place + v * place;
        }

        public static List<(int Cell, int Mode)> Plan(int kind, bool set, int code)
        {
            var plan = new List<(int, int)>();
            if (!set) return plan;
            int n = CellCount(kind);
            for (int i = 0; i < n; i++)
            {
                int d = Digit(code, i);
                if (d == ON) plan.Add((i, 1));
                else if (d == OFF) plan.Add((i, 0));
            }
            return plan;
        }

        public static string CellKey(int kind, int cell) => kind == LogicAction.KIND_BAY_STATION ? StationCellKey : TerminalCellKeys[cell];

        private static ID<PartToggleGuiRules> IdOf(int kind, int cell)
        {
            if (kind == LogicAction.KIND_BAY_STATION) return BayManager.StationOnId;
            switch (cell)
            {
                case BUILD: return BayManager.BuildId;
                case RELEASE: return BayManager.ReleaseId;
                case REPEAT: return BayManager.RepeatId;
                default: return BayManager.WhenIds[cell - WHEN_BUILT];
            }
        }

        public static string ApplyDo(Part part, int kind, LogicAction a, bool on) =>
            Apply(part, kind, a.Sided ? Plan(kind, a.OnSet, CodeOf(a.OnPose, a.OffPose)) : Plan(kind, on ? a.OnSet : a.OffSet, on ? a.OnPose : a.OffPose));

        public static string Apply(Part part, int kind, List<(int Cell, int Mode)> plan)
        {
            if (part == null || part.IsDestroyed || part.Ship == null || plan == null) return "";
            string changed = "";
            foreach (var (cell, mode) in plan)
            {
                ID<PartToggleGuiRules> id = IdOf(kind, cell);
                PartUIToggle t = PartUIToggle.GetOnPart(part, id);
                if (t == null)
                {
                    LogThrottle.Info("BayConsole|" + id, $"Bay console: {part.Rules.ID} at {part.Location} has no toggle {id}; cell skipped.");
                    continue;
                }
                if (t.Mode == mode) continue;
                t.Mode = mode;
                t.Part.MatchingBlueprintPart?.OnPartUIToggleStatesChanged();
                changed += (changed.Length > 0 ? ", " : "") + id + (mode != 0 ? " on" : " off");
            }
            return changed;
        }
    }
}
