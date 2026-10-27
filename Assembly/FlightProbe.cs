using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling.Geometry;

namespace ZTX.HingesPistonsDocking
{
    internal static class FlightProbe
    {
        public static readonly bool ENABLED = false;

        private const double WINDOW_SECONDS = 2.0;
        private const float LEGACY_PROBE_LIMIT = 100f;
        private const int MAX_TRAILER_LINES = 4;

        private sealed class HeadStats
        {
            public long Real, Probes, Misread, Recorded;
            public float MaxReal;
            public Vector3 LastReal;
            public string Command = "";
        }

        private sealed class TrailerStats
        {
            public long Calls, NoDesire, NoHead, Walks, ElapsedTicks;
            public double Demand, Achieved, Tau;
            public string Head = "";
        }

        private static readonly ConcurrentDictionary<Ship, HeadStats> s_heads = new ConcurrentDictionary<Ship, HeadStats>();
        private static readonly ConcurrentDictionary<Ship, TrailerStats> s_trailers = new ConcurrentDictionary<Ship, TrailerStats>();
        private static long s_planTicks, s_planBuilds, s_planTrailers;
        private static long s_windowStart = Stopwatch.GetTimestamp();

        public static void Reset()
        {
            s_heads.Clear();
            s_trailers.Clear();
            Interlocked.Exchange(ref s_planTicks, 0);
            Interlocked.Exchange(ref s_planBuilds, 0);
            Interlocked.Exchange(ref s_planTrailers, 0);
        }

        public static bool LegacyWouldProbe(Vector3 sra) =>
            Math.Abs(sra.X) >= LEGACY_PROBE_LIMIT || Math.Abs(sra.Y) >= LEGACY_PROBE_LIMIT || Math.Abs(sra.Z) >= LEGACY_PROBE_LIMIT;

        public static void OnSra(Ship ship, Vector3 sra, bool isProbe, bool recorded)
        {
            if (!ENABLED || ship == null) return;
            HeadStats h = s_heads.GetOrAdd(ship, _ => new HeadStats());
            lock (h)
            {
                if (isProbe) { h.Probes++; return; }
                h.Real++;
                if (recorded) h.Recorded++;
                if (LegacyWouldProbe(sra)) h.Misread++;
                float mag = Math.Max(Math.Abs(sra.X), Math.Max(Math.Abs(sra.Y), Math.Abs(sra.Z)));
                if (mag > h.MaxReal) h.MaxReal = mag;
                h.LastReal = sra;
                h.Command = ship.Commands?.CurConfirmedCommand?.GetType().Name ?? "none";
            }
        }

        public static void OnTrailer(Ship ship, Ship head, bool hadDesire, bool walked, Vector3 demanded, Vector3 achieved, long elapsedTicks)
        {
            if (!ENABLED || ship == null) return;
            TrailerStats t = s_trailers.GetOrAdd(ship, _ => new TrailerStats());
            lock (t)
            {
                t.Calls++;
                t.ElapsedTicks += elapsedTicks;
                if (walked) t.Walks++;
                if (head == null) { t.NoHead++; return; }
                t.Head = head.UniqueID.ToString();
                if (!hadDesire) { t.NoDesire++; return; }
                t.Demand += Math.Sqrt(demanded.X * demanded.X + demanded.Y * demanded.Y);
                t.Achieved += Math.Sqrt(achieved.X * achieved.X + achieved.Y * achieved.Y);
                t.Tau += Math.Abs(demanded.Z);
            }
        }

        public static void OnPlan(long elapsedTicks, int trailers)
        {
            if (!ENABLED) return;
            Interlocked.Add(ref s_planTicks, elapsedTicks);
            Interlocked.Increment(ref s_planBuilds);
            Interlocked.Add(ref s_planTrailers, trailers);
        }

