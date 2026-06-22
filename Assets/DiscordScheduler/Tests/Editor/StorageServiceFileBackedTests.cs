using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace DiscordScheduler.Tests
{
    public sealed class StorageServiceFileBackedTests
    {
        private const string DatabaseFileName = "discord_scheduler_db.json";

        [Test]
        public void LoadOrCreate_MissingDatabase_UsesInjectedPathAndCreatesPreparedDatabase()
        {
            var root = CreateTempRoot();
            try
            {
                var paths = new TestPathProvider(root);
                var storage = CreateStorage(paths, new SystemFileSystem());

                var db = storage.LoadOrCreate();

                Assert.That(db, Is.Not.Null);
                Assert.That(db.version, Is.EqualTo(MigrationService.CurrentVersion));
                Assert.That(File.Exists(paths.DataFilePath), Is.True);
                Assert.That(Directory.Exists(paths.AttachmentsFolder), Is.True);
                Assert.That(paths.DataFilePath, Does.StartWith(root));
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void LoadOrCreate_CorruptDatabase_BacksUpBeforeOverwriteAndCreatesCleanDatabase()
        {
            var root = CreateTempRoot();
            try
            {
                var paths = new TestPathProvider(root);
                Directory.CreateDirectory(paths.DataFolder);
                File.WriteAllText(paths.DataFilePath, "{not-json");

                var fixedNow = new DateTime(2026, 6, 22, 10, 30, 0, DateTimeKind.Utc);
                var storage = CreateStorage(paths, new SystemFileSystem(), () => fixedNow);

                var db = storage.LoadOrCreate();

                var expectedBackup = Path.Combine(root, "discord_scheduler_db.corrupt-20260622-103000.json");
                Assert.That(db.version, Is.EqualTo(MigrationService.CurrentVersion));
                Assert.That(File.Exists(expectedBackup), Is.True);
                Assert.That(File.ReadAllText(expectedBackup), Is.EqualTo("{not-json"));
                Assert.That(File.ReadAllText(paths.DataFilePath), Does.Contain("\"version\": 4"));
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void LoadOrCreate_CorruptDatabaseBackupCollision_UsesNumberedBackupName()
        {
            var root = CreateTempRoot();
            try
            {
                var paths = new TestPathProvider(root);
                Directory.CreateDirectory(paths.DataFolder);
                File.WriteAllText(paths.DataFilePath, "{broken");
                File.WriteAllText(
                    Path.Combine(root, "discord_scheduler_db.corrupt-20260622-103000.json"),
                    "older backup");

                var fixedNow = new DateTime(2026, 6, 22, 10, 30, 0, DateTimeKind.Utc);
                var storage = CreateStorage(paths, new SystemFileSystem(), () => fixedNow);

                storage.LoadOrCreate();

                var expectedBackup = Path.Combine(root, "discord_scheduler_db.corrupt-20260622-103000-01.json");
                Assert.That(File.Exists(expectedBackup), Is.True);
                Assert.That(File.ReadAllText(expectedBackup), Is.EqualTo("{broken"));
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void LoadOrCreate_CorruptDatabaseWhenBackupFails_DoesNotOverwriteOriginal()
        {
            var paths = new TestPathProvider(@"C:\disdove-test");
            var fake = new MemoryFileSystem();
            fake.Files[paths.DataFilePath] = "{broken";
            fake.FailCopyFile = true;
            var storage = CreateStorage(paths, fake);

            var db = storage.LoadOrCreate();

            Assert.That(db.version, Is.EqualTo(MigrationService.CurrentVersion));
            Assert.That(fake.Files[paths.DataFilePath], Is.EqualTo("{broken"));
            Assert.That(fake.WriteAllTextCount, Is.Zero);
        }

        [Test]
        public void TrySave_TransientReplaceFailure_RetriesCleansTempAndPreservesFinalDatabase()
        {
            var paths = new TestPathProvider(@"C:\disdove-test");
            var fake = new MemoryFileSystem();
            fake.Files[paths.DataFilePath] = "{\"version\":1}";
            fake.ReplaceFailuresRemaining = 1;
            var backoffCalls = 0;
            var storage = CreateStorage(paths, fake, backoff: _ => backoffCalls++);

            var result = storage.TrySave(new AppDatabase());

            Assert.That(result.ok, Is.True);
            Assert.That(backoffCalls, Is.EqualTo(1));
            Assert.That(fake.ReplaceFileCount, Is.EqualTo(2));
            Assert.That(fake.FileExists(paths.DataFilePath + ".tmp"), Is.False);
            Assert.That(fake.Files[paths.DataFilePath], Does.Contain("\"version\""));
        }

        [Test]
        public void TrySave_PermanentReplaceFailure_RemovesTempAndKeepsExistingDatabase()
        {
            var paths = new TestPathProvider(@"C:\disdove-test");
            var fake = new MemoryFileSystem();
            fake.Files[paths.DataFilePath] = "original";
            fake.ReplaceFailuresRemaining = 3;
            var backoffCalls = 0;
            var storage = CreateStorage(paths, fake, backoff: _ => backoffCalls++);

            var result = storage.TrySave(new AppDatabase());

            Assert.That(result.ok, Is.False);
            Assert.That(result.error, Does.Contain("failed to write database after 3 attempts"));
            Assert.That(backoffCalls, Is.EqualTo(2));
            Assert.That(fake.ReplaceFileCount, Is.EqualTo(3));
            Assert.That(fake.FileExists(paths.DataFilePath + ".tmp"), Is.False);
            Assert.That(fake.Files[paths.DataFilePath], Is.EqualTo("original"));
        }

        private static StorageService CreateStorage(
            IPathProvider paths,
            IFileSystem fileSystem,
            Func<DateTime> utcNow = null,
            Action<int> backoff = null)
        {
            return new StorageService(new LogService(), paths, fileSystem, utcNow, backoff ?? (_ => { }));
        }

        private static string CreateTempRoot()
        {
            return Path.Combine(Path.GetTempPath(), "disdove-storage-tests-" + Guid.NewGuid().ToString("N"));
        }

        private static void DeleteTempRoot(string root)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
            }
        }

        private sealed class TestPathProvider : IPathProvider
        {
            public TestPathProvider(string root)
            {
                DataFolder = root;
                DataFilePath = Path.Combine(root, DatabaseFileName);
                AttachmentsFolder = Path.Combine(root, "attachments");
            }

            public string DataFolder { get; }
            public string DataFilePath { get; }
            public string AttachmentsFolder { get; }
        }

        private sealed class MemoryFileSystem : IFileSystem
        {
            public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
            public readonly HashSet<string> Directories = new HashSet<string>();

            public bool FailCopyFile;
            public int ReplaceFailuresRemaining;
            public int ReplaceFileCount;
            public int WriteAllTextCount;

            public bool FileExists(string path)
            {
                return Files.ContainsKey(path);
            }

            public bool DirectoryExists(string path)
            {
                return Directories.Contains(path);
            }

            public ValidationResult EnsureDirectory(string path)
            {
                Directories.Add(path);
                return ValidationResult.Ok();
            }

            public ValidationResult CopyFile(string sourcePath, string destinationPath, bool overwrite)
            {
                if (FailCopyFile)
                    return ValidationResult.Fail("copy denied");

                if (!Files.TryGetValue(sourcePath, out var contents))
                    return ValidationResult.Fail("source missing");

                if (Files.ContainsKey(destinationPath) && !overwrite)
                    return ValidationResult.Fail("destination exists");

                Files[destinationPath] = contents;
                return ValidationResult.Ok();
            }

            public ValidationResult MoveFile(string sourcePath, string destinationPath, bool overwrite)
            {
                if (!Files.TryGetValue(sourcePath, out var contents))
                    return ValidationResult.Fail("source missing");

                if (Files.ContainsKey(destinationPath) && !overwrite)
                    return ValidationResult.Fail("destination exists");

                Files[destinationPath] = contents;
                Files.Remove(sourcePath);
                return ValidationResult.Ok();
            }

            public ValidationResult ReplaceFile(string sourcePath, string destinationPath, string backupPath)
            {
                ReplaceFileCount++;

                if (ReplaceFailuresRemaining > 0)
                {
                    ReplaceFailuresRemaining--;
                    return ValidationResult.Fail("locked");
                }

                if (!Files.TryGetValue(sourcePath, out var contents))
                    return ValidationResult.Fail("source missing");

                if (!Files.ContainsKey(destinationPath))
                    return ValidationResult.Fail("destination missing");

                Files[destinationPath] = contents;
                Files.Remove(sourcePath);
                return ValidationResult.Ok();
            }

            public ValidationResult DeleteFile(string path)
            {
                Files.Remove(path);
                return ValidationResult.Ok();
            }

            public ValidationResult WriteAllText(string path, string contents)
            {
                WriteAllTextCount++;
                Files[path] = contents ?? "";
                return ValidationResult.Ok();
            }

            public bool TryReadAllText(string path, out string contents, out string error)
            {
                error = "";
                if (Files.TryGetValue(path, out contents))
                    return true;

                contents = "";
                error = "file missing";
                return false;
            }

            public bool TryGetFileSizeBytes(string path, out long sizeBytes, out string error)
            {
                error = "";
                if (!Files.TryGetValue(path, out var contents))
                {
                    sizeBytes = 0;
                    error = "file missing";
                    return false;
                }

                sizeBytes = contents.Length;
                return true;
            }

            public IEnumerable<string> EnumerateFiles(string path)
            {
                return Array.Empty<string>();
            }
        }
    }
}
