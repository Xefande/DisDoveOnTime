using System;
using System.IO;
using UnityEngine;

namespace DiscordScheduler
{
    public class StorageService : IStorageService
    {
        private readonly LogService _log;
        private readonly DatabaseNormalizer _normalizer;
        private readonly MigrationService _migrationService;
        private readonly IPathProvider _pathProvider;
        private readonly IFileSystem _fileSystem;
        private readonly Func<DateTime> _utcNow;
        private readonly Action<int> _backoff;

        public StorageService(LogService log)
            : this(log, new UnityPathProvider(), new SystemFileSystem())
        {
        }

        public StorageService(
            LogService log,
            IPathProvider pathProvider,
            IFileSystem fileSystem,
            Func<DateTime> utcNow = null,
            Action<int> backoff = null)
        {
            _log = log;
            _pathProvider = pathProvider ?? new UnityPathProvider();
            _fileSystem = fileSystem ?? new SystemFileSystem();
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _backoff = backoff ?? DefaultBackoff;
            _normalizer = new DatabaseNormalizer();
            _migrationService = new MigrationService(_normalizer);
        }

        public AppDatabase LoadOrCreate()
        {
            var foldersResult = EnsureStorageFolders();
            if (!foldersResult.ok)
            {
                _log.Error("Failed to prepare storage folders: " + foldersResult.error);
                return CreatePreparedNewDatabase(out _);
            }

            var path = _pathProvider.DataFilePath;

            if (!_fileSystem.FileExists(path))
            {
                var db = CreatePreparedNewDatabase(out _);
                var saveResult = TrySave(db);
                if (saveResult.ok)
                    _log.Info("New database created: " + path);
                else
                    _log.Error("Failed to save new database: " + saveResult.error);

                return db;
            }

            try
            {
                if (!_fileSystem.TryReadAllText(path, out var json, out var readError))
                    throw new IOException(readError);

                var db = JsonUtility.FromJson<AppDatabase>(json);
                if (db == null)
                {
                    _log.Warn("Database parsed to null; using a new in-memory database.");
                    db = new AppDatabase();
                }

                LogNormalizationReport(_normalizer.Normalize(db));
                var migrationReport = MigrateAndNormalize(db);
                SaveMigrationIfNeeded(db, migrationReport);
                _log.Info("Database loaded: " + path);
                return db;
            }
            catch (Exception exception)
            {
                _log.Error("Failed to load database. Error: " + exception.Message);

                if (!TryBackupCorruptDatabase(path, out var backupPath, out var backupError))
                {
                    _log.Error("Corrupt database backup failed; original database was not overwritten. Error: " + backupError);
                    return CreatePreparedNewDatabase(out _);
                }

                _log.Warn("Corrupt database backed up: " + backupPath);

                var db = CreatePreparedNewDatabase(out _);
                var saveResult = TrySave(db);
                if (saveResult.ok)
                    _log.Info("New database created after corrupt backup: " + path);
                else
                    _log.Error("Failed to save new database after corrupt backup: " + saveResult.error);

                return db;
            }
        }

        private AppDatabase CreatePreparedNewDatabase(out MigrationReport migrationReport)
        {
            var db = new AppDatabase();
            LogNormalizationReport(_normalizer.Normalize(db));
            migrationReport = MigrateAndNormalize(db);
            return db;
        }

        private MigrationReport MigrateAndNormalize(AppDatabase db)
        {
            var migrationReport = _migrationService.Migrate(db);
            LogMigrationReport(migrationReport);

            if (!migrationReport.unsupportedFutureVersion)
                LogNormalizationReport(_normalizer.Normalize(db));

            return migrationReport;
        }

        private void SaveMigrationIfNeeded(AppDatabase db, MigrationReport migrationReport)
        {
            if (migrationReport == null || !migrationReport.HasChanges)
                return;

            if (migrationReport.unsupportedFutureVersion)
            {
                _log.Warn("Database version is newer than supported; automatic migration save skipped.");
                return;
            }

            var saveResult = TrySave(db);
            if (!saveResult.ok)
                _log.Error("Failed to save migrated database: " + saveResult.error);
        }

        private void LogNormalizationReport(DatabaseNormalizationReport report)
        {
            if (report == null || !report.HasChanges)
                return;

            _log.Warn(
                $"Database normalized: target fixes={report.fixedTargets}, post fixes={report.fixedPosts}, settings fixes={report.fixedSettings}, warnings={report.warnings.Count}.");

            for (int i = 0; i < report.warnings.Count; i++)
                _log.Warn("Database normalization warning: " + report.warnings[i]);
        }

