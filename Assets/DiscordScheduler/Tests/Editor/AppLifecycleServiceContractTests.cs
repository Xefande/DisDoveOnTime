using System;
using NUnit.Framework;

namespace DiscordScheduler.Tests
{
    public sealed class AppLifecycleServiceContractTests
    {
        private static readonly DateTime FixedNow = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void CanStartSend_AfterShutdownStarted_ReturnsFail()
        {
            var lifecycle = CreateLifecycle();

            lifecycle.MarkShutdownStarted();
            var result = lifecycle.CanStartSend();

            Assert.That(result.ok, Is.False);
            Assert.That(result.error, Does.Contain("shutdown"));
            Assert.That(lifecycle.ShutdownStarted, Is.True);
        }

        [Test]
        public void RecoverStaleSendingPosts_OnlySendingPostsMoveToNeedsReview()
        {
            var db = new AppDatabase();
            var sending = Post("sending", PostStatus.Sending);
            var pending = Post("pending", PostStatus.Pending);
            var review = Post("review", PostStatus.NeedsReview);
            db.posts.Add(sending);
            db.posts.Add(pending);
            db.posts.Add(review);

            var recovered = CreateLifecycle().RecoverStaleSendingPosts(db, "Recovered on startup.");

            Assert.That(recovered, Is.EqualTo(1));
            Assert.That(sending.status, Is.EqualTo(PostStatus.NeedsReview));
            Assert.That(sending.lastError, Is.EqualTo("Recovered on startup."));
            Assert.That(sending.nextAttemptAtUtcIso, Is.Empty);
            Assert.That(pending.status, Is.EqualTo(PostStatus.Pending));
            Assert.That(review.status, Is.EqualTo(PostStatus.NeedsReview));
        }

        [Test]
        public void MarkActiveSendAmbiguousOnShutdown_SendingMovesToNeedsReview()
        {
            var lifecycle = CreateLifecycle();
            var post = Post("active", PostStatus.Sending);
            post.nextAttemptAtUtcIso = TimeUtil.ToIsoUtc(FixedNow.AddMinutes(2));

            var result = lifecycle.MarkActiveSendAmbiguousOnShutdown(post, "Application quit during active send.");

            Assert.That(result.ok, Is.True);
            Assert.That(lifecycle.ShutdownStarted, Is.True);
            Assert.That(post.status, Is.EqualTo(PostStatus.NeedsReview));
            Assert.That(post.nextAttemptAtUtcIso, Is.Empty);
            Assert.That(post.lastError, Does.Contain("Application quit"));
        }

        [Test]
        public void MarkActiveSendAmbiguousOnShutdown_NonSendingDoesNotMutate()
        {
            var lifecycle = CreateLifecycle();
            var post = Post("pending", PostStatus.Pending);

            var result = lifecycle.MarkActiveSendAmbiguousOnShutdown(post, "Shutdown");

            Assert.That(result.ok, Is.True);
            Assert.That(lifecycle.ShutdownStarted, Is.True);
            Assert.That(post.status, Is.EqualTo(PostStatus.Pending));
            Assert.That(post.lastError, Is.Empty);
        }

        private static AppLifecycleService CreateLifecycle()
        {
            return new AppLifecycleService(new PostStateMachine(new FixedTimeProvider(FixedNow)));
        }

        private static ScheduledPost Post(string id, PostStatus status)
        {
            return new ScheduledPost
            {
                id = id,
                targetId = "target-1",
                status = status,
                updatedAtUtcIso = "",
                lastError = "",
                lastAttemptAtUtcIso = "",
                nextAttemptAtUtcIso = ""
            };
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
