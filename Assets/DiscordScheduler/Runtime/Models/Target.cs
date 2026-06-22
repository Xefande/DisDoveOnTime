using System;

namespace DiscordScheduler
{
    [Serializable]
    public class Target
    {
        public string id;
        public string name;

        // purely informational (for UI)
        public string serverLabel;
        public string channelLabel;

        public string webhookUrl;
        public string webhookSecretRef;

        // webhook overrides (optional)
        public string overrideUsername;
        public string overrideAvatarUrl;

        public static Target CreateNew()
        {
            return new Target
            {
                id = Guid.NewGuid().ToString("N"),
                name = "New Target",
                serverLabel = "",
                channelLabel = "",
                webhookUrl = "",
                webhookSecretRef = "",
                overrideUsername = "",
                overrideAvatarUrl = ""
            };
        }

        public string PrettyLabel()
        {
            var server = NormalizeLabelOrUnknown(serverLabel);
            var channel = NormalizeLabelOrUnknown(channelLabel);

            return $"{name}  ({server} / {channel})";
        }

        private static string NormalizeLabelOrUnknown(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) 
            { 
                return "?";
            }

            return label.Trim();
        }
    }
}
