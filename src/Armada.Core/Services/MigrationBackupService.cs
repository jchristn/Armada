namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Settings;

    /// <summary>
    /// Protects data before startup applies schema migrations (V1 readiness W3.2). Call
    /// <see cref="PrepareAsync"/> after creating the database driver and before
    /// <see cref="DatabaseDriver.InitializeAsync"/>. When migrations are pending on an existing database:
    /// for SQLite the database file and its -wal/-shm siblings are copied to a timestamped folder under
    /// <c>{DataDirectory}/backups</c> and the oldest pre-migration backups beyond
    /// <see cref="DatabaseSettings.MigrationBackupRetentionCount"/> are deleted; for PostgreSQL, MySQL, and SQL
    /// Server a prominent warning with the provider's dump command is logged and, when
    /// <see cref="DatabaseSettings.RequireBackupConfirmationForMigrations"/> is true and no confirmation covers the
    /// target version, startup is refused with <see cref="MigrationBackupRequiredException"/>.
    /// Not thread-safe; call once per startup.
    /// </summary>
    public class MigrationBackupService
    {
        #region Public-Members

        /// <summary>
        /// Environment variable an operator can set to the target schema version to confirm a server-provider
        /// backup was taken (an alternative to <see cref="DatabaseSettings.ConfirmedBackupForSchemaVersion"/>).
        /// </summary>
        public const string ConfirmBackupEnvVar = "ARMADA_CONFIRM_BACKUP_FOR_SCHEMA_VERSION";

        /// <summary>
        /// Name prefix of the folders holding automatic pre-migration SQLite backups.
        /// </summary>
        public const string BackupFolderPrefix = "pre-migration-";

        #endregion

        #region Private-Members

        private string _Header = "[MigrationBackupService] ";
        private ArmadaSettings _Settings;
        private LoggingModule _Logging;
        private Func<DateTime> _Clock;
        private Func<string, string?> _ReadEnvironment;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Application settings (database settings and data directory).</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="clock">UTC clock used to name backups; defaults to <see cref="DateTime.UtcNow"/>.</param>
        /// <param name="readEnvironment">Environment reader; defaults to <see cref="Environment.GetEnvironmentVariable(string)"/>.</param>
        /// <exception cref="ArgumentNullException">When settings or logging is null.</exception>
        public MigrationBackupService(ArmadaSettings settings, LoggingModule logging, Func<DateTime>? clock = null, Func<string, string?>? readEnvironment = null)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Clock = clock ?? (() => DateTime.UtcNow);
            _ReadEnvironment = readEnvironment ?? Environment.GetEnvironmentVariable;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Inspect the database and, when migrations are pending, back it up (SQLite) or warn and optionally refuse
        /// (server providers). Must run before <see cref="DatabaseDriver.InitializeAsync"/>.
        /// </summary>
        /// <param name="driver">Driver for the configured database (not yet initialized).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What was found and done.</returns>
        /// <exception cref="ArgumentNullException">When driver is null.</exception>
        /// <exception cref="MigrationBackupRequiredException">When a server provider requires a confirmed backup and none covers the target version.</exception>
        /// <exception cref="IOException">When the SQLite backup copy fails; startup must not migrate without it.</exception>
        public async Task<MigrationBackupResult> PrepareAsync(DatabaseDriver driver, CancellationToken token = default)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));

            DatabaseSettings db = _Settings.Database;
            int target = driver.GetLatestSchemaVersion();

            if (db.Type == DatabaseTypeEnum.Sqlite)
            {
                string path = SqlitePath();
                if (!File.Exists(path))
                {
                    return new MigrationBackupResult { Provider = db.Type, TargetVersion = target };
                }
            }

            int current = await driver.GetSchemaVersionAsync(token).ConfigureAwait(false);
            return await PrepareForVersionsAsync(current, target, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Apply the pre-migration policy for known schema versions. <see cref="PrepareAsync"/> calls this after
        /// reading the versions; it is public so the policy can be exercised without a live server database.
        /// </summary>
        /// <param name="currentVersion">Schema version in the database (0 when empty).</param>
        /// <param name="targetVersion">Schema version this build migrates to.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What was found and done.</returns>
        /// <exception cref="MigrationBackupRequiredException">When a server provider requires a confirmed backup and none covers the target version.</exception>
        public Task<MigrationBackupResult> PrepareForVersionsAsync(int currentVersion, int targetVersion, CancellationToken token = default)
        {
            DatabaseSettings db = _Settings.Database;
            MigrationBackupResult result = new MigrationBackupResult
            {
                Provider = db.Type,
                CurrentVersion = currentVersion,
                TargetVersion = targetVersion
            };

            if (currentVersion > targetVersion)
            {
                _Logging.Warn(_Header + "database schema v" + currentVersion + " is newer than this build (v" + targetVersion + "); " +
                    "running an older Armada against a newer database is not supported. Restore a backup taken before the upgrade or run the newer build.");
                return Task.FromResult(result);
            }

            // A new, empty database has nothing to protect; an up-to-date one has nothing to migrate.
            if (currentVersion <= 0 || currentVersion == targetVersion) return Task.FromResult(result);

            result.MigrationsPending = true;
            token.ThrowIfCancellationRequested();

            if (db.Type == DatabaseTypeEnum.Sqlite)
            {
                BackupSqlite(result);
            }
            else
            {
                CheckServerProvider(result);
            }

            return Task.FromResult(result);
        }

        /// <summary>
        /// Build the command an operator runs to dump the configured server database before an upgrade.
        /// </summary>
        /// <param name="db">Database settings.</param>
        /// <param name="targetVersion">Target schema version, used in the dump file name.</param>
        /// <returns>The dump command, or null for SQLite.</returns>
        public static string? BuildDumpCommand(DatabaseSettings db, int targetVersion)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            string file = "armada-before-schema-v" + targetVersion.ToString(CultureInfo.InvariantCulture);
            switch (db.Type)
            {
                case DatabaseTypeEnum.Postgresql:
                    return "pg_dump -h " + db.Hostname + " -p " + (db.Port > 0 ? db.Port : 5432) + " -U " + db.Username +
                        " -Fc -f " + file + ".dump " + db.DatabaseName;
                case DatabaseTypeEnum.Mysql:
                    return "mysqldump -h " + db.Hostname + " -P " + (db.Port > 0 ? db.Port : 3306) + " -u " + db.Username +
                        " -p --single-transaction --routines --triggers " + db.DatabaseName + " > " + file + ".sql";
                case DatabaseTypeEnum.SqlServer:
                    return "sqlcmd -S " + db.Hostname + "," + (db.Port > 0 ? db.Port : 1433) + " -U " + db.Username +
                        " -C -Q \"BACKUP DATABASE [" + db.DatabaseName + "] TO DISK = N'" + file + ".bak' WITH INIT, CHECKSUM\"";
                default:
                    return null;
            }
        }

        /// <summary>
        /// List the automatic pre-migration backup folders under the data directory, newest first.
        /// </summary>
        /// <returns>Absolute folder paths, newest first.</returns>
        public List<string> ListBackups()
        {
            string root = BackupRoot();
            if (!Directory.Exists(root)) return new List<string>();
            return Directory.GetDirectories(root, BackupFolderPrefix + "*")
                .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
                .ToList();
        }

        #endregion

        #region Private-Methods

        private string SqlitePath()
        {
            string filename = _Settings.Database.Filename;
            if (Path.IsPathRooted(filename)) return Path.GetFullPath(filename);
            return Path.GetFullPath(Path.Combine(_Settings.DataDirectory, filename));
        }

        private string BackupRoot()
        {
            return Path.Combine(_Settings.DataDirectory, "backups");
        }

        private void BackupSqlite(MigrationBackupResult result)
        {
            string source = SqlitePath();

            // Release pooled handles so the copy sees a quiescent file set (nothing else has the database open
            // before migrations run).
            SqliteConnection.ClearAllPools();

            string stamp = _Clock().ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
            string folderName = BackupFolderPrefix + stamp + "-v" + result.CurrentVersion + "-to-v" + result.TargetVersion;
            string folder = Path.Combine(BackupRoot(), folderName);
            int suffix = 1;
            while (Directory.Exists(folder))
            {
                folder = Path.Combine(BackupRoot(), folderName + "-" + suffix.ToString(CultureInfo.InvariantCulture));
                suffix++;
            }

            Directory.CreateDirectory(folder);
            string fileName = Path.GetFileName(source);
            long bytes = 0;
            foreach (string suffixName in new[] { "", "-wal", "-shm" })
            {
                string from = source + suffixName;
                if (!File.Exists(from)) continue;
                string to = Path.Combine(folder, fileName + suffixName);
                File.Copy(from, to, false);
                bytes += new FileInfo(to).Length;
            }

            result.BackupDirectory = folder;
            _Logging.Warn(_Header + "schema migrations pending (v" + result.CurrentVersion + " -> v" + result.TargetVersion + "); " +
                "backed up the SQLite database (" + bytes + " bytes) to " + folder + " before migrating. " +
                "To roll back: stop Armada, copy the files from that folder over " + source + ", and start the previous version.");

            result.BackupsPruned = PruneBackups();
        }

        private int PruneBackups()
        {
            int keep = _Settings.Database.MigrationBackupRetentionCount;
            List<string> backups = ListBackups();
            int pruned = 0;
            foreach (string old in backups.Skip(keep))
            {
                try
                {
                    Directory.Delete(old, true);
                    pruned++;
                    _Logging.Info(_Header + "deleted old pre-migration backup " + old + " (keeping the newest " + keep + ")");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    _Logging.Warn(_Header + "could not delete old pre-migration backup " + old + ": " + ex.Message);
                }
            }

            return pruned;
        }

        private void CheckServerProvider(MigrationBackupResult result)
        {
            DatabaseSettings db = _Settings.Database;
            string command = BuildDumpCommand(db, result.TargetVersion) ?? "";
            result.DumpCommand = command;

            int confirmed = db.ConfirmedBackupForSchemaVersion;
            string? fromEnv = _ReadEnvironment(ConfirmBackupEnvVar);
            if (!String.IsNullOrWhiteSpace(fromEnv)
                && Int32.TryParse(fromEnv.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int envConfirmed)
                && envConfirmed > confirmed)
            {
                confirmed = envConfirmed;
            }

            bool covered = confirmed >= result.TargetVersion;
            string banner = "==================== DATABASE UPGRADE ====================";
            _Logging.Warn(_Header + banner);
            _Logging.Warn(_Header + db.Type + " database " + db.DatabaseName + " on " + db.Hostname + " will be migrated from schema v" +
                result.CurrentVersion + " to v" + result.TargetVersion + ". Armada does not back up server databases itself.");
            _Logging.Warn(_Header + "Back it up first, for example: " + command);

            if (db.RequireBackupConfirmationForMigrations && !covered)
            {
                string message = db.Type + " database " + db.DatabaseName + " has pending schema migrations (v" + result.CurrentVersion +
                    " -> v" + result.TargetVersion + ") and Database.RequireBackupConfirmationForMigrations is true. " +
                    "Back up the database (for example: " + command + "), then confirm by setting Database.ConfirmedBackupForSchemaVersion to " +
                    result.TargetVersion + " in settings.json or the " + ConfirmBackupEnvVar + " environment variable to " + result.TargetVersion +
                    ", and start Armada again. See docs/UPGRADING.md.";
                _Logging.Alert(_Header + "refusing to migrate: " + message);
                _Logging.Warn(_Header + banner);
                throw new MigrationBackupRequiredException(message, result.CurrentVersion, result.TargetVersion);
            }

            if (covered)
                _Logging.Warn(_Header + "backup confirmed for schema v" + confirmed + "; migrating.");
            else
                _Logging.Warn(_Header + "migrating now. Set Database.RequireBackupConfirmationForMigrations to true to make startup wait for a confirmed backup.");
            _Logging.Warn(_Header + banner);
        }

        #endregion
    }
}
