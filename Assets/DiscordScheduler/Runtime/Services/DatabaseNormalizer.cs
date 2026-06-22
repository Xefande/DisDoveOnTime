using System.Collections.Generic;
using System.IO;

namespace DiscordScheduler
{
    public sealed class DatabaseNormalizer
    {
        public DatabaseNormalizationReport Normalize(AppDatabase db)
        {
            var report = new DatabaseNormalizationReport();

            if (db == null)
            {
                report.warnings.Add("AppDatabase was null; caller must create a new database before normalization.");
                return report;
            }

            NormalizeCollections(db, report);
            NormalizeSettings(db.settings, report);
            NormalizeTargets(db.targets, report);
            NormalizePosts(db.posts, db.targets, report);

            return report;
        }

        private static void NormalizeCollections(AppDatabase db, DatabaseNormalizationReport report)
        {
            if (db.targets == null)
            {
                db.targets = new List<Target>();
                report.fixedTargets++;
            }

            if (db.posts == null)
            {
                db.posts = new List<ScheduledPost>();
                report.fixedPosts++;
            }

            if (db.settings == null)
            {
                db.settings = new AppSettings();
                report.fixedSettings++;
            }
        }

        private static void NormalizeSettings(AppSettings settings, DatabaseNormalizationReport report)
        {
            if (settings.defaultAllowedMentions == null)
            {
                settings.defaultAllowedMentions = new AllowedMentions();
                report.fixedSettings++;
            }

            settings.defaultOffPolicy = NormalizeMissedPolicy(settings.defaultOffPolicy, report, "settings.defaultOffPolicy", true);
            settings.defaultSleepPolicy = NormalizeMissedPolicy(settings.defaultSleepPolicy, report, "settings.defaultSleepPolicy", true);
        }

        private static void NormalizeTargets(List<Target> targets, DatabaseNormalizationReport report)
        {
            var seenTargetIds = new HashSet<string>();

            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target == null)
                {
                    report.warnings.Add($"Target at index {i} is null.");
                    continue;
                }

                var id = target.id ?? "";
                if (string.IsNullOrWhiteSpace(id))
                {
                    report.warnings.Add($"Target at index {i} has missing id.");
                    continue;
                }

