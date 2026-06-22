using System;

namespace DiscordScheduler
{
    public sealed class PostStateSnapshot
    {
        public PostStatus status;
        public string updatedAtUtcIso;
        public string lastError;
        public int retries;
        public string lastAttemptAtUtcIso;
        public string nextAttemptAtUtcIso;
        public string lastDiscordMessageId;
    }

    public sealed class PostStateMachine
    {
        private readonly ITimeProvider _time;
        private readonly int _maxRetries;

        public PostStateMachine(ITimeProvider time, int maxRetries = 10)
        {
            _time = time ?? new SystemTimeProvider();
            _maxRetries = Math.Max(1, maxRetries);
        }

        public bool CanQueue(ScheduledPost post)
        {
            return CanQueueAt(post, _time.UtcNow).ok;
        }

        public ValidationResult CanQueueAt(ScheduledPost post, DateTime utcNow)
        {
            if (post == null)
                return ValidationResult.Fail("Missing post.");

            if (post.status != PostStatus.Pending)
                return ValidationResult.Fail("Post is not pending.");

            if (!string.IsNullOrWhiteSpace(post.nextAttemptAtUtcIso))
            {
                if (!TimeUtil.TryParseIsoUtc(post.nextAttemptAtUtcIso, out var nextAttemptUtc))
                    return ValidationResult.Fail("Post retry timestamp is invalid.");

                if (nextAttemptUtc > utcNow)
                    return ValidationResult.Fail("Post retry is not due yet.");
            }

            return ValidationResult.Ok();
        }

        public ValidationResult TryMarkSending(ScheduledPost post)
        {
            var canQueue = CanQueueAt(post, _time.UtcNow);
            if (!canQueue.ok)
                return canQueue;

            post.status = PostStatus.Sending;
            post.updatedAtUtcIso = TimeUtil.ToIsoUtc(_time.UtcNow);
            return ValidationResult.Ok();
        }

        public ValidationResult MarkSent(ScheduledPost post, WebhookSendResult result)
        {
            if (post == null)
                return ValidationResult.Fail("Missing post.");

            if (post.status != PostStatus.Sending)
                return ValidationResult.Fail("Post is not sending.");

            post.status = PostStatus.Sent;
            post.lastError = "";
            post.nextAttemptAtUtcIso = "";
            post.lastAttemptAtUtcIso = TimeUtil.ToIsoUtc(_time.UtcNow);
            if (result != null && !string.IsNullOrWhiteSpace(result.discordMessageId))
                post.lastDiscordMessageId = result.discordMessageId;
            post.updatedAtUtcIso = TimeUtil.ToIsoUtc(_time.UtcNow);
            return ValidationResult.Ok();
        }

        public ValidationResult MarkSendFailedOrRetry(ScheduledPost post, WebhookSendResult result)
        {
            if (post == null)
                return ValidationResult.Fail("Missing post.");

            if (post.status != PostStatus.Sending)
                return ValidationResult.Fail("Post is not sending.");

            post.retries++;
            post.lastError = result == null ? "" : (result.shortError ?? "");
            post.lastAttemptAtUtcIso = TimeUtil.ToIsoUtc(_time.UtcNow);
            post.updatedAtUtcIso = TimeUtil.ToIsoUtc(_time.UtcNow);

            if (result != null && result.outcome == SendOutcomeKind.Ambiguous)
            {
                post.status = PostStatus.NeedsReview;
                post.nextAttemptAtUtcIso = "";
                post.lastError = string.IsNullOrWhiteSpace(post.lastError)
                    ? "Ambiguous send result; NeedsReview required."
                    : post.lastError + " NeedsReview required.";
                return ValidationResult.Ok();
            }

            if (result != null && result.outcome == SendOutcomeKind.NonRetryable)
            {
                post.status = PostStatus.Failed;
                post.nextAttemptAtUtcIso = "";
                return ValidationResult.Ok();
            }

            if (post.retries >= _maxRetries)
            {
                post.status = PostStatus.Failed;
                post.nextAttemptAtUtcIso = "";
                return ValidationResult.Ok();
            }

            post.status = PostStatus.Pending;
            post.nextAttemptAtUtcIso = TimeUtil.ToIsoUtc(_time.UtcNow.AddSeconds(GetRetryAfterSeconds(post, result)));
            return ValidationResult.Ok();
        }

        public ValidationResult MarkMissed(ScheduledPost post, string reason)
        {
            if (post == null)
                return ValidationResult.Fail("Missing post.");

            if (post.status != PostStatus.Pending)
                return ValidationResult.Fail("Post is not pending.");

            post.status = PostStatus.Missed;
            post.lastError = reason ?? "";
            post.updatedAtUtcIso = TimeUtil.ToIsoUtc(_time.UtcNow);
            return ValidationResult.Ok();
        }

