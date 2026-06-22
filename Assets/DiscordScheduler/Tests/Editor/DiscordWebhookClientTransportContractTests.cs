using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;

namespace DiscordScheduler.Tests
{
    public sealed class DiscordWebhookClientTransportContractTests
    {
        [Test]
        public void Client_JsonOnly_CapturesPayloadWithoutRealDiscord()
        {
            var fake = new FakeWebhookTransport();
            fake.EnqueueResponse(200, "{\"id\":\"123456789012345678\",\"content\":\"ok\"}");

            var result = SendWithFake(fake, ValidTarget(), TextPost("Deployment", "Ready."));

            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.Success));
            Assert.That(result.discordMessageId, Is.EqualTo("123456789012345678"));
            Assert.That(fake.Requests, Has.Count.EqualTo(1));

            var request = fake.Requests[0];
            Assert.That(request.normalizedWebhookUrl, Does.EndWith("?wait=true"));
            Assert.That(request.redactedWebhookUrl, Does.Not.Contain("test-token"));
            Assert.That(request.isMultipart, Is.False);
            Assert.That(request.hasMedia, Is.False);
            Assert.That(request.payloadJson, Does.Contain("\"content\":\"Deployment"));
            Assert.That(request.payloadJson, Does.Contain("Ready."));
        }

        [Test]
        public void Client_ImageEmbed_CapturesMultipartPayloadJsonAndFile()
        {
            var imageBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
            var imagePath = CreateTempFile(".png", imageBytes);
            try
            {
                var fake = new FakeWebhookTransport();
                fake.EnqueueResponse(200, "{\"id\":\"223456789012345678\"}");

                var result = SendWithFake(fake, ValidTarget(), new ScheduledPost
                {
                    id = "post-image",
                    targetId = "target-1",
                    title = "",
                    body = "",
                    sendAsEmbed = true,
                    mediaKind = MediaKind.Image,
                    mediaPath = imagePath,
                    allowedMentions = new AllowedMentions()
                });

                Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.Success));
                Assert.That(fake.Requests, Has.Count.EqualTo(1));

                var request = fake.Requests[0];
                Assert.That(request.isMultipart, Is.True);
                Assert.That(request.hasMedia, Is.True);
                Assert.That(request.fileName, Is.EqualTo(Path.GetFileName(imagePath)));
                Assert.That(request.fileFieldName, Is.EqualTo("files[0]"));
                Assert.That(request.fileContentType, Is.EqualTo("image/png"));
                Assert.That(request.payloadPartContentType, Is.EqualTo("application/json"));
                Assert.That(request.fileLengthBytes, Is.EqualTo(8));
                Assert.That(request.fileSha256, Is.EqualTo(Sha256Hex(imageBytes)));
                Assert.That(request.payloadJson, Does.Contain("\"embeds\""));
                Assert.That(request.payloadJson, Does.Contain("attachment://" + Path.GetFileName(imagePath)));
            }
            finally
            {
                TryDelete(imagePath);
            }
        }

        [Test]
        public void Client_VideoOnlyEmbed_CapturesMultipartWithoutEmptyEmbed()
        {
            var videoPath = CreateTempFile(".mp4", new byte[] { 0, 0, 0, 24, 102, 116, 121, 112 });
            try
            {
                var fake = new FakeWebhookTransport();
                fake.EnqueueResponse(204, "");

                var result = SendWithFake(fake, ValidTarget(), new ScheduledPost
                {
                    id = "post-video",
                    targetId = "target-1",
                    title = "",
                    body = "",
                    sendAsEmbed = true,
                    mediaKind = MediaKind.Video,
                    mediaPath = videoPath,
                    allowedMentions = new AllowedMentions()
                });

                Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.Success));
                Assert.That(result.discordMessageId, Is.Empty);
                Assert.That(fake.Requests, Has.Count.EqualTo(1));

                var request = fake.Requests[0];
                Assert.That(request.isMultipart, Is.True);
                Assert.That(request.fileName, Is.EqualTo(Path.GetFileName(videoPath)));
                Assert.That(request.fileContentType, Is.EqualTo("video/mp4"));
                Assert.That(request.payloadJson, Does.Not.Contain("\"embeds\""));
                Assert.That(request.payloadJson, Does.Contain("\"content\":\"\""));
            }
            finally
            {
                TryDelete(videoPath);
            }
        }

        [Test]
        public void Client_ExistingQuery_PreservesThreadIdAndAddsWaitTrue()
        {
            var fake = new FakeWebhookTransport();
            fake.EnqueueResponse(204, "");

            var target = ValidTarget();
            target.webhookUrl += "?thread_id=987654321098765432&wait=false&with_components=true";

            var result = SendWithFake(fake, target, TextPost("Thread", "Preserve query."));

            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.Success));
            Assert.That(fake.Requests, Has.Count.EqualTo(1));

            var url = fake.Requests[0].normalizedWebhookUrl;
            Assert.That(url, Does.Contain("thread_id=987654321098765432"));
            Assert.That(url, Does.Contain("with_components=true"));
            Assert.That(url, Does.Contain("wait=true"));
            Assert.That(url, Does.Not.Contain("wait=false"));
        }

        [Test]
        public void Client_RateLimit_ClassifiesRetryAfterWithoutNetwork()
        {
            var fake = new FakeWebhookTransport();
            fake.EnqueueRateLimit(3.25f);

            var result = SendWithFake(fake, ValidTarget(), TextPost("Later", "Rate limited."));

            Assert.That(result.ok, Is.False);
            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.RateLimited));
            Assert.That(result.retryAfterSeconds, Is.EqualTo(3.25f));
            Assert.That(result.retryAfterSource, Is.EqualTo(WebhookRetryAfterSource.Body));
            Assert.That(fake.CallCount, Is.EqualTo(1));
        }

        [Test]
        public void Client_RateLimitGlobalScope_PreservesMetadataWithoutNetwork()
        {
            var fake = new FakeWebhookTransport();
            fake.EnqueueRateLimit(7.5f, global: true, scope: "global");

            var result = SendWithFake(fake, ValidTarget(), TextPost("Global", "Rate limited globally."));

            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.RateLimited));
            Assert.That(result.retryAfterSeconds, Is.EqualTo(7.5f));
            Assert.That(result.retryAfterSource, Is.EqualTo(WebhookRetryAfterSource.Body));
            Assert.That(result.rateLimitGlobal, Is.True);
            Assert.That(result.rateLimitScope, Is.EqualTo("global"));
        }

        [Test]
        public void Client_ServerError_ClassifiesRetryableTransientWithoutNetwork()
        {
            var fake = new FakeWebhookTransport();
            fake.EnqueueResponse(503, "Service unavailable");

            var result = SendWithFake(fake, ValidTarget(), TextPost("Retry", "Server is unavailable."));

            Assert.That(result.ok, Is.False);
            Assert.That(result.statusCode, Is.EqualTo(503));
            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.RetryableTransient));
            Assert.That(fake.CallCount, Is.EqualTo(1));
        }

        [Test]
        public void Client_TimeoutStatusZero_ClassifiesAmbiguousWithoutNetwork()
        {
            var fake = new FakeWebhookTransport();
            fake.EnqueueTimeout();

            var result = SendWithFake(fake, ValidTarget(), TextPost("Timeout", "Transport timeout."));

            Assert.That(result.ok, Is.False);
            Assert.That(result.statusCode, Is.EqualTo(0));
            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.Ambiguous));
            Assert.That(fake.CallCount, Is.EqualTo(1));
        }

        [Test]
        public void Client_NoQueuedFakeResponse_FailsFastWithoutNetworkFallback()
        {
            var fake = new FakeWebhookTransport();

            Assert.Throws<InvalidOperationException>(() =>
                SendWithFake(fake, ValidTarget(), TextPost("No script", "This must not fall back to HTTP.")));

            Assert.That(fake.CallCount, Is.EqualTo(1));
        }

        [Test]
        public void Client_MissingMedia_DoesNotCallTransport()
        {
            var fake = new FakeWebhookTransport();

            var result = SendWithFake(fake, ValidTarget(), new ScheduledPost
            {
                id = "post-missing-media",
                targetId = "target-1",
                title = "Missing file",
                body = "",
                sendAsEmbed = true,
                mediaKind = MediaKind.Image,
                mediaPath = Path.Combine(Path.GetTempPath(), "disdove-missing-" + Guid.NewGuid().ToString("N") + ".png"),
                allowedMentions = new AllowedMentions()
            });

            Assert.That(result.ok, Is.False);
            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.NonRetryable));
            Assert.That(result.shortError, Does.Contain("Media file not found"));
            Assert.That(fake.CallCount, Is.EqualTo(0));
        }

        [Test]
        public void Client_MediaPreflightUsesInjectedFileSystemBeforeTransport()
        {
            var fake = new FakeWebhookTransport();
            var fileSystem = new BlockingFileSystem();
            var client = new DiscordWebhookClient(new LogService(), fake, new WebhookRequestFactory(), fileSystem: fileSystem);
            var post = TextPost("Blocked", "Media is blocked by the test file system.");
            post.mediaKind = MediaKind.Image;
            post.mediaPath = @"C:\virtual-media\image.png";

            WebhookSendResult result = null;
            IEnumerator routine = client.Send(ValidTarget(), post, sendResult => result = sendResult);
            while (routine.MoveNext())
            {
            }

            Assert.That(result, Is.Not.Null);
            Assert.That(result.ok, Is.False);
            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.NonRetryable));
            Assert.That(result.shortError, Does.Contain("Media file not found"));
            Assert.That(fake.CallCount, Is.Zero);
            Assert.That(fileSystem.FileExistsCalls, Is.EqualTo(1));
        }

        [Test]
        public void Client_LocalFailureBeforeSend_ClassifiesNonRetryable()
        {
            var fake = new FakeWebhookTransport();
            fake.EnqueueLocalFailure("Local file read failed before send.");

            var result = SendWithFake(fake, ValidTarget(), TextPost("Local", "Pre-send failure."));

            Assert.That(result.ok, Is.False);
            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.NonRetryable));
            Assert.That(result.requestMayHaveReachedDiscord, Is.False);
            Assert.That(result.errorKind, Is.EqualTo(WebhookTransportErrorKind.LocalRequestBuild));
            Assert.That(result.shortError, Does.Contain("Local file read failed"));
        }

        [Test]
        public void Client_TwoHundredMalformedBody_ClassifiesSuccessWithoutMessageId()
        {
            var fake = new FakeWebhookTransport();
            fake.EnqueueResponse(200, "{not-json");

            var result = SendWithFake(fake, ValidTarget(), TextPost("Malformed", "Body is malformed."));

            Assert.That(result.ok, Is.True);
            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.Success));
            Assert.That(result.discordMessageId, Is.Empty);
            Assert.That(fake.CallCount, Is.EqualTo(1));
        }

        [Test]
        public void Client_ForumMediaMissingThreadBadRequest_ClassifiesNonRetryable()
        {
            var fake = new FakeWebhookTransport();
            fake.EnqueueResponse(400, "{\"message\":\"Thread_id is required for this channel.\"}");

            var result = SendWithFake(fake, ValidTarget(), TextPost("Forum", "Missing thread."));

            Assert.That(result.ok, Is.False);
            Assert.That(result.statusCode, Is.EqualTo(400));
            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.NonRetryable));
            Assert.That(result.shortError, Does.Contain("Thread_id"));
            Assert.That(result.requestMayHaveReachedDiscord, Is.True);
        }

        [Test]
        public void Client_TransportException_ClassifiesAmbiguousWithoutNetwork()
        {
            var fake = new FakeWebhookTransport();
            fake.EnqueueException(new TimeoutException("Synthetic timeout."));

            var result = SendWithFake(fake, ValidTarget(), TextPost("Exception", "Synthetic transport failure."));

            Assert.That(result.ok, Is.False);
            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.Ambiguous));
            Assert.That(result.shortError, Does.Contain("Synthetic timeout"));
            Assert.That(fake.CallCount, Is.EqualTo(1));
        }

        private static WebhookSendResult SendWithFake(FakeWebhookTransport fake, Target target, ScheduledPost post)
        {
            var client = new DiscordWebhookClient(new LogService(), fake, new WebhookRequestFactory());
            WebhookSendResult result = null;
            IEnumerator routine = client.Send(target, post, sendResult => result = sendResult);

            var guard = 0;
            while (routine.MoveNext())
            {
                guard++;
                if (guard > 1000)
                    Assert.Fail("DiscordWebhookClient.Send did not complete within the contract test guard.");
            }

            return result;
        }

        private static Target ValidTarget()
        {
            return new Target
            {
                id = "target-1",
                name = "Discord",
                webhookUrl = "https://discord.com/api/webhooks/123456789012345678/test-token",
                overrideUsername = "",
                overrideAvatarUrl = ""
            };
        }

        private static ScheduledPost TextPost(string title, string body)
        {
            return new ScheduledPost
            {
                id = "post-" + Guid.NewGuid().ToString("N"),
                targetId = "target-1",
                title = title,
                body = body,
                mediaKind = MediaKind.None,
                mediaPath = "",
                allowedMentions = new AllowedMentions()
            };
        }

        private static string CreateTempFile(string extension, byte[] bytes)
        {
            var path = Path.Combine(Path.GetTempPath(), "disdove-fake-webhook-" + Guid.NewGuid().ToString("N") + extension);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var chars = new char[hash.Length * 2];
                for (int i = 0; i < hash.Length; i++)
                {
                    chars[i * 2] = Hex(hash[i] >> 4);
                    chars[i * 2 + 1] = Hex(hash[i] & 0x0F);
                }

                return new string(chars);
            }
        }

        private static char Hex(int value)
        {
            return (char)(value < 10 ? '0' + value : 'a' + value - 10);
        }

        private sealed class BlockingFileSystem : IFileSystem
        {
            public int FileExistsCalls { get; private set; }

            public bool FileExists(string path)
            {
                FileExistsCalls++;
                return false;
            }

            public bool DirectoryExists(string path)
            {
                return false;
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
                return ValidationResult.Ok();
            }

            public ValidationResult WriteAllText(string path, string contents)
            {
                return ValidationResult.Ok();
            }

            public bool TryReadAllText(string path, out string contents, out string error)
            {
                contents = "";
                error = "";
                return false;
            }

            public bool TryGetFileSizeBytes(string path, out long sizeBytes, out string error)
            {
                sizeBytes = 0;
                error = "Blocked by test file system.";
                return false;
            }

            public System.Collections.Generic.IEnumerable<string> EnumerateFiles(string path)
            {
                return Array.Empty<string>();
            }
        }
    }
}
