using System;
using System.IO;
using UnityEngine;

namespace DiscordScheduler
{
    public static class FileUtil
    {
        public static string DataFolder => Application.persistentDataPath;
        public static string DataFilePath => Path.Combine(Application.persistentDataPath, "discord_scheduler_db.json");
        public static string AttachmentsFolder => Path.Combine(Application.persistentDataPath, "attachments");

        public const long MaxAttachmentBytes = 10L * 1024L * 1024L;

        private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".webp", ".gif" };
        private static readonly string[] VideoExts = { ".mp4", ".webm", ".mov" };

        public static void EnsureFolders()
        {
            if (!Directory.Exists(Application.persistentDataPath))
                Directory.CreateDirectory(Application.persistentDataPath);
            if (!Directory.Exists(AttachmentsFolder))
                Directory.CreateDirectory(AttachmentsFolder);
        }

        public static bool TryCopyToAttachments(string sourcePath, string postId, out string newPath, out string error)
            => TryCopyMediaToAttachments(sourcePath, postId, MediaKind.Image, out newPath, out error);

        public static bool TryCopyMediaToAttachments(string sourcePath, string postId, MediaKind kindHint, out string newPath, out string error)
        {
            newPath = "";
            error = "";

            if (string.IsNullOrWhiteSpace(sourcePath))
                return true;

            sourcePath = sourcePath.Trim().Trim('"');

            if (!File.Exists(sourcePath))
            {
                error = "File not exists";
                return false;
            }

            var size = GetFileSizeBytes(sourcePath);
            if (size > MaxAttachmentBytes)
            {
                error = $"Attachement too large (>{MaxAttachmentBytes / (1024 * 1024)} MB). Non-Nitro limit.";
                return false;
            }

            EnsureFolders();

            var ext = (Path.GetExtension(sourcePath) ?? "").ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ext))
                ext = kindHint == MediaKind.Video ? ".mp4" : ".png";

            var kind = kindHint == MediaKind.None ? GuessKindFromExt(ext) : kindHint;

            if (kind == MediaKind.Image && !IsAllowedExt(ext, ImageExts))
            {
                error = "Only image files are allowed (png, jpg, jpeg, webp, gif).";
                return false;
            }

            if (kind == MediaKind.Video && !IsAllowedExt(ext, VideoExts))
            {
                error = "Only video files are allowed (mp4, webm, mov).";
                return false;
            }

            var unique = string.IsNullOrWhiteSpace(postId) ? Guid.NewGuid().ToString("N") : postId;
            var prefix = kind == MediaKind.Video ? "vd_" : "img_";
            var baseName = SanitizeFileName(Path.GetFileNameWithoutExtension(sourcePath));
            var fileName = $"{prefix}{baseName}_{unique}{ext}";
            newPath = Path.Combine(AttachmentsFolder, fileName);

            try
            {
                File.Copy(sourcePath, newPath, true);
                return true;
            }
            catch (Exception e)
            {
                error = "copy file unsuccessful: " + e.Message;
                return false;
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "file";
            var invalid = Path.GetInvalidFileNameChars();
            return string.Concat(name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        }

        public static MediaKind GuessKindFromExt(string extLower)
        {
            if (IsAllowedExt(extLower, VideoExts)) return MediaKind.Video;
            return MediaKind.Image;
        }

        public static bool IsAllowedImagePath(string path)
            => IsAllowedExt((Path.GetExtension(path) ?? "").ToLowerInvariant(), ImageExts);

        public static bool IsAllowedVideoPath(string path)
            => IsAllowedExt((Path.GetExtension(path) ?? "").ToLowerInvariant(), VideoExts);

        private static bool IsAllowedExt(string extLower, string[] set)
        {
            if (string.IsNullOrWhiteSpace(extLower)) return false;
            for (int i = 0; i < set.Length; i++)
                if (set[i] == extLower) return true;
            return false;
        }

        public static long GetFileSizeBytes(string path)
        {
            try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
            catch { return 0; }
        }
    }
}
