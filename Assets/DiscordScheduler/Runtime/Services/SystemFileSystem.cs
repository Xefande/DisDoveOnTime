using System;
using System.Collections.Generic;
using System.IO;

namespace DiscordScheduler
{
    public sealed class SystemFileSystem : IFileSystem
    {
        public bool FileExists(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        }

        public bool DirectoryExists(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
        }

        public ValidationResult EnsureDirectory(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return ValidationResult.Fail("Directory path is empty.");

                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                return ValidationResult.Ok();
            }
            catch (Exception exception)
            {
                return ValidationResult.Fail(exception.Message);
            }
        }

        public ValidationResult CopyFile(string sourcePath, string destinationPath, bool overwrite)
        {
            try
            {
                File.Copy(sourcePath, destinationPath, overwrite);
                return ValidationResult.Ok();
            }
            catch (Exception exception)
            {
                return ValidationResult.Fail(exception.Message);
            }
        }

        public ValidationResult MoveFile(string sourcePath, string destinationPath, bool overwrite)
        {
            try
            {
                if (overwrite && File.Exists(destinationPath))
                    File.Delete(destinationPath);

                File.Move(sourcePath, destinationPath);
                return ValidationResult.Ok();
            }
            catch (Exception exception)
            {
                return ValidationResult.Fail(exception.Message);
            }
        }

        public ValidationResult ReplaceFile(string sourcePath, string destinationPath, string backupPath)
        {
            try
            {
                File.Replace(sourcePath, destinationPath, backupPath);
                return ValidationResult.Ok();
            }
            catch (Exception exception)
            {
                return ValidationResult.Fail(exception.Message);
            }
        }

        public ValidationResult DeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);

                return ValidationResult.Ok();
            }
            catch (Exception exception)
            {
                return ValidationResult.Fail(exception.Message);
            }
        }

        public ValidationResult WriteAllText(string path, string contents)
        {
            try
            {
                File.WriteAllText(path, contents ?? string.Empty);
                return ValidationResult.Ok();
            }
            catch (Exception exception)
            {
                return ValidationResult.Fail(exception.Message);
            }
        }

        public bool TryReadAllText(string path, out string contents, out string error)
        {
            contents = "";
            error = "";

            try
            {
                contents = File.ReadAllText(path);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public bool TryGetFileSizeBytes(string path, out long sizeBytes, out string error)
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
                error = exception.Message;
                return false;
            }
        }

        public IEnumerable<string> EnumerateFiles(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return Array.Empty<string>();

            try
            {
                return Directory.EnumerateFiles(path);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }
}
