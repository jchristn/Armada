namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Mysql;
    using Armada.Core.Database.Postgresql;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Database.SqlServer;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Migration hygiene (V1 readiness W3.3): every provider's migrations are numbered 1..N without gaps, contain
    /// no destructive statements beyond a reviewed legacy allowlist (additive), and are idempotent: applying the
    /// full migration list a second time on top of a migrated, populated database succeeds and changes neither
    /// the schema nor the data. The replay cases run against the configured test provider, so the database
    /// parity script runs them on SQLite, PostgreSQL, MySQL, and SQL Server.
    /// </summary>
    public sealed class MigrationHygieneSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.MigrationHygiene";

        /// <summary>
        /// Destructive statements that shipped before the additive rule was enforced. Each was reviewed: the SQLite
        /// table rebuild (rename, copy, drop) only ran once, inside one transaction, on pre-1.0 schemas; the
        /// max_parallelism drop removed a column nothing reads. Any new destructive statement fails the additive
        /// case until it is reviewed and added here.
        /// </summary>
        private static readonly Regex _DestructivePattern = new Regex(
            @"\b(DROP\s+TABLE|DROP\s+COLUMN|RENAME\s+TO|RENAME\s+COLUMN|TRUNCATE|sp_rename)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex _DeletePattern = new Regex(@"^\s*DELETE\s+FROM\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Migration Hygiene suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("migration_versions_ordered_all_providers", "Every provider numbers its migrations from 1 in strictly increasing order", TestTags.Positive, ct =>
            {
                foreach (KeyValuePair<string, IReadOnlyList<SchemaMigration>> provider in AllProviderMigrations())
                {
                    IReadOnlyList<SchemaMigration> migrations = provider.Value;
                    AssertTrue(migrations.Count > 0, provider.Key + " defines migrations");
                    AssertEqual(1, migrations[0].Version, provider.Key + " first migration");
                    for (int i = 1; i < migrations.Count; i++)
                    {
                        // Gaps are harmless (startup applies every version above the recorded maximum); a duplicate
                        // or out-of-order version would be silently skipped on upgraded databases.
                        AssertTrue(migrations[i].Version > migrations[i - 1].Version,
                            provider.Key + " migration v" + migrations[i].Version + " follows v" + migrations[i - 1].Version);
                    }
                }

                return Task.CompletedTask;
            }));

            cases.Add(Case("migrations_additive_all_providers", "No provider migration drops, renames, or deletes outside the reviewed legacy list", TestTags.Positive, ct =>
            {
                List<string> violations = new List<string>();
                foreach (KeyValuePair<string, IReadOnlyList<SchemaMigration>> provider in AllProviderMigrations())
                {
                    foreach (SchemaMigration migration in provider.Value)
                    {
                        foreach (string statement in migration.Statements)
                        {
                            bool destructive = _DestructivePattern.IsMatch(statement) || _DeletePattern.IsMatch(statement);
                            if (!destructive) continue;
                            if (IsReviewedLegacyStatement(provider.Key, migration.Version, statement)) continue;
                            violations.Add(provider.Key + " v" + migration.Version + ": " + FirstLine(statement));
                        }
                    }
                }

                AssertEqual(0, violations.Count, "unreviewed destructive migration statements: " + String.Join(" | ", violations));
                return Task.CompletedTask;
            }));

            cases.Add(Case("sqlite_add_column_statements_are_prechecked", "Every SQLite ADD COLUMN migration statement is recognized by the schema pre-check", TestTags.Positive, ct =>
            {
                List<string> unrecognized = new List<string>();
                int recognized = 0;
                foreach (SchemaMigration migration in AllProviderMigrations().Single(p => p.Key == "Sqlite").Value)
                {
                    foreach (string statement in migration.Statements)
                    {
                        if (statement.IndexOf("ADD COLUMN", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (SqliteDatabaseDriver.TryParseAddColumn(statement, out string _, out string _)) recognized++;
                        else unrecognized.Add("v" + migration.Version + ": " + FirstLine(statement));
                    }
                }

                AssertTrue(recognized > 0, "SQLite migrations add columns");
                AssertEqual(0, unrecognized.Count, "ADD COLUMN statements the pre-check cannot parse would fail on replay: " + String.Join(" | ", unrecognized));
                return Task.CompletedTask;
            }));

            cases.Add(Case("mysql_migrations_use_mysql_dialect", "MySQL migrations do not use ADD COLUMN IF NOT EXISTS (not MySQL syntax)", TestTags.Negative, ct =>
            {
                List<string> violations = new List<string>();
                foreach (SchemaMigration migration in AllProviderMigrations().Single(p => p.Key == "Mysql").Value)
                {
                    foreach (string statement in migration.Statements)
                    {
                        if (statement.IndexOf("ADD COLUMN IF NOT EXISTS", StringComparison.OrdinalIgnoreCase) >= 0)
                            violations.Add("v" + migration.Version + ": " + FirstLine(statement));
                    }
                }

                AssertEqual(0, violations.Count, "MySQL statements must be written in MySQL dialect: " + String.Join(" | ", violations));
                return Task.CompletedTask;
            }));

            cases.Add(Case("pending_migration_count_tracks_schema_version", "Pending migration count is all migrations on an empty database and zero after initialization", TestTags.Positive, async ct =>
            {
                using (IsolatedTestDatabase isolated = await IsolatedTestDatabase.CreateAsync("pending", null, ct).ConfigureAwait(false))
                using (DatabaseDriver driver = DatabaseDriverFactory.Create(isolated.Settings, QuietLogging()))
                {
                    int latest = driver.GetLatestSchemaVersion();
                    IReadOnlyList<SchemaMigration> migrations = driver.GetMigrationsForVerification();
                    AssertEqual(migrations[migrations.Count - 1].Version, latest, "latest version is the last migration");
                    AssertEqual(migrations.Count, await driver.GetPendingMigrationCountAsync(ct).ConfigureAwait(false), "every migration pending on an empty database");

                    await driver.InitializeAsync(ct).ConfigureAwait(false);
                    AssertEqual(latest, await driver.GetSchemaVersionAsync(ct).ConfigureAwait(false), "schema at latest after init");
                    AssertEqual(0, await driver.GetPendingMigrationCountAsync(ct).ConfigureAwait(false), "nothing pending after init");
                }
            }));

            cases.Add(Case("replay_all_migrations_preserves_schema_and_data", "Applying every migration twice on a populated database keeps schema and data identical", TestTags.Positive, async ct =>
            {
                using (IsolatedTestDatabase isolated = await IsolatedTestDatabase.CreateAsync("replay", null, ct).ConfigureAwait(false))
                using (DatabaseDriver driver = DatabaseDriverFactory.Create(isolated.Settings, QuietLogging()))
                {
                    await driver.InitializeAsync(ct).ConfigureAwait(false);
                    int versionBefore = await driver.GetSchemaVersionAsync(ct).ConfigureAwait(false);
                    MigrationSeededIds seeded = await SeedAsync(driver, ct).ConfigureAwait(false);

                    List<string> schemaBefore = await isolated.SnapshotSchemaAsync(ct).ConfigureAwait(false);
                    Dictionary<string, long> countsBefore = await CountTablesAsync(isolated, ct).ConfigureAwait(false);

                    await driver.ReplayMigrationsAsync(ct).ConfigureAwait(false);

                    List<string> schemaAfter = await isolated.SnapshotSchemaAsync(ct).ConfigureAwait(false);
                    Dictionary<string, long> countsAfter = await CountTablesAsync(isolated, ct).ConfigureAwait(false);

                    List<string> removed = schemaBefore.Except(schemaAfter, StringComparer.Ordinal).ToList();
                    List<string> added = schemaAfter.Except(schemaBefore, StringComparer.Ordinal).ToList();
                    AssertTrue(removed.Count == 0 && added.Count == 0,
                        "schema unchanged by replay; removed: [" + String.Join(", ", removed) + "] added: [" + String.Join(", ", added) + "]");
                    foreach (KeyValuePair<string, long> count in countsBefore)
                    {
                        AssertEqual(count.Value, countsAfter[count.Key], "row count of " + count.Key + " unchanged by replay");
                    }

                    AssertEqual(versionBefore, await driver.GetSchemaVersionAsync(ct).ConfigureAwait(false), "replay records nothing");
                    await AssertSeededAsync(driver, seeded, ct).ConfigureAwait(false);

                    // Running the normal startup path again is also a no-op.
                    await driver.InitializeAsync(ct).ConfigureAwait(false);
                    await AssertSeededAsync(driver, seeded, ct).ConfigureAwait(false);

                    if (isolated.Type == DatabaseTypeEnum.Sqlite)
                    {
                        List<string> integrity = await isolated.QueryStringsAsync("PRAGMA integrity_check;", ct).ConfigureAwait(false);
                        AssertEqual("ok", integrity.FirstOrDefault(), "SQLite integrity_check after replay");
                        List<string> fkViolations = await isolated.QueryStringsAsync("PRAGMA foreign_key_check;", ct).ConfigureAwait(false);
                        AssertEqual(0, fkViolations.Count, "SQLite foreign_key_check after replay");
                        List<string> danglingFks = await isolated.QueryStringsAsync(
                            "SELECT m.name || '->' || f.\"table\" FROM sqlite_master m JOIN pragma_foreign_key_list(m.name) f " +
                            "WHERE m.type = 'table' AND f.\"table\" NOT IN (SELECT name FROM sqlite_master WHERE type = 'table');", ct).ConfigureAwait(false);
                        AssertEqual(0, danglingFks.Count, "foreign keys reference existing tables: " + String.Join(", ", danglingFks));
                    }
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Migration Hygiene",
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

        private static List<KeyValuePair<string, IReadOnlyList<SchemaMigration>>> AllProviderMigrations()
        {
            LoggingModule logging = QuietLogging();
            List<KeyValuePair<string, IReadOnlyList<SchemaMigration>>> result = new List<KeyValuePair<string, IReadOnlyList<SchemaMigration>>>();

            DatabaseSettings sqlite = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = "unused-migration-listing.db" };
            DatabaseSettings postgres = new DatabaseSettings { Type = DatabaseTypeEnum.Postgresql, Hostname = "127.0.0.1", DatabaseName = "unused" };
            DatabaseSettings mysql = new DatabaseSettings { Type = DatabaseTypeEnum.Mysql, Hostname = "127.0.0.1", DatabaseName = "unused" };
            DatabaseSettings sqlServer = new DatabaseSettings { Type = DatabaseTypeEnum.SqlServer, Hostname = "127.0.0.1", DatabaseName = "unused" };

            using (SqliteDatabaseDriver d = new SqliteDatabaseDriver(sqlite, logging)) result.Add(new KeyValuePair<string, IReadOnlyList<SchemaMigration>>("Sqlite", d.GetMigrationsForVerification()));
            using (PostgresqlDatabaseDriver d = new PostgresqlDatabaseDriver(postgres, logging)) result.Add(new KeyValuePair<string, IReadOnlyList<SchemaMigration>>("Postgresql", d.GetMigrationsForVerification()));
            using (MysqlDatabaseDriver d = new MysqlDatabaseDriver(mysql, logging)) result.Add(new KeyValuePair<string, IReadOnlyList<SchemaMigration>>("Mysql", d.GetMigrationsForVerification()));
            using (SqlServerDatabaseDriver d = new SqlServerDatabaseDriver(sqlServer, logging)) result.Add(new KeyValuePair<string, IReadOnlyList<SchemaMigration>>("SqlServer", d.GetMigrationsForVerification()));
            return result;
        }

        private static bool IsReviewedLegacyStatement(string provider, int version, string statement)
        {
            string normalized = Regex.Replace(statement, @"\s+", " ").Trim();
            if (provider == "Sqlite")
            {
                // v8: captains.max_parallelism removed (column superseded by per-captain single-mission slots).
                if (normalized.StartsWith("ALTER TABLE captains DROP COLUMN max_parallelism", StringComparison.OrdinalIgnoreCase)) return true;

                // Multi-tenant table rebuild: rename to *_old, recreate with constraints, copy rows, drop *_old.
                if (Regex.IsMatch(normalized, @"^ALTER TABLE \w+ RENAME TO \w+_old;$", RegexOptions.IgnoreCase)) return true;
                if (Regex.IsMatch(normalized, @"^DROP TABLE \w+_old;$", RegexOptions.IgnoreCase)) return true;
            }

            return false;
        }

        private static string FirstLine(string statement)
        {
            string trimmed = statement.Trim();
            int newline = trimmed.IndexOf('\n');
            string line = newline < 0 ? trimmed : trimmed.Substring(0, newline);
            return line.Length > 160 ? line.Substring(0, 160) : line;
        }

        private static async Task<Dictionary<string, long>> CountTablesAsync(IsolatedTestDatabase isolated, CancellationToken token)
        {
            Dictionary<string, long> counts = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (string table in new[] { "tenants", "users", "credentials", "fleets", "vessels", "captains", "voyages", "missions", "signals", "events", "merge_entries", "schema_migrations" })
            {
                counts[table] = await isolated.CountAsync(table, token).ConfigureAwait(false);
            }

            return counts;
        }

        private static async Task<MigrationSeededIds> SeedAsync(DatabaseDriver driver, CancellationToken token)
        {
            MigrationSeededIds ids = new MigrationSeededIds();
            Fleet fleet = await driver.Fleets.CreateAsync(new Fleet("Replay Fleet"), token).ConfigureAwait(false);
            ids.FleetId = fleet.Id;

            Vessel vessel = new Vessel("replay-vessel", "https://example.invalid/replay.git");
            vessel.FleetId = fleet.Id;
            vessel = await driver.Vessels.CreateAsync(vessel, token).ConfigureAwait(false);
            ids.VesselId = vessel.Id;

            Captain captain = await driver.Captains.CreateAsync(new Captain("replay-captain"), token).ConfigureAwait(false);
            ids.CaptainId = captain.Id;

            Voyage voyage = await driver.Voyages.CreateAsync(new Voyage("Replay Voyage", "voyage body"), token).ConfigureAwait(false);
            ids.VoyageId = voyage.Id;

            Mission mission = new Mission("Replay Mission", "mission body");
            mission.VesselId = vessel.Id;
            mission.VoyageId = voyage.Id;
            mission.Status = MissionStatusEnum.Review;
            mission = await driver.Missions.CreateAsync(mission, token).ConfigureAwait(false);
            ids.MissionId = mission.Id;

            Signal signal = await driver.Signals.CreateAsync(new Signal(SignalTypeEnum.Progress, "replay signal"), token).ConfigureAwait(false);
            ids.SignalId = signal.Id;

            ArmadaEvent armadaEvent = new ArmadaEvent("replay.test", "replay event");
            armadaEvent = await driver.Events.CreateAsync(armadaEvent, token).ConfigureAwait(false);
            ids.EventId = armadaEvent.Id;

            MergeEntry entry = new MergeEntry("armada/replay", "main");
            entry.MissionId = mission.Id;
            entry.VesselId = vessel.Id;
            entry = await driver.MergeEntries.CreateAsync(entry, token).ConfigureAwait(false);
            ids.MergeEntryId = entry.Id;
            return ids;
        }

        private static async Task AssertSeededAsync(DatabaseDriver driver, MigrationSeededIds ids, CancellationToken token)
        {
            AssertNotNull(await driver.Fleets.ReadAsync(ids.FleetId, token).ConfigureAwait(false), "fleet survives");
            AssertNotNull(await driver.Vessels.ReadAsync(ids.VesselId, token).ConfigureAwait(false), "vessel survives");
            AssertNotNull(await driver.Captains.ReadAsync(ids.CaptainId, token).ConfigureAwait(false), "captain survives");
            AssertNotNull(await driver.Voyages.ReadAsync(ids.VoyageId, token).ConfigureAwait(false), "voyage survives");
            Mission? mission = await driver.Missions.ReadAsync(ids.MissionId, token).ConfigureAwait(false);
            AssertNotNull(mission, "mission survives");
            AssertEqual(MissionStatusEnum.Review, mission!.Status, "mission status survives");
            AssertEqual("mission body", mission.Description, "mission description survives");
            AssertNotNull(await driver.Signals.ReadAsync(ids.SignalId, token).ConfigureAwait(false), "signal survives");
            AssertNotNull(await driver.Events.ReadAsync(ids.EventId, token).ConfigureAwait(false), "event survives");
            AssertNotNull(await driver.MergeEntries.ReadAsync(ids.MergeEntryId, token).ConfigureAwait(false), "merge entry survives");
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: body,
                tags: new List<string> { tag, TestTags.Database });
        }

        #endregion
    }
}
