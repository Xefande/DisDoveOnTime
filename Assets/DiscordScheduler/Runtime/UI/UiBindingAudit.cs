using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace DiscordScheduler
{
    public sealed class RequiredUiControl
    {
        public RequiredUiControl(Type controlType, string name, string view, string role, string userPromise, string sideEffectRisk)
        {
            this.controlType = controlType;
            this.name = name ?? "";
            this.view = view ?? "";
            this.role = role ?? "";
            this.userPromise = userPromise ?? "";
            this.missingSeverity = "blocker";
            this.sideEffectRisk = sideEffectRisk ?? "";
        }

        public Type controlType;
        public string name;
        public string view;
        public string role;
        public string userPromise;
        public string missingSeverity;
        public string sideEffectRisk;
    }

    public sealed class UiBindingAuditIssue
    {
        public string severity = "";
        public string code = "";
        public string controlName = "";
        public string expectedType = "";
        public string view = "";
        public string userImpact = "";
        public string message = "";
    }

    public sealed class UiBindingAuditReport
    {
        public bool ok = true;
        public List<UiBindingAuditIssue> issues = new List<UiBindingAuditIssue>();

        public List<string> BlockingSummaries()
        {
            return Summaries("blocker");
        }

        public List<string> WarningSummaries()
        {
            return Summaries("warning");
        }

        private List<string> Summaries(string severity)
        {
            var summaries = new List<string>();
            for (int i = 0; i < issues.Count; i++)
            {
                var issue = issues[i];
                if (!string.Equals(issue.severity, severity, StringComparison.Ordinal))
                    continue;

                summaries.Add(issue.expectedType + ":" + issue.controlName + " (" + issue.message + ")");
            }

            return summaries;
        }
    }

    public sealed class UiBindingAudit
    {
        public UiBindingAuditReport Run(VisualElement root, bool includeNavigationSmoke = true)
        {
            var report = new UiBindingAuditReport();
            if (root == null)
            {
                AddIssue(report, "blocker", "missing-root", "Root", typeof(VisualElement), "App", "UI cannot start.", "Root visual element is missing.");
                return report;
            }

            var controls = UiBindingManifest.RequiredControls;
            for (int i = 0; i < controls.Count; i++)
                AuditControl(root, controls[i], report);

            if (includeNavigationSmoke)
                AuditNavigation(root, report);

            report.ok = !HasBlockers(report);
            return report;
        }

        private static void AuditControl(VisualElement root, RequiredUiControl control, UiBindingAuditReport report)
        {
            var matches = new List<VisualElement>();
            foreach (var match in root.Query<VisualElement>(control.name).Build())
                matches.Add(match);

            if (matches.Count == 0)
            {
                AddIssue(report, "blocker", "missing-control", control.name, control.controlType, control.view, control.userPromise, "Required control is missing.");
                return;
            }

            var typedMatches = 0;
            for (int i = 0; i < matches.Count; i++)
            {
                if (control.controlType.IsInstanceOfType(matches[i]))
                    typedMatches++;
            }

            if (typedMatches == 0)
            {
                AddIssue(report, "blocker", "wrong-control-type", control.name, control.controlType, control.view, control.userPromise, "Required control has the wrong type.");
                return;
            }

            if (typedMatches > 1)
            {
                AddIssue(report, "warning", "duplicate-control-name", control.name, control.controlType, control.view, control.sideEffectRisk, "Multiple controls share the same required name.");
            }
        }

        private static void AuditNavigation(VisualElement root, UiBindingAuditReport report)
        {
            var visible = 0;
            string visibleName = "";
            var viewNames = UiBindingManifest.ViewNames;
            for (int i = 0; i < viewNames.Count; i++)
            {
                var view = root.Q<VisualElement>(viewNames[i]);
                if (view == null)
                    continue;

                if (IsVisibleView(view))
                {
                    visible++;
                    visibleName = view.name;
                }
            }

            if (visible != 1)
            {
                AddIssue(report, "blocker", "navigation-visible-count", "views", typeof(VisualElement), "Navigation", "Wrong view may be visible to the user.", "Expected exactly one visible view, found " + visible + ".");
            }
            else if (string.IsNullOrWhiteSpace(visibleName))
            {
                AddIssue(report, "warning", "navigation-visible-name", "views", typeof(VisualElement), "Navigation", "Active view has no name.", "Active view name is empty.");
            }
        }

        private static bool IsVisibleView(VisualElement view)
        {
            if (view == null)
                return false;

            if (view.resolvedStyle.display == DisplayStyle.None)
                return false;

            return view.style.display.value != DisplayStyle.None;
        }

        private static bool HasBlockers(UiBindingAuditReport report)
        {
            for (int i = 0; i < report.issues.Count; i++)
                if (report.issues[i].severity == "blocker")
                    return true;

            return false;
        }

        private static void AddIssue(
            UiBindingAuditReport report,
            string severity,
            string code,
            string controlName,
            Type expectedType,
            string view,
            string userImpact,
            string message)
        {
            report.issues.Add(new UiBindingAuditIssue
            {
                severity = severity ?? "",
                code = code ?? "",
                controlName = controlName ?? "",
                expectedType = expectedType?.Name ?? "",
                view = view ?? "",
                userImpact = userImpact ?? "",
                message = message ?? ""
            });
        }
    }

    public static class UiBindingManifest
    {
        public static readonly List<string> ViewNames = new List<string>
        {
            "viewDashboard",
            "viewTargets",
            "viewNewPost",
            "viewPosts",
            "viewReview",
            "viewSettings",
            "viewLog"
        };

        public static readonly List<RequiredUiControl> RequiredControls = new List<RequiredUiControl>
        {
            R<Button>("btnDashboard", "Shell", "Open Dashboard", "Dashboard must be reachable.", "Lost navigation."),
            R<Button>("btnTargets", "Shell", "Open Targets", "Targets must be reachable.", "Lost navigation."),
            R<Button>("btnNewPost", "Shell", "Open New Post", "Scheduling must be reachable.", "Lost navigation."),
            R<Button>("btnPosts", "Shell", "Open Posts", "Post management must be reachable.", "Lost navigation."),
            R<Button>("btnReview", "Shell", "Open Review", "Ambiguous sends must be reachable.", "Lost navigation."),
            R<Button>("btnSettings", "Shell", "Open Settings", "Settings must be reachable.", "Lost navigation."),
            R<Button>("btnLog", "Shell", "Open Log", "Diagnostics must be reachable.", "Lost navigation."),
            R<Label>("lblStatus", "Shell", "Status", "User sees current app status.", "Invisible status."),
            R<Label>("lblHealthState", "Shell", "Health pill", "User sees health at a glance.", "Invisible health."),
            R<Label>("lblNavPending", "Shell", "Pending count", "User sees pending count.", "Misleading navigation."),
            R<Label>("lblNavQueue", "Shell", "Queue count", "User sees queue count.", "Misleading navigation."),
            R<Label>("lblNavReview", "Shell", "Review count", "User sees review count.", "Misleading navigation."),

            R<VisualElement>("viewDashboard", "Views", "Dashboard view", "Dashboard renders.", "Broken view."),
            R<VisualElement>("viewTargets", "Views", "Targets view", "Targets renders.", "Broken view."),
            R<VisualElement>("viewNewPost", "Views", "New Post view", "Scheduling renders.", "Broken view."),
            R<VisualElement>("viewPosts", "Views", "Posts view", "Post editing renders.", "Broken view."),
            R<VisualElement>("viewReview", "Views", "Review view", "Review renders.", "Broken view."),
            R<VisualElement>("viewSettings", "Views", "Settings view", "Settings renders.", "Broken view."),
            R<VisualElement>("viewLog", "Views", "Log view", "Log renders.", "Broken view."),

            R<Button>("btnDashboardTargets", "Dashboard", "Add target shortcut", "User can create a target from dashboard.", "Lost shortcut."),
            R<Button>("btnDashboardReview", "Dashboard", "Review shortcut", "User can jump to review.", "Lost shortcut."),
            R<Button>("btnDashboardNewPost", "Dashboard", "New post shortcut", "User can schedule from dashboard.", "Lost shortcut."),
            R<Label>("lblMetricTargets", "Dashboard", "Target metric", "Target count is visible.", "Wrong health summary."),
            R<Label>("lblMetricPending", "Dashboard", "Pending metric", "Pending count is visible.", "Wrong health summary."),
            R<Label>("lblMetricQueue", "Dashboard", "Queue metric", "Queue count is visible.", "Wrong health summary."),
            R<Label>("lblMetricReview", "Dashboard", "Review metric", "Review count is visible.", "Wrong health summary."),
            R<Label>("lblMetricWarnings", "Dashboard", "Warning metric", "Warning count is visible.", "Wrong health summary."),
            R<Label>("lblDashboardSummary", "Dashboard", "Summary", "Operational summary is visible.", "Invisible health."),
            R<Label>("lblQueueHealthSummary", "Dashboard", "Queue health", "Discord wait/backoff state is visible.", "Random-looking queue delay."),
            R<Label>("lblNextPost", "Dashboard", "Next post", "Next post is visible.", "Invisible queue."),
            R<Label>("lblSafetySummary", "Dashboard", "Safety summary", "Safety guidance is visible.", "Invisible safety."),

            R<ListView>("targetsList", "Targets", "Target list", "Saved targets are visible.", "Target edit/delete confusion."),
            R<Label>("lblTargetsEmpty", "Targets", "Target empty state", "First-run target setup has a clear next step.", "Confusing first run."),
            R<TextField>("tfTargetName", "Targets", "Target name", "Target can be named.", "Bad target data."),
            R<TextField>("tfTargetServer", "Targets", "Server label", "Server label can be edited.", "Bad target data."),
            R<TextField>("tfTargetChannel", "Targets", "Channel label", "Channel label can be edited.", "Bad target data."),
            R<TextField>("tfTargetWebhook", "Targets", "Webhook URL", "Webhook can be entered.", "Cannot send."),
            R<TextField>("tfTargetUsername", "Targets", "Username override", "Override username can be edited.", "Bad target data."),
            R<TextField>("tfTargetAvatar", "Targets", "Avatar override", "Override avatar can be edited.", "Bad target data."),
            R<Label>("lblTargetHint", "Targets", "Target hint", "Validation feedback is visible.", "Invisible error."),
            R<Label>("lblTargetHealth", "Targets", "Target health", "Webhook validation health is visible.", "Invalid target stays hidden."),
            R<Label>("lblTargetImpact", "Targets", "Target impact", "Mutation impact is visible.", "Risky target mutation."),
            R<Button>("btnTargetAdd", "Targets", "New target", "Target creation works.", "Lost creation."),
            R<Button>("btnTargetSave", "Targets", "Save target", "Target save works.", "Lost save."),
            R<Button>("btnTargetDelete", "Targets", "Delete target", "Target delete is guarded.", "Wrong delete."),
            R<Button>("btnTargetTest", "Targets", "Test target", "Manual test send is reachable.", "Wrong send."),

            R<DropdownField>("ddPostTarget", "New Post", "Target picker", "New post gets target.", "Cannot schedule."),
            R<TextField>("tfPostTitle", "New Post", "Title", "Title can be edited.", "Bad payload."),
            R<TextField>("tfPostBody", "New Post", "Body", "Body can be edited.", "Bad payload."),
            R<TextField>("tfDate", "New Post", "Date", "Date can be selected.", "Wrong schedule."),
            R<TextField>("tfTime", "New Post", "Time", "Time can be selected.", "Wrong schedule."),
            R<Toggle>("tgPostModeNormal", "New Post", "Message mode", "Message mode works.", "Bad payload."),
            R<Toggle>("tgPostModeEmbed", "New Post", "Embed mode", "Embed mode works.", "Bad payload."),
            R<Toggle>("tgAllowUsers", "New Post", "Allow users", "Mention safety works.", "Unsafe mention."),
            R<Toggle>("tgAllowRoles", "New Post", "Allow roles", "Mention safety works.", "Unsafe mention."),
            R<Toggle>("tgAllowEveryone", "New Post", "Allow everyone", "Mention safety works.", "Unsafe mention."),
            R<TextField>("tfMentionUserIds", "New Post", "User IDs", "Mention allowlist works.", "Unsafe mention."),
            R<TextField>("tfMentionRoleIds", "New Post", "Role IDs", "Mention allowlist works.", "Unsafe mention."),
            R<DropdownField>("ddOffPolicy", "New Post", "Off policy", "Missed policy is explicit.", "Wrong missed behavior."),
            R<DropdownField>("ddSleepPolicy", "New Post", "Sleep policy", "Sleep policy is explicit.", "Wrong missed behavior."),
            R<Label>("lblScheduleResult", "New Post", "Schedule result", "Schedule feedback is visible.", "Invisible error."),
            R<Label>("lblPostBodyCounter", "New Post", "Body counter", "Length feedback is visible.", "Discord limit error."),
            R<VisualElement>("payloadPreviewPanel", "New Post", "Payload preview panel", "Dry-run result is visible.", "Invisible preflight."),
            R<Button>("btnPayloadPreviewRefresh", "New Post", "Refresh preview", "Dry-run can be refreshed.", "Stale preview."),
            R<Label>("lblPayloadPreviewHeadline", "New Post", "Preview headline", "Preview verdict is visible.", "Invisible preflight."),
            R<Label>("lblPayloadPreviewDigest", "New Post", "Preview digest", "Preview summary is visible.", "Invisible preflight."),
            R<Label>("lblPayloadPreviewDetails", "New Post", "Preview details", "Preview blocker details are visible.", "Invisible preflight."),
            R<Label>("lblPayloadPreviewPayload", "New Post", "Redacted payload", "Technical payload evidence is safe.", "Secret leak."),
            R<Button>("btnSchedule", "New Post", "Schedule", "Schedule command works.", "Lost schedule."),
            R<Label>("lblLocalHint", "New Post", "Timezone hint", "User sees local timezone.", "Wrong schedule."),
            R<Toggle>("tgAttachMedia", "New Post", "Attach media", "Attachment opt-in works.", "Bad attachment."),
            R<VisualElement>("mediaOptionsNew", "New Post", "Media options", "Media options are visible.", "Bad attachment."),
            R<Toggle>("tgMediaIsImage", "New Post", "Image mode", "Image attachment works.", "Bad attachment."),
            R<Toggle>("tgMediaIsVideo", "New Post", "Video mode", "Video attachment works.", "Bad attachment."),
            R<TextField>("tfMediaPath", "New Post", "Media path", "Media path can be entered.", "Bad attachment."),
            R<VisualElement>("mediaButtonsNew", "New Post", "Media buttons", "Media commands are reachable.", "Bad attachment."),
            R<Button>("btnMediaClear", "New Post", "Clear media", "Media can be cleared.", "Stale attachment."),
            R<DropdownField>("ddMediaPick", "New Post", "Attachment picker", "Managed attachments can be picked.", "Bad attachment."),
            R<Label>("lblMediaHint", "New Post", "Media hint", "Attachment feedback is visible.", "Invisible error."),
            R<Image>("imgMediaPreview", "New Post", "Media preview", "Media preview is visible.", "Bad attachment."),
            R<VisualElement>("mediaPreviewNew", "New Post", "Preview container", "Preview area is visible.", "Bad attachment."),
            R<Button>("btnMediaBrowse", "New Post", "Browse", "File picker is reachable.", "Bad attachment."),
            R<Button>("btnMediaOpenAttachments", "New Post", "Open attachments", "Attachment folder is reachable.", "Bad attachment."),
            R<Button>("btnMediaRefresh", "New Post", "Refresh attachments", "Attachment list can refresh.", "Bad attachment."),

            R<ListView>("postsList", "Posts", "Post list", "Saved posts are visible.", "Wrong edit/delete."),
            R<Label>("lblPostsEmpty", "Posts", "Posts empty state", "First-run scheduling has a clear next step.", "Confusing first run."),
            R<Label>("lblPostSelectionHint", "Posts", "Post selection hint", "User knows to select a post before editing.", "Confusing edit panel."),
            R<DropdownField>("ddEditTarget", "Posts", "Edit target", "Target can be changed.", "Wrong target."),
            R<TextField>("tfEditTitle", "Posts", "Edit title", "Title can be changed.", "Bad payload."),
            R<TextField>("tfEditBody", "Posts", "Edit body", "Body can be changed.", "Bad payload."),
            R<TextField>("tfEditDate", "Posts", "Edit date", "Date can be changed.", "Wrong schedule."),
            R<TextField>("tfEditTime", "Posts", "Edit time", "Time can be changed.", "Wrong schedule."),
            R<TextField>("tfEditImagePath", "Posts", "Edit media path", "Media path can be changed.", "Bad attachment."),
            R<DropdownField>("ddEditAttachmentPick", "Posts", "Edit attachment picker", "Managed attachment can be picked.", "Bad attachment."),
            R<Image>("imgEditPreview", "Posts", "Edit media preview", "Media preview is visible.", "Bad attachment."),
            R<Toggle>("tgEditModeNormal", "Posts", "Edit message mode", "Message mode works.", "Bad payload."),
            R<Toggle>("tgEditModeEmbed", "Posts", "Edit embed mode", "Embed mode works.", "Bad payload."),
            R<Toggle>("tgEditAllowUsers", "Posts", "Edit allow users", "Mention safety works.", "Unsafe mention."),
            R<Toggle>("tgEditAllowRoles", "Posts", "Edit allow roles", "Mention safety works.", "Unsafe mention."),
            R<Toggle>("tgEditAllowEveryone", "Posts", "Edit allow everyone", "Mention safety works.", "Unsafe mention."),
            R<TextField>("tfEditMentionUserIds", "Posts", "Edit user IDs", "Mention allowlist works.", "Unsafe mention."),
            R<TextField>("tfEditMentionRoleIds", "Posts", "Edit role IDs", "Mention allowlist works.", "Unsafe mention."),
            R<DropdownField>("ddEditOffPolicy", "Posts", "Edit off policy", "Missed policy can be changed.", "Wrong missed behavior."),
            R<DropdownField>("ddEditSleepPolicy", "Posts", "Edit sleep policy", "Sleep policy can be changed.", "Wrong missed behavior."),
            R<Label>("lblEditStatus", "Posts", "Edit status", "Status is visible.", "Invisible status."),
            R<Label>("lblEditResult", "Posts", "Edit result", "Edit feedback is visible.", "Invisible error."),
            R<Label>("lblEditBodyCounter", "Posts", "Edit body counter", "Length feedback is visible.", "Discord limit error."),
            R<Button>("btnPostSave", "Posts", "Save post", "Post save works.", "Duplicate save."),
            R<Button>("btnPostDelete", "Posts", "Delete post", "Post delete is guarded.", "Wrong delete."),
            R<Button>("btnPostForceSend", "Posts", "Send now", "Manual send is reachable.", "Duplicate send."),
            R<Button>("btnPostDeleteSent", "Posts", "Delete sent", "Sent cleanup is guarded.", "Wrong delete."),
            R<Button>("btnPostMarkPending", "Posts", "Requeue pending", "Requeue command is guarded.", "Wrong retry."),
            R<Button>("btnEditImageClear", "Posts", "Clear media", "Media can be cleared.", "Stale attachment."),
            R<Button>("btnOpenAttachments2", "Posts", "Open attachments", "Attachment folder is reachable.", "Duplicate command."),
            R<Button>("btnEditRefreshAttachments", "Posts", "Refresh attachments", "Attachment list can refresh.", "Bad attachment."),

            R<ListView>("reviewList", "Review", "Review list", "NeedsReview posts are visible.", "Lost review."),
            R<Label>("lblReviewSummary", "Review", "Review summary", "Review impact is visible.", "Invisible safety."),
            R<Label>("lblReviewSelectedEvidence", "Review", "Selected review evidence", "Retry evidence is visible.", "Invisible safety."),
            R<Label>("lblReviewEmpty", "Review", "Empty state", "Empty review state is clear.", "Confusing review."),
            R<Button>("btnReviewRefresh", "Review", "Refresh review", "Review can refresh.", "Stale review."),
            R<Button>("btnReviewOpenPosts", "Review", "Open selected", "Selected review item can be inspected.", "Lost inspect."),
            R<Button>("btnReviewRetry", "Review", "Retry selected", "Safe retry is explicit.", "Duplicate send."),
            R<Button>("btnReviewDismiss", "Review", "Dismiss selected", "Dismiss decision is explicit.", "Wrong review decision."),

            R<IntegerField>("ifSleepThreshold", "Settings", "Sleep threshold", "Sleep threshold can be set.", "Wrong missed behavior."),
            R<Toggle>("tgDefaultAllowUsers", "Settings", "Default allow users", "Default mention safety works.", "Unsafe mention."),
            R<Toggle>("tgDefaultAllowRoles", "Settings", "Default allow roles", "Default mention safety works.", "Unsafe mention."),
            R<Toggle>("tgDefaultAllowEveryone", "Settings", "Default allow everyone", "Default mention safety works.", "Unsafe mention."),
            R<TextField>("tfDefaultUserIds", "Settings", "Default user IDs", "Default mention allowlist works.", "Unsafe mention."),
            R<TextField>("tfDefaultRoleIds", "Settings", "Default role IDs", "Default mention allowlist works.", "Unsafe mention."),
            R<DropdownField>("ddDefaultOffPolicy", "Settings", "Default off policy", "Default missed policy works.", "Wrong missed behavior."),
            R<DropdownField>("ddDefaultSleepPolicy", "Settings", "Default sleep policy", "Default sleep policy works.", "Wrong missed behavior."),
            R<Label>("lblPaths", "Settings", "Paths", "Local paths are visible.", "Invisible diagnostics."),
            R<Label>("lblSettingsResult", "Settings", "Settings result", "Settings feedback is visible.", "Invisible error."),
            R<Button>("btnSaveSettings", "Settings", "Save settings", "Settings save works.", "Lost save."),
            R<Button>("btnOpenDataFolder", "Settings", "Open data folder", "Data folder is reachable.", "Lost diagnostics."),
            R<Label>("lblSupportBundleSummary", "Settings", "Support bundle summary", "Redacted export dry-run is visible.", "Support evidence missing."),
            R<Label>("lblSecretStorageSummary", "Settings", "Secret storage summary", "Secret storage health is visible.", "Secret migration risk hidden."),
            R<Label>("lblSecretMigrationSummary", "Settings", "Secret migration summary", "Secret ref migration status is visible.", "Secret migration risk hidden."),
            R<Button>("btnSupportBundleDryRun", "Settings", "Support bundle dry-run", "Bundle preview is reachable.", "Unsafe blind export."),
            R<Button>("btnSupportBundleExport", "Settings", "Support bundle export", "Redacted export is reachable.", "No safe support export."),
            R<Button>("btnSecretMigrationDryRun", "Settings", "Secret migration dry-run", "Secret migration preview is reachable.", "Unsafe blind migration."),
            R<Button>("btnSecretMigrationApplyRefs", "Settings", "Secret migration apply", "Verified secret refs can be written explicitly.", "Unsafe secret migration."),
            R<Label>("lblSupportBundleResult", "Settings", "Support bundle result", "Export feedback is visible.", "Silent export failure."),
            R<Label>("lblRetentionSummary", "Settings", "Retention summary", "Retention dry-run is visible.", "Blind cleanup risk."),
            R<Label>("lblRestoreSummary", "Settings", "Restore summary", "Restore safety check is visible.", "Restore risk hidden."),
            R<TextField>("tfRestoreFilePath", "Settings", "Restore file path", "Restore source can be selected.", "Blind restore risk."),
            R<Button>("btnRetentionDryRun", "Settings", "Retention dry-run", "Cleanup preview is reachable.", "Blind cleanup risk."),
            R<Button>("btnRetentionApply", "Settings", "Retention apply", "Cleanup apply is explicit.", "Uncontrolled deletion."),
            R<Button>("btnRestoreCheckCurrent", "Settings", "Restore check", "Current DB safety check is reachable.", "Restore risk hidden."),
            R<Button>("btnRestoreBrowse", "Settings", "Restore browse", "Restore source picker is reachable.", "Wrong restore source."),
            R<Button>("btnRestoreDryRun", "Settings", "Restore dry-run", "Restore preview is reachable.", "Blind restore risk."),
            R<Button>("btnRestoreApply", "Settings", "Restore apply", "Restore apply is explicit.", "Uncontrolled restore."),
            R<Label>("lblRetentionResult", "Settings", "Retention result", "Cleanup feedback is visible.", "Silent cleanup failure."),
            R<Label>("lblRestoreResult", "Settings", "Restore result", "Restore feedback is visible.", "Silent restore failure."),

            R<ScrollView>("logScroll", "Log", "Log scroll", "Logs are visible.", "Lost diagnostics."),
            R<Label>("lblLogEmpty", "Log", "Log empty state", "Empty diagnostics state is clear.", "Confusing diagnostics."),
            R<Button>("btnLogClear", "Log", "Clear log", "Log clear works.", "Wrong diagnostics."),
            R<Button>("btnLogRefresh", "Log", "Refresh log", "Log refresh works.", "Stale diagnostics.")
        };

        private static RequiredUiControl R<T>(string name, string view, string role, string userPromise, string sideEffectRisk)
            where T : VisualElement
        {
            return new RequiredUiControl(typeof(T), name, view, role, userPromise, sideEffectRisk);
        }
    }
}
