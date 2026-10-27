using System.Collections.Generic;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogicLayoutState
    {
        public static float PaneWidth = float.NaN;
        public static bool SideCollapsed;
        public static float SummaryHeight = float.NaN;
        public static bool SummaryClosed, SummaryCollapsed;
        public static bool PartsClosed, PartsCollapsed;
        public static bool RulesPopped, SummaryPopped, PartsPopped;
        public static readonly float[] UiScales = { 1f, 1f, 1f };
        public static readonly HashSet<string> HiddenTypes = new HashSet<string>();
    }
}
