using System.Collections.Generic;

namespace DiscordScheduler
{
    public sealed class SecretStorageHealthReport
    {
        public string provider = "";
        public SecretStoreStatus providerStatus = SecretStoreStatus.Unavailable;
        public bool protectedStoreAvailable;
        public bool migrationAllowed;
        public bool requiresUserSecretReentry;
        public int targetCount;
        public int legacyPlaintextCount;
        public int protectedSecretRefCount;
        public int missingCredentialCount;
        public int cannotDecryptCount;
        public string summary = "";
        public string nextAction = "";
        public string exportPolicy = "";
        public List<string> warnings = new List<string>();
    }

    public sealed class SecretMigrationReport
    {
        public bool ok = true;
        public bool canApply;
        public bool applied;
        public bool clearLegacyPlaintextRequested;
        public bool legacyClearBlocked;
        public int targetCount;
        public int legacyPlaintextCount;
        public int protectedSecretRefCount;
        public int wouldCreateRefCount;
        public int storedRefCount;
        public int verifiedReadbackCount;
        public int failedStoreCount;
        public int wouldClearPlaintextCount;
        public string provider = "";
        public SecretStoreStatus providerStatus = SecretStoreStatus.Unavailable;
        public string error = "";
        public string summary = "";
        public List<string> warnings = new List<string>();
    }
}
