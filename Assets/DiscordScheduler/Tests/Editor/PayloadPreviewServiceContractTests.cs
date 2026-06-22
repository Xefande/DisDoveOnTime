using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace DiscordScheduler.Tests
{
    public sealed class PayloadPreviewServiceContractTests
    {
        [Test]
        public void Preview_TextPayload_MatchesCapturedSendPayloadWithoutHttp()
        {
            var fake = new FakeWebhookTransport();
            fake.EnqueueResponse(204, "");
            var target = ValidTarget();
            var draft = TextDraft("Deploy", "Ready\r\nNow");
            var preview = new PayloadPreviewService().Build(target, draft);

            var result = SendWithFake(fake, target, PostFromDraft(draft));

            Assert.That(preview.canSend, Is.True);
            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.Success));
            Assert.That(fake.CallCount, Is.EqualTo(1));
            Assert.That(preview.payloadJson, Is.EqualTo(fake.Requests[0].payloadJson));
            Assert.That(preview.normalizedBodyPreview, Is.EqualTo("Deploy\nReady\nNow"));
        }

        [Test]
        public void Preview_EmbedImagePayload_MatchesCapturedSendPayloadAndAttachmentName()
        {
            var imagePath = CreateTempFile(".png", new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            try
            {
                var fake = new FakeWebhookTransport();
                fake.EnqueueResponse(200, "{\"id\":\"423456789012345678\"}");
                var target = ValidTarget();
                var draft = TextDraft("", "");
                draft.sendAsEmbed = true;
                draft.mediaPath = imagePath;

                var preview = new PayloadPreviewService().Build(target, draft);
                var result = SendWithFake(fake, target, PostFromDraft(draft));

                Assert.That(preview.canSend, Is.True);
                Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.Success));
                Assert.That(preview.payloadJson, Is.EqualTo(fake.Requests[0].payloadJson));
                Assert.That(preview.payloadJson, Does.Contain("attachment://" + Path.GetFileName(imagePath)));
                Assert.That(fake.Requests[0].fileName, Is.EqualTo(Path.GetFileName(imagePath)));
            }
            finally
            {
                TryDelete(imagePath);
            }
        }

        [Test]
        public void Preview_EmbedVideoOnlyPayload_MatchesSendAndDoesNotEmitEmptyEmbed()
        {
            var videoPath = CreateTempFile(".mp4", new byte[] { 0, 0, 0, 24, 102, 116, 121, 112 });
            try
            {
                var fake = new FakeWebhookTransport();
                fake.EnqueueResponse(204, "");
                var target = ValidTarget();
                var draft = TextDraft("", "");
                draft.sendAsEmbed = true;
                draft.mediaPath = videoPath;

                var preview = new PayloadPreviewService().Build(target, draft);
                SendWithFake(fake, target, PostFromDraft(draft));

                Assert.That(preview.canSend, Is.True);
                Assert.That(preview.payloadJson, Is.EqualTo(fake.Requests[0].payloadJson));
                Assert.That(preview.payloadJson, Does.Not.Contain("\"embeds\""));
                Assert.That(preview.payloadJson, Does.Contain("\"content\":\"\""));
            }
            finally
            {
                TryDelete(videoPath);
            }
        }

        [Test]
        public void Preview_MalformedMentionIds_BlocksWithSameValidatorAsSend()
        {
            var fake = new FakeWebhookTransport();
            var target = ValidTarget();
            var draft = TextDraft("Mention", "Invalid id");
            draft.allowedMentions = new AllowedMentions
            {
                allowUsers = true,
                userIdsCsv = "123,not-a-number"
            };

            var preview = new PayloadPreviewService().Build(target, draft);
            var result = SendWithFake(fake, target, PostFromDraft(draft));

            Assert.That(preview.canSend, Is.False);
            Assert.That(preview.blockers, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(preview.blockers[0].message, Does.Contain("numeric Discord IDs"));
            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.NonRetryable));
            Assert.That(result.shortError, Does.Contain("numeric Discord IDs"));
            Assert.That(fake.CallCount, Is.Zero);
        }

        [Test]
        public void Preview_MissingMedia_BlocksBeforePayloadAndDoesNotNeedTransport()
        {
            var draft = TextDraft("", "");
            draft.sendAsEmbed = true;
            draft.mediaPath = Path.Combine(Path.GetTempPath(), "disdove-preview-missing-" + Guid.NewGuid().ToString("N") + ".png");

            var preview = new PayloadPreviewService().Build(ValidTarget(), draft);

            Assert.That(preview.canSend, Is.False);
            Assert.That(preview.payloadJson, Is.Empty);
            Assert.That(preview.blockers.Exists(blocker => blocker.code == "media-missing"), Is.True);
        }

        [Test]
        public void Preview_UnsupportedMedia_BlocksBeforePayloadAndDoesNotNeedTransport()
        {
            var documentPath = CreateTempFile(".txt", new byte[] { 1, 2, 3 });
            try
            {
                var draft = TextDraft("Unsupported", "Document");
                draft.mediaPath = documentPath;

                var preview = new PayloadPreviewService().Build(ValidTarget(), draft);

                Assert.That(preview.canSend, Is.False);
                Assert.That(preview.payloadJson, Is.Empty);
                Assert.That(preview.blockers.Exists(blocker => blocker.code == "media-kind"), Is.True);
            }
            finally
            {
                TryDelete(documentPath);
            }
        }

        [Test]
        public void Preview_MediaSizeProbeFailure_BlocksWithoutRawPathLeak()
        {
            var fakeFileSystem = new PreviewFileSystem();
            var mediaPath = @"C:\secret-webhooks\campaign.png";
            fakeFileSystem.Exists[mediaPath] = true;
            fakeFileSystem.SizeErrors[mediaPath] = "Media is locked by another process.";

            var draft = TextDraft("Locked", "Media");
            draft.mediaPath = mediaPath;
            var preview = new PayloadPreviewService(fileSystem: fakeFileSystem).Build(ValidTarget(), draft);

            Assert.That(preview.canSend, Is.False);
            Assert.That(preview.blockers.Exists(blocker => blocker.code == "media-size"), Is.True);
            Assert.That(preview.payloadJson, Is.Empty);
            Assert.That(string.Join(" ", preview.blockers.ConvertAll(blocker => blocker.message)), Does.Not.Contain(mediaPath));
        }

        [Test]
        public void Preview_FingerprintChangesWhenDraftContentChanges()
        {
            var service = new PayloadPreviewService(utcNow: () => new DateTime(2026, 6, 22, 12, 0, 0, DateTimeKind.Utc));
            var target = ValidTarget();
            var first = TextDraft("Deploy", "Ready");
            var second = TextDraft("Deploy", "Changed");

            var firstPreview = service.Build(target, first);
            var secondFingerprint = service.BuildFingerprint(target, second);

            Assert.That(firstPreview.fingerprint.contentHash, Is.Not.EqualTo(secondFingerprint.contentHash));
            Assert.That(firstPreview.fingerprint.Matches(secondFingerprint), Is.False);
        }

        [Test]
        public void Presenter_RedactsPayloadAndMarksStalePreview()
        {
            var service = new PayloadPreviewService();
            var target = ValidTarget();
            var draft = TextDraft("Deploy", "Ready");
            var preview = service.Build(target, draft);
            preview.payloadJson = "{\"webhook\":\"https://discord.com/api/webhooks/123456789012345678/secret-token\"}";

            var changedDraft = TextDraft("Deploy", "Changed");
            var currentFingerprint = service.BuildFingerprint(target, changedDraft);
            var viewModel = new PayloadPreviewPresenter().Build(preview, currentFingerprint);

            Assert.That(viewModel.isStale, Is.True);
            Assert.That(viewModel.canSend, Is.True);
            Assert.That(viewModel.primaryAction, Is.EqualTo("Refresh preview"));
            Assert.That(viewModel.redactedPayloadJson, Does.Not.Contain("secret-token"));
            Assert.That(viewModel.summaryRows.Count, Is.GreaterThanOrEqualTo(3));
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
                    Assert.Fail("DiscordWebhookClient.Send did not complete within the preview parity guard.");
            }

            return result;
        }

        private static Target ValidTarget()
        {
            return new Target
            {
                id = "target-1",
                name = "Discord",
                webhookUrl = "https://discord.com/api/webhooks/123456789012345678/test-token"
            };
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

        private static ScheduledPost PostFromDraft(PostDraft draft)
        {
            var post = new ScheduledPost
            {
                id = "post-" + Guid.NewGuid().ToString("N"),
                targetId = draft.targetId,
                title = draft.title,
                body = draft.body,
                sendAsEmbed = draft.sendAsEmbed,
                mediaKind = ResolveMediaKind(draft.mediaPath),
                mediaPath = draft.mediaPath,
                allowedMentions = draft.allowedMentions
            };

            return post;
        }

        private static MediaKind ResolveMediaKind(string mediaPath)
        {
            if (string.IsNullOrWhiteSpace(mediaPath))
                return MediaKind.None;

            if (MediaAttachmentRules.IsAllowedVideoPath(mediaPath))
                return MediaKind.Video;

            if (MediaAttachmentRules.IsAllowedImagePath(mediaPath))
                return MediaKind.Image;

            return MediaKind.None;
        }

        private static string CreateTempFile(string extension, byte[] bytes)
        {
            var path = Path.Combine(Path.GetTempPath(), "disdove-payload-preview-" + Guid.NewGuid().ToString("N") + extension);
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

        private sealed class PreviewFileSystem : IFileSystem
        {
            public readonly Dictionary<string, bool> Exists = new Dictionary<string, bool>();
            public readonly Dictionary<string, string> SizeErrors = new Dictionary<string, string>();

            public bool FileExists(string path)
            {
                return Exists.TryGetValue(path, out var exists) && exists;
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
                return true;
            }

            public bool TryGetFileSizeBytes(string path, out long sizeBytes, out string error)
            {
                sizeBytes = 0;
                if (SizeErrors.TryGetValue(path, out error))
                    return false;

                error = "";
                sizeBytes = 128;
                return true;
            }

            public IEnumerable<string> EnumerateFiles(string path)
            {
                return Array.Empty<string>();
            }
        }
    }
}
