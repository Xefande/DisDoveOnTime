using System;
using NUnit.Framework;

namespace DiscordScheduler.Tests
{
    public sealed class MutationGuardServiceContractTests
    {
        private static readonly DateTime FixedNow = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void CanDeletePost_ActiveQueueId_IsBlocked()
        {
            var queue = CreateQueue();
            var guard = CreateGuard(queue);
            var post = Post("p1", PostStatus.Pending);
            queue.MarkActive("p1");

            var result = guard.CanDeletePost(post);

            Assert.That(result.ok, Is.False);
            Assert.That(result.error, Does.Contain("actively sending"));
        }

        [Test]
        public void CanDeletePost_SendingStatus_IsBlockedEvenWithoutActiveQueueMarker()
        {
            var guard = CreateGuard(CreateQueue());
            var post = Post("p1", PostStatus.Sending);

            var result = guard.CanDeletePost(post);

            Assert.That(result.ok, Is.False);
            Assert.That(result.error, Does.Contain("actively sending"));
        }

        [Test]
        public void CanDeletePost_PendingNotQueued_ReturnsOk()
        {
            var guard = CreateGuard(CreateQueue());

            var result = guard.CanDeletePost(Post("p1", PostStatus.Pending));

            Assert.That(result.ok, Is.True);
        }

        [Test]
        public void CanDeletePost_QueuedPost_IsBlockedUntilCancelOrRequeue()
        {
            var queue = CreateQueue();
            var guard = CreateGuard(queue);
            var post = Post("p1", PostStatus.Pending);
            Assert.That(queue.TryEnqueue(post).ok, Is.True);

            var result = guard.CanDeletePost(post);

            Assert.That(result.ok, Is.False);
            Assert.That(result.error, Does.Contain("Cancel/requeue"));
        }

        [Test]
        public void CanEditPost_QueuedPost_IsBlockedUntilCancelOrRequeue()
        {
            var queue = CreateQueue();
            var guard = CreateGuard(queue);
            var post = Post("p1", PostStatus.Pending);
            Assert.That(queue.TryEnqueue(post).ok, Is.True);

            var result = guard.CanEditPost(post);

            Assert.That(result.ok, Is.False);
            Assert.That(result.error, Does.Contain("Cancel/requeue"));
        }

        private static MutationGuardService CreateGuard(SendQueueService queue)
        {
            return new MutationGuardService(queue, new TargetRevisionPolicy());
        }

        private static SendQueueService CreateQueue()
        {
            return new SendQueueService(new PostStateMachine(new FixedTimeProvider(FixedNow)));
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
