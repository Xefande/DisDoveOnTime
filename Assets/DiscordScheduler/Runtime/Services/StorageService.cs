using System;
using System.IO;
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
                _log.Error("Failed to load database, creating new one. Error: " + e.Message);
                var db = new AppDatabase();
                Save(db);
                return db;
            }
        }

        public void Save(AppDatabase db)
        {
            FileUtil.EnsureFolders();
            var path = FileUtil.DataFilePath;

            try
            {
                var json = JsonUtility.ToJson(db, true);
                File.WriteAllText(path, json);
            }
            catch (Exception e)
            {
                _log.Error("Save error: " + e.Message);
            }
        }
    }
}
