using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordScheduler
{
    public static class DiscordWebhookHttp11
    {
        private static readonly HttpClient _client;

        static DiscordWebhookHttp11()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            _client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        public struct Result
        {
            public bool ok;
            public int statusCode;
            public string body;
            public float retryAfterSeconds;
            public string retryAfterSource;
            public bool rateLimitGlobal;
            public string rateLimitScope;
            public bool requestMayHaveReachedDiscord;
            public string errorKind;
        }

        private struct RetryAfterInfo
        {
            public float seconds;
            public string source;
            public bool global;
            public string scope;
        }

        public static async Task<Result> ExecuteAsync(string webhookUrl, string payloadJson, string mediaFilePathOrNull, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                return new Result
                {
                    ok = false,
                    statusCode = 0,
                    body = "Missing webhook URL.",
                    retryAfterSeconds = 0,
                    retryAfterSource = WebhookRetryAfterSource.None,
                    requestMayHaveReachedDiscord = false,
                    errorKind = WebhookTransportErrorKind.LocalRequestBuild
                };
            }

            string url = NormalizeWaitTrueUrl(webhookUrl);
            var sendStarted = false;

            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    req.Version = HttpVersion.Version11;

                    if (string.IsNullOrWhiteSpace(mediaFilePathOrNull))
                    {
                        req.Content = new StringContent(payloadJson ?? "{}", Encoding.UTF8, "application/json");
                    }
                    else
                    {
                        var mp = new MultipartFormDataContent();
                        mp.Add(new StringContent(payloadJson ?? "{}", Encoding.UTF8, "application/json"), "payload_json");

                        var fileName = Path.GetFileName(mediaFilePathOrNull);
                        var fileBytes = File.ReadAllBytes(mediaFilePathOrNull);

                        var fileContent = new ByteArrayContent(fileBytes);
                        fileContent.Headers.ContentType = new MediaTypeHeaderValue(GuessMimeType(fileName));
                        mp.Add(fileContent, "files[0]", fileName);

                        req.Content = mp;
                    }

                    sendStarted = true;
                    using (var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false))
                    {
                        string body = "";
                        try { body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false); } catch { }

                        int code = (int)resp.StatusCode;
                        var retryAfter = code == 429 ? GetRetryAfterInfo(resp, body) : new RetryAfterInfo
                        {
                            seconds = 0,
                            source = WebhookRetryAfterSource.None,
                            global = false,
                            scope = ""
                        };

                        return new Result
                        {
                            ok = resp.IsSuccessStatusCode,
                            statusCode = code,
                            body = body,
                            retryAfterSeconds = retryAfter.seconds,
                            retryAfterSource = retryAfter.source,
                            rateLimitGlobal = retryAfter.global,
                            rateLimitScope = retryAfter.scope,
                            requestMayHaveReachedDiscord = true,
                            errorKind = WebhookTransportErrorKind.None
                        };
                    }
                }
            }
            catch (TaskCanceledException exception)
            {
                return new Result
                {
                    ok = false,
                    statusCode = 0,
                    body = exception.Message,
                    retryAfterSeconds = 0,
                    retryAfterSource = WebhookRetryAfterSource.None,
                    requestMayHaveReachedDiscord = sendStarted,
                    errorKind = sendStarted ? WebhookTransportErrorKind.Cancelled : WebhookTransportErrorKind.LocalRequestBuild
                };
            }
            catch (Exception exception)
            {
                return new Result
                {
                    ok = false,
                    statusCode = 0,
                    body = exception.Message,
                    retryAfterSeconds = 0,
                    retryAfterSource = WebhookRetryAfterSource.None,
                    requestMayHaveReachedDiscord = sendStarted,
                    errorKind = sendStarted ? WebhookTransportErrorKind.TransportUnknown : WebhookTransportErrorKind.LocalRequestBuild
                };
            }
        }

        public static string NormalizeWaitTrueUrl(string webhookUrl)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl))
                return "";

            var trimmed = webhookUrl.Trim();
            var fragment = "";
            var fragmentIndex = trimmed.IndexOf('#');
            if (fragmentIndex >= 0)
            {
                fragment = trimmed.Substring(fragmentIndex);
                trimmed = trimmed.Substring(0, fragmentIndex);
            }

            var baseUrl = trimmed;
            var query = "";
            var queryIndex = trimmed.IndexOf('?');
            if (queryIndex >= 0)
            {
                baseUrl = trimmed.Substring(0, queryIndex);
                query = queryIndex + 1 < trimmed.Length ? trimmed.Substring(queryIndex + 1) : "";
            }

            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(query))
            {
                var parts = query.Split('&');
                for (int i = 0; i < parts.Length; i++)
                {
                    var part = parts[i];
                    if (string.IsNullOrWhiteSpace(part))
                        continue;

                    var key = part;
                    var equals = part.IndexOf('=');
                    if (equals >= 0)
                        key = part.Substring(0, equals);

                    try { key = Uri.UnescapeDataString(key.Replace("+", " ")); } catch { }

                    if (string.Equals(key, "wait", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (sb.Length > 0)
                        sb.Append('&');
                    sb.Append(part);
                }
            }

            if (sb.Length > 0)
                sb.Append('&');
            sb.Append("wait=true");

            return baseUrl + "?" + sb + fragment;
        }

        private static float GetRetryAfterSeconds(HttpResponseMessage resp, string body)
        {
            return GetRetryAfterInfo(resp, body).seconds;
        }

        private static RetryAfterInfo GetRetryAfterInfo(HttpResponseMessage resp, string body)
        {
            var info = new RetryAfterInfo
            {
                seconds = 2.0f,
                source = WebhookRetryAfterSource.Default,
                global = IsGlobalRateLimit(resp, body),
                scope = ReadRateLimitScope(resp, body)
            };

            try
            {
                if (resp.Headers.TryGetValues("Retry-After", out var values))
                {
                    foreach (var retryAfterHeader in values)
                        if (float.TryParse(retryAfterHeader, NumberStyles.Float, CultureInfo.InvariantCulture, out var retryAfterSeconds))
                        {
                            info.seconds = ClampRetry(retryAfterSeconds);
                            info.source = WebhookRetryAfterSource.Header;
                            return NormalizeRateLimitInfo(info);
                        }
                }
            }
            catch { }

            float ra = TryParseRetryAfter(body);
            if (ra > 0)
            {
                info.seconds = ClampRetry(ra);
                info.source = WebhookRetryAfterSource.Body;
                return NormalizeRateLimitInfo(info);
            }

            return NormalizeRateLimitInfo(info);
        }

        private static float TryParseRetryAfter(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return 0;
            var key = "\"retry_after\"";
            int idx = body.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return 0;

            int colon = body.IndexOf(':', idx);
            if (colon < 0) return 0;

            int valueStart = colon + 1;
            while (valueStart < body.Length && (body[valueStart] == ' ' || body[valueStart] == '"')) valueStart++;

            int valueEnd = valueStart;
            while (valueEnd < body.Length && (char.IsDigit(body[valueEnd]) || body[valueEnd] == '.')) valueEnd++;

            if (valueEnd <= valueStart) return 0;

            if (!float.TryParse(body.Substring(valueStart, valueEnd - valueStart), NumberStyles.Float, CultureInfo.InvariantCulture, out var retryAfterSeconds))
                return 0;

            return retryAfterSeconds;
        }

        private static RetryAfterInfo NormalizeRateLimitInfo(RetryAfterInfo info)
        {
            if (string.IsNullOrWhiteSpace(info.scope) && info.global)
                info.scope = "global";

            if (string.Equals(info.scope, "global", StringComparison.OrdinalIgnoreCase))
                info.global = true;

            if (string.IsNullOrWhiteSpace(info.source))
                info.source = WebhookRetryAfterSource.Default;

            return info;
        }

        private static bool IsGlobalRateLimit(HttpResponseMessage resp, string body)
        {
            if (HeaderEquals(resp, "X-RateLimit-Global", "true"))
                return true;

            if (HeaderEquals(resp, "X-RateLimit-Scope", "global"))
                return true;

            return TryParseBooleanField(body, "global");
        }

        private static string ReadRateLimitScope(HttpResponseMessage resp, string body)
        {
            var header = ReadHeader(resp, "X-RateLimit-Scope");
            if (!string.IsNullOrWhiteSpace(header))
                return header.Trim();

            return TryParseStringField(body, "scope");
        }

        private static bool HeaderEquals(HttpResponseMessage resp, string name, string expected)
        {
            var value = ReadHeader(resp, name);
            return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadHeader(HttpResponseMessage resp, string name)
        {
            try
            {
                if (resp != null && resp.Headers.TryGetValues(name, out var values))
                    foreach (var value in values)
                        if (!string.IsNullOrWhiteSpace(value))
                            return value;
            }
            catch { }

            return "";
        }

        private static bool TryParseBooleanField(string body, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(fieldName))
                return false;

            var key = "\"" + fieldName + "\"";
            var idx = body.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return false;

            var colon = body.IndexOf(':', idx + key.Length);
            if (colon < 0)
                return false;

            var valueIndex = colon + 1;
            while (valueIndex < body.Length && char.IsWhiteSpace(body[valueIndex]))
                valueIndex++;

            if (valueIndex < body.Length && body[valueIndex] == '"')
                valueIndex++;

            return valueIndex + 4 <= body.Length &&
                   string.Compare(body, valueIndex, "true", 0, 4, StringComparison.OrdinalIgnoreCase) == 0;
        }

        private static string TryParseStringField(string body, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(fieldName))
                return "";

            var key = "\"" + fieldName + "\"";
            var idx = body.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return "";

            var colon = body.IndexOf(':', idx + key.Length);
            if (colon < 0)
                return "";

            var valueIndex = colon + 1;
            while (valueIndex < body.Length && char.IsWhiteSpace(body[valueIndex]))
                valueIndex++;

            if (valueIndex >= body.Length || body[valueIndex] != '"')
                return "";

            valueIndex++;
            var valueStart = valueIndex;
            while (valueIndex < body.Length && body[valueIndex] != '"')
                valueIndex++;

            return valueIndex > valueStart ? body.Substring(valueStart, valueIndex - valueStart).Trim() : "";
        }

        private static float ClampRetry(float seconds)
        {
            if (seconds < 0.5f) return 0.5f;
            if (seconds > 60f) return 60f;
            return seconds;
        }

        public static string GuessMimeType(string filename)
        {
            var ext = (Path.GetExtension(filename) ?? "").ToLowerInvariant();
            switch (ext)
            {
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".webp": return "image/webp";
                case ".gif": return "image/gif";
                case ".mp4": return "video/mp4";
                case ".webm": return "video/webm";
                case ".mov": return "video/quicktime";
                default: return "application/octet-stream";
            }
        }
    }
}
