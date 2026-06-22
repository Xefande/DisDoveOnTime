using NUnit.Framework;

namespace DiscordScheduler.Tests
{
    public sealed class DiscordWebhookPayloadContractTests
    {
        [Test]
        public void AllowedMentions_UserIds_DoNotAlsoParseUsers()
        {
            var mentions = new AllowedMentions
            {
                allowUsers = true,
                userIdsCsv = "111,222"
            };

            var json = JsonUtil.BuildAllowedMentions(mentions);

            Assert.That(json, Does.Contain("\"parse\":[]"));
            Assert.That(json, Does.Contain("\"users\":[\"111\",\"222\"]"));
            Assert.That(json, Does.Not.Contain("\"users\",\""));
        }

        [Test]
        public void AllowedMentions_RoleIds_DoNotAlsoParseRoles()
        {
            var mentions = new AllowedMentions
            {
                allowRoles = true,
                roleIdsCsv = "333"
            };

            var json = JsonUtil.BuildAllowedMentions(mentions);

            Assert.That(json, Does.Contain("\"parse\":[]"));
            Assert.That(json, Does.Contain("\"roles\":[\"333\"]"));
            Assert.That(json, Does.Not.Contain("\"roles\",\""));
        }

        [Test]
        public void AllowedMentions_ExplicitUsersAndEveryone_ParseOnlyEveryone()
        {
            var mentions = new AllowedMentions
            {
                allowUsers = true,
                userIdsCsv = "111",
                allowEveryone = true
            };

            var json = JsonUtil.BuildAllowedMentions(mentions);

            Assert.That(json, Does.Contain("\"parse\":[\"everyone\"]"));
            Assert.That(json, Does.Contain("\"users\":[\"111\"]"));
            Assert.That(json, Does.Not.Contain("\"parse\":[\"users\""));
        }

        [Test]
        public void AllowedMentions_ExplicitUsersRolesAndEveryone_ParseOnlyEveryoneWithExplicitArrays()
        {
            var mentions = new AllowedMentions
            {
                allowUsers = true,
                userIdsCsv = "111,222",
                allowRoles = true,
                roleIdsCsv = "333,444",
                allowEveryone = true
            };

            var json = JsonUtil.BuildAllowedMentions(mentions);

            Assert.That(json, Does.Contain("\"parse\":[\"everyone\"]"));
            Assert.That(json, Does.Contain("\"users\":[\"111\",\"222\"]"));
            Assert.That(json, Does.Contain("\"roles\":[\"333\",\"444\"]"));
            Assert.That(json, Does.Not.Contain("\"users\",\"roles\""));
        }

        [Test]
        public void PayloadValidator_MalformedMentionIds_Fails()
        {
            var validator = new DiscordPayloadValidator();
            var result = validator.Validate(new Target(), new PostDraft
            {
                title = "Message",
                allowedMentions = new AllowedMentions
                {
                    allowUsers = true,
                    userIdsCsv = "111,not-a-snowflake"
                }
            });

            Assert.That(result.ok, Is.False);
            Assert.That(result.error, Does.Contain("numeric Discord IDs"));
        }

        [Test]
        public void PayloadValidator_DuplicateMentionIds_Fails()
        {
            var validator = new DiscordPayloadValidator();
            var result = validator.Validate(new Target(), new PostDraft
            {
                title = "Message",
                allowedMentions = new AllowedMentions
                {
                    allowRoles = true,
                    roleIdsCsv = "333,333"
                }
            });

            Assert.That(result.ok, Is.False);
            Assert.That(result.error, Does.Contain("duplicate IDs"));
        }

        [Test]
        public void EmbedMode_VideoOnlyPayload_DoesNotEmitEmptyEmbed()
        {
            var payload = BuildPayloadJson(new ScheduledPost
            {
                title = "",
                body = "",
                sendAsEmbed = true,
                mediaKind = MediaKind.Video,
                mediaPath = "clip.mp4",
                allowedMentions = new AllowedMentions()
            }, embedFilename: "", hasMedia: true);

            Assert.That(payload, Does.Not.Contain("\"embeds\""));
            Assert.That(payload, Does.Contain("\"content\":\"\""));
        }

