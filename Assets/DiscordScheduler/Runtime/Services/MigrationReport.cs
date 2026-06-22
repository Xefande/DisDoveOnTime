using System.Collections.Generic;

namespace DiscordScheduler
{
    public sealed class MigrationReport
    {
        public int fromVersion;
        public int toVersion;
        public bool unsupportedFutureVersion;
        public readonly List<string> steps = new List<string>();

        public bool HasChanges => unsupportedFutureVersion || steps.Count > 0;
    }
}
