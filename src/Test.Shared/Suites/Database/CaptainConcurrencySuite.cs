namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Atomic slot admission, idempotent release and durable reconciliation.</summary>
    public sealed class CaptainConcurrencySuite : IArmadaTestSuite
    {
        /// <summary>Build provider-independent slot contracts.</summary>
        public TestSuiteDescriptor Build()
        {
            const string suite = "Database.CaptainConcurrency";
            return new TestSuiteDescriptor(suite, "Captain slot transactions", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(suite, "server_ceiling", "The server ceiling limits claims without rewriting stored capacity", async ct =>
                {
                    using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                    var db = fixture.Driver;
                    Captain captain = await db.Captains.CreateAsync(new Captain("ceiling") { MaxConcurrency = 8 });
                    Mission first = await db.Missions.CreateAsync(new Mission("first"));
                    Mission second = await db.Missions.CreateAsync(new Mission("second"));
                    Mission third = await db.Missions.CreateAsync(new Mission("third"));
                    AssertTrue(await db.Captains.TryClaimAsync(captain.Id, first.Id, null, ct, ceiling: 2));
                    AssertTrue(await db.Captains.TryClaimAsync(captain.Id, second.Id, null, ct, ceiling: 2));
                    AssertFalse(await db.Captains.TryClaimAsync(captain.Id, third.Id, null, ct, ceiling: 2));
                    AssertEqual(8, (await db.Captains.ReadAsync(captain.Id))!.MaxConcurrency);
                    AssertEqual(2, (await db.Captains.ReadAsync(captain.Id))!.ActiveMissionCount);
                }, tags: new List<string> { TestTags.Positive }),
                new TestCaseDescriptor(suite, "fifty_claims_three_slots", "Exactly three of fifty concurrent claims succeed; release is per mission and idempotent", async ct =>
                {
                    using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                    var db = fixture.Driver;
                    Captain captain = await db.Captains.CreateAsync(new Captain("parallel") { MaxConcurrency = 3 });
                    List<Mission> missions = new List<Mission>();
                    for (int i = 0; i < 50; i++) missions.Add(await db.Missions.CreateAsync(new Mission("work " + i)));
                    bool[] claims = await Task.WhenAll(missions.Select(m => Task.Run(() => db.Captains.TryClaimAsync(captain.Id, m.Id, null, ct))));
                    AssertEqual(3, claims.Count(success => success));
                    Captain occupied = (await db.Captains.ReadAsync(captain.Id))!;
                    AssertEqual(3, occupied.ActiveMissionCount);
                    AssertEqual(3, occupied.ActiveMissionIds.Count);
                    AssertEqual(3, (await db.Missions.EnumerateByStatusAsync(MissionStatusEnum.Assigned)).Count);
                    string released = occupied.ActiveMissionIds[1];
                    AssertTrue(await db.Captains.TryReleaseFromMissionAsync(captain.Id, released));
                    AssertFalse(await db.Captains.TryReleaseFromMissionAsync(captain.Id, released));
                    AssertEqual(2, (await db.Captains.ReadAsync(captain.Id))!.ActiveMissionCount);
                    Mission done = (await db.Missions.ReadAsync(released))!;
                    done.Status = MissionStatusEnum.Complete;
                    await db.Missions.UpdateAsync(done);
                    await db.Captains.ReconcileMissionSlotsAsync();
                    occupied = (await db.Captains.ReadAsync(captain.Id))!;
                    AssertEqual(2, occupied.ActiveMissionCount);
                    AssertFalse(occupied.ActiveMissionIds.Contains(released));
                    AssertEqual(CaptainStateEnum.Working, occupied.State);
                    // Stale operational snapshots cannot undo a concurrent configuration change.
                    Captain stale = occupied;
                    Captain edit = (await db.Captains.ReadAsync(captain.Id))!;
                    edit.MaxConcurrency = 1;
                    await db.Captains.UpdateAsync(edit, updateConcurrency: true);
                    await db.Captains.UpdateAsync(stale);
                    AssertEqual(1, (await db.Captains.ReadAsync(captain.Id))!.MaxConcurrency);
                    Mission extra = await db.Missions.CreateAsync(new Mission("extra"));
                    AssertFalse(await db.Captains.TryClaimAsync(captain.Id, extra.Id, null));
                }, tags: new List<string> { TestTags.Positive }),
                new TestCaseDescriptor(suite, "exclusive_and_quarantine", "Planning excludes claims; quarantined owners retain sibling slots when one closes", async ct =>
                {
                    using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                    var db = fixture.Driver;
                    Captain captain = await db.Captains.CreateAsync(new Captain("exclusive") { MaxConcurrency = 3, TenantId = Armada.Core.Constants.DefaultTenantId });
                    Mission first = await db.Missions.CreateAsync(new Mission("one"));
                    Mission second = await db.Missions.CreateAsync(new Mission("two"));
                    AssertTrue(await db.Captains.TryClaimAsync(captain.Id, first.Id, null));
                    AssertTrue(await db.Captains.TryClaimAsync(captain.Id, second.Id, null));
                    AssertFalse(await db.Captains.TryReserveAsync(captain.TenantId!, captain.Id, CaptainStateEnum.Planning));
                    await db.Captains.UpdateStateAsync(captain.Id, CaptainStateEnum.Quarantined);
                    AssertTrue(await db.Captains.TryReleaseFromMissionAsync(captain.Id, first.Id));
                    Captain held = (await db.Captains.ReadAsync(captain.Id))!;
                    AssertEqual(CaptainStateEnum.Quarantined, held.State);
                    AssertEqual(1, held.ActiveMissionCount);
                    AssertTrue(held.HoldsMission(second.Id));
                    Mission extra = await db.Missions.CreateAsync(new Mission("three"));
                    AssertFalse(await db.Captains.TryClaimAsync(captain.Id, extra.Id, null));
                }, tags: new List<string> { TestTags.Positive })
            });
        }
    }
}
