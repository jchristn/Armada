namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using Armada.Core;
    using ArmadaConstants = Armada.Core.Constants;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Settings;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Shared helper methods used by MCP tool registration classes.
    /// </summary>
    public static class McpToolHelpers
    {
        private static readonly AsyncLocal<CancellationToken> _CallToken = new AsyncLocal<CancellationToken>();

        /// <summary>
        /// Cancellation token of the current MCP tool call (cancelled when the client cancels the call or the transport
        /// drops it), or <see cref="CancellationToken.None"/> outside an MCP call (stdio, in-process execution).
        /// </summary>
        public static CancellationToken CallToken
        {
            get { return _CallToken.Value; }
        }

        /// <summary>
        /// Set the current MCP tool call's token for this async flow. Call from inside the per-call async handler so the
        /// value never flows back to the transport.
        /// </summary>
        /// <param name="token">Token of the call.</param>
        public static void SetCallToken(CancellationToken token)
        {
            _CallToken.Value = token;
        }

        /// <summary>
        /// Checks whether a mission status transition is valid.
        /// </summary>
        /// <param name="current">Current mission status.</param>
        /// <param name="target">Target mission status.</param>
        /// <returns>True if the transition is allowed; otherwise, false.</returns>
        public static bool IsValidTransition(MissionStatusEnum current, MissionStatusEnum target)
        {
            return MissionStateMachine.IsValidTransition(current, target);
        }

        /// <summary>
        /// Describe every legal manual transition, derived from <see cref="MissionStateMachine.IsValidTransition"/>, for
        /// tool descriptions (for example "Pending->Assigned/Cancelled, ...").
        /// </summary>
        /// <returns>Comma-separated transition list.</returns>
        public static string DescribeMissionTransitions()
        {
            List<string> parts = new List<string>();
            foreach (MissionStatusEnum from in Enum.GetValues<MissionStatusEnum>())
            {
                List<string> targets = new List<string>();
                foreach (MissionStatusEnum to in Enum.GetValues<MissionStatusEnum>())
                {
                    if (MissionStateMachine.IsValidTransition(from, to)) targets.Add(to.ToString());
                }
                if (targets.Count > 0) parts.Add(from + "->" + String.Join("/", targets));
            }
            return String.Join(", ", parts);
        }

        /// <summary>
        /// The names of an enum's values joined with ", ", for tool descriptions that list accepted values.
        /// </summary>
        /// <typeparam name="TEnum">Enum type.</typeparam>
        /// <returns>Comma-separated value names in declaration order.</returns>
        public static string EnumNames<TEnum>() where TEnum : struct, Enum
        {
            return String.Join(", ", Enum.GetNames<TEnum>());
        }

        /// <summary>
        /// Resolve the auth context for the current MCP tool invocation. When the MCP transport
        /// authenticated the caller, Voltaic publishes the caller's tenant/user claims on the ambient
        /// <see cref="Voltaic.Core.RpcCallContext.Current"/> for this request's async flow; that identity is
        /// used so tools are scoped per-user exactly like the REST API. When no caller identity is present
        /// (an unauthenticated local/stdio caller, or no authentication handler configured), this falls back
        /// to the default tenant-admin context so existing local workflows keep working.
        /// </summary>
        public static AuthContext ResolveCallerContext()
        {
            Voltaic.Core.RpcCallContext? caller = Voltaic.Core.RpcCallContext.Current;
            if (caller != null
                && caller.Claims != null
                && caller.Claims.TryGetValue("userId", out string? userId)
                && !String.IsNullOrEmpty(userId))
            {
                caller.Claims.TryGetValue("tenantId", out string? tenantId);
                caller.Claims.TryGetValue("isAdmin", out string? isAdmin);
                caller.Claims.TryGetValue("isTenantAdmin", out string? isTenantAdmin);
                caller.Claims.TryGetValue("authMethod", out string? authMethod);
                caller.Claims.TryGetValue("missionId", out string? missionId);
                caller.Claims.TryGetValue("askThreadId", out string? askThreadId);
                AuthContext resolved = AuthContext.Authenticated(
                    String.IsNullOrEmpty(tenantId) ? Constants.DefaultTenantId : tenantId,
                    userId,
                    String.Equals(isAdmin, "true", StringComparison.OrdinalIgnoreCase),
                    String.Equals(isTenantAdmin, "true", StringComparison.OrdinalIgnoreCase),
                    String.IsNullOrEmpty(authMethod) ? "Mcp" : authMethod,
                    null,
                    caller.Principal);
                if (!String.IsNullOrEmpty(missionId)) resolved.MissionId = missionId;
                if (!String.IsNullOrEmpty(askThreadId)) resolved.AskThreadId = askThreadId;
                return resolved;
            }

            return AuthContext.Authenticated(
                Constants.DefaultTenantId,
                Constants.DefaultUserId,
                false,
                true,
                "Mcp",
                null,
                "MCP Default Tenant");
        }

        /// <summary>
        /// Whether the current MCP call presented a credential that the transport authenticated (a bearer token, session
        /// token, or API key). False for the unauthenticated loopback default identity and for stdio callers.
        /// </summary>
        /// <returns>True when the caller identity comes from a presented credential.</returns>
        public static bool HasPresentedCredential()
        {
            Voltaic.Core.RpcCallContext? caller = Voltaic.Core.RpcCallContext.Current;
            return caller != null
                && caller.Claims != null
                && caller.Claims.TryGetValue("userId", out string? userId)
                && !String.IsNullOrEmpty(userId);
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
        /// Perform a backup of the database and settings into a ZIP file. SQLite only: the database is copied with the
        /// SQLite online backup API (a consistent snapshot that includes uncheckpointed WAL content) and zipped with
        /// settings.json and a manifest. The default destination is {DataDirectory}/backups.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="outputPath">ZIP path, or null for a timestamped file under {DataDirectory}/backups.</param>
        /// <returns>Path, timestamp, schema version, size, and record counts.</returns>
        /// <exception cref="NotSupportedException">When the database provider is not SQLite.</exception>
        public static async Task<object> PerformBackupAsync(DatabaseDriver database, ArmadaSettings settings, string? outputPath)
        {
            EnsureSqliteForBackup(settings);
            string databasePath = settings.Database.Filename;
            string backupsDir = Path.Combine(settings.DataDirectory, "backups");
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
                string sourceConnStr = "Data Source=" + databasePath;
                string destConnStr = "Data Source=" + tempDbPath + ";Pooling=False";

                using (SqliteConnection sourceConn = new SqliteConnection(sourceConnStr))
                using (SqliteConnection destConn = new SqliteConnection(destConnStr))
                {
                    await sourceConn.OpenAsync().ConfigureAwait(false);
                    await destConn.OpenAsync().ConfigureAwait(false);
                    sourceConn.BackupDatabase(destConn);
                }

                int schemaVersion = await database.GetSchemaVersionAsync().ConfigureAwait(false);

                // Get record counts
                Dictionary<string, long> recordCounts = await GetRecordCountsAsync(databasePath).ConfigureAwait(false);

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

                    string settingsPath = settings.EffectiveSettingsFilePath;
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
        /// Restore the database and settings from a ZIP backup file. SQLite only. A safety backup of the current state
        /// is written to {DataDirectory}/backups first; the backup's database is then copied into the live database
        /// with the SQLite online backup API (so open connections and the WAL stay consistent), and settings.json is
        /// replaced when the ZIP has one. Restart the server afterward so every component reloads.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="filePath">Path of the ZIP to restore.</param>
        /// <param name="originalFilename">Name to report for the backup (for uploads).</param>
        /// <returns>Status, safety backup path, restored schema version, and a message.</returns>
        /// <exception cref="FileNotFoundException">When the ZIP does not exist.</exception>
        /// <exception cref="BackupValidationException">When the file is not a ZIP, has no armada.db, or is not an Armada database.</exception>
        /// <exception cref="NotSupportedException">When the database provider is not SQLite.</exception>
        public static async Task<object> PerformRestoreAsync(DatabaseDriver database, ArmadaSettings settings, string filePath, string? originalFilename = null)
        {
            EnsureSqliteForBackup(settings);
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Backup file not found: " + filePath);

            // Validate ZIP contents
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(filePath))
                {
                    ZipArchiveEntry? dbEntry = zip.GetEntry("armada.db");
                    if (dbEntry == null)
                        throw new BackupValidationException(BackupValidationFailureEnum.MissingDatabase, "ZIP does not contain armada.db entry");
                }
            }
            catch (InvalidDataException ex)
            {
                throw new BackupValidationException(BackupValidationFailureEnum.NotAZipArchive, "The file is not a valid ZIP archive", ex);
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
                string validateConnStr = "Data Source=" + extractedDbPath + ";Pooling=False";
                object? migrationsTable;
                try
                {
                    using (SqliteConnection validateConn = new SqliteConnection(validateConnStr))
                    {
                        await validateConn.OpenAsync().ConfigureAwait(false);
                        using (SqliteCommand cmd = validateConn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_migrations';";
                            migrationsTable = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                        }
                    }
                }
                catch (SqliteException ex)
                {
                    throw new BackupValidationException(BackupValidationFailureEnum.NotAnArmadaDatabase, "The backup's armada.db is not a SQLite database", ex);
                }
                if (migrationsTable == null || migrationsTable == DBNull.Value)
                    throw new BackupValidationException(BackupValidationFailureEnum.NotAnArmadaDatabase, "Extracted database does not contain schema_migrations table; not a valid Armada backup");

                // Create safety backup of current state
                string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd-HHmmss");
                string backupsDir = Path.Combine(settings.DataDirectory, "backups");
                Directory.CreateDirectory(backupsDir);
                string safetyBackupPath = Path.Combine(backupsDir, "pre-restore-" + timestamp + ".zip");

                await PerformBackupAsync(database, settings, safetyBackupPath).ConfigureAwait(false);

                // Copy the backup into the live database page by page. Unlike overwriting the file, the online backup
                // API takes the database lock, writes through the live WAL, and leaves connections other components
                // hold open pointing at a consistent database.
                using (SqliteConnection source = new SqliteConnection(validateConnStr))
                using (SqliteConnection live = new SqliteConnection("Data Source=" + settings.Database.Filename))
                {
                    await source.OpenAsync().ConfigureAwait(false);
                    await live.OpenAsync().ConfigureAwait(false);
                    source.BackupDatabase(live);
                    using (SqliteCommand checkpoint = live.CreateCommand())
                    {
                        checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                        await checkpoint.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                }

                // Replace settings.json if present in backup
                bool settingsRestored = false;
                if (File.Exists(extractedSettingsPath))
                {
                    File.Copy(extractedSettingsPath, settings.EffectiveSettingsFilePath, overwrite: true);
                    settingsRestored = true;
                }

                int schemaVersion = await database.GetSchemaVersionAsync().ConfigureAwait(false);

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

        private static void EnsureSqliteForBackup(ArmadaSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (settings.Database.Type != DatabaseTypeEnum.Sqlite)
            {
                throw new NotSupportedException("Built-in backup and restore support SQLite only. Back up " + settings.Database.Type +
                    " with its own tools (pg_dump, mysqldump, or BACKUP DATABASE); see docs/UPGRADING.md.");
            }
        }
    }
}
