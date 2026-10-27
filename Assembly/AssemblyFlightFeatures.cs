namespace ZTX.HingesPistonsDocking
{
    internal static class AssemblyFlightFeatures
    {
        public static bool Enabled { get; private set; } = true;

        public static void Toggle()
        {
            Enabled = !Enabled;
            Log.Info("Assembly flight integration " + (Enabled ? "ON" : "OFF (pure vanilla + joints)") + ".");
        }

        public static void Reset()
        {
            Enabled = true;
        }
    }
}
