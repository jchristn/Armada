namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using Armada.Core;
    using ArmadaConstants = Armada.Core.Constants;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Settings;

    /// <summary>
    /// Shared helper methods used by MCP tool registration classes.
    /// </summary>
    public static class McpToolHelpers
    {
        /// <summary>
        /// Checks whether a mission status transition is valid.
        /// </summary>
        /// <param name="current">Current mission status.</param>
        /// <param name="target">Target mission status.</param>
        /// <returns>True if the transition is allowed; otherwise, false.</returns>
        public static bool IsValidTransition(MissionStatusEnum current, MissionStatusEnum target)
        {
            return (current, target) switch
            {
                (MissionStatusEnum.Pending, MissionStatusEnum.Assigned) => true,
                (MissionStatusEnum.Pending, MissionStatusEnum.Cancelled) => true,
                (MissionStatusEnum.Assigned, MissionStatusEnum.InProgress) => true,
                (MissionStatusEnum.Assigned, MissionStatusEnum.Cancelled) => true,
                (MissionStatusEnum.InProgress, MissionStatusEnum.WorkProduced) => true,
                (MissionStatusEnum.InProgress, MissionStatusEnum.Testing) => true,
                (MissionStatusEnum.InProgress, MissionStatusEnum.Review) => true,
                (MissionStatusEnum.InProgress, MissionStatusEnum.Complete) => true,
                (MissionStatusEnum.InProgress, MissionStatusEnum.Failed) => true,
                (MissionStatusEnum.InProgress, MissionStatusEnum.Cancelled) => true,
                (MissionStatusEnum.WorkProduced, MissionStatusEnum.Complete) => true,
                (MissionStatusEnum.WorkProduced, MissionStatusEnum.LandingFailed) => true,
                (MissionStatusEnum.WorkProduced, MissionStatusEnum.Cancelled) => true,
                (MissionStatusEnum.Testing, MissionStatusEnum.Review) => true,
                (MissionStatusEnum.Testing, MissionStatusEnum.InProgress) => true,
                (MissionStatusEnum.Testing, MissionStatusEnum.Complete) => true,
                (MissionStatusEnum.Testing, MissionStatusEnum.Failed) => true,
                (MissionStatusEnum.Review, MissionStatusEnum.Complete) => true,
                (MissionStatusEnum.Review, MissionStatusEnum.InProgress) => true,
                (MissionStatusEnum.Review, MissionStatusEnum.Failed) => true,
                (MissionStatusEnum.LandingFailed, MissionStatusEnum.WorkProduced) => true,
                (MissionStatusEnum.LandingFailed, MissionStatusEnum.Failed) => true,
                (MissionStatusEnum.LandingFailed, MissionStatusEnum.Cancelled) => true,
                _ => false
            };
        }

        /// <summary>
        /// Get record counts for all Armada tables.
        /// </summary>
        public static async Task<Dictionary<string, long>> GetRecordCountsAsync(string databasePath)
        {
            Dictionary<string, long> counts = new Dictionary<string, long>();
            string[] tables = new[] { "fleets", "vessels", "captains", "missions", "voyages", "docks", "signals", "events", "merge_entries" };

            string connStr = "Data Source=" + databasePath;
            using (SqliteConnection conn = new SqliteConnection(connStr))
            {
                await conn.OpenAsync().ConfigureAwait(false);

                foreach (string table in tables)
                {
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        // Verify table exists before counting
                        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@table;";
                        cmd.Parameters.AddWithValue("@table", table);
                        object? exists = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                        if (exists == null || exists == DBNull.Value)
                        {
                            counts[table] = 0;
                            continue;
                        }
                    }

                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM " + table + ";";
                        object? result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                        counts[table] = (result != null && result != DBNull.Value) ? Convert.ToInt64(result) : 0;
                    }
                }
            }

            return counts;
        }

        /// <summary>
        /// Read a text file safely, allowing concurrent writes from other processes.
        /// </summary>
        public static async Task<string> ReadTextFileSafeAsync(string path)
        {
            using FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using StreamReader reader = new StreamReader(fs);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Read a log file safely as lines, allowing concurrent writes from other processes.
        /// </summary>
        public static async Task<string[]> ReadLogFileSafeAsync(string path)
        {
            string content = await ReadTextFileSafeAsync(path).ConfigureAwait(false);
            return content.Split('\n');
        }

        /// <summary>
        /// Perform a backup of the database and settings into a ZIP file.
        /// </summary>
        public static async Task<object> PerformBackupAsync(DatabaseDriver database, ArmadaSettings settings, string? outputPath)
        {
            string backupsDir = Path.Combine(ArmadaConstants.DefaultDataDirectory, "backups");
            Directory.CreateDirectory(backupsDir);

            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd-HHmmss");
            string zipPath = outputPath ?? Path.Combine(backupsDir, "armada-backup-" + timestamp + ".zip");

            // Ensure parent directory exists
            string? zipDir = Path.GetDirectoryName(zipPath);
            if (!String.IsNullOrEmpty(zipDir)) Directory.CreateDirectory(zipDir);

            string tempDbPath = Path.Combine(Path.GetTempPath(), "armada-backup-" + Guid.NewGuid().ToString("N") + ".db");

            try
            {
                // Use SQLite online backup API for a consistent snapshot
                // Pooling=False ensures Windows releases the file handle when the connection is disposed,
                // so that ZipFile.Open can read the temp file without "used by another process" errors.
                string sourceConnStr = "Data Source=" + settings.DatabasePath;
                string destConnStr = "Data Source=" + tempDbPath + ";Pooling=False";

                using (SqliteConnection sourceConn = new SqliteConnection(sourceConnStr))
                using (SqliteConnection destConn = new SqliteConnection(destConnStr))
                {
                    await sourceConn.OpenAsync().ConfigureAwait(false);
                    await destConn.OpenAsync().ConfigureAwait(false);
                    sourceConn.BackupDatabase(destConn);
                }

                // Get schema version
                int schemaVersion = 0;
                if (database is SqliteDatabaseDriver sqliteDriver)
                {
                    schemaVersion = await sqliteDriver.GetSchemaVersionAsync().ConfigureAwait(false);
                }

                // Get record counts
                Dictionary<string, long> recordCounts = await GetRecordCountsAsync(settings.DatabasePath).ConfigureAwait(false);

                // Build manifest
                object manifest = new
                {
                    backupTimestampUtc = DateTime.UtcNow.ToString("o"),
                    schemaVersion = schemaVersion,
                    armadaVersion = ArmadaConstants.ProductVersion,
                    recordCounts = recordCounts
                };

                string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                });

                // Create ZIP
                if (File.Exists(zipPath)) File.Delete(zipPath);

                using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    zip.CreateEntryFromFile(tempDbPath, "armada.db");

                    string settingsPath = ArmadaSettings.DefaultSettingsPath;
                    if (File.Exists(settingsPath))
                    {
                        zip.CreateEntryFromFile(settingsPath, "settings.json");
                    }

                    ZipArchiveEntry manifestEntry = zip.CreateEntry("manifest.json");
                    using (StreamWriter writer = new StreamWriter(manifestEntry.Open()))
                    {
                        await writer.WriteAsync(manifestJson).ConfigureAwait(false);
                    }
                }

                long sizeBytes = new FileInfo(zipPath).Length;

                return new
                {
                    Path = zipPath,
                    TimestampUtc = DateTime.UtcNow.ToString("o"),
                    SchemaVersion = schemaVersion,
                    SizeBytes = sizeBytes,
                    RecordCounts = recordCounts
                };
            }
            finally
            {
                if (File.Exists(tempDbPath)) File.Delete(tempDbPath);
            }
        }

        /// <summary>
        /// Restore the database and settings from a ZIP backup file.
        /// </summary>
        public static async Task<object> PerformRestoreAsync(DatabaseDriver database, ArmadaSettings settings, string filePath, string? originalFilename = null)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Backup file not found: " + filePath);

            // Validate ZIP contents
            using (ZipArchive zip = ZipFile.OpenRead(filePath))
            {
                ZipArchiveEntry? dbEntry = zip.GetEntry("armada.db");
                if (dbEntry == null)
                    throw new InvalidOperationException("ZIP does not contain armada.db entry");
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "armada-restore-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                // Extract ZIP
                ZipFile.ExtractToDirectory(filePath, tempDir);

                string extractedDbPath = Path.Combine(tempDir, "armada.db");
                string extractedSettingsPath = Path.Combine(tempDir, "settings.json");

                // Validate extracted database
                string validateConnStr = "Data Source=" + extractedDbPath;
                using (SqliteConnection validateConn = new SqliteConnection(validateConnStr))
                {
                    await validateConn.OpenAsync().ConfigureAwait(false);
                    using (SqliteCommand cmd = validateConn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_migrations';";
                        object? result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                        if (result == null || result == DBNull.Value)
                            throw new InvalidOperationException("Extracted database does not contain schema_migrations table — not a valid Armada backup");
                    }
                }

                // Checkpoint the current live database
                string liveConnStr = "Data Source=" + settings.DatabasePath;
                using (SqliteConnection liveConn = new SqliteConnection(liveConnStr))
                {
                    await liveConn.OpenAsync().ConfigureAwait(false);
                    using (SqliteCommand cmd = liveConn.CreateCommand())
                    {
                        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                }

                // Create safety backup of current state
                string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd-HHmmss");
                string backupsDir = Path.Combine(ArmadaConstants.DefaultDataDirectory, "backups");
                Directory.CreateDirectory(backupsDir);
                string safetyBackupPath = Path.Combine(backupsDir, "pre-restore-" + timestamp + ".zip");

                await PerformBackupAsync(database, settings, safetyBackupPath).ConfigureAwait(false);

                // Replace database file
                File.Copy(extractedDbPath, settings.DatabasePath, overwrite: true);

                // Replace settings.json if present in backup
                bool settingsRestored = false;
                if (File.Exists(extractedSettingsPath))
                {
                    File.Copy(extractedSettingsPath, ArmadaSettings.DefaultSettingsPath, overwrite: true);
                    settingsRestored = true;
                }

                // Get schema version from restored database
                int schemaVersion = 0;
                if (database is SqliteDatabaseDriver sqliteDriver)
                {
                    schemaVersion = await sqliteDriver.GetSchemaVersionAsync().ConfigureAwait(false);
                }

                string displayName = !String.IsNullOrEmpty(originalFilename) ? originalFilename : Path.GetFileName(filePath);
                string message = "Database restored from " + displayName + ". ";
                if (!settingsRestored)
                    message += "Warning: settings.json was not found in the backup ZIP. ";
                message += "Restart the server to reload the restored data.";

                return new
                {
                    Status = "restored",
                    BackupPath = safetyBackupPath,
                    SchemaVersion = schemaVersion,
                    Message = message
                };
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, recursive: true); }
                    catch { /* best effort cleanup */ }
                }
            }
        }
    }
}
