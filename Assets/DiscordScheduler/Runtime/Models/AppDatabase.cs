using System;
using System.Collections.Generic;

namespace DiscordScheduler
{
    [Serializable]
    public class AppSettings
    {
        public int sleepThresholdMinutes = 2;

        public AllowedMentions defaultAllowedMentions = new AllowedMentions();

        public MissedPolicy defaultOffPolicy = MissedPolicy.SendOnNextRun;
        public MissedPolicy defaultSleepPolicy = MissedPolicy.SendOnNextRun;
    }

    [Serializable]
    public class AppDatabase
    {
        public int version = 1;
        public List<Target> targets = new List<Target>();
        public List<ScheduledPost> posts = new List<ScheduledPost>();
        public AppSettings settings = new AppSettings();
    }
}
