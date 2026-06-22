namespace DiscordScheduler
{
    public sealed class RetentionDryRunReport
    {
        public bool ok = true;
        public string error = "";
        public bool dryRunOnly = true;
        public bool canApply;
        public string dryRunToken = "";
        public string policyFingerprint = "";
        public string generatedAtUtcIso = "";
        public int scannedFileCount;
        public long dataFolderBytes;
        public long attachmentBytes;
        public long journalBytes;
        public long backupBytes;
        public long exportBytes;
        public long logBytes;
        public int deleteEligibleAttachmentCount;
        public long deleteEligibleAttachmentBytes;
        public int protectedActiveAttachmentCount;
        public int protectedReferencedAttachmentCount;
        public bool quotaWarning;
        public bool lowDiskWarning;
        public string warning = "";
    }

    public sealed class RetentionApplyResult
    {
        public bool ok;
        public string error = "";
        public string appliedToken = "";
        public int deletedAttachmentCount;
        public long deletedAttachmentBytes;
        public int protectedAttachmentCount;
        public int failedDeleteCount;
        public string summary = "";
    }
}
