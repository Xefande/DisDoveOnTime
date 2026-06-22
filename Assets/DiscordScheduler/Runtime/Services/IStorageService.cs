namespace DiscordScheduler
{
    public interface IStorageService
    {
        AppDatabase LoadOrCreate();
        ValidationResult TrySave(AppDatabase db);
    }
}
