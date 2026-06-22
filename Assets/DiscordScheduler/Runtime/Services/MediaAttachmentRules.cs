using System;
using System.IO;

namespace DiscordScheduler
{
    public static class MediaAttachmentRules
    {
        public const long MaxAttachmentBytes = 10L * 1024L * 1024L;

        private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".webp", ".gif" };
        private static readonly string[] VideoExts = { ".mp4", ".webm", ".mov" };

        public static string AttachmentTooLargeError => $"Attachment too large (>{MaxAttachmentBytes / (1024 * 1024)} MiB app guard).";

        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "file";

            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = string.Concat(name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
            return string.IsNullOrWhiteSpace(sanitized) ? "file" : sanitized;
        }

        public static MediaKind GuessKindFromExt(string extLower)
        {
            if (IsAllowedVideoExtension(extLower))
                return MediaKind.Video;

            return MediaKind.Image;
        }

        public static bool IsAllowedImagePath(string path)
        {
            return IsAllowedImageExtension((Path.GetExtension(path) ?? "").ToLowerInvariant());
        }

        public static bool IsAllowedVideoPath(string path)
        {
            return IsAllowedVideoExtension((Path.GetExtension(path) ?? "").ToLowerInvariant());
        }

        public static bool IsAllowedImageExtension(string extLower)
        {
            return IsAllowedExt(extLower, ImageExts);
        }

        public static bool IsAllowedVideoExtension(string extLower)
        {
            return IsAllowedExt(extLower, VideoExts);
        }

        private static bool IsAllowedExt(string extLower, string[] set)
        {
            if (string.IsNullOrWhiteSpace(extLower))
                return false;

            for (int i = 0; i < set.Length; i++)
                if (set[i] == extLower)
                    return true;

            return false;
        }
    }
}
