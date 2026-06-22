using System;
using System.Reflection;

namespace DiscordScheduler
{
    public sealed class SecretStorageHealthService
    {
        public SecretStorageHealthReport BuildReport(
            AppDatabase db,
            string providerName,
            SecretStoreStatus providerStatus,
            int missingCredentialCount = 0,
            int cannotDecryptCount = 0)
        {
            var report = new SecretStorageHealthReport
            {
                provider = string.IsNullOrWhiteSpace(providerName) ? "not-configured" : providerName,
                providerStatus = providerStatus,
                protectedStoreAvailable = providerStatus == SecretStoreStatus.Available,
                missingCredentialCount = Math.Max(0, missingCredentialCount),
                cannotDecryptCount = Math.Max(0, cannotDecryptCount),
                exportPolicy = "Support bundle and backup export must never include raw webhook secrets."
            };

            var targets = db?.targets;
            report.targetCount = targets?.Count ?? 0;

            if (targets != null)
            {
                foreach (var target in targets)
                {
                    if (target == null) continue;

                    if (!string.IsNullOrWhiteSpace(target.webhookUrl))
                        report.legacyPlaintextCount++;

                    if (HasStringMember(target, "webhookSecretRef"))
                        report.protectedSecretRefCount++;
                }
            }

            report.requiresUserSecretReentry =
                report.missingCredentialCount > 0 ||
                report.cannotDecryptCount > 0 ||
                providerStatus == SecretStoreStatus.MissingCredential ||
                providerStatus == SecretStoreStatus.Corrupt;

            report.migrationAllowed =
                report.protectedStoreAvailable &&
                report.legacyPlaintextCount > 0 &&
                report.missingCredentialCount == 0 &&
                report.cannotDecryptCount == 0;

            BuildWarningsAndSummary(report);
            return report;
        }

        private static bool HasStringMember(Target target, string memberName)
        {
            var type = target.GetType();
            var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                var value = field.GetValue(target) as string;
                return !string.IsNullOrWhiteSpace(value);
            }

            var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.PropertyType == typeof(string))
            {
                var value = property.GetValue(target, null) as string;
                return !string.IsNullOrWhiteSpace(value);
            }

            return false;
        }

        private static void BuildWarningsAndSummary(SecretStorageHealthReport report)
        {
            if (!report.protectedStoreAvailable)
                report.warnings.Add("Protected secret store is not available; legacy plaintext compatibility remains active.");

            if (report.legacyPlaintextCount > 0)
                report.warnings.Add("Webhook URLs are still stored in the legacy plaintext target field.");

            if (report.requiresUserSecretReentry)
                report.warnings.Add("One or more webhook credentials require user re-entry before sending can be trusted.");

            if (report.cannotDecryptCount > 0)
                report.warnings.Add("Protected credential read failed; do not clear legacy plaintext without verified backup and readback.");

            report.summary =
                $"Provider: {report.provider} ({report.providerStatus}) | Targets: {report.targetCount} | Legacy plaintext: {report.legacyPlaintextCount} | Secret refs: {report.protectedSecretRefCount}";

            if (report.requiresUserSecretReentry)
            {
                report.nextAction = "Ask the user to re-enter missing webhook credentials and keep exports redacted.";
            }
            else if (report.migrationAllowed)
            {
                report.nextAction = "Run a migration dry-run before any verify-before-clear apply step.";
            }
            else if (report.legacyPlaintextCount > 0)
            {
                report.nextAction = "Keep plaintext compatibility and redaction gates active until the ADR-backed provider is ready.";
            }
            else
            {
                report.nextAction = "No secret migration action is required.";
            }
        }
    }
}
