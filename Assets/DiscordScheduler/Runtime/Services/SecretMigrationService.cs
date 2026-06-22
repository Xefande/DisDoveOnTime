using System;

namespace DiscordScheduler
{
    public sealed class SecretMigrationService
    {
        private readonly ISecretStore _secretStore;
        private readonly string _providerName;

        public SecretMigrationService(ISecretStore secretStore, string providerName)
        {
            _secretStore = secretStore;
            _providerName = string.IsNullOrWhiteSpace(providerName) ? "not-configured" : providerName;
        }

        public SecretMigrationReport DryRun(AppDatabase db, bool clearLegacyPlaintext = false)
        {
            var report = BuildBaseReport(db, clearLegacyPlaintext);
            if (!report.ok)
                return report;

            report.canApply = report.providerStatus == SecretStoreStatus.Available &&
                              report.legacyPlaintextCount > 0 &&
                              !clearLegacyPlaintext;

            if (clearLegacyPlaintext)
            {
                report.legacyClearBlocked = true;
                report.canApply = false;
                report.warnings.Add("Legacy webhook URL clear is blocked until the send, preview, target-edit and restore paths all resolve webhookSecretRef.");
            }

            if (report.providerStatus != SecretStoreStatus.Available)
                report.warnings.Add("Protected secret store is unavailable; migration apply is blocked.");

            BuildSummary(report);
            return report;
        }

        public SecretMigrationReport Apply(AppDatabase db, bool clearLegacyPlaintext, bool confirmed)
        {
            var report = DryRun(db, clearLegacyPlaintext);
            if (!report.ok)
                return report;

            if (!confirmed)
                return Fail(report, "Secret migration requires explicit confirmation.");

            if (!report.canApply)
                return Fail(report, report.legacyClearBlocked
                    ? "Legacy webhook URL clear is blocked until webhookSecretRef cutover is complete."
                    : "Secret migration cannot apply in the current provider/database state.");

            for (int i = 0; i < db.targets.Count; i++)
            {
                var target = db.targets[i];
                if (target == null || string.IsNullOrWhiteSpace(target.webhookUrl))
                    continue;

                var key = BuildTargetSecretKey(target);
                var save = _secretStore.Save(key, target.webhookUrl);
                if (!save.ok)
                {
                    report.failedStoreCount++;
                    report.warnings.Add("Failed to store secret for target " + SafeTargetId(target) + ": " + save.error);
                    continue;
                }

                var readback = _secretStore.Read(key);
                if (!readback.ok || !string.Equals(readback.secret, target.webhookUrl, StringComparison.Ordinal))
                {
                    report.failedStoreCount++;
                    report.warnings.Add("Secret readback failed for target " + SafeTargetId(target) + ".");
                    continue;
                }

                target.webhookSecretRef = key;
                report.storedRefCount++;
                report.verifiedReadbackCount++;
            }

            report.applied = report.failedStoreCount == 0;
            report.ok = report.failedStoreCount == 0;
            report.canApply = false;
            if (!report.ok)
                report.error = "Secret migration finished with failed store/readback entries.";

            BuildSummary(report);
            return report;
        }

        private SecretMigrationReport BuildBaseReport(AppDatabase db, bool clearLegacyPlaintext)
        {
            var report = new SecretMigrationReport
            {
                provider = _providerName,
                providerStatus = _secretStore?.Status ?? SecretStoreStatus.Unavailable,
                clearLegacyPlaintextRequested = clearLegacyPlaintext
            };

            if (db == null || db.targets == null)
                return Fail(report, "Secret migration requires a valid database.");

            report.targetCount = db.targets.Count;
            for (int i = 0; i < db.targets.Count; i++)
            {
                var target = db.targets[i];
                if (target == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(target.webhookUrl))
                {
                    report.legacyPlaintextCount++;
                    if (string.IsNullOrWhiteSpace(target.webhookSecretRef))
                        report.wouldCreateRefCount++;
                }

                if (!string.IsNullOrWhiteSpace(target.webhookSecretRef))
                    report.protectedSecretRefCount++;
            }

            report.wouldClearPlaintextCount = clearLegacyPlaintext ? report.legacyPlaintextCount : 0;
            return report;
        }

        private static string BuildTargetSecretKey(Target target)
        {
            var id = string.IsNullOrWhiteSpace(target.id) ? Guid.NewGuid().ToString("N") : target.id.Trim();
            return "target:" + id + ":webhook";
        }

        private static string SafeTargetId(Target target)
        {
            return string.IsNullOrWhiteSpace(target?.id) ? "<missing-id>" : target.id.Trim();
        }

        private static SecretMigrationReport Fail(SecretMigrationReport report, string error)
        {
            report.ok = false;
            report.canApply = false;
            report.error = SecretRedactor.RedactAndTruncate(error ?? "Secret migration failed.");
            BuildSummary(report);
            return report;
        }

        private static void BuildSummary(SecretMigrationReport report)
        {
            report.summary =
                $"Provider: {report.provider} ({report.providerStatus}) | Targets: {report.targetCount} | Legacy plaintext: {report.legacyPlaintextCount} | " +
                $"Existing refs: {report.protectedSecretRefCount} | Would create refs: {report.wouldCreateRefCount} | Stored refs: {report.storedRefCount} | Failed: {report.failedStoreCount}";
        }
    }
}
