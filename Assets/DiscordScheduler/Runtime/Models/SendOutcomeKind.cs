namespace DiscordScheduler
{
    public enum SendOutcomeKind
    {
        Success,
        RateLimited,
        RetryableTransient,
        Ambiguous,
        NonRetryable
    }
}
