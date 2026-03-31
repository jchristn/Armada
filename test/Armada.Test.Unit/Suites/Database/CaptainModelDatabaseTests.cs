namespace Armada.Test.Unit.Suites.Database
{
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    /// <summary>
    /// Database persistence tests for the Captain.Model property added in v0.11.0.
    /// Verifies that the model field is correctly stored and retrieved across all CRUD operations.
    /// </summary>
    public class CaptainModelDatabaseTests : TestSuite
    {
        /// <summary>
        /// Test suite name.
        /// </summary>
        public override string Name => "Captain Model Database";

        /// <summary>
        /// Run all captain model database tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            await RunTest("Captain_Model_CreateWithModel_PersistsValue", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("model-create-test", AgentRuntimeEnum.ClaudeCode);
                    captain.Model = "claude-sonnet-4-5-20250514";
                    Captain result = await db.Captains.CreateAsync(captain);

                    AssertNotNull(result);
                    AssertEqual("claude-sonnet-4-5-20250514", result.Model);
                }
            });

            await RunTest("Captain_Model_CreateWithoutModel_DefaultsToNull", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("no-model-create-test");
                    Captain result = await db.Captains.CreateAsync(captain);

                    AssertNotNull(result);
                    AssertNull(result.Model);
                }
            });

            await RunTest("Captain_Model_ReadPreservesModel", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("model-read-test");
                    captain.Model = "claude-opus-4-0-20250514";
                    await db.Captains.CreateAsync(captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(result);
                    AssertEqual("claude-opus-4-0-20250514", result!.Model);
                }
            });

            await RunTest("Captain_Model_ReadPreservesNullModel", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("null-model-read-test");
                    await db.Captains.CreateAsync(captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(result);
                    AssertNull(result!.Model);
                }
            });

            await RunTest("Captain_Model_UpdateSetsModel", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("model-update-test");
                    await db.Captains.CreateAsync(captain);
                    AssertNull(captain.Model);

                    captain.Model = "claude-sonnet-4-5-20250514";
                    await db.Captains.UpdateAsync(captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(result);
                    AssertEqual("claude-sonnet-4-5-20250514", result!.Model);
                }
            });

            await RunTest("Captain_Model_UpdateClearsModel", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("model-clear-test");
                    captain.Model = "claude-sonnet-4-5-20250514";
                    await db.Captains.CreateAsync(captain);

                    captain.Model = null;
                    await db.Captains.UpdateAsync(captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(result);
                    AssertNull(result!.Model);
                }
            });

            await RunTest("Captain_Model_UpdateChangesModel", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("model-change-test");
                    captain.Model = "claude-sonnet-4-5-20250514";
                    await db.Captains.CreateAsync(captain);

                    captain.Model = "claude-opus-4-0-20250514";
                    await db.Captains.UpdateAsync(captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(result);
                    AssertEqual("claude-opus-4-0-20250514", result!.Model);
                }
            });

            await RunTest("Captain_Model_EnumeratePreservesModel", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain c1 = new Captain("enum-model-1");
                    c1.Model = "claude-sonnet-4-5-20250514";
                    Captain c2 = new Captain("enum-model-2");
                    c2.Model = "claude-opus-4-0-20250514";
                    Captain c3 = new Captain("enum-no-model");

                    await db.Captains.CreateAsync(c1);
                    await db.Captains.CreateAsync(c2);
                    await db.Captains.CreateAsync(c3);

                    List<Captain> all = await db.Captains.EnumerateAsync();
                    AssertEqual(3, all.Count);

                    Captain? withSonnet = all.Find(c => c.Name == "enum-model-1");
                    AssertNotNull(withSonnet);
                    AssertEqual("claude-sonnet-4-5-20250514", withSonnet!.Model);

                    Captain? withOpus = all.Find(c => c.Name == "enum-model-2");
                    AssertNotNull(withOpus);
                    AssertEqual("claude-opus-4-0-20250514", withOpus!.Model);

                    Captain? noModel = all.Find(c => c.Name == "enum-no-model");
                    AssertNotNull(noModel);
                    AssertNull(noModel!.Model);
                }
            });

            await RunTest("Captain_Model_PreservedAfterStateChange", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("model-state-test");
                    captain.Model = "claude-sonnet-4-5-20250514";
                    await db.Captains.CreateAsync(captain);

                    await db.Captains.UpdateStateAsync(captain.Id, CaptainStateEnum.Working);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(result);
                    AssertEqual(CaptainStateEnum.Working, result!.State);
                    AssertEqual("claude-sonnet-4-5-20250514", result.Model);
                }
            });

            await RunTest("Captain_Model_PreservedAfterHeartbeat", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("model-heartbeat-test");
                    captain.Model = "claude-opus-4-0-20250514";
                    await db.Captains.CreateAsync(captain);

                    await db.Captains.UpdateHeartbeatAsync(captain.Id);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(result);
                    AssertNotNull(result!.LastHeartbeatUtc);
                    AssertEqual("claude-opus-4-0-20250514", result.Model);
                }
            });

            await RunTest("Captain_Model_ReadByNamePreservesModel", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("model-byname-test");
                    captain.Model = "claude-sonnet-4-5-20250514";
                    await db.Captains.CreateAsync(captain);

                    Captain? result = await db.Captains.ReadByNameAsync("model-byname-test");
                    AssertNotNull(result);
                    AssertEqual("claude-sonnet-4-5-20250514", result!.Model);
                }
            });
        }
    }
}
