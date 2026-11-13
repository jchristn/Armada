namespace Test.Shared.Suites.Database
{
    using System.Data.Common;
    using System.Globalization;
    using System.Reflection;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Microsoft.Data.Sqlite;
    using Microsoft.Data.SqlClient;
    using MySqlConnector;
    using Npgsql;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Upgrade populated v1.2 storage through the final concurrency schema on each configured provider.</summary>
    public sealed class ConcurrencyUpgradeSuite : IArmadaTestSuite
    {
        public TestSuiteDescriptor Build()
        {
            const string suite = "Database.ConcurrencyUpgrade";
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                new(suite,"v120_to_v130","Populated v1.2 upgrade preserves execution/session/rescue ownership and removes legacy columns", async ct =>
                {
                    await VerifyUpgradeAsync(null, ct);
                }),
                new(suite,"runtime_context_owner", "Mission runtime clocks and recovery budgets are guarded by execution ownership", async ct =>
                {
                    using TestDatabase f = await TestDatabaseHelper.CreateDatabaseAsync();
                    var db = f.Driver;
                    Captain captain = await db.Captains.CreateAsync(new Captain("runtime owner"), ct);
                    Mission mission = await db.Missions.CreateAsync(new Mission("runtime context"), ct);
                    AssertTrue(await db.Captains.TryClaimAsync(captain.Id, mission.Id, null, ct));
                    AssertTrue(await db.Captains.TrySetProcessAsync(captain.Id, mission.Id, 4242, ct));
                    DateTime heartbeat = new DateTime(2026, 11, 1, 1, 2, 3, DateTimeKind.Utc);
                    AssertFalse(await db.Missions.TryUpdateRuntimeContextAsync(mission.Id, "wrong-captain", 4242, heartbeat, 99, ct));
                    AssertFalse(await db.Missions.TryUpdateRuntimeContextAsync(mission.Id, captain.Id, 9999, heartbeat, 99, ct));
                    AssertTrue(await db.Missions.TryUpdateRuntimeContextAsync(mission.Id, captain.Id, 4242, heartbeat, 2, ct));
                    Mission read = (await db.Missions.ReadAsync(mission.Id, ct))!;
                    AssertEqual(heartbeat, read.LastHeartbeatUtc); AssertEqual(2, read.StallRecoveryAttempts);
                    AssertTrue(await db.Captains.TrySetProcessAsync(captain.Id, mission.Id, 4243, ct));
                    AssertFalse(await db.Missions.TryUpdateRuntimeContextAsync(mission.Id, captain.Id, 4242, heartbeat.AddDays(1), 99, ct));
                    AssertEqual(2, (await db.Missions.ReadAsync(mission.Id, ct))!.StallRecoveryAttempts);
                }),
                new(suite,"metadata_and_wait_storage","Mission metadata and wait intervals round-trip, close, expire and purge without affecting siblings",async ct=>
                {
                    using TestDatabase f=await TestDatabaseHelper.CreateDatabaseAsync();var db=f.Driver;
                    Mission original=await db.Missions.CreateAsync(new Mission("original"),ct);
                    Mission mission=await db.Missions.CreateAsync(new Mission("metadata") { RescueOfMissionId=original.Id,StallRecoveryAttempts=2,LastHeartbeatUtc=new DateTime(2026,11,1,1,2,3,DateTimeKind.Utc),BranchStrategy=MissionBranchStrategyEnum.ForkFromParent,BaseBranchName="feature/parent",Depth=3,OwnedPathsJson="[\"src/a\"]",ReadsPathsJson="[\"src/b\"]",ReadyUtc=DateTime.UtcNow.AddMinutes(-3),AssignedUtc=DateTime.UtcNow.AddMinutes(-2) },ct);
                    Mission read=(await db.Missions.ReadAsync(mission.Id,ct))!;AssertEqual(original.Id,read.RescueOfMissionId);AssertEqual(mission.BranchStrategy,read.BranchStrategy);AssertEqual(mission.OwnedPathsJson,read.OwnedPathsJson);AssertEqual(mission.ReadsPathsJson,read.ReadsPathsJson);AssertEqual(3,read.Depth);AssertEqual(2,read.StallRecoveryAttempts);AssertEqual(mission.LastHeartbeatUtc,read.LastHeartbeatUtc);AssertEqual("feature/parent",read.BaseBranchName);AssertTrue(read.ReadyUtc.HasValue&&read.AssignedUtc.HasValue);
                    read.BaseBranchName="updated";await db.Missions.UpdateAsync(read,ct);AssertEqual("updated",(await db.Missions.ReadAsync(read.Id,ct))!.BaseBranchName);
                    MissionWaitInterval first=await db.MissionWaitIntervals.CreateAsync(new() { MissionId=mission.Id,Gate=MissionWaitGateEnum.CaptainSlot,StartedUtc=DateTime.UtcNow.AddDays(-2),Detail="full" },ct);
                    AssertEqual(1,await db.MissionWaitIntervals.CloseOpenAsync(mission.Id,DateTime.UtcNow.AddDays(-1),ct));AssertEqual(0,await db.MissionWaitIntervals.CloseOpenAsync(mission.Id,DateTime.UtcNow,ct));
                    await db.MissionWaitIntervals.CreateAsync(new() { MissionId=mission.Id,Gate=MissionWaitGateEnum.EndpointSlot,Detail="endpoint" },ct);
                    AssertEqual(2,(await db.MissionWaitIntervals.EnumerateByMissionAsync(mission.Id,token:ct)).Count);
                    AssertEqual(0,(await db.MissionWaitIntervals.EnumerateByMissionAsync(mission.Id,"another-tenant",ct)).Count);
                    AssertEqual(1,await db.MissionWaitIntervals.DeleteExpiredAsync(DateTime.UtcNow.AddHours(-1),ct));
                    AssertEqual(MissionWaitGateEnum.EndpointSlot,(await db.MissionWaitIntervals.EnumerateByMissionAsync(mission.Id,token:ct)).Single().Gate);
                    await db.Missions.DeleteAsync("wrong-tenant",original.Id,ct);
                    AssertEqual(original.Id,(await db.Missions.ReadAsync(mission.Id,ct))!.RescueOfMissionId);
                    await db.Missions.DeleteAsync(original.Id,ct);
                    AssertNull((await db.Missions.ReadAsync(mission.Id,ct))!.RescueOfMissionId);
                    await db.Missions.DeleteAsync(mission.Id,ct);AssertEqual(0,(await db.MissionWaitIntervals.EnumerateByMissionAsync(mission.Id,token:ct)).Count);
                })
            };
            if (String.Equals(Environment.GetEnvironmentVariable("ARMADA_TEST_DB_TYPE"), "mysql", StringComparison.OrdinalIgnoreCase))
                cases.Add(new(suite, "mysql_interrupted_ddl", "MySQL resumes interrupted migrations 119, 120 and 121 without losing backfills", async ct =>
                {
                    foreach (int phase in new[] { 119, 120, 121 }) await VerifyUpgradeAsync(phase, ct);
                }));
            return new(suite, "Concurrency migration boundary", cases);
        }
        private static async Task VerifyUpgradeAsync(int? interruptedPhase, CancellationToken ct)
        {
            using IsolatedTestDatabase isolated = await IsolatedTestDatabase.CreateAsync("upgrade121", token: ct);
            LoggingModule log = new(); log.Settings.EnableConsole = false;
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
                await Execute(connection, ddl, ct);
                MethodInfo apply = db.GetType().GetMethod("ExecuteMigrationAsync", BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)!;
                foreach (SchemaMigration migration in db.GetMigrationsForVerification().Where(m => m.Version <= 116))
                {
                    using DbTransaction tx = await connection.BeginTransactionAsync(ct);
                    await (Task)apply.Invoke(db, new object[] { connection, tx, migration, ct })!;
                    using DbCommand record = connection.CreateCommand(); record.Transaction = tx;
                    record.CommandText = "INSERT INTO schema_migrations(version,description,applied_utc) VALUES(@version,@description,@utc);";
                    Add(record, "@version", migration.Version); Add(record, "@description", migration.Description);
                    Add(record, "@utc", isolated.Type == DatabaseTypeEnum.Sqlite ? DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) : DateTime.UtcNow);
                    await record.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
                }
                string yes = isolated.Type == DatabaseTypeEnum.Postgresql ? "TRUE" : "1", no = isolated.Type == DatabaseTypeEnum.Postgresql ? "FALSE" : "0";
                string stamp = isolated.Type == DatabaseTypeEnum.Mysql ? "'2026-11-01 00:00:00'" : "'2026-11-01T00:00:00Z'";
                await Execute(connection, $"INSERT INTO vessels(id,name,repo_url,allow_concurrent_missions,created_utc,last_update_utc) VALUES('vsl_upgrade','upgrade','https://example.invalid/repo',{no},{stamp},{stamp});", ct);
                await Execute(connection, $"INSERT INTO vessels(id,name,repo_url,allow_concurrent_missions,created_utc,last_update_utc) VALUES('vsl_parallel','parallel','https://example.invalid/parallel',{yes},{stamp},{stamp});", ct);
                await Execute(connection, $"INSERT INTO captains(id,name,runtime,state,current_mission_id,current_dock_id,process_id,created_utc,last_update_utc) VALUES('cpt_upgrade','worker','Mux','Working','msn_running','dck_upgrade',4242,{stamp},{stamp});", ct);
                await Execute(connection, $"INSERT INTO captains(id,name,runtime,state,current_mission_id,process_id,created_utc,last_update_utc) VALUES('cpt_session','planner','ClaudeCode','Planning','psn_legacy',8123,{stamp},{stamp});", ct);
                await Execute(connection, $"INSERT INTO docks(id,vessel_id,captain_id,created_utc,last_update_utc) VALUES('dck_upgrade','vsl_upgrade','cpt_upgrade',{stamp},{stamp});", ct);
                await Execute(connection, $"INSERT INTO missions(id,title,vessel_id,captain_id,dock_id,status,started_utc,created_utc,last_update_utc) VALUES('msn_running','running','vsl_upgrade','cpt_upgrade','dck_upgrade','InProgress',{stamp},{stamp},{stamp});", ct);
                await Execute(connection, $"INSERT INTO captains(id,name,runtime,state,current_mission_id,process_id,created_utc,last_update_utc) VALUES('cpt_testing','legacy testing','Mux','Working','msn_testing',5252,{stamp},{stamp});", ct);
                await Execute(connection, $"INSERT INTO missions(id,title,vessel_id,captain_id,status,started_utc,created_utc,last_update_utc) VALUES('msn_testing','testing','vsl_upgrade','cpt_testing','Testing',{stamp},{stamp},{stamp});", ct);
                await Execute(connection, $"INSERT INTO missions(id,title,vessel_id,status,created_utc,last_update_utc) VALUES('msn_original','failed','vsl_upgrade','Failed',{stamp},{stamp});", ct);
                await Execute(connection, $"INSERT INTO missions(id,title,vessel_id,status,is_rescue,parent_mission_id,created_utc,last_update_utc) VALUES('msn_rescue','rescue','vsl_upgrade','Pending',{yes},'msn_original',{stamp},{stamp});", ct);
            }
            AssertEqual(116, await db.GetSchemaVersionAsync(ct), provider);
            if (interruptedPhase.HasValue)
            {
                using DbConnection connection = Connection(isolated); await connection.OpenAsync(ct);
                MethodInfo apply = db.GetType().GetMethod("ExecuteMigrationAsync", BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)!;
                foreach (SchemaMigration migration in db.GetMigrationsForVerification().Where(m => m.Version > 116 && m.Version <= interruptedPhase.Value))
                {
                    using DbTransaction tx = await connection.BeginTransactionAsync(ct);
                    bool partial = migration.Version == interruptedPhase.Value;
                    SchemaMigration step = partial ? new SchemaMigration(migration.Version, "Interrupted DDL", migration.Statements.Take(migration.Statements.Count - 1).ToArray()) : migration;
                    await (Task)apply.Invoke(db, new object[] { connection, tx, step, ct })!;
                    if (!partial)
                    {
                        using DbCommand record = connection.CreateCommand(); record.Transaction = tx;
                        record.CommandText = "INSERT INTO schema_migrations(version,description,applied_utc) VALUES(@version,@description,@utc);";
                        Add(record, "@version", migration.Version); Add(record, "@description", migration.Description); Add(record, "@utc", DateTime.UtcNow);
                        await record.ExecuteNonQueryAsync(ct);
                    }
                    await tx.CommitAsync(ct);
                }
            }
            await db.InitializeAsync(ct); AssertEqual(db.GetLatestSchemaVersion(), await db.GetSchemaVersionAsync(ct), provider);
            Captain captain = (await db.Captains.ReadAsync("cpt_upgrade", ct))!;
            AssertEqual(4, captain.MaxConcurrency); AssertEqual(1, captain.ActiveMissionCount); AssertEqual("msn_running", captain.ActiveMissionIds.Single()); AssertNull(captain.ProcessId);
            Mission testing = (await db.Missions.ReadAsync("msn_testing", ct))!;
            AssertEqual(MissionStatusEnum.InProgress, testing.Status); AssertEqual(5252, testing.ProcessId);
            AssertEqual(5252, await db.Captains.ReadMissionProcessAsync("cpt_testing", testing.Id, ct));
            AssertEqual(1, (await db.Captains.ReadAsync("cpt_testing", ct))!.ActiveMissionCount);
            Mission running = (await db.Missions.ReadAsync("msn_running", ct))!;
            AssertEqual(4242, running.ProcessId); AssertEqual("dck_upgrade", running.DockId); AssertEqual(running.StartedUtc, running.AssignedUtc);
            AssertEqual(4242, await db.Captains.ReadMissionProcessAsync(captain.Id, running.Id, ct));
            Captain session = (await db.Captains.ReadAsync("cpt_session", ct))!;
            AssertEqual("psn_legacy", session.CurrentSessionId); AssertEqual(8123, session.ProcessId); AssertEqual(0, session.ActiveMissionCount);
            Vessel serial = (await db.Vessels.ReadAsync("vsl_upgrade", ct))!, parallel = (await db.Vessels.ReadAsync("vsl_parallel", ct))!;
            AssertEqual(1, serial.MaxParallelVoyages); AssertEqual(1, serial.MaxParallelMissionsPerVoyage); AssertEqual(0, parallel.MaxParallelVoyages); AssertEqual(0, parallel.MaxParallelMissionsPerVoyage);
            Mission rescue = (await db.Missions.ReadAsync("msn_rescue", ct))!; AssertEqual("msn_original", rescue.RescueOfMissionId); AssertNull(rescue.ParentMissionId);
            AssertEqual(MissionBranchStrategyEnum.New, rescue.BranchStrategy); AssertEqual(0, rescue.Depth);
            List<string> schema = await isolated.SnapshotSchemaAsync(ct);
            AssertFalse(schema.Any(line => line.Contains("current_mission_id", StringComparison.OrdinalIgnoreCase) || line.Contains("current_dock_id", StringComparison.OrdinalIgnoreCase) || line.Contains("allow_concurrent_missions", StringComparison.OrdinalIgnoreCase)), "removed columns absent");
            await db.InitializeAsync(ct); await db.ReplayMigrationsAsync(ct);
            AssertEqual("msn_original", (await db.Missions.ReadAsync("msn_rescue", ct))!.RescueOfMissionId);
        }
        private static DbConnection Connection(IsolatedTestDatabase db) => db.Type switch
        {
            DatabaseTypeEnum.Sqlite => new SqliteProviderConnection(db.ConnectionString),
            DatabaseTypeEnum.Postgresql => new NpgsqlConnection(db.ConnectionString),
            DatabaseTypeEnum.Mysql => new MySqlConnection(db.ConnectionString),
            _ => new SqlConnection(db.ConnectionString)
        };
        private static void Add(DbCommand cmd, string name, object value) { DbParameter p = cmd.CreateParameter(); p.ParameterName = name; p.Value = value; cmd.Parameters.Add(p); }
        private static async Task Execute(DbConnection c, string sql, CancellationToken ct) { using DbCommand cmd = c.CreateCommand(); cmd.CommandText = sql; await cmd.ExecuteNonQueryAsync(ct); }
    }
}
