using System;
using System.Collections.Generic;

namespace ZTX.HingesPistonsDocking
{
    internal enum CartIntent { Travel = 0, Home = 1, Hold = 2 }

    internal static class RailCartRules
    {
        public const int MODE_HOLD = 5;

        public static CartIntent IntentOf(int mode, bool targetIsHome)
        {
            if (mode == MODE_HOLD) return CartIntent.Hold;
            if (mode == RailShipManager.MODE_GOTO && targetIsHome) return CartIntent.Home;
            return CartIntent.Travel;
        }

        public static int LowestFreeId(IEnumerable<int> used)
        {
            var set = new HashSet<int>(used);
            int id = 1;
            while (set.Contains(id)) id++;
            return id;
        }

        public static void PackName(string name, out int n1, out int n2)
        {
            string clean = LogicRuleDef.Clean(name ?? "");
            n1 = LogicRuleDef.PackName(clean, 0);
            n2 = LogicRuleDef.PackName(clean, 5);
        }

        public static string UnpackName(int n1, int n2) => (LogicRuleDef.UnpackName(n1) + LogicRuleDef.UnpackName(n2)).Trim();

        public static string DisplayName(string name, int id) => name.Length > 0 ? name : id > 0 ? "Cart " + id : "Cart";

        public static int Settle(int mode, bool targetIsHome, bool merged, bool moving, string resting, string targetKey, bool mergeOn, out string why)
        {
            why = "";
            switch (IntentOf(mode, targetIsHome))
            {
                case CartIntent.Hold:
                    if (moving) { why = "stopping for HOLD"; return SeqRunner.RUNNING; }
                    if (mergeOn && !merged) { why = "merging where it holds"; return SeqRunner.RUNNING; }
                    if (!mergeOn && merged && resting != "") { why = "unmerging"; return SeqRunner.RUNNING; }
                    return SeqRunner.DONE;
                case CartIntent.Home:
                    if (!merged || resting != "") { why = "going home"; return SeqRunner.RUNNING; }
                    return SeqRunner.DONE;
                default:
                    if (mode != RailShipManager.MODE_GOTO) return SeqRunner.DONE;
                    if (merged)
                    {
                        if (resting != targetKey) { why = "setting off"; return SeqRunner.RUNNING; }
                        if (!mergeOn) { why = "unmerging"; return SeqRunner.RUNNING; }
                        return SeqRunner.DONE;
                    }
                    if (moving || resting != targetKey) { why = "moving to " + targetKey; return SeqRunner.RUNNING; }
                    if (mergeOn) { why = "merging at " + targetKey; return SeqRunner.RUNNING; }
                    return SeqRunner.DONE;
            }
        }

        public static bool MergeWanted(int mode, bool targetIsHome, bool mergeOn, string resting, string targetKey, bool dwelling)
        {
            if (!mergeOn) return false;
            switch (IntentOf(mode, targetIsHome))
            {
                case CartIntent.Hold: return true;
                case CartIntent.Home: return false;
                default:
                    if (mode == RailShipManager.MODE_GOTO) return !string.IsNullOrEmpty(targetKey) && resting == targetKey;
                    return dwelling && !string.IsNullOrEmpty(resting);
            }
        }

        public static float HoldStopS(float natural, IList<float> feet, int dir)
        {
            float best = natural;
            bool found = false;
            foreach (float f in feet)
            {
                if (dir >= 0 ? f < natural - 1e-4f : f > natural + 1e-4f) continue;
                if (!found || (dir >= 0 ? f < best : f > best)) { best = f; found = true; }
            }
            return found ? best : natural;
        }

        public static int GripSettle(int grip, bool holding, bool refused, out string why)
        {
            why = "";
            if (grip == LogicAction.GRIP_ATTACH && !holding) { why = refused ? "attach refused" : "attaching"; return refused ? SeqRunner.REFUSED : SeqRunner.RUNNING; }
            if (grip == LogicAction.GRIP_RELEASE && holding) { why = refused ? "release refused" : "releasing"; return refused ? SeqRunner.REFUSED : SeqRunner.RUNNING; }
            return SeqRunner.DONE;
        }

        public static bool AtStation(string resting, bool wantHome, string wantKey)
        {
            if (resting == null) return false;
            return wantHome ? resting.Length == 0 : !string.IsNullOrEmpty(wantKey) && resting == wantKey;
        }

        public static readonly float[] OLD_SPEEDS = { 0.5f, 1f, 2f, 3f, 5f, 8f };
        public static readonly int[] OLD_POWERS = { 50, 100, 200, 400 };
        public static readonly float[] OLD_DWELLS = { 0f, 2f, 5f, 10f, 20f };
        public const float SPEED_MIN = 0.1f, SPEED_MAX = 51f, DWELL_MAX = 409.4f;
        public const int POWER_MIN = 10, POWER_MAX = 1000;

        public static bool DirectionChangeStops(bool moving, int lastDirMode, int mode) => moving && lastDirMode != mode;

        public const int STATE_WAITING = 0, STATE_MOVING = 1, STATE_DWELLING = 2, STATE_HOME = 3, STATE_MERGED = 4, STATE_HOLDING = 5;

