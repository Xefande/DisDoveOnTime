using System;
using System.IO;
using System.Text.RegularExpressions;

namespace DiscordScheduler
{
    public static class SecretRedactor
    {
        public const int DefaultMaxLength = 500;

        private static readonly Regex WebhookUrlPattern = new Regex(
            @"https?://(?:canary\.|ptb\.)?discord(?:app)?\.com/api/webhooks/[^\s""'<>]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SecretJsonFieldPattern = new Regex(
            @"(""(?:webhookUrl|webhook_url|webhook|webhookSecret|webhookToken|token|secret)""\s*:\s*"")[^""]*("")",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex KeyValueSecretPattern = new Regex(
            @"\b(webhookUrl|webhook_url|webhook|webhookSecret|webhookToken|token|secret)\s*=\s*[^\s,;]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex WindowsPathPattern = new Regex(
            @"\b[A-Za-z]:\\[^\r\n""'<>|]+",
            RegexOptions.Compiled);

        private static readonly WebhookUrlValidator WebhookUrlValidator = new WebhookUrlValidator();

        public static string Redact(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var redacted = WebhookUrlPattern.Replace(value, match => WebhookUrlValidator.Mask(match.Value));
            redacted = SecretJsonFieldPattern.Replace(redacted, match => match.Groups[1].Value + "***" + match.Groups[2].Value);
            redacted = KeyValueSecretPattern.Replace(redacted, match =>
            {
                var index = match.Value.IndexOf('=');
                return index < 0 ? "***" : match.Value.Substring(0, index + 1) + "***";
            });

            return WindowsPathPattern.Replace(redacted, match => RedactWindowsPath(match.Value));
        }

        public static string RedactAndTruncate(string value, int maxLength = DefaultMaxLength)
        {
            var redacted = Redact(value);
            if (maxLength > 0 && redacted.Length > maxLength)
                return redacted.Substring(0, maxLength) + "...";

            return redacted;
        }

        private static string RedactWindowsPath(string value)
        {
            var trimmed = (value ?? "").TrimEnd('.', ',', ';', ':', ')', ']');
            var suffix = value.Substring(trimmed.Length);
            var fileName = "";

            try
            {
                fileName = Path.GetFileName(trimmed);
            }
            catch
            {
                fileName = "";
            }

            if (string.IsNullOrWhiteSpace(fileName))
                return "[local-path]" + suffix;

            return "[local-path]/" + fileName + suffix;
        }
    }
}
