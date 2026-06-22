using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DiscordScheduler
{
    public sealed class StorageLease : IDisposable
    {
        private readonly FileStream _stream;

        public LockAcquireResult result { get; private set; }

        internal StorageLease(FileStream stream, LockAcquireResult result)
        {
            _stream = stream;
            this.result = result;
        }

        public bool IsAcquired => result != null && result.acquired;

        public void Dispose()
        {
            if (_stream != null)
                _stream.Dispose();
        }
    }

    public sealed class AppInstanceLock
    {
        private readonly ITimeProvider _time;
        private readonly TimeSpan _staleAfter;

        public AppInstanceLock(ITimeProvider time, TimeSpan? staleAfter = null)
        {
            _time = time ?? new SystemTimeProvider();
            _staleAfter = staleAfter ?? TimeSpan.FromHours(12);
        }

        public StorageLease TryAcquire(string dataFolder)
        {
            var folder = string.IsNullOrWhiteSpace(dataFolder) ? "." : dataFolder;
            var lockPath = Path.Combine(folder, "disdoveontime.storage.lock");

            try
            {
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                bool existed = File.Exists(lockPath);
                bool stale = false;
                if (existed)
                {
                    try
                    {
                        var age = _time.UtcNow - File.GetLastWriteTimeUtc(lockPath);
                        stale = age > _staleAfter;
                    }
                    catch
                    {
                        stale = false;
                    }
                }

                var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                WriteLockBody(stream);
                return new StorageLease(stream, LockAcquireResult.Acquired(lockPath, stale));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return new StorageLease(null, LockAcquireResult.Blocked(lockPath, "Storage is locked by another app instance."));
            }
            catch (Exception exception)
            {
                return new StorageLease(null, LockAcquireResult.Blocked(lockPath, "Storage lock failed: " + exception.GetType().Name));
            }
        }

        private void WriteLockBody(FileStream stream)
        {
            var pid = 0;
            try { pid = Process.GetCurrentProcess().Id; } catch { }

            var text = "utc=" + TimeUtil.ToIsoUtc(_time.UtcNow) + Environment.NewLine +
                       "pid=" + pid + Environment.NewLine;
            var bytes = Encoding.UTF8.GetBytes(text);
            stream.SetLength(0);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }
    }
}
