using System;
using System.IO;
using System.Threading;
using UnityEngine;

namespace DiscordScheduler
{
    public class StorageService
    {
        private readonly LogService _log;

        public StorageService(LogService log)
        {
            _log = log;
        }

        public AppDatabase LoadOrCreate()
        {
            FileUtil.EnsureFolders();
            var path = FileUtil.DataFilePath;

            if (!File.Exists(path))
            {
                var db = new AppDatabase();
                Save(db);
                _log.Info("New database created: " + path);
                return db;
            }

            try
            {
                var json = File.ReadAllText(path);
                var db = JsonUtility.FromJson<AppDatabase>(json);
                if (db == null) db = new AppDatabase();
                if (db.targets == null) db.targets = new System.Collections.Generic.List<Target>();
                if (db.posts == null) db.posts = new System.Collections.Generic.List<ScheduledPost>();
                if (db.settings == null) db.settings = new AppSettings();
                if (db.settings.defaultAllowedMentions == null) db.settings.defaultAllowedMentions = new AllowedMentions();
                _log.Info("Database loaded: " + path);
                return db;
            }
            catch (Exception e)
            {
                _log.Error("Failed to load database, creating new one. Error: " + e);
                var db = new AppDatabase();
                Save(db);
                return db;
            }
        }

        public void Save(AppDatabase db)
        {
            if (db == null) return;

            FileUtil.EnsureFolders();
            var path = FileUtil.DataFilePath;

            try
            {
                var json = JsonUtility.ToJson(db, true);
                bool ok = TryWriteAllTextAtomicWithRetry(path, json, out var attempts);
                if (!ok)
                    _log.Error($"Save error: failed to write after {attempts} attempts. Path: {path}");
            }
            catch (Exception e)
            {
                _log.Error("Save error: " + e);
            }
        }

        private bool TryWriteAllTextAtomicWithRetry(string path, string contents, out int attempts)
        {
            attempts = 0;

            if (string.IsNullOrWhiteSpace(path))
                return false;

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var tempPath = path + ".tmp";

            const int maxAttempts = 5;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                attempts = attempt;

                try
                {
                    File.WriteAllText(tempPath, contents ?? string.Empty);

                    if (File.Exists(path))
                        File.Replace(tempPath, path, null);
                    else
                        File.Move(tempPath, path);

                    if (attempt > 1)
                        _log.Warn($"DB save succeeded after retry {attempt}/{maxAttempts}.");

                    return true;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    CleanupTempFile(tempPath);

                    if (attempt < maxAttempts)
                    {
                        if (attempt == 1)
                            _log.Warn("DB save retrying due to IO/Access error: " + e.Message);

                        Backoff(attempt);
                        continue;
                    }

                    _log.Error("DB save failed (IO/Access): " + e);
                    return false;
                }
                catch (Exception e)
                {
                    CleanupTempFile(tempPath);
                    _log.Error("DB save failed: " + e);
                    return false;
                }
            }

            return false;
        }

        private static void Backoff(int attempt)
        {
            Thread.Sleep(Math.Min(250 * attempt, 1000));
        }

        private static void CleanupTempFile(string tempPath)
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
            }
        }
    }
}
