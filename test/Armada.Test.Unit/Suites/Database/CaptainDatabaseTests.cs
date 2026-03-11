namespace Armada.Test.Unit.Suites.Database
{
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    public class CaptainDatabaseTests : TestSuite
    {
        public override string Name => "Captain Database";

        protected override async Task RunTestsAsync()
        {
            await RunTest("CreateAsync returns captain", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("claude-1", AgentRuntimeEnum.ClaudeCode);
                    Captain result = await db.Captains.CreateAsync(captain);

                    AssertNotNull(result);
                    AssertEqual("claude-1", result.Name);
                    AssertEqual(AgentRuntimeEnum.ClaudeCode, result.Runtime);
                }
            });

            await RunTest("ReadAsync returns created captain", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("read-test");
                    await db.Captains.CreateAsync(captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(result);
                    AssertEqual(captain.Id, result!.Id);
                    AssertEqual(CaptainStateEnum.Idle, result.State);
                }
            });

            await RunTest("ReadByNameAsync returns correct captain", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("name-lookup");
                    await db.Captains.CreateAsync(captain);

                    Captain? result = await db.Captains.ReadByNameAsync("name-lookup");
                    AssertNotNull(result);
                    AssertEqual(captain.Id, result!.Id);
                }
            });

            await RunTest("UpdateAsync modifies captain", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("update-test");
                    await db.Captains.CreateAsync(captain);

                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = "msn_test";
                    captain.ProcessId = 12345;
                    captain.RecoveryAttempts = 2;
                    await db.Captains.UpdateAsync(captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Working, result!.State);
                    AssertEqual("msn_test", result.CurrentMissionId);
                    AssertEqual(12345, result.ProcessId);
                    AssertEqual(2, result.RecoveryAttempts);
                }
            });

            await RunTest("DeleteAsync removes captain", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("to-delete");
                    await db.Captains.CreateAsync(captain);

                    await db.Captains.DeleteAsync(captain.Id);
                    AssertNull(await db.Captains.ReadAsync(captain.Id));
                }
            });

            await RunTest("EnumerateByStateAsync filters correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain c1 = new Captain("idle-1");
                    Captain c2 = new Captain("working-1");
                    c2.State = CaptainStateEnum.Working;
                    Captain c3 = new Captain("idle-2");

                    await db.Captains.CreateAsync(c1);
                    await db.Captains.CreateAsync(c2);
                    await db.Captains.CreateAsync(c3);

                    List<Captain> idle = await db.Captains.EnumerateByStateAsync(CaptainStateEnum.Idle);
                    AssertEqual(2, idle.Count);

                    List<Captain> working = await db.Captains.EnumerateByStateAsync(CaptainStateEnum.Working);
                    AssertEqual(1, working.Count);
                }
            });

            await RunTest("UpdateStateAsync changes state", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("state-test");
                    await db.Captains.CreateAsync(captain);

                    await db.Captains.UpdateStateAsync(captain.Id, CaptainStateEnum.Stalled);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Stalled, result!.State);
                }
            });

            await RunTest("UpdateHeartbeatAsync sets timestamp", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("heartbeat-test");
                    await db.Captains.CreateAsync(captain);
                    AssertNull(captain.LastHeartbeatUtc);

                    await db.Captains.UpdateHeartbeatAsync(captain.Id);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(result!.LastHeartbeatUtc);
                }
            });

            await RunTest("ExistsAsync works correctly", async () =>
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
        }
    }
}
