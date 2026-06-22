using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine.UIElements;

namespace DiscordScheduler
{
    public sealed class SettingsPanelController
    {
        private readonly SettingsApplicationService _settingsApp;
        private readonly Func<AppDatabase> _getDatabase;
        private readonly Func<ValidationResult> _saveDatabase;
        private readonly Func<HealthSnapshot> _getHealthSnapshot;
        private readonly Func<List<string>> _getLogSnapshot;
        private readonly List<string> _policyLabels;
        private readonly SupportBundleService _supportBundleService;
        private readonly SecretStorageHealthService _secretStorageHealthService;
        private readonly RetentionPolicyService _retentionPolicyService;
        private readonly BackupRestoreService _backupRestoreService;

        private IntegerField _ifSleepThreshold;
        private Toggle _tgDefaultAllowUsers;
        private Toggle _tgDefaultAllowRoles;
        private Toggle _tgDefaultAllowEveryone;
        private TextField _tfDefaultUserIds;
        private TextField _tfDefaultRoleIds;
        private DropdownField _ddDefaultOffPolicy;
        private DropdownField _ddDefaultSleepPolicy;
        private Label _lblPaths;
        private Label _lblSettingsResult;
        private Label _lblSupportBundleSummary;
        private Label _lblSecretStorageSummary;
        private Label _lblSecretMigrationSummary;
        private Label _lblSupportBundleResult;
        private Label _lblRetentionSummary;
        private Label _lblRestoreSummary;
        private Label _lblRetentionResult;
        private Label _lblRestoreResult;
        private TextField _tfRestoreFilePath;
        private SupportBundleDryRun _lastSupportBundleDryRun;
        private RetentionDryRunReport _lastRetentionDryRun;
        private RestoreDryRunReport _lastRestoreDryRun;

        public SettingsPanelController(
            SettingsApplicationService settingsApp,
            Func<AppDatabase> getDatabase,
            Func<ValidationResult> saveDatabase,
            IEnumerable<string> policyLabels,
            Func<HealthSnapshot> getHealthSnapshot = null,
            Func<List<string>> getLogSnapshot = null,
            SupportBundleService supportBundleService = null,
            SecretStorageHealthService secretStorageHealthService = null,
            RetentionPolicyService retentionPolicyService = null,
            BackupRestoreService backupRestoreService = null)
        {
            _settingsApp = settingsApp ?? throw new ArgumentNullException(nameof(settingsApp));
            _getDatabase = getDatabase ?? throw new ArgumentNullException(nameof(getDatabase));
            _saveDatabase = saveDatabase ?? throw new ArgumentNullException(nameof(saveDatabase));
            _policyLabels = new List<string>(policyLabels ?? Array.Empty<string>());
            _getHealthSnapshot = getHealthSnapshot;
            _getLogSnapshot = getLogSnapshot;
            _supportBundleService = supportBundleService ?? new SupportBundleService();
            _secretStorageHealthService = secretStorageHealthService ?? new SecretStorageHealthService();
            _retentionPolicyService = retentionPolicyService ?? new RetentionPolicyService(new AttachmentCleanupService(new AttachmentRepository(FileUtil.AttachmentsFolder)));
            _backupRestoreService = backupRestoreService ?? new BackupRestoreService();
        }

