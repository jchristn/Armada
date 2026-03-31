namespace Armada.Test.Unit.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    public class EventTests : TestSuite
    {
        public override string Name => "Event Tests";

        #region Private-Methods

        private static ArmadaEvent CreateEvent(
            string eventType,
            string message,
            string? entityType = null,
            string? entityId = null,
            string? captainId = null,
            string? missionId = null,
            string? vesselId = null,
            string? voyageId = null,
            DateTime? createdUtc = null)
        {
            ArmadaEvent evt = new ArmadaEvent(eventType, message);
            evt.EntityType = entityType;
            evt.EntityId = entityId;
            evt.CaptainId = captainId;
            evt.MissionId = missionId;
            evt.VesselId = vesselId;
            evt.VoyageId = voyageId;
            if (createdUtc.HasValue) evt.CreatedUtc = createdUtc.Value;
            return evt;
        }

        #endregion

        protected override async Task RunTestsAsync()
        {
            await RunTest("Event_Create", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    ArmadaEvent evt = CreateEvent(
                        "mission.created",
                        "Mission was created",
                        entityType: "mission",
                        entityId: "msn_test123",
                        captainId: "cpt_abc",
                        missionId: "msn_test123",
                        vesselId: "vsl_repo1",
                        voyageId: "vyg_batch1");

                    ArmadaEvent result = await db.Events.CreateAsync(evt);

                    AssertNotNull(result);
                    AssertStartsWith("evt_", result.Id);
                    AssertEqual("mission.created", result.EventType);
                    AssertEqual("Mission was created", result.Message);
                    AssertEqual("mission", result.EntityType);
                    AssertEqual("msn_test123", result.EntityId);
                    AssertEqual("cpt_abc", result.CaptainId);
                    AssertEqual("msn_test123", result.MissionId);
                    AssertEqual("vsl_repo1", result.VesselId);
                    AssertEqual("vyg_batch1", result.VoyageId);
                }
            });

            await RunTest("Event_EnumerateRecent", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    DateTime baseTime = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);

                    // Create 10 events with ascending timestamps
                    for (int i = 0; i < 10; i++)
                    {
                        ArmadaEvent evt = CreateEvent(
                            "test.event",
                            "Event " + i,
                            createdUtc: baseTime.AddMinutes(i));
                        await db.Events.CreateAsync(evt);
                    }

                    // Request only 5 most recent
                    List<ArmadaEvent> recent = await db.Events.EnumerateRecentAsync(5);
                    AssertEqual(5, recent.Count);

                    // Verify ordering: most recent first (descending by CreatedUtc)
                    for (int i = 0; i < recent.Count - 1; i++)
                    {
                        AssertTrue(recent[i].CreatedUtc >= recent[i + 1].CreatedUtc);
                    }
                }
            });

            await RunTest("Event_EnumerateByType", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    await db.Events.CreateAsync(CreateEvent("mission.created", "Created 1"));
                    await db.Events.CreateAsync(CreateEvent("mission.completed", "Completed 1"));
                    await db.Events.CreateAsync(CreateEvent("mission.created", "Created 2"));
                    await db.Events.CreateAsync(CreateEvent("captain.launched", "Launched 1"));
                    await db.Events.CreateAsync(CreateEvent("mission.created", "Created 3"));

                    List<ArmadaEvent> created = await db.Events.EnumerateByTypeAsync("mission.created");
                    AssertEqual(3, created.Count);

                    foreach (ArmadaEvent evt in created)
                    {
                        AssertEqual("mission.created", evt.EventType);
                    }

                    List<ArmadaEvent> completed = await db.Events.EnumerateByTypeAsync("mission.completed");
                    AssertEqual(1, completed.Count);

                    List<ArmadaEvent> none = await db.Events.EnumerateByTypeAsync("nonexistent.type");
                    AssertEqual(0, none.Count);
                }
            });

            await RunTest("Event_EnumerateByEntity", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    await db.Events.CreateAsync(CreateEvent("mission.created", "M1", entityType: "mission", entityId: "msn_aaa"));
                    await db.Events.CreateAsync(CreateEvent("mission.updated", "M2", entityType: "mission", entityId: "msn_aaa"));
                    await db.Events.CreateAsync(CreateEvent("captain.launched", "C1", entityType: "captain", entityId: "cpt_bbb"));
                    await db.Events.CreateAsync(CreateEvent("voyage.completed", "V1", entityType: "voyage", entityId: "vyg_ccc"));

                    List<ArmadaEvent> missionEvents = await db.Events.EnumerateByEntityAsync("mission", "msn_aaa");
                    AssertEqual(2, missionEvents.Count);
                    foreach (ArmadaEvent evt in missionEvents)
                    {
                        AssertEqual("mission", evt.EntityType);
                        AssertEqual("msn_aaa", evt.EntityId);
                    }

                    List<ArmadaEvent> captainEvents = await db.Events.EnumerateByEntityAsync("captain", "cpt_bbb");
                    AssertEqual(1, captainEvents.Count);

                    List<ArmadaEvent> noMatch = await db.Events.EnumerateByEntityAsync("mission", "msn_zzz");
                    AssertEqual(0, noMatch.Count);
                }
            });

            await RunTest("Event_EnumerateByCaptain", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    await db.Events.CreateAsync(CreateEvent("captain.launched", "L1", captainId: "cpt_alpha"));
                    await db.Events.CreateAsync(CreateEvent("captain.stalled", "S1", captainId: "cpt_alpha"));
                    await db.Events.CreateAsync(CreateEvent("captain.launched", "L2", captainId: "cpt_beta"));
                    await db.Events.CreateAsync(CreateEvent("mission.created", "M1"));

                    List<ArmadaEvent> alphaEvents = await db.Events.EnumerateByCaptainAsync("cpt_alpha");
                    AssertEqual(2, alphaEvents.Count);
                    foreach (ArmadaEvent evt in alphaEvents)
                    {
                        AssertEqual("cpt_alpha", evt.CaptainId);
                    }

                    List<ArmadaEvent> betaEvents = await db.Events.EnumerateByCaptainAsync("cpt_beta");
                    AssertEqual(1, betaEvents.Count);

                    List<ArmadaEvent> noMatch = await db.Events.EnumerateByCaptainAsync("cpt_nonexistent");
                    AssertEqual(0, noMatch.Count);
                }
            });

            await RunTest("Event_EnumerateByMission", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    await db.Events.CreateAsync(CreateEvent("mission.created", "M1", missionId: "msn_target"));
                    await db.Events.CreateAsync(CreateEvent("mission.updated", "M2", missionId: "msn_target"));
                    await db.Events.CreateAsync(CreateEvent("mission.completed", "M3", missionId: "msn_target"));
                    await db.Events.CreateAsync(CreateEvent("mission.created", "M4", missionId: "msn_other"));
                    await db.Events.CreateAsync(CreateEvent("captain.launched", "C1"));

                    List<ArmadaEvent> targetEvents = await db.Events.EnumerateByMissionAsync("msn_target");
                    AssertEqual(3, targetEvents.Count);
                    foreach (ArmadaEvent evt in targetEvents)
                    {
                        AssertEqual("msn_target", evt.MissionId);
                    }

                    List<ArmadaEvent> otherEvents = await db.Events.EnumerateByMissionAsync("msn_other");
                    AssertEqual(1, otherEvents.Count);

                    List<ArmadaEvent> noMatch = await db.Events.EnumerateByMissionAsync("msn_nonexistent");
                    AssertEqual(0, noMatch.Count);
                }
            });

            await RunTest("Event_EnumerateByVessel", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    await db.Events.CreateAsync(CreateEvent("vessel.updated", "V1", vesselId: "vsl_repo1"));
                    await db.Events.CreateAsync(CreateEvent("mission.created", "V2", vesselId: "vsl_repo1"));
                    await db.Events.CreateAsync(CreateEvent("vessel.updated", "V3", vesselId: "vsl_repo2"));
                    await db.Events.CreateAsync(CreateEvent("captain.launched", "C1"));

                    List<ArmadaEvent> repo1Events = await db.Events.EnumerateByVesselAsync("vsl_repo1");
                    AssertEqual(2, repo1Events.Count);
                    foreach (ArmadaEvent evt in repo1Events)
                    {
                        AssertEqual("vsl_repo1", evt.VesselId);
                    }

                    List<ArmadaEvent> repo2Events = await db.Events.EnumerateByVesselAsync("vsl_repo2");
                    AssertEqual(1, repo2Events.Count);

                    List<ArmadaEvent> noMatch = await db.Events.EnumerateByVesselAsync("vsl_nonexistent");
                    AssertEqual(0, noMatch.Count);
                }
            });

            await RunTest("Event_EnumerateByVoyage", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    await db.Events.CreateAsync(CreateEvent("voyage.created", "V1", voyageId: "vyg_batch1"));
                    await db.Events.CreateAsync(CreateEvent("mission.dispatched", "V2", voyageId: "vyg_batch1"));
                    await db.Events.CreateAsync(CreateEvent("voyage.completed", "V3", voyageId: "vyg_batch1"));
                    await db.Events.CreateAsync(CreateEvent("voyage.created", "V4", voyageId: "vyg_batch2"));
                    await db.Events.CreateAsync(CreateEvent("other.event", "O1"));

                    List<ArmadaEvent> batch1Events = await db.Events.EnumerateByVoyageAsync("vyg_batch1");
                    AssertEqual(3, batch1Events.Count);
                    foreach (ArmadaEvent evt in batch1Events)
                    {
                        AssertEqual("vyg_batch1", evt.VoyageId);
                    }

                    List<ArmadaEvent> batch2Events = await db.Events.EnumerateByVoyageAsync("vyg_batch2");
                    AssertEqual(1, batch2Events.Count);

                    List<ArmadaEvent> noMatch = await db.Events.EnumerateByVoyageAsync("vyg_nonexistent");
                    AssertEqual(0, noMatch.Count);
                }
            });

            await RunTest("Event_EnumeratePaginated", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    DateTime baseTime = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);

                    // Create 15 events with different types and ascending timestamps
                    for (int i = 0; i < 15; i++)
                    {
                        string eventType = i % 3 == 0 ? "mission.created" : (i % 3 == 1 ? "captain.launched" : "voyage.completed");
                        ArmadaEvent evt = CreateEvent(
                            eventType,
                            "Event " + i,
                            captainId: "cpt_pag",
                            createdUtc: baseTime.AddMinutes(i));
                        await db.Events.CreateAsync(evt);
                    }

                    // Page 1 of size 5
                    EnumerationQuery query = new EnumerationQuery();
                    query.PageNumber = 1;
                    query.PageSize = 5;
                    query.Order = EnumerationOrderEnum.CreatedDescending;

                    EnumerationResult<ArmadaEvent> page1 = await db.Events.EnumerateAsync(query);
                    AssertTrue(page1.Success);
                    AssertEqual(5, page1.Objects.Count);
                    AssertEqual(1, page1.PageNumber);
                    AssertEqual(5, page1.PageSize);
                    AssertEqual(15, (int)page1.TotalRecords);
                    AssertEqual(3, page1.TotalPages);

                    // Page 2
                    EnumerationQuery query2 = new EnumerationQuery();
                    query2.PageNumber = 2;
                    query2.PageSize = 5;
                    query2.Order = EnumerationOrderEnum.CreatedDescending;

                    EnumerationResult<ArmadaEvent> page2 = await db.Events.EnumerateAsync(query2);
                    AssertEqual(5, page2.Objects.Count);
                    AssertEqual(2, page2.PageNumber);

                    // Page 3
                    EnumerationQuery query3 = new EnumerationQuery();
                    query3.PageNumber = 3;
                    query3.PageSize = 5;
                    query3.Order = EnumerationOrderEnum.CreatedDescending;

                    EnumerationResult<ArmadaEvent> page3 = await db.Events.EnumerateAsync(query3);
                    AssertEqual(5, page3.Objects.Count);
                    AssertEqual(3, page3.PageNumber);

                    // Verify no duplicates across pages
                    List<string> allIds = new List<string>();
                    foreach (ArmadaEvent evt in page1.Objects) allIds.Add(evt.Id);
                    foreach (ArmadaEvent evt in page2.Objects) allIds.Add(evt.Id);
                    foreach (ArmadaEvent evt in page3.Objects) allIds.Add(evt.Id);
                    AssertEqual(15, allIds.Count);
                    AssertEqual(15, new HashSet<string>(allIds).Count);

                    // Filter by event type with pagination
                    EnumerationQuery typeQuery = new EnumerationQuery();
                    typeQuery.PageNumber = 1;
                    typeQuery.PageSize = 100;
                    typeQuery.EventType = "mission.created";

                    EnumerationResult<ArmadaEvent> typeResult = await db.Events.EnumerateAsync(typeQuery);
                    AssertEqual(5, (int)typeResult.TotalRecords);
                    foreach (ArmadaEvent evt in typeResult.Objects)
                    {
                        AssertEqual("mission.created", evt.EventType);
                    }

                    // Filter by captain with pagination
                    EnumerationQuery captainQuery = new EnumerationQuery();
                    captainQuery.PageNumber = 1;
                    captainQuery.PageSize = 10;
                    captainQuery.CaptainId = "cpt_pag";

                    EnumerationResult<ArmadaEvent> captainResult = await db.Events.EnumerateAsync(captainQuery);
                    AssertEqual(15, (int)captainResult.TotalRecords);

                    // Empty page (beyond total)
                    EnumerationQuery emptyQuery = new EnumerationQuery();
                    emptyQuery.PageNumber = 10;
                    emptyQuery.PageSize = 5;

                    EnumerationResult<ArmadaEvent> emptyPage = await db.Events.EnumerateAsync(emptyQuery);
                    AssertEqual(0, emptyPage.Objects.Count);
                    AssertEqual(15, (int)emptyPage.TotalRecords);
                }
            });
        }
    }
}
