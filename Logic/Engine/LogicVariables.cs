using System;
using System.Collections.Generic;
using System.Globalization;

namespace ZTX.HingesPistonsDocking
{
    internal static class LogicVariables
    {
        public static List<string> UniqueNames(IList<string> names, int maxChars)
        {
            var all = new HashSet<string>();
            foreach (string n in names) all.Add(n ?? "");
            var seen = new HashSet<string>();
            var result = new List<string>(names.Count);
            foreach (string raw in names)
            {
                string n = raw ?? "";
                if (seen.Add(n)) { result.Add(n); continue; }
                for (int k = 2; ; k++)
                {
                    string candidate = WithNumber(n, k, maxChars);
                    if (all.Contains(candidate) || seen.Contains(candidate)) continue;
                    all.Add(candidate);
                    seen.Add(candidate);
                    result.Add(candidate);
                    break;
                }
            }
            return result;
        }

        public static string FreeName(IList<string> taken, string stem, int maxChars)
        {
            var used = new HashSet<string>(taken);
            for (int k = 1; ; k++)
            {
                string candidate = WithNumber(stem, k, maxChars);
                if (!used.Contains(candidate)) return candidate;
            }
        }

        private static string WithNumber(string stem, int k, int maxChars)
        {
            string suffix = k.ToString(CultureInfo.InvariantCulture);
            if (stem.Length + suffix.Length > maxChars) stem = stem.Substring(0, Math.Max(0, maxChars - suffix.Length));
            return stem + suffix;
        }
    }
}
