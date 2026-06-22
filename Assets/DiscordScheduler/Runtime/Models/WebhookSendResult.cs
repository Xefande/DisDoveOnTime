namespace DiscordScheduler
{
    public sealed class WebhookSendResult
    {
        public bool ok;
        public int statusCode;
        public SendOutcomeKind outcome;
        public string shortError;
        public string discordMessageId;
        public float retryAfterSeconds;
        public string retryAfterSource;
        public bool rateLimitGlobal;
        public string rateLimitScope;
        public bool requestMayHaveReachedDiscord;
        public string errorKind;

        public bool IsAmbiguous()
        {
            return outcome == SendOutcomeKind.Ambiguous;
        }
    }
}