        private void LogMigrationReport(MigrationReport report)
        {
            if (report == null || !report.HasChanges)
                return;

            if (report.unsupportedFutureVersion)
                _log.Warn($"Database migration skipped: future version {report.fromVersion}, supported {MigrationService.CurrentVersion}.");
            else
                _log.Info($"Database migrated: v{report.fromVersion} -> v{report.toVersion}.");

            for (int i = 0; i < report.steps.Count; i++)
                _log.Info("Database migration step: " + report.steps[i]);
        }

        public void Save(AppDatabase db)
        {
            var result = TrySave(db);
            if (!result.ok)
                _log.Error(result.error);
        }

        public ValidationResult TrySave(AppDatabase db)
        {
            if (db == null)
                return ValidationResult.Fail("Save error: database is missing.");

            try
            {
                var foldersResult = EnsureStorageFolders();
                if (!foldersResult.ok)
                    return ValidationResult.Fail("Save error: failed to prepare storage folders: " + foldersResult.error);

                var path = _pathProvider.DataFilePath;
                var json = JsonUtility.ToJson(db, true);
                bool ok = TryWriteAllTextAtomicWithRetry(path, json, out _, out var error);
                return ok ? ValidationResult.Ok() : ValidationResult.Fail(error);
            }
            catch (Exception exception)
            {
                return ValidationResult.Fail("Save error: " + exception.Message);
            }
        }

        private bool TryWriteAllTextAtomicWithRetry(string path, string contents, out int attempts, out string error)
        {
            attempts = 0;
            error = "";

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Save error: database path is empty.";
                return false;
            }

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    var directoryResult = _fileSystem.EnsureDirectory(directory);
                    if (!directoryResult.ok)
                    {
                        error = "Save error: failed to prepare database directory: " + directoryResult.error;
                        return false;
                    }
                }
            }
            catch (Exception exception)
            {
                error = "Save error: failed to prepare database directory: " + exception.Message;
                return false;
            }

            var tempPath = path + ".tmp";

            const int maxAttempts = 3;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                attempts = attempt;

                try
                {
                    var writeResult = _fileSystem.WriteAllText(tempPath, contents ?? string.Empty);
                    if (!writeResult.ok)
                        throw new IOException(writeResult.error);

                    var moveResult = _fileSystem.FileExists(path)
                        ? _fileSystem.ReplaceFile(tempPath, path, null)
                        : _fileSystem.MoveFile(tempPath, path, false);

                    if (!moveResult.ok)
                        throw new IOException(moveResult.error);

                    if (attempt > 1)
                        _log.Warn($"DB save succeeded after retry {attempt}/{maxAttempts}.");

                    return true;
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    CleanupTempFile(tempPath);

                    if (attempt < maxAttempts)
                    {
                        if (attempt == 1)
                            _log.Warn("DB save retrying due to IO/Access error: " + exception.Message);

                        _backoff(attempt);
                        continue;
                    }

                    error = $"Save error: failed to write database after {attempts} attempts: {exception.Message}";
                    return false;
                }
                catch (Exception exception)
                {
                    CleanupTempFile(tempPath);
                    error = "Save error: " + exception.Message;
                    return false;
                }
            }

            error = "Save error: failed to write database.";
            return false;
        }

        private bool TryBackupCorruptDatabase(string path, out string backupPath, out string error)
        {
            backupPath = "";
            error = "";

            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    error = "Database path is empty.";
                    return false;
                }

                if (!_fileSystem.FileExists(path))
                {
                    error = "Database file does not exist.";
                    return false;
                }

                var directory = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(directory))
                    directory = ".";

                var name = Path.GetFileNameWithoutExtension(path);
                var ext = Path.GetExtension(path);
                var stamp = _utcNow().ToString("yyyyMMdd-HHmmss");

                for (int i = 0; i < 100; i++)
                {
                    var suffix = i == 0 ? "" : "-" + i.ToString("00");
                    backupPath = Path.Combine(directory, $"{name}.corrupt-{stamp}{suffix}{ext}");

                    if (_fileSystem.FileExists(backupPath))
                        continue;

                    var copyResult = _fileSystem.CopyFile(path, backupPath, false);
                    if (copyResult.ok)
                        return true;

                    error = copyResult.error;
                    return false;
                }

                error = "No available corrupt backup filename.";
                return false;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private ValidationResult EnsureStorageFolders()
        {
            var dataResult = _fileSystem.EnsureDirectory(_pathProvider.DataFolder);
            if (!dataResult.ok)
                return dataResult;

            var attachmentsResult = _fileSystem.EnsureDirectory(_pathProvider.AttachmentsFolder);
            if (!attachmentsResult.ok)
                return attachmentsResult;

            return ValidationResult.Ok();
        }

        private static void DefaultBackoff(int attempt)
        {
            System.Threading.Thread.Sleep(Math.Min(50 * attempt, 150));
        }

        private void CleanupTempFile(string tempPath)
        {
            try
            {
                _fileSystem.DeleteFile(tempPath);
            }
            catch
            {
            }
        }
    }
}
