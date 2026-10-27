using System;
using System.Collections.Generic;

namespace ZTX.HingesPistonsDocking
{
    internal enum SeqKind
    {
        None = 0,
        Command = 1,
        Wait = 2,
        WaitUntil = 3,
        Do = 5,
        If = 6,
        Else = 7,
        End = 8,
        Repeat = 9,
        While = 10,
        Stop = 11,
        Together = 12,
        Call = 13,
        MemSet = 14,
        MemAdd = 15,
    }

    internal struct SeqStep
    {
        public const int FORMAT = 2;
        public const int ORDER_MAX = 2047, POSE_MAX = 4095, SECONDS_MAX = 4095, TIMEOUT_MAX = 127, SPEED_MAX = 255;
        public const int DEFAULT_COMMAND_TIMEOUT = 30;

        public int Seq;
        public bool NoWait;
        public bool Not;
        public bool Settle;
        public int Order;
        public SeqKind Kind;
        public LogicRef Target;
        public bool PoseSet;
        public int Pose;
        public int Seconds;
        public int Timeout;
        public int Speed;
        public int Strength;
        public int Accel;
        public int Grip;
        public int Merge;
        public int Lock;
        public LogicRef Station;
        public int Drive;

        public bool IsUsed => Kind != SeqKind.None && Seq >= 1;

        public int PackM() => Kind == SeqKind.None ? 0
            : ((int)Kind & 15) | (Not ? 1 << 4 : 0) | (NoWait ? 1 << 5 : 0) | (Settle ? 1 << 10 : 0) | ((Grip & 3) << 6) | ((Merge & 3) << 8)
              | (PoseSet ? 1 << 11 : 0) | ((Clamp(Order, ORDER_MAX)) << 13) | (FORMAT << 24) | ((Lock & 3) << 28);

        public int PackP() => Clamp(Pose, POSE_MAX) | (Clamp(Seconds, SECONDS_MAX) << 12) | (Clamp(Timeout, TIMEOUT_MAX) << 24);

        public int PackQ() => Clamp(Speed, SPEED_MAX) | (Clamp(Strength, 15) << 8) | (Clamp(Accel, 3) << 12);

        public static SeqStep Unpack(int t, int m, int p, int q, int owner)
        {
            var s = new SeqStep();
            if (((m >> 24) & 15) != FORMAT) return s;
            int kind = m & 15;
            if (kind < 1 || kind > (int)SeqKind.MemAdd || kind == 4) return s;
            s.Kind = (SeqKind)kind;
            s.Seq = owner;
            s.NoWait = (m & (1 << 5)) != 0;
            s.Settle = (m & (1 << 10)) != 0;
            s.Not = (m & (1 << 4)) != 0;
            s.Grip = (m >> 6) & 3;
            s.Merge = Math.Min(2, (m >> 8) & 3);
            s.PoseSet = (m & (1 << 11)) != 0;
            s.Order = (m >> 13) & ORDER_MAX;
            s.Lock = Math.Min(2, (m >> 28) & 3);
            s.Pose = p & POSE_MAX;
            s.Seconds = (p >> 12) & SECONDS_MAX;
            s.Timeout = (p >> 24) & TIMEOUT_MAX;
            s.Speed = q & SPEED_MAX;
            s.Strength = (q >> 8) & 15;
            s.Accel = Math.Min(2, (q >> 12) & 3);
            s.Target = LogicRef.Unpack(t);
            return s;
        }

        private static int Clamp(int v, int max) => v < 0 ? 0 : v > max ? max : v;

        public override string ToString() => Kind + "#" + Order + " " + Target
            + (PoseSet ? " pos " + Pose / 10f : "") + (Grip != 0 ? " grip " + Grip : "") + (Merge != 0 ? " merge " + Merge : "")
            + (!Station.IsNone ? " station " + Station : "") + (NoWait ? " nowait" : "") + (Kind == SeqKind.Wait ? " " + Seconds / 10f + "s" : "")
            + (Timeout > 0 ? " timeout " + Timeout : "");
    }

    internal struct SeqDef
    {
        public const int FORMAT = 1;
        public const int POS_MAX = 4095;

        public int Rule;
        public const int TRIGGER_SHIFT = 16;
        public HandlerTrigger Trigger;
        public bool QueueOne;
        public bool StopOnRefusal;
        public int Pos;

