namespace DiscordScheduler
{
    public sealed class PostDraft
    {
        public string id;
        public string targetId;
        public string title;
        public string body;
        public string dateYmd;
        public string timeHm;
        public string mediaPath;
        public bool sendAsEmbed;
        public AllowedMentions allowedMentions;
        public MissedPolicy missedPolicyIfOff;
        public MissedPolicy missedPolicyIfSleep;
    }
}
