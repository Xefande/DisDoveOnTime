using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace DiscordScheduler
{
    public sealed class SupportBundleService
    {
        private readonly IFileSystem _fileSystem;
        private readonly Func<DateTime> _utcNow;

        public SupportBundleService(IFileSystem fileSystem = null, Func<DateTime> utcNow = null)
        {
            _fileSystem = fileSystem ?? new SystemFileSystem();
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public SupportBundleDryRun DryRun(
            AppDatabase db,
            HealthSnapshot health,
            IList<string> logLines,
            SupportBundleOptions options = null)
        {
            options = NormalizeOptions(options);
            var dryRun = new SupportBundleDryRun
            {
                ok = true,
                generatedAtUtcIso = TimeUtil.ToIsoUtc(_utcNow()),
                shareSafetyVerdict = "Safe to share: raw database, raw media and webhook tokens are excluded.",
                redactionConfidence = "high",
                targetCount = db?.targets?.Count ?? 0,
                postCount = db?.posts?.Count ?? 0,
                logLineCount = logLines?.Count ?? 0,
                rawDatabaseExcluded = true,
                rawMediaExcluded = true
            };

            if (options.includeRawDatabase || options.includeRawMedia)
            {
                dryRun.ok = false;
                dryRun.error = "Safe support bundle profile does not allow raw database or raw media export.";
                dryRun.shareSafetyVerdict = "Not safe to share until raw database/media export is disabled.";
                dryRun.redactionConfidence = "blocked";
            }

            AddManifest(dryRun, "manifest.json", "generated", true, false, "metadata-only", "Lists included, excluded and redacted items.");
            AddManifest(dryRun, "summary.md", "generated", true, false, "redacted-summary", "Human-readable support summary.");
            AddManifest(dryRun, "diagnostics.json", "generated", true, false, "redacted-json", "Machine-readable diagnostics without raw secrets.");

            if (options.includeLogs)
                AddManifest(dryRun, "logs.txt", "LogService.Snapshot", true, false, "redacted-and-truncated", "Recent app log lines.");
            else
                AddManifest(dryRun, "logs.txt", "LogService.Snapshot", false, false, "omitted", "Logs disabled by support bundle options.");

            AddManifest(dryRun, "discord_scheduler_db.json", "StorageService", false, true, "omitted-raw-default-deny", "Raw database is excluded by the safe profile.");
            AddManifest(dryRun, "attachments/*", "AttachmentRepository", false, true, "omitted-raw-default-deny", "Raw media files are excluded by the safe profile.");

            dryRun.includedCount = dryRun.manifest.Count(item => item.included);
            dryRun.excludedCount = dryRun.manifest.Count(item => !item.included);
            dryRun.redactedCount = dryRun.manifest.Count(item => item.included && item.redactionPolicy.IndexOf("redacted", StringComparison.OrdinalIgnoreCase) >= 0);
            dryRun.sensitiveExcludedCount = dryRun.manifest.Count(item => !item.included && item.rawData);
            dryRun.nextActions.Add("Review the generated summary before sharing.");
            dryRun.nextActions.Add("Share the support bundle folder only if the manifest verdict is safe.");
            dryRun.nextActions.Add("Do not attach raw database or media files separately.");
            dryRun.dryRunToken = BuildToken(dryRun, options, health);

            return dryRun;
        }

        public SupportBundleExportResult Export(
            SupportBundleDryRun dryRun,
            AppDatabase db,
            HealthSnapshot health,
            IList<string> logLines,
            string outputFolder,
            bool confirmed,
            SupportBundleOptions options = null)
        {
            var result = new SupportBundleExportResult();

            if (dryRun == null || string.IsNullOrWhiteSpace(dryRun.dryRunToken))
                return Fail(result, "Support bundle export requires a dry-run token.");

            if (!dryRun.ok)
                return Fail(result, dryRun.error);

            if (!confirmed)
                return Fail(result, "Support bundle export requires explicit confirmation.");

            if (string.IsNullOrWhiteSpace(outputFolder))
                return Fail(result, "Support bundle output folder is required.");

            options = NormalizeOptions(options);
            if (options.includeRawDatabase || options.includeRawMedia)
                return Fail(result, "Raw database/media export is not available in the safe support profile.");

            var ensure = _fileSystem.EnsureDirectory(outputFolder);
            if (!ensure.ok)
                return Fail(result, "Failed to create support bundle folder: " + SecretRedactor.RedactAndTruncate(ensure.error));

            result.outputFolder = SecretRedactor.Redact(outputFolder);

            WriteFile(result, outputFolder, "manifest.json", BuildManifestJson(dryRun));
            WriteFile(result, outputFolder, "summary.md", BuildSummaryMarkdown(dryRun, db, health, logLines, options));
            WriteFile(result, outputFolder, "diagnostics.json", BuildDiagnosticsJson(dryRun, db, health, logLines, options));

            if (options.includeLogs)
                WriteFile(result, outputFolder, "logs.txt", BuildLogs(logLines, options.maxLogLines));

            result.ok = result.partialFailures.Count == 0;
            if (!result.ok)
                result.error = "Support bundle export finished with write failures.";

            result.manifestPath = RedactedPath(outputFolder, "manifest.json");
            result.summaryPath = RedactedPath(outputFolder, "summary.md");
            result.diagnosticsPath = RedactedPath(outputFolder, "diagnostics.json");
            result.logPath = options.includeLogs ? RedactedPath(outputFolder, "logs.txt") : "";

            foreach (var item in dryRun.manifest)
            {
                if (!item.included)
                    result.omitted.Add(item.name + " - " + item.reason);
            }

            return result;
        }

        private static SupportBundleOptions NormalizeOptions(SupportBundleOptions options)
        {
            options = options ?? new SupportBundleOptions();
            if (options.maxLogLines <= 0)
                options.maxLogLines = 200;
            return options;
        }

        private static void AddManifest(
            SupportBundleDryRun dryRun,
            string name,
            string source,
            bool included,
            bool rawData,
            string redactionPolicy,
            string reason)
        {
            dryRun.manifest.Add(new SupportBundleManifestItem
            {
                name = name,
                source = source,
                included = included,
                rawData = rawData,
                redactionPolicy = redactionPolicy,
                reason = reason
            });
        }

        private static string BuildToken(SupportBundleDryRun dryRun, SupportBundleOptions options, HealthSnapshot health)
        {
            var seed = string.Join("|",
                dryRun.generatedAtUtcIso,
                dryRun.targetCount.ToString(),
                dryRun.postCount.ToString(),
                dryRun.logLineCount.ToString(),
                (health?.summary ?? ""),
                options.includeLogs.ToString(),
                options.includeHealth.ToString(),
                options.includeConfigSummary.ToString(),
                options.includeAttemptSummary.ToString());

            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
                var sb = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                    sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private void WriteFile(SupportBundleExportResult result, string folder, string name, string contents)
        {
            var path = CombinePath(folder, name);
            var write = _fileSystem.WriteAllText(path, SecretRedactor.Redact(contents ?? ""));
            if (!write.ok)
            {
                result.partialFailures.Add(name + ": " + SecretRedactor.RedactAndTruncate(write.error));
                return;
            }

            result.writtenFiles.Add(name);
        }

        private static SupportBundleExportResult Fail(SupportBundleExportResult result, string error)
        {
            result.ok = false;
            result.error = SecretRedactor.RedactAndTruncate(error);
            return result;
        }

        private static string BuildManifestJson(SupportBundleDryRun dryRun)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            AppendJsonProperty(sb, "generatedAtUtcIso", dryRun.generatedAtUtcIso, true);
            AppendJsonProperty(sb, "shareSafetyVerdict", dryRun.shareSafetyVerdict, true);
            AppendJsonProperty(sb, "redactionConfidence", dryRun.redactionConfidence, true);
            AppendJsonProperty(sb, "dryRunToken", dryRun.dryRunToken, true);
            sb.Append("  \"items\": [\n");
            for (int i = 0; i < dryRun.manifest.Count; i++)
            {
                var item = dryRun.manifest[i];
                sb.Append("    {");
                AppendInlineJsonProperty(sb, "name", item.name, true);
                AppendInlineJsonProperty(sb, "source", item.source, true);
                sb.Append("\"included\": ").Append(item.included ? "true" : "false").Append(", ");
                sb.Append("\"rawData\": ").Append(item.rawData ? "true" : "false").Append(", ");
                AppendInlineJsonProperty(sb, "redactionPolicy", item.redactionPolicy, true);
                AppendInlineJsonProperty(sb, "reason", item.reason, false);
                sb.Append("}");
                if (i < dryRun.manifest.Count - 1) sb.Append(",");
                sb.Append("\n");
            }
            sb.Append("  ]\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        private static string BuildSummaryMarkdown(
            SupportBundleDryRun dryRun,
            AppDatabase db,
            HealthSnapshot health,
            IList<string> logLines,
            SupportBundleOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# DisDove support bundle");
            sb.AppendLine();
            sb.AppendLine("- Verdict: " + dryRun.shareSafetyVerdict);
            sb.AppendLine("- Generated UTC: " + dryRun.generatedAtUtcIso);
            sb.AppendLine("- Targets: " + dryRun.targetCount);
            sb.AppendLine("- Posts: " + dryRun.postCount);
            sb.AppendLine("- Included files: " + dryRun.includedCount);
            sb.AppendLine("- Excluded sensitive items: " + dryRun.sensitiveExcludedCount);

            if (options.includeHealth && health != null)
            {
                sb.AppendLine();
                sb.AppendLine("## Health");
                sb.AppendLine("- Summary: " + Clean(health.summary));
                sb.AppendLine("- Warnings: " + health.warningCount);
                sb.AppendLine("- NeedsReview: " + health.needsReviewCount);
                sb.AppendLine("- Active backoff: " + health.activeBackoffCount);
            }

            if (options.includeConfigSummary)
            {
                sb.AppendLine();
                sb.AppendLine("## Config summary");
                AppendTargetSummary(sb, db);
            }

            if (options.includeLogs)
            {
                sb.AppendLine();
                sb.AppendLine("## Log summary");
                sb.AppendLine("- Log lines included: " + Math.Min(logLines?.Count ?? 0, options.maxLogLines));
            }

            sb.AppendLine();
            sb.AppendLine("## Omitted by default");
            foreach (var item in dryRun.manifest.Where(item => !item.included))
                sb.AppendLine("- " + item.name + ": " + item.reason);

            return sb.ToString();
        }

        private static string BuildDiagnosticsJson(
            SupportBundleDryRun dryRun,
            AppDatabase db,
            HealthSnapshot health,
            IList<string> logLines,
            SupportBundleOptions options)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            AppendJsonProperty(sb, "generatedAtUtcIso", dryRun.generatedAtUtcIso, true);
            AppendJsonProperty(sb, "shareSafetyVerdict", dryRun.shareSafetyVerdict, true);
            sb.Append("  \"counts\": {\n");
            sb.Append("    \"targets\": ").Append(dryRun.targetCount).Append(",\n");
            sb.Append("    \"posts\": ").Append(dryRun.postCount).Append(",\n");
            sb.Append("    \"logs\": ").Append(Math.Min(logLines?.Count ?? 0, options.maxLogLines)).Append("\n");
            sb.Append("  },\n");
            sb.Append("  \"health\": {\n");
            AppendJsonProperty(sb, "summary", health?.summary ?? "", true, 4);
            sb.Append("    \"warningCount\": ").Append(health?.warningCount ?? 0).Append(",\n");
            sb.Append("    \"needsReviewCount\": ").Append(health?.needsReviewCount ?? 0).Append(",\n");
            sb.Append("    \"activeBackoffCount\": ").Append(health?.activeBackoffCount ?? 0).Append("\n");
            sb.Append("  },\n");
            sb.Append("  \"targets\": [\n");
            var targets = db?.targets ?? new List<Target>();
            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                sb.Append("    {");
                AppendInlineJsonProperty(sb, "id", target?.id ?? "", true);
                AppendInlineJsonProperty(sb, "name", target?.name ?? "", true);
                AppendInlineJsonProperty(sb, "serverLabel", target?.serverLabel ?? "", true);
                AppendInlineJsonProperty(sb, "channelLabel", target?.channelLabel ?? "", true);
                AppendInlineJsonProperty(sb, "webhook", SecretRedactor.Redact(target?.webhookUrl ?? ""), false);
                sb.Append("}");
                if (i < targets.Count - 1) sb.Append(",");
                sb.Append("\n");
            }
            sb.Append("  ]\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        private static void AppendTargetSummary(StringBuilder sb, AppDatabase db)
        {
            var targets = db?.targets ?? new List<Target>();
            if (targets.Count == 0)
            {
                sb.AppendLine("- No targets configured.");
                return;
            }

            foreach (var target in targets)
            {
                if (target == null) continue;
                sb.Append("- ");
                sb.Append(Clean(target.name));
                sb.Append(" (");
                sb.Append(Clean(target.serverLabel));
                sb.Append(" / ");
                sb.Append(Clean(target.channelLabel));
                sb.Append(") webhook=");
                sb.AppendLine(Clean(target.webhookUrl));
            }
        }

        private static string BuildLogs(IList<string> logLines, int maxLogLines)
        {
            var sb = new StringBuilder();
            if (logLines == null || logLines.Count == 0)
                return "";

            var start = Math.Max(0, logLines.Count - maxLogLines);
            for (int i = start; i < logLines.Count; i++)
                sb.AppendLine(Clean(logLines[i]));

            return sb.ToString();
        }

        private static void AppendJsonProperty(StringBuilder sb, string name, string value, bool comma, int indent = 2)
        {
            sb.Append(new string(' ', indent));
            sb.Append('"').Append(JsonUtil.Escape(name)).Append("\": \"");
            sb.Append(JsonUtil.Escape(Clean(value))).Append('"');
            if (comma) sb.Append(",");
            sb.Append("\n");
        }

        private static void AppendInlineJsonProperty(StringBuilder sb, string name, string value, bool comma)
        {
            sb.Append('"').Append(JsonUtil.Escape(name)).Append("\": \"");
            sb.Append(JsonUtil.Escape(Clean(value))).Append('"');
            if (comma) sb.Append(", ");
        }

        private static string Clean(string value)
        {
            return SecretRedactor.RedactAndTruncate(value ?? "", 1000);
        }

        private static string CombinePath(string folder, string file)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return file;

            var separator = folder.Contains("\\") ? "\\" : "/";
            return folder.TrimEnd('\\', '/') + separator + file;
        }

        private static string RedactedPath(string folder, string file)
        {
            return SecretRedactor.Redact(CombinePath(folder, file));
        }
    }
}
