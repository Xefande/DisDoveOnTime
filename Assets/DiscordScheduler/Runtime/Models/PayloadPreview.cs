using System.Collections.Generic;

namespace DiscordScheduler
{
    public sealed class PayloadPreview
    {
        public bool canSend;
        public string payloadJson = "";
        public string normalizedBodyPreview = "";
        public int normalizedTextLength;
        public PayloadPreviewFingerprint fingerprint = new PayloadPreviewFingerprint();
        public PayloadPreviewMediaMetadata mediaMetadata = new PayloadPreviewMediaMetadata();
        public List<PreviewWarning> warnings = new List<PreviewWarning>();
        public List<PreviewBlocker> blockers = new List<PreviewBlocker>();
        public string allowedMentionsSummary = "";
        public string mediaSummary = "";
    }

    public sealed class PayloadPreviewFingerprint
    {
        public string targetId = "";
        public string targetRevision = "";
        public string draftId = "";
        public string contentHash = "";
        public string mediaHash = "";
        public string mediaKind = "";
        public bool mediaExists;
        public long mediaSizeBytes;
        public string createdAtUtcIso = "";

        public bool Matches(PayloadPreviewFingerprint other)
        {
            if (other == null)
                return false;

            return string.Equals(targetId, other.targetId, System.StringComparison.Ordinal) &&
                   string.Equals(targetRevision, other.targetRevision, System.StringComparison.Ordinal) &&
                   string.Equals(draftId, other.draftId, System.StringComparison.Ordinal) &&
                   string.Equals(contentHash, other.contentHash, System.StringComparison.Ordinal) &&
                   string.Equals(mediaHash, other.mediaHash, System.StringComparison.Ordinal) &&
                   string.Equals(mediaKind, other.mediaKind, System.StringComparison.Ordinal) &&
                   mediaExists == other.mediaExists &&
                   mediaSizeBytes == other.mediaSizeBytes;
        }
    }

    public sealed class PayloadPreviewMediaMetadata
    {
        public bool hasMedia;
        public bool exists;
        public bool canReadSize;
        public long sizeBytes;
        public string fileName = "";
        public string mediaKind = "";
        public string pathHash = "";
        public string errorCode = "";
        public string errorMessage = "";
    }

    public sealed class PayloadPreviewViewModel
    {
        public bool canSend;
        public bool isStale;
        public string headline = "";
        public string readiness = "";
        public string primaryAction = "";
        public string disabledReason = "";
        public string firstBlockingField = "";
        public string previewDigest = "";
        public string redactedPayloadJson = "";
        public bool technicalDetailsAvailable;
        public List<PayloadPreviewSummaryRow> summaryRows = new List<PayloadPreviewSummaryRow>();
    }

    public sealed class PayloadPreviewSummaryRow
    {
        public string label = "";
        public string value = "";
        public string severity = "";
    }

    public sealed class PreviewWarning
    {
        public string code = "";
        public string message = "";
    }

    public sealed class PreviewBlocker
    {
        public string code = "";
        public string message = "";
    }
}
