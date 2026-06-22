namespace DiscordScheduler
{
    public sealed class SchedulePolicyService
    {
        public ScheduleDecision Decide(DuePost duePost)
        {
            if (duePost == null || duePost.post == null)
                return ScheduleDecision.Ignore("Missing due post.");

            switch (duePost.reason)
            {
                case DueReason.NormalDue:
                    return ScheduleDecision.Enqueue("Normal due.");
                case DueReason.AppWasOff:
                    return DecideByPolicy(duePost.post.missedPolicyIfOff, "App was off.");
                case DueReason.SleepGap:
                    return DecideByPolicy(duePost.post.missedPolicyIfSleep, "Sleep gap.");
                default:
                    return ScheduleDecision.Ignore("Unknown due reason.");
            }
        }

        private static ScheduleDecision DecideByPolicy(MissedPolicy policy, string reason)
        {
            switch (policy)
            {
                case MissedPolicy.SendOnNextRun:
                    return ScheduleDecision.Enqueue(reason);
                case MissedPolicy.MarkMissed:
                    return ScheduleDecision.MarkMissed(reason);
                case MissedPolicy.MarkFailed:
                    return ScheduleDecision.MarkFailed(reason);
                default:
                    return ScheduleDecision.Enqueue(reason);
            }
        }
    }
}
