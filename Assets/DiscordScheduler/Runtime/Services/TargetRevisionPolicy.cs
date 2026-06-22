using System;
using System.Security.Cryptography;
using System.Text;

namespace DiscordScheduler
{
    public sealed class TargetRevisionPolicy
    {
        public SendCommandSnapshot CreateSnapshot(ScheduledPost post, Target target, string attemptId, string startedAtUtcIso)
        {
            return new SendCommandSnapshot
            {
                attemptId = attemptId ?? "",
                postId = post?.id ?? "",
                targetId = target?.id ?? post?.targetId ?? "",
                targetRevision = GetTargetRevision(target),
                payloadFingerprint = GetPostPayloadFingerprint(post),
                startedAtUtcIso = startedAtUtcIso ?? ""
            };
        }

        public bool Matches(SendCommandSnapshot snapshot, ScheduledPost post, Target target, string attemptId)
        {
            if (snapshot == null || post == null)
                return false;

            return string.Equals(snapshot.attemptId ?? "", attemptId ?? "", StringComparison.Ordinal) &&
                   string.Equals(snapshot.postId ?? "", post.id ?? "", StringComparison.Ordinal) &&
                   string.Equals(snapshot.targetId ?? "", target?.id ?? post.targetId ?? "", StringComparison.Ordinal) &&
                   string.Equals(snapshot.targetRevision ?? "", GetTargetRevision(target), StringComparison.Ordinal) &&
                   string.Equals(snapshot.payloadFingerprint ?? "", GetPostPayloadFingerprint(post), StringComparison.Ordinal);
        }

        public bool IsWebhookChanged(Target target, TargetDraft draft)
        {
            return !string.Equals((target?.webhookUrl ?? "").Trim(), (draft?.webhookUrl ?? "").Trim(), StringComparison.Ordinal);
        }

        public string GetTargetRevision(Target target)
        {
            if (target == null)
                return "";

            return HashParts(
                target.id,
                target.webhookUrl,
                target.webhookSecretRef,
                target.overrideUsername,
                target.overrideAvatarUrl);
        }

        public string GetPostPayloadFingerprint(ScheduledPost post)
        {
            if (post == null)
                return "";

            return HashParts(
                post.id,
                post.targetId,
                post.title,
                post.body,
                post.scheduledAtUtcIso,
                post.EffectiveMediaKind().ToString(),
                post.EffectiveMediaPath(),
                post.sendAsEmbed.ToString(),
                post.allowedMentions?.allowUsers.ToString(),
                post.allowedMentions?.allowRoles.ToString(),
                post.allowedMentions?.allowEveryone.ToString(),
                post.allowedMentions?.userIdsCsv,
                post.allowedMentions?.roleIdsCsv);
        }

        private static string HashParts(params string[] parts)
        {
            using (var sha = SHA256.Create())
            {
                var text = string.Join("\n", parts ?? new string[0]);
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                    sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
