using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace DiscordScheduler.Tests
{
    public sealed class DataLifecycleContractTests
    {
        [Test]
        public void BackupRestore_PrepareReplace_DoesNotMutateSourceCandidate()
        {
            var source = new AppDatabase { version = 1 };
            source.targets.Add(new Target { id = "t1", name = "Target", webhookUrl = "https://discord.com/api/webhooks/123456789012345678/test-token" });
            source.posts.Add(new ScheduledPost { id = "p1", targetId = "t1", imagePath = "legacy.png", mediaPath = "", mediaKind = MediaKind.None });

            var result = new BackupRestoreService().PrepareReplace(source, out var prepared, out var report);

            Assert.That(result.ok, Is.True);
            Assert.That(report.canApply, Is.True);
            Assert.That(prepared.version, Is.EqualTo(MigrationService.CurrentVersion));
            Assert.That(prepared.posts[0].mediaPath, Is.EqualTo("legacy.png"));
            Assert.That(source.version, Is.EqualTo(1));
            Assert.That(source.posts[0].mediaPath, Is.Empty);
        }

        [Test]
        public void BackupRestore_BrokenTargetReference_BlocksApplyAndReportsCount()
        {
            var source = new AppDatabase();
            source.posts.Add(new ScheduledPost { id = "p1", targetId = "missing" });

            var result = new BackupRestoreService().PrepareReplace(source, out var prepared, out var report);

            Assert.That(result.ok, Is.False);
            Assert.That(prepared, Is.Null);
            Assert.That(report.canApply, Is.False);
            Assert.That(report.brokenTargetReferenceCount, Is.EqualTo(1));
            Assert.That(report.error, Does.Contain("broken target"));
        }

        [Test]
        public void BackupRestore_DryRunFile_InvalidJson_BlocksApply()
        {
            var paths = new TestPathProvider(@"C:\disdove-restore");
            var fake = new MemoryFileSystem();
            fake.Files[@"C:\restore\broken.json"] = "{not-json";
            var service = RestoreService(paths, fake);

            var report = service.DryRunFile(@"C:\restore\broken.json");

            Assert.That(report.ok, Is.False);
            Assert.That(report.canApply, Is.False);
            Assert.That(report.error, Does.Contain("valid JSON"));
        }

        [Test]
        public void BackupRestore_ApplyFromFile_CreatesBackupAndReplacesCurrentDatabase()
        {
            var paths = new TestPathProvider(@"C:\disdove-restore");
            var fake = new MemoryFileSystem();
            var current = DatabaseWithTarget("old", "Old");
            var restore = DatabaseWithTarget("new", "New");
            fake.Files[paths.DataFilePath] = JsonUtility.ToJson(current, true);
            fake.Files[@"C:\restore\db.json"] = JsonUtility.ToJson(restore, true);
            var service = RestoreService(paths, fake);
            var dryRun = service.DryRunFile(@"C:\restore\db.json");

            var result = service.ApplyReplaceFromFile(
                current,
                @"C:\restore\db.json",
                dryRun.dryRunToken,
                () =>
                {
                    fake.Files[paths.DataFilePath] = JsonUtility.ToJson(current, true);
                    return ValidationResult.Ok();
                },
                confirmed: true);

            var expectedBackup = @"C:\disdove-restore\discord_scheduler_db.pre-restore-20260622-123000.json";
            Assert.That(result.ok, Is.True, result.error);
            Assert.That(result.backupCreated, Is.True);
            Assert.That(fake.Files.ContainsKey(expectedBackup), Is.True);
            Assert.That(fake.Files[expectedBackup], Does.Contain("\"old\""));
            Assert.That(current.targets[0].id, Is.EqualTo("new"));
            Assert.That(fake.Files[paths.DataFilePath], Does.Contain("\"new\""));
        }

        [Test]
        public void BackupRestore_ApplyFromFile_StaleDryRun_BlocksBeforeMutationOrBackup()
        {
            var paths = new TestPathProvider(@"C:\disdove-restore");
            var fake = new MemoryFileSystem();
            var current = DatabaseWithTarget("old", "Old");
            fake.Files[paths.DataFilePath] = JsonUtility.ToJson(current, true);
            fake.Files[@"C:\restore\db.json"] = JsonUtility.ToJson(DatabaseWithTarget("new", "New"), true);
            var service = RestoreService(paths, fake);
            var dryRun = service.DryRunFile(@"C:\restore\db.json");
            fake.Files[@"C:\restore\db.json"] = JsonUtility.ToJson(DatabaseWithTarget("newer", "Newer"), true);

            var result = service.ApplyReplaceFromFile(
                current,
                @"C:\restore\db.json",
                dryRun.dryRunToken,
                () => ValidationResult.Ok(),
                confirmed: true);

            Assert.That(result.ok, Is.False);
            Assert.That(result.error, Does.Contain("stale"));
            Assert.That(current.targets[0].id, Is.EqualTo("old"));
            Assert.That(fake.Files.ContainsKey(@"C:\disdove-restore\discord_scheduler_db.pre-restore-20260622-123000.json"), Is.False);
        }

        [Test]
        public void BackupRestore_ApplyFromFile_SaveFailureRollsBackCurrentDatabase()
        {
            var paths = new TestPathProvider(@"C:\disdove-restore");
            var fake = new MemoryFileSystem();
            var current = DatabaseWithTarget("old", "Old");
            fake.Files[paths.DataFilePath] = JsonUtility.ToJson(current, true);
            fake.Files[@"C:\restore\db.json"] = JsonUtility.ToJson(DatabaseWithTarget("new", "New"), true);
            var service = RestoreService(paths, fake);
            var dryRun = service.DryRunFile(@"C:\restore\db.json");
            var saveCalls = 0;

            var result = service.ApplyReplaceFromFile(
                current,
                @"C:\restore\db.json",
                dryRun.dryRunToken,
                () =>
                {
                    saveCalls++;
                    if (saveCalls == 1)
                        return ValidationResult.Fail("disk locked");

                    fake.Files[paths.DataFilePath] = JsonUtility.ToJson(current, true);
                    return ValidationResult.Ok();
                },
                confirmed: true);

            Assert.That(result.ok, Is.False);
            Assert.That(result.rollbackAttempted, Is.True);
            Assert.That(result.rollbackSucceeded, Is.True);
            Assert.That(current.targets[0].id, Is.EqualTo("old"));
            Assert.That(fake.Files[paths.DataFilePath], Does.Contain("\"old\""));
        }

        [Test]
        public void AttachmentCleanup_NeedsReviewSharedAttachment_IsProtected()
        {
            var root = CreateTempRoot();
            try
            {
                var attachments = Path.Combine(root, "attachments");
                Directory.CreateDirectory(attachments);
                var sharedPath = Path.Combine(attachments, "shared.png");
                File.WriteAllBytes(sharedPath, new byte[] { 1, 2, 3 });

                var db = new AppDatabase();
                db.posts.Add(new ScheduledPost { id = "sent", targetId = "t1", status = PostStatus.Sent, mediaPath = sharedPath, mediaKind = MediaKind.Image });
                db.posts.Add(new ScheduledPost { id = "review", targetId = "t1", status = PostStatus.NeedsReview, mediaPath = sharedPath, mediaKind = MediaKind.Image });

                var report = new AttachmentCleanupService(new AttachmentRepository(attachments)).DryRun(db);

                Assert.That(report.ok, Is.True);
                Assert.That(report.protectedActiveCount, Is.EqualTo(1));
                Assert.That(report.deleteEligibleCount, Is.Zero);
                Assert.That(report.entries[0].action, Is.EqualTo(AttachmentCleanupAction.ProtectedActive));
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void Retention_FreshDryRunApply_DeletesOnlyOrphanAttachments()
        {
            var root = CreateTempRoot();
            try
            {
                var attachments = Path.Combine(root, "attachments");
                Directory.CreateDirectory(attachments);
                var orphan = Path.Combine(attachments, "orphan.png");
                var referenced = Path.Combine(attachments, "referenced.png");
                File.WriteAllBytes(orphan, new byte[] { 1, 2, 3, 4 });
                File.WriteAllBytes(referenced, new byte[] { 5, 6, 7 });
                var db = new AppDatabase();
                db.posts.Add(new ScheduledPost { id = "p1", targetId = "t1", status = PostStatus.Pending, mediaPath = referenced, mediaKind = MediaKind.Image });
                var service = Retention(attachments);

                var dryRun = service.DryRun(db, root);
                var apply = service.Apply(db, root, dryRun, confirmed: true);

                Assert.That(dryRun.canApply, Is.True);
                Assert.That(dryRun.dryRunToken, Is.Not.Empty);
                Assert.That(apply.ok, Is.True);
                Assert.That(apply.deletedAttachmentCount, Is.EqualTo(1));
                Assert.That(File.Exists(orphan), Is.False);
                Assert.That(File.Exists(referenced), Is.True);
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void Retention_ApplyRequiresConfirmation()
        {
            var root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "attachments"));
                var service = Retention(Path.Combine(root, "attachments"));
                var dryRun = service.DryRun(new AppDatabase(), root);

                var apply = service.Apply(new AppDatabase(), root, dryRun, confirmed: false);

                Assert.That(apply.ok, Is.False);
                Assert.That(apply.error, Does.Contain("confirmation"));
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        [Test]
        public void Retention_StaleDryRunToken_BlocksApply()
        {
            var root = CreateTempRoot();
            try
            {
                var attachments = Path.Combine(root, "attachments");
                Directory.CreateDirectory(attachments);
                File.WriteAllBytes(Path.Combine(attachments, "first.png"), new byte[] { 1 });
                var service = Retention(attachments);
                var dryRun = service.DryRun(new AppDatabase(), root);
                File.WriteAllBytes(Path.Combine(attachments, "second.png"), new byte[] { 2 });

                var apply = service.Apply(new AppDatabase(), root, dryRun, confirmed: true);

                Assert.That(apply.ok, Is.False);
                Assert.That(apply.error, Does.Contain("stale"));
            }
            finally
            {
                DeleteTempRoot(root);
            }
        }

        private static RetentionPolicyService Retention(string attachmentsFolder)
        {
            var cleanup = new AttachmentCleanupService(new AttachmentRepository(attachmentsFolder));
            return new RetentionPolicyService(cleanup, () => new DateTime(2026, 6, 22, 12, 0, 0, DateTimeKind.Utc));
        }

        private static BackupRestoreService RestoreService(IPathProvider paths, IFileSystem fileSystem)
        {
            return new BackupRestoreService(paths, fileSystem, () => new DateTime(2026, 6, 22, 12, 30, 0, DateTimeKind.Utc));
        }

        private static AppDatabase DatabaseWithTarget(string targetId, string targetName)
        {
            var db = new AppDatabase();
            db.targets.Add(new Target
            {
                id = targetId,
                name = targetName,
                webhookUrl = "https://discord.com/api/webhooks/123456789012345678/token"
            });
            db.posts.Add(new ScheduledPost { id = "post-" + targetId, targetId = targetId, status = PostStatus.Pending });
            return db;
        }

        private static string CreateTempRoot()
        {
            return Path.Combine(Path.GetTempPath(), "disdove-lifecycle-tests-" + Guid.NewGuid().ToString("N"));
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
                DataFilePath = Path.Combine(root, "discord_scheduler_db.json");
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
                return MoveFile(sourcePath, destinationPath, true);
            }

            public ValidationResult DeleteFile(string path)
            {
                Files.Remove(path);
                return ValidationResult.Ok();
            }

            public ValidationResult WriteAllText(string path, string contents)
            {
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
