using System;
using System.Collections;
using NUnit.Framework;

namespace DiscordScheduler.Tests
{
    public sealed class SendQueueFakeTransportIntegrationTests
    {
        private static readonly DateTime FixedNow = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Queue_FakeSuccess_StoresDiscordMessageIdAndClearsActiveSend()
        {
            var fake = new FakeWebhookTransport();
            fake.EnqueueResponse(200, "{\"id\":\"333456789012345678\",\"content\":\"sent\"}");

            var context = CreateContext();
            var post = context.post;

            var result = SendQueuedPost(context, fake);

            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.Success));
            Assert.That(post.status, Is.EqualTo(PostStatus.Sent));
            Assert.That(post.lastDiscordMessageId, Is.EqualTo("333456789012345678"));
            Assert.That(post.lastError, Is.Empty);
            Assert.That(post.nextAttemptAtUtcIso, Is.Empty);
            Assert.That(context.queue.HasActiveSend, Is.False);
            Assert.That(context.queue.Count, Is.Zero);
        }

        [Test]
        public void Queue_FakeRateLimited_PersistsRetryAndClearsActiveSend()
        {
            var fake = new FakeWebhookTransport();
            fake.EnqueueRateLimit(13f);

            var context = CreateContext();
            var post = context.post;

            var result = SendQueuedPost(context, fake);

            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.RateLimited));
            Assert.That(result.retryAfterSeconds, Is.EqualTo(13f));
            Assert.That(post.status, Is.EqualTo(PostStatus.Pending));
            Assert.That(post.retries, Is.EqualTo(1));
            Assert.That(post.nextAttemptAtUtcIso, Is.EqualTo(TimeUtil.ToIsoUtc(FixedNow.AddSeconds(13))));
            Assert.That(context.queue.HasActiveSend, Is.False);
            Assert.That(context.queue.Count, Is.Zero);
        }

        [Test]
        public void Queue_FakeTimeout_MovesToNeedsReviewWithoutBlindRetry()
        {
            var fake = new FakeWebhookTransport();
            fake.EnqueueTimeout("Synthetic timeout after send started.");

            var context = CreateContext();
            var post = context.post;

            var result = SendQueuedPost(context, fake);

            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.Ambiguous));
            Assert.That(result.requestMayHaveReachedDiscord, Is.True);
            Assert.That(post.status, Is.EqualTo(PostStatus.NeedsReview));
            Assert.That(post.nextAttemptAtUtcIso, Is.Empty);
            Assert.That(post.lastError, Does.Contain("NeedsReview"));
            Assert.That(context.queue.HasActiveSend, Is.False);
        }

        [Test]
        public void Queue_FakeServerError_ReturnsPendingWithBackoff()
        {
            var fake = new FakeWebhookTransport();
            fake.EnqueueResponse(503, "Service unavailable");

            var context = CreateContext();
            var post = context.post;

            var result = SendQueuedPost(context, fake);

            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.RetryableTransient));
            Assert.That(post.status, Is.EqualTo(PostStatus.Pending));
            Assert.That(post.retries, Is.EqualTo(1));
            Assert.That(post.nextAttemptAtUtcIso, Is.EqualTo(TimeUtil.ToIsoUtc(FixedNow.AddSeconds(2))));
            Assert.That(context.queue.HasActiveSend, Is.False);
        }

        [Test]
        public void Queue_FakeGlobalRateLimit_MetadataCanDriveGlobalRegistry()
        {
            var fake = new FakeWebhookTransport();
            fake.EnqueueRateLimit(8f, global: true, scope: "global");

            var context = CreateContext();
            var result = SendQueuedPost(context, fake);

            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.RateLimited));
            Assert.That(result.rateLimitGlobal, Is.True);
            Assert.That(result.rateLimitScope, Is.EqualTo("global"));

            var registry = new RateLimitRegistry();
            if (result.rateLimitGlobal || string.Equals(result.rateLimitScope, "global", StringComparison.OrdinalIgnoreCase))
                registry.RecordGlobalBackoff(FixedNow, result.retryAfterSeconds, result.shortError);

            Assert.That(registry.CanSend("target-2", FixedNow.AddSeconds(1)).ok, Is.False);
            Assert.That(registry.CanSend("target-2", FixedNow.AddSeconds(9)).ok, Is.True);
        }

        private static QueueSendContext CreateContext()
        {
            var target = new Target
            {
                id = "target-1",
                name = "Discord",
                webhookUrl = "https://discord.com/api/webhooks/123456789012345678/test-token"
            };

            var post = new ScheduledPost
            {
                id = "post-1",
                targetId = target.id,
                title = "Queued post",
                body = "Body",
                status = PostStatus.Pending,
                mediaKind = MediaKind.None,
                mediaPath = "",
                allowedMentions = new AllowedMentions(),
                updatedAtUtcIso = "",
                lastError = "",
                lastAttemptAtUtcIso = "",
                nextAttemptAtUtcIso = ""
            };

            var db = new AppDatabase();
            db.targets.Add(target);
            db.posts.Add(post);

            var state = new PostStateMachine(new FixedTimeProvider(FixedNow), maxRetries: 3);
            var queue = new SendQueueService(state);

            return new QueueSendContext
            {
                db = db,
                target = target,
                post = post,
                state = state,
                queue = queue
            };
        }

        private static WebhookSendResult SendQueuedPost(QueueSendContext context, FakeWebhookTransport fake)
        {
            Assert.That(context.queue.TryEnqueue(context.post).ok, Is.True);
            Assert.That(context.queue.TryDequeueNext(context.db, out var activePost), Is.True);
            Assert.That(activePost, Is.SameAs(context.post));

            context.queue.MarkActive(activePost.id);
            var result = SendWithFake(fake, context.target, activePost);
            if (result.ok)
                Assert.That(context.state.MarkSent(activePost, result).ok, Is.True);
            else
                Assert.That(context.state.MarkSendFailedOrRetry(activePost, result).ok, Is.True);

            context.queue.MarkFinished(activePost.id);
            return result;
        }

        private static WebhookSendResult SendWithFake(FakeWebhookTransport fake, Target target, ScheduledPost post)
        {
            var client = new DiscordWebhookClient(new LogService(), fake, new WebhookRequestFactory());
            WebhookSendResult result = null;
            IEnumerator routine = client.Send(target, post, sendResult => result = sendResult);

            var guard = 0;
            while (routine.MoveNext())
            {
                guard++;
                if (guard > 1000)
                    Assert.Fail("DiscordWebhookClient.Send did not complete within the queue integration guard.");
            }

            return result;
        }

        private sealed class QueueSendContext
        {
            public AppDatabase db;
            public Target target;
            public ScheduledPost post;
            public PostStateMachine state;
            public SendQueueService queue;
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
