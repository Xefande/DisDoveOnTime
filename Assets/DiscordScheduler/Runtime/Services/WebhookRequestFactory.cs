using System.IO;

namespace DiscordScheduler
{
    public sealed class WebhookRequestFactory
    {
        public WebhookTransportRequest Create(Target target, ScheduledPost post, string payloadJson, string mediaPath, MediaKind mediaKind)
        {
            var webhookUrl = target?.webhookUrl ?? "";
            var normalizedUrl = DiscordWebhookHttp11.NormalizeWaitTrueUrl(webhookUrl);
            var hasMedia = !string.IsNullOrWhiteSpace(mediaPath);
            var fileName = hasMedia ? Path.GetFileName(mediaPath) : "";
            var fileContentType = hasMedia ? DiscordWebhookHttp11.GuessMimeType(fileName) : "";

            return new WebhookTransportRequest(
                webhookUrl,
                normalizedUrl,
                payloadJson,
                hasMedia ? mediaPath : "",
                fileName,
                fileContentType,
                "application/json",
                "files[0]",
                hasMedia);
        }
    }
}
