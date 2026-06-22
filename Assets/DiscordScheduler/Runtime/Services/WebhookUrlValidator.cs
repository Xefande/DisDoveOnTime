using System;

namespace DiscordScheduler
{
    public sealed class WebhookUrlValidator
    {
        public ValidationResult Validate(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return ValidationResult.Fail("Webhook URL is required.");

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
                return ValidationResult.Fail("Webhook URL is invalid.");

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return ValidationResult.Fail("Webhook URL must use https.");

            var host = uri.Host.ToLowerInvariant();
            var hostOk =
                host == "discord.com" ||
                host == "discordapp.com" ||
                host == "canary.discord.com" ||
                host == "ptb.discord.com";

            if (!hostOk)
                return ValidationResult.Fail("Webhook URL must be a Discord webhook URL.");

            if (!string.IsNullOrEmpty(uri.Fragment))
                return ValidationResult.Fail("Webhook URL must not include a fragment.");

            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length < 4 ||
                !string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(segments[1], "webhooks", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(segments[2]) ||
                string.IsNullOrWhiteSpace(segments[3]))
            {
                return ValidationResult.Fail("Webhook URL path must be a Discord webhook path.");
            }

            return ValidationResult.Ok();
        }

        public string Mask(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "";

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
                return "***";

            var scheme = string.IsNullOrWhiteSpace(uri.Scheme) ? "https" : uri.Scheme.ToLowerInvariant();
            var host = string.IsNullOrWhiteSpace(uri.Host) ? "discord.com" : uri.Host.ToLowerInvariant();
            return $"{scheme}://{host}/api/webhooks/***/***";
        }
    }
}
