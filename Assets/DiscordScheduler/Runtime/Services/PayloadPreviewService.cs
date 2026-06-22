using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DiscordScheduler
{
    public sealed class PayloadPreviewService
    {
        private readonly WebhookUrlValidator _webhookUrlValidator;
        private readonly PayloadTextNormalizer _textNormalizer;
        private readonly DiscordPayloadValidator _payloadValidator;
        private readonly DiscordWebhookPayloadBuilder _payloadBuilder;
        private readonly TargetRevisionPolicy _targetRevisionPolicy;
        private readonly IWebhookSecretResolver _webhookSecretResolver;
        private readonly IFileSystem _fileSystem;
        private readonly Func<DateTime> _utcNow;

        public PayloadPreviewService(
            WebhookUrlValidator webhookUrlValidator = null,
            PayloadTextNormalizer textNormalizer = null,
            DiscordPayloadValidator payloadValidator = null,
            DiscordWebhookPayloadBuilder payloadBuilder = null,
            TargetRevisionPolicy targetRevisionPolicy = null,
            IWebhookSecretResolver webhookSecretResolver = null,
            IFileSystem fileSystem = null,
            Func<DateTime> utcNow = null)
        {
            _webhookUrlValidator = webhookUrlValidator ?? new WebhookUrlValidator();
            _textNormalizer = textNormalizer ?? new PayloadTextNormalizer();
            _payloadValidator = payloadValidator ?? new DiscordPayloadValidator(_textNormalizer);
            _payloadBuilder = payloadBuilder ?? new DiscordWebhookPayloadBuilder(_textNormalizer);
            _targetRevisionPolicy = targetRevisionPolicy ?? new TargetRevisionPolicy();
            _webhookSecretResolver = webhookSecretResolver ?? new WebhookSecretResolver();
            _fileSystem = fileSystem ?? new SystemFileSystem();
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public PayloadPreview Build(Target target, PostDraft draft)
        {
            var preview = new PayloadPreview();

            if (draft == null)
            {
                AddBlocker(preview, "missing-draft", "Missing post draft.");
                return preview;
            }

            var normalizedTitle = _textNormalizer.Normalize(draft.title);
            var normalizedBody = _textNormalizer.Normalize(draft.body);
            preview.normalizedBodyPreview = _textNormalizer.BuildNormalContent(normalizedTitle, normalizedBody);
            preview.normalizedTextLength = preview.normalizedBodyPreview.Length;
            preview.allowedMentionsSummary = BuildAllowedMentionsSummary(draft.allowedMentions);

            var mediaPath = (draft.mediaPath ?? "").Trim().Trim('"');
            var hasMedia = !string.IsNullOrWhiteSpace(mediaPath);
            var mediaKind = ResolveMediaKind(mediaPath);
            preview.mediaMetadata = ProbeMedia(mediaPath, mediaKind);
            preview.mediaSummary = BuildMediaSummary(preview.mediaMetadata);
            preview.fingerprint = BuildFingerprint(target, draft, mediaKind, preview.mediaMetadata);

            var resolvedTarget = target;
            if (target == null)
            {
                AddBlocker(preview, "missing-target", "Target is required.");
            }
            else
            {
                var secretResolution = _webhookSecretResolver.Resolve(target);
                if (!secretResolution.ok)
                {
                    AddBlocker(preview, "target-webhook-secret", secretResolution.error);
                }
                else
                {
                    resolvedTarget = WebhookSecretResolver.CloneWithWebhookUrl(target, secretResolution.webhookUrl);
                    var urlValidation = _webhookUrlValidator.Validate(resolvedTarget.webhookUrl);
                    if (!urlValidation.ok)
                        AddBlocker(preview, "target-webhook", urlValidation.error);
                }
            }

            if (hasMedia)
                ValidateMedia(preview, preview.mediaMetadata, mediaKind);

            var payloadValidation = _payloadValidator.Validate(resolvedTarget, draft);
            if (!payloadValidation.ok)
                AddBlocker(preview, "payload", payloadValidation.error);

            preview.canSend = preview.blockers.Count == 0;
            if (!preview.canSend)
                return preview;

            var embedFilename = mediaKind == MediaKind.Image && hasMedia ? Path.GetFileName(mediaPath) : "";
            preview.payloadJson = _payloadBuilder.Build(resolvedTarget, draft, embedFilename, hasMedia);
            return preview;
        }

        public PayloadPreviewFingerprint BuildFingerprint(Target target, PostDraft draft)
        {
            if (draft == null)
                return new PayloadPreviewFingerprint();

            var mediaPath = (draft.mediaPath ?? "").Trim().Trim('"');
            var mediaKind = ResolveMediaKind(mediaPath);
            return BuildFingerprint(target, draft, mediaKind, ProbeMedia(mediaPath, mediaKind));
        }

        private void ValidateMedia(PayloadPreview preview, PayloadPreviewMediaMetadata media, MediaKind mediaKind)
        {
            if (mediaKind == MediaKind.None)
            {
                AddBlocker(preview, "media-kind", "Unsupported media type.");
                return;
            }

            if (!media.exists)
            {
                AddBlocker(preview, "media-missing", "Media file not found.");
                return;
            }

            if (!media.canReadSize)
            {
                AddBlocker(preview, "media-size", media.errorMessage);
                return;
            }

            if (media.sizeBytes > MediaAttachmentRules.MaxAttachmentBytes)
                AddBlocker(preview, "media-too-large", MediaAttachmentRules.AttachmentTooLargeError);
        }

        private static MediaKind ResolveMediaKind(string mediaPath)
        {
            if (string.IsNullOrWhiteSpace(mediaPath))
                return MediaKind.None;

            try
            {
                if (MediaAttachmentRules.IsAllowedVideoPath(mediaPath))
                    return MediaKind.Video;

                if (MediaAttachmentRules.IsAllowedImagePath(mediaPath))
                    return MediaKind.Image;
            }
            catch
            {
                return MediaKind.None;
            }

            return MediaKind.None;
        }

        private static string BuildAllowedMentionsSummary(AllowedMentions mentions)
        {
            if (mentions == null)
                return "No mention parsing.";

            var users = CountCsv(mentions.userIdsCsv);
            var roles = CountCsv(mentions.roleIdsCsv);
            var summary = mentions.allowEveryone ? "everyone" : "no everyone";
            summary += BuildMentionTypeSummary(mentions.allowUsers, users, "users");
            summary += BuildMentionTypeSummary(mentions.allowRoles, roles, "roles");
            return summary;
        }

        private static string BuildMentionTypeSummary(bool allowed, int explicitCount, string label)
        {
            if (!allowed)
                return ", " + label + " disabled";

            if (explicitCount > 0)
                return ", explicit " + label + ": " + explicitCount;

            return ", " + label + " parse";
        }

        private PayloadPreviewMediaMetadata ProbeMedia(string mediaPath, MediaKind mediaKind)
        {
            var metadata = new PayloadPreviewMediaMetadata
            {
                hasMedia = !string.IsNullOrWhiteSpace(mediaPath),
                mediaKind = mediaKind.ToString(),
                pathHash = HashParts(mediaPath ?? "")
            };

            if (string.IsNullOrWhiteSpace(mediaPath))
                return metadata;

            try
            {
                metadata.fileName = Path.GetFileName(mediaPath);
            }
            catch
            {
                metadata.fileName = "attachment";
            }

            metadata.exists = _fileSystem.FileExists(mediaPath);
            if (!metadata.exists)
            {
                metadata.errorCode = "media-missing";
                metadata.errorMessage = "Media file not found.";
                return metadata;
            }

            if (!_fileSystem.TryGetFileSizeBytes(mediaPath, out var sizeBytes, out var sizeError))
            {
                metadata.errorCode = "media-size";
                metadata.errorMessage = string.IsNullOrWhiteSpace(sizeError) ? "Cannot read media size." : sizeError;
                return metadata;
            }

            metadata.canReadSize = true;
            metadata.sizeBytes = sizeBytes;
            return metadata;
        }

        private static string BuildMediaSummary(PayloadPreviewMediaMetadata metadata)
        {
            if (metadata == null || !metadata.hasMedia)
                return "No media.";

            var fileName = string.IsNullOrWhiteSpace(metadata.fileName) ? "attachment" : metadata.fileName;
            if (metadata.mediaKind == MediaKind.None.ToString())
                return "Unsupported media: " + fileName;

            return metadata.mediaKind + ": " + fileName;
        }

        private static int CountCsv(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
                return 0;

            var count = 0;
            var parts = csv.Split(',');
            for (int i = 0; i < parts.Length; i++)
                if (!string.IsNullOrWhiteSpace(parts[i]))
                    count++;

            return count;
        }

        private PayloadPreviewFingerprint BuildFingerprint(
            Target target,
            PostDraft draft,
            MediaKind mediaKind,
            PayloadPreviewMediaMetadata media)
        {
            media = media ?? new PayloadPreviewMediaMetadata();
            return new PayloadPreviewFingerprint
            {
                targetId = target?.id ?? draft?.targetId ?? "",
                targetRevision = _targetRevisionPolicy.GetTargetRevision(target),
                draftId = draft?.id ?? "",
                contentHash = HashParts(
                    draft?.targetId,
                    draft?.title,
                    draft?.body,
                    draft?.dateYmd,
                    draft?.timeHm,
                    draft?.sendAsEmbed.ToString(),
                    draft?.allowedMentions?.allowUsers.ToString(),
                    draft?.allowedMentions?.allowRoles.ToString(),
                    draft?.allowedMentions?.allowEveryone.ToString(),
                    draft?.allowedMentions?.userIdsCsv,
                    draft?.allowedMentions?.roleIdsCsv,
                    draft?.missedPolicyIfOff.ToString(),
                    draft?.missedPolicyIfSleep.ToString()),
                mediaHash = HashParts(media.pathHash, media.sizeBytes.ToString(), media.exists.ToString()),
                mediaKind = mediaKind.ToString(),
                mediaExists = media.exists,
                mediaSizeBytes = media.sizeBytes,
                createdAtUtcIso = TimeUtil.ToIsoUtc(_utcNow())
            };
        }

        private static string HashParts(params string[] parts)
        {
            using (var sha = SHA256.Create())
            {
                var text = string.Join("\n", parts ?? new string[0]);
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                    sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static void AddBlocker(PayloadPreview preview, string code, string message)
        {
            preview.blockers.Add(new PreviewBlocker
            {
                code = code ?? "",
                message = message ?? ""
            });
        }
    }
}