        public void Bind(VisualElement root)
        {
            if (root == null)
                return;

            _ifSleepThreshold = root.Q<IntegerField>("ifSleepThreshold");
            _tgDefaultAllowUsers = root.Q<Toggle>("tgDefaultAllowUsers");
            _tgDefaultAllowRoles = root.Q<Toggle>("tgDefaultAllowRoles");
            _tgDefaultAllowEveryone = root.Q<Toggle>("tgDefaultAllowEveryone");
            _tfDefaultUserIds = root.Q<TextField>("tfDefaultUserIds");
            _tfDefaultRoleIds = root.Q<TextField>("tfDefaultRoleIds");
            _ddDefaultOffPolicy = root.Q<DropdownField>("ddDefaultOffPolicy");
            _ddDefaultSleepPolicy = root.Q<DropdownField>("ddDefaultSleepPolicy");
            _lblPaths = root.Q<Label>("lblPaths");
            _lblSettingsResult = root.Q<Label>("lblSettingsResult");
            _lblSupportBundleSummary = root.Q<Label>("lblSupportBundleSummary");
            _lblSecretStorageSummary = root.Q<Label>("lblSecretStorageSummary");
            _lblSecretMigrationSummary = root.Q<Label>("lblSecretMigrationSummary");
            _lblSupportBundleResult = root.Q<Label>("lblSupportBundleResult");
            _lblRetentionSummary = root.Q<Label>("lblRetentionSummary");
            _lblRestoreSummary = root.Q<Label>("lblRestoreSummary");
            _lblRetentionResult = root.Q<Label>("lblRetentionResult");
            _lblRestoreResult = root.Q<Label>("lblRestoreResult");
            _tfRestoreFilePath = root.Q<TextField>("tfRestoreFilePath");

            if (_ddDefaultOffPolicy != null)
                _ddDefaultOffPolicy.choices = new List<string>(_policyLabels);

            if (_ddDefaultSleepPolicy != null)
                _ddDefaultSleepPolicy.choices = new List<string>(_policyLabels);

            var btnSave = root.Q<Button>("btnSaveSettings");
            if (btnSave != null)
                btnSave.clicked += Save;

            var btnSupportBundleDryRun = root.Q<Button>("btnSupportBundleDryRun");
            if (btnSupportBundleDryRun != null)
                btnSupportBundleDryRun.clicked += RefreshSupportBundleDryRun;

            var btnSupportBundleExport = root.Q<Button>("btnSupportBundleExport");
            if (btnSupportBundleExport != null)
                btnSupportBundleExport.clicked += ExportSupportBundle;

            var btnSecretMigrationDryRun = root.Q<Button>("btnSecretMigrationDryRun");
            if (btnSecretMigrationDryRun != null)
                btnSecretMigrationDryRun.clicked += DryRunSecretMigration;

            var btnSecretMigrationApplyRefs = root.Q<Button>("btnSecretMigrationApplyRefs");
            if (btnSecretMigrationApplyRefs != null)
                btnSecretMigrationApplyRefs.clicked += ApplySecretMigrationRefs;

            var btnRetentionDryRun = root.Q<Button>("btnRetentionDryRun");
            if (btnRetentionDryRun != null)
                btnRetentionDryRun.clicked += RefreshRetentionDryRun;

            var btnRetentionApply = root.Q<Button>("btnRetentionApply");
            if (btnRetentionApply != null)
                btnRetentionApply.clicked += ApplyRetentionCleanup;

            var btnRestoreCheckCurrent = root.Q<Button>("btnRestoreCheckCurrent");
            if (btnRestoreCheckCurrent != null)
                btnRestoreCheckCurrent.clicked += CheckCurrentDatabaseRestoreSafety;

            var btnRestoreBrowse = root.Q<Button>("btnRestoreBrowse");
            if (btnRestoreBrowse != null)
                btnRestoreBrowse.clicked += BrowseRestoreFile;

            var btnRestoreDryRun = root.Q<Button>("btnRestoreDryRun");
            if (btnRestoreDryRun != null)
                btnRestoreDryRun.clicked += DryRunRestoreFile;

            var btnRestoreApply = root.Q<Button>("btnRestoreApply");
            if (btnRestoreApply != null)
                btnRestoreApply.clicked += ApplyRestoreFile;
        }

        public void Refresh()
        {
            var db = _getDatabase();
            var normalize = _settingsApp.Normalize(db);
            if (!normalize.ok)
            {
                SetResult(normalize.error);
                return;
            }

            var draft = _settingsApp.ToDraft(db.settings);

            if (_ifSleepThreshold != null)
                _ifSleepThreshold.value = draft.sleepThresholdMinutes;

            if (_tgDefaultAllowUsers != null)
                _tgDefaultAllowUsers.value = draft.defaultAllowedMentions.allowUsers;

            if (_tgDefaultAllowRoles != null)
                _tgDefaultAllowRoles.value = draft.defaultAllowedMentions.allowRoles;

            if (_tgDefaultAllowEveryone != null)
                _tgDefaultAllowEveryone.value = draft.defaultAllowedMentions.allowEveryone;

            if (_tfDefaultUserIds != null)
                _tfDefaultUserIds.value = draft.defaultAllowedMentions.userIdsCsv ?? "";

            if (_tfDefaultRoleIds != null)
                _tfDefaultRoleIds.value = draft.defaultAllowedMentions.roleIdsCsv ?? "";

            if (_ddDefaultOffPolicy != null)
                _ddDefaultOffPolicy.index = draft.defaultOffPolicyIndex;

            if (_ddDefaultSleepPolicy != null)
                _ddDefaultSleepPolicy.index = draft.defaultSleepPolicyIndex;

            if (_lblPaths != null)
                _lblPaths.text = $"Data: {FileUtil.DataFolder}\nAttachments: {FileUtil.AttachmentsFolder}";

            RefreshSecretStorageSummary();
            if (_lastSupportBundleDryRun == null)
                SetSupportBundleSummary("Support bundle dry-run has not been generated yet.");
            else
                RenderSupportBundleDryRun(_lastSupportBundleDryRun);

            if (_lastRetentionDryRun == null)
                SetRetentionSummary("Retention dry-run has not been generated yet.");
            else
                RenderRetentionDryRun(_lastRetentionDryRun);

            if (_lastRestoreDryRun == null)
                SetRestoreSummary("Restore dry-run has not been generated yet.");
            else
                RenderRestoreDryRun(_lastRestoreDryRun);
        }

