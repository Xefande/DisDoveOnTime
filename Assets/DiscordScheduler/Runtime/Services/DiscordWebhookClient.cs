using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace DiscordScheduler
{
    public class DiscordWebhookClient
    {
        private readonly LogService _log;

        public DiscordWebhookClient(LogService log)
        {
            _log = log;
        }

        public IEnumerator Send(Target target, ScheduledPost post, Action<bool, string> done)
        {
            if (target == null) { done(false, "Missing target."); yield break; }
            if (post == null) { done(false, "Missing post."); yield break; }
            if (string.IsNullOrWhiteSpace(target.webhookUrl)) { done(false, "Webhook URL is empty."); yield break; }

            string mediaPath = post.EffectiveMediaPath();
            MediaKind mediaKind = post.EffectiveMediaKind();

            if (!string.IsNullOrWhiteSpace(mediaPath))
            {
                if (!File.Exists(mediaPath))
                {
                    done(false, "Media file not found.");
                    yield break;
                }

                long size = FileUtil.GetFileSizeBytes(mediaPath);
                if (size > FileUtil.MaxAttachmentBytes)
                {
                    done(false, "Attachement too large (>10 MB). Non-Nitro limit.");
                    yield break;
                }
            }

            string embedFilename = (mediaKind == MediaKind.Image && !string.IsNullOrWhiteSpace(mediaPath))
                ? Path.GetFileName(mediaPath)
                : "";

            string payload = BuildPayloadJson(target, post, embedFilename);

            const int maxAttempts = 5;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var task = DiscordWebhookHttp11.ExecuteAsync(target.webhookUrl, payload,
                    string.IsNullOrWhiteSpace(mediaPath) ? null : mediaPath);

                while (!task.IsCompleted) yield return null;

                if (task.IsFaulted)
                {
                    var msg = task.Exception?.GetBaseException().Message ?? "HTTP hiba";
                    done(false, msg);
                    yield break;
                }

                var res = task.Result;
                if (res.ok)
                {
                    done(true, "");
                    yield break;
                }

                if (res.statusCode == 429 && attempt < maxAttempts)
                {
                    float wait = Mathf.Clamp(res.retryAfterSeconds <= 0 ? 2f : res.retryAfterSeconds, 0.5f, 30f);
                    _log.Warn($"Discord 429 rate limit. Retry in {wait:0.0}s.");
                    yield return new WaitForSeconds(wait);
                    continue;
                }

                done(false, string.IsNullOrWhiteSpace(res.body) ? $"HTTP {res.statusCode}" : res.body);
                yield break;
            }

            done(false, "Nem sikerült elküldeni (túl sok retry).");
        }

        private static string BuildPayloadJson(Target target, ScheduledPost post, string embedFilename)
        {
            string title = post.title ?? string.Empty;
            string body = post.body ?? string.Empty;
            bool useEmbed = post.sendAsEmbed;

            var sb = new StringBuilder();
            sb.Append('{');

            if (!string.IsNullOrWhiteSpace(target.overrideUsername))
                sb.Append("\"username\":\"").Append(JsonUtil.Escape(target.overrideUsername)).Append("\",");

            if (!string.IsNullOrWhiteSpace(target.overrideAvatarUrl))
                sb.Append("\"avatar_url\":\"").Append(JsonUtil.Escape(target.overrideAvatarUrl)).Append("\",");

            if (post.allowedMentions != null)
                sb.Append("\"allowed_mentions\":").Append(JsonUtil.BuildAllowedMentions(post.allowedMentions)).Append(',');

            if (useEmbed)
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
                    sb.Append("\"image\":{\"url\":\"attachment://").Append(JsonUtil.Escape(embedFilename)).Append("\"}");
                }

                sb.Append("}],\"content\":\"\"}");
            }
            else
            {
                // Normal content mode
                string content = string.IsNullOrWhiteSpace(title)
                    ? body
                    : string.IsNullOrWhiteSpace(body)
                        ? title
                        : $"{title}\n{body}";

                sb.Append("\"content\":\"").Append(JsonUtil.Escape(content)).Append("\"}");
            }
            return sb.ToString();
        }
    }
}
