namespace ZTX.HingesPistonsDocking
{
    internal static class RailGlobal
    {
        public static int Encode(bool on) => on ? 1 : 2;

        public static bool Decode(int stored) => stored == 1;

        public static bool UseTerminal(bool hasTerminal, int globalStored) => hasTerminal && Decode(globalStored);

        public static bool SeedOnSwitch(int globalStored, bool turningOn) => turningOn && globalStored == 0;
    }
}
