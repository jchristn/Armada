namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="DataExpiryService"/>: retention-driven purge of old completed
    /// voyages, missions, read signals, and events on the configured database provider. The
    /// disabled-retention case asserts the no-op guard. Positive cases assert removal of aged
    /// records, and the snapshot case asserts that the newest objective snapshot survives.
    /// </summary>
    public sealed class DataExpiryServiceSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the DataExpiryService suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("purge_disabled_when_retention_zero", "PurgeExpiredDataAsync DisabledWhenRetentionZero", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    DataExpiryService service = new DataExpiryService(logging, "Data Source=dummy;Mode=Memory;Cache=Shared", 0);
                    int deleted = await service.PurgeExpiredDataAsync();
                    AssertEqual(0, deleted);
                }
            }));

            cases.Add(CaseAsync("retention_live_update", "Changing active retention preserves screenshot history and validates negative input", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Mission mission = new Mission { Title = "Synthetic retention test", Status = MissionStatusEnum.Complete, CreatedUtc = DateTime.UtcNow.AddDays(-46), CompletedUtc = DateTime.UtcNow.AddDays(-45) };
                    await testDb.Driver.Missions.CreateAsync(mission);
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;
                    DataExpiryService service = new DataExpiryService(logging, testDb.ConnectionString, 30);
                    service.RetentionDays = 60;
                    await service.PurgeExpiredDataAsync();
                    AssertNotNull(await testDb.Driver.Missions.ReadAsync(mission.Id));
                    bool rejected = false;
                    try { service.RetentionDays = -1; } catch (ArgumentOutOfRangeException) { rejected = true; }
                    AssertTrue(rejected);
                    service.RetentionDays = 30;
                    await service.PurgeExpiredDataAsync();
                    AssertNull(await testDb.Driver.Missions.ReadAsync(mission.Id));
                }
            }));

            cases.Add(CaseAsync("purge_removes_old_completed_voyages_and_missions", "PurgeExpiredDataAsync RemovesOldCompletedVoyagesAndMissions", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    Voyage oldVoyage = new Voyage("Old Voyage");
                    oldVoyage.Status = VoyageStatusEnum.Complete;
                    oldVoyage.CompletedUtc = DateTime.UtcNow.AddDays(-60);
                    await db.Voyages.CreateAsync(oldVoyage);

                    Mission oldMission = new Mission("Old Mission");
                    oldMission.VoyageId = oldVoyage.Id;
                    oldMission.Status = MissionStatusEnum.Complete;
                    oldMission.CompletedUtc = DateTime.UtcNow.AddDays(-60);
                    await db.Missions.CreateAsync(oldMission);

                    Voyage recentVoyage = new Voyage("Recent Voyage");
                    recentVoyage.Status = VoyageStatusEnum.Complete;
                    recentVoyage.CompletedUtc = DateTime.UtcNow.AddDays(-5);
                    await db.Voyages.CreateAsync(recentVoyage);

                    DataExpiryService service = new DataExpiryService(logging, testDb.ConnectionString, 30);
                    int deleted = await service.PurgeExpiredDataAsync();

                    AssertTrue(deleted > 0);

                    AssertNull(await db.Voyages.ReadAsync(oldVoyage.Id));
                    AssertNull(await db.Missions.ReadAsync(oldMission.Id));

                    AssertNotNull(await db.Voyages.ReadAsync(recentVoyage.Id));
                }
            }));

            cases.Add(CaseAsync("purge_removes_old_read_signals", "PurgeExpiredDataAsync RemovesOldReadSignals", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    Signal oldSignal = new Signal(SignalTypeEnum.Nudge, "old");
                    oldSignal.Read = true;
                    oldSignal.CreatedUtc = DateTime.UtcNow.AddDays(-60);
                    await db.Signals.CreateAsync(oldSignal);

                    Signal recentSignal = new Signal(SignalTypeEnum.Nudge, "recent");
                    await db.Signals.CreateAsync(recentSignal);

                    DataExpiryService service = new DataExpiryService(logging, testDb.ConnectionString, 30);
                    await service.PurgeExpiredDataAsync();

                    AssertNotNull(await db.Signals.ReadAsync(recentSignal.Id));
                }
            }));

            cases.Add(CaseAsync("purge_removes_old_events", "PurgeExpiredDataAsync RemovesOldEvents", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    ArmadaEvent oldEvent = new ArmadaEvent("test.event", "Old event");
                    oldEvent.CreatedUtc = DateTime.UtcNow.AddDays(-60);
                    await db.Events.CreateAsync(oldEvent);

                    ArmadaEvent recentEvent = new ArmadaEvent("test.event", "Recent event");
                    await db.Events.CreateAsync(recentEvent);

                    DataExpiryService service = new DataExpiryService(logging, testDb.ConnectionString, 30);
                    await service.PurgeExpiredDataAsync();

                    List<ArmadaEvent> remaining = await db.Events.EnumerateRecentAsync();
                    AssertTrue(remaining.Count >= 1);
                }
            }));

            cases.Add(CaseAsync("purge_zero_retention_keeps_old_voyage", "Zero retention deletes nothing on the active provider", TestTags.Negative, AssertZeroRetentionKeepsOldVoyageAsync));

            cases.Add(CaseAsync("purge_keeps_latest_objective_snapshot", "Purge keeps the newest objective snapshot and still deletes a normal old event", TestTags.Positive, AssertLatestObjectiveSnapshotSurvivesAsync));

            cases.Add(CaseAsync("purge_old_completed_voyage_postgresql", "Positive retention deletes an old completed voyage on PostgreSQL", TestTags.Positive, AssertOldVoyagePurgedAsync, DatabaseTypeEnum.Postgresql));
            cases.Add(CaseAsync("purge_old_completed_voyage_mysql", "Positive retention deletes an old completed voyage on MySQL", TestTags.Positive, AssertOldVoyagePurgedAsync, DatabaseTypeEnum.Mysql));
            cases.Add(CaseAsync("purge_old_completed_voyage_sqlserver", "Positive retention deletes an old completed voyage on SQL Server", TestTags.Positive, AssertOldVoyagePurgedAsync, DatabaseTypeEnum.SqlServer));
            cases.Add(CaseAsync("sqlserver_clears_parent_before_mission_delete", "SQL Server clears parent_mission_id before deleting an expired parent mission", TestTags.Positive, AssertSqlServerParentClearedAsync, DatabaseTypeEnum.SqlServer));

            return new TestSuiteDescriptor(
                suiteId: "Services.DataExpiryService",
                displayName: "Data Expiry Service",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body, DatabaseTypeEnum? requiredProvider = null)
        {
            bool skip = requiredProvider.HasValue && TestDatabaseConfig.Type != requiredProvider.Value;
            string? skipReason = null;
            if (skip)
            {
                skipReason = "Runs only when ARMADA_TEST_DB_TYPE is " + ProviderToken(requiredProvider!.Value) + ". A skip is not proof for that provider.";
            }

            return new TestCaseDescriptor(
                suiteId: "Services.DataExpiryService",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag },
                skip: skip,
                skipReason: skipReason);
        }

        private static string ProviderToken(DatabaseTypeEnum provider)
        {
            switch (provider)
            {
                case DatabaseTypeEnum.Postgresql: return "postgresql";
                case DatabaseTypeEnum.Mysql: return "mysql";
                case DatabaseTypeEnum.SqlServer: return "sqlserver";
                default: return "sqlite";
            }
        }

        private static LoggingModule QuietLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static async Task AssertZeroRetentionKeepsOldVoyageAsync()
        {
            using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
            {
                Voyage oldVoyage = new Voyage("Old voyage kept when retention is zero");
                oldVoyage.Status = VoyageStatusEnum.Complete;
                oldVoyage.CompletedUtc = DateTime.UtcNow.AddDays(-60);
                await testDb.Driver.Voyages.CreateAsync(oldVoyage);

                DataExpiryService service = new DataExpiryService(QuietLogging(), testDb.ConnectionString, 0);
                int deleted = await service.PurgeExpiredDataAsync();

                AssertEqual(0, deleted, "zero retention");
                AssertNotNull(await testDb.Driver.Voyages.ReadAsync(oldVoyage.Id), "old voyage remains");
            }
        }

        private static async Task AssertOldVoyagePurgedAsync()
        {
            using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
            {
                DatabaseDriver db = testDb.Driver;
                Voyage oldVoyage = new Voyage("Old completed voyage");
                oldVoyage.Status = VoyageStatusEnum.Complete;
                oldVoyage.CompletedUtc = DateTime.UtcNow.AddDays(-60);
                await db.Voyages.CreateAsync(oldVoyage);

                Mission oldMission = new Mission("Old completed mission");
                oldMission.VoyageId = oldVoyage.Id;
                oldMission.Status = MissionStatusEnum.Complete;
                oldMission.CompletedUtc = DateTime.UtcNow.AddDays(-60);
                await db.Missions.CreateAsync(oldMission);

                Voyage recentVoyage = new Voyage("Recent completed voyage");
                recentVoyage.Status = VoyageStatusEnum.Complete;
                recentVoyage.CompletedUtc = DateTime.UtcNow.AddDays(-5);
                await db.Voyages.CreateAsync(recentVoyage);

                DataExpiryService service = new DataExpiryService(QuietLogging(), testDb.ConnectionString, 30);
                await service.PurgeExpiredDataAsync();

                AssertNull(await db.Voyages.ReadAsync(oldVoyage.Id), "old voyage purged");
                AssertNull(await db.Missions.ReadAsync(oldMission.Id), "old mission purged");
                AssertNotNull(await db.Voyages.ReadAsync(recentVoyage.Id), "recent voyage kept");
            }
        }

        private static async Task AssertLatestObjectiveSnapshotSurvivesAsync()
        {
            using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
            {
                DatabaseDriver db = testDb.Driver;
                DateTime supersededUtc = DateTime.UtcNow.AddDays(-50);
                DateTime latestBusyUtc = DateTime.UtcNow.AddDays(-40);
                DateTime quietUtc = DateTime.UtcNow.AddDays(-45);

                Objective busy = new Objective();
                busy.Title = "Latest busy snapshot";
                busy.LastUpdateUtc = latestBusyUtc;
                Objective busyOld = new Objective();
                busyOld.Id = busy.Id;
                busyOld.Title = "Superseded snapshot";
                busyOld.LastUpdateUtc = supersededUtc;
                Objective quiet = new Objective();
                quiet.Title = "Quiet objective";
                quiet.LastUpdateUtc = quietUtc;

                ArmadaEvent superseded = await db.Events.CreateAsync(Snapshot(busyOld, supersededUtc));
                ArmadaEvent latestBusy = await db.Events.CreateAsync(Snapshot(busy, latestBusyUtc));
                ArmadaEvent quietSnapshot = await db.Events.CreateAsync(Snapshot(quiet, quietUtc));
                ArmadaEvent oldEvent = await db.Events.CreateAsync(new ArmadaEvent("test.event", "Old normal event")
                {
                    CreatedUtc = DateTime.UtcNow.AddDays(-60)
                });
                ArmadaEvent recentEvent = await db.Events.CreateAsync(new ArmadaEvent("test.event", "Recent normal event"));

                DataExpiryService service = new DataExpiryService(QuietLogging(), testDb.ConnectionString, 30);
                await service.PurgeExpiredDataAsync();

                AssertNotNull(await db.Events.ReadAsync(quietSnapshot.Id), "only snapshot of a quiet objective remains");
                AssertNotNull(await db.Events.ReadAsync(latestBusy.Id), "newer snapshot remains");
                AssertNull(await db.Events.ReadAsync(superseded.Id), "superseded snapshot purged");
                AssertNull(await db.Events.ReadAsync(oldEvent.Id), "normal old event purged");
                AssertNotNull(await db.Events.ReadAsync(recentEvent.Id), "recent event kept");

                AuthContext auth = AuthContext.Authenticated("ten_expiry", "usr_expiry", true, false, "UnitTest");
                ObjectiveService objectives = new ObjectiveService(db);
                Objective? restoredQuiet = await objectives.ReadAsync(auth, quiet.Id);
                AssertNotNull(restoredQuiet, "quiet objective rehydrates");
                AssertEqual("Quiet objective", restoredQuiet.Title, "quiet objective title");
                Objective? restoredBusy = await objectives.ReadAsync(auth, busy.Id);
                AssertNotNull(restoredBusy, "busy objective rehydrates");
                AssertEqual("Latest busy snapshot", restoredBusy.Title, "busy objective title");
            }
        }

        private static ArmadaEvent Snapshot(Objective objective, DateTime createdUtc)
        {
            return new ArmadaEvent
            {
                EventType = "objective.snapshot",
                EntityType = "objective",
                EntityId = objective.Id,
                Message = objective.Title,
                Payload = JsonSerializer.Serialize(objective),
                CreatedUtc = createdUtc
            };
        }

        private static async Task AssertSqlServerParentClearedAsync()
        {
            using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
            {
                DatabaseDriver db = testDb.Driver;
                Mission parent = new Mission("Expired parent mission");
                parent.Status = MissionStatusEnum.Complete;
                parent.CreatedUtc = DateTime.UtcNow.AddDays(-61);
                parent.CompletedUtc = DateTime.UtcNow.AddDays(-60);
                await db.Missions.CreateAsync(parent);

                Mission child = new Mission("Live child mission");
                child.ParentMissionId = parent.Id;
                child.Status = MissionStatusEnum.Pending;
                await db.Missions.CreateAsync(child);

                DataExpiryService service = new DataExpiryService(QuietLogging(), testDb.ConnectionString, 30);
                await service.PurgeExpiredDataAsync();

                AssertNull(await db.Missions.ReadAsync(parent.Id), "expired parent purged");
                Mission? kept = await db.Missions.ReadAsync(child.Id);
                AssertNotNull(kept, "child mission remains");
                AssertNull(kept.ParentMissionId, "parent reference cleared");
            }
        }

        #endregion
    }
}