        private void Save()
        {
            SetResult("");

            var result = _settingsApp.Apply(_getDatabase(), BuildDraft());
            if (!result.ok)
            {
                SetResult(result.error);
                return;
            }

            var save = _saveDatabase();
            if (!save.ok)
            {
                SetResult(save.error);
                return;
            }

            SetResult("Saved.");
        }

        private SettingsDraft BuildDraft()
        {
            return new SettingsDraft
            {
                sleepThresholdMinutes = _ifSleepThreshold?.value ?? SettingsApplicationService.MinSleepThresholdMinutes,
                defaultAllowedMentions = new AllowedMentions
                {
                    allowUsers = _tgDefaultAllowUsers != null && _tgDefaultAllowUsers.value,
                    allowRoles = _tgDefaultAllowRoles != null && _tgDefaultAllowRoles.value,
                    allowEveryone = _tgDefaultAllowEveryone != null && _tgDefaultAllowEveryone.value,
                    userIdsCsv = _tfDefaultUserIds?.value ?? "",
                    roleIdsCsv = _tfDefaultRoleIds?.value ?? ""
                },
                defaultOffPolicyIndex = _ddDefaultOffPolicy?.index ?? 0,
                defaultSleepPolicyIndex = _ddDefaultSleepPolicy?.index ?? 0
            };
        }

        private void SetResult(string message)
        {
            if (_lblSettingsResult != null)
                _lblSettingsResult.text = message ?? "";
        }

        private void RefreshSupportBundleDryRun()
        {
            _lastSupportBundleDryRun = BuildSupportBundleDryRun();
            RenderSupportBundleDryRun(_lastSupportBundleDryRun);
            SetSupportBundleResult(_lastSupportBundleDryRun.ok ? "Dry-run ready. Review the manifest summary before export." : _lastSupportBundleDryRun.error);
        }

        private SupportBundleDryRun BuildSupportBundleDryRun()
        {
            return _supportBundleService.DryRun(
                _getDatabase(),
                _getHealthSnapshot != null ? _getHealthSnapshot() : null,
                _getLogSnapshot != null ? _getLogSnapshot() : new List<string>());
        }

        private void ExportSupportBundle()
        {
            if (_lastSupportBundleDryRun == null)
                _lastSupportBundleDryRun = BuildSupportBundleDryRun();

            RenderSupportBundleDryRun(_lastSupportBundleDryRun);
            if (!_lastSupportBundleDryRun.ok)
            {
                SetSupportBundleResult(_lastSupportBundleDryRun.error);
                return;
            }

            var folder = Path.Combine(FileUtil.DataFolder, "support-bundle-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            var result = _supportBundleService.Export(
                _lastSupportBundleDryRun,
                _getDatabase(),
                _getHealthSnapshot != null ? _getHealthSnapshot() : null,
                _getLogSnapshot != null ? _getLogSnapshot() : new List<string>(),
                folder,
                confirmed: true);

            SetSupportBundleResult(result.ok
                ? "Exported redacted support bundle: " + result.outputFolder
                : "Support bundle export failed: " + result.error);
        }

        private void RenderSupportBundleDryRun(SupportBundleDryRun dryRun)
        {
            if (dryRun == null)
            {
                SetSupportBundleSummary("Support bundle dry-run has not been generated yet.");
                return;
            }

            SetSupportBundleSummary(
                dryRun.shareSafetyVerdict +
                $"\nIncluded: {dryRun.includedCount} | Excluded sensitive: {dryRun.sensitiveExcludedCount} | Redacted: {dryRun.redactedCount}" +
                $"\nRaw DB excluded: {dryRun.rawDatabaseExcluded} | Raw media excluded: {dryRun.rawMediaExcluded}");
        }

