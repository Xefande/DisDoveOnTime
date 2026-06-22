using System;

namespace DiscordScheduler
{
    public sealed class WebhookErrorClassifier
    {
        public const int MaxShortErrorLength = 500;

        public WebhookSendResult Classify(DiscordWebhookHttp11.Result result)
        {
            var statusCode = result.statusCode;
            var body = SecretRedactor.RedactAndTruncate(result.body, MaxShortErrorLength);

            if (statusCode >= 200 && statusCode <= 299)
            {
                return new WebhookSendResult
                {
                    ok = true,
                    statusCode = statusCode,
                    outcome = SendOutcomeKind.Success,
                    shortError = "",
                    discordMessageId = ParseDiscordMessageId(result.body),
                    retryAfterSeconds = 0,
                    retryAfterSource = WebhookRetryAfterSource.None,
                    requestMayHaveReachedDiscord = result.requestMayHaveReachedDiscord,
                    errorKind = NormalizeErrorKind(result.errorKind)
                };
            }

            if (statusCode == 429)
            {
                return Fail(
                    statusCode,
                    SendOutcomeKind.RateLimited,
                    BuildHttpError(statusCode, body),
                    result.retryAfterSeconds > 0 ? result.retryAfterSeconds : 2f,
                    result.retryAfterSource,
                    result.rateLimitGlobal,
                    result.rateLimitScope,
                    result.requestMayHaveReachedDiscord,
                    result.errorKind);
            }

            if (statusCode >= 500 && statusCode <= 599)
                return Fail(statusCode, SendOutcomeKind.RetryableTransient, BuildHttpError(statusCode, body), 0);

            if (statusCode == 400 || statusCode == 401 || statusCode == 403 || statusCode == 404)
                return Fail(statusCode, SendOutcomeKind.NonRetryable, BuildHttpError(statusCode, body), 0);

            if (statusCode >= 400 && statusCode <= 499)
                return Fail(statusCode, SendOutcomeKind.NonRetryable, BuildHttpError(statusCode, body), 0);

            if (statusCode == 0)
            {
                if (string.Equals(result.errorKind, WebhookTransportErrorKind.LocalRequestBuild, StringComparison.Ordinal))
                {
                    return Fail(
                        statusCode,
                        SendOutcomeKind.NonRetryable,
                        string.IsNullOrWhiteSpace(body) ? "Local webhook request failed before send." : body,
                        0,
                        WebhookRetryAfterSource.None,
                        false,
                        "",
                        false,
                        WebhookTransportErrorKind.LocalRequestBuild);
                }

                if (ContainsIgnoreCase(body, "missing webhook"))
                    return NonRetryable(body);

                return Fail(
                    statusCode,
                    SendOutcomeKind.Ambiguous,
                    string.IsNullOrWhiteSpace(body) ? "Transport status 0." : body,
                    0,
                    WebhookRetryAfterSource.None,
                    false,
                    "",
                    true,
                    result.errorKind);
            }

            return Ambiguous(BuildHttpError(statusCode, body));
        }

        public WebhookSendResult ClassifyException(Exception exception)
        {
            var message = exception == null ? "HTTP error." : exception.Message;
            return Fail(
                0,
                SendOutcomeKind.Ambiguous,
                message,
                0,
                WebhookRetryAfterSource.None,
                false,
                "",
                true,
                WebhookTransportErrorKind.TransportUnknown);
        }

        public WebhookSendResult NonRetryable(string error, int statusCode = 0)
        {
            return Fail(
                statusCode,
                SendOutcomeKind.NonRetryable,
                NormalizeError(error),
                0,
                WebhookRetryAfterSource.None,
                false,
                "",
                false,
                WebhookTransportErrorKind.LocalRequestBuild);
        }

        public WebhookSendResult Ambiguous(string error, int statusCode = 0)
        {
            return Fail(
                statusCode,
                SendOutcomeKind.Ambiguous,
                NormalizeError(error),
                0,
                WebhookRetryAfterSource.None,
                false,
                "",
                true,
                WebhookTransportErrorKind.TransportUnknown);
        }

        public string ParseDiscordMessageId(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return "";

            var key = "\"id\"";
            var index = body.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return "";

            var colon = body.IndexOf(':', index + key.Length);
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

            if (valueIndex <= valueStart)
                return "";

            var id = body.Substring(valueStart, valueIndex - valueStart).Trim();
            if (id.Length > 32)
                return "";

            for (int digitIndex = 0; digitIndex < id.Length; digitIndex++)
                if (!char.IsDigit(id[digitIndex]))
                    return "";

            return id;
        }

        private static WebhookSendResult Fail(
            int statusCode,
            SendOutcomeKind outcome,
            string error,
            float retryAfterSeconds,
            string retryAfterSource = "",
            bool rateLimitGlobal = false,
            string rateLimitScope = "",
            bool requestMayHaveReachedDiscord = true,
            string errorKind = "")
        {
            return new WebhookSendResult
            {
                ok = false,
                statusCode = statusCode,
                outcome = outcome,
                shortError = NormalizeError(error),
                retryAfterSeconds = retryAfterSeconds,
                retryAfterSource = NormalizeRetryAfterSource(retryAfterSource),
                rateLimitGlobal = rateLimitGlobal,
                rateLimitScope = rateLimitScope ?? "",
                requestMayHaveReachedDiscord = requestMayHaveReachedDiscord,
                errorKind = NormalizeErrorKind(errorKind)
            };
        }

        private static string BuildHttpError(int statusCode, string body)
        {
            var prefix = statusCode > 0 ? "HTTP " + statusCode : "HTTP error";
            if (string.IsNullOrWhiteSpace(body))
                return prefix;

            return prefix + ": " + body;
        }

        private static string NormalizeError(string error)
        {
            var normalized = string.IsNullOrWhiteSpace(error) ? "Webhook request failed." : error;
            return SecretRedactor.RedactAndTruncate(normalized, MaxShortErrorLength);
        }

        private static bool ContainsIgnoreCase(string value, string token)
        {
            return !string.IsNullOrEmpty(value) &&
                   !string.IsNullOrEmpty(token) &&
                   value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeRetryAfterSource(string source)
        {
            return string.IsNullOrWhiteSpace(source) ? WebhookRetryAfterSource.None : source;
        }

        private static string NormalizeErrorKind(string errorKind)
        {
            return string.IsNullOrWhiteSpace(errorKind) ? WebhookTransportErrorKind.None : errorKind;
        }
    }
}
