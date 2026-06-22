using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DiscordScheduler.Tests
{
    public sealed class TrustLayerProjectionContractTests
    {
        private static readonly DateTime FixedNow = new DateTime(2026, 6, 22, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void TargetHealth_InvalidWebhook_ReportsBlockerWithoutRawToken()
        {
            var db = DbWithTargets(Target("t1", "Broken", "https://example.com/api/webhooks/123456789012345678/secret-token"));

            var report = new TargetHealthService().Build(db, "t1");

            Assert.That(report.exists, Is.True);
            Assert.That(report.webhookOk, Is.False);
            Assert.That(report.blocker, Does.Contain("Discord webhook"));
            Assert.That(report.maskedWebhook, Does.Not.Contain("secret-token"));
        }

        [Test]
        public void TargetHealth_MutationPreview_SplitsPendingQueuedActiveSentAndNeedsReview()
        {
            var db = DbWithTargets(Target("t1", "Main"));
            var queued = Post("queued", "t1", PostStatus.Pending, 0);
            var active = Post("active", "t1", PostStatus.Sending, 1);
            db.posts.Add(Post("pending", "t1", PostStatus.Pending, 2));
            db.posts.Add(queued);
            db.posts.Add(active);
            db.posts.Add(Post("sent", "t1", PostStatus.Sent, 3));
            db.posts.Add(Post("review", "t1", PostStatus.NeedsReview, 4));
            var queue = Queue();
            Assert.That(queue.TryEnqueue(queued).ok, Is.True);
            queue.MarkActive("active");

            var report = new TargetHealthService().PreviewMutation(db, "t1", TargetMutationKind.EditWebhook, queue);

            Assert.That(report.linkedPostCount, Is.EqualTo(5));
            Assert.That(report.pendingPostCount, Is.EqualTo(1));
            Assert.That(report.queuedPostCount, Is.EqualTo(1));
            Assert.That(report.activePostCount, Is.EqualTo(1));
            Assert.That(report.sentPostCount, Is.EqualTo(1));
            Assert.That(report.needsReviewPostCount, Is.EqualTo(1));
            Assert.That(report.hasMutationImpact, Is.True);
            Assert.That(report.impactSummary, Does.Contain("queued=1"));
        }

        [Test]
        public void TargetApplication_DeletePreview_ReportsLinkedQueuedAndActiveWithoutMutating()
        {
            var db = DbWithTargets(Target("t1", "Main"));
            var queued = Post("queued", "t1", PostStatus.Pending, 0);
            var active = Post("active", "t1", PostStatus.Sending, 1);
            db.posts.Add(queued);
            db.posts.Add(active);
            var queue = Queue();
            Assert.That(queue.TryEnqueue(queued).ok, Is.True);
            queue.MarkActive("active");
            var app = new TargetApplicationService(new WebhookUrlValidator(), queue);

            var result = app.PreviewDeleteTarget(db.targets[0], db, out var preview);

            Assert.That(result.ok, Is.True);
            Assert.That(preview.deletedPostCount, Is.EqualTo(2));
            Assert.That(preview.cancelledQueuedPostCount, Is.EqualTo(1));
            Assert.That(preview.blockedActivePostCount, Is.EqualTo(1));
            Assert.That(db.targets.Count, Is.EqualTo(1));
            Assert.That(db.posts.Count, Is.EqualTo(2));
        }

        [Test]
        public void TargetApplication_SaveImpactPreview_UsesMutationGuardCounts()
        {
            var db = DbWithTargets(Target("t1", "Main"));
            db.posts.Add(Post("pending", "t1", PostStatus.Pending, 0));
            var queue = Queue();
            var app = new TargetApplicationService(new WebhookUrlValidator(), queue, new MutationGuardService(queue, new TargetRevisionPolicy()));
            var draft = new TargetDraft
            {
                id = "t1",
                name = "Main",
                webhookUrl = "https://discord.com/api/webhooks/123456789012345678/rotated-token"
            };

            var preview = app.PreviewSaveImpact(db.targets[0], draft, db);
            var save = app.SaveTarget(db.targets[0], draft, db);

            Assert.That(preview.webhookChanged, Is.True);
            Assert.That(preview.pendingPostCount, Is.EqualTo(1));
            Assert.That(save.ok, Is.False);
            Assert.That(save.error, Does.Contain("linked=1"));
        }

        [Test]
        public void ReviewQueue_BuildsEvidenceAndAllowsRetryWhenTargetIsValid()
        {
            var db = DbWithTargets(Target("t1", "Main"));
            var post = Post("review", "t1", PostStatus.NeedsReview, 0);
            post.lastError = "timeout NeedsReview required.";
            post.retries = 2;
            post.lastAttemptAtUtcIso = TimeUtil.ToIsoUtc(FixedNow.AddMinutes(-1));
            post.lastDiscordMessageId = "123456789012345678";
            db.posts.Add(post);
            var attempts = new List<SendAttemptRecord>
            {
                new SendAttemptRecord
                {
                    postId = "review",
                    outcome = SendOutcomeKind.Ambiguous,
                    statusCode = 0,
                    shortError = "timeout"
                }
            };

            var items = new ReviewQueueService().Build(db, attempts, Queue());

            Assert.That(items.Count, Is.EqualTo(1));
            Assert.That(items[0].retryAllowed, Is.True);
            Assert.That(items[0].dismissAllowed, Is.True);
            Assert.That(items[0].evidence.lastDiscordMessageId, Is.EqualTo("123456789012345678"));
            Assert.That(items[0].evidence.latestAttemptOutcome, Is.EqualTo(SendOutcomeKind.Ambiguous.ToString()));
        }

        [Test]
        public void ReviewQueue_RetryMissingTarget_IsBlockedButInspectAndDismissAreExplicit()
        {
            var db = new AppDatabase();
            db.posts.Add(Post("review", "missing", PostStatus.NeedsReview, 0));
            var service = new ReviewQueueService();

            var retry = service.EvaluateAction(db, "review", ReviewAction.Retry, Queue());
            var inspect = service.EvaluateAction(db, "review", ReviewAction.Inspect, Queue());
            var dismiss = service.EvaluateAction(db, "review", ReviewAction.Dismiss, Queue());

            Assert.That(retry.allowed, Is.False);
            Assert.That(retry.message, Does.Contain("Target is missing"));
            Assert.That(inspect.allowed, Is.True);
            Assert.That(dismiss.allowed, Is.True);
            Assert.That(dismiss.requiresConfirmation, Is.True);
        }

        [Test]
        public void QueueHealth_BackoffPost_ShowsNextAttemptAndDisabledRetryReason()
        {
            var db = DbWithTargets(Target("t1", "Main"));
            var post = Post("p1", "t1", PostStatus.Pending, 0);
            post.nextAttemptAtUtcIso = TimeUtil.ToIsoUtc(FixedNow.AddMinutes(5));
            post.lastError = "HTTP 429";
            db.posts.Add(post);

            var viewModel = new QueueHealthPresenter().Build(db, Queue(), new RateLimitRegistry(), FixedNow);

            Assert.That(viewModel.hasBackoff, Is.True);
            Assert.That(viewModel.headline, Is.EqualTo("Queue is waiting"));
            Assert.That(viewModel.nextAttemptUtcIso, Is.EqualTo(post.nextAttemptAtUtcIso));
            Assert.That(viewModel.disabledRetryReason, Does.Contain(post.nextAttemptAtUtcIso));
        }

        [Test]
        public void QueueBurst_TargetBackoffDefersOnlyThatTargetAndKeepsDeterministicOrder()
        {
            var db = DbWithTargets(Target("t1", "One"), Target("t2", "Two"));
            var backoffPost = Post("a-backoff", "t1", PostStatus.Pending, 0);
            var sendablePost = Post("b-sendable", "t2", PostStatus.Pending, 1);
            db.posts.Add(backoffPost);
            db.posts.Add(sendablePost);
            var registry = new RateLimitRegistry();
            registry.RecordTargetBackoff("t1", FixedNow, 60, "HTTP 429");

            var plan = new QueueBurstPlanner().Build(db, db.posts, FixedNow, Queue(), registry);

            Assert.That(plan.total, Is.EqualTo(2));
            Assert.That(plan.deferredCount, Is.EqualTo(1));
            Assert.That(plan.sendableCount, Is.EqualTo(1));
            Assert.That(plan.items[0].postId, Is.EqualTo("a-backoff"));
            Assert.That(plan.items[0].status, Is.EqualTo(QueueBurstItemStatus.Deferred));
            Assert.That(plan.items[1].postId, Is.EqualTo("b-sendable"));
            Assert.That(plan.items[1].status, Is.EqualTo(QueueBurstItemStatus.Sendable));
        }

        [Test]
        public void QueueBurst_MissingTarget_IsBlockedWithHealthReason()
        {
            var db = new AppDatabase();
            var post = Post("missing-target", "missing", PostStatus.Pending, 0);
            db.posts.Add(post);

            var plan = new QueueBurstPlanner().Build(db, db.posts, FixedNow, Queue(), new RateLimitRegistry());

            Assert.That(plan.blockedCount, Is.EqualTo(1));
            Assert.That(plan.items[0].status, Is.EqualTo(QueueBurstItemStatus.Blocked));
            Assert.That(plan.items[0].reason, Does.Contain("Target is missing"));
            Assert.That(plan.summary, Does.Contain("blocked=1"));
        }

        private static AppDatabase DbWithTargets(params Target[] targets)
        {
            var db = new AppDatabase();
            db.targets.AddRange(targets);
            return db;
        }

        private static Target Target(string id, string name, string webhookUrl = null)
        {
            return new Target
            {
                id = id,
                name = name,
                webhookUrl = webhookUrl ?? "https://discord.com/api/webhooks/123456789012345678/test-token"
            };
        }

        private static ScheduledPost Post(string id, string targetId, PostStatus status, int minuteOffset)
        {
            return new ScheduledPost
            {
                id = id,
                targetId = targetId,
                title = id,
                status = status,
                scheduledAtUtcIso = TimeUtil.ToIsoUtc(FixedNow.AddMinutes(minuteOffset)),
                createdAtUtcIso = TimeUtil.ToIsoUtc(FixedNow.AddMinutes(-10 + minuteOffset)),
                updatedAtUtcIso = "",
                lastError = "",
                lastAttemptAtUtcIso = "",
                nextAttemptAtUtcIso = "",
                lastDiscordMessageId = ""
            };
        }

        private static SendQueueService Queue()
        {
            return new SendQueueService(new PostStateMachine(new FixedTimeProvider(FixedNow)));
        }

        private sealed class FixedTimeProvider : ITimeProvider
        {
            public FixedTimeProvider(DateTime utcNow)
            {
                UtcNow = utcNow;
            }

            public DateTime UtcNow { get; }
        }
    }
}
