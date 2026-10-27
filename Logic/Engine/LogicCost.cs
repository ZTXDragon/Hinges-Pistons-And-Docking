using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogicCost
    {
        public enum Phase { Decode, Scan, Links, Lines, Push }

        public const double ReportSeconds = 60.0;
        public const int MaxReports = 10;

        private sealed class Bucket
        {
            public long Ticks, Stamp, Max;
            public readonly HashSet<ILogicTicker> Parts = new HashSet<ILogicTicker>();
        }

        private static readonly string[] s_phaseNames = { "decode", "wire scan", "links", "lines", "push" };
        private static readonly long[] s_phase = new long[5];
        private static readonly Dictionary<string, Bucket> s_types = new Dictionary<string, Bucket>();
        private static long s_windowStart;
        private static int s_reports;
        private static int s_rounds;

        public static int WindowId { get; private set; }

        public static bool Active => s_reports < MaxReports;

        public static long Now => Stopwatch.GetTimestamp();

        public static double Ms(long stamps) => stamps * 1000.0 / Stopwatch.Frequency;

        public static void AddPhase(Phase p, long stamps) => s_phase[(int)p] += stamps;

        private static long s_worstTotal;
        private static string s_worstText;
        private static int s_rescans;

        public static void NoteConsoleTick(Cosmoteer.Ships.Parts.Part console, long decode, long scan, long links, long lines, long push, bool rescanned)
        {
            if (rescanned) s_rescans++;
            long total = decode + scan + links + lines + push;
            if (total <= s_worstTotal) return;
            s_worstTotal = total;
            s_worstText = "worst console tick " + Ms(total).ToString("0.000") + " ms at " + console.Location + ": decode " + Ms(decode).ToString("0.000")
                + ", wire scan " + Ms(scan).ToString("0.000") + (rescanned ? " (rescanned)" : "") + ", links " + Ms(links).ToString("0.000")
                + ", lines " + Ms(lines).ToString("0.000")
                + ", push " + Ms(push).ToString("0.000");
        }

        public static void AddTicker(ILogicTicker t, long stamps)
        {
            string name = t is ZtxRuleEngineComponent ? "Logic Console" : t.GetType().Name;
            lock (s_types)
            {
                if (!s_types.TryGetValue(name, out Bucket b)) s_types[name] = b = new Bucket();
                b.Ticks++;
                b.Stamp += stamps;
                if (stamps > b.Max) b.Max = stamps;
                b.Parts.Add(t);
            }
        }

        public static void NoteRounds(int ticks)
        {
            if (ticks > s_rounds) s_rounds = ticks;
        }

        public static void MaybeReport()
        {
            if (!Active) return;
            long now = Now;
            if (s_windowStart == 0) { s_windowStart = now; return; }
            double seconds = (now - s_windowStart) / (double)Stopwatch.Frequency;
            if (seconds < ReportSeconds) return;
            s_reports++;
            double gameSeconds = s_rounds * (double)LogicShipManager.TickSeconds;
            long all = 0;
            lock (s_types)
            {
            foreach (Bucket b in s_types.Values) all += b.Stamp;
            string head = "logic cost #" + s_reports + "/" + MaxReports + (s_reports == 1 ? " (includes load and JIT)" : "")
                + ": " + seconds.ToString("0.0") + " s real, " + s_rounds + " logic ticks (" + gameSeconds.ToString("0.0") + " s game); all logic parts "
                + Ms(all).ToString("0.00") + " ms";
            if (gameSeconds > 0) head += " = " + (Ms(all) / gameSeconds).ToString("0.000") + " ms per game second (" + (Ms(all) / gameSeconds / 10.0).ToString("0.000") + "% of one core)";
            Log.Info(head + ".");
            foreach (KeyValuePair<string, Bucket> kv in s_types)
            {
                Bucket b = kv.Value;
                var sb = new StringBuilder();
                sb.Append("  ").Append(kv.Key).Append(": ").Append(b.Parts.Count).Append(" part(s), ").Append(b.Ticks).Append(" ticks, average ")
                    .Append((Ms(b.Stamp) / Math.Max(1, b.Ticks)).ToString("0.0000")).Append(" ms, worst ").Append(Ms(b.Max).ToString("0.000"))
                    .Append(" ms, share ").Append(all > 0 ? (100.0 * b.Stamp / all).ToString("0") : "0").Append("%");
                if (kv.Key == "Logic Console")
                {
                    sb.Append(" (average per tick:");
                    for (int i = 0; i < s_phase.Length; i++)
                        sb.Append(' ').Append(s_phaseNames[i]).Append(' ').Append((Ms(s_phase[i]) / Math.Max(1, b.Ticks)).ToString("0.0000"));
                    sb.Append(')');
                }
                Log.Info(sb.Append('.').ToString());
            }
            if (s_worstText != null) Log.Info("  " + s_worstText + "; wire rescans this window: " + s_rescans + ".");
            s_worstTotal = 0;
            s_worstText = null;
            s_rescans = 0;
            s_types.Clear();
            }
            Array.Clear(s_phase, 0, s_phase.Length);
            s_rounds = 0;
            s_windowStart = now;
            WindowId++;
        }
    }

    internal static class ConsoleWindowCost
    {
        public enum Phase { Members, Parts, Rules, Live }

        public const int FirstFrames = 5;
        public const int SteadyFrames = 300;

        private static readonly string[] s_names = { "members", "parts tab", "rules tab", "live tab" };
        private static readonly long[] s_frame = new long[4];
        private static readonly long[] s_sum = new long[4];
        private static readonly StringBuilder s_first = new StringBuilder();
        private static int s_count = -1;
        private static long s_last, s_frameStart, s_worst, s_total;

        public static bool Active => s_count >= 0;

        public static Func<string> Extra;

        public static void Opened()
        {
            s_count = 0;
            s_last = LogicCost.Now;
            s_first.Clear();
            Array.Clear(s_sum, 0, s_sum.Length);
            s_worst = 0;
            s_total = 0;
        }

        public static void BeginFrame()
        {
            if (!Active) return;
            s_frameStart = LogicCost.Now;
            Array.Clear(s_frame, 0, s_frame.Length);
        }

        public static void Add(Phase p, long stamps)
        {
            if (Active) s_frame[(int)p] += stamps;
        }

        public static void EndFrame()
        {
            if (!Active) return;
            long now = LogicCost.Now;
            long update = now - s_frameStart;
            s_count++;
            if (s_count <= FirstFrames)
            {
                s_first.Append(s_count == 1 ? "" : "; ").Append("gap ").Append(LogicCost.Ms(s_frameStart - s_last).ToString("0"))
                    .Append(" ms, update ").Append(LogicCost.Ms(update).ToString("0.0")).Append(" (parts ")
                    .Append(LogicCost.Ms(s_frame[(int)Phase.Parts]).ToString("0.0")).Append(", rules ")
                    .Append(LogicCost.Ms(s_frame[(int)Phase.Rules]).ToString("0.0")).Append(')');
                if (s_count == FirstFrames) Log.Info("console window first frames after open: " + s_first + (Extra != null ? "; " + Extra() : "") + ".");
            }
            else
            {
                for (int i = 0; i < s_sum.Length; i++) s_sum[i] += s_frame[i];
                s_total += update;
                if (update > s_worst) s_worst = update;
                if (s_count == FirstFrames + SteadyFrames)
                {
                    var sb = new StringBuilder("console window steady: ").Append(SteadyFrames).Append(" frames, update average ")
                        .Append((LogicCost.Ms(s_total) / SteadyFrames).ToString("0.000")).Append(" ms (");
                    for (int i = 0; i < s_sum.Length; i++)
                        sb.Append(i == 0 ? "" : ", ").Append(s_names[i]).Append(' ').Append((LogicCost.Ms(s_sum[i]) / SteadyFrames).ToString("0.000"));
                    Log.Info(sb.Append("), worst ").Append(LogicCost.Ms(s_worst).ToString("0.00")).Append(" ms.").ToString());
                    s_count = -1;
                }
            }
            s_last = now;
        }
    }
}
