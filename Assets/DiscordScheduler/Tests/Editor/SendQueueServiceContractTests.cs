using System;
using NUnit.Framework;

namespace DiscordScheduler.Tests
{
    public sealed class SendQueueServiceContractTests
    {
        private static readonly DateTime FixedNow = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void TryEnqueue_Null_ReturnsFail()
        {
            var queue = CreateQueue();

            var result = queue.TryEnqueue(null);

            Assert.That(result.ok, Is.False);
            Assert.That(queue.Count, Is.Zero);
        }

        [Test]
        public void TryEnqueue_MissingId_ReturnsFail()
        {
            var queue = CreateQueue();
            var post = PendingPost("");

            var result = queue.TryEnqueue(post);

            Assert.That(result.ok, Is.False);
            Assert.That(post.status, Is.EqualTo(PostStatus.Pending));
            Assert.That(queue.Count, Is.Zero);
        }

        [Test]
        public void TryEnqueue_Pending_AddsOnceAndMarksSending()
        {
            var queue = CreateQueue();
            var post = PendingPost("p1");

            var result = queue.TryEnqueue(post);

            Assert.That(result.ok, Is.True);
            Assert.That(post.status, Is.EqualTo(PostStatus.Sending));
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(queue.IsQueued("p1"), Is.True);
            Assert.That(queue.IsQueuedOrActive("p1"), Is.True);
        }

        [Test]
        public void TryEnqueue_NotDueYet_ReturnsFailWithoutQueueingOrMutating()
        {
            var queue = CreateQueue();
            var post = PendingPost("p1");
            post.nextAttemptAtUtcIso = TimeUtil.ToIsoUtc(FixedNow.AddMinutes(1));

            var result = queue.TryEnqueue(post);

            Assert.That(result.ok, Is.False);
            Assert.That(result.error, Does.Contain("not due"));
            Assert.That(post.status, Is.EqualTo(PostStatus.Pending));
            Assert.That(queue.Count, Is.Zero);
            Assert.That(queue.IsQueued("p1"), Is.False);
        }

        [Test]
        public void TryEnqueue_DuplicateQueued_ReturnsFail()
        {
            var queue = CreateQueue();
            var post = PendingPost("p1");
            Assert.That(queue.TryEnqueue(post).ok, Is.True);

            var duplicate = PendingPost("p1");
            var result = queue.TryEnqueue(duplicate);

            Assert.That(result.ok, Is.False);
            Assert.That(duplicate.status, Is.EqualTo(PostStatus.Pending));
            Assert.That(queue.Count, Is.EqualTo(1));
        }

        [Test]
        public void TryEnqueue_ActivePost_ReturnsFail()
        {
            var queue = CreateQueue();
            queue.MarkActive("p1");
            var post = PendingPost("p1");

            var result = queue.TryEnqueue(post);

            Assert.That(result.ok, Is.False);
            Assert.That(post.status, Is.EqualTo(PostStatus.Pending));
            Assert.That(queue.Count, Is.Zero);
        }

        [TestCase(PostStatus.Sending)]
        [TestCase(PostStatus.Sent)]
        [TestCase(PostStatus.Failed)]
        [TestCase(PostStatus.Missed)]
        [TestCase(PostStatus.NeedsReview)]
        public void TryEnqueue_NonPending_ReturnsFail(PostStatus status)
        {
            var queue = CreateQueue();
            var post = PendingPost("p1");
            post.status = status;

            var result = queue.TryEnqueue(post);

            Assert.That(result.ok, Is.False);
            Assert.That(post.status, Is.EqualTo(status));
            Assert.That(queue.Count, Is.Zero);
        }

        [Test]
        public void TryDequeueNext_ReturnsFirstQueuedAndCleansQueueMarker()
        {
            var queue = CreateQueue();
            var first = PendingPost("p1");
            var second = PendingPost("p2");
            Assert.That(queue.TryEnqueue(first).ok, Is.True);
            Assert.That(queue.TryEnqueue(second).ok, Is.True);
            var db = new AppDatabase();
            db.posts.Add(first);
            db.posts.Add(second);

            var dequeued = queue.TryDequeueNext(db, out var post);

            Assert.That(dequeued, Is.True);
            Assert.That(post, Is.SameAs(first));
            Assert.That(queue.IsQueued("p1"), Is.False);
            Assert.That(queue.IsQueued("p2"), Is.True);
            Assert.That(queue.Count, Is.EqualTo(1));
        }

        [Test]
        public void TryDequeueNext_MissingPost_SkipsAndCleansQueue()
        {
            var queue = CreateQueue();
            var missing = PendingPost("missing");
            Assert.That(queue.TryEnqueue(missing).ok, Is.True);

            var dequeued = queue.TryDequeueNext(new AppDatabase(), out var post);

            Assert.That(dequeued, Is.False);
            Assert.That(post, Is.Null);
            Assert.That(queue.Count, Is.Zero);
            Assert.That(queue.IsQueued("missing"), Is.False);
        }

        [Test]
        public void TryDequeueNext_NullDatabase_SkipsQueuedMarkerWithoutThrowing()
        {
            var queue = CreateQueue();
            var post = PendingPost("p1");
            Assert.That(queue.TryEnqueue(post).ok, Is.True);

            var dequeued = queue.TryDequeueNext(null, out var result);

            Assert.That(dequeued, Is.False);
            Assert.That(result, Is.Null);
            Assert.That(queue.Count, Is.Zero);
            Assert.That(queue.IsQueued("p1"), Is.False);
        }

