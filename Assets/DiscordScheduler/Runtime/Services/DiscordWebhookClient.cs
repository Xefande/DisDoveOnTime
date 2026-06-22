using System;
using System.Collections;
using System.IO;

namespace DiscordScheduler
{
    public class DiscordWebhookClient
    {
        private readonly LogService _log;
        private readonly WebhookUrlValidator _webhookUrlValidator;
        private readonly WebhookErrorClassifier _classifier;
        private readonly PayloadTextNormalizer _textNormalizer;
        private readonly DiscordPayloadValidator _payloadValidator;
        private readonly DiscordWebhookPayloadBuilder _payloadBuilder;
        private readonly IWebhookTransport _transport;
        private readonly WebhookRequestFactory _requestFactory;
        private readonly IWebhookSecretResolver _webhookSecretResolver;
        private readonly IFileSystem _fileSystem;

        public DiscordWebhookClient(LogService log)
            : this(log, new DiscordWebhookHttpTransport(), new WebhookRequestFactory())
        {
        }

        public DiscordWebhookClient(
            LogService log,
            IWebhookTransport transport,
            WebhookRequestFactory requestFactory = null,
            IWebhookSecretResolver webhookSecretResolver = null,
            IFileSystem fileSystem = null)
        {
            _log = log;
            _webhookUrlValidator = new WebhookUrlValidator();
            _classifier = new WebhookErrorClassifier();
            _textNormalizer = new PayloadTextNormalizer();
            _payloadValidator = new DiscordPayloadValidator(_textNormalizer);
            _payloadBuilder = new DiscordWebhookPayloadBuilder(_textNormalizer);
            _transport = transport ?? new DiscordWebhookHttpTransport();
            _requestFactory = requestFactory ?? new WebhookRequestFactory();
            _webhookSecretResolver = webhookSecretResolver ?? new WebhookSecretResolver();
            _fileSystem = fileSystem ?? new SystemFileSystem();
        }

        public IEnumerator Send(Target target, ScheduledPost post, Action<bool, string> done)
        {
            yield return Send(target, post, result =>
            {
                if (done != null)
                    done(result != null && result.ok, result?.shortError ?? "");
            });
        }

        public IEnumerator Send(Target target, ScheduledPost post, Action<WebhookSendResult> done)
        {
            if (target == null) { Complete(done, _classifier.NonRetryable("Missing target.")); yield break; }
            if (post == null) { Complete(done, _classifier.NonRetryable("Missing post.")); yield break; }

            var secretResolution = _webhookSecretResolver.Resolve(target);
            if (!secretResolution.ok) { Complete(done, _classifier.NonRetryable(secretResolution.error)); yield break; }

            var resolvedTarget = WebhookSecretResolver.CloneWithWebhookUrl(target, secretResolution.webhookUrl);
            var webhookValidation = _webhookUrlValidator.Validate(resolvedTarget.webhookUrl);
            if (!webhookValidation.ok) { Complete(done, _classifier.NonRetryable(webhookValidation.error)); yield break; }

            string mediaPath = post.EffectiveMediaPath();
            MediaKind mediaKind = post.EffectiveMediaKind();

            if (!string.IsNullOrWhiteSpace(mediaPath))
            {
                if (!_fileSystem.FileExists(mediaPath))
                {
                    Complete(done, _classifier.NonRetryable("Media file not found."));
                    yield break;
                }

                if (!_fileSystem.TryGetFileSizeBytes(mediaPath, out var size, out var sizeError))
                {
                    Complete(done, _classifier.NonRetryable(sizeError));
                    yield break;
                }

                if (size > MediaAttachmentRules.MaxAttachmentBytes)
                {
                    Complete(done, _classifier.NonRetryable(MediaAttachmentRules.AttachmentTooLargeError));
                    yield break;
                }
            }

            string embedFilename = (mediaKind == MediaKind.Image && !string.IsNullOrWhiteSpace(mediaPath))
                ? Path.GetFileName(mediaPath)
                : "";

            var payloadValidation = _payloadValidator.Validate(resolvedTarget, ToDraft(post, mediaPath));
            if (!payloadValidation.ok)
            {
                Complete(done, _classifier.NonRetryable(payloadValidation.error));
                yield break;
            }

            string payload = _payloadBuilder.Build(resolvedTarget, post, embedFilename, !string.IsNullOrWhiteSpace(mediaPath));
            var request = _requestFactory.Create(resolvedTarget, post, payload, mediaPath, mediaKind);

            var task = _transport.SendAsync(request);
            if (task == null)
            {
                Complete(done, _classifier.Ambiguous("Webhook transport returned no task."));
                yield break;
            }

            while (!task.IsCompleted) yield return null;

            if (task.IsFaulted)
            {
                Complete(done, _classifier.ClassifyException(task.Exception?.GetBaseException()));
                yield break;
            }

            Complete(done, _classifier.Classify(ToHttpResult(task.Result)));
        }

        private static void Complete(Action<WebhookSendResult> done, WebhookSendResult result)
        {
            if (done != null)
                done(result);
        }

        private static PostDraft ToDraft(ScheduledPost post, string mediaPath)
        {
            return new PostDraft
            {
                id = post.id,
                targetId = post.targetId,
                title = post.title,
                body = post.body,
                mediaPath = mediaPath ?? "",
                sendAsEmbed = post.sendAsEmbed,
                allowedMentions = post.allowedMentions
            };
        }

        private static DiscordWebhookHttp11.Result ToHttpResult(WebhookTransportResponse response)
        {
            return new DiscordWebhookHttp11.Result
            {
                ok = response.ok,
                statusCode = response.statusCode,
                body = response.body,
                retryAfterSeconds = response.retryAfterSeconds,
                retryAfterSource = response.retryAfterSource,
                rateLimitGlobal = response.rateLimitGlobal,
                rateLimitScope = response.rateLimitScope,
                requestMayHaveReachedDiscord = response.requestMayHaveReachedDiscord,
                errorKind = response.errorKind
            };
        }

    }
}
