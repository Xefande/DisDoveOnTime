namespace DiscordScheduler
{
    public sealed class HealthSnapshot
    {
        public bool hasBlocker;
        public string summary = "";
        public int targetCount;
        public int postCount;
        public int pendingCount;
        public int needsReviewCount;
        public int queuedCount;
        public int activeSendCount;
        public int activeBackoffCount;
        public int warningCount;
        public string saveWarning = "";
        public string journalWarning = "";
        public string lockWarning = "";
        public string retentionWarning = "";
    }
}
