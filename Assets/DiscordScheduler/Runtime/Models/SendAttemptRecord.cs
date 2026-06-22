namespace DiscordScheduler
{
    public sealed class SendAttemptRecord
    {
        public string attemptId;
        public string postId;
        public string targetId;
        public string startedAtUtcIso;
        public string finishedAtUtcIso;
        public int statusCode;
        public SendOutcomeKind outcome;
        public string discordMessageId;
        public string shortError;
    }
}