        [Test]
        public void TryDequeueNext_StatusChanged_SkipsAndCleansQueue()
        {
            var queue = CreateQueue();
            var post = PendingPost("p1");
            Assert.That(queue.TryEnqueue(post).ok, Is.True);
            post.status = PostStatus.Pending;
            var db = new AppDatabase();
            db.posts.Add(post);

            var dequeued = queue.TryDequeueNext(db, out var result);

            Assert.That(dequeued, Is.False);
            Assert.That(result, Is.Null);
            Assert.That(queue.Count, Is.Zero);
            Assert.That(queue.IsQueued("p1"), Is.False);
        }

        [Test]
        public void TryDequeueNext_ActiveSend_ReturnsFalseWithoutDequeuing()
        {
            var queue = CreateQueue();
            var post = PendingPost("p1");
            Assert.That(queue.TryEnqueue(post).ok, Is.True);
            queue.MarkActive("active");
            var db = new AppDatabase();
            db.posts.Add(post);

            var dequeued = queue.TryDequeueNext(db, out var result);

            Assert.That(dequeued, Is.False);
            Assert.That(result, Is.Null);
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(queue.IsQueued("p1"), Is.True);
        }

        [Test]
        public void MarkFinished_Active_ClearsActive()
        {
            var queue = CreateQueue();
            queue.MarkActive("p1");

            queue.MarkFinished("p1");

            Assert.That(queue.HasActiveSend, Is.False);
            Assert.That(queue.ActivePostId, Is.Empty);
        }

        [Test]
        public void MarkFinished_WrongId_KeepsActive()
        {
            var queue = CreateQueue();
            queue.MarkActive("p1");

            queue.MarkFinished("p2");

            Assert.That(queue.HasActiveSend, Is.True);
            Assert.That(queue.ActivePostId, Is.EqualTo("p1"));
        }

        [Test]
        public void MarkFinished_WithAttemptIdRequiresMatchingSnapshot()
        {
            var queue = CreateQueue();
            queue.MarkActive("p1");

            queue.MarkFinished("p1", "unexpected-attempt");

            Assert.That(queue.HasActiveSend, Is.True);
            Assert.That(queue.ActivePostId, Is.EqualTo("p1"));

            queue.MarkFinished("p1", "");

            Assert.That(queue.HasActiveSend, Is.False);
        }

        [Test]
        public void ActiveAttemptSnapshot_GatesFinishByAttemptId()
        {
            var queue = CreateQueue();
            queue.MarkActive("p1");
            queue.SetActiveSnapshot(new SendCommandSnapshot { postId = "p1", attemptId = "a1" });

            Assert.That(queue.IsActiveAttempt("p1", "a1"), Is.True);
            Assert.That(queue.IsActiveAttempt("p1", "wrong"), Is.False);

            queue.MarkFinished("p1", "wrong");
            Assert.That(queue.HasActiveSend, Is.True);

            queue.MarkFinished("p1", "a1");
            Assert.That(queue.HasActiveSend, Is.False);
            Assert.That(queue.GetActiveSnapshot(), Is.Null);
        }

        [Test]
        public void ActiveAttemptSnapshot_WrongPostIsIgnored()
        {
            var queue = CreateQueue();
            queue.MarkActive("p1");

            queue.SetActiveSnapshot(new SendCommandSnapshot { postId = "p2", attemptId = "a1" });

            Assert.That(queue.GetActiveSnapshot(), Is.Null);
            Assert.That(queue.IsActiveAttempt("p1", ""), Is.True);
        }

        [Test]
        public void CancelQueued_RemovesQueuedIdAndPreservesOtherItems()
        {
            var queue = CreateQueue();
            var first = PendingPost("p1");
            var second = PendingPost("p2");
            Assert.That(queue.TryEnqueue(first).ok, Is.True);
            Assert.That(queue.TryEnqueue(second).ok, Is.True);

            var cancelled = queue.CancelQueued("p1");

            Assert.That(cancelled, Is.True);
            Assert.That(queue.IsQueued("p1"), Is.False);
            Assert.That(queue.IsQueued("p2"), Is.True);
            Assert.That(queue.Count, Is.EqualTo(1));
        }

        [Test]
        public void CancelQueued_MiddleItemPreservesRemainingDequeueOrder()
        {
            var queue = CreateQueue();
            var first = PendingPost("p1");
            var second = PendingPost("p2");
            var third = PendingPost("p3");
            Assert.That(queue.TryEnqueue(first).ok, Is.True);
            Assert.That(queue.TryEnqueue(second).ok, Is.True);
            Assert.That(queue.TryEnqueue(third).ok, Is.True);
            var db = new AppDatabase();
            db.posts.Add(first);
            db.posts.Add(second);
            db.posts.Add(third);

            Assert.That(queue.CancelQueued("p2"), Is.True);

            Assert.That(queue.TryDequeueNext(db, out var dequeuedFirst), Is.True);
            Assert.That(dequeuedFirst, Is.SameAs(first));
            Assert.That(queue.TryDequeueNext(db, out var dequeuedThird), Is.True);
            Assert.That(dequeuedThird, Is.SameAs(third));
            Assert.That(queue.TryDequeueNext(db, out _), Is.False);
        }

        [Test]
        public void CancelQueued_MissingId_ReturnsFalse()
        {
            var queue = CreateQueue();

            Assert.That(queue.CancelQueued("missing"), Is.False);
        }

        [Test]
        public void IsQueuedOrActive_BlankPostId_ReturnsFalse()
        {
            var queue = CreateQueue();

            Assert.That(queue.IsQueuedOrActive(""), Is.False);
            Assert.That(queue.IsQueuedOrActive("   "), Is.False);
        }

        private static SendQueueService CreateQueue()
        {
            return new SendQueueService(new PostStateMachine(new FixedTimeProvider(FixedNow)));
        }

        private static ScheduledPost PendingPost(string id)
        {
            return new ScheduledPost
            {
                id = id,
                targetId = "target-1",
                status = PostStatus.Pending,
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
