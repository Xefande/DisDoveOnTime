using System.Threading;
using System.Threading.Tasks;

namespace DiscordScheduler
{
    public sealed class DiscordWebhookHttpTransport : IWebhookTransport
    {
        public async Task<WebhookTransportResponse> SendAsync(WebhookTransportRequest request, CancellationToken ct = default)
        {
            if (request == null)
            {
                return new WebhookTransportResponse
                {
                    ok = false,
                    statusCode = 0,
                    body = "Missing webhook request.",
                    retryAfterSeconds = 0,
                    retryAfterSource = WebhookRetryAfterSource.None,
                    requestMayHaveReachedDiscord = false,
                    errorKind = WebhookTransportErrorKind.LocalRequestBuild
                };
            }

            var url = string.IsNullOrWhiteSpace(request.normalizedWebhookUrl)
                ? request.webhookUrl
                : request.normalizedWebhookUrl;
            var result = await DiscordWebhookHttp11.ExecuteAsync(
                url,
                request.payloadJson,
                request.hasMedia ? request.mediaFilePath : null,
                ct).ConfigureAwait(false);

            return new WebhookTransportResponse
            {
                ok = result.ok,
                statusCode = result.statusCode,
                body = result.body,
                retryAfterSeconds = result.retryAfterSeconds,
                retryAfterSource = result.retryAfterSource,
                rateLimitGlobal = result.rateLimitGlobal,
                rateLimitScope = result.rateLimitScope,
                requestMayHaveReachedDiscord = result.requestMayHaveReachedDiscord,
                errorKind = result.errorKind
            };
        }
    }
}
