using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;

namespace DiscordScheduler.Tests
{
    public sealed class SupportBundleAndSecretStorageContractTests
    {
        private const string WebhookUrl = "https://discord.com/api/webhooks/123456/secret-token";

        [Test]
        public void SecretRedactor_RedactsWebhookJsonKeyValueAndLocalPath()
        {
            var input = "url=" + WebhookUrl +
                        " json={\"webhookUrl\":\"https://ptb.discord.com/api/webhooks/999/ptb-token\"}" +
                        " path=C:\\Users\\DevPC\\Secrets\\payload.png";

            var redacted = SecretRedactor.Redact(input);

            Assert.That(redacted, Does.Not.Contain("secret-token"));
            Assert.That(redacted, Does.Not.Contain("ptb-token"));
            Assert.That(redacted, Does.Not.Contain("C:\\Users\\DevPC\\Secrets"));
            Assert.That(redacted, Does.Contain("https://discord.com/api/webhooks/***/***"));
            Assert.That(redacted, Does.Contain("\"webhookUrl\":\"***\""));
            Assert.That(redacted, Does.Contain("[local-path]/payload.png"));
        }

        [Test]
        public void DryRun_DefaultProfile_ExcludesRawDatabaseAndMedia()
        {
            var service = new SupportBundleService(new MemoryFileSystem(), FixedNow);
            var dryRun = service.DryRun(CreateDatabase(), CreateHealth(), CreateLogs());

            Assert.That(dryRun.ok, Is.True);
            Assert.That(dryRun.rawDatabaseExcluded, Is.True);
            Assert.That(dryRun.rawMediaExcluded, Is.True);
            Assert.That(dryRun.sensitiveExcludedCount, Is.EqualTo(2));
            Assert.That(dryRun.manifest.Exists(item => item.name == "discord_scheduler_db.json" && !item.included), Is.True);
            Assert.That(dryRun.manifest.Exists(item => item.name == "attachments/*" && !item.included), Is.True);
            Assert.That(dryRun.shareSafetyVerdict, Does.Contain("Safe to share"));
        }

        [Test]
        public void Export_DefaultProfile_RedactsWebhookTokenLogsAndLocalPaths()
        {
            var fileSystem = new MemoryFileSystem();
            var service = new SupportBundleService(fileSystem, FixedNow);
            var db = CreateDatabase();
            var dryRun = service.DryRun(db, CreateHealth(), CreateLogs());

            var result = service.Export(dryRun, db, CreateHealth(), CreateLogs(), @"C:\Users\DevPC\Support\bundle", true);

            Assert.That(result.ok, Is.True);
            Assert.That(fileSystem.Files.ContainsKey(@"C:\Users\DevPC\Support\bundle\manifest.json"), Is.True);
            Assert.That(fileSystem.Files.ContainsKey(@"C:\Users\DevPC\Support\bundle\summary.md"), Is.True);
            Assert.That(fileSystem.Files.ContainsKey(@"C:\Users\DevPC\Support\bundle\diagnostics.json"), Is.True);
            Assert.That(fileSystem.Files.ContainsKey(@"C:\Users\DevPC\Support\bundle\logs.txt"), Is.True);

            var allOutput = string.Join("\n", fileSystem.Files.Values);
            Assert.That(allOutput, Does.Not.Contain("secret-token"));
            Assert.That(allOutput, Does.Not.Contain(@"C:\Users\DevPC\Secrets"));
            Assert.That(allOutput, Does.Not.Contain("\"webhookUrl\":\"" + WebhookUrl + "\""));
            Assert.That(allOutput, Does.Contain("https://discord.com/api/webhooks/***/***"));
            Assert.That(allOutput, Does.Contain("[local-path]/media.png"));
            Assert.That(result.omitted, Has.Some.Contains("discord_scheduler_db.json"));
            Assert.That(result.omitted, Has.Some.Contains("attachments/*"));
        }

        [Test]
        public void Export_WithoutConfirmation_DoesNotWrite()
        {
            var fileSystem = new MemoryFileSystem();
            var service = new SupportBundleService(fileSystem, FixedNow);
            var dryRun = service.DryRun(CreateDatabase(), CreateHealth(), CreateLogs());

            var result = service.Export(dryRun, CreateDatabase(), CreateHealth(), CreateLogs(), @"C:\bundle", false);

            Assert.That(result.ok, Is.False);
            Assert.That(result.error, Does.Contain("confirmation"));
            Assert.That(fileSystem.Files, Is.Empty);
        }

