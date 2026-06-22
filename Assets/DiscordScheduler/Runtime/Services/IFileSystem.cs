using System.Collections.Generic;

namespace DiscordScheduler
{
    public interface IFileSystem
    {
        bool FileExists(string path);
        bool DirectoryExists(string path);
        ValidationResult EnsureDirectory(string path);
        ValidationResult CopyFile(string sourcePath, string destinationPath, bool overwrite);
        ValidationResult MoveFile(string sourcePath, string destinationPath, bool overwrite);
        ValidationResult ReplaceFile(string sourcePath, string destinationPath, string backupPath);
        ValidationResult DeleteFile(string path);
        ValidationResult WriteAllText(string path, string contents);
        bool TryReadAllText(string path, out string contents, out string error);
        bool TryGetFileSizeBytes(string path, out long sizeBytes, out string error);
        IEnumerable<string> EnumerateFiles(string path);
    }
}
