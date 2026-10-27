using System.Collections.Generic;

namespace ZTX.HingesPistonsDocking
{
    internal enum HandlerTrigger
    {
        WhenOn = 0,
        WhenOff = 1,
        WhileOn = 2,
        WhileOff = 3,
        Always = 4,
        OnCall = 5,
    }

    internal static class HandlerRun
    {
        public const int WHEN = 0, WHILE = 1, ALWAYS = 2, CALL = 3;

        public static HandlerTrigger Compose(int family, bool off) => family == WHEN ? (off ? HandlerTrigger.WhenOff : HandlerTrigger.WhenOn)
            : family == WHILE ? (off ? HandlerTrigger.WhileOff : HandlerTrigger.WhileOn) : family == CALL ? HandlerTrigger.OnCall : HandlerTrigger.Always;

        public static int Family(HandlerTrigger t) => t == HandlerTrigger.WhenOn || t == HandlerTrigger.WhenOff ? WHEN
            : t == HandlerTrigger.WhileOn || t == HandlerTrigger.WhileOff ? WHILE : t == HandlerTrigger.OnCall ? CALL : ALWAYS;

        public static bool NoVariable(HandlerTrigger t) => t == HandlerTrigger.Always || t == HandlerTrigger.OnCall;

        public static bool IsOff(HandlerTrigger t) => t == HandlerTrigger.WhenOff || t == HandlerTrigger.WhileOff;

        public static bool Repeats(HandlerTrigger t) => t == HandlerTrigger.WhileOn || t == HandlerTrigger.WhileOff || t == HandlerTrigger.Always;

        public static bool IsActive(HandlerTrigger t, bool cur) => t == HandlerTrigger.Always || (t == HandlerTrigger.WhileOn ? cur : t == HandlerTrigger.WhileOff && !cur);

        public static void Decide(HandlerTrigger t, bool primed, bool cur, bool prev, bool running, out bool start, out bool stop)
        {
            start = false;
            stop = false;
            switch (t)
            {
                case HandlerTrigger.WhenOn: start = primed && cur && !prev; return;
                case HandlerTrigger.WhenOff: start = primed && !cur && prev; return;
                case HandlerTrigger.OnCall: start = cur; return;
            }
            bool active = IsActive(t, cur);
            if (!active) { stop = running; return; }
            start = !running;
        }
    }

    internal static class HandlerConversion
    {
        public static HandlerTrigger TriggerFor(bool whenOff, bool hold) =>
            hold ? (whenOff ? HandlerTrigger.WhileOff : HandlerTrigger.WhileOn) : (whenOff ? HandlerTrigger.WhenOff : HandlerTrigger.WhenOn);

        public static List<(int Trigger, int Pos, List<int> Slots)> Plan(List<(int Slot, bool Sided, bool WhenOff, bool Hold, int Pos)> acts)
        {
            var sorted = new List<(int Slot, bool Sided, bool WhenOff, bool Hold, int Pos)>(acts);
            sorted.Sort((a, b) => a.Pos != b.Pos ? a.Pos.CompareTo(b.Pos) : a.Slot.CompareTo(b.Slot));
            var groups = new List<(int Trigger, int Pos, List<int> Slots)>();
            foreach (var a in sorted)
            {
                if (!a.Sided) continue;
                int trigger = (int)TriggerFor(a.WhenOff, a.Hold);
                int at = groups.FindIndex(g => g.Trigger == trigger);
                if (at < 0) groups.Add((trigger, a.Pos, new List<int> { a.Slot }));
                else groups[at].Slots.Add(a.Slot);
            }
            return groups;
        }
    }
}