        [Test]
        public void Export_WriteFailure_ReturnsFailureAndPartialManifest()
        {
            var fileSystem = new MemoryFileSystem { FailWriteName = "diagnostics.json" };
            var service = new SupportBundleService(fileSystem, FixedNow);
            var db = CreateDatabase();
            var dryRun = service.DryRun(db, CreateHealth(), CreateLogs());

            var result = service.Export(dryRun, db, CreateHealth(), CreateLogs(), @"C:\bundle", true);

            Assert.That(result.ok, Is.False);
            Assert.That(result.error, Does.Contain("write failures"));
            Assert.That(result.partialFailures, Has.Some.Contains("diagnostics.json"));
            Assert.That(fileSystem.Files.ContainsKey(@"C:\bundle\manifest.json"), Is.True);
        }

        [Test]
        public void DryRun_RawExportOptions_AreBlocked()
        {
            var service = new SupportBundleService(new MemoryFileSystem(), FixedNow);

            var dryRun = service.DryRun(
                CreateDatabase(),
                CreateHealth(),
                CreateLogs(),
                new SupportBundleOptions { includeRawDatabase = true });

            Assert.That(dryRun.ok, Is.False);
            Assert.That(dryRun.error, Does.Contain("does not allow raw database"));
        }

        [Test]
        public void SecretHealth_LegacyPlaintextProviderUnavailable_BlocksMigrationAndKeepsSafeAction()
        {
            var report = new SecretStorageHealthService().BuildReport(
                CreateDatabase(),
                "dpapi-user-scope",
                SecretStoreStatus.Unavailable);

            Assert.That(report.legacyPlaintextCount, Is.EqualTo(1));
            Assert.That(report.protectedSecretRefCount, Is.Zero);
            Assert.That(report.migrationAllowed, Is.False);
            Assert.That(report.requiresUserSecretReentry, Is.False);
            Assert.That(report.nextAction, Does.Contain("plaintext compatibility"));
            Assert.That(report.summary, Does.Not.Contain("secret-token"));
        }

        [Test]
        public void SecretHealth_MissingCredential_RequiresUserReentry()
        {
            var report = new SecretStorageHealthService().BuildReport(
                new AppDatabase(),
                "dpapi-user-scope",
                SecretStoreStatus.MissingCredential,
                missingCredentialCount: 2);

            Assert.That(report.requiresUserSecretReentry, Is.True);
            Assert.That(report.nextAction, Does.Contain("re-enter"));
            Assert.That(report.exportPolicy, Does.Contain("never include raw webhook"));
        }

        [Test]
        public void SecretMigration_DryRunWithAvailableStore_CanApplyRefsWithoutClearingLegacy()
        {
            var report = new SecretMigrationService(new MemorySecretStore(), "memory").DryRun(CreateDatabase());

            Assert.That(report.ok, Is.True);
            Assert.That(report.canApply, Is.True);
            Assert.That(report.legacyPlaintextCount, Is.EqualTo(1));
            Assert.That(report.wouldCreateRefCount, Is.EqualTo(1));
            Assert.That(report.summary, Does.Not.Contain("secret-token"));
        }

        [Test]
        public void SecretMigration_ApplyStoresVerifiedRefAndKeepsLegacyForCompatibility()
        {
            var store = new MemorySecretStore();
            var db = CreateDatabase();

            var report = new SecretMigrationService(store, "memory").Apply(db, clearLegacyPlaintext: false, confirmed: true);

            Assert.That(report.ok, Is.True, report.error);
            Assert.That(report.applied, Is.True);
            Assert.That(report.storedRefCount, Is.EqualTo(1));
            Assert.That(report.verifiedReadbackCount, Is.EqualTo(1));
            Assert.That(db.targets[0].webhookSecretRef, Is.EqualTo("target:target-1:webhook"));
            Assert.That(db.targets[0].webhookUrl, Is.EqualTo(WebhookUrl));
            Assert.That(store.Secrets[db.targets[0].webhookSecretRef], Is.EqualTo(WebhookUrl));
        }

        [Test]
        public void SecretMigration_ClearLegacyPlaintext_IsBlockedUntilCutover()
        {
            var db = CreateDatabase();

            var report = new SecretMigrationService(new MemorySecretStore(), "memory").Apply(db, clearLegacyPlaintext: true, confirmed: true);

            Assert.That(report.ok, Is.False);
            Assert.That(report.legacyClearBlocked, Is.True);
            Assert.That(report.error, Does.Contain("blocked"));
            Assert.That(db.targets[0].webhookUrl, Is.EqualTo(WebhookUrl));
        }

        [Test]
        public void WebhookSecretResolver_ProtectedRefReadsUrlWithoutLegacyPlaintext()
        {
            var store = ProtectedStore();
            var target = ProtectedTarget();

            var result = new WebhookSecretResolver(store).Resolve(target);

            Assert.That(result.ok, Is.True, result.error);
            Assert.That(result.usedProtectedSecret, Is.True);
            Assert.That(result.webhookUrl, Is.EqualTo(WebhookUrl));
            Assert.That(target.webhookUrl, Is.Empty);
        }

