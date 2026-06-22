namespace DiscordScheduler
{
    public sealed class LockAcquireResult
    {
        public bool acquired;
        public bool readOnly;
        public bool staleRecovered;
        public string lockPath;
        public string message;

        public static LockAcquireResult Acquired(string lockPath, bool staleRecovered)
        {
            return new LockAcquireResult
            {
                acquired = true,
                readOnly = false,
                staleRecovered = staleRecovered,
                lockPath = lockPath ?? "",
                message = staleRecovered ? "Recovered stale storage lock." : ""
            };
        }

        public static LockAcquireResult Blocked(string lockPath, string message)
        {
            return new LockAcquireResult
            {
                acquired = false,
                readOnly = true,
                staleRecovered = false,
                lockPath = lockPath ?? "",
                message = message ?? "Storage is locked by another app instance."
            };
        }
    }
}
