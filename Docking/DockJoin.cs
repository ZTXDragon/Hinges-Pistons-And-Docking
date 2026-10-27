using System;
using System.Collections.Generic;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.UI;

namespace ZTX.HingesPistonsDocking
{
    internal static class DockJoin
    {
        public static readonly ID<PartToggleGuiRules> JoinToggleId = new ID<PartToggleGuiRules>("ztx_dock_join");

        public const int SEPARATE = 0, JOIN = 1, MERGE = 2;

        public static bool Joined(bool isPad, bool joinA, bool joinB) => isPad ? joinA : joinA && joinB;

        public static int PairMode(int a, int b) => Math.Max(SEPARATE, Math.Min(MERGE, Math.Min(a, b)));

        public static bool Merges(int a, int b) => PairMode(a, b) == MERGE;

        public static bool AdoptsMergedPair(bool sameShip, bool facesBack, int a, int b) => sameShip && facesBack && Merges(a, b);

        public static int ModeOf(Part dockPart)
        {
            PartUIToggle t = dockPart != null ? PartUIToggle.GetOnPart(dockPart, JoinToggleId) : null;
            return t == null ? SEPARATE : Math.Max(SEPARATE, Math.Min(MERGE, t.Mode));
        }

        public static List<ulong> PilotCandidates(List<ulong> cluster, ISet<ulong> visitors)
        {
            var result = new List<ulong>();
            foreach (ulong id in cluster)
                if (visitors == null || !visitors.Contains(id)) result.Add(id);
            return result.Count > 0 ? result : new List<ulong>(cluster);
        }

        public static bool IsJoinOn(Part dockPart)
        {
            PartUIToggle t = dockPart != null ? PartUIToggle.GetOnPart(dockPart, JoinToggleId) : null;
            return t != null && t.Mode != 0;
        }
    }
}
