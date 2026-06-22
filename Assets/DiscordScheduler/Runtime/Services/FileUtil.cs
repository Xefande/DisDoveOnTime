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

        public const long MaxAttachmentBytes = MediaAttachmentRules.MaxAttachmentBytes;
        public static string AttachmentTooLargeError => MediaAttachmentRules.AttachmentTooLargeError;

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

            if (!TryGetFileSizeBytes(sourcePath, out var size, out error))
                return false;

            if (size > MaxAttachmentBytes)
            {
                error = AttachmentTooLargeError;
                return false;
            }

            EnsureFolders();

            var ext = (Path.GetExtension(sourcePath) ?? "").ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ext))
                ext = kindHint == MediaKind.Video ? ".mp4" : ".png";

            var kind = kindHint == MediaKind.None ? MediaAttachmentRules.GuessKindFromExt(ext) : kindHint;

            if (kind == MediaKind.Image && !MediaAttachmentRules.IsAllowedImageExtension(ext))
            {
                error = "Only image files are allowed (png, jpg, jpeg, webp, gif).";
                return false;
            }

            if (kind == MediaKind.Video && !MediaAttachmentRules.IsAllowedVideoExtension(ext))
            {
                error = "Only video files are allowed (mp4, webm, mov).";
                return false;
            }

            var unique = string.IsNullOrWhiteSpace(postId) ? Guid.NewGuid().ToString("N") : postId;
            var prefix = kind == MediaKind.Video ? "vd_" : "img_";
            var baseName = MediaAttachmentRules.SanitizeFileName(Path.GetFileNameWithoutExtension(sourcePath));
            var fileName = $"{prefix}{baseName}_{unique}{ext}";
            newPath = Path.Combine(AttachmentsFolder, fileName);

            try
            {
                File.Copy(sourcePath, newPath, true);
                return true;
            }
            catch (Exception exception)
            {
                error = "copy file unsuccessful: " + exception.Message;
                return false;
            }
        }

        public static string SanitizeFileName(string name)
        {
            return MediaAttachmentRules.SanitizeFileName(name);
        }

        public static MediaKind GuessKindFromExt(string extLower)
        {
            return MediaAttachmentRules.GuessKindFromExt(extLower);
        }

        public static bool IsAllowedImagePath(string path)
            => MediaAttachmentRules.IsAllowedImagePath(path);

        public static bool IsAllowedVideoPath(string path)
            => MediaAttachmentRules.IsAllowedVideoPath(path);

        public static bool IsManagedAttachmentPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                var root = Path.GetFullPath(AttachmentsFolder)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
                return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static long GetFileSizeBytes(string path)
        {
            try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
            catch { return 0; }
        }

        public static bool TryGetFileSizeBytes(string path, out long sizeBytes, out string error)
        {
            sizeBytes = 0;
            error = "";

            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    error = "File does not exist.";
                    return false;
                }

                sizeBytes = new FileInfo(path).Length;
                return true;
            }
            catch (Exception exception)
            {
                error = "Cannot read file size: " + exception.Message;
                return false;
            }
        }
    }
}
