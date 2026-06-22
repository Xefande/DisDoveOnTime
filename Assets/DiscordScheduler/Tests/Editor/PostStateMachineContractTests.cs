using System;
using NUnit.Framework;

namespace DiscordScheduler.Tests
{
    public sealed class PostStateMachineContractTests
    {
        private static readonly DateTime FixedNow = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void CanQueue_Null_ReturnsFail()
        {
            var result = CreateStateMachine().CanQueueAt(null, FixedNow);

            Assert.That(result.ok, Is.False);
            Assert.That(result.error, Does.Contain("Missing post"));
        }

        [Test]
        public void CanQueue_FutureNextAttempt_ReturnsFail()
        {
            var post = PendingPost();
            post.nextAttemptAtUtcIso = TimeUtil.ToIsoUtc(FixedNow.AddSeconds(30));

            var result = CreateStateMachine().CanQueueAt(post, FixedNow);

            Assert.That(result.ok, Is.False);
            Assert.That(result.error, Does.Contain("not due"));
        }

        [Test]
        public void CanQueue_InvalidNextAttempt_ReturnsFail()
        {
            var post = PendingPost();
            post.nextAttemptAtUtcIso = "not-a-date";

            var result = CreateStateMachine().CanQueueAt(post, FixedNow);

            Assert.That(result.ok, Is.False);
            Assert.That(result.error, Does.Contain("invalid"));
        }

        [Test]
        public void CanQueue_NextAttemptAtNow_ReturnsOk()
        {
            var post = PendingPost();
            post.nextAttemptAtUtcIso = TimeUtil.ToIsoUtc(FixedNow);

            var result = CreateStateMachine().CanQueueAt(post, FixedNow);

            Assert.That(result.ok, Is.True);
        }

        [Test]
        public void CanQueue_PastNextAttempt_ReturnsOk()
        {
            var post = PendingPost();
            post.nextAttemptAtUtcIso = TimeUtil.ToIsoUtc(FixedNow.AddSeconds(-1));

            var result = CreateStateMachine().CanQueueAt(post, FixedNow);

            Assert.That(result.ok, Is.True);
        }

        [Test]
        public void TryMarkSending_Pending_SetsSendingAndTimestamp()
        {
            var post = PendingPost();

            var result = CreateStateMachine().TryMarkSending(post);

            Assert.That(result.ok, Is.True);
            Assert.That(post.status, Is.EqualTo(PostStatus.Sending));
            Assert.That(post.updatedAtUtcIso, Is.EqualTo(TimeUtil.ToIsoUtc(FixedNow)));
        }

        [TestCase(PostStatus.Sending)]
        [TestCase(PostStatus.Sent)]
        [TestCase(PostStatus.Failed)]
        [TestCase(PostStatus.Missed)]
        [TestCase(PostStatus.NeedsReview)]
        public void TryMarkSending_NonPending_ReturnsFail(PostStatus status)
        {
            var post = PendingPost();
            post.status = status;

            var result = CreateStateMachine().TryMarkSending(post);

            Assert.That(result.ok, Is.False);
            Assert.That(post.status, Is.EqualTo(status));
        }

        [Test]
        public void MarkSent_Sending_SetsSentClearsErrorAndStoresMessageId()
        {
            var post = SendingPost();
            post.lastError = "old error";
            post.nextAttemptAtUtcIso = TimeUtil.ToIsoUtc(FixedNow.AddMinutes(1));

            var result = CreateStateMachine().MarkSent(post, new WebhookSendResult
            {
                discordMessageId = "123456789012345678"
            });

            Assert.That(result.ok, Is.True);
            Assert.That(post.status, Is.EqualTo(PostStatus.Sent));
            Assert.That(post.lastError, Is.Empty);
            Assert.That(post.nextAttemptAtUtcIso, Is.Empty);
            Assert.That(post.lastAttemptAtUtcIso, Is.EqualTo(TimeUtil.ToIsoUtc(FixedNow)));
            Assert.That(post.lastDiscordMessageId, Is.EqualTo("123456789012345678"));
        }

        [Test]
        public void MarkSent_NotSending_DoesNotSilentlyMutate()
        {
            var post = PendingPost();

            var result = CreateStateMachine().MarkSent(post, new WebhookSendResult());

            Assert.That(result.ok, Is.False);
            Assert.That(post.status, Is.EqualTo(PostStatus.Pending));
            Assert.That(post.lastAttemptAtUtcIso, Is.Empty);
        }