        public bool IsUsed => Rule >= 1;

        public int PackM() => Rule < 1 ? 0
            : (Trigger == HandlerTrigger.WhenOff ? 1 : 0) | (TriggerMode(Trigger) << TRIGGER_SHIFT) | (QueueOne ? 1 << 1 : 0) | (StopOnRefusal ? 1 << 2 : 0)
              | ((Pos < 0 ? 0 : Pos > POS_MAX ? POS_MAX : Pos) << 4) | (FORMAT << 24);

        public static SeqDef Unpack(int m, int owner)
        {
            var d = new SeqDef();
            if (owner < 1 || ((m >> 24) & 15) != FORMAT) return d;
            d.Rule = owner;
            int mode = (m >> TRIGGER_SHIFT) & 7;
            d.Trigger = mode == 1 ? HandlerTrigger.WhileOn : mode == 2 ? HandlerTrigger.WhileOff : mode == 3 ? HandlerTrigger.Always : mode == 4 ? HandlerTrigger.OnCall
                : (m & 1) != 0 ? HandlerTrigger.WhenOff : HandlerTrigger.WhenOn;
            d.QueueOne = (m & (1 << 1)) != 0;
            d.StopOnRefusal = (m & (1 << 2)) != 0;
            d.Pos = (m >> 4) & POS_MAX;
            return d;
        }

        private static int TriggerMode(HandlerTrigger t) => t == HandlerTrigger.WhileOn ? 1 : t == HandlerTrigger.WhileOff ? 2 : t == HandlerTrigger.Always ? 3 : t == HandlerTrigger.OnCall ? 4 : 0;

        public override string ToString() => "seq(rule " + Rule + ", " + Trigger + (QueueOne ? ", queue one" : "")
            + (StopOnRefusal ? ", stop on refusal" : "") + ", pos " + Pos + ")";
    }

    internal sealed class SeqRun
    {
        public bool Running;
        public int Index;
        public int StartTick;
        public bool Begun;
        public bool Queued;
        public string Why = "";
        public readonly Dictionary<int, int> Counts = new Dictionary<int, int>();
        public int Together = -1;
        public readonly List<(int Index, int Start)> Pending = new List<(int, int)>();

        public int Pack() => (Running ? 1 : 0) | (Queued ? 1 << 4 : 0) | ((Index & 0xFFFFF) << 8);

        public static SeqRun Unpack(int v) => new SeqRun
        {
            Running = (v & 1) != 0,
            Queued = (v & (1 << 4)) != 0,
            Index = (v >> 8) & 0xFFFFF,
        };
    }

    internal sealed class SeqHost
    {
        public Func<int> Now;
        public int TicksPerSecond = 30;
        public Action<int, int> Begin;
        public Func<int, int, int, int> Poll;
        public Func<int, int, bool> LineTrue;
        public Action<int, string> Note;
        public string LastWhy = "";
    }

    internal static class SeqRunner
    {
        public const int RUNNING = 0, DONE = 1, REFUSED = 2;
        public const int MAX_ADVANCES = 32;

        public static bool Tick(SeqRun run, List<SeqStep> steps, bool trigger, bool queueOne, bool stopOnRefusal, int seq, SeqHost host)
        {
            bool changed = false;
            if (trigger) changed |= Trigger(run, steps, queueOne, seq, host);
            int[] partner = null;
            for (int guard = 0; guard < MAX_ADVANCES && run.Running; guard++)
            {
                if (steps == null || run.Index >= steps.Count)
                {
                    End(run);
                    changed = true;
                    break;
                }
                SeqStep s = steps[run.Index];
                if (IsFlow(s.Kind))
                {
                    if (partner == null) partner = Partners(steps);
                    changed = true;
                    if (Flow(run, steps, partner, s, seq, host)) break;
                    continue;
                }
                int now = host.Now();
                if (!run.Begun)
                {
                    run.Begun = true;
                    run.StartTick = now;
                    if (Acts(s.Kind)) host.Begin?.Invoke(seq, run.Index);
                    changed = true;
                }
                if (run.Together >= 0 && (s.Kind == SeqKind.Do || s.Kind == SeqKind.Command || s.Kind == SeqKind.Call))
                {
                    if (s.Kind == SeqKind.Do ? s.Settle : !s.NoWait) run.Pending.Add((run.Index, run.StartTick));
                    run.Index++;
                    run.Begun = false;
                    run.Why = "";
                    changed = true;
                    continue;
                }
                int status = Evaluate(run, s, seq, host, now, out bool timedOut);
                if (status == RUNNING) break;
                if (status == REFUSED)
                {
                    host.Note?.Invoke(seq, "step " + (run.Index + 1) + " " + (timedOut ? "timed out" : "refused") + (run.Why.Length > 0 ? " -- " + run.Why : ""));
                    if (stopOnRefusal)
                    {
                        End(run);
                        changed = true;
                        break;
                    }
                }
                run.Index++;
                run.Begun = false;
                run.Why = "";
                changed = true;
            }
            return changed;
        }

