using System;
using System.Collections.Generic;

namespace DiscordScheduler
{
    [Serializable]
    public sealed class SupportBundleOptions
    {
        public bool includeLogs = true;
        public bool includeHealth = true;
        public bool includeConfigSummary = true;
        public bool includeAttemptSummary = true;
        public bool includeRawDatabase = false;
        public bool includeRawMedia = false;
        public int maxLogLines = 200;
    }

    [Serializable]
    public sealed class SupportBundleManifestItem
    {
        public string name = "";
        public string source = "";
        public bool included;
        public bool rawData;
        public string redactionPolicy = "";
        public string reason = "";
    }

    [Serializable]
    public sealed class SupportBundleDryRun
    {
        public bool ok;
        public string error = "";
        public string dryRunToken = "";
        public string generatedAtUtcIso = "";
        public string shareSafetyVerdict = "";
        public string redactionConfidence = "";
        public int targetCount;
        public int postCount;
        public int logLineCount;
        public int includedCount;
        public int excludedCount;
        public int redactedCount;
        public int sensitiveExcludedCount;
        public bool rawDatabaseExcluded = true;
        public bool rawMediaExcluded = true;
        public List<SupportBundleManifestItem> manifest = new List<SupportBundleManifestItem>();
        public List<string> partialFailures = new List<string>();
        public List<string> nextActions = new List<string>();
    }

    [Serializable]
    public sealed class SupportBundleExportResult
    {
        public bool ok;
        public string error = "";
        public string outputFolder = "";
        public string manifestPath = "";
        public string summaryPath = "";
        public string diagnosticsPath = "";
        public string logPath = "";
        public List<string> writtenFiles = new List<string>();
        public List<string> omitted = new List<string>();
        public List<string> partialFailures = new List<string>();
    }
}