        private void RefreshSecretStorageSummary()
        {
            var secretStore = BuildSecretStore();
            var report = _secretStorageHealthService.BuildReport(
                _getDatabase(),
                SecretProviderName(),
                secretStore.Status);

            if (_lblSecretStorageSummary != null)
            {
                _lblSecretStorageSummary.text =
                    report.summary +
                    "\nNext: " + report.nextAction +
                    "\nExport policy: " + report.exportPolicy;
            }

            SetSecretMigrationSummary("Secret migration dry-run has not been generated yet. Legacy webhook URL compatibility remains active.");
        }

        private void DryRunSecretMigration()
        {
            var report = BuildSecretMigrationService().DryRun(_getDatabase(), clearLegacyPlaintext: false);
            RenderSecretMigrationReport(report);
        }

        private void ApplySecretMigrationRefs()
        {
            var db = _getDatabase();
            var report = BuildSecretMigrationService().Apply(db, clearLegacyPlaintext: false, confirmed: true);
            if (!report.ok)
            {
                RenderSecretMigrationReport(report);
                return;
            }

            var save = _saveDatabase();
            if (!save.ok)
            {
                SetSecretMigrationSummary(report.summary + "\nSave failed after secret-ref migration: " + SecretRedactor.RedactAndTruncate(save.error));
                return;
            }

            RenderSecretMigrationReport(report);
        }

        private SecretMigrationService BuildSecretMigrationService()
        {
            return new SecretMigrationService(BuildSecretStore(), SecretProviderName());
        }

        private ISecretStore BuildSecretStore()
        {
            return new DpapiSecretStore(Path.Combine(FileUtil.DataFolder, "secrets"));
        }

        private static string SecretProviderName()
        {
            return "dpapi-current-user-file-store";
        }

        private void RenderSecretMigrationReport(SecretMigrationReport report)
        {
            if (report == null)
            {
                SetSecretMigrationSummary("Secret migration dry-run has not been generated yet.");
                return;
            }

            var message = report.summary +
                          $"\nCan apply: {report.canApply} | Applied: {report.applied} | Verified readback: {report.verifiedReadbackCount}" +
                          "\nLegacy clear: blocked until webhookSecretRef cutover is complete.";

            if (!string.IsNullOrWhiteSpace(report.error))
                message += "\nError: " + report.error;

            if (report.warnings != null && report.warnings.Count > 0)
                message += "\nWarning: " + string.Join(" | ", report.warnings);

            SetSecretMigrationSummary(message);
        }

        private void SetSupportBundleSummary(string message)
        {
            if (_lblSupportBundleSummary != null)
                _lblSupportBundleSummary.text = SecretRedactor.Redact(message ?? "");
        }

        private void SetSupportBundleResult(string message)
        {
            if (_lblSupportBundleResult != null)
                _lblSupportBundleResult.text = SecretRedactor.Redact(message ?? "");
        }

        private void SetSecretMigrationSummary(string message)
        {
            if (_lblSecretMigrationSummary != null)
                _lblSecretMigrationSummary.text = SecretRedactor.Redact(message ?? "");
        }

        private void RefreshRetentionDryRun()
        {
            _lastRetentionDryRun = _retentionPolicyService.DryRun(_getDatabase(), FileUtil.DataFolder);
            RenderRetentionDryRun(_lastRetentionDryRun);
            SetRetentionResult(_lastRetentionDryRun.ok
                ? "Retention dry-run ready. Apply only if the delete/protect summary is expected."
                : _lastRetentionDryRun.error);
        }

        private void ApplyRetentionCleanup()
        {
            if (_lastRetentionDryRun == null)
                _lastRetentionDryRun = _retentionPolicyService.DryRun(_getDatabase(), FileUtil.DataFolder);

            RenderRetentionDryRun(_lastRetentionDryRun);
            var result = _retentionPolicyService.Apply(_getDatabase(), FileUtil.DataFolder, _lastRetentionDryRun, confirmed: true);
            SetRetentionResult(result.ok ? result.summary : result.error);

            if (result.ok)
                _lastRetentionDryRun = _retentionPolicyService.DryRun(_getDatabase(), FileUtil.DataFolder);
        }

