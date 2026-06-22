namespace DiscordScheduler
{
    public enum ScheduleDecisionKind
    {
        Enqueue = 0,
        MarkMissed = 1,
        MarkFailed = 2,
        Ignore = 3
    }

    public sealed class ScheduleDecision
    {
        public ScheduleDecisionKind kind;
        public string reason;

        public static ScheduleDecision Enqueue(string reason) => New(ScheduleDecisionKind.Enqueue, reason);
        public static ScheduleDecision MarkMissed(string reason) => New(ScheduleDecisionKind.MarkMissed, reason);
        public static ScheduleDecision MarkFailed(string reason) => New(ScheduleDecisionKind.MarkFailed, reason);
        public static ScheduleDecision Ignore(string reason) => New(ScheduleDecisionKind.Ignore, reason);

        private static ScheduleDecision New(ScheduleDecisionKind kind, string reason)
        {
            return new ScheduleDecision { kind = kind, reason = reason ?? "" };
        }
    }
}
