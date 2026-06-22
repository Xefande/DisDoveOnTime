using System;

namespace DiscordScheduler
{
    public sealed class DuePost
    {
        public ScheduledPost post;
        public DueReason reason;
        public DateTime dueUtc;
        public DateTime observedUtc;
    }
}
