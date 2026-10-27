using System.Collections.Generic;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;

namespace ZTX.HingesPistonsDocking
{
    internal static class CommandGateBatch
    {
        private static int s_depth;
        private static readonly List<CommandManager> s_pending = new List<CommandManager>();
        private static readonly List<Ship> s_scratch = new List<Ship>();

        public static bool ShouldDefer(int depth, bool jointed) => depth > 0 && jointed;

        public static int Depth => s_depth;

        public static void Enter() => s_depth++;

        public static void Defer(CommandManager cm)
        {
            if (cm != null && !s_pending.Contains(cm)) s_pending.Add(cm);
        }

        public static void Exit(Ship a, Ship b)
        {
            if (s_depth > 0) s_depth--;
            if (s_depth > 0) return;
            var todo = new List<CommandManager>(s_pending);
            s_pending.Clear();
            if (a?.Commands != null) todo.Add(a.Commands);
            if (b?.Commands != null) todo.Add(b.Commands);
            var done = new HashSet<Ship>();
            foreach (CommandManager cm in todo)
            {
                Ship ship = cm?.Ship;
                if (ship == null || !cm.IsActive || done.Contains(ship)) continue;
                HingeJointManager.GetAssemblyShips(ship, s_scratch, rigidOnly: false, identity: true);
                done.Add(ship);
                foreach (Ship m in s_scratch) if (m != null) done.Add(m);
                try { cm.RefreshCanExecuteCommands(); }
                catch (System.Exception ex) { Log.Exception("CommandGateBatch.Exit", ex); }
            }
        }
    }
}
