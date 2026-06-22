using System.IO;
using System.Text;

namespace DiscordScheduler
{
    public sealed class DiscordWebhookPayloadBuilder
    {
        private readonly PayloadTextNormalizer _textNormalizer;

        public DiscordWebhookPayloadBuilder(PayloadTextNormalizer textNormalizer = null)
        {
            _textNormalizer = textNormalizer ?? new PayloadTextNormalizer();
        }

        public string Build(Target target, ScheduledPost post, string embedFilename, bool hasMedia)
        {
            if (post == null)
                return Build(target, "", "", false, null, embedFilename, hasMedia);

            return Build(
                target,
                post.title,
                post.body,
                post.sendAsEmbed,
                post.allowedMentions,
                embedFilename,
                hasMedia);
        }

        public string Build(Target target, PostDraft draft, string embedFilename, bool hasMedia)
        {
            if (draft == null)
                return Build(target, "", "", false, null, embedFilename, hasMedia);

            return Build(
                target,
                draft.title,
                draft.body,
                draft.sendAsEmbed,
                draft.allowedMentions,
                embedFilename,
                hasMedia);
        }

        private string Build(
            Target target,
            string rawTitle,
            string rawBody,
            bool sendAsEmbed,
            AllowedMentions allowedMentions,
            string embedFilename,
            bool hasMedia)
        {
            string title = _textNormalizer.Normalize(rawTitle);
            string body = _textNormalizer.Normalize(rawBody);
            bool hasEmbedPayload = sendAsEmbed &&
                (!string.IsNullOrWhiteSpace(title) ||
                 !string.IsNullOrWhiteSpace(body) ||
                 !string.IsNullOrWhiteSpace(embedFilename));

            var sb = new StringBuilder();
            sb.Append('{');

            if (target != null && !string.IsNullOrWhiteSpace(target.overrideUsername))
                sb.Append("\"username\":\"").Append(JsonUtil.Escape(target.overrideUsername)).Append("\",");

            if (target != null && !string.IsNullOrWhiteSpace(target.overrideAvatarUrl))
                sb.Append("\"avatar_url\":\"").Append(JsonUtil.Escape(target.overrideAvatarUrl)).Append("\",");

            if (allowedMentions != null)
                sb.Append("\"allowed_mentions\":").Append(JsonUtil.BuildAllowedMentions(allowedMentions)).Append(',');

            if (hasEmbedPayload)
            {
                sb.Append("\"embeds\":[{");
                bool hasAny = false;
                if (!string.IsNullOrWhiteSpace(title))
                {
                    sb.Append("\"title\":\"").Append(JsonUtil.Escape(title)).Append("\"");
                    hasAny = true;
                }

                if (!string.IsNullOrWhiteSpace(body))
                {
                    if (hasAny) sb.Append(',');
                    sb.Append("\"description\":\"").Append(JsonUtil.Escape(body)).Append("\"");
                    hasAny = true;
                }

                if (!string.IsNullOrWhiteSpace(embedFilename))
                {
                    if (hasAny) sb.Append(',');
                    sb.Append("\"image\":{\"url\":\"attachment://").Append(JsonUtil.Escape(Path.GetFileName(embedFilename))).Append("\"}");
                }

                sb.Append("}],\"content\":\"\"}");
            }
            else
            {
                string content = sendAsEmbed && hasMedia ? "" : _textNormalizer.BuildNormalContent(title, body);
                sb.Append("\"content\":\"").Append(JsonUtil.Escape(content)).Append("\"}");
            }

            return sb.ToString();
        }
    }
}