                if (!seenTargetIds.Add(id))
                    report.warnings.Add($"Duplicate target id detected at target index {i}.");
            }
        }

        private static void NormalizePosts(List<ScheduledPost> posts, List<Target> targets, DatabaseNormalizationReport report)
        {
            var seenPostIds = new HashSet<string>();
            var targetIds = BuildTargetIdSet(targets);

            for (int i = 0; i < posts.Count; i++)
            {
                var post = posts[i];
                if (post == null)
                {
                    report.warnings.Add($"Post at index {i} is null.");
                    continue;
                }

                NormalizePostId(post, i, seenPostIds, report);
                NormalizePostTargetRef(post, i, targetIds, report);
                NormalizePostDefaults(post, report);
                NormalizePostEnums(post, i, report);
                NormalizePostMedia(post, i, report);

                if (post.status == PostStatus.Sending)
                    report.warnings.Add($"Post at index {i} is in stale Sending state; app startup recovery should move it to NeedsReview.");
            }
        }

        private static HashSet<string> BuildTargetIdSet(List<Target> targets)
        {
            var ids = new HashSet<string>();

            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target == null || string.IsNullOrWhiteSpace(target.id))
                    continue;

                ids.Add(target.id);
            }

            return ids;
        }

        private static void NormalizePostId(
            ScheduledPost post,
            int index,
            HashSet<string> seenPostIds,
            DatabaseNormalizationReport report)
        {
            var id = post.id ?? "";
            if (string.IsNullOrWhiteSpace(id))
            {
                report.warnings.Add($"Post at index {index} has missing id.");
                return;
            }

            if (!seenPostIds.Add(id))
                report.warnings.Add($"Duplicate post id detected at post index {index}.");
        }

        private static void NormalizePostTargetRef(
            ScheduledPost post,
            int index,
            HashSet<string> targetIds,
            DatabaseNormalizationReport report)
        {
            var targetId = post.targetId ?? "";
            if (string.IsNullOrWhiteSpace(targetId))
            {
                report.warnings.Add($"Post at index {index} has missing targetId.");
                return;
            }

            if (!targetIds.Contains(targetId))
                report.warnings.Add($"Post at index {index} references missing targetId.");
        }

        private static void NormalizePostDefaults(ScheduledPost post, DatabaseNormalizationReport report)
        {
            if (post.allowedMentions == null)
            {
                post.allowedMentions = new AllowedMentions();
                report.fixedPosts++;
            }

            if (post.imagePath == null)
            {
                post.imagePath = "";
                report.fixedPosts++;
            }

            if (post.mediaPath == null)
            {
                post.mediaPath = "";
                report.fixedPosts++;
            }

            if (post.lastAttemptAtUtcIso == null)
            {
                post.lastAttemptAtUtcIso = "";
                report.fixedPosts++;
            }

            if (post.nextAttemptAtUtcIso == null)
            {
                post.nextAttemptAtUtcIso = "";
                report.fixedPosts++;
            }

            if (post.lastDiscordMessageId == null)
            {
                post.lastDiscordMessageId = "";
                report.fixedPosts++;
            }
        }

        private static void NormalizePostEnums(ScheduledPost post, int index, DatabaseNormalizationReport report)
        {
            if (!IsValidPostStatus(post.status))
            {
                report.warnings.Add($"Invalid PostStatus {(int)post.status} on post index {index}; clamped to Pending.");
                post.status = PostStatus.Pending;
                report.fixedPosts++;
            }

            post.missedPolicyIfOff = NormalizeMissedPolicy(post.missedPolicyIfOff, report, "post.missedPolicyIfOff", false);
            post.missedPolicyIfSleep = NormalizeMissedPolicy(post.missedPolicyIfSleep, report, "post.missedPolicyIfSleep", false);

            if (!IsValidMediaKind(post.mediaKind))
            {
                report.warnings.Add($"Invalid MediaKind {(int)post.mediaKind} on post index {index}; clamped to None.");
                post.mediaKind = MediaKind.None;
                report.fixedPosts++;
            }
        }

        private static MissedPolicy NormalizeMissedPolicy(
            MissedPolicy policy,
            DatabaseNormalizationReport report,
            string field,
            bool settingsField)
        {
            if (IsValidMissedPolicy(policy))
                return policy;

            report.warnings.Add($"Invalid MissedPolicy {(int)policy} in {field}; clamped to SendOnNextRun.");

            if (settingsField)
                report.fixedSettings++;
            else
                report.fixedPosts++;

            return MissedPolicy.SendOnNextRun;
        }

        private static void NormalizePostMedia(ScheduledPost post, int index, DatabaseNormalizationReport report)
        {
            var mediaPath = post.mediaPath ?? "";
            var imagePath = post.imagePath ?? "";

            if (string.IsNullOrWhiteSpace(mediaPath) && !string.IsNullOrWhiteSpace(imagePath))
            {
                post.mediaPath = imagePath;
                post.mediaKind = MediaKind.Image;
                report.fixedPosts++;
                return;
            }

            if (string.IsNullOrWhiteSpace(mediaPath))
                return;

            if (post.mediaKind == MediaKind.None)
            {
                post.mediaKind = GuessKindFromPath(mediaPath);
                report.fixedPosts++;
            }

            if (post.mediaKind == MediaKind.Image && string.IsNullOrWhiteSpace(imagePath))
            {
                post.imagePath = mediaPath;
                report.fixedPosts++;
            }

            if (post.mediaKind == MediaKind.Video && !string.IsNullOrWhiteSpace(imagePath))
            {
                post.imagePath = "";
                report.fixedPosts++;
                report.warnings.Add($"Post at index {index} had video mediaPath with legacy imagePath; mediaPath kept.");
            }
        }

        private static bool IsValidPostStatus(PostStatus status)
        {
            var value = (int)status;
            return value >= (int)PostStatus.Pending && value <= (int)PostStatus.NeedsReview;
        }

        private static bool IsValidMissedPolicy(MissedPolicy policy)
        {
            var value = (int)policy;
            return value >= (int)MissedPolicy.SendOnNextRun && value <= (int)MissedPolicy.MarkFailed;
        }

        private static bool IsValidMediaKind(MediaKind kind)
        {
            var value = (int)kind;
            return value >= (int)MediaKind.None && value <= (int)MediaKind.Video;
        }

        private static MediaKind GuessKindFromPath(string path)
        {
            var ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            return MediaAttachmentRules.GuessKindFromExt(ext);
        }
    }
}
