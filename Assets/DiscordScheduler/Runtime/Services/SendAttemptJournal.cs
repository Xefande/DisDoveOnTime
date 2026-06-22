using System;
using System.IO;
using System.Text;

namespace DiscordScheduler
{
    public sealed class SendAttemptJournal : ISendAttemptJournal
    {
        private readonly string _journalPath;
        private readonly long _maxBytes;

        public SendAttemptJournal(string dataFolder, long maxBytes = 1024L * 1024L)
        {
            var folder = string.IsNullOrWhiteSpace(dataFolder) ? "." : dataFolder;
            _journalPath = Path.Combine(folder, "send_attempts.jsonl");
            _maxBytes = Math.Max(4096L, maxBytes);
        }

        public ValidationResult Append(SendAttemptRecord record)
        {
            if (record == null)
                return ValidationResult.Fail("Send attempt journal append failed: missing record.");

            try
            {
                var directory = Path.GetDirectoryName(_journalPath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                RotateIfNeeded();
                File.AppendAllText(_journalPath, Serialize(record) + Environment.NewLine, Encoding.UTF8);
                return ValidationResult.Ok();
            }
            catch (Exception exception)
            {
                return ValidationResult.Fail("Send attempt journal append failed: " + exception.GetType().Name);
            }
        }

        private void RotateIfNeeded()
        {
            if (!File.Exists(_journalPath))
                return;

            var info = new FileInfo(_journalPath);
            if (info.Length <= _maxBytes)
                return;

            var backupPath = _journalPath + ".1";
            if (File.Exists(backupPath))
                File.Delete(backupPath);

            File.Move(_journalPath, backupPath);
        }

        private static string Serialize(SendAttemptRecord record)
        {
            var shortError = SecretRedactor.RedactAndTruncate(record.shortError, WebhookErrorClassifier.MaxShortErrorLength);

            var sb = new StringBuilder();
            sb.Append('{');
            AppendNumber(sb, "schemaVersion", 1).Append(',');
            AppendString(sb, "attemptId", record.attemptId).Append(',');
            AppendString(sb, "postId", record.postId).Append(',');
            AppendString(sb, "targetId", record.targetId).Append(',');
            AppendString(sb, "startedAtUtcIso", record.startedAtUtcIso).Append(',');
            AppendString(sb, "finishedAtUtcIso", record.finishedAtUtcIso).Append(',');
            AppendNumber(sb, "statusCode", record.statusCode).Append(',');
            AppendString(sb, "outcome", record.outcome.ToString()).Append(',');
            AppendString(sb, "discordMessageId", record.discordMessageId).Append(',');
            AppendString(sb, "shortError", shortError);
            sb.Append('}');
            return sb.ToString();
        }

        private static StringBuilder AppendString(StringBuilder sb, string name, string value)
        {
            sb.Append('"').Append(JsonUtil.Escape(name)).Append("\":\"")
                .Append(JsonUtil.Escape(value ?? "")).Append('"');
            return sb;
        }

        private static StringBuilder AppendNumber(StringBuilder sb, string name, int value)
        {
            sb.Append('"').Append(JsonUtil.Escape(name)).Append("\":").Append(value);
            return sb;
        }
    }
}
