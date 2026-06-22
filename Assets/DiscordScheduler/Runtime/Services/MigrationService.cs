using System.Collections.Generic;

namespace DiscordScheduler
{
    public sealed class MigrationService
    {
        public const int CurrentVersion = 4;

        private readonly DatabaseNormalizer _normalizer;

        public MigrationService(DatabaseNormalizer normalizer)
        {
            _normalizer = normalizer;
        }

        public MigrationReport Migrate(AppDatabase db)
        {
            var report = new MigrationReport();
            if (db == null)
                return report;

            report.fromVersion = db.version;

            if (db.version > CurrentVersion)
            {
                report.toVersion = db.version;
                report.unsupportedFutureVersion = true;
                report.steps.Add("Future database version is not migrated automatically.");
                return report;
            }

            if (db.version < 1)
            {
                db.version = 1;
                report.steps.Add("v0->v1 normalized invalid database version.");
            }

            if (db.version < 2)
            {
                _normalizer.Normalize(db);
                db.version = 2;
                report.steps.Add("v1->v2 normalized nested fields and legacy media.");
            }

            if (db.version < 3)
            {
                MigrateLegacyImagePath(db.posts);
                db.version = 3;
                report.steps.Add("v2->v3 normalized mediaKind/mediaPath while keeping legacy imagePath compatibility.");
            }

            if (db.version < 4)
            {
                db.version = 4;
                report.steps.Add("v3->v4 reserved send-review metadata defaults.");
            }

            report.toVersion = db.version;
            return report;
        }

        private static void MigrateLegacyImagePath(List<ScheduledPost> posts)
        {
            if (posts == null)
                return;

            for (int i = 0; i < posts.Count; i++)
            {
                var post = posts[i];
                if (post == null)
                    continue;

                if (string.IsNullOrWhiteSpace(post.mediaPath) && !string.IsNullOrWhiteSpace(post.imagePath))
                {
                    post.mediaPath = post.imagePath;
                    if (post.mediaKind == MediaKind.None)
                        post.mediaKind = MediaKind.Image;
                }
            }
        }
    }
}
