using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace DiscordScheduler
{
    public sealed class BackupRestoreService
    {
        private readonly DatabaseNormalizer _normalizer;
        private readonly MigrationService _migrationService;
        private readonly IPathProvider _pathProvider;
        private readonly IFileSystem _fileSystem;
        private readonly Func<DateTime> _utcNow;

        public BackupRestoreService()
            : this(new UnityPathProvider(), new SystemFileSystem())
        {
        }

        public BackupRestoreService(IPathProvider pathProvider, IFileSystem fileSystem, Func<DateTime> utcNow = null)
        {
            _normalizer = new DatabaseNormalizer();
            _migrationService = new MigrationService(_normalizer);
            _pathProvider = pathProvider ?? new UnityPathProvider();
            _fileSystem = fileSystem ?? new SystemFileSystem();
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public RestoreDryRunReport DryRunReplace(AppDatabase restoreSource)
        {
            PrepareCandidate(restoreSource, out _, out var report);
            return report;
        }

        public RestoreDryRunReport DryRunFile(string restoreFilePath)
        {
            PrepareReplaceFromFile(restoreFilePath, out _, out var report);
            return report;
        }

        public ValidationResult PrepareReplace(AppDatabase restoreSource, out AppDatabase preparedDatabase, out RestoreDryRunReport report)
        {
            return PrepareCandidate(restoreSource, out preparedDatabase, out report);
        }

        public ValidationResult PrepareReplaceFromFile(string restoreFilePath, out AppDatabase preparedDatabase, out RestoreDryRunReport report)
        {
            preparedDatabase = null;
            report = new RestoreDryRunReport
            {
                ok = false,
                canApply = false,
                sourcePath = SecretRedactor.Redact(restoreFilePath ?? "")
            };

            if (string.IsNullOrWhiteSpace(restoreFilePath))
            {
                report.error = "Restore file path is required.";
                return ValidationResult.Fail(report.error);
            }

            if (!_fileSystem.FileExists(restoreFilePath))
            {
                report.error = "Restore file does not exist.";
                return ValidationResult.Fail(report.error);
            }

            if (!_fileSystem.TryReadAllText(restoreFilePath, out var json, out var readError))
            {
                report.error = "Restore file cannot be read: " + SecretRedactor.RedactAndTruncate(readError);
                return ValidationResult.Fail(report.error);
            }

            if (_fileSystem.TryGetFileSizeBytes(restoreFilePath, out var sizeBytes, out _))
                report.sourceBytes = sizeBytes;
            else
                report.sourceBytes = Encoding.UTF8.GetByteCount(json ?? "");

            AppDatabase source;
            try
            {
                source = JsonUtility.FromJson<AppDatabase>(json);
            }
            catch (Exception exception)
            {
                report.error = "Restore file is not valid JSON: " + SecretRedactor.RedactAndTruncate(exception.Message);
                return ValidationResult.Fail(report.error);
            }

            var result = PrepareCandidate(source, out preparedDatabase, out var preparedReport);
            CopyReport(preparedReport, report);
            report.sourcePath = SecretRedactor.Redact(restoreFilePath);
            report.sourceBytes = report.sourceBytes > 0 ? report.sourceBytes : Encoding.UTF8.GetByteCount(json ?? "");
            report.dryRunToken = BuildDryRunToken(json, report);
            if (!string.IsNullOrWhiteSpace(report.summary))
                report.summary = $"sourceBytes={report.sourceBytes}, " + report.summary;
            return result;
        }

        public RestoreApplyResult ApplyReplaceFromFile(
            AppDatabase currentDatabase,
            string restoreFilePath,
            string expectedDryRunToken,
            Func<ValidationResult> saveDatabase,
            bool confirmed)
        {
            var result = new RestoreApplyResult
            {
                sourcePath = SecretRedactor.Redact(restoreFilePath ?? "")
            };

            if (!confirmed)
                return Fail(result, "Restore apply requires explicit confirmation.");

            if (currentDatabase == null)
                return Fail(result, "Restore apply requires the current database instance.");

            if (saveDatabase == null)
                return Fail(result, "Restore apply requires a save callback.");

            var prepare = PrepareReplaceFromFile(restoreFilePath, out var preparedDatabase, out var dryRun);
            result.dryRunToken = dryRun.dryRunToken;
            result.targetCount = dryRun.targetCount;
            result.postCount = dryRun.postCount;

            if (!prepare.ok || !dryRun.canApply)
                return Fail(result, dryRun.error);

            if (string.IsNullOrWhiteSpace(expectedDryRunToken) ||
                !string.Equals(expectedDryRunToken, dryRun.dryRunToken, StringComparison.Ordinal))
            {
                return Fail(result, "Restore dry-run is stale. Run dry-run again before applying.");
            }

            var rollbackSnapshot = CloneDatabase(currentDatabase);
            var backup = CreatePreRestoreBackup(out var backupPath, out var backupError);
            if (!backup.ok)
                return Fail(result, backupError);

            result.backupCreated = !string.IsNullOrWhiteSpace(backupPath);
            result.backupPath = SecretRedactor.Redact(backupPath);

            CopyDatabase(preparedDatabase, currentDatabase);
            var save = saveDatabase();
            if (save.ok)
            {
                result.ok = true;
                result.applied = true;
                result.summary = BuildApplySummary(result);
                return result;
            }

            result.rollbackAttempted = true;
            CopyDatabase(rollbackSnapshot, currentDatabase);
            var rollback = saveDatabase();
            if (rollback.ok)
            {
                result.rollbackSucceeded = true;
                return Fail(result, "Restore save failed and the previous database was restored: " + SecretRedactor.RedactAndTruncate(save.error));
            }

            result.rollbackFailed = true;
            return Fail(
                result,
                "Restore save failed and rollback save failed. Backup path: " + result.backupPath +
                ". Error: " + SecretRedactor.RedactAndTruncate(save.error) +
                " Rollback error: " + SecretRedactor.RedactAndTruncate(rollback.error));
        }

        private ValidationResult PrepareCandidate(AppDatabase restoreSource, out AppDatabase preparedDatabase, out RestoreDryRunReport report)
        {
            preparedDatabase = null;
            report = new RestoreDryRunReport();

            if (restoreSource == null)
            {
                report.ok = false;
                report.canApply = false;
                report.error = "Restore source is missing.";
                return ValidationResult.Fail(report.error);
            }

            var candidate = CloneDatabase(restoreSource);

            if (candidate.targets == null)
                candidate.targets = new List<Target>();
            if (candidate.posts == null)
                candidate.posts = new List<ScheduledPost>();
            if (candidate.settings == null)
                candidate.settings = new AppSettings();

            report.duplicateTargetIdCount = CountDuplicateTargetIds(candidate);
            report.duplicatePostIdCount = CountDuplicatePostIds(candidate);
            if (report.duplicateTargetIdCount > 0 || report.duplicatePostIdCount > 0)
            {
                report.ok = false;
                report.canApply = false;
                report.error = "Restore source contains duplicate IDs.";
                return ValidationResult.Fail(report.error);
            }

            var migration = _migrationService.Migrate(candidate);
            report.unsupportedFutureVersion = migration.unsupportedFutureVersion;
            if (migration.unsupportedFutureVersion)
            {
                report.ok = false;
                report.canApply = false;
                report.schemaVersion = candidate.version;
                report.error = "Restore source uses a future schema version.";
                return ValidationResult.Fail(report.error);
            }

            _normalizer.Normalize(candidate);
            report.brokenTargetReferenceCount = CountBrokenTargetReferences(candidate);
            if (report.brokenTargetReferenceCount > 0)
            {
                report.ok = false;
                report.canApply = false;
                report.schemaVersion = candidate.version;
                report.targetCount = candidate.targets.Count;
                report.postCount = candidate.posts.Count;
                report.attachmentReferenceCount = CountAttachmentReferences(candidate);
                report.error = "Restore source contains broken target references.";
                report.summary = BuildSummary(report);
                return ValidationResult.Fail(report.error);
            }

            report.schemaVersion = candidate.version;
            report.targetCount = candidate.targets.Count;
            report.postCount = candidate.posts.Count;
            report.attachmentReferenceCount = CountAttachmentReferences(candidate);
            report.canApply = true;
            report.summary = BuildSummary(report);
            preparedDatabase = candidate;
            return ValidationResult.Ok();
        }

        private ValidationResult CreatePreRestoreBackup(out string backupPath, out string error)
        {
            backupPath = "";
            error = "";

            var sourcePath = _pathProvider.DataFilePath;
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                error = "Current database path is empty.";
                return ValidationResult.Fail(error);
            }

            if (!_fileSystem.FileExists(sourcePath))
                return ValidationResult.Ok();

            string directory;
            try
            {
                directory = Path.GetDirectoryName(sourcePath);
            }
            catch (Exception exception)
            {
                error = "Cannot resolve database folder: " + SecretRedactor.RedactAndTruncate(exception.Message);
                return ValidationResult.Fail(error);
            }

            if (string.IsNullOrWhiteSpace(directory))
                directory = ".";

            var ensure = _fileSystem.EnsureDirectory(directory);
            if (!ensure.ok)
            {
                error = "Cannot prepare restore backup folder: " + SecretRedactor.RedactAndTruncate(ensure.error);
                return ValidationResult.Fail(error);
            }

            var name = Path.GetFileNameWithoutExtension(sourcePath);
            var ext = Path.GetExtension(sourcePath);
            var stamp = _utcNow().ToString("yyyyMMdd-HHmmss");

            for (int i = 0; i < 100; i++)
            {
                var suffix = i == 0 ? "" : "-" + i.ToString("00");
                backupPath = Path.Combine(directory, $"{name}.pre-restore-{stamp}{suffix}{ext}");
                if (_fileSystem.FileExists(backupPath))
                    continue;

                var copy = _fileSystem.CopyFile(sourcePath, backupPath, false);
                if (copy.ok)
                    return ValidationResult.Ok();

                error = "Cannot create pre-restore backup: " + SecretRedactor.RedactAndTruncate(copy.error);
                return ValidationResult.Fail(error);
            }

            error = "No available pre-restore backup filename.";
            return ValidationResult.Fail(error);
        }

        private static int CountDuplicateTargetIds(AppDatabase db)
        {
            var seen = new HashSet<string>();
            var duplicates = 0;
            for (int i = 0; i < db.targets.Count; i++)
            {
                var id = db.targets[i]?.id ?? "";
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                if (!seen.Add(id))
                    duplicates++;
            }
            return duplicates;
        }

        private static int CountDuplicatePostIds(AppDatabase db)
        {
            var seen = new HashSet<string>();
            var duplicates = 0;
            for (int i = 0; i < db.posts.Count; i++)
            {
                var id = db.posts[i]?.id ?? "";
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                if (!seen.Add(id))
                    duplicates++;
            }
            return duplicates;
        }

        private static int CountAttachmentReferences(AppDatabase db)
        {
            var count = 0;
            for (int i = 0; i < db.posts.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(db.posts[i]?.EffectiveMediaPath()))
                    count++;
            }
            return count;
        }

        private static int CountBrokenTargetReferences(AppDatabase db)
        {
            var targetIds = new HashSet<string>();
            for (int i = 0; i < db.targets.Count; i++)
            {
                var id = db.targets[i]?.id ?? "";
                if (!string.IsNullOrWhiteSpace(id))
                    targetIds.Add(id);
            }

            var broken = 0;
            for (int i = 0; i < db.posts.Count; i++)
            {
                var targetId = db.posts[i]?.targetId ?? "";
                if (!string.IsNullOrWhiteSpace(targetId) && !targetIds.Contains(targetId))
                    broken++;
            }

            return broken;
        }

        private static string BuildSummary(RestoreDryRunReport report)
        {
            return $"targets={report.targetCount}, posts={report.postCount}, attachments={report.attachmentReferenceCount}, " +
                   $"duplicateTargets={report.duplicateTargetIdCount}, duplicatePosts={report.duplicatePostIdCount}, brokenTargets={report.brokenTargetReferenceCount}.";
        }

        private static string BuildApplySummary(RestoreApplyResult result)
        {
            var backup = result.backupCreated ? ", backup=" + result.backupPath : ", backup=not-needed";
            return $"Restore applied: targets={result.targetCount}, posts={result.postCount}{backup}.";
        }

        private static RestoreApplyResult Fail(RestoreApplyResult result, string error)
        {
            result.ok = false;
            result.error = SecretRedactor.RedactAndTruncate(error ?? "Restore failed.");
            if (string.IsNullOrWhiteSpace(result.summary))
                result.summary = result.error;
            return result;
        }

        private static void CopyReport(RestoreDryRunReport source, RestoreDryRunReport destination)
        {
            destination.ok = source.ok;
            destination.canApply = source.canApply;
            destination.error = source.error;
            destination.policy = source.policy;
            destination.schemaVersion = source.schemaVersion;
            destination.targetCount = source.targetCount;
            destination.postCount = source.postCount;
            destination.attachmentReferenceCount = source.attachmentReferenceCount;
            destination.brokenTargetReferenceCount = source.brokenTargetReferenceCount;
            destination.duplicateTargetIdCount = source.duplicateTargetIdCount;
            destination.duplicatePostIdCount = source.duplicatePostIdCount;
            destination.unsupportedFutureVersion = source.unsupportedFutureVersion;
            destination.requiresConfirmation = source.requiresConfirmation;
            destination.summary = source.summary;
        }

        private static string BuildDryRunToken(string json, RestoreDryRunReport report)
        {
            using (var sha = SHA256.Create())
            {
                var material = (json ?? "") + "|" + report.schemaVersion + "|" + report.targetCount + "|" + report.postCount + "|" + report.brokenTargetReferenceCount;
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(material));
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static void CopyDatabase(AppDatabase source, AppDatabase destination)
        {
            destination.version = source.version;
            destination.settings = CloneSettings(source.settings);
            destination.targets = new List<Target>();
            destination.posts = new List<ScheduledPost>();

            if (source.targets != null)
            {
                for (int i = 0; i < source.targets.Count; i++)
                    destination.targets.Add(CloneTarget(source.targets[i]));
            }

            if (source.posts != null)
            {
                for (int i = 0; i < source.posts.Count; i++)
                    destination.posts.Add(ClonePost(source.posts[i]));
            }
        }

        private static AppDatabase CloneDatabase(AppDatabase source)
        {
            var clone = new AppDatabase
            {
                version = source.version,
                settings = CloneSettings(source.settings),
                targets = new List<Target>(),
                posts = new List<ScheduledPost>()
            };

            if (source.targets != null)
            {
                for (int i = 0; i < source.targets.Count; i++)
                    clone.targets.Add(CloneTarget(source.targets[i]));
            }

            if (source.posts != null)
            {
                for (int i = 0; i < source.posts.Count; i++)
                    clone.posts.Add(ClonePost(source.posts[i]));
            }

            return clone;
        }

        private static AppSettings CloneSettings(AppSettings settings)
        {
            settings = settings ?? new AppSettings();
            return new AppSettings
            {
                sleepThresholdMinutes = settings.sleepThresholdMinutes,
                defaultAllowedMentions = CloneAllowedMentions(settings.defaultAllowedMentions),
                defaultOffPolicy = settings.defaultOffPolicy,
                defaultSleepPolicy = settings.defaultSleepPolicy
            };
        }

        private static Target CloneTarget(Target target)
        {
            if (target == null)
                return null;

            return new Target
            {
                id = target.id,
                name = target.name,
                serverLabel = target.serverLabel,
                channelLabel = target.channelLabel,
                webhookUrl = target.webhookUrl,
                webhookSecretRef = target.webhookSecretRef,
                overrideUsername = target.overrideUsername,
                overrideAvatarUrl = target.overrideAvatarUrl
            };
        }

        private static ScheduledPost ClonePost(ScheduledPost post)
        {
            if (post == null)
                return null;

            return new ScheduledPost
            {
                id = post.id,
                targetId = post.targetId,
                title = post.title,
                body = post.body,
                scheduledAtUtcIso = post.scheduledAtUtcIso,
                imagePath = post.imagePath,
                mediaKind = post.mediaKind,
                mediaPath = post.mediaPath,
                allowedMentions = CloneAllowedMentions(post.allowedMentions),
                missedPolicyIfOff = post.missedPolicyIfOff,
                missedPolicyIfSleep = post.missedPolicyIfSleep,
                sendAsEmbed = post.sendAsEmbed,
                status = post.status,
                createdAtUtcIso = post.createdAtUtcIso,
                updatedAtUtcIso = post.updatedAtUtcIso,
                lastError = post.lastError,
                retries = post.retries,
                lastAttemptAtUtcIso = post.lastAttemptAtUtcIso,
                nextAttemptAtUtcIso = post.nextAttemptAtUtcIso,
                lastDiscordMessageId = post.lastDiscordMessageId
            };
        }

        private static AllowedMentions CloneAllowedMentions(AllowedMentions mentions)
        {
            mentions = mentions ?? new AllowedMentions();
            return new AllowedMentions
            {
                allowUsers = mentions.allowUsers,
                allowRoles = mentions.allowRoles,
                allowEveryone = mentions.allowEveryone,
                userIdsCsv = mentions.userIdsCsv,
                roleIdsCsv = mentions.roleIdsCsv
            };
        }
    }
}
