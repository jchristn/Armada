namespace Test.Shared.Suites.Database
{
    using System.Linq;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the captain database methods: create, read, read-by-name, update
    /// (including model clearing), delete, state and heartbeat updates, state-filtered
    /// enumeration, and existence checks. Each case runs against its own fresh SQLite store.
    /// </summary>
    public sealed class CaptainDatabaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.CaptainDatabase";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Captain Database suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("create_async_returns_captain", "CreateAsync returns captain", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("claude-1", AgentRuntimeEnum.ClaudeCode);
                    captain.Model = "gpt-5.4";
                    Captain result = await CaptainAssignmentFixture.CreateAsync(db, captain);
                    Captain? read = await db.Captains.ReadAsync(captain.Id);

                    AssertNotNull(result);
                    AssertNotNull(read);
                    AssertEqual("claude-1", result.Name);
                    AssertEqual(AgentRuntimeEnum.ClaudeCode, result.Runtime);
                    AssertEqual("gpt-5.4", result.Model);
                    AssertEqual("gpt-5.4", read!.Model);
                }
            }));

            cases.Add(CaseAsync("pi_captain_roundtrips", "A Pi captain round-trips its runtime and model", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("pi-1", AgentRuntimeEnum.Pi);
                    captain.Model = "anthropic/claude-opus-4-8";
                    await CaptainAssignmentFixture.CreateAsync(db, captain);
                    Captain? read = await db.Captains.ReadAsync(captain.Id);

                    AssertNotNull(read);
                    AssertEqual(AgentRuntimeEnum.Pi, read!.Runtime);
                    AssertEqual("anthropic/claude-opus-4-8", read.Model);
                }
            }));

            cases.Add(CaseAsync("read_async_returns_created_captain", "ReadAsync returns created captain", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("read-test");
                    await CaptainAssignmentFixture.CreateAsync(db, captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(result);
                    AssertEqual(captain.Id, result!.Id);
                    AssertEqual(CaptainStateEnum.Idle, result.State);
                }
            }));

            cases.Add(CaseAsync("read_by_name_async_returns_correct_captain", "ReadByNameAsync returns correct captain", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("name-lookup");
                    await CaptainAssignmentFixture.CreateAsync(db, captain);

                    Captain? result = await db.Captains.ReadByNameAsync("name-lookup");
                    AssertNotNull(result);
                    AssertEqual(captain.Id, result!.Id);
                }
            }));

            cases.Add(CaseAsync("update_async_modifies_captain", "UpdateAsync modifies captain", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("update-test");
                    await CaptainAssignmentFixture.CreateAsync(db, captain);

                    captain.State = CaptainStateEnum.Working;
                    captain.Assignment = new Mission { Id = "msn_test" };
                    captain.ProcessId = 12345;
                    captain.RecoveryAttempts = 2;
                    captain.Model = "gpt-5.4-mini";
                    await CaptainAssignmentFixture.UpdateAsync(db, captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Working, result!.State);
                    AssertEqual("msn_test", result.ActiveMissionIds.SingleOrDefault());
                    AssertNull(result.ProcessId);
                    AssertEqual(12345, await db.Captains.ReadMissionProcessAsync(captain.Id, "msn_test"));
                    AssertEqual(2, result.RecoveryAttempts);
                    AssertEqual("gpt-5.4-mini", result.Model);
                }
            }));

            cases.Add(CaseAsync("update_async_clears_captain_model_when_empty_string_is_assigned", "UpdateAsync clears captain model when empty string is assigned", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("clear-model-test");
                    captain.Model = "gpt-5.4";
                    await CaptainAssignmentFixture.CreateAsync(db, captain);

                    captain.Model = "";
                    await CaptainAssignmentFixture.UpdateAsync(db, captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNull(result!.Model);
                }
            }));

            cases.Add(CaseAsync("delete_async_removes_captain", "DeleteAsync removes captain", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("to-delete");
                    await CaptainAssignmentFixture.CreateAsync(db, captain);

                    await db.Captains.DeleteAsync(captain.Id);
                    AssertNull(await db.Captains.ReadAsync(captain.Id));
                }
            }));

            cases.Add(CaseAsync("enumerate_by_state_async_filters_correctly", "EnumerateByStateAsync filters correctly", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain c1 = new Captain("idle-1");
                    Captain c2 = new Captain("working-1");
                    c2.State = CaptainStateEnum.Working;
                    Captain c3 = new Captain("idle-2");

                    await CaptainAssignmentFixture.CreateAsync(db, c1);
                    await CaptainAssignmentFixture.CreateAsync(db, c2);
                    await CaptainAssignmentFixture.CreateAsync(db, c3);

                    List<Captain> idle = await db.Captains.EnumerateByStateAsync(CaptainStateEnum.Idle);
                    AssertEqual(2, idle.Count);

                    List<Captain> working = await db.Captains.EnumerateByStateAsync(CaptainStateEnum.Working);
                    AssertEqual(1, working.Count);
                }
            }));

            cases.Add(CaseAsync("update_state_async_changes_state", "UpdateStateAsync changes state", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("state-test");
                    await CaptainAssignmentFixture.CreateAsync(db, captain);

                    await db.Captains.UpdateStateAsync(captain.Id, CaptainStateEnum.Stalled);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Stalled, result!.State);
                }
            }));

            cases.Add(CaseAsync("update_heartbeat_async_sets_timestamp", "UpdateHeartbeatAsync sets timestamp", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("heartbeat-test");
                    await CaptainAssignmentFixture.CreateAsync(db, captain);
                    AssertNull(captain.LastHeartbeatUtc);

                    await db.Captains.UpdateHeartbeatAsync(captain.Id);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(result!.LastHeartbeatUtc);
                }
            }));

            cases.Add(CaseAsync("exists_async_works_correctly", "ExistsAsync works correctly", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("exists-test");
                    await CaptainAssignmentFixture.CreateAsync(db, captain);

                    AssertTrue(await db.Captains.ExistsAsync(captain.Id));
                    AssertFalse(await db.Captains.ExistsAsync("cpt_nonexistent"));
                }
            }));

            // Audit addition: read of a missing id must return null (not-found path).
            cases.Add(CaseAsync("read_async_missing_returns_null", "ReadAsync missing returns null", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain? result = await db.Captains.ReadAsync("cpt_does_not_exist");
                    AssertNull(result);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Captain Database",
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
