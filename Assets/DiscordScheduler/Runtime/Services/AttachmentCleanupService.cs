using System.Collections.Generic;

namespace DiscordScheduler
{
    public enum AttachmentCleanupAction
    {
        DeleteEligible = 0,
        Deleted = 1,
        ProtectedReferenced = 2,
        ProtectedActive = 3,
        Skipped = 4,
        DeleteFailed = 5
    }

    public sealed class AttachmentCleanupEntry
    {
        public string fileName = "";
        public long sizeBytes;
        public AttachmentCleanupAction action;
        public string reason = "";
        public bool deleted;
        public string error = "";
    }

    public sealed class AttachmentCleanupReport
    {
        public bool ok = true;
        public string error = "";
        public int scannedCount;
        public long scannedBytes;
        public int deleteEligibleCount;
        public long deleteEligibleBytes;
        public int deletedCount;
        public long deletedBytes;
        public int protectedReferencedCount;
        public int protectedActiveCount;
        public int skippedCount;
        public int failedDeleteCount;
        public List<AttachmentCleanupEntry> entries = new List<AttachmentCleanupEntry>();
    }

    public sealed class AttachmentCleanupService
    {
        private readonly AttachmentRepository _repository;

        public AttachmentCleanupService(AttachmentRepository repository)
        {
            _repository = repository;
        }

        public AttachmentCleanupReport DryRun(AppDatabase db)
        {
            return BuildReport(db, false);
        }

        public AttachmentCleanupReport CleanupOrphans(AppDatabase db)
        {
            return BuildReport(db, true);
        }

        private AttachmentCleanupReport BuildReport(AppDatabase db, bool execute)
        {
            var report = new AttachmentCleanupReport();
            if (_repository == null)
            {
                report.ok = false;
                report.error = "Attachment repository is missing.";
                return report;
            }

            var listResult = _repository.TryListFiles(out var files);
            if (!listResult.ok)
            {
                report.ok = false;
                report.error = listResult.error;
                return report;
            }

            var referenced = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var active = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            CollectReferences(db, referenced, active);

            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                report.scannedCount++;
                report.scannedBytes += file.sizeBytes;

                if (file.isReparsePoint)
                {
                    AddSkipped(report, file, "Reparse point skipped.");
                    continue;
                }

                if (active.Contains(file.fullPath))
                {
                    report.protectedActiveCount++;
                    report.entries.Add(CreateEntry(file, AttachmentCleanupAction.ProtectedActive, "Active or review attachment is protected."));
                    continue;
                }

                if (referenced.Contains(file.fullPath))
                {
                    report.protectedReferencedCount++;
                    report.entries.Add(CreateEntry(file, AttachmentCleanupAction.ProtectedReferenced, "Referenced attachment is protected."));
                    continue;
                }

                report.deleteEligibleCount++;
                report.deleteEligibleBytes += file.sizeBytes;

                if (!execute)
                {
                    report.entries.Add(CreateEntry(file, AttachmentCleanupAction.DeleteEligible, "Orphan attachment."));
                    continue;
                }

                var deleteResult = _repository.TryDeleteManagedFile(file.fullPath, out var deletedBytes);
                if (deleteResult.ok)
                {
                    report.deletedCount++;
                    report.deletedBytes += deletedBytes;
                    var entry = CreateEntry(file, AttachmentCleanupAction.Deleted, "Orphan attachment deleted.");
                    entry.deleted = true;
                    report.entries.Add(entry);
                }
                else
                {
                    report.ok = false;
                    report.failedDeleteCount++;
                    var entry = CreateEntry(file, AttachmentCleanupAction.DeleteFailed, "Orphan attachment delete failed.");
                    entry.error = deleteResult.error;
                    report.entries.Add(entry);
                }
            }

            return report;
        }

        private void CollectReferences(AppDatabase db, HashSet<string> referenced, HashSet<string> active)
        {
            if (db == null || db.posts == null)
                return;

            for (int i = 0; i < db.posts.Count; i++)
            {
                var post = db.posts[i];
                if (post == null)
                    continue;

                var path = post.EffectiveMediaPath();
                if (!_repository.TryGetManagedFullPath(path, out var fullPath))
                    continue;

                referenced.Add(fullPath);

                if (IsProtectedStatus(post.status))
                    active.Add(fullPath);
            }
        }

        private static bool IsProtectedStatus(PostStatus status)
        {
            return status == PostStatus.Pending ||
                   status == PostStatus.Sending ||
                   status == PostStatus.NeedsReview;
        }

        private static void AddSkipped(AttachmentCleanupReport report, AttachmentFile file, string reason)
        {
            report.skippedCount++;
            report.entries.Add(CreateEntry(file, AttachmentCleanupAction.Skipped, reason));
        }

        private static AttachmentCleanupEntry CreateEntry(AttachmentFile file, AttachmentCleanupAction action, string reason)
        {
            return new AttachmentCleanupEntry
            {
                fileName = file.fileName,
                sizeBytes = file.sizeBytes,
                action = action,
                reason = reason ?? ""
            };
        }
    }
}
