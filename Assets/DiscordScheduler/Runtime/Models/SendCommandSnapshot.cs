namespace DiscordScheduler
{
    public sealed class SendCommandSnapshot
    {
        public string attemptId;
        public string postId;
        public string targetId;
        public string targetRevision;
        public string payloadFingerprint;
        public string startedAtUtcIso;
    }
}
