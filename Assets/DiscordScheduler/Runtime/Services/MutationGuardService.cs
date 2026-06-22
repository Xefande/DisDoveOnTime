using System;
using System.Linq;

namespace DiscordScheduler
{
    public sealed class MutationGuardService
    {
        private readonly SendQueueService _sendQueue;
        private readonly TargetRevisionPolicy _targetRevisionPolicy;

        public MutationGuardService(SendQueueService sendQueue, TargetRevisionPolicy targetRevisionPolicy)
        {
            _sendQueue = sendQueue ?? throw new ArgumentNullException(nameof(sendQueue));
            _targetRevisionPolicy = targetRevisionPolicy ?? throw new ArgumentNullException(nameof(targetRevisionPolicy));
        }

        public ValidationResult CanEditPost(ScheduledPost post)
        {
            if (post == null)
                return ValidationResult.Fail("Missing post.");

            if (_sendQueue.IsQueued(post.id))
                return ValidationResult.Fail("Cannot edit a queued post. Cancel/requeue before editing.");

            if (_sendQueue.IsActive(post.id) || post.status == PostStatus.Sending)
                return ValidationResult.Fail("Cannot edit a post while it is actively sending.");

            return ValidationResult.Ok();
        }

        public ValidationResult CanDeletePost(ScheduledPost post)
        {
            if (post == null)
                return ValidationResult.Fail("Missing post.");

            if (_sendQueue.IsQueued(post.id))
                return ValidationResult.Fail("Cannot delete a queued post. Cancel/requeue before deleting.");

            if (_sendQueue.IsActive(post.id) || post.status == PostStatus.Sending)
                return ValidationResult.Fail("Cannot delete a post while it is actively sending.");

            return ValidationResult.Ok();
        }

        public ValidationResult CanSaveTarget(Target target, TargetDraft draft, AppDatabase db, out MutationImpactReport report)
        {
            report = BuildTargetImpact(target, draft, db);
            if (target == null || draft == null)
                return ValidationResult.Ok();

            if (!report.webhookChanged)
                return ValidationResult.Ok();

            if (report.HasBlockedPosts())
                return ValidationResult.Fail(report.message);

            return ValidationResult.Ok();
        }

        public MutationImpactReport BuildTargetImpact(Target target, TargetDraft draft, AppDatabase db)
        {
            var report = new MutationImpactReport();
            if (target == null || draft == null || db?.posts == null)
                return report;

            report.webhookChanged = _targetRevisionPolicy.IsWebhookChanged(target, draft);
            var targetId = target.id ?? "";
            var linked = db.posts
                .Where(post => post != null && string.Equals(post.targetId ?? "", targetId, StringComparison.Ordinal))
                .ToList();

            report.linkedPostCount = linked.Count;
            report.pendingPostCount = linked.Count(post => post.status == PostStatus.Pending);
            report.queuedPostCount = linked.Count(post => _sendQueue.IsQueued(post.id));
            report.activePostCount = linked.Count(post =>
                _sendQueue.IsActive(post.id) ||
                (post.status == PostStatus.Sending && !_sendQueue.IsQueued(post.id)));
            report.needsReviewPostCount = linked.Count(post => post.status == PostStatus.NeedsReview);
            report.message = "Cannot change webhook URL while linked posts need an explicit impact decision. " +
                             $"linked={report.linkedPostCount}, pending={report.pendingPostCount}, queued={report.queuedPostCount}, " +
                             $"active={report.activePostCount}, needsReview={report.needsReviewPostCount}.";
            return report;
        }
    }
}