        public static int StateOf(bool riding, bool moving, bool dwelling, bool atHome, bool merged, bool waiting)
        {
            if (riding && waiting) return STATE_WAITING;
            if (moving) return STATE_MOVING;
            if (dwelling) return STATE_DWELLING;
            if (atHome) return STATE_HOME;
            if (merged) return STATE_MERGED;
            return STATE_HOLDING;
        }

        public static int CurveRestSide(int oldSide, int wantSide, out bool reverse)
        {
            reverse = wantSide != oldSide;
            return reverse ? -oldSide : oldSide;
        }

        public static float StopOutsideCurve(float target, float dir, float[] spanLo, float[] spanHi, int count)
        {
            for (int i = 0; i < count; i++)
                if (target > spanLo[i] && target < spanHi[i]) return dir > 0f ? spanHi[i] : spanLo[i];
            return target;
        }

        public static int EncodeSpeed(float cells) => Math.Max(1, Math.Min(510, (int)Math.Round(cells * 10f))) + 1;
        public static float DecodeSpeed(int stored, int oldMode) => stored > 0 ? (stored - 1) / 10f : OLD_SPEEDS[Math.Max(0, Math.Min(OLD_SPEEDS.Length - 1, oldMode))];
        public static int EncodePower(int percent) => Math.Max(POWER_MIN, Math.Min(POWER_MAX, percent)) + 1;
        public static int DecodePower(int stored, int oldMode) => stored > 0 ? stored - 1 : OLD_POWERS[Math.Max(0, Math.Min(OLD_POWERS.Length - 1, oldMode))];
        public static int EncodeDwell(float seconds) => Math.Max(0, Math.Min(4094, (int)Math.Round(seconds * 10f))) + 1;
        public static float DecodeDwell(int stored, int oldMode) => stored > 0 ? (stored - 1) / 10f : OLD_DWELLS[Math.Max(0, Math.Min(OLD_DWELLS.Length - 1, oldMode))];

        public static int PackDrive(float? speed, int? power, float? dwell)
        {
            uint u = (uint)(speed.HasValue ? EncodeSpeed(speed.Value) : 0)
                | ((uint)(power.HasValue ? EncodePower(power.Value) : 0) << 9)
                | ((uint)(dwell.HasValue ? EncodeDwell(dwell.Value) : 0) << 20);
            return unchecked((int)u);
        }

        public static void UnpackDrive(int packed, out float? speed, out int? power, out float? dwell)
        {
            uint u = unchecked((uint)packed);
            uint s = u & 511u, p = (u >> 9) & 2047u, d = (u >> 20) & 4095u;
            speed = s > 0 ? (s - 1) / 10f : (float?)null;
            power = p > 0 ? (int)p - 1 : (int?)null;
            dwell = d > 0 ? (d - 1) / 10f : (float?)null;
        }

        public static bool SameWay(float ax, float ay, float bx, float by)
        {
            float la = (float)Math.Sqrt(ax * ax + ay * ay), lb = (float)Math.Sqrt(bx * bx + by * by);
            if (la < 1e-6f || lb < 1e-6f) return false;
            return (ax * bx + ay * by) / (la * lb) > 0.7f;
        }

        public static int CompareNames(string a, string b)
        {
            a ??= "";
            b ??= "";
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (si < a.Length && a[si] == '0') si++;
                    while (sj < b.Length && b[sj] == '0') sj++;
                    int ei = si, ej = sj;
                    while (ei < a.Length && char.IsDigit(a[ei])) ei++;
                    while (ej < b.Length && char.IsDigit(b[ej])) ej++;
                    int la = ei - si, lb = ej - sj;
                    if (la != lb) return la < lb ? -1 : 1;
                    int c = string.CompareOrdinal(a, si, b, sj, la);
                    if (c != 0) return c < 0 ? -1 : 1;
                    i = ei;
                    j = ej;
                    continue;
                }
                if (a[i] != b[j]) return a[i] < b[j] ? -1 : 1;
                i++;
                j++;
            }
            if (i < a.Length || j < b.Length) return i < a.Length ? 1 : -1;
            int o = string.CompareOrdinal(a, b);
            return o < 0 ? -1 : o > 0 ? 1 : 0;
        }

        public static readonly string[] LIFT_IDS =
        {
            "ztx.hinges_pistons_docking.frame",
            "ztx.hinges_pistons_docking.frame_wedge",
            "ztx.hinges_pistons_docking.frame_1x2_wedge",
            "ztx.hinges_pistons_docking.frame_1x3_wedge",
            "ztx.hinges_pistons_docking.frame_tri",
        };

        public static int LiftIndex(string id) => Array.IndexOf(LIFT_IDS, id);

        public static bool StartBlend(bool hasCameFrom, bool cameFromIsNeighbour, bool cameFromIsNextTile, bool freshPair)
            => hasCameFrom && cameFromIsNeighbour && !cameFromIsNextTile && !freshPair;

        public static HashSet<string> Duplicates(IEnumerable<string> names)
        {
            var seen = new HashSet<string>();
            var dup = new HashSet<string>();
            foreach (string n in names)
            {
                if (string.IsNullOrEmpty(n)) continue;
                if (!seen.Add(n)) dup.Add(n);
            }
            return dup;
        }
    }
}
