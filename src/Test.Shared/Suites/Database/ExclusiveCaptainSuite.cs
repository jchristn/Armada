namespace Test.Shared.Suites.Database
{
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Exclusive session ownership races with mission slots and rejects stale callbacks.</summary>
    public sealed class ExclusiveCaptainSuite : IArmadaTestSuite
    {
        public TestSuiteDescriptor Build()
        {
            const string suite = "Database.ExclusiveCaptain";
            List<TestCaseDescriptor> cases = new();
            void Add(string name, Func<Task> test) => cases.Add(new(suite, name, name, _ => test()));
            Add("fifty_competing_sessions_have_one_owner", async () =>
            {
                using TestDatabase f = await TestDatabaseHelper.CreateDatabaseAsync(); var db = f.Driver;
                Captain captain = await db.Captains.CreateAsync(new Captain("exclusive") { MaxConcurrency = 4 });
                bool[] claimed = await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(() => db.Captains.TryClaimExclusiveAsync(captain.Id, "session-" + i,
                    i % 2 == 0 ? CaptainStateEnum.Planning : CaptainStateEnum.Refining))));
                AssertEqual(1, claimed.Count(value => value)); Captain stored = (await db.Captains.ReadAsync(captain.Id))!;
                AssertEqual("session-" + Array.FindIndex(claimed, value => value), stored.CurrentSessionId);
                AssertEqual(0, stored.ActiveMissionCount); AssertEqual(0, stored.ActiveMissionIds.Count);
            });
            Add("missions_and_sessions_never_share_a_captain", async () =>
            {
                using TestDatabase f = await TestDatabaseHelper.CreateDatabaseAsync(); var db = f.Driver;
                Captain captain = await db.Captains.CreateAsync(new Captain("race") { MaxConcurrency = 3 });
                List<Mission> missions = new(); for (int i = 0; i < 50; i++) missions.Add(await db.Missions.CreateAsync(new Mission("race " + i)));
                Task<bool>[] claims = missions.Select(m => Task.Run(() => db.Captains.TryClaimAsync(captain.Id, m.Id, null)))
                    .Concat(Enumerable.Range(0, 50).Select(i => Task.Run(() => db.Captains.TryClaimExclusiveAsync(captain.Id, "session-" + i, CaptainStateEnum.Planning)))).ToArray();
                bool[] results = await Task.WhenAll(claims); Captain stored = (await db.Captains.ReadAsync(captain.Id))!;
                int sessions = results.Skip(50).Count(value => value); int work = results.Take(50).Count(value => value);
                AssertTrue((sessions == 1 && work == 0) || (sessions == 0 && work == 3));
                AssertEqual(work, stored.ActiveMissionCount); AssertEqual(work, stored.ActiveMissionIds.Count);
                AssertEqual(sessions == 1, stored.CurrentSessionId != null);
            });
            Add("spare_mission_slots_do_not_allow_an_exclusive_session", async () =>
            {
                using TestDatabase f = await TestDatabaseHelper.CreateDatabaseAsync(); var db = f.Driver;
                Captain captain = await db.Captains.CreateAsync(new Captain("busy") { MaxConcurrency = 3 });
                Mission mission = await db.Missions.CreateAsync(new Mission("work")); AssertTrue(await db.Captains.TryClaimAsync(captain.Id, mission.Id, null));
                AssertFalse(await db.Captains.TryClaimExclusiveAsync(captain.Id, "session", CaptainStateEnum.Refining));
                AssertTrue((await db.Captains.ReadAsync(captain.Id))!.HoldsMission(mission.Id));
            });
            foreach (string reason in new[] { "inactive", "quarantine", "exclusive-state" })
                Add("claim_rejects_" + reason, async () =>
                {
                    using TestDatabase f = await TestDatabaseHelper.CreateDatabaseAsync(); var db = f.Driver;
                    Captain captain = await db.Captains.CreateAsync(new Captain(reason) { Active = reason != "inactive",
                        State = reason == "exclusive-state" ? CaptainStateEnum.Planning : CaptainStateEnum.Idle,
                        QuarantineUntilUtc = reason == "quarantine" ? DateTime.UtcNow.AddHours(1) : null });
                    AssertFalse(await db.Captains.TryClaimExclusiveAsync(captain.Id, "session", CaptainStateEnum.Planning));
                    AssertNull((await db.Captains.ReadAsync(captain.Id))!.CurrentSessionId);
                });
            Add("old_owner_cannot_update_or_release_a_new_session", async () =>
            {
                using TestDatabase f = await TestDatabaseHelper.CreateDatabaseAsync(); var db = f.Driver;
                Captain captain = await db.Captains.CreateAsync(new Captain("reuse"));
                AssertTrue(await db.Captains.TryClaimExclusiveAsync(captain.Id, "old", CaptainStateEnum.Planning));
                AssertTrue(await db.Captains.TryReleaseExclusiveAsync(captain.Id, "old"));
                AssertTrue(await db.Captains.TryClaimExclusiveAsync(captain.Id, "new", CaptainStateEnum.Refining));
                AssertTrue(await db.Captains.TryUpdateExclusiveAsync(captain.Id, "new", CaptainStateEnum.Refining, "new-dock", 42));
                AssertFalse(await db.Captains.TryUpdateExclusiveAsync(captain.Id, "old", CaptainStateEnum.Planning, null, null));
                AssertFalse(await db.Captains.TryReleaseExclusiveAsync(captain.Id, "old"));
                Captain stored = (await db.Captains.ReadAsync(captain.Id))!;
                AssertEqual("new", stored.CurrentSessionId); AssertEqual(42, stored.ProcessId); AssertEqual("new-dock", stored.CurrentDockId);
            });
            Add("idempotent_claim_preserves_runtime_fields", async () =>
            {
                using TestDatabase f = await TestDatabaseHelper.CreateDatabaseAsync(); var db = f.Driver;
                TenantMetadata tenant = await db.Tenants.CreateAsync(new TenantMetadata("exclusive"));
                Captain captain = await db.Captains.CreateAsync(new Captain("resume") { TenantId = tenant.Id });
                AssertTrue(await db.Captains.TryClaimExclusiveAsync(captain.Id, "session", CaptainStateEnum.Planning));
                AssertTrue(await db.Captains.TryUpdateExclusiveAsync(captain.Id, "session", CaptainStateEnum.Planning, "dock", 42));
                AssertFalse(await db.Captains.TryReleaseAsync(tenant.Id, captain.Id, CaptainStateEnum.Planning));
                AssertTrue(await db.Captains.TryClaimExclusiveAsync(captain.Id, "session", CaptainStateEnum.Planning));
                Captain stored = (await db.Captains.ReadAsync(captain.Id))!; AssertEqual(42, stored.ProcessId); AssertEqual("dock", stored.CurrentDockId);
                AssertTrue(await db.Captains.TryReleaseExclusiveAsync(captain.Id, "session")); AssertFalse(await db.Captains.TryReleaseExclusiveAsync(captain.Id, "session"));
                stored = (await db.Captains.ReadAsync(captain.Id))!; AssertNull(stored.CurrentSessionId); AssertNull(stored.ProcessId); AssertNull(stored.CurrentDockId);
                AssertEqual(CaptainStateEnum.Idle, stored.State);
            });
            Add("recovery_adopts_unowned_legacy_state_without_stealing", async () =>
            {
                using TestDatabase f = await TestDatabaseHelper.CreateDatabaseAsync(); var db = f.Driver;
                Captain captain = await db.Captains.CreateAsync(new Captain("legacy") { State = CaptainStateEnum.Refining });
                AssertFalse(await db.Captains.TryClaimExclusiveAsync(captain.Id, "wrong-state", CaptainStateEnum.Planning, recover: true));
                AssertTrue(await db.Captains.TryClaimExclusiveAsync(captain.Id, "legacy", CaptainStateEnum.Refining, recover: true));
                AssertFalse(await db.Captains.TryClaimExclusiveAsync(captain.Id, "other", CaptainStateEnum.Refining, recover: true));
                AssertEqual("legacy", (await db.Captains.ReadAsync(captain.Id))!.CurrentSessionId);
            });
            Add("entity_updates_cannot_acquire_or_erase_session_ownership", async () =>
            {
                using TestDatabase f = await TestDatabaseHelper.CreateDatabaseAsync(); var db = f.Driver;
                Captain stale = await db.Captains.CreateAsync(new Captain("configuration"));
                AssertTrue(await db.Captains.TryClaimExclusiveAsync(stale.Id, "session", CaptainStateEnum.Planning));
                stale.ProcessId = 99; stale.CurrentSessionId = null; await db.Captains.UpdateAsync(stale);
                Captain owner = (await db.Captains.ReadAsync(stale.Id))!; owner.Name = "edited"; owner.State = CaptainStateEnum.Idle; owner.ProcessId = 99;
                await db.Captains.UpdateAsync(owner);
                await db.Captains.UpdateStateAsync(owner.Id, CaptainStateEnum.Idle);
                Captain stored = (await db.Captains.ReadAsync(stale.Id))!;
                AssertEqual("session", stored.CurrentSessionId); AssertEqual(CaptainStateEnum.Planning, stored.State); AssertNull(stored.ProcessId); AssertEqual("edited", stored.Name);
            });
            Add("retired_session_snapshot_cannot_clobber_a_mission", async () =>
            {
                using TestDatabase f = await TestDatabaseHelper.CreateDatabaseAsync(); var db = f.Driver;
                Captain captain = await db.Captains.CreateAsync(new Captain("reuse"));
                AssertTrue(await db.Captains.TryClaimExclusiveAsync(captain.Id, "old", CaptainStateEnum.Planning));
                Captain stale = (await db.Captains.ReadAsync(captain.Id))!; AssertTrue(await db.Captains.TryReleaseExclusiveAsync(captain.Id, "old"));
                Mission mission = await db.Missions.CreateAsync(new Mission("work")); AssertTrue(await db.Captains.TryClaimAsync(captain.Id, mission.Id, "mission-dock"));
                stale.CurrentSessionId = null; stale.ProcessId = null; stale.State = CaptainStateEnum.Idle; await db.Captains.UpdateAsync(stale);
                AssertFalse(await db.Captains.TryReleaseExclusiveAsync(captain.Id, "old"));
                Captain stored = (await db.Captains.ReadAsync(captain.Id))!; AssertTrue(stored.HoldsMission(mission.Id)); AssertEqual(CaptainStateEnum.Working, stored.State); AssertEqual("mission-dock", stored.CurrentDockId);
            });
            Add("invalid_exclusive_state_is_rejected", async () =>
            {
                using TestDatabase f = await TestDatabaseHelper.CreateDatabaseAsync();
                bool rejected = false;
                try { await f.Driver.Captains.TryClaimExclusiveAsync("captain", "session", CaptainStateEnum.Working); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                AssertTrue(rejected);
            });
            return new(suite, "Exclusive captain ownership", cases);
        }
    }
}
