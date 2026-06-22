using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscordScheduler
{
    public enum ReviewAction
    {
        Inspect,
        Retry,
        Dismiss
    }

    public sealed class ReviewEvidence
    {
        public string lastAttemptAtUtcIso = "";
        public string lastDiscordMessageId = "";
        public string lastError = "";
        public int retries;
        public string latestAttemptOutcome = "";
        public int latestAttemptStatusCode;
        public string latestAttemptError = "";
        public string mediaStatus = "";
    }

    public sealed class ReviewQueueItem
    {
        public string postId = "";
        public string title = "";
        public string targetId = "";
        public string targetName = "";
        public string reason = "";
        public bool inspectAllowed = true;
        public bool retryAllowed;
        public bool dismissAllowed;
        public string retryBlockedReason = "";
        public ReviewEvidence evidence = new ReviewEvidence();
    }

    public sealed class ReviewActionPlan
    {
        public ReviewAction action;
        public bool allowed;
        public string postId = "";
        public string message = "";
        public bool requiresConfirmation;
        public string targetStatus = "";
    }

    public sealed class ReviewQueueService
    {
        private readonly WebhookUrlValidator _webhookUrlValidator;
        private readonly IWebhookSecretResolver _webhookSecretResolver;

        public ReviewQueueService(WebhookUrlValidator webhookUrlValidator = null, IWebhookSecretResolver webhookSecretResolver = null)
        {
            _webhookUrlValidator = webhookUrlValidator ?? new WebhookUrlValidator();
            _webhookSecretResolver = webhookSecretResolver ?? new WebhookSecretResolver();
        }

        public IReadOnlyList<ReviewQueueItem> Build(AppDatabase db, IReadOnlyList<SendAttemptRecord> recentAttempts, SendQueueService sendQueue = null)
        {
            var items = new List<ReviewQueueItem>();
            if (db?.posts == null)
                return items;

            var attempts = recentAttempts ?? Array.Empty<SendAttemptRecord>();
            foreach (var post in db.posts.Where(post => post != null && post.status == PostStatus.NeedsReview).OrderBy(post => post.lastAttemptAtUtcIso ?? "").ThenBy(post => post.id ?? ""))
            {
                var target = FindTarget(db, post.targetId);
                var retry = EvaluateRetry(post, target, sendQueue);
                var latestAttempt = attempts.LastOrDefault(attempt => attempt != null && string.Equals(attempt.postId ?? "", post.id ?? "", StringComparison.Ordinal));

                items.Add(new ReviewQueueItem
                {
                    postId = post.id ?? "",
                    title = post.title ?? "",
                    targetId = post.targetId ?? "",
                    targetName = target?.name ?? "Missing target",
                    reason = string.IsNullOrWhiteSpace(post.lastError) ? "Manual review required." : SecretRedactor.RedactAndTruncate(post.lastError, WebhookErrorClassifier.MaxShortErrorLength),
                    retryAllowed = retry.ok,
                    retryBlockedReason = retry.ok ? "" : retry.error,
                    dismissAllowed = sendQueue == null || (!sendQueue.IsQueued(post.id) && !sendQueue.IsActive(post.id)),
                    evidence = BuildEvidence(post, latestAttempt)
                });
            }

            return items;
        }

        public ReviewActionPlan EvaluateAction(AppDatabase db, string postId, ReviewAction action, SendQueueService sendQueue = null)
        {
            var plan = new ReviewActionPlan { action = action, postId = postId ?? "" };
            var post = db?.posts?.FirstOrDefault(item => item != null && string.Equals(item.id ?? "", postId ?? "", StringComparison.Ordinal));
            if (post == null)
            {
                plan.message = "Post does not exist.";
                return plan;
            }

            if (action == ReviewAction.Inspect)
            {
                plan.allowed = true;
                plan.message = "Open the post details before deciding.";
                return plan;
            }

            if (post.status != PostStatus.NeedsReview)
            {
                plan.message = "Post is not in NeedsReview.";
                return plan;
            }

            if (sendQueue != null && (sendQueue.IsQueued(post.id) || sendQueue.IsActive(post.id)))
            {
                plan.message = "Post is already queued or active.";
                return plan;
            }

            if (action == ReviewAction.Dismiss)
            {
                plan.allowed = true;
                plan.requiresConfirmation = true;
                plan.message = "Dismiss keeps the post but clears the manual review decision path.";
                return plan;
            }

            var target = FindTarget(db, post.targetId);
            var retry = EvaluateRetry(post, target, sendQueue);
            plan.allowed = retry.ok;
            plan.message = retry.ok ? "Retry can requeue this post after confirmation." : retry.error;
            plan.requiresConfirmation = retry.ok;
            plan.targetStatus = target == null ? "missing" : "present";
            return plan;
        }

        private ReviewEvidence BuildEvidence(ScheduledPost post, SendAttemptRecord latestAttempt)
        {
            return new ReviewEvidence
            {
                lastAttemptAtUtcIso = post.lastAttemptAtUtcIso ?? "",
                lastDiscordMessageId = post.lastDiscordMessageId ?? "",
                lastError = SecretRedactor.RedactAndTruncate(post.lastError, WebhookErrorClassifier.MaxShortErrorLength),
                retries = post.retries,
                latestAttemptOutcome = latestAttempt?.outcome.ToString() ?? "",
                latestAttemptStatusCode = latestAttempt?.statusCode ?? 0,
                latestAttemptError = SecretRedactor.RedactAndTruncate(latestAttempt?.shortError, WebhookErrorClassifier.MaxShortErrorLength),
                mediaStatus = BuildMediaStatus(post)
            };
        }

        private ValidationResult EvaluateRetry(ScheduledPost post, Target target, SendQueueService sendQueue)
        {
            if (post == null)
                return ValidationResult.Fail("Missing post.");

            if (sendQueue != null && (sendQueue.IsQueued(post.id) || sendQueue.IsActive(post.id)))
                return ValidationResult.Fail("Post is already queued or active.");

            if (target == null)
                return ValidationResult.Fail("Target is missing.");

            var secretResolution = _webhookSecretResolver.Resolve(target);
            if (!secretResolution.ok)
                return ValidationResult.Fail(secretResolution.error);

            var webhook = _webhookUrlValidator.Validate(secretResolution.webhookUrl);
            if (!webhook.ok)
                return ValidationResult.Fail(webhook.error);

            return ValidationResult.Ok();
        }

        private static Target FindTarget(AppDatabase db, string targetId)
        {
            return db?.targets?.FirstOrDefault(target => target != null && string.Equals(target.id ?? "", targetId ?? "", StringComparison.Ordinal));
        }

        private static string BuildMediaStatus(ScheduledPost post)
        {
            var path = post?.EffectiveMediaPath() ?? "";
            if (string.IsNullOrWhiteSpace(path))
                return "No attachment.";

            if (FileUtil.IsManagedAttachmentPath(path))
                return "Managed attachment.";

            return "External attachment path.";
        }
    }
}
