namespace DiscordScheduler
{
    public sealed class MutationImpactReport
    {
        public int linkedPostCount;
        public int pendingPostCount;
        public int queuedPostCount;
        public int activePostCount;
        public int needsReviewPostCount;
        public bool webhookChanged;
        public string message;

        public bool HasBlockedPosts()
        {
            return queuedPostCount > 0 || activePostCount > 0 || pendingPostCount > 0 || needsReviewPostCount > 0;
        }
    }
}
