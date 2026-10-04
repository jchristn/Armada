namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Microsoft.Data.Sqlite;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Backup before migrate (V1 readiness W3.2): <see cref="MigrationBackupService"/> copies a SQLite database
    /// (with its -wal/-shm files) to a timestamped folder under the data directory when startup finds pending
    /// migrations, keeps only the newest N copies, and does nothing for new or current databases; for server
    /// providers it reports the dump command and, when confirmation is required, refuses until a confirmation
    /// covers the target schema version. The final case runs the whole path against the configured provider.
    /// </summary>
    public sealed class MigrationBackupServiceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.MigrationBackup";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Migration Backup suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("sqlite_pending_migrations_backs_up_database", "SQLite with pending migrations is copied to a timestamped backup before migrating", async ct =>
            {
                string dataDir = TestTemp.NewDirectory("mig_backup");
                string dbPath = Path.Combine(dataDir, "armada.db");
                string fleetId = await CreateRewoundSqliteAsync(dbPath, 5, ct).ConfigureAwait(false);
                ArmadaSettings settings = SqliteSettings(dataDir, dbPath);

                using (DatabaseDriver driver = DatabaseDriverFactory.Create(settings.Database, QuietLogging()))
                {
                    int latest = driver.GetLatestSchemaVersion();
                    MigrationBackupResult result = await new MigrationBackupService(settings, QuietLogging()).PrepareAsync(driver, ct).ConfigureAwait(false);

                    AssertTrue(result.MigrationsPending, "migrations pending");
                    AssertEqual(latest - 5, result.CurrentVersion, "current version");
                    AssertEqual(latest, result.TargetVersion, "target version");
                    AssertNotNull(result.BackupDirectory, "backup directory reported");
                    AssertTrue(result.BackupDirectory!.StartsWith(Path.Combine(dataDir, "backups"), StringComparison.Ordinal), "backup lives under the data directory");
                    AssertTrue(Path.GetFileName(result.BackupDirectory).StartsWith(MigrationBackupService.BackupFolderPrefix, StringComparison.Ordinal), "backup folder prefix");
                    AssertContains("-v" + (latest - 5) + "-to-v" + latest, Path.GetFileName(result.BackupDirectory), "folder names the versions");

                    string copy = Path.Combine(result.BackupDirectory, "armada.db");
                    AssertTrue(File.Exists(copy), "database copied");
                    AssertEqual(latest - 5, await ReadVersionAsync(copy, ct).ConfigureAwait(false), "copy holds the pre-migration schema version");
                    AssertEqual(1L, await CountAsync(copy, "SELECT COUNT(*) FROM fleets WHERE id = '" + fleetId + "';", ct).ConfigureAwait(false), "copy holds the data");

                    await driver.InitializeAsync(ct).ConfigureAwait(false);
                    AssertEqual(latest, await driver.GetSchemaVersionAsync(ct).ConfigureAwait(false), "migrated after the backup");
                    AssertEqual(latest - 5, await ReadVersionAsync(copy, ct).ConfigureAwait(false), "backup untouched by the migration");
                }
            }));

            cases.Add(Case("sqlite_backup_includes_wal_and_shm", "SQLite backup includes the -wal and -shm files so uncheckpointed writes are kept", async ct =>
            {
                string dataDir = TestTemp.NewDirectory("mig_backup_wal");
                string dbPath = Path.Combine(dataDir, "armada.db");
                await CreateRewoundSqliteAsync(dbPath, 2, ct).ConfigureAwait(false);
                ArmadaSettings settings = SqliteSettings(dataDir, dbPath);

                // Hold a connection open with an uncheckpointed write so the -wal file holds the newest row.
                using (SqliteConnection holder = new SqliteConnection("Data Source=" + dbPath + ";Pooling=False"))
                {
                    await holder.OpenAsync(ct).ConfigureAwait(false);
                    using (SqliteCommand cmd = holder.CreateCommand())
                    {
                        cmd.CommandText = "PRAGMA wal_autocheckpoint=0; INSERT INTO fleets (id, tenant_id, user_id, name, active, created_utc, last_update_utc) " +
                            "VALUES ('flt_walrow', 'default', 'default', 'wal-only fleet', 1, '2026-01-01T00:00:00.0000000Z', '2026-01-01T00:00:00.0000000Z');";
                        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }

                    AssertTrue(File.Exists(dbPath + "-wal"), "-wal exists while the writer is open");

                    using (DatabaseDriver driver = DatabaseDriverFactory.Create(settings.Database, QuietLogging()))
                    {
                        MigrationBackupResult result = await new MigrationBackupService(settings, QuietLogging()).PrepareAsync(driver, ct).ConfigureAwait(false);
                        string copy = Path.Combine(result.BackupDirectory!, "armada.db");
                        AssertTrue(File.Exists(copy + "-wal"), "-wal copied");
                        AssertTrue(File.Exists(copy + "-shm"), "-shm copied");
                        AssertEqual(1L, await CountAsync(copy, "SELECT COUNT(*) FROM fleets WHERE id = 'flt_walrow';", ct).ConfigureAwait(false), "row that only lived in the -wal is in the backup");
                    }
                }
            }));

            cases.Add(Case("sqlite_current_schema_takes_no_backup", "SQLite at the current schema version is not backed up", async ct =>
            {
                string dataDir = TestTemp.NewDirectory("mig_backup_current");
                string dbPath = Path.Combine(dataDir, "armada.db");
                await CreateRewoundSqliteAsync(dbPath, 0, ct).ConfigureAwait(false);
                ArmadaSettings settings = SqliteSettings(dataDir, dbPath);
                using (DatabaseDriver driver = DatabaseDriverFactory.Create(settings.Database, QuietLogging()))
                {
                    MigrationBackupResult result = await new MigrationBackupService(settings, QuietLogging()).PrepareAsync(driver, ct).ConfigureAwait(false);
                    AssertFalse(result.MigrationsPending, "nothing pending");
                    AssertNull(result.BackupDirectory, "no backup");
                    AssertFalse(Directory.Exists(Path.Combine(dataDir, "backups")), "no backups folder created");
                }
            }));

            cases.Add(Case("sqlite_new_database_takes_no_backup", "A first start (no database file yet) is not backed up and does not create the file", async ct =>
            {
                string dataDir = TestTemp.NewDirectory("mig_backup_new");
                string dbPath = Path.Combine(dataDir, "armada.db");
                ArmadaSettings settings = SqliteSettings(dataDir, dbPath);
                using (DatabaseDriver driver = DatabaseDriverFactory.Create(settings.Database, QuietLogging()))
                {
                    MigrationBackupResult result = await new MigrationBackupService(settings, QuietLogging()).PrepareAsync(driver, ct).ConfigureAwait(false);
                    AssertFalse(result.MigrationsPending, "nothing pending on a new install");
                    AssertNull(result.BackupDirectory, "no backup");
                    AssertFalse(File.Exists(dbPath), "the check does not create the database");
                }
            }));

            cases.Add(Case("sqlite_backup_retention_keeps_newest", "Only the newest MigrationBackupRetentionCount pre-migration backups are kept", async ct =>
            {
                string dataDir = TestTemp.NewDirectory("mig_backup_retention");
                string dbPath = Path.Combine(dataDir, "armada.db");
                await CreateRewoundSqliteAsync(dbPath, 1, ct).ConfigureAwait(false);
                ArmadaSettings settings = SqliteSettings(dataDir, dbPath);
                settings.Database.MigrationBackupRetentionCount = 2;

                string backups = Path.Combine(dataDir, "backups");
                foreach (string name in new[] { "pre-migration-20200101T000000000Z-v1-to-v2", "pre-migration-20210101T000000000Z-v2-to-v3", "pre-migration-20220101T000000000Z-v3-to-v4" })
                {
                    Directory.CreateDirectory(Path.Combine(backups, name));
                }

                string unrelated = Path.Combine(backups, "armada-backup-manual");
                Directory.CreateDirectory(unrelated);

                using (DatabaseDriver driver = DatabaseDriverFactory.Create(settings.Database, QuietLogging()))
                {
                    MigrationBackupResult result = await new MigrationBackupService(settings, QuietLogging(), () => new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc)).PrepareAsync(driver, ct).ConfigureAwait(false);
                    AssertEqual(2, result.BackupsPruned, "two oldest pruned");
                }

                List<string> remaining = Directory.GetDirectories(backups).Select(Path.GetFileName).Where(n => n != null).Select(n => n!).OrderBy(n => n, StringComparer.Ordinal).ToList();
                AssertEqual(3, remaining.Count, "two pre-migration backups plus the unrelated folder remain: " + String.Join(", ", remaining));
                AssertTrue(remaining.Contains("armada-backup-manual"), "folders without the prefix are never pruned");
                AssertTrue(remaining.Contains("pre-migration-20220101T000000000Z-v3-to-v4"), "newest older backup kept");
                AssertTrue(remaining.Any(n => n.StartsWith("pre-migration-20261004T120000000Z", StringComparison.Ordinal)), "new backup kept");
            }));

            cases.Add(Case("server_provider_warns_with_dump_command", "Server providers report a dump command and migrate when confirmation is not required", async ct =>
            {
                foreach (DatabaseTypeEnum type in new[] { DatabaseTypeEnum.Postgresql, DatabaseTypeEnum.Mysql, DatabaseTypeEnum.SqlServer })
                {
                    ArmadaSettings settings = ServerSettings(type);
                    MigrationBackupResult result = await new MigrationBackupService(settings, QuietLogging(), null, _ => null).PrepareForVersionsAsync(60, 75, ct).ConfigureAwait(false);
                    AssertTrue(result.MigrationsPending, type + " pending");
                    AssertNull(result.BackupDirectory, type + " is not copied by Armada");
                    AssertNotNull(result.DumpCommand, type + " dump command");
                    string expected = type == DatabaseTypeEnum.Postgresql ? "pg_dump" : (type == DatabaseTypeEnum.Mysql ? "mysqldump" : "BACKUP DATABASE [armada_prod]");
                    AssertContains(expected, result.DumpCommand!, type + " dump command tool");
                    AssertContains("armada_prod", result.DumpCommand!, type + " dump command names the database");
                    AssertContains("v75", result.DumpCommand!, type + " dump file names the target version");
                }
            }));

            cases.Add(Case("server_provider_refuses_without_confirmation", "With RequireBackupConfirmationForMigrations, server providers refuse until the target version is confirmed", async ct =>
            {
                ArmadaSettings settings = ServerSettings(DatabaseTypeEnum.Postgresql);
                settings.Database.RequireBackupConfirmationForMigrations = true;
                MigrationBackupService service = new MigrationBackupService(settings, QuietLogging(), null, _ => null);

                MigrationBackupRequiredException? refused = null;
                try { await service.PrepareForVersionsAsync(60, 75, ct).ConfigureAwait(false); }
                catch (MigrationBackupRequiredException ex) { refused = ex; }
                AssertNotNull(refused, "refused without confirmation");
                AssertEqual(60, refused!.CurrentVersion);
                AssertEqual(75, refused.TargetVersion);
                AssertContains("pg_dump", refused.Message, "message carries the dump command");
                AssertContains("ConfirmedBackupForSchemaVersion to 75", refused.Message, "message says how to confirm");
                AssertContains(MigrationBackupService.ConfirmBackupEnvVar, refused.Message, "message names the environment variable");

                settings.Database.ConfirmedBackupForSchemaVersion = 74;
                await AssertThrowsAsync<MigrationBackupRequiredException>(() => service.PrepareForVersionsAsync(60, 75, ct), "a confirmation for an older version does not approve a newer one").ConfigureAwait(false);

                settings.Database.ConfirmedBackupForSchemaVersion = 75;
                MigrationBackupResult allowed = await service.PrepareForVersionsAsync(60, 75, ct).ConfigureAwait(false);
                AssertTrue(allowed.MigrationsPending, "confirmed backup lets the migration proceed");

                AssertFalse((await service.PrepareForVersionsAsync(75, 75, ct).ConfigureAwait(false)).MigrationsPending, "nothing to confirm when current");
                AssertFalse((await service.PrepareForVersionsAsync(0, 75, ct).ConfigureAwait(false)).MigrationsPending, "a new empty database needs no confirmation");
            }));

            cases.Add(Case("server_provider_environment_confirmation", "The confirmation environment variable approves the target version", async ct =>
            {
                ArmadaSettings settings = ServerSettings(DatabaseTypeEnum.SqlServer);
                settings.Database.RequireBackupConfirmationForMigrations = true;

                MigrationBackupService confirmed = new MigrationBackupService(settings, QuietLogging(), null, name => name == MigrationBackupService.ConfirmBackupEnvVar ? " 75 " : null);
                AssertTrue((await confirmed.PrepareForVersionsAsync(70, 75, ct).ConfigureAwait(false)).MigrationsPending, "environment confirmation accepted");

                MigrationBackupService garbage = new MigrationBackupService(settings, QuietLogging(), null, name => name == MigrationBackupService.ConfirmBackupEnvVar ? "yes" : null);
                await AssertThrowsAsync<MigrationBackupRequiredException>(() => garbage.PrepareForVersionsAsync(70, 75, ct), "a non-numeric confirmation is ignored").ConfigureAwait(false);
            }));

            cases.Add(Case("newer_database_than_build_is_not_migrated", "A database newer than the build is reported, not backed up or refused", async ct =>
            {
                ArmadaSettings settings = ServerSettings(DatabaseTypeEnum.Mysql);
                settings.Database.RequireBackupConfirmationForMigrations = true;
                MigrationBackupResult result = await new MigrationBackupService(settings, QuietLogging(), null, _ => null).PrepareForVersionsAsync(80, 75, ct).ConfigureAwait(false);
                AssertFalse(result.MigrationsPending, "nothing to migrate");
                AssertNull(result.DumpCommand, "no dump command");
            }));

            cases.Add(Case("settings_clamp_and_defaults", "Migration backup settings default and clamp", ct =>
            {
                DatabaseSettings db = new DatabaseSettings();
                AssertEqual(5, db.MigrationBackupRetentionCount, "default retention");
                AssertFalse(db.RequireBackupConfirmationForMigrations, "confirmation not required by default");
                AssertEqual(0, db.ConfirmedBackupForSchemaVersion, "nothing confirmed by default");
                db.MigrationBackupRetentionCount = 0;
                AssertEqual(1, db.MigrationBackupRetentionCount, "minimum 1");
                db.MigrationBackupRetentionCount = 1000;
                AssertEqual(100, db.MigrationBackupRetentionCount, "maximum 100");
                db.ConfirmedBackupForSchemaVersion = -4;
                AssertEqual(0, db.ConfirmedBackupForSchemaVersion, "negative confirmation clamped");
                return Task.CompletedTask;
            }));

            cases.Add(Case("configured_provider_end_to_end", "Rewound database on the configured provider: backup or refusal, then migration to the latest version", async ct =>
            {
                string dataDir = TestTemp.NewDirectory("mig_backup_provider");
                using (IsolatedTestDatabase isolated = await IsolatedTestDatabase.CreateAsync("migbackup", dataDir, ct).ConfigureAwait(false))
                {
                    int latest;
                    using (DatabaseDriver seed = DatabaseDriverFactory.Create(isolated.Settings, QuietLogging()))
                    {
                        await seed.InitializeAsync(ct).ConfigureAwait(false);
                        latest = seed.GetLatestSchemaVersion();
                        await seed.Fleets.CreateAsync(new Fleet("Provider Backup Fleet"), ct).ConfigureAwait(false);
                    }

                    await isolated.QueryStringsAsync("DELETE FROM schema_migrations WHERE version > " + (latest - 3) + ";", ct).ConfigureAwait(false);

                    ArmadaSettings settings = new ArmadaSettings();
                    settings.DataDirectory = dataDir;
                    settings.Database = isolated.Settings;
                    settings.Database.RequireBackupConfirmationForMigrations = true;

                    using (DatabaseDriver driver = DatabaseDriverFactory.Create(settings.Database, QuietLogging()))
                    {
                        MigrationBackupService service = new MigrationBackupService(settings, QuietLogging(), null, _ => null);
                        if (isolated.Type == DatabaseTypeEnum.Sqlite)
                        {
                            MigrationBackupResult result = await service.PrepareAsync(driver, ct).ConfigureAwait(false);
                            AssertNotNull(result.BackupDirectory, "SQLite is backed up even when confirmation is required");
                        }
                        else
                        {
                            await AssertThrowsAsync<MigrationBackupRequiredException>(() => service.PrepareAsync(driver, ct), "server provider refused").ConfigureAwait(false);
                            settings.Database.ConfirmedBackupForSchemaVersion = latest;
                            MigrationBackupResult result = await service.PrepareAsync(driver, ct).ConfigureAwait(false);
                            AssertTrue(result.MigrationsPending, "confirmed and pending");
                            AssertEqual(latest - 3, result.CurrentVersion, "rewound version read from the database");
                        }

                        await driver.InitializeAsync(ct).ConfigureAwait(false);
                        AssertEqual(latest, await driver.GetSchemaVersionAsync(ct).ConfigureAwait(false), "migrated to latest");
                        AssertEqual(1L, await isolated.CountAsync("fleets", ct).ConfigureAwait(false), "data kept through the migration");
                    }
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Migration Backup",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule QuietLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static ArmadaSettings SqliteSettings(string dataDir, string dbPath)
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.DataDirectory = dataDir;
            DatabaseSettings db = new DatabaseSettings();
            db.Type = DatabaseTypeEnum.Sqlite;
            db.Filename = dbPath;
            settings.Database = db;
            return settings;
        }

        private static ArmadaSettings ServerSettings(DatabaseTypeEnum type)
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.DataDirectory = TestTemp.NewDirectory("mig_backup_server");
            DatabaseSettings db = new DatabaseSettings();
            db.Type = type;
            db.Hostname = "db.example.internal";
            db.Username = "armada";
            db.DatabaseName = "armada_prod";
            settings.Database = db;
            return settings;
        }

        /// <summary>
        /// Create a fully migrated SQLite database holding one fleet, then remove the newest <paramref name="rewind"/>
        /// migration records so startup sees that many pending (re-runnable) migrations.
        /// </summary>
        private static async Task<string> CreateRewoundSqliteAsync(string dbPath, int rewind, CancellationToken token)
        {
            string fleetId;
            using (SqliteDatabaseDriver driver = new SqliteDatabaseDriver("Data Source=" + dbPath, QuietLogging()))
            {
                await driver.InitializeAsync(token).ConfigureAwait(false);
                Fleet fleet = await driver.Fleets.CreateAsync(new Fleet("Backup Fleet"), token).ConfigureAwait(false);
                fleetId = fleet.Id;
                int latest = driver.GetLatestSchemaVersion();
                if (rewind > 0)
                {
                    using (SqliteConnection conn = new SqliteConnection("Data Source=" + dbPath))
                    {
                        await conn.OpenAsync(token).ConfigureAwait(false);
                        using (SqliteCommand cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "DELETE FROM schema_migrations WHERE version > " + (latest - rewind) + ";";
                            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                        }
                    }
                }
            }

            SqliteConnection.ClearAllPools();
            return fleetId;
        }

        private static async Task<int> ReadVersionAsync(string path, CancellationToken token)
        {
            return (int)await CountAsync(path, "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;", token).ConfigureAwait(false);
        }

        private static async Task<long> CountAsync(string path, string sql, CancellationToken token)
        {
            using (SqliteConnection conn = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly;Pooling=False"))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    object? value = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                    return value == null || value == DBNull.Value ? 0 : Convert.ToInt64(value);
                }
            }
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: body,
                tags: new List<string> { TestTags.Positive });
        }

        #endregion
    }
}
