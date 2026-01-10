using System;
using System.Collections.Generic;
using UnityEngine;

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

        public void OnStartupHandleMissed(Action<ScheduledPost, bool, bool> handlePost)
        {
            var nowUtc = _utcNow();

            var postsSnapshot = _db.posts != null ? new List<ScheduledPost>(_db.posts) : new List<ScheduledPost>();

            foreach (var p in postsSnapshot)
            {
                if (p == null) continue;
                if (p.status != PostStatus.Pending) continue;
                if (p.ScheduledAtUtc() <= nowUtc)
                {
                    handlePost?.Invoke(p, true, false);
                }
            }
        }

        public void Tick(Action<ScheduledPost, bool, bool> handlePost)
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

            foreach (var p in postsSnapshot)
            {
                if (p == null) continue;
                if (p.status != PostStatus.Pending) continue;

                var dueUtc = p.ScheduledAtUtc();
                if (dueUtc <= nowUtc)
                {
                    bool isSleepMissed = sleepGap && dueUtc > gapStartUtc && dueUtc <= gapEndUtc;
                    handlePost?.Invoke(p, false, isSleepMissed);
                }
            }
        }
    }
}