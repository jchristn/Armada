namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Microsoft.Data.SqlClient;
    using MySqlConnector;
    using Npgsql;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Migration 133 (RELIABILITY_SIMPLICITY_PLAN.md RS-3.1): a database at schema 132 with missions in every description
    /// marker state upgrades to typed columns that hold the same state, and the columns round-trip on every provider.
    /// </summary>
    public sealed class MissionTypedStateUpgradeSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Database.MissionTypedStateUpgrade";

        /// <summary>
        /// Build the descriptor for the typed mission state suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(SuiteId, "upgrade_backfills_markers", "Upgrading from schema 132 moves escalation, fan-out and slice markers into typed columns", UpgradeBackfillsMarkersAsync),
                new TestCaseDescriptor(SuiteId, "typed_columns_round_trip", "Typed escalation, fan-out and launch columns round-trip through create and update", TypedColumnsRoundTripAsync),
                new TestCaseDescriptor(SuiteId, "notes_keep_description_immutable", "Notes round-trip, replace by kind, and render after a description that never changes", NotesKeepDescriptionImmutableAsync)
            };
            return new TestSuiteDescriptor(SuiteId, "Typed mission state (migration 133)", cases);
        }

        private static async Task UpgradeBackfillsMarkersAsync(CancellationToken ct)
        {
            using IsolatedTestDatabase isolated = await IsolatedTestDatabase.CreateAsync("typedstate133", token: ct);
            LoggingModule log = new LoggingModule();
            log.Settings.EnableConsole = false;
            using DatabaseDriver db = DatabaseDriverFactory.Create(isolated.Settings, log);
            string provider = isolated.Type.ToString();
            using (DbConnection connection = Connection(isolated))
            {
                await connection.OpenAsync(ct);
                using IDisposable? lease = isolated.Type == DatabaseTypeEnum.Sqlite ? await SqliteWriteGate.ForConnectionString(isolated.ConnectionString).EnterAsync(ct) : null;
                string ddl = isolated.Type switch
                {
                    DatabaseTypeEnum.Sqlite => "CREATE TABLE IF NOT EXISTS schema_migrations(version INTEGER NOT NULL PRIMARY KEY,description TEXT NOT NULL,applied_utc TEXT NOT NULL);",
                    DatabaseTypeEnum.Postgresql => "CREATE TABLE IF NOT EXISTS schema_migrations(version INTEGER NOT NULL PRIMARY KEY,description TEXT NOT NULL,applied_utc TIMESTAMP NOT NULL);",
                    DatabaseTypeEnum.SqlServer => Armada.Core.Database.SqlServer.Queries.TableQueries.SchemaMigrations,
                    _ => Armada.Core.Database.Mysql.Queries.TableQueries.SchemaMigrations
                };
                await ExecuteAsync(connection, ddl, ct);
                MethodInfo apply = db.GetType().GetMethod("ExecuteMigrationAsync", BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)!;
                foreach (SchemaMigration migration in db.GetMigrationsForVerification().Where(m => m.Version < 133))
                {
                    using DbTransaction tx = await connection.BeginTransactionAsync(ct);
                    await (Task)apply.Invoke(db, new object[] { connection, tx, migration, ct })!;
                    using DbCommand record = connection.CreateCommand();
                    record.Transaction = tx;
                    record.CommandText = "INSERT INTO schema_migrations(version,description,applied_utc) VALUES(@version,@description,@utc);";
                    AddParameter(record, "@version", migration.Version);
                    AddParameter(record, "@description", migration.Description);
                    AddParameter(record, "@utc", isolated.Type == DatabaseTypeEnum.Sqlite ? DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) : DateTime.UtcNow);
                    await record.ExecuteNonQueryAsync(ct);
                    await tx.CommitAsync(ct);
                }

                string early = isolated.Type == DatabaseTypeEnum.Mysql ? "'2026-11-01 00:00:00'" : "'2026-11-01T00:00:00Z'";
                string later = isolated.Type == DatabaseTypeEnum.Mysql ? "'2026-11-01 00:05:00'" : "'2026-11-01T00:05:00Z'";
                string latest = isolated.Type == DatabaseTypeEnum.Mysql ? "'2026-11-01 00:06:00'" : "'2026-11-01T00:06:00Z'";
                await ExecuteAsync(connection, "INSERT INTO vessels(id,name,repo_url,created_utc,last_update_utc) VALUES('vsl_typed','typed','https://example.invalid/repo'," + early + "," + early + ");", ct);
                await InsertMissionAsync(connection, "msn_plain", "Worker", null, "A brief that mentions nothing special.", early, early, ct);
                await InsertMissionAsync(connection, "msn_escalated", "Worker", null, "Do the work.\n\n## Escalated to opus\nqwen could not finish this mission.", early, later, ct);
                await InsertMissionAsync(connection, "msn_architect", "Architect", null, "Plan the work.\n<!-- armada:fanout-complete -->", early, later, ct);
                await InsertMissionAsync(connection, "msn_slice0", "Worker", "msn_architect", "<!-- ARMADA:ARCHITECT-HANDOFF -->\nFirst slice", early, later, ct);
                await InsertMissionAsync(connection, "msn_slice1", "Worker", "msn_architect", "<!-- ARMADA:ARCHITECT-HANDOFF -->\nSecond slice", later, later, ct);
                await InsertMissionAsync(connection, "msn_slice2", "Worker", "msn_architect", "<!-- ARMADA:ARCHITECT-HANDOFF -->\nThird slice", latest, latest, ct);
                await InsertMissionAsync(connection, "msn_judge", "Judge", "msn_slice0", "<!-- ARMADA:ARCHITECT-HANDOFF -->\nReview the first slice", latest, latest, ct);
            }

            AssertEqual(132, await db.GetSchemaVersionAsync(ct), provider);
            await db.InitializeAsync(ct);
            AssertEqual(db.GetLatestSchemaVersion(), await db.GetSchemaVersionAsync(ct), provider);

            Mission plain = (await db.Missions.ReadAsync("msn_plain", ct))!;
            AssertEqual(0, plain.EscalationCount, provider + " plain escalation");
            AssertNull(plain.FanOutCompletedUtc, provider + " plain fan-out");
            AssertNull(plain.ArchitectSourceMissionId, provider + " plain source");
            AssertNull(plain.ArchitectPlanIndex, provider + " plain index");

            AssertEqual(1, (await db.Missions.ReadAsync("msn_escalated", ct))!.EscalationCount, provider + " escalated");

            Mission architect = (await db.Missions.ReadAsync("msn_architect", ct))!;
            AssertTrue(architect.FanOutCompletedUtc.HasValue, provider + " fan-out completed");
            AssertEqual(0, architect.EscalationCount, provider + " architect escalation");

            for (int i = 0; i < 3; i++)
            {
                Mission slice = (await db.Missions.ReadAsync("msn_slice" + i, ct))!;
                AssertEqual("msn_architect", slice.ArchitectSourceMissionId, provider + " slice " + i + " source");
                AssertEqual((int?)i, slice.ArchitectPlanIndex, provider + " slice " + i + " index");
            }

            Mission judge = (await db.Missions.ReadAsync("msn_judge", ct))!;
            AssertNull(judge.ArchitectSourceMissionId, provider + " judge is not a slice");

            // Old descriptions are left as written.
            AssertTrue((await db.Missions.ReadAsync("msn_escalated", ct))!.Description!.Contains("## Escalated to opus", StringComparison.Ordinal), provider + " description kept");

            await db.ReplayMigrationsAsync(ct);
            AssertEqual((int?)2, (await db.Missions.ReadAsync("msn_slice2", ct))!.ArchitectPlanIndex, provider + " replay keeps the index");
        }

        private static async Task TypedColumnsRoundTripAsync(CancellationToken ct)
        {
            using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
            DatabaseDriver db = fixture.Driver;
            DateTime fanOut = new DateTime(2026, 11, 2, 3, 4, 5, DateTimeKind.Utc);
            DateTime intent = new DateTime(2026, 11, 2, 3, 4, 6, DateTimeKind.Utc);
            Mission created = await db.Missions.CreateAsync(new Mission("typed state")
            {
                EscalationCount = 2,
                EscalatedFromCaptainId = "cpt_qwen",
                EscalatedFromModel = "qwen3.8",
                FanOutCompletedUtc = fanOut,
                ArchitectSourceMissionId = "msn_source",
                ArchitectPlanIndex = 3,
                LaunchIntentUtc = intent,
                LaunchJobId = "job_1"
            }, ct);

            Mission read = (await db.Missions.ReadAsync(created.Id, ct))!;
            AssertEqual(2, read.EscalationCount);
            AssertEqual("cpt_qwen", read.EscalatedFromCaptainId);
            AssertEqual("qwen3.8", read.EscalatedFromModel);
            AssertEqual((DateTime?)fanOut, read.FanOutCompletedUtc);
            AssertEqual("msn_source", read.ArchitectSourceMissionId);
            AssertEqual((int?)3, read.ArchitectPlanIndex);
            AssertEqual((DateTime?)intent, read.LaunchIntentUtc);
            AssertEqual("job_1", read.LaunchJobId);

            read.EscalationCount = 3;
            read.LaunchIntentUtc = null;
            read.LaunchJobId = null;
            read.ArchitectPlanIndex = null;
            await db.Missions.UpdateAsync(read, ct);
            Mission updated = (await db.Missions.ReadAsync(created.Id, ct))!;
            AssertEqual(3, updated.EscalationCount);
            AssertNull(updated.LaunchIntentUtc);
            AssertNull(updated.LaunchJobId);
            AssertNull(updated.ArchitectPlanIndex);
            AssertEqual((DateTime?)fanOut, updated.FanOutCompletedUtc);
        }

        private static async Task NotesKeepDescriptionImmutableAsync(CancellationToken ct)
        {
            using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
            DatabaseDriver db = fixture.Driver;
            Mission mission = await db.Missions.CreateAsync(new Mission("notes", "Build the parser.\n"), ct);
            AssertEqual("Build the parser.\n", MissionNotes.EffectiveDescription(mission), "no notes: the description as written");

            MissionNotes.Set(mission, MissionNoteKindEnum.PipelineHandoff, "first handoff");
            MissionNotes.Set(mission, MissionNoteKindEnum.ReviewFeedback, "fix the tests");
            MissionNotes.Set(mission, MissionNoteKindEnum.PipelineHandoff, "second handoff");
            await db.Missions.UpdateAsync(mission, ct);

            Mission read = (await db.Missions.ReadAsync(mission.Id, ct))!;
            AssertEqual("Build the parser.\n", read.Description, "the description is never rewritten");
            AssertEqual(2, read.Notes.Count, "one note per kind");
            AssertEqual("Build the parser.\n\nfix the tests\n\nsecond handoff", MissionNotes.EffectiveDescription(read), "a replaced note moves to the end");

            AssertTrue(MissionNotes.Remove(read, MissionNoteKindEnum.ReviewFeedback));
            MissionNotes.Set(read, MissionNoteKindEnum.Resume, "   ");
            await db.Missions.UpdateAsync(read, ct);
            Mission after = (await db.Missions.ReadAsync(mission.Id, ct))!;
            AssertEqual(MissionNoteKindEnum.PipelineHandoff, after.Notes.Single().Kind, "removed and empty notes are gone");
            AssertEqual(0, MissionNotes.FromJson("not json").Count, "unreadable notes do not fail a read");
        }

        private static async Task InsertMissionAsync(DbConnection connection, string id, string persona, string? dependsOn, string description, string created, string updated, CancellationToken ct)
        {
            using DbCommand cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO missions(id,title,vessel_id,status,persona,depends_on_mission_id,description,created_utc,last_update_utc) "
                + "VALUES(@id,@title,'vsl_typed','Pending',@persona,@depends,@description," + created + "," + updated + ");";
            AddParameter(cmd, "@id", id);
            AddParameter(cmd, "@title", id);
            AddParameter(cmd, "@persona", persona);
            AddParameter(cmd, "@depends", (object?)dependsOn ?? DBNull.Value);
            AddParameter(cmd, "@description", description);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static DbConnection Connection(IsolatedTestDatabase db)
        {
            return db.Type switch
            {
                DatabaseTypeEnum.Sqlite => new SqliteProviderConnection(db.ConnectionString),
                DatabaseTypeEnum.Postgresql => new NpgsqlConnection(db.ConnectionString),
                DatabaseTypeEnum.Mysql => new MySqlConnection(db.ConnectionString),
                _ => new SqlConnection(db.ConnectionString)
            };
        }

        private static void AddParameter(DbCommand cmd, string name, object value)
        {
            DbParameter parameter = cmd.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            cmd.Parameters.Add(parameter);
        }

        private static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken ct)
        {
            using DbCommand cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
