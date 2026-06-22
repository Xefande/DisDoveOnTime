namespace DiscordScheduler
{
    public interface IPathProvider
    {
        string DataFolder { get; }
        string DataFilePath { get; }
        string AttachmentsFolder { get; }
    }
}
