using System;

namespace ZTX.HingesPistonsDocking
{
    internal static class MergeFaultInjection
    {
        public const int ARMED_AFTER_PARTS = 0;
        private static bool s_splitFired, s_mergeFired;

        public static void SplitMoved(int moved)
        {
            if (ARMED_AFTER_PARTS <= 0 || s_splitFired || moved < ARMED_AFTER_PARTS) return;
            s_splitFired = true;
            throw new InvalidOperationException("MergeFaultInjection: split fault after " + moved + " parts (test build).");
        }

        public static void MergeMoved(int moved)
        {
            if (ARMED_AFTER_PARTS <= 0 || s_mergeFired || moved < ARMED_AFTER_PARTS) return;
            s_mergeFired = true;
            throw new InvalidOperationException("MergeFaultInjection: merge fault after " + moved + " parts (test build).");
        }
    }
}
