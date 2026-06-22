namespace DiscordScheduler
{
    public struct WebhookTransportResponse
    {
        public bool ok;
        public int statusCode;
        public string body;
        public float retryAfterSeconds;
        public string retryAfterSource;
        public bool rateLimitGlobal;
        public string rateLimitScope;
        public bool requestMayHaveReachedDiscord;
        public string errorKind;
    }
}
