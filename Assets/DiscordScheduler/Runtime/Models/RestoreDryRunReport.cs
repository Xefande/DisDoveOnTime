namespace DiscordScheduler
{
    public sealed class RestoreDryRunReport
    {
        public bool ok = true;
        public bool canApply;
        public string error = "";
        public string policy = "ReplaceOnly";
        public int schemaVersion;
        public int targetCount;
        public int postCount;
        public int attachmentReferenceCount;
        public int brokenTargetReferenceCount;
        public int duplicateTargetIdCount;
        public int duplicatePostIdCount;
        public bool unsupportedFutureVersion;
        public bool requiresConfirmation = true;
        public string sourcePath = "";
        public long sourceBytes;
        public string dryRunToken = "";
        public string summary = "";
    }

    public sealed class RestoreApplyResult
    {
        public bool ok;
        public string error = "";
        public string summary = "";
        public string sourcePath = "";
        public string backupPath = "";
        public string dryRunToken = "";
        public bool backupCreated;
        public bool applied;
        public bool rollbackAttempted;
        public bool rollbackSucceeded;
        public bool rollbackFailed;
        public int targetCount;
        public int postCount;
    }
}
