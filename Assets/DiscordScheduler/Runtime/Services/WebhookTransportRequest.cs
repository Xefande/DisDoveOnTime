namespace DiscordScheduler
{
    public sealed class WebhookTransportRequest
    {
        public readonly string webhookUrl;
        public readonly string normalizedWebhookUrl;
        public readonly string payloadJson;
        public readonly string mediaFilePath;
        public readonly string fileName;
        public readonly string fileContentType;
        public readonly string payloadPartContentType;
        public readonly string fileFieldName;
        public readonly bool hasMedia;
        public readonly bool isMultipart;

        public WebhookTransportRequest(
            string webhookUrl,
            string normalizedWebhookUrl,
            string payloadJson,
            string mediaFilePath,
            string fileName,
            string fileContentType,
            string payloadPartContentType,
            string fileFieldName,
            bool hasMedia)
        {
            this.webhookUrl = webhookUrl ?? "";
            this.normalizedWebhookUrl = normalizedWebhookUrl ?? "";
            this.payloadJson = payloadJson ?? "{}";
            this.mediaFilePath = mediaFilePath ?? "";
            this.fileName = fileName ?? "";
            this.fileContentType = fileContentType ?? "";
            this.payloadPartContentType = payloadPartContentType ?? "application/json";
            this.fileFieldName = string.IsNullOrWhiteSpace(fileFieldName) ? "files[0]" : fileFieldName;
            this.hasMedia = hasMedia;
            isMultipart = hasMedia;
        }
    }
}
