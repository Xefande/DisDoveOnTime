using System;
using System.Linq;

namespace DiscordScheduler
{
    public sealed class OperationalHealthService
    {
        public HealthSnapshot BuildSnapshot(
            AppDatabase db,
            SendQueueService sendQueue,
            RateLimitRegistry rateLimitRegistry,
            DateTime utcNow,
            string saveWarning,
            string journalWarning,
            string lockWarning,
            string retentionWarning)
        {
            var snapshot = new HealthSnapshot();
            snapshot.targetCount = db?.targets?.Count ?? 0;
            snapshot.postCount = db?.posts?.Count ?? 0;
            snapshot.pendingCount = db?.posts?.Count(post => post != null && post.status == PostStatus.Pending) ?? 0;
            snapshot.needsReviewCount = db?.posts?.Count(post => post != null && post.status == PostStatus.NeedsReview) ?? 0;
            snapshot.queuedCount = sendQueue?.Count ?? 0;
            snapshot.activeSendCount = sendQueue != null && sendQueue.HasActiveSend ? 1 : 0;
            snapshot.activeBackoffCount = rateLimitRegistry?.ActiveBackoffCount(utcNow) ?? 0;
            snapshot.saveWarning = Clean(saveWarning);
            snapshot.journalWarning = Clean(journalWarning);
            snapshot.lockWarning = Clean(lockWarning);
            snapshot.retentionWarning = Clean(retentionWarning);

            snapshot.warningCount = CountWarnings(snapshot);
            snapshot.hasBlocker = !string.IsNullOrWhiteSpace(snapshot.saveWarning) ||
                                  !string.IsNullOrWhiteSpace(snapshot.lockWarning);

            snapshot.summary = BuildSummary(snapshot);
            return snapshot;
        }

        private static int CountWarnings(HealthSnapshot snapshot)
        {
            var count = 0;
            if (!string.IsNullOrWhiteSpace(snapshot.saveWarning)) count++;
            if (!string.IsNullOrWhiteSpace(snapshot.journalWarning)) count++;
            if (!string.IsNullOrWhiteSpace(snapshot.lockWarning)) count++;
            if (!string.IsNullOrWhiteSpace(snapshot.retentionWarning)) count++;
            if (snapshot.needsReviewCount > 0) count++;
            if (snapshot.activeBackoffCount > 0) count++;
            return count;
        }

        private static string BuildSummary(HealthSnapshot snapshot)
        {
            var summary = $"Targets: {snapshot.targetCount} | Posts: {snapshot.postCount} | Pending: {snapshot.pendingCount}";

            if (snapshot.needsReviewCount > 0)
                summary += $" | NeedsReview: {snapshot.needsReviewCount}";

            if (snapshot.queuedCount > 0 || snapshot.activeSendCount > 0)
                summary += $" | Queue: {snapshot.queuedCount} active: {snapshot.activeSendCount}";

            if (snapshot.activeBackoffCount > 0)
                summary += $" | Backoff: {snapshot.activeBackoffCount}";

            if (snapshot.warningCount > 0)
                summary += $" | Warnings: {snapshot.warningCount}";

            return summary;
        }

        private static string Clean(string value)
        {
            return SecretRedactor.RedactAndTruncate(value, WebhookErrorClassifier.MaxShortErrorLength);
        }
    }
}
