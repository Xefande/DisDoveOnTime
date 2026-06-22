using System;
using System.Collections.Generic;

namespace DiscordScheduler
{
    public class SchedulerService
    {
        private readonly AppDatabase _db;
        private readonly LogService _log;

        private DateTime _lastTickUtc;
        private bool _hasLastTick;
        private readonly Func<DateTime> _utcNow;

        public int SleepThresholdMinutes => Math.Max(1, _db.settings?.sleepThresholdMinutes ?? 2);

        public SchedulerService(AppDatabase db, LogService log, Func<DateTime> utcNow = null)
        {
            _db = db;
            _log = log;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public void OnStartupHandleMissed(Action<DuePost> handlePost)
        {
            var nowUtc = _utcNow();

            var postsSnapshot = _db.posts != null ? new List<ScheduledPost>(_db.posts) : new List<ScheduledPost>();

            foreach (var scheduledPost in postsSnapshot)
            {
                if (scheduledPost == null) continue;
                if (scheduledPost.status != PostStatus.Pending) continue;
                if (!TimeUtil.TryParseIsoUtc(scheduledPost.scheduledAtUtcIso, out var scheduledAtUtc))
                {
                    _log.Warn($"Startup skipped invalid scheduledAtUtcIso for post {scheduledPost.id}.");
                    continue;
                }

                if (scheduledAtUtc <= nowUtc)
                {
                    handlePost?.Invoke(CreateDuePost(scheduledPost, DueReason.AppWasOff, scheduledAtUtc, nowUtc));
                }
            }
        }

        public void OnStartupHandleMissed(Action<ScheduledPost, bool, bool> handlePost)
        {
            OnStartupHandleMissed(duePost =>
            {
                if (duePost == null) return;
                handlePost?.Invoke(duePost.post, duePost.reason == DueReason.AppWasOff, duePost.reason == DueReason.SleepGap);
            });
        }

        public void Tick(Action<DuePost> handlePost)
        {
            var nowUtc = _utcNow();
            bool sleepGap = false;
            DateTime gapStartUtc = nowUtc;
            DateTime gapEndUtc = nowUtc;

            if (_hasLastTick)
            {
                var delta = nowUtc - _lastTickUtc;
                if (delta.TotalMinutes >= SleepThresholdMinutes)
                {
                    sleepGap = true;
                    gapStartUtc = _lastTickUtc;
                    gapEndUtc = nowUtc;
                    _log.Info($"Sleep gap detektálva: {delta.TotalMinutes:0.0} perc (küszöb: {SleepThresholdMinutes} perc)");
                }
            }

            _lastTickUtc = nowUtc;
            _hasLastTick = true;

            var postsSnapshot = _db.posts != null ? new List<ScheduledPost>(_db.posts) : new List<ScheduledPost>();

            foreach (var scheduledPost in postsSnapshot)
            {
                if (scheduledPost == null) continue;
                if (scheduledPost.status != PostStatus.Pending) continue;

                if (!TimeUtil.TryParseIsoUtc(scheduledPost.scheduledAtUtcIso, out var dueUtc))
                {
                    _log.Warn($"Tick skipped invalid scheduledAtUtcIso for post {scheduledPost.id}.");
                    continue;
                }

                if (dueUtc <= nowUtc)
                {
                    var reason = sleepGap && dueUtc > gapStartUtc && dueUtc <= gapEndUtc
                        ? DueReason.SleepGap
                        : DueReason.NormalDue;
                    handlePost?.Invoke(CreateDuePost(scheduledPost, reason, dueUtc, nowUtc));
                }
            }
        }

        public void Tick(Action<ScheduledPost, bool, bool> handlePost)
        {
            Tick(duePost =>
            {
                if (duePost == null) return;
                handlePost?.Invoke(duePost.post, duePost.reason == DueReason.AppWasOff, duePost.reason == DueReason.SleepGap);
            });
        }

        private static DuePost CreateDuePost(ScheduledPost post, DueReason reason, DateTime dueUtc, DateTime observedUtc)
        {
            return new DuePost
            {
                post = post,
                reason = reason,
                dueUtc = dueUtc,
                observedUtc = observedUtc
            };
        }
    }
}
