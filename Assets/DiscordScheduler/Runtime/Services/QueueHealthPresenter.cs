using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscordScheduler
{
    public sealed class QueueHealthViewModel
    {
        public bool hasBackoff;
        public string headline = "";
        public string summary = "";
        public string nextAttemptUtcIso = "";
        public string waitReason = "";
        public string disabledRetryReason = "";
        public int affectedPostCount;
        public List<QueueHealthRow> rows = new List<QueueHealthRow>();
    }

    public sealed class QueueHealthRow
    {
        public string label = "";
        public string value = "";
        public string severity = "";
    }

    public sealed class QueueHealthPresenter
    {
        public QueueHealthViewModel Build(AppDatabase db, SendQueueService sendQueue, RateLimitRegistry rateLimitRegistry, DateTime nowUtc)
        {
            var viewModel = new QueueHealthViewModel();
            var pendingBackoffPosts = db?.posts?
                .Where(post => post != null && post.status == PostStatus.Pending && IsFuture(post.nextAttemptAtUtcIso, nowUtc))
                .OrderBy(post => TimeUtil.ParseIsoUtc(post.nextAttemptAtUtcIso))
                .ThenBy(post => post.id ?? "")
                .ToList() ?? new List<ScheduledPost>();

            viewModel.affectedPostCount = pendingBackoffPosts.Count;
            viewModel.hasBackoff = pendingBackoffPosts.Count > 0 || (rateLimitRegistry?.ActiveBackoffCount(nowUtc) ?? 0) > 0;
            viewModel.headline = viewModel.hasBackoff ? "Queue is waiting" : "Queue ready";
            viewModel.summary = BuildSummary(sendQueue, pendingBackoffPosts.Count, rateLimitRegistry, nowUtc);

            if (pendingBackoffPosts.Count > 0)
            {
                var first = pendingBackoffPosts[0];
                viewModel.nextAttemptUtcIso = first.nextAttemptAtUtcIso ?? "";
                viewModel.waitReason = SecretRedactor.RedactAndTruncate(first.lastError, WebhookErrorClassifier.MaxShortErrorLength);
                viewModel.disabledRetryReason = "Manual retry is disabled until " + viewModel.nextAttemptUtcIso + ".";
                AddRow(viewModel, "Next retry", viewModel.nextAttemptUtcIso, "warning");
            }

            AddRow(viewModel, "Queued", (sendQueue?.Count ?? 0).ToString(), "info");
            AddRow(viewModel, "Active", (sendQueue != null && sendQueue.HasActiveSend ? 1 : 0).ToString(), "info");
            AddRow(viewModel, "Backoff posts", pendingBackoffPosts.Count.ToString(), pendingBackoffPosts.Count > 0 ? "warning" : "info");
            return viewModel;
        }

        private static string BuildSummary(SendQueueService sendQueue, int backoffPostCount, RateLimitRegistry registry, DateTime nowUtc)
        {
            var activeBackoffs = registry?.ActiveBackoffCount(nowUtc) ?? 0;
            if (backoffPostCount <= 0 && activeBackoffs <= 0)
                return "No active Discord wait state.";

            return $"Waiting: posts={backoffPostCount}, registry={activeBackoffs}, queued={sendQueue?.Count ?? 0}.";
        }

        private static bool IsFuture(string iso, DateTime nowUtc)
        {
            return !string.IsNullOrWhiteSpace(iso) &&
                   TimeUtil.TryParseIsoUtc(iso, out var nextAttemptUtc) &&
                   nextAttemptUtc > nowUtc;
        }

        private static void AddRow(QueueHealthViewModel viewModel, string label, string value, string severity)
        {
            viewModel.rows.Add(new QueueHealthRow
            {
                label = label ?? "",
                value = value ?? "",
                severity = severity ?? "info"
            });
        }
    }
}
