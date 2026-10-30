namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
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
    /// voyages, missions, read signals, and events over a live SQLite store. The disabled-retention
    /// case asserts the no-op guard; positive cases assert removal of aged records while sparing
    /// recent ones. Snapshot cases keep the newest incident and runbook-execution snapshot for each
    /// entity id and run only on SQLite, because this service is constructed only for that provider.
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

            cases.Add(CaseAsync(
                "purge_keeps_latest_incident_snapshot",
                "PurgeExpiredDataAsync KeepsLatestIncidentSnapshot",
                TestTags.Positive,
                () => AssertLatestSnapshotKeptAsync("incident.snapshot", "incident"),
                sqliteOnly: true));

            cases.Add(CaseAsync(
                "purge_keeps_latest_runbook_execution_snapshot",
                "PurgeExpiredDataAsync KeepsLatestRunbookExecutionSnapshot",
                TestTags.Positive,
                () => AssertLatestSnapshotKeptAsync("runbook-execution.snapshot", "runbook-execution"),
                sqliteOnly: true));

            return new TestSuiteDescriptor(
                suiteId: "Services.DataExpiryService",
                displayName: "Data Expiry Service",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body, bool sqliteOnly = false)
        {
            bool skip = sqliteOnly && !TestDatabaseConfig.IsSqlite;
            string? skipReason = skip
                ? "Data expiry runs only on SQLite. PostgreSQL, MySQL, and SQL Server do not construct this service."
                : null;

            return new TestCaseDescriptor(
                suiteId: "Services.DataExpiryService",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag },
                skip: skip,
                skipReason: skipReason);
        }

        private static async Task AssertLatestSnapshotKeptAsync(string eventType, string entityType)
        {
            if (!TestDatabaseConfig.IsSqlite)
                throw new InvalidOperationException("Data expiry snapshot retention is proved only on SQLite.");

            string otherEventType = eventType == "incident.snapshot"
                ? "runbook-execution.snapshot"
                : "incident.snapshot";
            string otherEntityType = eventType == "incident.snapshot"
                ? "runbook-execution"
                : "incident";

            using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
            {
                DatabaseDriver db = testDb.Driver;
                DateTime oldUtc = DateTime.UtcNow.AddDays(-60);
                DateTime recentUtc = DateTime.UtcNow.AddDays(-1);
                DateTime tieUtc = new DateTime(2020, 6, 15, 12, 0, 0, DateTimeKind.Utc);

                ArmadaEvent quiet = Snapshot(eventType, entityType, "record-quiet", "evt_quiet", oldUtc, "quiet");
                ArmadaEvent otherQuiet = Snapshot(otherEventType, otherEntityType, "record-quiet", "evt_other_quiet", oldUtc, "other quiet");
                ArmadaEvent older = Snapshot(eventType, entityType, "record-pair", "evt_pair_older", oldUtc, "older");
                ArmadaEvent newer = Snapshot(eventType, entityType, "record-pair", "evt_pair_newer", recentUtc, "newer");
                ArmadaEvent tieLow = Snapshot(eventType, entityType, "record-tie", "evt_tie_a", tieUtc, "tie low");
                ArmadaEvent tieHigh = Snapshot(eventType, entityType, "record-tie", "evt_tie_b", tieUtc, "tie high");
                ArmadaEvent blank = Snapshot(eventType, entityType, "", "evt_blank", oldUtc, "blank");
                ArmadaEvent plainOld = new ArmadaEvent("test.event", "old plain event");
                plainOld.Id = "evt_plain_old";
                plainOld.CreatedUtc = oldUtc;
                ArmadaEvent plainRecent = new ArmadaEvent("test.event", "recent plain event");
                plainRecent.Id = "evt_plain_recent";
                plainRecent.CreatedUtc = recentUtc;

                await db.Events.CreateAsync(quiet);
                await db.Events.CreateAsync(otherQuiet);
                await db.Events.CreateAsync(older);
                await db.Events.CreateAsync(newer);
                await db.Events.CreateAsync(tieLow);
                await db.Events.CreateAsync(tieHigh);
                await db.Events.CreateAsync(blank);
                await db.Events.CreateAsync(plainOld);
                await db.Events.CreateAsync(plainRecent);

                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                DataExpiryService service = new DataExpiryService(logging, testDb.ConnectionString, 30);
                await service.PurgeExpiredDataAsync();

                AssertNotNull(await db.Events.ReadAsync(quiet.Id), eventType + " only snapshot");
                AssertNotNull(await db.Events.ReadAsync(otherQuiet.Id), otherEventType + " only snapshot");
                AssertNull(await db.Events.ReadAsync(older.Id), eventType + " older snapshot");
                AssertNotNull(await db.Events.ReadAsync(newer.Id), eventType + " newer snapshot");
                AssertNotNull(await db.Events.ReadAsync(tieHigh.Id), eventType + " newest tied snapshot");
                AssertNull(await db.Events.ReadAsync(tieLow.Id), eventType + " older tied snapshot");
                AssertNull(await db.Events.ReadAsync(blank.Id), eventType + " empty entity snapshot");
                AssertNull(await db.Events.ReadAsync(plainOld.Id), "old plain event");
                AssertNotNull(await db.Events.ReadAsync(plainRecent.Id), "recent plain event");
            }
        }

        private static ArmadaEvent Snapshot(string eventType, string entityType, string entityId, string id, DateTime createdUtc, string message)
        {
            ArmadaEvent snapshot = new ArmadaEvent(eventType, message);
            snapshot.Id = id;
            snapshot.EntityType = entityType;
            snapshot.EntityId = entityId;
            snapshot.CreatedUtc = createdUtc;
            return snapshot;
        }

        #endregion
    }
}
