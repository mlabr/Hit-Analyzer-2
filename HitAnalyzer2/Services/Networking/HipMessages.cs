namespace HitAnalyzer2.Services.Networking
{
    internal static class HipMessages
    {
        public static string CreateTest(string sid) =>
            $"HA-0200-{sid}-Hello world!";
    }
}
