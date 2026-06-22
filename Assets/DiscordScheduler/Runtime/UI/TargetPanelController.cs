using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace DiscordScheduler
{
    public sealed class TargetPanelController
    {
        private readonly TargetApplicationService _targetApp;
        private readonly WebhookUrlValidator _webhookUrlValidator;
        private readonly IWebhookSecretResolver _webhookSecretResolver;
        private readonly DiscordWebhookClient _webhook;
        private readonly LogService _log;
        private readonly Func<AppDatabase> _getDatabase;
        private readonly Func<ValidationResult> _saveDatabase;
        private readonly Action _refreshTargetsDropdowns;
        private readonly Action _refreshPostsList;
        private readonly Func<IEnumerator, Coroutine> _startCoroutine;

        private ListView _targetsList;
        private TextField _tfTargetName;
        private TextField _tfTargetServer;
        private TextField _tfTargetChannel;
        private TextField _tfTargetWebhook;
        private TextField _tfTargetUsername;
        private TextField _tfTargetAvatar;
        private Label _lblTargetsEmpty;
        private Label _lblTargetHint;
        private Label _lblTargetHealth;
        private Label _lblTargetImpact;
        private string _pendingDeleteConfirmTargetId = "";

        public TargetPanelController(
            TargetApplicationService targetApp,
            WebhookUrlValidator webhookUrlValidator,
            IWebhookSecretResolver webhookSecretResolver,
            DiscordWebhookClient webhook,
            LogService log,
            Func<AppDatabase> getDatabase,
            Func<ValidationResult> saveDatabase,
            Action refreshTargetsDropdowns,
            Action refreshPostsList,
            Func<IEnumerator, Coroutine> startCoroutine)
        {
            _targetApp = targetApp ?? throw new ArgumentNullException(nameof(targetApp));
            _webhookUrlValidator = webhookUrlValidator ?? throw new ArgumentNullException(nameof(webhookUrlValidator));
            _webhookSecretResolver = webhookSecretResolver ?? new WebhookSecretResolver();
            _webhook = webhook ?? throw new ArgumentNullException(nameof(webhook));
            _log = log;
            _getDatabase = getDatabase ?? throw new ArgumentNullException(nameof(getDatabase));
            _saveDatabase = saveDatabase ?? throw new ArgumentNullException(nameof(saveDatabase));
            _refreshTargetsDropdowns = refreshTargetsDropdowns ?? throw new ArgumentNullException(nameof(refreshTargetsDropdowns));
            _refreshPostsList = refreshPostsList ?? throw new ArgumentNullException(nameof(refreshPostsList));
            _startCoroutine = startCoroutine ?? throw new ArgumentNullException(nameof(startCoroutine));
        }

        public void Bind(VisualElement root)
        {
            if (root == null)
                return;

            _targetsList = root.Q<ListView>("targetsList");
            _tfTargetName = root.Q<TextField>("tfTargetName");
            _tfTargetServer = root.Q<TextField>("tfTargetServer");
            _tfTargetChannel = root.Q<TextField>("tfTargetChannel");
            _tfTargetWebhook = root.Q<TextField>("tfTargetWebhook");
            _tfTargetUsername = root.Q<TextField>("tfTargetUsername");
            _tfTargetAvatar = root.Q<TextField>("tfTargetAvatar");
            _lblTargetsEmpty = root.Q<Label>("lblTargetsEmpty");
            _lblTargetHint = root.Q<Label>("lblTargetHint");
            _lblTargetHealth = root.Q<Label>("lblTargetHealth");
            _lblTargetImpact = root.Q<Label>("lblTargetImpact");
            ConfigureSecretField(_tfTargetWebhook);

            BindList();
            BindFieldChangePreviews();

            var btnAdd = root.Q<Button>("btnTargetAdd");
            if (btnAdd != null)
                btnAdd.clicked += AddTarget;

            var btnSave = root.Q<Button>("btnTargetSave");
            if (btnSave != null)
                btnSave.clicked += SaveTargetFromFields;

            var btnDelete = root.Q<Button>("btnTargetDelete");
            if (btnDelete != null)
                btnDelete.clicked += DeleteSelectedTarget;

            var btnTest = root.Q<Button>("btnTargetTest");
            if (btnTest != null)
                btnTest.clicked += TestSelectedTarget;
        }

        private static void ConfigureSecretField(TextField field)
        {
            if (field == null)
                return;

            field.isPasswordField = true;
            field.maskChar = '*';
        }

        public void RefreshList()
        {
            if (_targetsList == null)
                return;

            var selectedId = GetSelectedTarget()?.id ?? "";
            var targets = _getDatabase()?.targets;
            var hasTargets = targets != null && targets.Count > 0;

            _targetsList.itemsSource = targets;
            _targetsList.Rebuild();
            _targetsList.style.display = hasTargets ? DisplayStyle.Flex : DisplayStyle.None;

            if (_lblTargetsEmpty != null)
                _lblTargetsEmpty.style.display = hasTargets ? DisplayStyle.None : DisplayStyle.Flex;

            if (hasTargets)
            {
                var index = !string.IsNullOrWhiteSpace(selectedId)
                    ? targets.FindIndex(target => target != null && target.id == selectedId)
                    : -1;
                _targetsList.selectedIndex = index >= 0 ? index : 0;
            }
            else
            {
                _targetsList.selectedIndex = -1;
            }
        }

        private void BindList()
        {
            if (_targetsList == null)
                return;

            _targetsList.makeItem = () => new Label();
            _targetsList.bindItem = (element, index) =>
            {
                var label = element as Label;
                var targets = _getDatabase()?.targets;
                if (label == null || targets == null || index < 0 || index >= targets.Count)
                    return;

                var target = targets[index];
                label.text = $"{target.name}  -  {target.serverLabel} / {target.channelLabel}";
            };
            _targetsList.selectionType = SelectionType.Single;
            _targetsList.selectionChanged += _ => LoadSelectedTargetIntoFields();
        }

        private void AddTarget()
        {
            var addResult = _targetApp.CreateTarget(_getDatabase(), out var target);
            if (!addResult.ok)
            {
                SetHint(addResult.error);
                return;
            }

            _refreshTargetsDropdowns();
            RefreshList();
            if (_targetsList != null)
                _targetsList.selectedIndex = _getDatabase()?.targets?.IndexOf(target) ?? -1;
            LoadSelectedTargetIntoFields();
            SetHint("Fill target details and save.");
        }

        private void SaveTargetFromFields()
        {
            var target = GetSelectedTarget();
            if (target == null)
            {
                SetHint("Target not selected.");
                return;
            }

            var draft = BuildTargetDraftFromFields(target);
            RenderTargetImpact(target, draft);
            var saveResult = _targetApp.SaveTarget(target, draft, _getDatabase());
            if (!saveResult.ok)
            {
                SetHint(saveResult.error);
                return;
            }

            var persist = _saveDatabase();
            if (!persist.ok)
            {
                SetHint(persist.error);
                return;
            }

            _refreshTargetsDropdowns();
            RefreshList();
            _pendingDeleteConfirmTargetId = "";
            RenderTargetHealth(target);
            RenderTargetImpact(target, BuildTargetDraftFromFields(target));
            SetHint("Saved.");
        }

        private void DeleteSelectedTarget()
        {
            var target = GetSelectedTarget();
            if (target == null)
                return;

            var previewResult = _targetApp.PreviewDeleteTarget(target, _getDatabase(), out var preview);
            if (!previewResult.ok)
            {
                SetHint(previewResult.error);
                return;
            }

            if (preview.blockedActivePostCount > 0)
            {
                SetHint($"Cannot delete while {preview.blockedActivePostCount} linked post(s) are actively sending.");
                return;
            }

            if (preview.deletedPostCount > 0 &&
                !string.Equals(_pendingDeleteConfirmTargetId, target.id ?? "", StringComparison.Ordinal))
            {
                _pendingDeleteConfirmTargetId = target.id ?? "";
                SetHint($"Delete impact: {preview.deletedPostCount} linked post(s) will be deleted, {preview.cancelledQueuedPostCount} queued send(s) cancelled. Click Delete again to confirm.");
                RenderDeleteImpact(preview);
                return;
            }

            var deleteResult = _targetApp.DeleteTarget(target, _getDatabase(), out var deleted);
            if (!deleteResult.ok)
            {
                SetHint(deleteResult.error);
                return;
            }

            if (deleted.cancelledQueuedPostCount > 0)
                _log?.Warn($"Cancelled queued linked post(s) while deleting target: {deleted.cancelledQueuedPostCount}");

            var persist = _saveDatabase();
            if (!persist.ok)
            {
                SetHint(persist.error);
                return;
            }

            _refreshTargetsDropdowns();
            RefreshList();
            _refreshPostsList();
            _pendingDeleteConfirmTargetId = "";
            SetHint(deleted.deletedPostCount > 0
                ? $"Deleted target and {deleted.deletedPostCount} linked post(s)."
                : "Deleted.");
            SetImpact("Target impact: target deleted.");
        }

        private void LoadSelectedTargetIntoFields()
        {
            var target = GetSelectedTarget();
            if (target == null)
            {
                SetTargetFields("", "", "", "", "", "");
                SetHealth("Webhook health: select or enter a target.");
                SetImpact("Target impact: no target selected.");
                return;
            }

            SetTargetFields(
                target.name ?? "",
                target.serverLabel ?? "",
                target.channelLabel ?? "",
                target.webhookUrl ?? "",
                target.overrideUsername ?? "",
                target.overrideAvatarUrl ?? "");
            _pendingDeleteConfirmTargetId = "";
            RenderTargetHealth(target);
            RenderTargetImpact(target, BuildTargetDraftFromFields(target));
        }

        private void TestSelectedTarget()
        {
            var target = GetSelectedTarget();
            if (target == null)
            {
                SetHint("No target selected.");
                return;
            }

            var secretResolution = _webhookSecretResolver.Resolve(target);
            if (!secretResolution.ok)
            {
                SetHint(secretResolution.error);
                return;
            }

            var webhookValidation = _webhookUrlValidator.Validate(secretResolution.webhookUrl);
            if (!webhookValidation.ok)
            {
                SetHint(webhookValidation.error);
                return;
            }

            var temp = ScheduledPost.CreateNew(target.id);
            temp.title = "Test message";
            temp.body = "Test message from DisDoveOnTime.";
            temp.SetScheduledAtUtc(DateTime.UtcNow);
            var resolvedWebhookUrl = secretResolution.webhookUrl;

            _startCoroutine(_webhook.Send(target, temp, (ok, error) =>
            {
                SetHint(ok ? "Test sent." : $"Error: {SafeWebhookError(error, resolvedWebhookUrl)}");
            }));
        }

        private Target GetSelectedTarget()
        {
            var db = _getDatabase();
            var index = _targetsList?.selectedIndex ?? -1;
            if (db?.targets == null || index < 0 || index >= db.targets.Count)
                return null;

            return db.targets[index];
        }

        private TargetDraft BuildTargetDraftFromFields(Target target)
        {
            return new TargetDraft
            {
                id = target?.id ?? "",
                name = (_tfTargetName?.value ?? "").Trim(),
                serverLabel = (_tfTargetServer?.value ?? "").Trim(),
                channelLabel = (_tfTargetChannel?.value ?? "").Trim(),
                webhookUrl = (_tfTargetWebhook?.value ?? "").Trim(),
                overrideUsername = (_tfTargetUsername?.value ?? "").Trim(),
                overrideAvatarUrl = (_tfTargetAvatar?.value ?? "").Trim()
            };
        }

        private void SetTargetFields(
            string name,
            string server,
            string channel,
            string webhook,
            string username,
            string avatar)
        {
            if (_tfTargetName != null) _tfTargetName.value = name;
            if (_tfTargetServer != null) _tfTargetServer.value = server;
            if (_tfTargetChannel != null) _tfTargetChannel.value = channel;
            if (_tfTargetWebhook != null) _tfTargetWebhook.value = webhook;
            if (_tfTargetUsername != null) _tfTargetUsername.value = username;
            if (_tfTargetAvatar != null) _tfTargetAvatar.value = avatar;
        }

        private void BindFieldChangePreviews()
        {
            RegisterPreview(_tfTargetName);
            RegisterPreview(_tfTargetServer);
            RegisterPreview(_tfTargetChannel);
            RegisterPreview(_tfTargetWebhook);
            RegisterPreview(_tfTargetUsername);
            RegisterPreview(_tfTargetAvatar);
        }

        private void RegisterPreview(TextField field)
        {
            if (field == null)
                return;

            field.RegisterValueChangedCallback(_ =>
            {
                _pendingDeleteConfirmTargetId = "";
                var target = GetSelectedTarget();
                if (target == null)
                    return;

                RenderDraftWebhookHealth();
                RenderTargetImpact(target, BuildTargetDraftFromFields(target));
            });
        }

        private void RenderDraftWebhookHealth()
        {
            var url = (_tfTargetWebhook?.value ?? "").Trim();
            var validation = _webhookUrlValidator.Validate(url);
            SetHealth(validation.ok
                ? "Webhook health: URL format is valid. Send test to verify Discord accepts it."
                : "Webhook health: " + validation.error);
        }

        private void RenderTargetHealth(Target target)
        {
            if (target == null)
            {
                SetHealth("Webhook health: no target selected.");
                return;
            }

            var report = new TargetHealthService(_webhookUrlValidator, _webhookSecretResolver).Build(_getDatabase(), target.id);
            var message = report.webhookOk
                ? $"Webhook health: valid. Linked={report.linkedPostCount}, pending={report.pendingPostCount}, queued={report.queuedPostCount}, active={report.activePostCount}, sent={report.sentPostCount}, review={report.needsReviewPostCount}."
                : "Webhook health: " + report.blocker;
            SetHealth(message);
        }

        private void RenderTargetImpact(Target target, TargetDraft draft)
        {
            var report = _targetApp.PreviewSaveImpact(target, draft, _getDatabase());
            if (report == null || !report.webhookChanged)
            {
                SetImpact("Target impact: webhook unchanged.");
                return;
            }

            SetImpact("Target impact: webhook change affects " +
                      $"linked={report.linkedPostCount}, pending={report.pendingPostCount}, queued={report.queuedPostCount}, " +
                      $"active={report.activePostCount}, needsReview={report.needsReviewPostCount}. " +
                      "Save is blocked until impact is resolved.");
        }

        private void RenderDeleteImpact(TargetDeleteResult preview)
        {
            if (preview == null)
            {
                SetImpact("Target impact: unavailable.");
                return;
            }

            SetImpact($"Target delete impact: deletes linked={preview.deletedPostCount}, cancels queued={preview.cancelledQueuedPostCount}, active blockers={preview.blockedActivePostCount}.");
        }

        private string SafeWebhookError(string error, string webhookUrl)
        {
            var safe = SecretRedactor.Redact(string.IsNullOrWhiteSpace(error) ? "Webhook request failed." : error);
            if (!string.IsNullOrWhiteSpace(webhookUrl))
                safe = safe.Replace(webhookUrl.Trim(), _webhookUrlValidator.Mask(webhookUrl));

            const int maxLength = 240;
            if (safe.Length > maxLength)
                safe = safe.Substring(0, maxLength) + "...";

            return safe;
        }

        private void SetHint(string message)
        {
            if (_lblTargetHint != null)
                _lblTargetHint.text = message ?? "";
        }

        private void SetHealth(string message)
        {
            if (_lblTargetHealth != null)
                _lblTargetHealth.text = SecretRedactor.Redact(message ?? "");
        }

        private void SetImpact(string message)
        {
            if (_lblTargetImpact != null)
                _lblTargetImpact.text = SecretRedactor.Redact(message ?? "");
        }
    }
}
