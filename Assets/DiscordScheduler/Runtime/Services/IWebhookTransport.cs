using System.Threading;
using System.Threading.Tasks;

namespace DiscordScheduler
{
    public interface IWebhookTransport
    {
        Task<WebhookTransportResponse> SendAsync(WebhookTransportRequest request, CancellationToken ct = default);
    }
}
