using System;
using System.Collections.Generic;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogThrottle
    {
        private const int REPEAT_EVERY = 1800;

        private static readonly Dictionary<string, int> s_counts = new Dictionary<string, int>();
        private static readonly object s_lock = new object();

        public static void Exception(string where, Exception ex)
        {
            string key = where + "|" + ex.GetType().FullName + "|" + ex.Message;
            int n;
            lock (s_lock)
            {
                s_counts.TryGetValue(key, out n);
                s_counts[key] = ++n;
            }
            if (n == 1)
                Log.Exception(where, ex);
            else if (n % REPEAT_EVERY == 0)
                Log.Error($"{where}: the same {ex.GetType().Name} again ({n} times so far): {ex.Message}");
        }

        public static void Info(string key, string line)
        {
            int n;
            lock (s_lock)
            {
                s_counts.TryGetValue("info|" + key, out n);
                s_counts["info|" + key] = ++n;
            }
            if (n == 1 || n % REPEAT_EVERY == 0) Log.Info(n == 1 ? line : line + $" ({n} times so far)");
        }

        public static void Reset()
        {
            lock (s_lock) s_counts.Clear();
        }
    }
}
