using System;
using System.IO;

namespace DiscordScheduler
{
    public sealed class PostMediaService
    {
        private readonly IPathProvider _pathProvider;
        private readonly IFileSystem _fileSystem;

        public PostMediaService()
            : this(new UnityPathProvider(), new SystemFileSystem())
        {
        }

        public PostMediaService(IPathProvider pathProvider, IFileSystem fileSystem)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public ValidationResult TryPrepareMedia(string sourcePath, string postId, out MediaKind mediaKind, out string storedPath)
        {
            return TryPrepareMedia(sourcePath, postId, MediaKind.None, out mediaKind, out storedPath);
        }

        public ValidationResult TryPrepareMedia(string sourcePath, string postId, MediaKind kindHint, out MediaKind mediaKind, out string storedPath)
        {
            mediaKind = MediaKind.None;
            storedPath = "";

            sourcePath = (sourcePath ?? "").Trim().Trim('"');
            if (string.IsNullOrEmpty(sourcePath))
                return ValidationResult.Ok();

            if (IsManagedAttachmentPath(sourcePath))
            {
                mediaKind = ResolveKind(sourcePath, kindHint);
                storedPath = sourcePath;
                return ValidatePrepared(mediaKind, storedPath);
            }

            return TryCopyMediaToAttachments(sourcePath, postId, kindHint, out mediaKind, out storedPath);
        }

        public bool IsManagedAttachmentPath(string path)
        {
            return IsPathUnderFolder(path, _pathProvider.AttachmentsFolder);
        }

        private ValidationResult TryCopyMediaToAttachments(
            string sourcePath,
            string postId,
            MediaKind kindHint,
            out MediaKind mediaKind,
            out string storedPath)
        {
            mediaKind = MediaKind.None;
            storedPath = "";

            if (!_fileSystem.FileExists(sourcePath))
                return ValidationResult.Fail("File not exists");

            if (!_fileSystem.TryGetFileSizeBytes(sourcePath, out var size, out var sizeError))
                return ValidationResult.Fail(sizeError);

            if (size > MediaAttachmentRules.MaxAttachmentBytes)
                return ValidationResult.Fail(MediaAttachmentRules.AttachmentTooLargeError);

            var ensureResult = _fileSystem.EnsureDirectory(_pathProvider.AttachmentsFolder);
            if (!ensureResult.ok)
                return ValidationResult.Fail("Cannot prepare attachment folder: " + ensureResult.error);

            var ext = (Path.GetExtension(sourcePath) ?? "").ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ext))
                ext = kindHint == MediaKind.Video ? ".mp4" : ".png";

            var kind = kindHint == MediaKind.None ? MediaAttachmentRules.GuessKindFromExt(ext) : kindHint;
            var extensionResult = ValidateExtension(kind, ext);
            if (!extensionResult.ok)
                return extensionResult;

            var unique = string.IsNullOrWhiteSpace(postId) ? Guid.NewGuid().ToString("N") : postId;
            var prefix = kind == MediaKind.Video ? "vd_" : "img_";
            var baseName = MediaAttachmentRules.SanitizeFileName(Path.GetFileNameWithoutExtension(sourcePath));
            storedPath = Path.Combine(_pathProvider.AttachmentsFolder, $"{prefix}{baseName}_{unique}{ext}");

            var copyResult = _fileSystem.CopyFile(sourcePath, storedPath, true);
            if (!copyResult.ok)
                return ValidationResult.Fail("copy file unsuccessful: " + copyResult.error);

            mediaKind = kind;
            return ValidatePrepared(mediaKind, storedPath);
        }

        private static MediaKind ResolveKind(string path, MediaKind kindHint)
        {
            if (kindHint != MediaKind.None)
                return kindHint;

            if (MediaAttachmentRules.IsAllowedVideoPath(path))
                return MediaKind.Video;
            if (MediaAttachmentRules.IsAllowedImagePath(path))
                return MediaKind.Image;
            return MediaKind.None;
        }

        private ValidationResult ValidatePrepared(MediaKind kind, string path)
        {
            if (kind == MediaKind.None)
                return ValidationResult.Fail("Unsupported media type.");

            if (!_fileSystem.TryGetFileSizeBytes(path, out var size, out var sizeError))
                return ValidationResult.Fail(sizeError);

            if (size > MediaAttachmentRules.MaxAttachmentBytes)
                return ValidationResult.Fail(MediaAttachmentRules.AttachmentTooLargeError);

            return ValidationResult.Ok();
        }

        private static ValidationResult ValidateExtension(MediaKind kind, string ext)
        {
            if (kind == MediaKind.Image && !MediaAttachmentRules.IsAllowedImageExtension(ext))
                return ValidationResult.Fail("Only image files are allowed (png, jpg, jpeg, webp, gif).");

            if (kind == MediaKind.Video && !MediaAttachmentRules.IsAllowedVideoExtension(ext))
                return ValidationResult.Fail("Only video files are allowed (mp4, webm, mov).");

            if (kind == MediaKind.None)
                return ValidationResult.Fail("Unsupported media type.");

            return ValidationResult.Ok();
        }

        private static bool IsPathUnderFolder(string path, string rootFolder)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(rootFolder))
                return false;

            try
            {
                var root = Path.GetFullPath(rootFolder)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
                return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