        [Test]
        public void EmbedMode_ImageOnlyPayload_EmitsAttachmentImageEmbed()
        {
            var payload = BuildPayloadJson(new ScheduledPost
            {
                title = "",
                body = "",
                sendAsEmbed = true,
                mediaKind = MediaKind.Image,
                mediaPath = "image.png",
                allowedMentions = new AllowedMentions()
            }, embedFilename: "image.png", hasMedia: true);

            Assert.That(payload, Does.Contain("\"embeds\""));
            Assert.That(payload, Does.Contain("attachment://image.png"));
            Assert.That(payload, Does.Not.Contain("[{}]"));
        }

        [Test]
        public void NormalMode_TitleAndBody_AreNormalizedIntoSingleContentField()
        {
            var payload = BuildPayloadJson(new ScheduledPost
            {
                title = "Deploy",
                body = "Ready\r\nNow",
                sendAsEmbed = false,
                allowedMentions = new AllowedMentions()
            }, embedFilename: "", hasMedia: false);

            Assert.That(payload, Does.Contain("\"content\":\"Deploy\\nReady\\nNow\""));
            Assert.That(payload, Does.Not.Contain("\"embeds\""));
        }

        [Test]
        public void EmbedMode_TitleBodyAndAttachment_UseEmbedAndEmptyContent()
        {
            var payload = BuildPayloadJson(new ScheduledPost
            {
                title = "Deploy",
                body = "Ready\r\nNow",
                sendAsEmbed = true,
                mediaKind = MediaKind.Image,
                mediaPath = "image.png",
                allowedMentions = new AllowedMentions()
            }, embedFilename: "image.png", hasMedia: true);

            Assert.That(payload, Does.Contain("\"title\":\"Deploy\""));
            Assert.That(payload, Does.Contain("\"description\":\"Ready\\nNow\""));
            Assert.That(payload, Does.Contain("\"image\":{\"url\":\"attachment://image.png\"}"));
            Assert.That(payload, Does.Contain("\"content\":\"\""));
        }

        [Test]
        public void EmbedMode_AttachmentFilename_UsesBasenameOnly()
        {
            var payload = BuildPayloadJson(new ScheduledPost
            {
                title = "",
                body = "",
                sendAsEmbed = true,
                mediaKind = MediaKind.Image,
                mediaPath = @"C:\secret\folder\image.png",
                allowedMentions = new AllowedMentions()
            }, embedFilename: @"C:\secret\folder\image.png", hasMedia: true);

            Assert.That(payload, Does.Contain("attachment://image.png"));
            Assert.That(payload, Does.Not.Contain("secret"));
            Assert.That(payload, Does.Not.Contain("folder"));
        }

        [Test]
        public void TargetOverrides_EmitUsernameAndAvatarWithoutChangingContent()
        {
            var target = new Target
            {
                overrideUsername = "Release Bot",
                overrideAvatarUrl = "https://cdn.example.test/avatar.png"
            };
            var post = new ScheduledPost
            {
                title = "Deploy",
                body = "Ready",
                sendAsEmbed = false,
                allowedMentions = new AllowedMentions()
            };

            var payload = new DiscordWebhookPayloadBuilder(new PayloadTextNormalizer())
                .Build(target, post, "", false);

            Assert.That(payload, Does.Contain("\"username\":\"Release Bot\""));
            Assert.That(payload, Does.Contain("\"avatar_url\":\"https://cdn.example.test/avatar.png\""));
            Assert.That(payload, Does.Contain("\"content\":\"Deploy\\nReady\""));
        }

        private static string BuildPayloadJson(ScheduledPost post, string embedFilename, bool hasMedia)
        {
            return new DiscordWebhookPayloadBuilder(new PayloadTextNormalizer())
                .Build(new Target(), post, embedFilename, hasMedia);
        }
    }
}
