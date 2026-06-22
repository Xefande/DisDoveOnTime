using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace DiscordScheduler.Tests
{
    public sealed class CoreServicesContractTests
    {
        private static readonly DateTime FixedNow = new DateTime(2026, 6, 22, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void SchedulePolicy_MapsDueReasonsToConfiguredActions()
        {
            var service = new SchedulePolicyService();
            var post = Post("p1", "t1", PostStatus.Pending, FixedNow);
            post.missedPolicyIfOff = MissedPolicy.MarkMissed;
            post.missedPolicyIfSleep = MissedPolicy.MarkFailed;

            Assert.That(service.Decide(new DuePost { post = post, reason = DueReason.NormalDue }).kind, Is.EqualTo(ScheduleDecisionKind.Enqueue));
            Assert.That(service.Decide(new DuePost { post = post, reason = DueReason.AppWasOff }).kind, Is.EqualTo(ScheduleDecisionKind.MarkMissed));
            Assert.That(service.Decide(new DuePost { post = post, reason = DueReason.SleepGap }).kind, Is.EqualTo(ScheduleDecisionKind.MarkFailed));
            Assert.That(service.Decide(null).kind, Is.EqualTo(ScheduleDecisionKind.Ignore));
            Assert.That(service.Decide(new DuePost { post = post, reason = (DueReason)999 }).kind, Is.EqualTo(ScheduleDecisionKind.Ignore));
        }

        [Test]
        public void Scheduler_StartupReportsDuePendingOnlyAndSkipsInvalidSchedule()
        {
            var db = new AppDatabase();
            db.posts.Add(Post("due", "t1", PostStatus.Pending, FixedNow.AddMinutes(-1)));
            db.posts.Add(Post("future", "t1", PostStatus.Pending, FixedNow.AddMinutes(1)));
            db.posts.Add(Post("sent", "t1", PostStatus.Sent, FixedNow.AddMinutes(-1)));
            db.posts.Add(Post("invalid", "t1", PostStatus.Pending, FixedNow));
            db.posts.Last().scheduledAtUtcIso = "not-a-date";
            var due = new List<DuePost>();
            var log = new LogService();

            new SchedulerService(db, log, () => FixedNow).OnStartupHandleMissed(due.Add);

            Assert.That(due.Select(item => item.post.id), Is.EquivalentTo(new[] { "due" }));
            Assert.That(due[0].reason, Is.EqualTo(DueReason.AppWasOff));
            Assert.That(log.Snapshot().Any(line => line.Contains("invalid")), Is.True);
        }

        [Test]
        public void Scheduler_TickDetectsSleepGapAndKeepsGapStartBoundaryNormalDue()
        {
            var now = FixedNow;
            var db = new AppDatabase();
            db.settings.sleepThresholdMinutes = 10;
            db.posts.Add(Post("gap-start", "t1", PostStatus.Pending, FixedNow));
            db.posts.Add(Post("inside-gap", "t1", PostStatus.Pending, FixedNow.AddMinutes(5)));
            var due = new List<DuePost>();
            var scheduler = new SchedulerService(db, new LogService(), () => now);

            scheduler.Tick(due.Add);
            Assert.That(due.Select(item => item.post.id), Is.EquivalentTo(new[] { "gap-start" }));

            due.Clear();
            now = FixedNow.AddMinutes(10);
            scheduler.Tick(due.Add);

            var byId = due.ToDictionary(item => item.post.id, item => item.reason);
            Assert.That(byId["gap-start"], Is.EqualTo(DueReason.NormalDue));
            Assert.That(byId["inside-gap"], Is.EqualTo(DueReason.SleepGap));
        }

        [Test]
        public void TimeUtil_RejectsInvalidAndAmbiguousBudapestLocalTimes()
        {
            Assert.That(TimeUtil.TryParseIsoUtc("", out _), Is.False);
            Assert.That(TimeUtil.TryParseIsoUtc("not-a-date", out _), Is.False);

            var invalid = TimeUtil.TryLocalBudapestToUtc("2026-03-29", "02:30", out _, out var invalidError);
            var ambiguous = TimeUtil.TryLocalBudapestToUtc("2026-10-25", "02:30", out _, out var ambiguousError);

            Assert.That(invalid, Is.False);
            Assert.That(invalidError, Does.Contain("Invalid local time"));
            Assert.That(ambiguous, Is.False);
            Assert.That(ambiguousError, Does.Contain("Ambiguous local time"));
        }

        [Test]
        public void WebhookUrlValidator_RejectsUnsafeFormsAndMasksToken()
        {
            var validator = new WebhookUrlValidator();

            Assert.That(validator.Validate("http://discord.com/api/webhooks/123/token").ok, Is.False);
            Assert.That(validator.Validate("https://example.com/api/webhooks/123/token").ok, Is.False);
            Assert.That(validator.Validate("https://discord.com/api/webhooks/123/token#frag").ok, Is.False);
            Assert.That(validator.Validate("https://discord.com/api/webhooks/123/token").ok, Is.True);

            var masked = validator.Mask("https://discord.com/api/webhooks/123456789012345678/super-secret-token?wait=true");
            Assert.That(masked, Is.EqualTo("https://discord.com/api/webhooks/***/***"));
            Assert.That(masked, Does.Not.Contain("super-secret-token"));
        }

        [Test]
        public void LogService_RedactsSecretsKeepsCopyAndDropsOldest()
        {
            var log = new LogService(
                maxLines: 1,
                now: () => new DateTime(2026, 6, 22, 14, 15, 16));

            for (int i = 0; i < 55; i++)
                log.Info("line-" + i);
            log.Error("https://discord.com/api/webhooks/123456789012345678/super-secret-token");

            var snapshot = log.Snapshot();
            snapshot.Clear();
            var secondSnapshot = log.Snapshot();

            Assert.That(secondSnapshot.Count, Is.EqualTo(50));
            Assert.That(secondSnapshot.Any(line => line.Contains("line-0")), Is.False);
            Assert.That(secondSnapshot.Any(line => line.Contains("super-secret-token")), Is.False);
            Assert.That(secondSnapshot.Last(), Does.Contain("ERR"));
            Assert.That(secondSnapshot.Last(), Does.StartWith("[2026-06-22 14:15:16]"));
        }

        [Test]
        public void SettingsApplication_ClampsPoliciesAndClonesAllowedMentions()
        {
            var db = new AppDatabase();
            var mentions = new AllowedMentions { allowUsers = true, userIdsCsv = "123" };
            var service = new SettingsApplicationService();

            var result = service.Apply(db, new SettingsDraft
            {
                sleepThresholdMinutes = -10,
                defaultAllowedMentions = mentions,
                defaultOffPolicyIndex = 999,
                defaultSleepPolicyIndex = -1
            });
            mentions.userIdsCsv = "mutated";
            var draft = service.ToDraft(db.settings);

            Assert.That(result.ok, Is.True);
            Assert.That(db.settings.sleepThresholdMinutes, Is.EqualTo(SettingsApplicationService.MinSleepThresholdMinutes));
            Assert.That(db.settings.defaultOffPolicy, Is.EqualTo(MissedPolicy.SendOnNextRun));
            Assert.That(db.settings.defaultSleepPolicy, Is.EqualTo(MissedPolicy.SendOnNextRun));
            Assert.That(db.settings.defaultAllowedMentions.userIdsCsv, Is.EqualTo("123"));
            Assert.That(draft.defaultAllowedMentions, Is.Not.SameAs(db.settings.defaultAllowedMentions));
        }

        [Test]
        public void TargetRevisionPolicy_SnapshotFailsWhenAttemptTargetOrPayloadChanges()
        {
            var policy = new TargetRevisionPolicy();
            var target = Target("t1");
            var post = Post("p1", "t1", PostStatus.Sending, FixedNow);
            post.title = "original";
            var snapshot = policy.CreateSnapshot(post, target, "attempt-1", TimeUtil.ToIsoUtc(FixedNow));

            Assert.That(policy.Matches(snapshot, post, target, "attempt-1"), Is.True);
            Assert.That(policy.Matches(snapshot, post, target, "attempt-2"), Is.False);

            post.title = "changed";
            Assert.That(policy.Matches(snapshot, post, target, "attempt-1"), Is.False);

            post.title = "original";
            target.webhookSecretRef = "protected://changed";
            Assert.That(policy.Matches(snapshot, post, target, "attempt-1"), Is.False);
        }

        [Test]
        public void OperationalHealth_RedactsWarningsCountsBackoffQueueAndNeedsReview()
        {
            var db = DbWithTarget(Target("t1"));
            db.posts.Add(Post("pending", "t1", PostStatus.Pending, FixedNow));
            db.posts.Add(Post("review", "t1", PostStatus.NeedsReview, FixedNow));
            var queue = new SendQueueService(new PostStateMachine(new FixedTimeProvider(FixedNow)));
            Assert.That(queue.TryEnqueue(db.posts[0]).ok, Is.True);
            queue.MarkActive("pending");
            var registry = new RateLimitRegistry();
            registry.RecordTargetBackoff("t1", FixedNow, 60, "HTTP 429");

            var snapshot = new OperationalHealthService().BuildSnapshot(
                db,
                queue,
                registry,
                FixedNow.AddSeconds(1),
                "save failed https://discord.com/api/webhooks/123456789012345678/super-secret-token",
                "journal warning",
                "lock warning",
                "retention warning");

            Assert.That(snapshot.hasBlocker, Is.True);
            Assert.That(snapshot.warningCount, Is.EqualTo(6));
            Assert.That(snapshot.queuedCount, Is.EqualTo(1));
            Assert.That(snapshot.activeSendCount, Is.EqualTo(1));
            Assert.That(snapshot.activeBackoffCount, Is.EqualTo(1));
            Assert.That(snapshot.saveWarning, Does.Not.Contain("super-secret-token"));
            Assert.That(snapshot.summary, Does.Contain("NeedsReview: 1"));
            Assert.That(snapshot.summary, Does.Contain("Warnings: 6"));
        }

        [Test]
        public void AppInstanceLock_BlocksSecondLeaseAndReportsStaleRecovery()
        {
            var folder = Path.Combine(Path.GetTempPath(), "DisDoveLockTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var firstLock = new AppInstanceLock(new FixedTimeProvider(FixedNow), TimeSpan.FromMinutes(1));
                using (var first = firstLock.TryAcquire(folder))
                {
                    Assert.That(first.IsAcquired, Is.True);
                    var second = firstLock.TryAcquire(folder);
                    try
                    {
                        Assert.That(second.IsAcquired, Is.False);
                        Assert.That(second.result.readOnly, Is.True);
                    }
                    finally
                    {
                        second.Dispose();
                    }
                }

                var lockPath = Path.Combine(folder, "disdoveontime.storage.lock");
                File.SetLastWriteTimeUtc(lockPath, FixedNow.AddHours(-2));
                using (var recovered = firstLock.TryAcquire(folder))
                {
                    Assert.That(recovered.IsAcquired, Is.True);
                    Assert.That(recovered.result.staleRecovered, Is.True);
                }
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
        }

        [Test]
        public void PostApplication_CreatePostUsesValidatorUtcScheduleAndClonedMentions()
        {
            var db = DbWithTarget(Target("t1"));
            var mentions = new AllowedMentions { allowUsers = true, userIdsCsv = "123456789012345678" };
            var service = new PostApplicationService(new PostFormValidator(), new PostMediaService(), new FixedTimeProvider(FixedNow));

            var result = service.CreatePost(new PostDraft
            {
                targetId = "t1",
                title = "Title",
                body = "Body",
                dateYmd = "2026-06-22",
                timeHm = "14:00",
                allowedMentions = mentions,
                missedPolicyIfOff = MissedPolicy.MarkMissed,
                missedPolicyIfSleep = MissedPolicy.MarkFailed
            }, db, out var post);
            mentions.userIdsCsv = "mutated";

            Assert.That(result.ok, Is.True);
            Assert.That(post, Is.Not.Null);
            Assert.That(db.posts, Has.Count.EqualTo(1));
            Assert.That(post.createdAtUtcIso, Is.EqualTo(TimeUtil.ToIsoUtc(FixedNow)));
            Assert.That(TimeUtil.TryParseIsoUtc(post.scheduledAtUtcIso, out var scheduledUtc), Is.True);
            Assert.That(scheduledUtc, Is.EqualTo(new DateTime(2026, 6, 22, 12, 0, 0, DateTimeKind.Utc)));
            Assert.That(post.allowedMentions.userIdsCsv, Is.EqualTo("123456789012345678"));
            Assert.That(post.missedPolicyIfOff, Is.EqualTo(MissedPolicy.MarkMissed));
            Assert.That(post.missedPolicyIfSleep, Is.EqualTo(MissedPolicy.MarkFailed));
        }

        [Test]
        public void PostApplication_UpdatePostMediaFailureRestoresOriginalSnapshot()
        {
            var db = DbWithTarget(Target("t1"));
            var post = Post("p1", "t1", PostStatus.Pending, FixedNow);
            post.title = "Original";
            post.body = "Original body";
            post.scheduledAtUtcIso = TimeUtil.ToIsoUtc(FixedNow.AddHours(1));
            post.mediaKind = MediaKind.Image;
            post.mediaPath = "old.png";
            post.imagePath = "old.png";
            post.allowedMentions = new AllowedMentions { allowEveryone = true };
            post.updatedAtUtcIso = "old-update";
            db.posts.Add(post);
            var service = new PostApplicationService(new PostFormValidator(), new PostMediaService(), new FixedTimeProvider(FixedNow));

            var result = service.UpdatePost(post, new PostDraft
            {
                targetId = "t1",
                title = "Changed",
                body = "Changed body",
                dateYmd = "2026-06-22",
                timeHm = "15:00",
                mediaPath = Path.Combine(Path.GetTempPath(), "missing-image-file.png"),
                allowedMentions = new AllowedMentions { allowUsers = true, userIdsCsv = "123456789012345678" }
            }, db);

            Assert.That(result.ok, Is.False);
            Assert.That(post.title, Is.EqualTo("Original"));
            Assert.That(post.body, Is.EqualTo("Original body"));
            Assert.That(post.scheduledAtUtcIso, Is.EqualTo(TimeUtil.ToIsoUtc(FixedNow.AddHours(1))));
            Assert.That(post.mediaKind, Is.EqualTo(MediaKind.Image));
            Assert.That(post.mediaPath, Is.EqualTo("old.png"));
            Assert.That(post.imagePath, Is.EqualTo("old.png"));
            Assert.That(post.allowedMentions.allowEveryone, Is.True);
            Assert.That(post.updatedAtUtcIso, Is.EqualTo("old-update"));
        }

        [Test]
        public void PostMedia_PrepareMediaUsesInjectedAttachmentFolderAndRules()
        {
            var root = Path.Combine(Path.GetTempPath(), "DisDovePostMediaTests", Guid.NewGuid().ToString("N"));
            var sourcePath = Path.Combine(root, "source", "launch-card.png");
            var paths = new TestPathProvider(root);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(sourcePath));
                File.WriteAllBytes(sourcePath, new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
                var service = new PostMediaService(paths, new SystemFileSystem());

                var result = service.TryPrepareMedia(sourcePath, "post-123", out var mediaKind, out var storedPath);

                Assert.That(result.ok, Is.True);
                Assert.That(mediaKind, Is.EqualTo(MediaKind.Image));
                Assert.That(storedPath, Does.StartWith(paths.AttachmentsFolder));
                Assert.That(Path.GetFileName(storedPath), Is.EqualTo("img_launch-card_post-123.png"));
                Assert.That(File.Exists(storedPath), Is.True);
                Assert.That(service.IsManagedAttachmentPath(storedPath), Is.True);
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void PostMedia_ManagedAttachmentPathSkipsCopyAndValidatesInjectedRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "DisDovePostMediaTests", Guid.NewGuid().ToString("N"));
            var paths = new TestPathProvider(root);
            var managedPath = Path.Combine(paths.AttachmentsFolder, "img_existing_post-123.png");
            try
            {
                Directory.CreateDirectory(paths.AttachmentsFolder);
                File.WriteAllBytes(managedPath, new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
                var service = new PostMediaService(paths, new SystemFileSystem());

                var result = service.TryPrepareMedia(managedPath, "different-post", out var mediaKind, out var storedPath);

                Assert.That(result.ok, Is.True);
                Assert.That(mediaKind, Is.EqualTo(MediaKind.Image));
                Assert.That(storedPath, Is.EqualTo(managedPath));
                Assert.That(File.Exists(Path.Combine(paths.AttachmentsFolder, "img_existing_different-post.png")), Is.False);
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        private static AppDatabase DbWithTarget(Target target)
        {
            var db = new AppDatabase();
            db.targets.Add(target);
            return db;
        }

        private static Target Target(string id)
        {
            return new Target
            {
                id = id,
                name = "Main",
                webhookUrl = "https://discord.com/api/webhooks/123456789012345678/test-token",
                webhookSecretRef = "",
                overrideUsername = "",
                overrideAvatarUrl = ""
            };
        }

        private static ScheduledPost Post(string id, string targetId, PostStatus status, DateTime scheduledUtc)
        {
            return new ScheduledPost
            {
                id = id,
                targetId = targetId,
                title = id,
                body = "Body",
                scheduledAtUtcIso = TimeUtil.ToIsoUtc(scheduledUtc),
                status = status,
                createdAtUtcIso = TimeUtil.ToIsoUtc(FixedNow.AddMinutes(-10)),
                updatedAtUtcIso = TimeUtil.ToIsoUtc(FixedNow.AddMinutes(-5)),
                allowedMentions = new AllowedMentions(),
                lastError = "",
                lastAttemptAtUtcIso = "",
                nextAttemptAtUtcIso = "",
                lastDiscordMessageId = ""
            };
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch
            {
            }
        }

        private sealed class TestPathProvider : IPathProvider
        {
            public TestPathProvider(string root)
            {
                DataFolder = root;
                DataFilePath = Path.Combine(root, "discord_scheduler_db.json");
                AttachmentsFolder = Path.Combine(root, "attachments");
            }

            public string DataFolder { get; }
            public string DataFilePath { get; }
            public string AttachmentsFolder { get; }
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
