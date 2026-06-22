using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DiscordScheduler
{
    public sealed class RetentionPolicyService
    {
        private readonly AttachmentCleanupService _attachmentCleanup;
        private readonly Func<DateTime> _utcNow;

        public RetentionPolicyService(AttachmentCleanupService attachmentCleanup, Func<DateTime> utcNow = null)
        {
            _attachmentCleanup = attachmentCleanup;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public RetentionDryRunReport DryRun(AppDatabase db, string dataFolder, long quotaWarningBytes = 0, long lowDiskWarningBytes = 0)
        {
            var report = new RetentionDryRunReport();

            AddAttachmentSummary(db, report);
            AddDataFolderSummary(dataFolder, report);

            if (quotaWarningBytes > 0 && report.dataFolderBytes > quotaWarningBytes)
            {
                report.quotaWarning = true;
                AppendWarning(report, "Data folder is over the configured quota warning threshold.");
            }

            if (lowDiskWarningBytes > 0 && IsLowDisk(dataFolder, lowDiskWarningBytes))
            {
                report.lowDiskWarning = true;
                AppendWarning(report, "Available disk space is below the configured warning threshold.");
            }

            FinalizeDryRun(report);
            return report;
        }

        public RetentionApplyResult Apply(AppDatabase db, string dataFolder, RetentionDryRunReport dryRun, bool confirmed)
        {
            var result = new RetentionApplyResult();
            if (!confirmed)
            {
                result.error = "Retention apply requires explicit confirmation.";
                return result;
            }

            if (dryRun == null || string.IsNullOrWhiteSpace(dryRun.dryRunToken))
            {
                result.error = "Retention apply requires a prior dry-run token.";
                return result;
            }

            var current = DryRun(db, dataFolder);
            if (!current.ok)
            {
                result.error = current.error;
                return result;
            }

            if (!string.Equals(current.policyFingerprint, dryRun.policyFingerprint, StringComparison.Ordinal))
            {
                result.error = "Retention dry-run is stale. Refresh dry-run before applying.";
                return result;
            }

            if (_attachmentCleanup == null)
            {
                result.error = "Attachment cleanup service is missing.";
                return result;
            }

            var cleanup = _attachmentCleanup.CleanupOrphans(db);
            result.ok = cleanup.ok;
            result.error = cleanup.ok ? "" : cleanup.error;
            result.appliedToken = dryRun.dryRunToken;
            result.deletedAttachmentCount = cleanup.deletedCount;
            result.deletedAttachmentBytes = cleanup.deletedBytes;
            result.protectedAttachmentCount = cleanup.protectedActiveCount + cleanup.protectedReferencedCount;
            result.failedDeleteCount = cleanup.failedDeleteCount;
            result.summary = $"Deleted {result.deletedAttachmentCount} attachment(s), freed {result.deletedAttachmentBytes} bytes, protected {result.protectedAttachmentCount}, failed {result.failedDeleteCount}.";
            return result;
        }

        private void AddAttachmentSummary(AppDatabase db, RetentionDryRunReport report)
        {
            if (_attachmentCleanup == null)
                return;

            var cleanup = _attachmentCleanup.DryRun(db);
            if (!cleanup.ok)
            {
                report.ok = false;
                report.error = cleanup.error;
                return;
            }

            report.attachmentBytes = cleanup.scannedBytes;
            report.deleteEligibleAttachmentCount = cleanup.deleteEligibleCount;
            report.deleteEligibleAttachmentBytes = cleanup.deleteEligibleBytes;
            report.protectedActiveAttachmentCount = cleanup.protectedActiveCount;
            report.protectedReferencedAttachmentCount = cleanup.protectedReferencedCount;
        }

        private static void AddDataFolderSummary(string dataFolder, RetentionDryRunReport report)
        {
            if (string.IsNullOrWhiteSpace(dataFolder) || !Directory.Exists(dataFolder))
                return;

            try
            {
                var files = Directory.GetFiles(dataFolder, "*", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < files.Length; i++)
                {
                    var size = GetFileSizeSafe(files[i]);
                    var name = Path.GetFileName(files[i]) ?? "";
                    var ext = (Path.GetExtension(files[i]) ?? "").ToLowerInvariant();

                    report.scannedFileCount++;
                    report.dataFolderBytes += size;

                    if (name.IndexOf("send_attempts", StringComparison.OrdinalIgnoreCase) >= 0)
                        report.journalBytes += size;
                    else if (name.IndexOf("backup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             name.IndexOf("corrupt-", StringComparison.OrdinalIgnoreCase) >= 0)
                        report.backupBytes += size;
                    else if (name.IndexOf("export", StringComparison.OrdinalIgnoreCase) >= 0)
                        report.exportBytes += size;
                    else if (ext == ".log" || ext == ".txt")
                        report.logBytes += size;
                }
            }
            catch (Exception exception)
            {
                report.ok = false;
                report.error = "Retention dry-run failed: " + exception.GetType().Name;
            }
        }

        private static bool IsLowDisk(string dataFolder, long thresholdBytes)
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(dataFolder));
                if (string.IsNullOrWhiteSpace(root))
                    return false;

                var drive = new DriveInfo(root);
                return drive.AvailableFreeSpace < thresholdBytes;
            }
            catch
            {
                return false;
            }
        }

        private static long GetFileSizeSafe(string path)
        {
            try
            {
                return File.Exists(path) ? new FileInfo(path).Length : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static void AppendWarning(RetentionDryRunReport report, string warning)
        {
            if (string.IsNullOrWhiteSpace(warning))
                return;

            if (string.IsNullOrWhiteSpace(report.warning))
                report.warning = warning;
            else
                report.warning += " " + warning;
        }

        private void FinalizeDryRun(RetentionDryRunReport report)
        {
            report.generatedAtUtcIso = TimeUtil.ToIsoUtc(_utcNow());
            report.policyFingerprint = BuildFingerprint(
                report.ok.ToString(),
                report.scannedFileCount.ToString(),
                report.dataFolderBytes.ToString(),
                report.attachmentBytes.ToString(),
                report.deleteEligibleAttachmentCount.ToString(),
                report.deleteEligibleAttachmentBytes.ToString(),
                report.protectedActiveAttachmentCount.ToString(),
                report.protectedReferencedAttachmentCount.ToString(),
                report.journalBytes.ToString(),
                report.backupBytes.ToString(),
                report.exportBytes.ToString(),
                report.logBytes.ToString());
            report.dryRunToken = BuildFingerprint("retention", report.policyFingerprint, report.generatedAtUtcIso);
            report.canApply = report.ok && report.deleteEligibleAttachmentCount > 0;
        }

        private static string BuildFingerprint(params string[] parts)
        {
            using (var sha = SHA256.Create())
            {
                var text = string.Join("\n", parts ?? new string[0]);
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                    sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
