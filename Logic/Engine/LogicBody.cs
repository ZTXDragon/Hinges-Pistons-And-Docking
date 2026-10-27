namespace ZTX.HingesPistonsDocking
{
    internal static class LogicBody
    {
        public static bool IsSignal(bool hasLines, bool hasCode) => hasLines && !hasCode;
    }
}
