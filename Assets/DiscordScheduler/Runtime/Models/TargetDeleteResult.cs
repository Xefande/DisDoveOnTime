namespace DiscordScheduler
{
    public sealed class TargetDeleteResult
    {
        public string targetId;
        public int deletedPostCount;
        public int cancelledQueuedPostCount;
        public int blockedActivePostCount;
    }
}
