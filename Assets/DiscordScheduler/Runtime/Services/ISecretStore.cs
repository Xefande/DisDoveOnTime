namespace DiscordScheduler
{
    public enum SecretStoreStatus
    {
        Available,
        Unavailable,
        PermissionDenied,
        Corrupt,
        MissingCredential
    }

    public sealed class SecretStoreResult
    {
        public bool ok;
        public SecretStoreStatus status = SecretStoreStatus.Available;
        public string key = "";
        public string error = "";

        public static SecretStoreResult Ok(string key)
        {
            return new SecretStoreResult { ok = true, key = key ?? "", error = "" };
        }

        public static SecretStoreResult Fail(SecretStoreStatus status, string error)
        {
            return new SecretStoreResult { ok = false, status = status, error = SecretRedactor.RedactAndTruncate(error) };
        }
    }

    public sealed class SecretStoreReadResult
    {
        public bool ok;
        public SecretStoreStatus status = SecretStoreStatus.Available;
        public string secret = "";
        public string error = "";

        public static SecretStoreReadResult Ok(string secret)
        {
            return new SecretStoreReadResult { ok = true, secret = secret ?? "", error = "" };
        }

        public static SecretStoreReadResult Fail(SecretStoreStatus status, string error)
        {
            return new SecretStoreReadResult { ok = false, status = status, error = SecretRedactor.RedactAndTruncate(error) };
        }
    }

    public interface ISecretStore
    {
        SecretStoreStatus Status { get; }
        SecretStoreResult Save(string key, string secret);
        SecretStoreReadResult Read(string key);
        ValidationResult Delete(string key);
    }

    public sealed class WebhookSecretResolution
    {
        public bool ok;
        public string webhookUrl = "";
        public string secretRef = "";
        public bool usedProtectedSecret;
        public SecretStoreStatus status = SecretStoreStatus.Available;
        public string error = "";

        public static WebhookSecretResolution Plaintext(string webhookUrl)
        {
            return new WebhookSecretResolution
            {
                ok = true,
                webhookUrl = (webhookUrl ?? "").Trim(),
                usedProtectedSecret = false,
                status = SecretStoreStatus.Available
            };
        }

        public static WebhookSecretResolution Protected(string webhookUrl, string secretRef)
        {
            return new WebhookSecretResolution
            {
                ok = true,
                webhookUrl = (webhookUrl ?? "").Trim(),
                secretRef = (secretRef ?? "").Trim(),
                usedProtectedSecret = true,
                status = SecretStoreStatus.Available
            };
        }

        public static WebhookSecretResolution Fail(SecretStoreStatus status, string error)
        {
            return new WebhookSecretResolution
            {
                ok = false,
                status = status,
                error = SecretRedactor.RedactAndTruncate(error ?? "Webhook secret could not be resolved.")
            };
        }
    }

    public interface IWebhookSecretResolver
    {
        WebhookSecretResolution Resolve(Target target);
    }

    public sealed class WebhookSecretResolver : IWebhookSecretResolver
    {
        private readonly ISecretStore _secretStore;

        public WebhookSecretResolver(ISecretStore secretStore = null)
        {
            _secretStore = secretStore;
        }

        public WebhookSecretResolution Resolve(Target target)
        {
            if (target == null)
                return WebhookSecretResolution.Fail(SecretStoreStatus.Unavailable, "Missing target.");

            var plaintext = (target.webhookUrl ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(plaintext))
                return WebhookSecretResolution.Plaintext(plaintext);

            var secretRef = (target.webhookSecretRef ?? "").Trim();
            if (string.IsNullOrWhiteSpace(secretRef))
                return WebhookSecretResolution.Fail(SecretStoreStatus.MissingCredential, "Target webhook is missing.");

            if (_secretStore == null)
                return WebhookSecretResolution.Fail(SecretStoreStatus.Unavailable, "Protected webhook secret cannot be resolved because no secret store is configured.");

            var status = _secretStore.Status;
            if (status != SecretStoreStatus.Available)
                return WebhookSecretResolution.Fail(status, "Protected webhook secret store is unavailable: " + status + ".");

            var read = _secretStore.Read(secretRef);
            if (!read.ok)
                return WebhookSecretResolution.Fail(read.status, "Protected webhook secret could not be read: " + read.error);

            if (string.IsNullOrWhiteSpace(read.secret))
                return WebhookSecretResolution.Fail(SecretStoreStatus.MissingCredential, "Protected webhook secret is empty.");

            return WebhookSecretResolution.Protected(read.secret, secretRef);
        }

        public static Target CloneWithWebhookUrl(Target source, string webhookUrl)
        {
            if (source == null)
                return null;

            return new Target
            {
                id = source.id,
                name = source.name,
                serverLabel = source.serverLabel,
                channelLabel = source.channelLabel,
                webhookUrl = (webhookUrl ?? "").Trim(),
                webhookSecretRef = source.webhookSecretRef,
                overrideUsername = source.overrideUsername,
                overrideAvatarUrl = source.overrideAvatarUrl
            };
        }
    }
}
