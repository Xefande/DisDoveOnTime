using System;
using System.Collections.Generic;

namespace DiscordScheduler
{
    public class LogService
    {
        private readonly int _max;
        private readonly Queue<string> _lines;
        private readonly Func<DateTime> _now;

        public LogService(int maxLines = 300, Func<DateTime> now = null)
        {
            _max = Math.Max(50, maxLines);
            _lines = new Queue<string>(_max);
            _now = now ?? (() => DateTime.Now);
        }

        public void Info(string msg) => Add("INFO", msg);
        public void Warn(string msg) => Add("WARN", msg);
        public void Error(string msg) => Add("ERR ", msg);

        public void Clear() => _lines.Clear();

        private void Add(string level, string msg)
        {
            var line = $"[{_now():yyyy-MM-dd HH:mm:ss}] {level}  {SecretRedactor.Redact(msg ?? "")}";
            _lines.Enqueue(line);
            while (_lines.Count > _max) _lines.Dequeue();
        }

        public List<string> Snapshot()
        {
            return new List<string>(_lines);
        }
    }
}
