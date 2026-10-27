using System;
using System.Reflection;
using HarmonyLib;

namespace ZTX.HingesPistonsDocking
{
    internal static class Log
    {
        public const string VERSION = "v1.0.0";
        private const string Prefix = "[ZTX.HingesDock " + VERSION + "] ";

        public const bool VERBOSE = false;
        private static MethodInfo s_log;
        private static bool s_resolved;

        private static MethodInfo GetLogMethod()
        {
            if (s_resolved) return s_log;
            s_resolved = true;
            var loggerType = AccessTools.TypeByName("Halfling.Logging.Logger");
            if (loggerType != null)
            {
                s_log = AccessTools.Method(loggerType, "Log", new[] { typeof(string) });
            }
            return s_log;
        }

        private static readonly object s_lock = new object();

        public static void Info(string msg)
        {
            try
            {
                var m = GetLogMethod();
                if (m != null) lock (s_lock) { m.Invoke(null, new object[] { Prefix + msg }); }
            }
            catch { }
        }

        public static void Error(string msg) => Info("ERROR: " + msg);

        public static void Exception(string context, Exception ex)
        {
            if (ex == null)
            {
                Error(context + " threw: <null exception>");
                return;
            }
            var inner = ex.InnerException ?? ex;
            string where = "";
            try
            {
                var st = new System.Diagnostics.StackTrace(inner, true);
                var frames = st.GetFrames();
                if (frames != null)
                {
                    int shown = 0;
                    foreach (var f in frames)
                    {
                        var mth = f.GetMethod();
                        if (mth == null) continue;
                        where += (shown == 0 ? " at " : " <- ") + mth.DeclaringType?.Name + "." + mth.Name;
                        int line = f.GetFileLineNumber();
                        if (line > 0) where += ":" + line;
                        if (++shown >= 4) break;
                    }
                }
            }
            catch { }
            Error(context + " threw: " + inner.GetType().Name + ": " + inner.Message + where);
        }
    }
}
