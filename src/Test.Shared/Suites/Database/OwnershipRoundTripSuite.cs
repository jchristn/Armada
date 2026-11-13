namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Owner and policy columns survive a write and read on every provider. PostgreSQL and MySQL readers once dropped
    /// <c>user_id</c> for events, signals and docks, and MySQL dropped vessel branch policy.
    /// </summary>
    public sealed class OwnershipRoundTripSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Database.OwnershipRoundTrip";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(SuiteId, "event_signal_dock_keep_their_owner", "Events, signals and docks read back with their tenant and user", OwnersAsync),
                new TestCaseDescriptor(SuiteId, "vessel_branch_policy_round_trips", "A vessel's branch policy reads back as written", BranchPolicyAsync)
            };
            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Owner and policy round trips", cases: cases);
        }

        private static async Task OwnersAsync(CancellationToken token)
        {
            using TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            TenantMetadata tenant = await database.Driver.Tenants.CreateAsync(new TenantMetadata("ownership"), token).ConfigureAwait(false);
            UserMaster owner = await database.Driver.Users.CreateAsync(new UserMaster(tenant.Id, "owner@example.invalid", "fixture-password"), token).ConfigureAwait(false);
            Vessel vessel = await database.Driver.Vessels.CreateAsync(new Vessel("ownership", "https://example.invalid/repo") { TenantId = tenant.Id, UserId = owner.Id }, token).ConfigureAwait(false);

            ArmadaEvent evt = await database.Driver.Events.CreateAsync(new ArmadaEvent("ownership.test", "owned event") { TenantId = tenant.Id, UserId = owner.Id }, token).ConfigureAwait(false);
            ArmadaEvent? readEvent = await database.Driver.Events.ReadAsync(tenant.Id, evt.Id, token).ConfigureAwait(false);
            AssertEqual(tenant.Id, readEvent!.TenantId); AssertEqual(owner.Id, readEvent.UserId, "event owner");
            AssertEqual(owner.Id, (await database.Driver.Events.EnumerateByTypeAsync("ownership.test", token: token).ConfigureAwait(false))[0].UserId, "event owner by type");

            Signal signal = await database.Driver.Signals.CreateAsync(new Signal(SignalTypeEnum.Nudge, "owned signal") { TenantId = tenant.Id, UserId = owner.Id }, token).ConfigureAwait(false);
            Signal? readSignal = await database.Driver.Signals.ReadAsync(tenant.Id, signal.Id, token).ConfigureAwait(false);
            AssertEqual(tenant.Id, readSignal!.TenantId); AssertEqual(owner.Id, readSignal.UserId, "signal owner");

            Dock dock = await database.Driver.Docks.CreateAsync(new Dock(vessel.Id) { TenantId = tenant.Id, UserId = owner.Id, WorktreePath = "/tmp/ownership", BranchName = "ownership" }, token).ConfigureAwait(false);
            Dock? readDock = await database.Driver.Docks.ReadAsync(tenant.Id, dock.Id, token).ConfigureAwait(false);
            AssertEqual(tenant.Id, readDock!.TenantId); AssertEqual(owner.Id, readDock.UserId, "dock owner");
            AssertNotNull(await database.Driver.Docks.ReadAsync(tenant.Id, owner.Id, dock.Id, token).ConfigureAwait(false), "owner-scoped dock read");
        }

        private static async Task BranchPolicyAsync(CancellationToken token)
        {
            using TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            Vessel vessel = await database.Driver.Vessels.CreateAsync(new Vessel("policy", "https://example.invalid/repo")
            {
                ProtectedBranchPatterns = new List<string> { "main", "release/*" }, ReleaseBranchPrefix = "rel/", HotfixBranchPrefix = "fix/",
                RequirePullRequestForProtectedBranches = true, RequireMergeQueueForReleaseBranches = true
            }, token).ConfigureAwait(false);
            Vessel read = (await database.Driver.Vessels.ReadAsync(vessel.Id, token).ConfigureAwait(false))!;
            AssertEqual("main,release/*", String.Join(",", read.ProtectedBranchPatterns));
            AssertEqual("rel/", read.ReleaseBranchPrefix); AssertEqual("fix/", read.HotfixBranchPrefix);
            AssertTrue(read.RequirePullRequestForProtectedBranches, "pull request requirement");
            AssertTrue(read.RequireMergeQueueForReleaseBranches, "merge queue requirement");
        }
    }
}
