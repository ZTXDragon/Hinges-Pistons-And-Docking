using System.Collections.Generic;

namespace ZTX.HingesPistonsDocking
{
    internal static class PartWindowFit
    {
        public static float Height(float chrome, float paddingY, IEnumerable<float> shownHeights, float max)
        {
            float need = chrome;
            int n = 0;
            foreach (float h in shownHeights)
            {
                need += h;
                n++;
            }
            if (n > 1) need += (n - 1) * paddingY;
            return max > 0f && need > max ? max : need;
        }

        public static float TextHeight(string text, float rowHeight)
        {
            if (string.IsNullOrEmpty(text)) return rowHeight;
            int lines = 1;
            string body = text.TrimEnd('\n');
            foreach (char c in body)
            {
                if (c == '\n') lines++;
            }
            return rowHeight * lines;
        }

        public static string EntryKey(string[] keys, int mode)
        {
            return keys != null && mode >= 0 && mode < keys.Length ? keys[mode] : null;
        }
    }
}
