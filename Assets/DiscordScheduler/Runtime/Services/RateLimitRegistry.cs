using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscordScheduler
{
    public sealed class RateLimitRegistry
    {
        private readonly Dictionary<string, BackoffWindow> _targetBackoffs = new Dictionary<string, BackoffWindow>();
        private BackoffWindow _globalBackoff;

        public void RecordTargetBackoff(string targetId, DateTime utcNow, float retryAfterSeconds, string reason)
        {
            if (string.IsNullOrWhiteSpace(targetId))
                return;

            var window = BuildWindow("target", targetId, utcNow, retryAfterSeconds, reason);
            if (_targetBackoffs.TryGetValue(targetId, out var existing) &&
                TimeUtil.TryParseIsoUtc(existing.untilUtcIso, out var existingUntil) &&
                TimeUtil.TryParseIsoUtc(window.untilUtcIso, out var windowUntil) &&
                existingUntil > windowUntil)
            {
                return;
            }

            _targetBackoffs[targetId] = window;
        }

        public void RecordGlobalBackoff(DateTime utcNow, float retryAfterSeconds, string reason)
        {
            var window = BuildWindow("global", "", utcNow, retryAfterSeconds, reason);
            if (_globalBackoff != null &&
                TimeUtil.TryParseIsoUtc(_globalBackoff.untilUtcIso, out var existingUntil) &&
                TimeUtil.TryParseIsoUtc(window.untilUtcIso, out var windowUntil) &&
                existingUntil > windowUntil)
            {
                return;
            }

            _globalBackoff = window;
        }

        public ValidationResult CanSend(string targetId, DateTime utcNow)
        {
            PruneExpired(utcNow);

            if (IsActive(_globalBackoff, utcNow))
                return ValidationResult.Fail("Global rate-limit backoff is active until " + _globalBackoff.untilUtcIso + ".");

            if (!string.IsNullOrWhiteSpace(targetId) &&
                _targetBackoffs.TryGetValue(targetId, out var targetWindow) &&
                IsActive(targetWindow, utcNow))
            {
                return ValidationResult.Fail("Target rate-limit backoff is active until " + targetWindow.untilUtcIso + ".");
            }

            return ValidationResult.Ok();
        }

        public int ActiveBackoffCount(DateTime utcNow)
        {
            PruneExpired(utcNow);
            var count = IsActive(_globalBackoff, utcNow) ? 1 : 0;
            count += _targetBackoffs.Values.Count(window => IsActive(window, utcNow));
            return count;
        }

        public BackoffWindow GetTargetBackoff(string targetId, DateTime utcNow)
        {
            PruneExpired(utcNow);
            if (string.IsNullOrWhiteSpace(targetId))
                return null;

            return _targetBackoffs.TryGetValue(targetId, out var window) && IsActive(window, utcNow)
                ? window
                : null;
        }

        private static BackoffWindow BuildWindow(string scope, string targetId, DateTime utcNow, float retryAfterSeconds, string reason)
        {
            var seconds = Math.Max(0.5f, Math.Min(3600f, retryAfterSeconds <= 0 ? 2f : retryAfterSeconds));
            return new BackoffWindow
            {
                scope = scope ?? "",
                targetId = targetId ?? "",
                startedAtUtcIso = TimeUtil.ToIsoUtc(utcNow),
                untilUtcIso = TimeUtil.ToIsoUtc(utcNow.AddSeconds(seconds)),
                retryAfterSeconds = seconds,
                reason = SecretRedactor.RedactAndTruncate(reason, WebhookErrorClassifier.MaxShortErrorLength)
            };
        }

        private void PruneExpired(DateTime utcNow)
        {
            if (_globalBackoff != null && !IsActive(_globalBackoff, utcNow))
                _globalBackoff = null;

            var expired = _targetBackoffs
                .Where(pair => !IsActive(pair.Value, utcNow))
                .Select(pair => pair.Key)
                .ToList();

            for (int i = 0; i < expired.Count; i++)
                _targetBackoffs.Remove(expired[i]);
        }

        private static bool IsActive(BackoffWindow window, DateTime utcNow)
        {
            if (window == null || !TimeUtil.TryParseIsoUtc(window.untilUtcIso, out var untilUtc))
                return false;

            return untilUtc > utcNow;
        }
    }
}
