namespace Armada.Test.Unit.Suites.Database
{
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    /// <summary>
    /// Comprehensive CRUD and behavior tests for captain database operations.
    /// </summary>
    public class CaptainTests : TestSuite
    {
        /// <summary>
        /// Test suite name.
        /// </summary>
        public override string Name => "Captain Tests";

        /// <summary>
        /// Run all captain tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            await RunTest("Captain_Create", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("create-test", AgentRuntimeEnum.ClaudeCode);
                    Captain result = await db.Captains.CreateAsync(captain);

                    AssertNotNull(result);
                    AssertEqual("create-test", result.Name);
                    AssertEqual(AgentRuntimeEnum.ClaudeCode, result.Runtime);
                    AssertEqual(CaptainStateEnum.Idle, result.State);
                    AssertStartsWith("cpt_", result.Id);
                }
            });

            await RunTest("Captain_Read", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("read-test");
                    await db.Captains.CreateAsync(captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(result);
                    AssertEqual(captain.Id, result!.Id);
                    AssertEqual("read-test", result.Name);
                    AssertEqual(CaptainStateEnum.Idle, result.State);
                }
            });

            await RunTest("Captain_ReadByName", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("name-lookup-test");
                    await db.Captains.CreateAsync(captain);

                    Captain? result = await db.Captains.ReadByNameAsync("name-lookup-test");
                    AssertNotNull(result);
                    AssertEqual(captain.Id, result!.Id);
                    AssertEqual("name-lookup-test", result.Name);
                }
            });

            await RunTest("Captain_Update", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("update-test");
                    await db.Captains.CreateAsync(captain);

                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = "msn_test123";
                    captain.CurrentDockId = "dck_test456";
                    captain.ProcessId = 9876;
                    captain.RecoveryAttempts = 3;
                    await db.Captains.UpdateAsync(captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(result);
                    AssertEqual(CaptainStateEnum.Working, result!.State);
                    AssertEqual("msn_test123", result.CurrentMissionId);
                    AssertEqual("dck_test456", result.CurrentDockId);
                    AssertEqual(9876, result.ProcessId);
                    AssertEqual(3, result.RecoveryAttempts);
                }
            });

            await RunTest("Captain_Exists", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("exists-test");
                    await db.Captains.CreateAsync(captain);

                    AssertTrue(await db.Captains.ExistsAsync(captain.Id));
                    AssertFalse(await db.Captains.ExistsAsync("cpt_nonexistent"));
                }
            });

            await RunTest("Captain_Enumerate", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain c1 = new Captain("enum-1");
                    Captain c2 = new Captain("enum-2");
                    Captain c3 = new Captain("enum-3");
                    await db.Captains.CreateAsync(c1);
                    await db.Captains.CreateAsync(c2);
                    await db.Captains.CreateAsync(c3);

                    List<Captain> all = await db.Captains.EnumerateAsync();
                    AssertEqual(3, all.Count);
                }
            });

            await RunTest("Captain_EnumeratePaginated", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    for (int i = 0; i < 5; i++)
                    {
                        Captain captain = new Captain("page-" + i.ToString());
                        await db.Captains.CreateAsync(captain);
                    }

                    EnumerationQuery query = new EnumerationQuery();
                    query.PageNumber = 1;
                    query.PageSize = 2;

                    EnumerationResult<Captain> page1 = await db.Captains.EnumerateAsync(query);
                    AssertEqual(2, page1.Objects.Count);
                    AssertEqual(5, (int)page1.TotalRecords);
                    AssertEqual(3, page1.TotalPages);
                    AssertEqual(1, page1.PageNumber);
                    AssertEqual(2, page1.PageSize);

                    query.PageNumber = 2;
                    EnumerationResult<Captain> page2 = await db.Captains.EnumerateAsync(query);
                    AssertEqual(2, page2.Objects.Count);
                    AssertEqual(2, page2.PageNumber);

                    query.PageNumber = 3;
                    EnumerationResult<Captain> page3 = await db.Captains.EnumerateAsync(query);
                    AssertEqual(1, page3.Objects.Count);
                    AssertEqual(3, page3.PageNumber);
                }
            });

            await RunTest("Captain_EnumerateByState", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain idle1 = new Captain("idle-1");
                    Captain idle2 = new Captain("idle-2");
                    Captain working1 = new Captain("working-1");
                    working1.State = CaptainStateEnum.Working;
                    Captain stalled1 = new Captain("stalled-1");
                    stalled1.State = CaptainStateEnum.Stalled;

                    await db.Captains.CreateAsync(idle1);
                    await db.Captains.CreateAsync(idle2);
                    await db.Captains.CreateAsync(working1);
                    await db.Captains.CreateAsync(stalled1);

                    List<Captain> idle = await db.Captains.EnumerateByStateAsync(CaptainStateEnum.Idle);
                    AssertEqual(2, idle.Count);

                    List<Captain> working = await db.Captains.EnumerateByStateAsync(CaptainStateEnum.Working);
                    AssertEqual(1, working.Count);
                    AssertEqual("working-1", working[0].Name);

                    List<Captain> stalled = await db.Captains.EnumerateByStateAsync(CaptainStateEnum.Stalled);
                    AssertEqual(1, stalled.Count);
                    AssertEqual("stalled-1", stalled[0].Name);

                    List<Captain> stopping = await db.Captains.EnumerateByStateAsync(CaptainStateEnum.Stopping);
                    AssertEqual(0, stopping.Count);
                }
            });

            await RunTest("Captain_UpdateState", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("state-change-test");
                    await db.Captains.CreateAsync(captain);
                    AssertEqual(CaptainStateEnum.Idle, captain.State);

                    await db.Captains.UpdateStateAsync(captain.Id, CaptainStateEnum.Working);
                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Working, result!.State);

                    await db.Captains.UpdateStateAsync(captain.Id, CaptainStateEnum.Stalled);
                    result = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Stalled, result!.State);

                    await db.Captains.UpdateStateAsync(captain.Id, CaptainStateEnum.Idle);
                    result = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Idle, result!.State);
                }
            });

            await RunTest("Captain_UpdateHeartbeat", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("heartbeat-test");
                    await db.Captains.CreateAsync(captain);

                    Captain? before = await db.Captains.ReadAsync(captain.Id);
                    AssertNull(before!.LastHeartbeatUtc);

                    await db.Captains.UpdateHeartbeatAsync(captain.Id);

                    Captain? after = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(after!.LastHeartbeatUtc);
                    AssertTrue(after.LastHeartbeatUtc!.Value <= DateTime.UtcNow);
                    AssertTrue(after.LastHeartbeatUtc!.Value > DateTime.UtcNow.AddMinutes(-1));
                }
            });

            await RunTest("Captain_TryClaim_Success", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("claim-test");
                    await db.Captains.CreateAsync(captain);
                    AssertEqual(CaptainStateEnum.Idle, captain.State);

                    bool claimed = await db.Captains.TryClaimAsync(captain.Id, "msn_claim_test", "dck_claim_test");
                    AssertTrue(claimed);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(result);
                    AssertEqual(CaptainStateEnum.Working, result!.State);
                    AssertEqual("msn_claim_test", result.CurrentMissionId);
                    AssertEqual("dck_claim_test", result.CurrentDockId);
                }
            });

            await RunTest("Captain_TryClaim_AlreadyWorking", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("already-working");
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = "msn_existing";
                    captain.CurrentDockId = "dck_existing";
                    await db.Captains.CreateAsync(captain);

                    bool claimed = await db.Captains.TryClaimAsync(captain.Id, "msn_new", "dck_new");
                    AssertFalse(claimed);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Working, result!.State);
                    AssertEqual("msn_existing", result.CurrentMissionId);
                    AssertEqual("dck_existing", result.CurrentDockId);
                }
            });

            await RunTest("Captain_Delete", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("delete-test");
                    await db.Captains.CreateAsync(captain);
                    AssertTrue(await db.Captains.ExistsAsync(captain.Id));

                    await db.Captains.DeleteAsync(captain.Id);
                    AssertFalse(await db.Captains.ExistsAsync(captain.Id));
                    AssertNull(await db.Captains.ReadAsync(captain.Id));
                }
            });

            await RunTest("Captain_ReadNotFound", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain? result = await db.Captains.ReadAsync("cpt_does_not_exist");
                    AssertNull(result);
                }
            });
        }
    }
}
