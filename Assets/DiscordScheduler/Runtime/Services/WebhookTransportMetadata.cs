namespace DiscordScheduler
{
    public static class WebhookTransportErrorKind
    {
        public const string None = "None";
        public const string LocalRequestBuild = "LocalRequestBuild";
        public const string TransportUnknown = "TransportUnknown";
        public const string Cancelled = "Cancelled";
        public const string NoScriptedResponse = "NoScriptedResponse";
    }

    public static class WebhookRetryAfterSource
    {
        public const string None = "None";
        public const string Header = "Header";
        public const string Body = "Body";
        public const string Default = "Default";
    }
}