        private static bool IsFlow(SeqKind k) => k >= SeqKind.If && k <= SeqKind.Together;

        private static bool Acts(SeqKind k) => k == SeqKind.Command || k == SeqKind.Do || k == SeqKind.Call || k == SeqKind.MemSet || k == SeqKind.MemAdd;

        private static int[] Partners(List<SeqStep> steps)
        {
            var kinds = new List<int>(steps.Count);
            foreach (SeqStep st in steps) kinds.Add((int)st.Kind);
            return LogicFlow.Match(kinds);
        }

        private static bool Test(SeqStep s, int seq, SeqHost host) => (host.LineTrue != null && host.LineTrue(seq, s.Target.Pack())) != s.Not;

        private static int After(int[] partner, List<SeqStep> steps, int index)
        {
            int p = partner[index];
            if (p >= 0 && steps[p].Kind == SeqKind.Else) return p + 1;
            return p < 0 ? steps.Count : p + 1;
        }

        private static int PastEnd(int[] partner, List<SeqStep> steps, int index)
        {
            int p = partner[index];
            if (p >= 0 && steps[p].Kind == SeqKind.Else) p = partner[p];
            return p < 0 ? steps.Count : p + 1;
        }

        private static bool Flow(SeqRun run, List<SeqStep> steps, int[] partner, SeqStep s, int seq, SeqHost host)
        {
            int i = run.Index;
            run.Begun = false;
            run.Why = "";
            switch (s.Kind)
            {
                case SeqKind.If:
                    run.Index = Test(s, seq, host) ? i + 1 : After(partner, steps, i);
                    return false;
                case SeqKind.Else:
                    run.Index = partner[i] < 0 ? steps.Count : partner[i] + 1;
                    return false;
                case SeqKind.Repeat:
                    run.Counts[i] = 0;
                    run.Index = s.Pose > 0 ? i + 1 : PastEnd(partner, steps, i);
                    return false;
                case SeqKind.While:
                    run.Index = Test(s, seq, host) ? i + 1 : PastEnd(partner, steps, i);
                    return false;
                case SeqKind.Stop:
                    End(run);
                    return true;
                case SeqKind.Together:
                    if (run.Together < 0) { run.Together = i; run.Pending.Clear(); }
                    run.Index = i + 1;
                    return false;
                case SeqKind.End:
                {
                    int o = partner[i];
                    if (o >= 0 && steps[o].Kind == SeqKind.Together)
                    {
                        if (o != run.Together) { run.Index = i + 1; return false; }
                        if (WaitTogether(run, steps, seq, host)) return true;
                        run.Together = -1;
                        run.Pending.Clear();
                        run.Index = i + 1;
                        return false;
                    }
                    if (o >= 0 && steps[o].Kind == SeqKind.Repeat)
                    {
                        int done = (run.Counts.TryGetValue(o, out int c) ? c : 0) + 1;
                        if (done < steps[o].Pose)
                        {
                            run.Counts[o] = done;
                            run.Index = o + 1;
                            return true;
                        }
                        run.Counts.Remove(o);
                        run.Index = i + 1;
                        return false;
                    }
                    if (o >= 0 && steps[o].Kind == SeqKind.While)
                    {
                        run.Index = o;
                        return true;
                    }
                    run.Index = i + 1;
                    return false;
                }
            }
            run.Index = i + 1;
            return false;
        }

