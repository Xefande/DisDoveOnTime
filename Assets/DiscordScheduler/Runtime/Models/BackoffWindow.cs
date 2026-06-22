namespace DiscordScheduler
{
    public sealed class BackoffWindow
    {
        public string scope = "";
        public string targetId = "";
        public string startedAtUtcIso = "";
        public string untilUtcIso = "";
        public float retryAfterSeconds;
        public string reason = "";
    }
}
