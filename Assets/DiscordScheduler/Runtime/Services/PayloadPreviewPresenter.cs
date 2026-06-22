namespace DiscordScheduler
{
    public sealed class PayloadPreviewPresenter
    {
        public PayloadPreviewViewModel Build(PayloadPreview preview, PayloadPreviewFingerprint currentFingerprint = null)
        {
            var viewModel = new PayloadPreviewViewModel();
            if (preview == null)
            {
                viewModel.headline = "Preview unavailable";
                viewModel.readiness = "Blocked";
                viewModel.primaryAction = "Fix draft";
                viewModel.disabledReason = "Preview data is missing.";
                return viewModel;
            }

            viewModel.canSend = preview.canSend;
            viewModel.isStale = currentFingerprint != null && !preview.fingerprint.Matches(currentFingerprint);
            viewModel.technicalDetailsAvailable = !string.IsNullOrWhiteSpace(preview.payloadJson);
            viewModel.redactedPayloadJson = SecretRedactor.RedactAndTruncate(preview.payloadJson, 1200);
            viewModel.previewDigest = BuildDigest(preview);

            if (viewModel.isStale)
            {
                viewModel.headline = "Preview is stale";
                viewModel.readiness = "Refresh required";
                viewModel.primaryAction = "Refresh preview";
                viewModel.disabledReason = "The draft, target or media changed after this preview was generated.";
            }
            else if (preview.canSend)
            {
                viewModel.headline = "Ready to schedule";
                viewModel.readiness = "Ready";
                viewModel.primaryAction = "Schedule post";
                viewModel.disabledReason = "";
            }
            else
            {
                viewModel.headline = "Cannot schedule yet";
                viewModel.readiness = "Blocked";
                viewModel.primaryAction = "Fix blocker";
                viewModel.disabledReason = FirstBlockerMessage(preview);
                viewModel.firstBlockingField = FirstBlockerCode(preview);
            }

            AddRow(viewModel, "Content", preview.normalizedTextLength + " characters", preview.normalizedTextLength > 1900 ? "warning" : "info");
            AddRow(viewModel, "Mentions", preview.allowedMentionsSummary, "info");
            AddRow(viewModel, "Media", preview.mediaSummary, MediaSeverity(preview));

            for (int i = 0; i < preview.blockers.Count; i++)
                AddRow(viewModel, "Blocker", preview.blockers[i].message, "blocker");

            for (int i = 0; i < preview.warnings.Count; i++)
                AddRow(viewModel, "Warning", preview.warnings[i].message, "warning");

            return viewModel;
        }

        private static string BuildDigest(PayloadPreview preview)
        {
            if (preview == null)
                return "";

            var media = preview.mediaMetadata == null || !preview.mediaMetadata.hasMedia
                ? "no media"
                : preview.mediaSummary;

            return $"{preview.normalizedTextLength} chars, {media}, {preview.allowedMentionsSummary}";
        }

        private static string FirstBlockerMessage(PayloadPreview preview)
        {
            return preview.blockers.Count == 0 ? "" : preview.blockers[0].message;
        }

        private static string FirstBlockerCode(PayloadPreview preview)
        {
            return preview.blockers.Count == 0 ? "" : preview.blockers[0].code;
        }

        private static string MediaSeverity(PayloadPreview preview)
        {
            if (preview?.mediaMetadata == null || !preview.mediaMetadata.hasMedia)
                return "info";

            if (!preview.mediaMetadata.exists || !preview.mediaMetadata.canReadSize)
                return "blocker";

            if (preview.mediaMetadata.sizeBytes > MediaAttachmentRules.MaxAttachmentBytes)
                return "blocker";

            return "info";
        }

        private static void AddRow(PayloadPreviewViewModel viewModel, string label, string value, string severity)
        {
            viewModel.summaryRows.Add(new PayloadPreviewSummaryRow
            {
                label = label ?? "",
                value = SecretRedactor.RedactAndTruncate(value ?? "", 500),
                severity = severity ?? "info"
            });
        }
    }
}
