using System;
using System.Collections.Generic;
using Cosmoteer.Ships;
using Halfling.Geometry;
using Halfling.Graphics;

namespace ZTX.HingesPistonsDocking
{
    internal static class BayText
    {
        public const float DURATION = 3f;
        public const float RISE = 40f;
        public const int REPEAT_MS = 3000;

        private static readonly Dictionary<string, int> s_shownAt = new Dictionary<string, int>();

        public static bool ShowAt(Ship ship, string kind, string text, Color color, Vector2 world, int repeatMs = REPEAT_MS)
        {
            if (ship?.Sim?.Game?.Gui == null || !ship.Commands.IsCommandableByLocalPlayer) return false;
            int now = Environment.TickCount;
            bool shown = s_shownAt.TryGetValue(kind, out int last);
            if (!BayDesign.TellDue(shown, last, now, repeatMs)) return false;
            s_shownAt[kind] = now;
            ship.Sim.Game.Gui.CreateFloatingText(text, world, color, DURATION, new Vector2(0f, -RISE));
            return true;
        }

        public static bool Show(Ship ship, string kind, string text, Color color, int repeatMs = REPEAT_MS)
        {
            if (ship?.Sim?.Game?.Gui == null || !ship.Commands.IsCommandableByLocalPlayer) return false;
            int now = Environment.TickCount;
            bool shown = s_shownAt.TryGetValue(kind, out int last);
            if (!BayDesign.TellDue(shown, last, now, repeatMs)) return false;
            s_shownAt[kind] = now;
            ship.Sim.Game.Gui.CreateFloatingText(text, ship.Sim.WorldMouseLoc, color, DURATION, new Vector2(0f, -RISE));
            return true;
        }
    }
}
