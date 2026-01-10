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
        }

        public static async Task<Result> ExecuteAsync(string webhookUrl, string payloadJson, string mediaFilePathOrNull, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl))
                return new Result { ok = false, statusCode = 0, body = "Missing webhook URL.", retryAfterSeconds = 0 };

            string url = webhookUrl.Trim();
            if (!url.Contains("?")) url += "?wait=true"; else url += "&wait=true";

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

                    using (var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false))
                    {
                        string body = "";
                        try { body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false); } catch { }

                        int code = (int)resp.StatusCode;
                        float retryAfter = code == 429 ? GetRetryAfterSeconds(resp, body) : 0;

                        return new Result
                        {
                            ok = resp.IsSuccessStatusCode,
                            statusCode = code,
                            body = body,
                            retryAfterSeconds = retryAfter
                        };
                    }
                }
            }
            catch (Exception e)
            {
                return new Result { ok = false, statusCode = 0, body = e.Message, retryAfterSeconds = 0 };
            }
        }

        private static float GetRetryAfterSeconds(HttpResponseMessage resp, string body)
        {
            try
            {
                if (resp.Headers.TryGetValues("Retry-After", out var values))
                {
                    foreach (var v in values)
                        if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                            return ClampRetry(f);
                }
            }
            catch { }

            float ra = TryParseRetryAfter(body);
            if (ra > 0) return ClampRetry(ra);

            return 2.0f;
        }

        private static float TryParseRetryAfter(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return 0;
            var key = "\"retry_after\"";
            int idx = body.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return 0;

            int colon = body.IndexOf(':', idx);
            if (colon < 0) return 0;

            int i = colon + 1;
            while (i < body.Length && (body[i] == ' ' || body[i] == '"')) i++;

            int start = i;
            while (i < body.Length && (char.IsDigit(body[i]) || body[i] == '.')) i++;

            if (i <= start) return 0;

            if (!float.TryParse(body.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return 0;

            if (v > 50f) v /= 1000f; // ms -> sec heuristic
            return v;
        }

        private static float ClampRetry(float seconds)
        {
            if (seconds < 0.5f) return 0.5f;
            if (seconds > 60f) return 60f;
            return seconds;
        }

        private static string GuessMimeType(string filename)
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