        public ValidationResult MarkFailedByPolicy(ScheduledPost post, string reason)
        {
            if (post == null)
                return ValidationResult.Fail("Missing post.");

            if (post.status != PostStatus.Pending && post.status != PostStatus.Sending)
                return ValidationResult.Fail("Post is not pending or sending.");

            post.status = PostStatus.Failed;
            post.lastError = reason ?? "";
            post.nextAttemptAtUtcIso = "";
            post.updatedAtUtcIso = TimeUtil.ToIsoUtc(_time.UtcNow);
            return ValidationResult.Ok();
        }

        public ValidationResult DeferPendingUntil(ScheduledPost post, DateTime nextAttemptUtc, string reason)
        {
            if (post == null)
                return ValidationResult.Fail("Missing post.");

            if (post.status != PostStatus.Pending && post.status != PostStatus.Sending)
                return ValidationResult.Fail("Post is not pending or sending.");

            post.status = PostStatus.Pending;
            post.nextAttemptAtUtcIso = TimeUtil.ToIsoUtc(nextAttemptUtc);
            post.lastError = reason ?? "";
            post.updatedAtUtcIso = TimeUtil.ToIsoUtc(_time.UtcNow);
            return ValidationResult.Ok();
        }

        public ValidationResult TryMarkPendingManual(ScheduledPost post, bool resetRetries, bool allowSent = false)
        {
            if (post == null)
                return ValidationResult.Fail("Missing post.");

            if (post.status == PostStatus.Sent && !allowSent)
                return ValidationResult.Fail("Sent post requires explicit confirmation before manual requeue.");

            post.status = PostStatus.Pending;
            post.nextAttemptAtUtcIso = "";
            if (resetRetries)
            {
                post.retries = 0;
                post.lastError = "";
                post.lastAttemptAtUtcIso = "";
            }

            post.updatedAtUtcIso = TimeUtil.ToIsoUtc(_time.UtcNow);
            return ValidationResult.Ok();
        }

        public ValidationResult DismissNeedsReview(ScheduledPost post, string reason)
        {
            if (post == null)
                return ValidationResult.Fail("Missing post.");

            if (post.status != PostStatus.NeedsReview)
                return ValidationResult.Fail("Post is not in NeedsReview.");

            post.status = PostStatus.Failed;
            post.nextAttemptAtUtcIso = "";
            post.lastError = string.IsNullOrWhiteSpace(reason) ? "Dismissed from review." : reason;
            post.updatedAtUtcIso = TimeUtil.ToIsoUtc(_time.UtcNow);
            return ValidationResult.Ok();
        }

        public ValidationResult RecoverStaleSending(ScheduledPost post, string reason)
        {
            if (post == null)
                return ValidationResult.Fail("Missing post.");

            if (post.status != PostStatus.Sending)
                return ValidationResult.Fail("Post is not sending.");

            post.status = PostStatus.NeedsReview;
            post.nextAttemptAtUtcIso = "";
            post.lastError = string.IsNullOrWhiteSpace(reason)
                ? "Recovered stale Sending state; manual review required."
                : reason;
            post.updatedAtUtcIso = TimeUtil.ToIsoUtc(_time.UtcNow);
            return ValidationResult.Ok();
        }

        public PostStateSnapshot Capture(ScheduledPost post)
        {
            if (post == null)
                return null;

            return new PostStateSnapshot
            {
                status = post.status,
                updatedAtUtcIso = post.updatedAtUtcIso,
                lastError = post.lastError,
                retries = post.retries,
                lastAttemptAtUtcIso = post.lastAttemptAtUtcIso,
                nextAttemptAtUtcIso = post.nextAttemptAtUtcIso,
                lastDiscordMessageId = post.lastDiscordMessageId
            };
        }

        public void Restore(ScheduledPost post, PostStateSnapshot snapshot)
        {
            if (post == null || snapshot == null)
                return;

            post.status = snapshot.status;
            post.updatedAtUtcIso = snapshot.updatedAtUtcIso;
            post.lastError = snapshot.lastError;
            post.retries = snapshot.retries;
            post.lastAttemptAtUtcIso = snapshot.lastAttemptAtUtcIso;
            post.nextAttemptAtUtcIso = snapshot.nextAttemptAtUtcIso;
            post.lastDiscordMessageId = snapshot.lastDiscordMessageId;
        }

        private static double GetRetryAfterSeconds(ScheduledPost post, WebhookSendResult result)
        {
            if (result != null && result.retryAfterSeconds > 0)
                return Math.Min(3600, Math.Max(2, result.retryAfterSeconds));

            if (result != null && result.outcome == SendOutcomeKind.RateLimited)
                return 60;

            return Math.Min(60, Math.Max(2, post.retries * 2));
        }
    }
}
