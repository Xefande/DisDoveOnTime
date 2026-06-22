using System;
using System.Linq;

namespace DiscordScheduler
{
    public sealed class TargetApplicationService
    {
        private readonly WebhookUrlValidator _webhookValidator;
        private readonly SendQueueService _sendQueue;
        private readonly MutationGuardService _mutationGuard;

        public TargetApplicationService(WebhookUrlValidator webhookValidator, SendQueueService sendQueue, MutationGuardService mutationGuard = null)
        {
            _webhookValidator = webhookValidator ?? throw new ArgumentNullException(nameof(webhookValidator));
            _sendQueue = sendQueue ?? throw new ArgumentNullException(nameof(sendQueue));
            _mutationGuard = mutationGuard;
        }

        public ValidationResult CreateTarget(AppDatabase db, out Target target)
        {
            target = null;
            var ready = ValidateDatabase(db);
            if (!ready.ok)
                return ready;

            target = Target.CreateNew();
            db.targets.Add(target);
            return ValidationResult.Ok();
        }

        public ValidationResult SaveTarget(Target target, TargetDraft draft, AppDatabase db)
        {
            if (target == null)
                return ValidationResult.Fail("Target not selected.");

            var ready = ValidateDatabase(db);
            if (!ready.ok)
                return ready;

            if (!db.targets.Any(existing => existing != null && string.Equals(existing.id, target.id, StringComparison.Ordinal)))
                return ValidationResult.Fail("Target does not exist.");

            var valid = ValidateDraft(draft);
            if (!valid.ok)
                return valid;

            if (_mutationGuard != null)
            {
                var mutation = _mutationGuard.CanSaveTarget(target, draft, db, out _);
                if (!mutation.ok)
                    return mutation;
            }

            var nextWebhookUrl = (draft.webhookUrl ?? "").Trim();
            var webhookChanged = !string.Equals((target.webhookUrl ?? "").Trim(), nextWebhookUrl, StringComparison.Ordinal);

            target.name = draft.name.Trim();
            target.serverLabel = (draft.serverLabel ?? "").Trim();
            target.channelLabel = (draft.channelLabel ?? "").Trim();
            target.webhookUrl = nextWebhookUrl;
            if (webhookChanged)
                target.webhookSecretRef = "";
            target.overrideUsername = (draft.overrideUsername ?? "").Trim();
            target.overrideAvatarUrl = (draft.overrideAvatarUrl ?? "").Trim();

            return ValidationResult.Ok();
        }

        public MutationImpactReport PreviewSaveImpact(Target target, TargetDraft draft, AppDatabase db)
        {
            if (_mutationGuard == null)
                return new MutationImpactReport();

            return _mutationGuard.BuildTargetImpact(target, draft, db);
        }

        public ValidationResult PreviewDeleteTarget(Target target, AppDatabase db, out TargetDeleteResult deleteResult)
        {
            deleteResult = new TargetDeleteResult();

            if (target == null)
                return ValidationResult.Fail("Target not selected.");

            var ready = ValidateDatabase(db);
            if (!ready.ok)
                return ready;

            var targetId = (target.id ?? "").Trim();
            if (string.IsNullOrWhiteSpace(targetId))
                return ValidationResult.Fail("Target id is missing.");

            var linkedPosts = db.posts
                .Where(post => post != null && string.Equals(post.targetId, targetId, StringComparison.Ordinal))
                .ToList();

            deleteResult.targetId = targetId;
            deleteResult.deletedPostCount = linkedPosts.Count;
            deleteResult.cancelledQueuedPostCount = linkedPosts.Count(post => _sendQueue.IsQueued(post.id));
            deleteResult.blockedActivePostCount = linkedPosts.Count(post => _sendQueue.IsActive(post.id));

            return ValidationResult.Ok();
        }

        public ValidationResult DeleteTarget(Target target, AppDatabase db, out TargetDeleteResult deleteResult)
        {
            deleteResult = new TargetDeleteResult();

            if (target == null)
                return ValidationResult.Fail("Target not selected.");

            var ready = ValidateDatabase(db);
            if (!ready.ok)
                return ready;

            var targetId = (target.id ?? "").Trim();
            if (string.IsNullOrWhiteSpace(targetId))
                return ValidationResult.Fail("Target id is missing.");

            var targetIndex = db.targets.FindIndex(existing => existing != null && string.Equals(existing.id, targetId, StringComparison.Ordinal));
            if (targetIndex < 0)
                return ValidationResult.Fail("Target does not exist.");

            var linkedPosts = db.posts
                .Where(post => post != null && string.Equals(post.targetId, targetId, StringComparison.Ordinal))
                .ToList();

            deleteResult.targetId = targetId;
            deleteResult.blockedActivePostCount = linkedPosts.Count(post => _sendQueue.IsActive(post.id));
            if (deleteResult.blockedActivePostCount > 0)
                return ValidationResult.Fail("Cannot delete target while a linked post is actively sending.");

            foreach (var post in linkedPosts)
            {
                if (_sendQueue.CancelQueued(post.id))
                    deleteResult.cancelledQueuedPostCount++;
            }

            deleteResult.deletedPostCount = db.posts.RemoveAll(post => post != null && string.Equals(post.targetId, targetId, StringComparison.Ordinal));
            db.targets.RemoveAt(targetIndex);
            return ValidationResult.Ok();
        }

        private static ValidationResult ValidateDatabase(AppDatabase db)
        {
            if (db == null || db.targets == null || db.posts == null)
                return ValidationResult.Fail("Target database is not available.");

            return ValidationResult.Ok();
        }

        private ValidationResult ValidateDraft(TargetDraft draft)
        {
            if (draft == null)
                return ValidationResult.Fail("Missing target draft.");

            if (string.IsNullOrWhiteSpace(draft.name))
                return ValidationResult.Fail("Target name is required.");

            return _webhookValidator.Validate(draft.webhookUrl);
        }
    }
}
