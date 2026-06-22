using System;
using System.Linq;

namespace DiscordScheduler
{
    public enum TargetMutationKind
    {
        EditWebhook,
        Delete
    }

    public sealed class TargetHealthReport
    {
        public string targetId = "";
        public string targetName = "";
        public bool exists;
        public bool webhookOk;
        public string maskedWebhook = "";
        public string blocker = "";
        public int linkedPostCount;
        public int pendingPostCount;
        public int queuedPostCount;
        public int activePostCount;
        public int sentPostCount;
        public int failedPostCount;
        public int missedPostCount;
        public int needsReviewPostCount;
        public bool hasMutationImpact;
        public string impactSummary = "";
    }

    public sealed class TargetHealthService
    {
        private readonly WebhookUrlValidator _webhookUrlValidator;
        private readonly IWebhookSecretResolver _webhookSecretResolver;

        public TargetHealthService(WebhookUrlValidator webhookUrlValidator = null, IWebhookSecretResolver webhookSecretResolver = null)
        {
            _webhookUrlValidator = webhookUrlValidator ?? new WebhookUrlValidator();
            _webhookSecretResolver = webhookSecretResolver ?? new WebhookSecretResolver();
        }

        public TargetHealthReport Build(AppDatabase db, string targetId, SendQueueService sendQueue = null)
        {
            var report = new TargetHealthReport { targetId = targetId ?? "" };
            var target = db?.targets?.FirstOrDefault(item => item != null && string.Equals(item.id ?? "", targetId ?? "", StringComparison.Ordinal));
            if (target == null)
            {
                report.blocker = "Target does not exist.";
                return report;
            }

            report.exists = true;
            report.targetName = target.name ?? "";

            var secretResolution = _webhookSecretResolver.Resolve(target);
            if (!secretResolution.ok)
            {
                report.maskedWebhook = string.IsNullOrWhiteSpace(target.webhookSecretRef) ? "" : "protected:" + MaskSecretRef(target.webhookSecretRef);
                report.webhookOk = false;
                report.blocker = secretResolution.error;
            }
            else
            {
                report.maskedWebhook = _webhookUrlValidator.Mask(secretResolution.webhookUrl);
                var webhook = _webhookUrlValidator.Validate(secretResolution.webhookUrl);
                report.webhookOk = webhook.ok;
                if (!webhook.ok)
                    report.blocker = webhook.error;
            }

            CountLinkedPosts(report, db, target.id, sendQueue);
            report.hasMutationImpact = report.pendingPostCount > 0 ||
                                       report.queuedPostCount > 0 ||
                                       report.activePostCount > 0 ||
                                       report.needsReviewPostCount > 0;
            report.impactSummary = BuildImpactSummary(report);
            return report;
        }

        public TargetHealthReport PreviewMutation(AppDatabase db, string targetId, TargetMutationKind kind, SendQueueService sendQueue = null)
        {
            var report = Build(db, targetId, sendQueue);
            if (!report.exists)
                return report;

            report.hasMutationImpact = report.linkedPostCount > 0 || report.hasMutationImpact;
            var action = kind == TargetMutationKind.Delete ? "Deleting" : "Changing webhook";
            report.impactSummary = action + " impacts " + report.linkedPostCount + " linked post(s): " +
                                   BuildImpactSummary(report);
            return report;
        }

        private static void CountLinkedPosts(TargetHealthReport report, AppDatabase db, string targetId, SendQueueService sendQueue)
        {
            if (db?.posts == null)
                return;

            var linked = db.posts
                .Where(post => post != null && string.Equals(post.targetId ?? "", targetId ?? "", StringComparison.Ordinal))
                .ToList();

            report.linkedPostCount = linked.Count;
            report.pendingPostCount = linked.Count(post => post.status == PostStatus.Pending);
            report.queuedPostCount = linked.Count(post => sendQueue != null && sendQueue.IsQueued(post.id));
            report.activePostCount = linked.Count(post =>
                (sendQueue != null && sendQueue.IsActive(post.id)) ||
                (post.status == PostStatus.Sending && (sendQueue == null || !sendQueue.IsQueued(post.id))));
            report.sentPostCount = linked.Count(post => post.status == PostStatus.Sent);
            report.failedPostCount = linked.Count(post => post.status == PostStatus.Failed);
            report.missedPostCount = linked.Count(post => post.status == PostStatus.Missed);
            report.needsReviewPostCount = linked.Count(post => post.status == PostStatus.NeedsReview);
        }

        private static string BuildImpactSummary(TargetHealthReport report)
        {
            return $"linked={report.linkedPostCount}, pending={report.pendingPostCount}, queued={report.queuedPostCount}, " +
                   $"active={report.activePostCount}, sent={report.sentPostCount}, failed={report.failedPostCount}, " +
                   $"missed={report.missedPostCount}, needsReview={report.needsReviewPostCount}.";
        }

        private static string MaskSecretRef(string secretRef)
        {
            var value = (secretRef ?? "").Trim();
            if (value.Length <= 8)
                return "***";

            return value.Substring(0, 4) + "***" + value.Substring(value.Length - 4);
        }
    }
}
