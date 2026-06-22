using System.Collections.Generic;

namespace DiscordScheduler
{
    public sealed class DatabaseNormalizationReport
    {
        public int fixedTargets;
        public int fixedPosts;
        public int fixedSettings;
        public readonly List<string> warnings = new List<string>();

        public bool HasChanges =>
            fixedTargets > 0 ||
            fixedPosts > 0 ||
            fixedSettings > 0 ||
            warnings.Count > 0;
    }
}