        public static void Emit(SimRoot sim)
        {
            if (!ENABLED) return;
            long now = Stopwatch.GetTimestamp();
            double seconds = (now - s_windowStart) / (double)Stopwatch.Frequency;
            if (seconds < WINDOW_SECONDS) return;
            s_windowStart = now;
            CultureInfo inv = CultureInfo.InvariantCulture;
            foreach (KeyValuePair<Ship, HeadStats> kv in s_heads.ToArray())
            {
                HeadStats h = kv.Value;
                lock (h)
                {
                    if (h.Real == 0) { h.Probes = 0; continue; }
                    Log.Info(string.Format(inv,
                        "Flight probe head {0} ({1}): {2:0}/s real requests, {3:0}/s recorded, {4:0}/s the old size rule dropped as probes, {5:0}/s probes; largest {6:0.#}, last ({7:0.#}, {8:0.#}, {9:0.##})",
                        kv.Key.UniqueID, h.Command, h.Real / seconds, h.Recorded / seconds, h.Misread / seconds, h.Probes / seconds,
                        h.MaxReal, h.LastReal.X, h.LastReal.Y, h.LastReal.Z));
                    h.Real = h.Probes = h.Misread = h.Recorded = 0;
                    h.MaxReal = 0f;
                }
            }
            var trailers = s_trailers.ToArray().Where(kv => kv.Value.Calls > 0).ToList();
            if (trailers.Count > 0)
            {
                long calls = 0, noDesire = 0, noHead = 0, walks = 0, ticks = 0;
                double demand = 0, achieved = 0;
                var worst = new List<(double Ratio, long Fed, string Line)>();
                foreach (KeyValuePair<Ship, TrailerStats> kv in trailers)
                {
                    TrailerStats t = kv.Value;
                    lock (t)
                    {
                        calls += t.Calls; noDesire += t.NoDesire; noHead += t.NoHead; walks += t.Walks; ticks += t.ElapsedTicks;
                        demand += t.Demand; achieved += t.Achieved;
                        double ratio = t.Demand > 0 ? t.Achieved / t.Demand : 1.0;
                        long fed = t.Calls - t.NoDesire - t.NoHead;
                        worst.Add((ratio, fed, string.Format(inv, "segment {0} (head {1}): {2} echoes, {3} without a head desire, mean demand {4:0} achieved {5:0} ({6:0%}), mean |tau| {7:0}",
                            kv.Key.UniqueID, t.Head, t.Calls, t.NoDesire, fed > 0 ? t.Demand / fed : 0, fed > 0 ? t.Achieved / fed : 0, ratio, fed > 0 ? t.Tau / fed : 0)));
                        t.Calls = t.NoDesire = t.NoHead = t.Walks = t.ElapsedTicks = 0;
                        t.Demand = t.Achieved = t.Tau = 0;
                    }
                }
                double ms = ticks * 1000.0 / Stopwatch.Frequency;
                Log.Info(string.Format(inv,
                    "Flight probe trailers: {0} segments, {1:0}/s echoes, {2} without a head, {3} without a head desire, {4} assembly walks; demand {5:0} achieved {6:0} ({7:0%}); cost {8:0.00} ms/s",
                    trailers.Count, calls / seconds, noHead, noDesire, walks, demand, achieved, demand > 0 ? achieved / demand : 1.0, ms / seconds));
                foreach (var w in worst.Where(x => x.Fed > 0).OrderBy(x => x.Ratio).Take(MAX_TRAILER_LINES)) Log.Info("Flight probe " + w.Line);
            }
            long builds = Interlocked.Exchange(ref s_planBuilds, 0);
            long planTicks = Interlocked.Exchange(ref s_planTicks, 0);
            long planTrailers = Interlocked.Exchange(ref s_planTrailers, 0);
            if (builds > 0 && planTrailers > 0)
                Log.Info(string.Format(inv, "Flight probe plan: {0:0}/s builds, {1:0.0} trailers each, {2:0.00} ms/s",
                    builds / seconds, planTrailers / (double)builds, planTicks * 1000.0 / Stopwatch.Frequency / seconds));
            foreach (Ship s in s_heads.Keys.ToArray()) if (s.IsDisposed) s_heads.TryRemove(s, out _);
            foreach (Ship s in s_trailers.Keys.ToArray()) if (s.IsDisposed) s_trailers.TryRemove(s, out _);
        }
    }
}
