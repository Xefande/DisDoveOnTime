using System;
using System.Collections.Generic;
using System.IO;

namespace DiscordScheduler
{
    public sealed class AttachmentFile
    {
        internal readonly string fullPath;
        public readonly string fileName;
        public readonly long sizeBytes;
        public readonly bool isReparsePoint;

        public AttachmentFile(string fullPath, string fileName, long sizeBytes, bool isReparsePoint)
        {
            this.fullPath = fullPath ?? "";
            this.fileName = fileName ?? "";
            this.sizeBytes = sizeBytes;
            this.isReparsePoint = isReparsePoint;
        }
    }

    public sealed class AttachmentRepository
    {
        private readonly string _attachmentsFolder;

        public AttachmentRepository(string attachmentsFolder)
        {
            _attachmentsFolder = attachmentsFolder ?? "";
        }

        public string AttachmentsFolder => _attachmentsFolder;

        public ValidationResult TryListFiles(out List<AttachmentFile> files)
        {
            files = new List<AttachmentFile>();

            try
            {
                if (string.IsNullOrWhiteSpace(_attachmentsFolder) || !Directory.Exists(_attachmentsFolder))
                    return ValidationResult.Ok();

                var paths = Directory.GetFiles(_attachmentsFolder, "*", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < paths.Length; i++)
                {
                    if (!TryGetManagedFullPath(paths[i], out var fullPath))
                        continue;

                    var fileName = Path.GetFileName(fullPath) ?? "";
                    files.Add(new AttachmentFile(fullPath, fileName, GetFileSizeSafe(fullPath), IsReparsePoint(fullPath)));
                }

                return ValidationResult.Ok();
            }
            catch (Exception exception)
            {
                return ValidationResult.Fail("Attachment scan failed: " + exception.GetType().Name);
            }
        }

        public ValidationResult TryDeleteManagedFile(string path, out long deletedBytes)
        {
            deletedBytes = 0;

            if (!TryGetManagedFullPath(path, out var fullPath))
                return ValidationResult.Fail("Attachment delete blocked: path is outside managed attachments.");

            if (IsReparsePoint(fullPath))
                return ValidationResult.Fail("Attachment delete blocked: reparse point.");

            try
            {
                if (!File.Exists(fullPath))
                    return ValidationResult.Ok();

                deletedBytes = GetFileSizeSafe(fullPath);
                File.Delete(fullPath);
                return ValidationResult.Ok();
            }
            catch (Exception exception)
            {
                deletedBytes = 0;
                return ValidationResult.Fail("Attachment delete failed: " + exception.GetType().Name);
            }
        }

        public bool IsManagedPath(string path)
        {
            return TryGetManagedFullPath(path, out _);
        }

        public bool TryGetManagedFullPath(string path, out string fullPath)
        {
            fullPath = "";

            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(_attachmentsFolder))
                return false;

            try
            {
                var root = Path.GetFullPath(_attachmentsFolder)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                var candidate = Path.GetFullPath(path.Trim().Trim('"'));

                if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return false;

                fullPath = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static long GetFileSizeSafe(string path)
        {
            try
            {
                return File.Exists(path) ? new FileInfo(path).Length : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return true;
            }
        }
    }
}
