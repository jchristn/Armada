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
    /// Comprehensive CRUD and behavior descriptors for captain database operations: create,
    /// read, read-by-name, update, existence, enumeration (all, paginated, by state), state
    /// and heartbeat updates, optimistic claim (success and contention rejection), delete, and
    /// the not-found read path. Each case runs against its own fresh SQLite store.
    /// </summary>
    public sealed class CaptainSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.Captain";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Captain Tests suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(CaseAsync("active_persistence_claim_and_reactivation", "Inactive rows reject claims; reactivation restores eligibility without changing runtime State", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = await CaptainAssignmentFixture.CreateAsync(db, new Captain("active-test"));
                    AssertTrue((await db.Captains.ReadAsync(captain.Id))!.Active);
                    captain.Active = false;
                    await CaptainAssignmentFixture.UpdateAsync(db, captain, updateActive: true);
                    AssertFalse((await db.Captains.ReadAsync(captain.Id))!.Active);
                    AssertFalse(await db.Captains.TryClaimAsync(captain.Id, "msn_disabled", null));
                    captain.Active = true;
                    await CaptainAssignmentFixture.UpdateAsync(db, captain, updateActive: true);
                    AssertTrue(await db.Captains.TryClaimAsync(captain.Id, "msn_enabled", null));
                    Captain working = (await db.Captains.ReadAsync(captain.Id))!;
                    working.Active = false;
                    await CaptainAssignmentFixture.UpdateAsync(db, working, updateActive: true);
                    Captain stored = (await db.Captains.ReadAsync(captain.Id))!;
                    AssertFalse(stored.Active);
                    working.Active = true;
                    await CaptainAssignmentFixture.UpdateAsync(db, working);
                    AssertFalse((await db.Captains.ReadAsync(captain.Id))!.Active);
                    AssertEqual(CaptainStateEnum.Working, stored.State);
                    AssertEqual("msn_enabled", stored.ActiveMissionIds.SingleOrDefault());
                }
            }));


            cases.Add(CaseAsync("captain_create", "Captain_Create", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("create-test", AgentRuntimeEnum.ClaudeCode);
                    Captain result = await CaptainAssignmentFixture.CreateAsync(db, captain);

                    AssertNotNull(result);
                    AssertEqual("create-test", result.Name);
                    AssertEqual(AgentRuntimeEnum.ClaudeCode, result.Runtime);
                    AssertEqual(CaptainStateEnum.Idle, result.State);
                    AssertStartsWith("cpt_", result.Id);
                }
            }));

            cases.Add(CaseAsync("captain_read", "Captain_Read", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("read-test");
                    await CaptainAssignmentFixture.CreateAsync(db, captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(result);
                    AssertEqual(captain.Id, result!.Id);
                    AssertEqual("read-test", result.Name);
                    AssertEqual(CaptainStateEnum.Idle, result.State);
                }
            }));

            cases.Add(CaseAsync("captain_read_by_name", "Captain_ReadByName", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("name-lookup-test");
                    await CaptainAssignmentFixture.CreateAsync(db, captain);

                    Captain? result = await db.Captains.ReadByNameAsync("name-lookup-test");
                    AssertNotNull(result);
                    AssertEqual(captain.Id, result!.Id);
                    AssertEqual("name-lookup-test", result.Name);
                }
            }));

            cases.Add(CaseAsync("captain_update", "Captain_Update", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("update-test");
                    await CaptainAssignmentFixture.CreateAsync(db, captain);

                    captain.State = CaptainStateEnum.Working;
                    captain.Assignment = await db.Missions.CreateAsync(new Mission("update assignment") { Id = "msn_test123" });
                    captain.Assignment ??= new Mission("fixture assignment"); captain.Assignment.DockId = "dck_test456";
                    captain.ProcessId = 9876;
                    captain.RecoveryAttempts = 3;
                    await CaptainAssignmentFixture.UpdateAsync(db, captain);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(result);
                    AssertEqual(CaptainStateEnum.Working, result!.State);
                    AssertEqual("msn_test123", result.ActiveMissionIds.SingleOrDefault());
                    AssertNull(result.Assignment);
                    AssertEqual("dck_test456", (await db.Missions.ReadAsync("msn_test123"))!.DockId);
                    AssertNull(result.ProcessId);
                    AssertEqual(9876, await db.Captains.ReadMissionProcessAsync(captain.Id, "msn_test123"));
                    AssertEqual(3, result.RecoveryAttempts);
                }
            }));

            cases.Add(CaseAsync("captain_exists", "Captain_Exists", TestTags.Positive, async () =>
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

            cases.Add(CaseAsync("captain_enumerate", "Captain_Enumerate", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain c1 = new Captain("enum-1");
                    Captain c2 = new Captain("enum-2");
                    Captain c3 = new Captain("enum-3");
                    await CaptainAssignmentFixture.CreateAsync(db, c1);
                    await CaptainAssignmentFixture.CreateAsync(db, c2);
                    await CaptainAssignmentFixture.CreateAsync(db, c3);

                    List<Captain> all = await db.Captains.EnumerateAsync();
                    AssertEqual(3, all.Count);
                }
            }));

            cases.Add(CaseAsync("captain_enumerate_paginated", "Captain_EnumeratePaginated", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    for (int i = 0; i < 5; i++)
                    {
                        Captain captain = new Captain("page-" + i.ToString());
                        await CaptainAssignmentFixture.CreateAsync(db, captain);
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
            }));

            cases.Add(CaseAsync("captain_enumerate_by_state", "Captain_EnumerateByState", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain idle1 = new Captain("idle-1");
                    Captain idle2 = new Captain("idle-2");
                    Captain working1 = new Captain("working-1");
                    working1.State = CaptainStateEnum.Working;
                    Captain stalled1 = new Captain("stalled-1");
                    stalled1.State = CaptainStateEnum.Stalled;

                    await CaptainAssignmentFixture.CreateAsync(db, idle1);
                    await CaptainAssignmentFixture.CreateAsync(db, idle2);
                    await CaptainAssignmentFixture.CreateAsync(db, working1);
                    await CaptainAssignmentFixture.CreateAsync(db, stalled1);

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
            }));

            cases.Add(CaseAsync("captain_update_state", "Captain_UpdateState", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("state-change-test");
                    await CaptainAssignmentFixture.CreateAsync(db, captain);
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
            }));

            cases.Add(CaseAsync("captain_update_heartbeat", "Captain_UpdateHeartbeat", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("heartbeat-test");
                    await CaptainAssignmentFixture.CreateAsync(db, captain);

                    Captain? before = await db.Captains.ReadAsync(captain.Id);
                    AssertNull(before!.LastHeartbeatUtc);

                    await db.Captains.UpdateHeartbeatAsync(captain.Id);

                    Captain? after = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(after!.LastHeartbeatUtc);
                    AssertTrue(after.LastHeartbeatUtc!.Value <= DateTime.UtcNow);
                    AssertTrue(after.LastHeartbeatUtc!.Value > DateTime.UtcNow.AddMinutes(-1));
                }
            }));

            cases.Add(CaseAsync("captain_try_claim_success", "Captain_TryClaim_Success", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("claim-test");
                    await CaptainAssignmentFixture.CreateAsync(db, captain);
                    AssertEqual(CaptainStateEnum.Idle, captain.State);

                    await db.Missions.CreateAsync(new Mission("claim") { Id = "msn_claim_test" });
                    bool claimed = await db.Captains.TryClaimAsync(captain.Id, "msn_claim_test", "dck_claim_test");
                    AssertTrue(claimed);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(result);
                    AssertEqual(CaptainStateEnum.Working, result!.State);
                    AssertEqual("msn_claim_test", result.ActiveMissionIds.SingleOrDefault());
                    AssertNull(result.Assignment);
                    AssertEqual("dck_claim_test", (await db.Missions.ReadAsync("msn_claim_test"))!.DockId);
                }
            }));

            cases.Add(CaseAsync("captain_try_claim_already_working", "Captain_TryClaim_AlreadyWorking", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("already-working");
                    captain.State = CaptainStateEnum.Working;
                    captain.Assignment = await db.Missions.CreateAsync(new Mission("existing") { Id = "msn_existing" });
                    captain.Assignment ??= new Mission("fixture assignment"); captain.Assignment.DockId = "dck_existing";
                    await CaptainAssignmentFixture.CreateAsync(db, captain);

                    bool claimed = await db.Captains.TryClaimAsync(captain.Id, "msn_new", "dck_new");
                    AssertFalse(claimed);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Working, result!.State);
                    AssertEqual("msn_existing", result.ActiveMissionIds.SingleOrDefault());
                    AssertNull(result.Assignment);
                    AssertEqual("dck_existing", (await db.Missions.ReadAsync("msn_existing"))!.DockId);
                }
            }));

            cases.Add(CaseAsync("captain_try_claim_without_dock", "A captain can be claimed before its dock exists, and the dock attached later", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("claim-first");
                    await CaptainAssignmentFixture.CreateAsync(db, captain);

                    await db.Missions.CreateAsync(new Mission("first") { Id = "msn_first" });
                    AssertTrue(await db.Captains.TryClaimAsync(captain.Id, "msn_first", null), "claim without a dock");
                    Captain? claimed = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Working, claimed!.State);
                    AssertEqual("msn_first", claimed.ActiveMissionIds.SingleOrDefault());
                    AssertNull(claimed.Assignment?.DockId);

                    AssertFalse(await db.Captains.TrySetDockAsync(captain.Id, "msn_other", "dck_other"), "dock for another mission is refused");
                    AssertTrue(await db.Captains.TrySetDockAsync(captain.Id, "msn_first", "dck_first"), "dock for the held mission is recorded");
                    AssertFalse(await db.Captains.TrySetProcessAsync(captain.Id, "msn_other", 4242), "process for another mission is refused");
                    AssertTrue(await db.Captains.TrySetProcessAsync(captain.Id, "msn_first", 4242), "process for the held mission is recorded");

                    Captain? attached = await db.Captains.ReadAsync(captain.Id);
                    AssertNull(attached!.Assignment);
                    AssertEqual("dck_first", (await db.Missions.ReadAsync("msn_first"))!.DockId);
                    AssertNull(attached.ProcessId);
                    AssertEqual(4242, await db.Captains.ReadMissionProcessAsync(captain.Id, "msn_first"));
                }
            }));

            cases.Add(CaseAsync("captain_try_release_from_mission_is_guarded", "Release only applies while the captain still holds the given mission", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("guarded-release");
                    await CaptainAssignmentFixture.CreateAsync(db, captain);
                    AssertTrue(await db.Captains.TryClaimAsync(captain.Id, "msn_current", "dck_current"));
                    AssertTrue(await db.Captains.TrySetProcessAsync(captain.Id, "msn_current", 77));

                    // A stale rollback for an earlier mission must not wipe the current assignment.
                    AssertFalse(await db.Captains.TryReleaseFromMissionAsync(captain.Id, "msn_stale"), "stale release is refused");
                    Captain? untouched = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Working, untouched!.State);
                    AssertEqual("msn_current", untouched.ActiveMissionIds.SingleOrDefault());
                    AssertNull(untouched.ProcessId);
                    AssertEqual(77, await db.Captains.ReadMissionProcessAsync(captain.Id, "msn_current"));

                    AssertTrue(await db.Captains.TryReleaseFromMissionAsync(captain.Id, "msn_current"), "release of the held mission applies");
                    Captain? released = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Idle, released!.State);
                    AssertNull(released.ActiveMissionIds.SingleOrDefault());
                    AssertNull(released.Assignment?.DockId);
                    AssertNull(released.ProcessId);
                    AssertFalse(await db.Captains.TryReleaseFromMissionAsync(captain.Id, "msn_current"), "a second release is a no-op");
                }
            }));

            cases.Add(CaseAsync("captain_try_release_from_mission_keeps_quarantine", "Release never lifts a quarantine", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("quarantined-release");
                    captain.State = CaptainStateEnum.Quarantined;
                    captain.Assignment = new Mission { Id = "msn_q" };
                    await CaptainAssignmentFixture.CreateAsync(db, captain);

                    AssertTrue(await db.Captains.TryReleaseFromMissionAsync(captain.Id, "msn_q"));
                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Quarantined, result!.State);
                }
            }));

            cases.Add(CaseAsync("captain_delete", "Captain_Delete", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("delete-test");
                    await CaptainAssignmentFixture.CreateAsync(db, captain);
                    AssertTrue(await db.Captains.ExistsAsync(captain.Id));

                    await db.Captains.DeleteAsync(captain.Id);
                    AssertFalse(await db.Captains.ExistsAsync(captain.Id));
                    AssertNull(await db.Captains.ReadAsync(captain.Id));
                }
            }));

            cases.Add(CaseAsync("captain_read_not_found", "Captain_ReadNotFound", TestTags.Negative, async () =>
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
                displayName: "Captain Tests",
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