        private void CheckCurrentDatabaseRestoreSafety()
        {
            var report = _backupRestoreService.DryRunReplace(_getDatabase());
            RenderRestoreDryRun(report, "Current DB restore safety");
        }

        private void BrowseRestoreFile()
        {
            var path = NativeFileDialogWin.OpenFile(NativeFileDialogWin.BuildFilter(("JSON database", "*.json"), ("All files", "*.*")));
            if (!string.IsNullOrWhiteSpace(path) && _tfRestoreFilePath != null)
                _tfRestoreFilePath.value = path;
        }

        private void DryRunRestoreFile()
        {
            _lastRestoreDryRun = _backupRestoreService.DryRunFile(_tfRestoreFilePath?.value ?? "");
            RenderRestoreDryRun(_lastRestoreDryRun);
            SetRestoreResult(_lastRestoreDryRun.ok
                ? "Restore dry-run ready. Review duplicate IDs, broken targets and backup expectations before apply."
                : _lastRestoreDryRun.error);
        }

        private void ApplyRestoreFile()
        {
            var path = _tfRestoreFilePath?.value ?? "";
            if (_lastRestoreDryRun == null || !string.Equals(_lastRestoreDryRun.sourcePath, SecretRedactor.Redact(path), StringComparison.Ordinal))
                _lastRestoreDryRun = _backupRestoreService.DryRunFile(path);

            RenderRestoreDryRun(_lastRestoreDryRun);
            if (!_lastRestoreDryRun.ok || !_lastRestoreDryRun.canApply)
            {
                SetRestoreResult(_lastRestoreDryRun.error);
                return;
            }

            var result = _backupRestoreService.ApplyReplaceFromFile(
                _getDatabase(),
                path,
                _lastRestoreDryRun.dryRunToken,
                _saveDatabase,
                confirmed: true);

            SetRestoreResult(result.ok ? result.summary : result.error + (string.IsNullOrWhiteSpace(result.backupPath) ? "" : "\nBackup: " + result.backupPath));
            if (result.ok)
            {
                _lastRestoreDryRun = null;
                Refresh();
            }
        }

        private void RenderRestoreDryRun(RestoreDryRunReport report, string prefix = "Restore dry-run")
        {
            if (report == null)
            {
                SetRestoreSummary("Restore dry-run has not been generated yet.");
                return;
            }

            var summary = report.ok
                ? $"{prefix}: {report.summary}"
                : $"{prefix} blocker: {SecretRedactor.RedactAndTruncate(report.error)} {report.summary}";

            if (!string.IsNullOrWhiteSpace(report.sourcePath))
                summary += "\nSource: " + report.sourcePath;

            SetRestoreSummary(summary);
        }

        private void RenderRetentionDryRun(RetentionDryRunReport report)
        {
            if (report == null)
            {
                SetRetentionSummary("Retention dry-run has not been generated yet.");
                return;
            }

            var summary =
                $"Can apply: {report.canApply} | Delete eligible attachments: {report.deleteEligibleAttachmentCount} ({report.deleteEligibleAttachmentBytes} bytes)" +
                $"\nProtected active: {report.protectedActiveAttachmentCount} | Protected referenced: {report.protectedReferencedAttachmentCount}" +
                $"\nData folder: {report.dataFolderBytes} bytes | Logs: {report.logBytes} | Backups: {report.backupBytes} | Exports: {report.exportBytes}";

            if (!string.IsNullOrWhiteSpace(report.warning))
                summary += "\nWarning: " + report.warning;

            if (!report.ok)
                summary += "\nError: " + report.error;

            SetRetentionSummary(summary);
        }

        private void SetRetentionSummary(string message)
        {
            if (_lblRetentionSummary != null)
                _lblRetentionSummary.text = SecretRedactor.Redact(message ?? "");
        }

        private void SetRetentionResult(string message)
        {
            if (_lblRetentionResult != null)
                _lblRetentionResult.text = SecretRedactor.Redact(message ?? "");
        }

        private void SetRestoreSummary(string message)
        {
            if (_lblRestoreSummary != null)
                _lblRestoreSummary.text = SecretRedactor.Redact(message ?? "");
        }

        private void SetRestoreResult(string message)
        {
            if (_lblRestoreResult != null)
                _lblRestoreResult.text = SecretRedactor.Redact(message ?? "");
        }
    }
}
