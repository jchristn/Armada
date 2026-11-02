namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the event database methods: create, recent enumeration with a limit, and
    /// filtered enumeration by type, entity, captain, mission, vessel, and voyage. Includes a
    /// negative case confirming that filtering by an unknown correlation id yields no rows.
    /// Reads also return a stored user id and keep a null user id null. A user-scoped read
    /// returns only that user's rows. Each case runs against its own fresh store.
    /// </summary>
    public sealed class EventDatabaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.EventDatabase";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Event Database suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("create_async_returns_event", "CreateAsync returns event", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    ArmadaEvent evt = new ArmadaEvent("mission.created", "Mission created");
                    ArmadaEvent result = await db.Events.CreateAsync(evt);

                    AssertNotNull(result);
                    AssertEqual("mission.created", result.EventType);
                }
            }));

            cases.Add(CaseAsync("enumerate_recent_async_returns_limited", "EnumerateRecentAsync returns limited", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    for (int i = 0; i < 10; i++)
                    {
                        await db.Events.CreateAsync(new ArmadaEvent("test.event", "Event " + i));
                    }

                    List<ArmadaEvent> recent = await db.Events.EnumerateRecentAsync(5);
                    AssertEqual(5, recent.Count);
                }
            }));

            cases.Add(CaseAsync("enumerate_by_type_async_filters_correctly", "EnumerateByTypeAsync filters correctly", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    await db.Events.CreateAsync(new ArmadaEvent("mission.created", "Created"));
                    await db.Events.CreateAsync(new ArmadaEvent("mission.completed", "Completed"));
                    await db.Events.CreateAsync(new ArmadaEvent("mission.created", "Created 2"));

                    List<ArmadaEvent> created = await db.Events.EnumerateByTypeAsync("mission.created");
                    AssertEqual(2, created.Count);
                }
            }));

            cases.Add(CaseAsync("enumerate_by_entity_async_filters_correctly", "EnumerateByEntityAsync filters correctly", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    ArmadaEvent evt1 = new ArmadaEvent("mission.created", "Created");
                    evt1.EntityType = "mission";
                    evt1.EntityId = "msn_abc";
                    await db.Events.CreateAsync(evt1);

                    ArmadaEvent evt2 = new ArmadaEvent("captain.launched", "Launched");
                    evt2.EntityType = "captain";
                    evt2.EntityId = "cpt_abc";
                    await db.Events.CreateAsync(evt2);

                    List<ArmadaEvent> missionEvents = await db.Events.EnumerateByEntityAsync("mission", "msn_abc");
                    AssertEqual(1, missionEvents.Count);
                    AssertEqual("mission.created", missionEvents[0].EventType);
                }
            }));

            cases.Add(CaseAsync("enumerate_by_captain_async_filters_correctly", "EnumerateByCaptainAsync filters correctly", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    ArmadaEvent evt1 = new ArmadaEvent("captain.launched", "Launched");
                    evt1.CaptainId = "cpt_test";
                    await db.Events.CreateAsync(evt1);

                    ArmadaEvent evt2 = new ArmadaEvent("captain.launched", "Other");
                    evt2.CaptainId = "cpt_other";
                    await db.Events.CreateAsync(evt2);

                    List<ArmadaEvent> events = await db.Events.EnumerateByCaptainAsync("cpt_test");
                    AssertEqual(1, events.Count);
                }
            }));

            cases.Add(CaseAsync("enumerate_by_mission_async_filters_correctly", "EnumerateByMissionAsync filters correctly", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    ArmadaEvent evt = new ArmadaEvent("mission.updated", "Updated");
                    evt.MissionId = "msn_target";
                    await db.Events.CreateAsync(evt);

                    await db.Events.CreateAsync(new ArmadaEvent("other", "Other"));

                    List<ArmadaEvent> events = await db.Events.EnumerateByMissionAsync("msn_target");
                    AssertEqual(1, events.Count);
                }
            }));

            cases.Add(CaseAsync("enumerate_by_vessel_async_filters_correctly", "EnumerateByVesselAsync filters correctly", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    ArmadaEvent evt = new ArmadaEvent("vessel.event", "Vessel event");
                    evt.VesselId = "vsl_target";
                    await db.Events.CreateAsync(evt);

                    List<ArmadaEvent> events = await db.Events.EnumerateByVesselAsync("vsl_target");
                    AssertEqual(1, events.Count);
                }
            }));

            cases.Add(CaseAsync("enumerate_by_voyage_async_filters_correctly", "EnumerateByVoyageAsync filters correctly", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    ArmadaEvent evt = new ArmadaEvent("voyage.completed", "Completed");
                    evt.VoyageId = "vyg_target";
                    await db.Events.CreateAsync(evt);

                    List<ArmadaEvent> events = await db.Events.EnumerateByVoyageAsync("vyg_target");
                    AssertEqual(1, events.Count);
                }
            }));

            cases.Add(CaseAsync("read_async_returns_stored_user_id", "ReadAsync returns stored user id", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    UserMaster owner = await db.Users.CreateAsync(new UserMaster(Constants.DefaultTenantId, "owner-a@example.com", "password"));
                    UserMaster other = await db.Users.CreateAsync(new UserMaster(Constants.DefaultTenantId, "owner-b@example.com", "password"));

                    ArmadaEvent owned = new ArmadaEvent("owner.recorded", "Owned event");
                    owned.TenantId = Constants.DefaultTenantId;
                    owned.UserId = owner.Id;
                    await db.Events.CreateAsync(owned);

                    ArmadaEvent otherOwned = new ArmadaEvent("owner.recorded", "Other event");
                    otherOwned.TenantId = Constants.DefaultTenantId;
                    otherOwned.UserId = other.Id;
                    await db.Events.CreateAsync(otherOwned);

                    ArmadaEvent? read = await db.Events.ReadAsync(owned.Id);
                    AssertNotNull(read);
                    AssertEqual(owner.Id, read!.UserId);

                    ArmadaEvent? scoped = await db.Events.ReadAsync(Constants.DefaultTenantId, owner.Id, owned.Id);
                    AssertNotNull(scoped);
                    AssertEqual(owner.Id, scoped!.UserId);

                    List<ArmadaEvent> ownerRows = await db.Events.EnumerateAsync(Constants.DefaultTenantId, owner.Id);
                    AssertEqual(1, ownerRows.Count);
                    AssertEqual(owned.Id, ownerRows[0].Id);
                    AssertEqual(owner.Id, ownerRows[0].UserId);

                    List<ArmadaEvent> otherRows = await db.Events.EnumerateAsync(Constants.DefaultTenantId, other.Id);
                    AssertEqual(1, otherRows.Count);
                    AssertEqual(otherOwned.Id, otherRows[0].Id);
                    AssertEqual(other.Id, otherRows[0].UserId);

                    ArmadaEvent? hidden = await db.Events.ReadAsync(Constants.DefaultTenantId, other.Id, owned.Id);
                    AssertNull(hidden);
                }
            }));

            cases.Add(CaseAsync("read_async_null_user_id_stays_null", "ReadAsync null user id stays null", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    ArmadaEvent evt = new ArmadaEvent("owner.absent", "Event without an owner");
                    evt.TenantId = Constants.DefaultTenantId;
                    evt.UserId = null;
                    await db.Events.CreateAsync(evt);

                    ArmadaEvent? read = await db.Events.ReadAsync(evt.Id);
                    AssertNotNull(read);
                    AssertNull(read!.UserId);

                    List<ArmadaEvent> defaultUserRows = await db.Events.EnumerateAsync(Constants.DefaultTenantId, Constants.DefaultUserId);
                    bool found = false;
                    foreach (ArmadaEvent row in defaultUserRows)
                    {
                        if (row.Id == evt.Id) found = true;
                    }
                    AssertFalse(found);
                }
            }));

            // Audit addition: filtering by an unknown correlation id must return an empty set.
            cases.Add(CaseAsync("enumerate_by_mission_async_unknown_returns_empty", "EnumerateByMissionAsync unknown returns empty", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    ArmadaEvent evt = new ArmadaEvent("mission.updated", "Updated");
                    evt.MissionId = "msn_present";
                    await db.Events.CreateAsync(evt);

                    List<ArmadaEvent> events = await db.Events.EnumerateByMissionAsync("msn_absent");
                    AssertEqual(0, events.Count);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Event Database",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
