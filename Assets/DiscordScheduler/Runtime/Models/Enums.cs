using System;

namespace DiscordScheduler
{
    public enum PostStatus
    {
        Pending = 0,
        Sending = 1,
        Sent = 2,
        Failed = 3,
        Missed = 4,
        NeedsReview = 5
    }

    public enum MissedPolicy
    {
        SendOnNextRun = 0,
        MarkMissed = 1,
        MarkFailed = 2
    }

    public enum MediaKind
    {
        None = 0,
        Image = 1,
        Video = 2
    }

    [Serializable]
    public class AllowedMentions
    {
        public bool allowUsers = false;
        public bool allowRoles = false;
        public bool allowEveryone = false;

        /// <summary>CSV string of Discord user IDs (optional). Only applied if allowUsers is true.</summary>
        public string userIdsCsv = "";

        /// <summary>CSV string of Discord role IDs (optional). Only applied if allowRoles is true.</summary>
        public string roleIdsCsv = "";
    }
}