        [Test]
        public void PayloadPreview_ProtectedWebhookRef_AllowsPreviewWithoutLegacyPlaintext()
        {
            var store = ProtectedStore();
            var target = ProtectedTarget();
            var service = new PayloadPreviewService(webhookSecretResolver: new WebhookSecretResolver(store));

            var preview = service.Build(target, TextDraft("Deploy", "Ready."));

            Assert.That(preview.canSend, Is.True);
            Assert.That(preview.payloadJson, Does.Contain("Deploy"));
            Assert.That(preview.payloadJson, Does.Not.Contain("secret-token"));
        }

        [Test]
        public void DiscordWebhookClient_ProtectedWebhookRef_SendsWithFakeTransportWithoutLegacyPlaintext()
        {
            var store = ProtectedStore();
            var target = ProtectedTarget();
            var fake = new FakeWebhookTransport();
            fake.EnqueueResponse(204, "");
            var client = new DiscordWebhookClient(new LogService(), fake, new WebhookRequestFactory(), new WebhookSecretResolver(store));

            var result = SendWithClient(client, target, TextPost("Deploy", "Ready."));

            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.Success));
            Assert.That(fake.Requests, Has.Count.EqualTo(1));
            Assert.That(fake.Requests[0].normalizedWebhookUrl, Does.EndWith("?wait=true"));
            Assert.That(fake.Requests[0].redactedWebhookUrl, Does.Not.Contain("secret-token"));
            Assert.That(target.webhookUrl, Is.Empty);
        }

        [Test]
        public void TargetHealth_ProtectedWebhookRef_ReportsValidWithoutRawToken()
        {
            var db = new AppDatabase();
            db.targets.Add(ProtectedTarget());

            var report = new TargetHealthService(webhookSecretResolver: new WebhookSecretResolver(ProtectedStore())).Build(db, "target-1");

            Assert.That(report.exists, Is.True);
            Assert.That(report.webhookOk, Is.True);
            Assert.That(report.maskedWebhook, Does.Not.Contain("secret-token"));
            Assert.That(report.blocker, Is.Empty);
        }

        [Test]
        public void ReviewQueue_ProtectedWebhookRef_AllowsRetryWithoutLegacyPlaintext()
        {
            var db = new AppDatabase();
            db.targets.Add(ProtectedTarget());
            db.posts.Add(new ScheduledPost
            {
                id = "review-1",
                targetId = "target-1",
                title = "Needs review",
                body = "Retry me",
                status = PostStatus.NeedsReview
            });

            var items = new ReviewQueueService(webhookSecretResolver: new WebhookSecretResolver(ProtectedStore())).Build(db, null);

            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].retryAllowed, Is.True);
            Assert.That(items[0].retryBlockedReason, Is.Empty);
        }

        [Test]
        public void QueueBurst_ProtectedWebhookRef_IsSendableWithoutLegacyPlaintext()
        {
            var now = new DateTime(2026, 6, 22, 12, 0, 0, DateTimeKind.Utc);
            var db = new AppDatabase();
            db.targets.Add(ProtectedTarget());
            db.posts.Add(new ScheduledPost
            {
                id = "post-1",
                targetId = "target-1",
                title = "Due",
                body = "Ready",
                status = PostStatus.Pending,
                scheduledAtUtcIso = TimeUtil.ToIsoUtc(now.AddMinutes(-1)),
                createdAtUtcIso = TimeUtil.ToIsoUtc(now.AddMinutes(-10))
            });

            var plan = new QueueBurstPlanner(webhookSecretResolver: new WebhookSecretResolver(ProtectedStore()))
                .Build(db, db.posts, now);

            Assert.That(plan.sendableCount, Is.EqualTo(1));
            Assert.That(plan.blockedCount, Is.Zero);
            Assert.That(plan.items[0].status, Is.EqualTo(QueueBurstItemStatus.Sendable));
        }

        private static AppDatabase CreateDatabase()
        {
            return new AppDatabase
            {
                targets = new List<Target>
                {
                    new Target
                    {
                        id = "target-1",
                        name = "Main",
                        serverLabel = "Guild",
                        channelLabel = "announcements",
                        webhookUrl = WebhookUrl
                    }
                },
                posts = new List<ScheduledPost>
                {
                    new ScheduledPost
                    {
                        id = "post-1",
                        targetId = "target-1",
                        title = "Post",
                        body = "Body",
                        status = PostStatus.NeedsReview,
                        mediaPath = @"C:\Users\DevPC\Secrets\media.png",
                        lastError = "failed " + WebhookUrl
                    }
                }
            };
        }

        private static Target ProtectedTarget()
        {
            return new Target
            {
                id = "target-1",
                name = "Main",
                serverLabel = "Guild",
                channelLabel = "announcements",
                webhookUrl = "",
                webhookSecretRef = "target:target-1:webhook"
            };
        }

        private static MemorySecretStore ProtectedStore()
        {
            var store = new MemorySecretStore();
            store.Secrets["target:target-1:webhook"] = WebhookUrl;
            return store;
        }

        private static PostDraft TextDraft(string title, string body)
        {
            return new PostDraft
            {
                id = "draft-1",
                targetId = "target-1",
                title = title,
                body = body,
                mediaPath = "",
                sendAsEmbed = false,
                allowedMentions = new AllowedMentions()
            };
        }

        private static ScheduledPost TextPost(string title, string body)
        {
            return new ScheduledPost
            {
                id = "post-1",
                targetId = "target-1",
                title = title,
                body = body,
                status = PostStatus.Pending,
                mediaKind = MediaKind.None,
                mediaPath = "",
                allowedMentions = new AllowedMentions()
            };
        }

        private static WebhookSendResult SendWithClient(DiscordWebhookClient client, Target target, ScheduledPost post)
        {
            WebhookSendResult result = null;
            IEnumerator routine = client.Send(target, post, sendResult => result = sendResult);

            var guard = 0;
            while (routine.MoveNext())
            {
                guard++;
                if (guard > 1000)
                    Assert.Fail("DiscordWebhookClient.Send did not complete within the protected-secret contract guard.");
            }

            return result;
        }

        private static HealthSnapshot CreateHealth()
        {
            return new HealthSnapshot
            {
                summary = "Webhook failed " + WebhookUrl,
                warningCount = 1,
                needsReviewCount = 1,
                activeBackoffCount = 0
            };
        }

        private static List<string> CreateLogs()
        {
            return new List<string>
            {
                "INFO Created bundle for " + WebhookUrl,
                @"ERR File C:\Users\DevPC\Secrets\media.png failed"
            };
        }

        private static DateTime FixedNow()
        {
            return new DateTime(2026, 6, 22, 12, 0, 0, DateTimeKind.Utc);
        }

        private sealed class MemoryFileSystem : IFileSystem
        {
            public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
            public string FailWriteName = "";

            public bool FileExists(string path)
            {
                return Files.ContainsKey(path);
            }

            public bool DirectoryExists(string path)
            {
                return true;
            }

            public ValidationResult EnsureDirectory(string path)
            {
                return ValidationResult.Ok();
            }

            public ValidationResult CopyFile(string sourcePath, string destinationPath, bool overwrite)
            {
                return ValidationResult.Ok();
            }

            public ValidationResult MoveFile(string sourcePath, string destinationPath, bool overwrite)
            {
                return ValidationResult.Ok();
            }

            public ValidationResult ReplaceFile(string sourcePath, string destinationPath, string backupPath)
            {
                return ValidationResult.Ok();
            }

            public ValidationResult DeleteFile(string path)
            {
                Files.Remove(path);
                return ValidationResult.Ok();
            }

            public ValidationResult WriteAllText(string path, string contents)
            {
                if (!string.IsNullOrWhiteSpace(FailWriteName) && path.EndsWith(FailWriteName, StringComparison.OrdinalIgnoreCase))
                    return ValidationResult.Fail("write denied " + WebhookUrl);

                Files[path] = contents ?? "";
                return ValidationResult.Ok();
            }

            public bool TryReadAllText(string path, out string contents, out string error)
            {
                error = "";
                if (Files.TryGetValue(path, out contents))
                    return true;

                contents = "";
                error = "missing";
                return false;
            }

            public bool TryGetFileSizeBytes(string path, out long sizeBytes, out string error)
            {
                sizeBytes = 0;
                error = "";
                return true;
            }

            public IEnumerable<string> EnumerateFiles(string path)
            {
                return Array.Empty<string>();
            }
        }

        private sealed class MemorySecretStore : ISecretStore
        {
            public readonly Dictionary<string, string> Secrets = new Dictionary<string, string>();

            public SecretStoreStatus Status { get; set; } = SecretStoreStatus.Available;

            public SecretStoreResult Save(string key, string secret)
            {
                if (Status != SecretStoreStatus.Available)
                    return SecretStoreResult.Fail(Status, "store unavailable");

                Secrets[key] = secret ?? "";
                return SecretStoreResult.Ok(key);
            }

            public SecretStoreReadResult Read(string key)
            {
                if (Status != SecretStoreStatus.Available)
                    return SecretStoreReadResult.Fail(Status, "store unavailable");

                return Secrets.TryGetValue(key, out var secret)
                    ? SecretStoreReadResult.Ok(secret)
                    : SecretStoreReadResult.Fail(SecretStoreStatus.MissingCredential, "missing");
            }

            public ValidationResult Delete(string key)
            {
                Secrets.Remove(key);
                return ValidationResult.Ok();
            }
        }
    }
}