        private static bool WaitTogether(SeqRun run, List<SeqStep> steps, int seq, SeqHost host)
        {
            int now = host.Now();
            for (int p = run.Pending.Count - 1; p >= 0; p--)
            {
                var (index, start) = run.Pending[p];
                SeqStep s = index < steps.Count ? steps[index] : default;
                host.LastWhy = "";
                int status = host.Poll != null ? host.Poll(seq, index, start) : DONE;
                bool overTime = s.Timeout > 0 && now - start >= s.Timeout * host.TicksPerSecond;
                if (status == RUNNING && !overTime) continue;
                if (status != DONE) host.Note?.Invoke(seq, "statement " + (index + 1) + (overTime ? " timed out" : " refused") + " inside TOGETHER");
                run.Pending.RemoveAt(p);
            }
            if (run.Pending.Count == 0) return false;
            run.Why = "waiting for " + run.Pending.Count + " part(s)";
            return true;
        }

        private static bool Trigger(SeqRun run, List<SeqStep> steps, bool queueOne, int seq, SeqHost host)
        {
            if (steps == null || steps.Count == 0) return false;
            if (!run.Running)
            {
                run.Running = true;
                run.Index = 0;
                run.Counts.Clear();
                run.Together = -1;
                run.Pending.Clear();
                run.Begun = false;
                run.Why = "";
                return true;
            }
            if (queueOne)
            {
                if (run.Queued) return false;
                run.Queued = true;
                return true;
            }
            host.Note?.Invoke(seq, "trigger ignored -- the sequence is still running");
            return false;
        }

        public static void Stop(SeqRun run)
        {
            run.Queued = false;
            End(run);
        }

        private static void End(SeqRun run)
        {
            run.Counts.Clear();
            run.Together = -1;
            run.Pending.Clear();
            run.Running = run.Queued;
            run.Queued = false;
            run.Index = 0;
            run.Begun = false;
            run.Why = "";
        }

        private static int Evaluate(SeqRun run, SeqStep s, int seq, SeqHost host, int now, out bool timedOut)
        {
            timedOut = false;
            int elapsed = now - run.StartTick;
            bool overTime = s.Timeout > 0 && elapsed >= s.Timeout * host.TicksPerSecond;
            switch (s.Kind)
            {
                case SeqKind.Wait:
                {
                    int need = s.Seconds * host.TicksPerSecond / 10;
                    run.Why = "waiting " + Math.Max(0, (need - elapsed) / (float)host.TicksPerSecond).ToString("0.0") + " s";
                    return elapsed >= need ? DONE : RUNNING;
                }
                case SeqKind.WaitUntil:
                    host.LastWhy = "";
                    if (host.LineTrue != null && host.LineTrue(seq, s.Target.Pack())) return DONE;
                    if (overTime) { timedOut = true; run.Why = "waited " + s.Timeout + " s"; return REFUSED; }
                    run.Why = string.IsNullOrEmpty(host.LastWhy) ? "waiting for " + s.Target : host.LastWhy;
                    return RUNNING;
                case SeqKind.Command:
                case SeqKind.Do:
                case SeqKind.Call:
                {
                    if (s.Kind == SeqKind.Do ? !s.Settle : s.NoWait) return DONE;
                    host.LastWhy = "";
                    int poll = host.Poll != null ? host.Poll(seq, run.Index, run.StartTick) : DONE;
                    run.Why = host.LastWhy ?? "";
                    if (poll == RUNNING && overTime)
                    {
                        timedOut = true;
                        run.Why = "not settled after " + s.Timeout + " s" + (run.Why.Length > 0 ? " (" + run.Why + ")" : "");
                        return REFUSED;
                    }
                    return poll;
                }
                default:
                    return DONE;
            }
        }
    }

    internal static class SeqTargets
    {
        public static bool WaitAllowed(LogicRef target, int ownerRule, Func<int, int> lineOwner, Func<int, bool> isVariable)
        {
            if (target.IsRule && isVariable != null && isVariable(target.Index)) return true;
            return WaitAllowed(target, ownerRule, lineOwner);
        }

        public static bool WaitAllowed(LogicRef target, int ownerRule, Func<int, int> lineOwner)
        {
            if (target.IsRule) return target.Index != ownerRule;
            if (target.IsLocal) return ownerRule >= 1 && lineOwner != null && lineOwner(target.Index) == ownerRule;
            return false;
        }
    }
}
