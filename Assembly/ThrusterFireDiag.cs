using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts.Thrusters;
using Halfling.Geometry;
using Halfling.Physics2D.Dynamics;
using Halfling.Timing;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class ThrusterFireDiag
    {
        private const int LOG_PERIOD_MS = 1000;
        private const float ON_LEVEL = 0.5f;
        private const float OFF_LEVEL = 0.1f;
        private const float IDLE_LEVEL = 0.005f;

        private sealed class ThrusterHist
        {
            public bool On;
            public int Flips;
            public float WantMin = float.MaxValue;
            public float WantMax = float.MinValue;
            public float ActMin = float.MaxValue;
            public float ActMax = float.MinValue;
        }

        private sealed class State
        {
            public long LastLogMs;
            public int Commits;
            public int FailsafeCommits;
            public bool DemandValid;
            public bool Failsafe;
            public Vector3 LastDemand;
            public Vector3 LastAchieved;
            public Vector3 LastSrfFactors;
            public float LastIntervalMs;
            public bool Union;
            public readonly Dictionary<Thruster, ThrusterHist> Hist = new Dictionary<Thruster, ThrusterHist>();
        }

        private static readonly ConditionalWeakTable<ThrusterManager, State> s_state =
            new ConditionalWeakTable<ThrusterManager, State>();
        private static readonly ConditionalWeakTable<Ship, object> s_watched =
            new ConditionalWeakTable<Ship, object>();
        private static readonly object s_marker = new object();

        public static bool Enabled = false;

        public static void Watch(Ship ship)
        {
            try
            {
                if (!Enabled || ship == null) return;
                if (!s_watched.TryGetValue(ship, out _))
                {
                    s_watched.Add(ship, s_marker);
                    Log.Info($"FIRE watch: ship {ship.UniqueID} will log thruster firing (1 Hz).");
                }
            }
            catch { }
        }

        private static bool IsWatched(Ship ship)
        {
            return Enabled && ship != null && s_watched.TryGetValue(ship, out _);
        }

        [HarmonyPatch(typeof(ThrusterManager), "CalculateDesiredSRF")]
        private static class CalcPatch
        {
            static void Prefix(ThrusterManager __instance, Vector3 desiredSRF, Time interval,
                Vector3 srfFactors, bool setMagicSRF)
            {
                try
                {
                    if (!IsWatched(__instance.Ship)) return;
                    State st = s_state.GetValue(__instance, _ => new State());
                    if (!setMagicSRF && desiredSRF == Vector3.Zero)
                    {
                        st.Failsafe = true;
                        return;
                    }
                    st.DemandValid = true;
                    st.Union = false;
                    st.Failsafe = false;
                    st.LastDemand = desiredSRF;
                    st.LastSrfFactors = srfFactors;
                    st.LastIntervalMs = (float)interval * 1000f;
                }
                catch { }
            }

            static void Postfix(ThrusterManager __instance, bool setMagicSRF, Vector3 __result)
            {
                try
                {
                    if (!setMagicSRF) return;
                    if (!IsWatched(__instance.Ship)) return;
                    State st = s_state.GetValue(__instance, _ => new State());
                    st.LastAchieved = __result;
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(ThrusterManager), "CommitActivationLevels")]
        private static class CommitPatch
        {
            static void Postfix(ThrusterManager __instance, Time interval)
            {
                try
                {
                    Ship ship = __instance.Ship;
                    if (!IsWatched(ship)) return;
                    State st = s_state.GetValue(__instance, _ => new State());
                    st.Commits++;
                    if (st.Failsafe) st.FailsafeCommits++;

                    List<Thruster> thrusters = __instance._orderedThrusters;
                    for (int i = 0; i < thrusters.Count; i++)
                    {
                        Thruster t = thrusters[i];
                        if (!st.Hist.TryGetValue(t, out ThrusterHist h))
                        {
                            h = new ThrusterHist();
                            st.Hist[t] = h;
                        }
                        float want = Math.Abs(t.UncomittedActivationLevel);
                        float act = Math.Abs(t.ActivationLevel);
                        if (h.On && want < OFF_LEVEL) { h.On = false; h.Flips++; }
                        else if (!h.On && want > ON_LEVEL) { h.On = true; h.Flips++; }
                        if (want < h.WantMin) h.WantMin = want;
                        if (want > h.WantMax) h.WantMax = want;
                        if (act < h.ActMin) h.ActMin = act;
                        if (act > h.ActMax) h.ActMax = act;
                    }

                    long now = Environment.TickCount64;
                    if (now - st.LastLogMs < LOG_PERIOD_MS) return;
                    st.LastLogMs = now;
                    Emit(ship, __instance, st, (float)interval * 1000f);

                    st.Commits = 0;
                    st.FailsafeCommits = 0;
                    foreach (ThrusterHist h in st.Hist.Values)
                    {
                        h.Flips = 0;
                        h.WantMin = float.MaxValue; h.WantMax = float.MinValue;
                        h.ActMin = float.MaxValue; h.ActMax = float.MinValue;
                    }
                }
                catch (Exception ex)
                {
                    Log.Exception("ThrusterFireDiag.CommitPatch", ex);
                }
            }
        }

        public static void NoteUnionSolve(List<AssemblyServo.Member> members, Vector3 demand, Vector3 achieved,
            Time interval, Vector3 srfFactors, bool failsafe)
        {
            try
            {
                foreach (AssemblyServo.Member m in members)
                {
                    if (!IsWatched(m.Ship)) continue;
                    State st = s_state.GetValue(m.Thrusters, _ => new State());
                    st.DemandValid = true;
                    st.Union = true;
                    st.Failsafe = failsafe;
                    st.LastDemand = demand;
                    st.LastAchieved = achieved;
                    st.LastSrfFactors = srfFactors;
                    st.LastIntervalMs = (float)interval * 1000f;
                }
            }
            catch { }
        }

        private static void Emit(Ship ship, ThrusterManager tm, State st, float commitIntervalMs)
        {
            Body body = ship.Physics?.Body;
            if (body == null) return;

            string mode = "VANILLA";
            bool jointed = HingeJointManager.IsRigidlyJointed(ship);
            bool basisAsm = ThrusterTorqueBasisPatch.TryGetOffset(ship, out float rx, out float ry);
            if (jointed)
            {
                if (!AssemblyFlightFeatures.Enabled) mode = "VANILLA+JOINTS";
                else mode = ReferenceEquals(HingeJointManager.GetDesignatedPilot(ship), ship) ? "SERVO-PILOT" : "SERVO-MEMBER";
            }

            float fx = 0f, fy = 0f, tauSrf = 0f;
            int firing = 0, pulsing = 0;
            List<Thruster> thrusters = tm._orderedThrusters;
            var lines = new List<string>(thrusters.Count);
            for (int i = 0; i < thrusters.Count; i++)
            {
                Thruster t = thrusters[i];
                Vector3 srf = t.GetSRF();
                float lvl = t.ActivationLevel;
                fx += srf.X * lvl;
                fy += srf.Y * lvl;
                tauSrf += srf.Z * lvl;

                st.Hist.TryGetValue(t, out ThrusterHist h);
                float want = Math.Abs(t.UncomittedActivationLevel);
                bool idle = Math.Abs(lvl) < IDLE_LEVEL && want < IDLE_LEVEL && (h == null || h.Flips == 0);
                if (idle) continue;
                firing++;
                if (h != null && h.Flips >= 2) pulsing++;

                float f = Math.Max(t.Force, 1e-6f);
                float cross = rx * srf.Y - ry * srf.X;
                float armOwn = (basisAsm ? srf.Z - cross : srf.Z) / f;
                float armAsm = (basisAsm ? srf.Z : srf.Z + cross) / f;
                var loc = t.Part != null ? t.Part.Location : default;
                string hist = h == null ? "" :
                    $" want[{Fmt(h.WantMin)},{Fmt(h.WantMax)}] act[{Fmt(h.ActMin)},{Fmt(h.ActMax)}] flips={h.Flips}";
                lines.Add($"  T{i} {t.ShipDirection} @({loc.X},{loc.Y}) F={t.Force:F0} arm={armOwn:F2}/{armAsm:F2} act={lvl:F2} want={t.UncomittedActivationLevel:F2}{hist}");
            }
            float crossNet = rx * fy - ry * fx;
            float tauOwn = basisAsm ? tauSrf - crossNet : tauSrf;
            float tauAsm = basisAsm ? tauSrf : tauSrf + crossNet;

            string demand = st.DemandValid
                ? $"{(st.Union ? "asmDemandSRF" : "demandSRF")}=({st.LastDemand.X:F0},{st.LastDemand.Y:F0},{st.LastDemand.Z:F0}) achievedSRF=({st.LastAchieved.X:F0},{st.LastAchieved.Y:F0},{st.LastAchieved.Z:F0}) srfF=({st.LastSrfFactors.X:F2},{st.LastSrfFactors.Y:F2},{st.LastSrfFactors.Z:F2}) solveDt={st.LastIntervalMs:F0}ms"
                : "demandSRF=<none>";
            string fs = st.Failsafe ? " FAILSAFE-ZEROED" : "";
            Log.Info($"FIRE ship {ship.UniqueID} [{mode}{(basisAsm ? "/ASM-BASIS" : "")}]: commits={st.Commits} failsafeCommits={st.FailsafeCommits}{fs} thr={thrusters.Count} softOp={tm._orderedSoftOpThrusters.Count} firing={firing} pulsing={pulsing} {demand} committed=({fx:F0},{fy:F0},tauOwn={tauOwn:F0},tauAsm={tauAsm:F0}) rOwn=({rx:F2},{ry:F2}) commitDt={commitIntervalMs:F0}ms |magic|={tm._magicSRF.XY.Length:F1}");
            foreach (string l in lines)
                Log.Info($"FIRE ship {ship.UniqueID}{l}");
        }

        private static string Fmt(float v)
        {
            return (v == float.MaxValue || v == float.MinValue) ? "-" : v.ToString("F2");
        }
    }
}
