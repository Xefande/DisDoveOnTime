namespace DiscordScheduler
{
    public sealed class UnityPathProvider : IPathProvider
    {
        public string DataFolder => FileUtil.DataFolder;
        public string DataFilePath => FileUtil.DataFilePath;
        public string AttachmentsFolder => FileUtil.AttachmentsFolder;
    }
}