        [Test]
        public void MarkSendFailedOrRetry_TransientUnderLimit_ReturnsPending()
        {
            var post = SendingPost();

            var result = CreateStateMachine(maxRetries: 3).MarkSendFailedOrRetry(post, new WebhookSendResult
            {
                outcome = SendOutcomeKind.RetryableTransient,
                shortError = "temporary"
            });

            Assert.That(result.ok, Is.True);
            Assert.That(post.status, Is.EqualTo(PostStatus.Pending));
            Assert.That(post.retries, Is.EqualTo(1));
            Assert.That(post.lastError, Is.EqualTo("temporary"));
            Assert.That(post.nextAttemptAtUtcIso, Is.EqualTo(TimeUtil.ToIsoUtc(FixedNow.AddSeconds(2))));
        }

        [Test]
        public void MarkSendFailedOrRetry_TransientAtLimit_ReturnsFailed()
        {
            var post = SendingPost();
            post.retries = 1;

            var result = CreateStateMachine(maxRetries: 2).MarkSendFailedOrRetry(post, new WebhookSendResult
            {
                outcome = SendOutcomeKind.RetryableTransient,
                shortError = "temporary"
            });

            Assert.That(result.ok, Is.True);
            Assert.That(post.status, Is.EqualTo(PostStatus.Failed));
            Assert.That(post.retries, Is.EqualTo(2));
            Assert.That(post.nextAttemptAtUtcIso, Is.Empty);
        }

        [Test]
        public void MarkSendFailedOrRetry_RateLimited_SetsPersistedNextAttempt()
        {
            var post = SendingPost();

            var result = CreateStateMachine(maxRetries: 3).MarkSendFailedOrRetry(post, new WebhookSendResult
            {
                outcome = SendOutcomeKind.RateLimited,
                retryAfterSeconds = 13f,
                shortError = "rate limited"
            });

            Assert.That(result.ok, Is.True);
            Assert.That(post.status, Is.EqualTo(PostStatus.Pending));
            Assert.That(post.nextAttemptAtUtcIso, Is.EqualTo(TimeUtil.ToIsoUtc(FixedNow.AddSeconds(13))));
        }

        [Test]
        public void MarkSendFailedOrRetry_NonRetryable_ReturnsFailedNoRetry()
        {
            var post = SendingPost();

            var result = CreateStateMachine().MarkSendFailedOrRetry(post, new WebhookSendResult
            {
                outcome = SendOutcomeKind.NonRetryable,
                shortError = "HTTP 403"
            });

            Assert.That(result.ok, Is.True);
            Assert.That(post.status, Is.EqualTo(PostStatus.Failed));
            Assert.That(post.nextAttemptAtUtcIso, Is.Empty);
        }

        [TestCase(PostStatus.Pending)]
        [TestCase(PostStatus.Sending)]
        public void MarkFailedByPolicy_PendingOrSending_MarksFailedAndClearsBackoff(PostStatus status)
        {
            var post = PendingPost();
            post.status = status;
            post.nextAttemptAtUtcIso = TimeUtil.ToIsoUtc(FixedNow.AddMinutes(3));

            var result = CreateStateMachine().MarkFailedByPolicy(post, "Policy blocked send.");

            Assert.That(result.ok, Is.True);
            Assert.That(post.status, Is.EqualTo(PostStatus.Failed));
            Assert.That(post.lastError, Is.EqualTo("Policy blocked send."));
            Assert.That(post.nextAttemptAtUtcIso, Is.Empty);
            Assert.That(post.updatedAtUtcIso, Is.EqualTo(TimeUtil.ToIsoUtc(FixedNow)));
        }

        [Test]
        public void DeferPendingUntil_Sending_ReturnsPendingWithRetryEvidence()
        {
            var post = SendingPost();
            var nextAttempt = FixedNow.AddMinutes(10);

            var result = CreateStateMachine().DeferPendingUntil(post, nextAttempt, "Global Discord backoff.");

            Assert.That(result.ok, Is.True);
            Assert.That(post.status, Is.EqualTo(PostStatus.Pending));
            Assert.That(post.nextAttemptAtUtcIso, Is.EqualTo(TimeUtil.ToIsoUtc(nextAttempt)));
            Assert.That(post.lastError, Is.EqualTo("Global Discord backoff."));
            Assert.That(post.updatedAtUtcIso, Is.EqualTo(TimeUtil.ToIsoUtc(FixedNow)));
        }

