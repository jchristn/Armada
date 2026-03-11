namespace Armada.Test.Unit.Suites.Database
{
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    public class EventDatabaseTests : TestSuite
    {
        public override string Name => "Event Database";

        protected override async Task RunTestsAsync()
        {
            await RunTest("CreateAsync returns event", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    ArmadaEvent evt = new ArmadaEvent("mission.created", "Mission created");
                    ArmadaEvent result = await db.Events.CreateAsync(evt);

                    AssertNotNull(result);
                    AssertEqual("mission.created", result.EventType);
                }
            });

            await RunTest("EnumerateRecentAsync returns limited", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    for (int i = 0; i < 10; i++)
                    {
                        await db.Events.CreateAsync(new ArmadaEvent("test.event", "Event " + i));
                    }

                    List<ArmadaEvent> recent = await db.Events.EnumerateRecentAsync(5);
                    AssertEqual(5, recent.Count);
                }
            });

            await RunTest("EnumerateByTypeAsync filters correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    await db.Events.CreateAsync(new ArmadaEvent("mission.created", "Created"));
                    await db.Events.CreateAsync(new ArmadaEvent("mission.completed", "Completed"));
                    await db.Events.CreateAsync(new ArmadaEvent("mission.created", "Created 2"));

                    List<ArmadaEvent> created = await db.Events.EnumerateByTypeAsync("mission.created");
                    AssertEqual(2, created.Count);
                }
            });

            await RunTest("EnumerateByEntityAsync filters correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
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
            });

            await RunTest("EnumerateByCaptainAsync filters correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    ArmadaEvent evt1 = new ArmadaEvent("captain.launched", "Launched");
                    evt1.CaptainId = "cpt_test";
                    await db.Events.CreateAsync(evt1);

                    ArmadaEvent evt2 = new ArmadaEvent("captain.launched", "Other");
                    evt2.CaptainId = "cpt_other";
                    await db.Events.CreateAsync(evt2);

                    List<ArmadaEvent> events = await db.Events.EnumerateByCaptainAsync("cpt_test");
                    AssertEqual(1, events.Count);
                }
            });

            await RunTest("EnumerateByMissionAsync filters correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    ArmadaEvent evt = new ArmadaEvent("mission.updated", "Updated");
                    evt.MissionId = "msn_target";
                    await db.Events.CreateAsync(evt);

                    await db.Events.CreateAsync(new ArmadaEvent("other", "Other"));

                    List<ArmadaEvent> events = await db.Events.EnumerateByMissionAsync("msn_target");
                    AssertEqual(1, events.Count);
                }
            });

            await RunTest("EnumerateByVesselAsync filters correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    ArmadaEvent evt = new ArmadaEvent("vessel.event", "Vessel event");
                    evt.VesselId = "vsl_target";
                    await db.Events.CreateAsync(evt);

                    List<ArmadaEvent> events = await db.Events.EnumerateByVesselAsync("vsl_target");
                    AssertEqual(1, events.Count);
                }
            });

            await RunTest("EnumerateByVoyageAsync filters correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    ArmadaEvent evt = new ArmadaEvent("voyage.completed", "Completed");
                    evt.VoyageId = "vyg_target";
                    await db.Events.CreateAsync(evt);

                    List<ArmadaEvent> events = await db.Events.EnumerateByVoyageAsync("vyg_target");
                    AssertEqual(1, events.Count);
                }
            });
        }
    }
}
