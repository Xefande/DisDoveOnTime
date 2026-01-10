using System;
using System.IO;

namespace DiscordScheduler
{
    [Serializable]
    public class ScheduledPost
    {
        public string id;
        public string targetId;

        public string title;
        public string body;

        public string scheduledAtUtcIso;

        // legacy
        public string imagePath;

        public MediaKind mediaKind = MediaKind.None;
        public string mediaPath = "";

        public AllowedMentions allowedMentions = new AllowedMentions();

        public MissedPolicy missedPolicyIfOff = MissedPolicy.SendOnNextRun;
        public MissedPolicy missedPolicyIfSleep = MissedPolicy.SendOnNextRun;

        // presentation: false = normal (content), true = embed
        public bool sendAsEmbed = false;

        public PostStatus status = PostStatus.Pending;

        public string createdAtUtcIso;
        public string updatedAtUtcIso;

        public string lastError;
        public int retries;

        public static ScheduledPost CreateNew(string targetId)
        {
            var nowUtc = DateTime.UtcNow;
            return new ScheduledPost
            {
                id = Guid.NewGuid().ToString("N"),
                targetId = targetId,
                title = "",
                body = "",
                scheduledAtUtcIso = TimeUtil.ToIsoUtc(nowUtc.AddMinutes(5)),
                imagePath = "",
                mediaKind = MediaKind.None,
                mediaPath = "",
                allowedMentions = new AllowedMentions(),
                missedPolicyIfOff = MissedPolicy.SendOnNextRun,
                missedPolicyIfSleep = MissedPolicy.SendOnNextRun,
                sendAsEmbed = false,
                status = PostStatus.Pending,
                createdAtUtcIso = TimeUtil.ToIsoUtc(nowUtc),
                updatedAtUtcIso = TimeUtil.ToIsoUtc(nowUtc),
                lastError = "",
                retries = 0
            };
        }

        public DateTime ScheduledAtUtc() => TimeUtil.ParseIsoUtc(scheduledAtUtcIso);

        public void SetScheduledAtUtc(DateTime dtUtc)
        {
            scheduledAtUtcIso = TimeUtil.ToIsoUtc(dtUtc);
            Touch();
        }

        public void Touch() => updatedAtUtcIso = TimeUtil.ToIsoUtc(DateTime.UtcNow);

        public MediaKind EffectiveMediaKind()
        {
            if (mediaKind != MediaKind.None) return mediaKind;

            if (!string.IsNullOrWhiteSpace(mediaPath))
                return GuessKindFromExt(mediaPath);

            if (!string.IsNullOrWhiteSpace(imagePath))
                return MediaKind.Image;

            return MediaKind.None;
        }

        public string EffectiveMediaPath()
        {
            if (!string.IsNullOrWhiteSpace(mediaPath)) return mediaPath;
            if (!string.IsNullOrWhiteSpace(imagePath)) return imagePath;
            return "";
        }

        public void SetMedia(MediaKind kind, string path)
        {
            mediaKind = kind;
            mediaPath = path ?? "";

            if (kind == MediaKind.Image)
            {
                imagePath = mediaPath;
            }
            else if (kind == MediaKind.None || kind == MediaKind.Video)
            {
                imagePath = "";
            }
        }

        private static MediaKind GuessKindFromExt(string path)
        {
            var ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            switch (ext)
            {
                case ".mp4":
                case ".webm":
                case ".mov":
                    return MediaKind.Video;
                default:
                    return MediaKind.Image;
            }
        }
    }
}