        [Test]
        public void DeferPendingUntil_TerminalPost_ReturnsFailWithoutMutation()
        {
            var post = PendingPost();
            post.status = PostStatus.Sent;
            post.nextAttemptAtUtcIso = "";

            var result = CreateStateMachine().DeferPendingUntil(post, FixedNow.AddMinutes(5), "Backoff");

            Assert.That(result.ok, Is.False);
            Assert.That(post.status, Is.EqualTo(PostStatus.Sent));
            Assert.That(post.nextAttemptAtUtcIso, Is.Empty);
        }

        [Test]
        public void MarkSendFailedOrRetry_Ambiguous_ReturnsNeedsReviewNoRetry()
        {
            var post = SendingPost();
            post.nextAttemptAtUtcIso = TimeUtil.ToIsoUtc(FixedNow.AddMinutes(1));

            var result = CreateStateMachine().MarkSendFailedOrRetry(post, new WebhookSendResult
            {
                outcome = SendOutcomeKind.Ambiguous,
                shortError = "timeout"
            });

            Assert.That(result.ok, Is.True);
            Assert.That(post.status, Is.EqualTo(PostStatus.NeedsReview));
            Assert.That(post.nextAttemptAtUtcIso, Is.Empty);
            Assert.That(post.lastError, Does.Contain("NeedsReview"));
        }

        [Test]
        public void TryMarkPendingManual_SentRequiresConfirmation()
        {
            var post = PendingPost();
            post.status = PostStatus.Sent;

            var result = CreateStateMachine().TryMarkPendingManual(post, resetRetries: true);

            Assert.That(result.ok, Is.False);
            Assert.That(post.status, Is.EqualTo(PostStatus.Sent));
        }

        [Test]
        public void TryMarkPendingManual_Reset_ClearsRetryFields()
        {
            var post = PendingPost();
            post.status = PostStatus.Failed;
            post.retries = 3;
            post.lastError = "old";
            post.lastAttemptAtUtcIso = TimeUtil.ToIsoUtc(FixedNow.AddMinutes(-5));
            post.nextAttemptAtUtcIso = TimeUtil.ToIsoUtc(FixedNow.AddMinutes(5));

            var result = CreateStateMachine().TryMarkPendingManual(post, resetRetries: true);

            Assert.That(result.ok, Is.True);
            Assert.That(post.status, Is.EqualTo(PostStatus.Pending));
            Assert.That(post.retries, Is.Zero);
            Assert.That(post.lastError, Is.Empty);
            Assert.That(post.lastAttemptAtUtcIso, Is.Empty);
            Assert.That(post.nextAttemptAtUtcIso, Is.Empty);
        }

        [Test]
        public void TryMarkPendingManual_NoReset_KeepsRetryEvidence()
        {
            var post = PendingPost();
            post.status = PostStatus.Failed;
            post.retries = 3;
            post.lastError = "old";
            post.lastAttemptAtUtcIso = TimeUtil.ToIsoUtc(FixedNow.AddMinutes(-5));
            post.nextAttemptAtUtcIso = TimeUtil.ToIsoUtc(FixedNow.AddMinutes(5));

            var result = CreateStateMachine().TryMarkPendingManual(post, resetRetries: false);

            Assert.That(result.ok, Is.True);
            Assert.That(post.status, Is.EqualTo(PostStatus.Pending));
            Assert.That(post.retries, Is.EqualTo(3));
            Assert.That(post.lastError, Is.EqualTo("old"));
            Assert.That(post.lastAttemptAtUtcIso, Is.EqualTo(TimeUtil.ToIsoUtc(FixedNow.AddMinutes(-5))));
            Assert.That(post.nextAttemptAtUtcIso, Is.Empty);
        }

        [Test]
        public void TryMarkPendingManual_AllowSent_RequeuesOnlyWithExplicitConfirmation()
        {
            var post = PendingPost();
            post.status = PostStatus.Sent;
            post.retries = 2;
            post.lastError = "old";

            var result = CreateStateMachine().TryMarkPendingManual(post, resetRetries: true, allowSent: true);

            Assert.That(result.ok, Is.True);
            Assert.That(post.status, Is.EqualTo(PostStatus.Pending));
            Assert.That(post.retries, Is.Zero);
            Assert.That(post.lastError, Is.Empty);
        }

