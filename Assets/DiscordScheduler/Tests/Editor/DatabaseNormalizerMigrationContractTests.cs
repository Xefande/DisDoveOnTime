using NUnit.Framework;

namespace DiscordScheduler.Tests
{
    public sealed class DatabaseNormalizerMigrationContractTests
    {
        [Test]
        public void Normalize_NullCollections_FillsDefaults()
        {
            var db = new AppDatabase
            {
                targets = null,
                posts = null,
                settings = null
            };

            var report = new DatabaseNormalizer().Normalize(db);

            Assert.That(db.targets, Is.Not.Null);
            Assert.That(db.posts, Is.Not.Null);
            Assert.That(db.settings, Is.Not.Null);
            Assert.That(report.fixedTargets, Is.EqualTo(1));
            Assert.That(report.fixedPosts, Is.EqualTo(1));
            Assert.That(report.fixedSettings, Is.EqualTo(1));
        }

        [Test]
        public void Normalize_NullAllowedMentions_FillsDefaults()
        {
            var db = ValidDb();
            db.settings.defaultAllowedMentions = null;
            db.posts.Add(new ScheduledPost
            {
                id = "p1",
                targetId = "t1",
                allowedMentions = null
            });

            var report = new DatabaseNormalizer().Normalize(db);

            Assert.That(db.settings.defaultAllowedMentions, Is.Not.Null);
            Assert.That(db.posts[0].allowedMentions, Is.Not.Null);
            Assert.That(report.fixedSettings, Is.EqualTo(1));
            Assert.That(report.fixedPosts, Is.GreaterThanOrEqualTo(4));
        }

        [Test]
        public void Normalize_InvalidEnums_ReportsAndClamps()
        {
            var db = ValidDb();
            db.settings.defaultOffPolicy = (MissedPolicy)999;
            db.posts.Add(new ScheduledPost
            {
                id = "p1",
                targetId = "t1",
                status = (PostStatus)999,
                missedPolicyIfOff = (MissedPolicy)999,
                mediaKind = (MediaKind)999
            });

            var report = new DatabaseNormalizer().Normalize(db);

            Assert.That(db.settings.defaultOffPolicy, Is.EqualTo(MissedPolicy.SendOnNextRun));
            Assert.That(db.posts[0].status, Is.EqualTo(PostStatus.Pending));
            Assert.That(db.posts[0].missedPolicyIfOff, Is.EqualTo(MissedPolicy.SendOnNextRun));
            Assert.That(db.posts[0].mediaKind, Is.EqualTo(MediaKind.None));
            Assert.That(report.warnings.Count, Is.EqualTo(4));
        }

        [Test]
        public void Normalize_DuplicateIds_Reports()
        {
            var db = new AppDatabase();
            db.targets.Add(new Target { id = "t1" });
            db.targets.Add(new Target { id = "t1" });
            db.posts.Add(new ScheduledPost { id = "p1", targetId = "t1" });
            db.posts.Add(new ScheduledPost { id = "p1", targetId = "t1" });

            var report = new DatabaseNormalizer().Normalize(db);

            Assert.That(report.warnings, Has.Some.Contains("Duplicate target id"));
            Assert.That(report.warnings, Has.Some.Contains("Duplicate post id"));
        }

        [Test]
        public void Normalize_PostMissingTarget_Reports()
        {
            var db = ValidDb();
            db.posts.Add(new ScheduledPost { id = "p1", targetId = "missing" });

            var report = new DatabaseNormalizer().Normalize(db);

            Assert.That(report.warnings, Has.Some.Contains("references missing targetId"));
        }

        [Test]
        public void Normalize_LegacyImagePathOnly_SetsMediaCompatibility()
        {
            var db = ValidDb();
            db.posts.Add(new ScheduledPost
            {
                id = "p1",
                targetId = "t1",
                imagePath = "image.png",
                mediaPath = "",
                mediaKind = MediaKind.None
            });

            var report = new DatabaseNormalizer().Normalize(db);

            Assert.That(db.posts[0].mediaPath, Is.EqualTo("image.png"));
            Assert.That(db.posts[0].mediaKind, Is.EqualTo(MediaKind.Image));
            Assert.That(report.fixedPosts, Is.GreaterThanOrEqualTo(4));
        }

        [Test]
        public void Normalize_ConflictingVideoMedia_ClearsLegacyImagePath()
        {
            var db = ValidDb();
            db.posts.Add(new ScheduledPost
            {
                id = "p1",
                targetId = "t1",
                imagePath = "old.png",
                mediaPath = "clip.mp4",
                mediaKind = MediaKind.Video
            });

            var report = new DatabaseNormalizer().Normalize(db);

            Assert.That(db.posts[0].mediaPath, Is.EqualTo("clip.mp4"));
            Assert.That(db.posts[0].imagePath, Is.Empty);
            Assert.That(report.warnings, Has.Some.Contains("video mediaPath"));
        }

        [Test]
        public void Migrate_FutureVersion_ReturnsUnsupportedWithoutChangingVersion()
        {
            var db = ValidDb();
            db.version = MigrationService.CurrentVersion + 1;

            var report = CreateMigrationService().Migrate(db);

            Assert.That(report.unsupportedFutureVersion, Is.True);
            Assert.That(report.fromVersion, Is.EqualTo(MigrationService.CurrentVersion + 1));
            Assert.That(report.toVersion, Is.EqualTo(MigrationService.CurrentVersion + 1));
            Assert.That(db.version, Is.EqualTo(MigrationService.CurrentVersion + 1));
        }

        [Test]
        public void Migrate_ZeroVersion_NormalizesToCurrentVersion()
        {
            var db = ValidDb();
            db.version = 0;
            db.posts.Add(new ScheduledPost { id = "p1", targetId = "t1", imagePath = "image.png" });

            var report = CreateMigrationService().Migrate(db);

            Assert.That(report.fromVersion, Is.Zero);
            Assert.That(report.toVersion, Is.EqualTo(MigrationService.CurrentVersion));
            Assert.That(db.version, Is.EqualTo(MigrationService.CurrentVersion));
            Assert.That(db.posts[0].mediaPath, Is.EqualTo("image.png"));
            Assert.That(db.posts[0].mediaKind, Is.EqualTo(MediaKind.Image));
            Assert.That(report.steps.Count, Is.EqualTo(4));
        }

        private static MigrationService CreateMigrationService()
        {
            return new MigrationService(new DatabaseNormalizer());
        }

        private static AppDatabase ValidDb()
        {
            var db = new AppDatabase();
            db.targets.Add(new Target { id = "t1", name = "Target" });
            return db;
        }
    }
}
