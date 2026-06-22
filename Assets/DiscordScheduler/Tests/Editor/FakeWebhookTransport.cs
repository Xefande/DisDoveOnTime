using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordScheduler.Tests
{
    public sealed class FakeWebhookTransport : IWebhookTransport
    {
        private readonly Queue<ScriptedSend> _scriptedSends = new Queue<ScriptedSend>();
        private readonly List<CapturedWebhookRequest> _requests = new List<CapturedWebhookRequest>();

        public IReadOnlyList<CapturedWebhookRequest> Requests => _requests;
        public int CallCount => _requests.Count;

        public void EnqueueResponse(
            int statusCode,
            string body = "",
            float retryAfterSeconds = 0,
            string retryAfterSource = "",
            bool rateLimitGlobal = false,
            string rateLimitScope = "",
            bool requestMayHaveReachedDiscord = true,
            string errorKind = "")
        {
            _scriptedSends.Enqueue(ScriptedSend.Response(new WebhookTransportResponse
            {
                ok = statusCode >= 200 && statusCode <= 299,
                statusCode = statusCode,
                body = body ?? "",
                retryAfterSeconds = retryAfterSeconds,
                retryAfterSource = string.IsNullOrWhiteSpace(retryAfterSource) ? WebhookRetryAfterSource.None : retryAfterSource,
                rateLimitGlobal = rateLimitGlobal,
                rateLimitScope = rateLimitScope ?? "",
                requestMayHaveReachedDiscord = requestMayHaveReachedDiscord,
                errorKind = string.IsNullOrWhiteSpace(errorKind) ? WebhookTransportErrorKind.None : errorKind
            }));
        }

        public void EnqueueRateLimit(float retryAfterSeconds, bool global = false, string scope = "")
        {
            var scopeValue = string.IsNullOrWhiteSpace(scope) && global ? "global" : (scope ?? "");
            EnqueueResponse(
                429,
                "{\"message\":\"You are being rate limited.\",\"retry_after\":" + retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"global\":" + (global ? "true" : "false") + "}",
                retryAfterSeconds,
                WebhookRetryAfterSource.Body,
                global,
                scopeValue);
        }

        public void EnqueueTimeout(string message = "A task was canceled.")
        {
            EnqueueResponse(
                0,
                message,
                0,
                WebhookRetryAfterSource.None,
                false,
                "",
                true,
                WebhookTransportErrorKind.TransportUnknown);
        }

        public void EnqueueLocalFailure(string message = "Local webhook request failed before send.")
        {
            EnqueueResponse(
                0,
                message,
                0,
                WebhookRetryAfterSource.None,
                false,
                "",
                false,
                WebhookTransportErrorKind.LocalRequestBuild);
        }

        public void EnqueueException(Exception exception)
        {
            _scriptedSends.Enqueue(ScriptedSend.Exception(exception ?? new Exception("Fake webhook transport exception.")));
        }

        public Task<WebhookTransportResponse> SendAsync(WebhookTransportRequest request, CancellationToken ct = default)
        {
            _requests.Add(CapturedWebhookRequest.From(request));

            if (_scriptedSends.Count == 0)
                throw new InvalidOperationException("Fake webhook transport has no scripted response.");

            var scripted = _scriptedSends.Dequeue();
            if (scripted.exception != null)
            {
                var tcs = new TaskCompletionSource<WebhookTransportResponse>();
                tcs.SetException(scripted.exception);
                return tcs.Task;
            }

            return Task.FromResult(scripted.response);
        }

        private sealed class ScriptedSend
        {
            public WebhookTransportResponse response;
            public Exception exception;

            public static ScriptedSend Response(WebhookTransportResponse response)
            {
                return new ScriptedSend { response = response };
            }

            public static ScriptedSend Exception(Exception exception)
            {
                return new ScriptedSend { exception = exception };
            }
        }
    }

    public sealed class CapturedWebhookRequest
    {
        public string webhookUrl;
        public string normalizedWebhookUrl;
        public string redactedWebhookUrl;
        public string payloadJson;
        public string mediaFilePath;
        public string fileName;
        public string fileContentType;
        public string payloadPartContentType;
        public string fileFieldName;
        public bool hasMedia;
        public bool isMultipart;
        public long fileLengthBytes;
        public string fileSha256;

        public static CapturedWebhookRequest From(WebhookTransportRequest request)
        {
            if (request == null)
                return new CapturedWebhookRequest();

            long length = 0;
            var hash = "";
            if (request.hasMedia && File.Exists(request.mediaFilePath))
            {
                length = new FileInfo(request.mediaFilePath).Length;
                hash = ComputeSha256(request.mediaFilePath);
            }

            return new CapturedWebhookRequest
            {
                webhookUrl = request.webhookUrl,
                normalizedWebhookUrl = request.normalizedWebhookUrl,
                redactedWebhookUrl = SecretRedactor.RedactAndTruncate(request.normalizedWebhookUrl),
                payloadJson = request.payloadJson,
                mediaFilePath = request.mediaFilePath,
                fileName = request.fileName,
                fileContentType = request.fileContentType,
                payloadPartContentType = request.payloadPartContentType,
                fileFieldName = request.fileFieldName,
                hasMedia = request.hasMedia,
                isMultipart = request.isMultipart,
                fileLengthBytes = length,
                fileSha256 = hash
            };
        }

        private static string ComputeSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                var hash = sha.ComputeHash(stream);
                return ToHex(hash);
            }
        }

        private static string ToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return "";

            var chars = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                var value = bytes[i];
                chars[i * 2] = GetHexChar(value >> 4);
                chars[i * 2 + 1] = GetHexChar(value & 0x0F);
            }

            return new string(chars);
        }

        private static char GetHexChar(int value)
        {
            return (char)(value < 10 ? '0' + value : 'a' + value - 10);
        }
    }
}
