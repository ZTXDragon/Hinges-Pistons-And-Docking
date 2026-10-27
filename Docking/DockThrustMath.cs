using System.Collections.Generic;

namespace ZTX.HingesPistonsDocking
{
    internal static class DockThrustMath
    {
        private const int CELL_OFFSET = 4096, CELL_SPAN = 8192;

        public static int CarrierSide(float massA, float massB, int idCompare)
        {
            if (massA > massB) return 0;
            if (massB > massA) return 1;
            return idCompare <= 0 ? 0 : 1;
        }

        public static List<T> DockedSide<T>(Dictionary<T, List<T>> jointAdj, T docked, T carrier)
        {
            return HingeJointManager.ParentSide(jointAdj, docked, new HashSet<T> { carrier });
        }

        public static bool SameOwner(int playerA, int playerB) => playerA == playerB;

        public static int EncodeCell(int x, int y) => (x + CELL_OFFSET) * CELL_SPAN + (y + CELL_OFFSET) + 1;

        public static bool TryDecodeCell(int stored, out int x, out int y)
        {
            x = y = 0;
            if (stored <= 0) return false;
            int v = stored - 1;
            x = v / CELL_SPAN - CELL_OFFSET;
            y = v % CELL_SPAN - CELL_OFFSET;
            return true;
        }
    }
}