        [Test]
        public void MarkMissed_Pending_SetsMissedAndStoresReason()
        {
            var post = PendingPost();

            var result = CreateStateMachine().MarkMissed(post, "Missed while app was offline.");

            Assert.That(result.ok, Is.True);
            Assert.That(post.status, Is.EqualTo(PostStatus.Missed));
            Assert.That(post.lastError, Is.EqualTo("Missed while app was offline."));
            Assert.That(post.updatedAtUtcIso, Is.EqualTo(TimeUtil.ToIsoUtc(FixedNow)));
        }

        [Test]
        public void DismissNeedsReview_MarksFailedAndKeepsEvidence()
        {
            var post = PendingPost();
            post.status = PostStatus.NeedsReview;
            post.lastDiscordMessageId = "123456789012345678";
            post.lastAttemptAtUtcIso = TimeUtil.ToIsoUtc(FixedNow.AddMinutes(-1));

            var result = CreateStateMachine().DismissNeedsReview(post, "Dismissed from review by user.");

            Assert.That(result.ok, Is.True);
            Assert.That(post.status, Is.EqualTo(PostStatus.Failed));
            Assert.That(post.nextAttemptAtUtcIso, Is.Empty);
            Assert.That(post.lastError, Does.Contain("Dismissed"));
            Assert.That(post.lastDiscordMessageId, Is.EqualTo("123456789012345678"));
        }

        [Test]
        public void RecoverStaleSending_MarksNeedsReview()
        {
            var post = SendingPost();
            post.nextAttemptAtUtcIso = TimeUtil.ToIsoUtc(FixedNow.AddMinutes(1));

            var result = CreateStateMachine().RecoverStaleSending(post, "");

            Assert.That(result.ok, Is.True);
            Assert.That(post.status, Is.EqualTo(PostStatus.NeedsReview));
            Assert.That(post.nextAttemptAtUtcIso, Is.Empty);
            Assert.That(post.lastError, Does.Contain("manual review"));
        }

        [Test]
        public void CaptureAndRestore_RoundTripsRollbackEvidence()
        {
            var state = CreateStateMachine();
            var post = SendingPost();
            post.retries = 4;
            post.lastError = "before save";
            post.lastAttemptAtUtcIso = TimeUtil.ToIsoUtc(FixedNow.AddMinutes(-2));
            post.nextAttemptAtUtcIso = TimeUtil.ToIsoUtc(FixedNow.AddMinutes(2));
            post.lastDiscordMessageId = "123456789012345678";

            var snapshot = state.Capture(post);
            post.status = PostStatus.Failed;
            post.retries = 0;
            post.lastError = "mutated";
            post.lastAttemptAtUtcIso = "";
            post.nextAttemptAtUtcIso = "";
            post.lastDiscordMessageId = "";

            state.Restore(post, snapshot);

            Assert.That(post.status, Is.EqualTo(PostStatus.Sending));
            Assert.That(post.retries, Is.EqualTo(4));
            Assert.That(post.lastError, Is.EqualTo("before save"));
            Assert.That(post.lastAttemptAtUtcIso, Is.EqualTo(TimeUtil.ToIsoUtc(FixedNow.AddMinutes(-2))));
            Assert.That(post.nextAttemptAtUtcIso, Is.EqualTo(TimeUtil.ToIsoUtc(FixedNow.AddMinutes(2))));
            Assert.That(post.lastDiscordMessageId, Is.EqualTo("123456789012345678"));
        }

        private static PostStateMachine CreateStateMachine(int maxRetries = 10)
        {
            return new PostStateMachine(new FixedTimeProvider(FixedNow), maxRetries);
        }

        private static ScheduledPost PendingPost()
        {
            return new ScheduledPost
            {
                id = "post-1",
                targetId = "target-1",
                status = PostStatus.Pending,
                updatedAtUtcIso = "",
                lastError = "",
                lastAttemptAtUtcIso = "",
                nextAttemptAtUtcIso = ""
            };
        }

        private static ScheduledPost SendingPost()
        {
            var post = PendingPost();
            post.status = PostStatus.Sending;
            return post;
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
