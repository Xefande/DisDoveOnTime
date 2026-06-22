namespace DiscordScheduler
{
    public interface ISendAttemptJournal
    {
        ValidationResult Append(SendAttemptRecord record);
    }
}
