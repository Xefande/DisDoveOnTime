using System;
using System.Collections.Generic;

namespace DiscordScheduler
{
    public sealed class DiscordPayloadValidator
    {
        public const int MaxContentChars = 2000;
        public const int MaxEmbedTitleChars = 256;
        public const int MaxEmbedDescriptionChars = 4096;
        public const int MaxUsernameChars = 80;

        private readonly PayloadTextNormalizer _textNormalizer;

        public DiscordPayloadValidator(PayloadTextNormalizer textNormalizer = null)
        {
            _textNormalizer = textNormalizer ?? new PayloadTextNormalizer();
        }

        public ValidationResult Validate(Target target, PostDraft draft)
        {
            if (draft == null)
                return ValidationResult.Fail("Missing post draft.");

            var title = _textNormalizer.Normalize(draft.title);
            var body = _textNormalizer.Normalize(draft.body);
            var mediaPath = draft.mediaPath ?? "";
            bool hasText = !_textNormalizer.IsBlank(title) || !_textNormalizer.IsBlank(body);
            bool hasMedia = !string.IsNullOrWhiteSpace(mediaPath);

            if (!hasText && !hasMedia)
                return ValidationResult.Fail("Title, message, or media is required.");

            if (draft.sendAsEmbed)
            {
                if (title.Length > MaxEmbedTitleChars)
                    return ValidationResult.Fail("Embed title is too long.");

                if (body.Length > MaxEmbedDescriptionChars)
                    return ValidationResult.Fail("Embed description is too long.");
            }
            else
            {
                var contentLength = _textNormalizer.BuildNormalContent(title, body).Length;
                if (contentLength > MaxContentChars)
                    return ValidationResult.Fail("Message content is too long.");
            }

            if (target != null)
            {
                var username = target.overrideUsername ?? "";
                if (username.Length > MaxUsernameChars)
                    return ValidationResult.Fail("Override username is too long.");

                var avatar = (target.overrideAvatarUrl ?? "").Trim();
                if (!string.IsNullOrEmpty(avatar) &&
                    (!Uri.TryCreate(avatar, UriKind.Absolute, out var uri) ||
                     !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                {
                    return ValidationResult.Fail("Override avatar URL must be a valid https URL.");
                }
            }

            var mentionResult = ValidateAllowedMentions(draft.allowedMentions);
            if (!mentionResult.ok)
                return mentionResult;

            return ValidationResult.Ok();
        }

        private static ValidationResult ValidateAllowedMentions(AllowedMentions mentions)
        {
            if (mentions == null)
                return ValidationResult.Ok();

            if (mentions.allowUsers)
            {
                var result = ValidateCsvIds(mentions.userIdsCsv, "User mention IDs");
                if (!result.ok) return result;
            }

            if (mentions.allowRoles)
            {
                var result = ValidateCsvIds(mentions.roleIdsCsv, "Role mention IDs");
                if (!result.ok) return result;
            }

            return ValidationResult.Ok();
        }

        private static ValidationResult ValidateCsvIds(string csv, string label)
        {
            if (string.IsNullOrWhiteSpace(csv))
                return ValidationResult.Ok();

            var seen = new HashSet<string>();
            var parts = csv.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                var value = (parts[i] ?? "").Trim();
                if (value.Length == 0)
                    continue;

                for (int c = 0; c < value.Length; c++)
                {
                    if (value[c] < '0' || value[c] > '9')
                        return ValidationResult.Fail(label + " must contain numeric Discord IDs only.");
                }

                if (!seen.Add(value))
                    return ValidationResult.Fail(label + " must not contain duplicate IDs.");
            }

            return ValidationResult.Ok();
        }
    }
}
