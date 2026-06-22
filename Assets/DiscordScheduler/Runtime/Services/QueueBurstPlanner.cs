using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscordScheduler
{
    public enum QueueBurstItemStatus
    {
        Sendable,
        Deferred,
        Blocked,
        NeedsReview,
        AlreadyQueuedOrActive
    }

    public sealed class QueueBurstItem
    {
        public string postId = "";
        public string targetId = "";
        public QueueBurstItemStatus status;
        public string reason = "";
        public string orderingKey = "";
    }

    public sealed class QueueBurstPlan
    {
        public int total;
        public int sendableCount;
        public int deferredCount;
        public int blockedCount;
        public int needsReviewCount;
        public List<QueueBurstItem> items = new List<QueueBurstItem>();
        public string summary = "";
    }

    public sealed class QueueBurstPlanner
    {
        private readonly WebhookUrlValidator _webhookUrlValidator;
        private readonly IWebhookSecretResolver _webhookSecretResolver;

        public QueueBurstPlanner(WebhookUrlValidator webhookUrlValidator = null, IWebhookSecretResolver webhookSecretResolver = null)
        {
            _webhookUrlValidator = webhookUrlValidator ?? new WebhookUrlValidator();
            _webhookSecretResolver = webhookSecretResolver ?? new WebhookSecretResolver();
        }

        public QueueBurstPlan Build(AppDatabase db, IReadOnlyList<ScheduledPost> duePosts, DateTime nowUtc, SendQueueService sendQueue = null, RateLimitRegistry rateLimitRegistry = null)
        {
            var plan = new QueueBurstPlan();
            var ordered = (duePosts ?? Array.Empty<ScheduledPost>())
                .Where(post => post != null)
                .OrderBy(post => SortDate(post.scheduledAtUtcIso))
                .ThenBy(post => SortText(post.createdAtUtcIso))
                .ThenBy(post => SortText(post.id))
                .ToList();

            foreach (var post in ordered)
            {
                var item = BuildItem(db, post, nowUtc, sendQueue, rateLimitRegistry);
                plan.items.Add(item);
                Count(plan, item.status);
            }

            plan.total = plan.items.Count;
            plan.summary = $"due={plan.total}, sendable={plan.sendableCount}, deferred={plan.deferredCount}, blocked={plan.blockedCount}, needsReview={plan.needsReviewCount}.";
            return plan;
        }

        private QueueBurstItem BuildItem(AppDatabase db, ScheduledPost post, DateTime nowUtc, SendQueueService sendQueue, RateLimitRegistry rateLimitRegistry)
        {
            var item = new QueueBurstItem
            {
                postId = post.id ?? "",
                targetId = post.targetId ?? "",
                orderingKey = SortDate(post.scheduledAtUtcIso).ToString("O") + "|" + SortText(post.createdAtUtcIso) + "|" + SortText(post.id)
            };

            if (sendQueue != null && sendQueue.IsQueuedOrActive(post.id))
                return Set(item, QueueBurstItemStatus.AlreadyQueuedOrActive, "Post is already queued or active.");

            if (post.status == PostStatus.NeedsReview)
                return Set(item, QueueBurstItemStatus.NeedsReview, "Post already needs manual review.");

            if (post.status != PostStatus.Pending)
                return Set(item, QueueBurstItemStatus.Blocked, "Post is not pending.");

            if (!string.IsNullOrWhiteSpace(post.nextAttemptAtUtcIso) &&
                TimeUtil.TryParseIsoUtc(post.nextAttemptAtUtcIso, out var nextAttemptUtc) &&
                nextAttemptUtc > nowUtc)
            {
                return Set(item, QueueBurstItemStatus.Deferred, "Post retry is waiting until " + post.nextAttemptAtUtcIso + ".");
            }

            var target = db?.targets?.FirstOrDefault(candidate => candidate != null && string.Equals(candidate.id ?? "", post.targetId ?? "", StringComparison.Ordinal));
            if (target == null)
                return Set(item, QueueBurstItemStatus.Blocked, "Target is missing.");

            var secretResolution = _webhookSecretResolver.Resolve(target);
            if (!secretResolution.ok)
                return Set(item, QueueBurstItemStatus.Blocked, secretResolution.error);

            var webhook = _webhookUrlValidator.Validate(secretResolution.webhookUrl);
            if (!webhook.ok)
                return Set(item, QueueBurstItemStatus.Blocked, webhook.error);

            var rateLimit = rateLimitRegistry?.CanSend(target.id, nowUtc) ?? ValidationResult.Ok();
            if (!rateLimit.ok)
                return Set(item, QueueBurstItemStatus.Deferred, rateLimit.error);

            return Set(item, QueueBurstItemStatus.Sendable, "Ready.");
        }

        private static QueueBurstItem Set(QueueBurstItem item, QueueBurstItemStatus status, string reason)
        {
            item.status = status;
            item.reason = SecretRedactor.RedactAndTruncate(reason, WebhookErrorClassifier.MaxShortErrorLength);
            return item;
        }

        private static void Count(QueueBurstPlan plan, QueueBurstItemStatus status)
        {
            switch (status)
            {
                case QueueBurstItemStatus.Sendable:
                    plan.sendableCount++;
                    break;
                case QueueBurstItemStatus.Deferred:
                    plan.deferredCount++;
                    break;
                case QueueBurstItemStatus.NeedsReview:
                    plan.needsReviewCount++;
                    break;
                default:
                    plan.blockedCount++;
                    break;
            }
        }

        private static DateTime SortDate(string iso)
        {
            return TimeUtil.TryParseIsoUtc(iso, out var parsed) ? parsed : DateTime.MaxValue;
        }

        private static string SortText(string value)
        {
            return value ?? "";
        }
    }
}
