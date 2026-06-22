using System;

namespace DiscordScheduler
{
    [Serializable]
    public sealed class ValidationResult
    {
        public bool ok;
        public string error;

        public static ValidationResult Ok()
        {
            return new ValidationResult { ok = true, error = "" };
        }

        public static ValidationResult Fail(string error)
        {
            return new ValidationResult { ok = false, error = error ?? "" };
        }
    }
}
