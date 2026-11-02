namespace Test.Shared.Suites.Database
{
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
    /// Referential-integrity descriptors for the configured database provider. Cases verify that
    /// deleting a parent row severs child references according to the declared ON DELETE behavior:
    /// ON DELETE SET NULL nulls the child's foreign-key column (fleet/vessel/voyage/captain on
    /// missions, captain on docks and signals) while the child row survives, and ON DELETE CASCADE
    /// removes dependent dock rows outright. SQL Server captain deletes null signal captain
    /// references in application code; a scoped delete that does not remove the captain leaves
    /// those references unchanged, and a delete that fails leaves them unchanged. This suite is
    /// negative-heavy by nature: every case asserts that a reference is broken (a nulled key or a
    /// now-missing child), or that a non-matching or failed delete leaves it intact, and the audit
    /// case confirms that inserting a child that points at a non-existent parent is rejected outright.
    /// </summary>
    public sealed class ForeignKeySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.ForeignKey";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Foreign Key suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("delete_fleet_sets_vessel_fleet_id_null", "DeleteFleet sets vessel FleetId null", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("FK Fleet");
                    await db.Fleets.CreateAsync(fleet);

                    Vessel vessel = new Vessel("FK Vessel", "https://github.com/test/repo");
                    vessel.FleetId = fleet.Id;
                    await db.Vessels.CreateAsync(vessel);

                    await db.Fleets.DeleteAsync(fleet.Id);

                    Vessel? result = await db.Vessels.ReadAsync(vessel.Id);
                    AssertNotNull(result);
                    AssertNull(result!.FleetId);
                }
            }));

            cases.Add(CaseAsync("delete_voyage_sets_mission_voyage_id_null", "DeleteVoyage sets mission VoyageId null", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Voyage voyage = new Voyage("FK Voyage");
                    await db.Voyages.CreateAsync(voyage);

                    Mission mission = new Mission("FK Mission");
                    mission.VoyageId = voyage.Id;
                    await db.Missions.CreateAsync(mission);

                    await db.Voyages.DeleteAsync(voyage.Id);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(result);
                    AssertNull(result!.VoyageId);
                }
            }));

            cases.Add(CaseAsync("delete_captain_sets_mission_captain_id_null", "DeleteCaptain sets mission CaptainId null", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain captain = new Captain("fk-captain");
                    await db.Captains.CreateAsync(captain);

                    Mission mission = new Mission("FK Captain Mission");
                    mission.CaptainId = captain.Id;
                    await db.Missions.CreateAsync(mission);

                    await db.Captains.DeleteAsync(captain.Id);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(result);
                    AssertNull(result!.CaptainId);
                }
            }));

            cases.Add(CaseAsync("delete_vessel_sets_mission_vessel_id_null", "DeleteVessel sets mission VesselId null", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("FK Fleet2");
                    await db.Fleets.CreateAsync(fleet);

                    Vessel vessel = new Vessel("FK Vessel2", "https://github.com/test/repo2");
                    vessel.FleetId = fleet.Id;
                    await db.Vessels.CreateAsync(vessel);

                    Mission mission = new Mission("FK Vessel Mission");
                    mission.VesselId = vessel.Id;
                    await db.Missions.CreateAsync(mission);

                    await db.Vessels.DeleteAsync(vessel.Id);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(result);
                    AssertNull(result!.VesselId);
                }
            }));

            cases.Add(CaseAsync("delete_vessel_cascade_deletes_docks", "DeleteVessel cascade deletes docks", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("Cascade Fleet");
                    await db.Fleets.CreateAsync(fleet);

                    Vessel vessel = new Vessel("Cascade Vessel", "https://github.com/test/cascade");
                    vessel.FleetId = fleet.Id;
                    await db.Vessels.CreateAsync(vessel);

                    Dock dock = new Dock(vessel.Id);
                    dock.WorktreePath = "/path/to/worktree";
                    await db.Docks.CreateAsync(dock);

                    await db.Vessels.DeleteAsync(vessel.Id);

                    Dock? result = await db.Docks.ReadAsync(dock.Id);
                    AssertNull(result);
                }
            }));

            cases.Add(CaseAsync("delete_captain_sets_dock_captain_id_null", "DeleteCaptain sets dock CaptainId null", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("Dock FK Fleet");
                    await db.Fleets.CreateAsync(fleet);

                    Vessel vessel = new Vessel("Dock FK Vessel", "https://github.com/test/dockfk");
                    vessel.FleetId = fleet.Id;
                    await db.Vessels.CreateAsync(vessel);

                    Captain captain = new Captain("dock-fk-captain");
                    await db.Captains.CreateAsync(captain);

                    Dock dock = new Dock(vessel.Id);
                    dock.WorktreePath = "/path/to/worktree2";
                    dock.CaptainId = captain.Id;
                    await db.Docks.CreateAsync(dock);

                    await db.Captains.DeleteAsync(captain.Id);

                    Dock? result = await db.Docks.ReadAsync(dock.Id);
                    AssertNotNull(result);
                    AssertNull(result!.CaptainId);
                }
            }));

            cases.Add(CaseAsync("delete_captain_sets_signal_captain_ids_null", "DeleteCaptain sets signal CaptainIds null", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Captain sender = new Captain("signal-sender");
                    Captain receiver = new Captain("signal-receiver");
                    await db.Captains.CreateAsync(sender);
                    await db.Captains.CreateAsync(receiver);

                    Signal signal = new Signal(SignalTypeEnum.Nudge, "test");
                    signal.FromCaptainId = sender.Id;
                    signal.ToCaptainId = receiver.Id;
                    await db.Signals.CreateAsync(signal);

                    await db.Captains.DeleteAsync(sender.Id);

                    Signal? result = await db.Signals.ReadAsync(signal.Id);
                    AssertNotNull(result);
                    AssertNull(result!.FromCaptainId);
                    AssertEqual(receiver.Id, result.ToCaptainId);
                }
            }));

            cases.Add(CaseAsync("tenant_scoped_delete_miss_keeps_signal_references", "Tenant-scoped delete miss keeps signal references", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    ScopedCaptainSignals seeded = await SeedScopedCaptainSignalsAsync(db);

                    await db.Captains.DeleteAsync(seeded.OtherTenantId, seeded.Captain.Id);

                    Captain? captain = await db.Captains.ReadAsync(seeded.Captain.Id);
                    Signal? outbound = await db.Signals.ReadAsync(seeded.Outbound.Id);
                    Signal? inbound = await db.Signals.ReadAsync(seeded.Inbound.Id);
                    AssertNotNull(captain);
                    AssertNotNull(outbound);
                    AssertNotNull(inbound);
                    AssertEqual(seeded.Captain.Id, outbound!.FromCaptainId, "outbound from");
                    AssertEqual(seeded.Peer.Id, outbound.ToCaptainId, "outbound to");
                    AssertEqual(seeded.Peer.Id, inbound!.FromCaptainId, "inbound from");
                    AssertEqual(seeded.Captain.Id, inbound.ToCaptainId, "inbound to");
                }
            }));

            cases.Add(CaseAsync("user_scoped_delete_clears_signal_references", "User-scoped delete clears signal references", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    ScopedCaptainSignals seeded = await SeedScopedCaptainSignalsAsync(db);

                    await db.Captains.DeleteAsync(seeded.TenantId, seeded.UserId, seeded.Captain.Id);

                    AssertNull(await db.Captains.ReadAsync(seeded.Captain.Id));
                    Signal? outbound = await db.Signals.ReadAsync(seeded.Outbound.Id);
                    Signal? inbound = await db.Signals.ReadAsync(seeded.Inbound.Id);
                    AssertNotNull(outbound);
                    AssertNotNull(inbound);
                    AssertNull(outbound!.FromCaptainId, "outbound from");
                    AssertEqual(seeded.Peer.Id, outbound.ToCaptainId, "outbound to");
                    AssertEqual(seeded.Peer.Id, inbound!.FromCaptainId, "inbound from");
                    AssertNull(inbound.ToCaptainId, "inbound to");
                    AssertNotNull(await db.Captains.ReadAsync(seeded.Peer.Id));
                }
            }));

            cases.Add(CaseAsync("blocked_captain_delete_keeps_signal_references", "Blocked captain delete keeps signal references", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    ScopedCaptainSignals seeded = await SeedScopedCaptainSignalsAsync(db);
                    Objective objective = new Objective();
                    objective.Title = "Signal reference objective";
                    objective.TenantId = seeded.TenantId;
                    objective.UserId = seeded.UserId;
                    await db.Objectives.CreateAsync(objective);
                    ObjectiveRefinementSession session = new ObjectiveRefinementSession();
                    session.ObjectiveId = objective.Id;
                    session.TenantId = seeded.TenantId;
                    session.UserId = seeded.UserId;
                    session.CaptainId = seeded.Captain.Id;
                    session.Title = "Signal reference session";
                    await db.ObjectiveRefinementSessions.CreateAsync(session);

                    Exception? deleteFailure = null;
                    try
                    {
                        await db.Captains.DeleteAsync(seeded.TenantId, seeded.Captain.Id);
                    }
                    catch (Exception ex)
                    {
                        deleteFailure = ex;
                    }

                    Captain? captain = await db.Captains.ReadAsync(seeded.Captain.Id);
                    Signal? outbound = await db.Signals.ReadAsync(seeded.Outbound.Id);
                    Signal? inbound = await db.Signals.ReadAsync(seeded.Inbound.Id);
                    AssertNotNull(outbound);
                    AssertNotNull(inbound);
                    if (captain != null)
                    {
                        AssertNotNull(deleteFailure);
                        AssertEqual(seeded.Captain.Id, outbound!.FromCaptainId, "outbound from");
                        AssertEqual(seeded.Peer.Id, outbound.ToCaptainId, "outbound to");
                        AssertEqual(seeded.Peer.Id, inbound!.FromCaptainId, "inbound from");
                        AssertEqual(seeded.Captain.Id, inbound.ToCaptainId, "inbound to");
                        ObjectiveRefinementSession? blocking = await db.ObjectiveRefinementSessions.ReadAsync(session.Id);
                        AssertNotNull(blocking);
                        AssertEqual(seeded.Captain.Id, blocking!.CaptainId, "blocking session");
                    }
                    else
                    {
                        AssertNull(deleteFailure);
                        AssertNull(outbound!.FromCaptainId, "outbound from");
                        AssertEqual(seeded.Peer.Id, outbound.ToCaptainId, "outbound to");
                        AssertEqual(seeded.Peer.Id, inbound!.FromCaptainId, "inbound from");
                        AssertNull(inbound.ToCaptainId, "inbound to");
                    }
                }
            }));

            cases.Add(CaseAsync("create_child_with_missing_parent_rejected_audit", "CreateChild MissingParent Rejected", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Vessel vessel = new Vessel("Orphan Vessel", "https://github.com/test/orphan");
                    vessel.FleetId = "flt_does_not_exist";
                    await AssertThrowsAsync<Exception>(() => db.Vessels.CreateAsync(vessel));
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Foreign Key",
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

        private static async Task<ScopedCaptainSignals> SeedScopedCaptainSignalsAsync(DatabaseDriver db)
        {
            TenantMetadata tenant = new TenantMetadata("Scope tenant");
            TenantMetadata otherTenant = new TenantMetadata("Other scope tenant");
            await db.Tenants.CreateAsync(tenant);
            await db.Tenants.CreateAsync(otherTenant);
            UserMaster user = await db.Users.CreateAsync(new UserMaster(tenant.Id, "scope-captain@example.com", "fixture-pass"));

            Captain captain = new Captain("scoped-captain");
            captain.TenantId = tenant.Id;
            captain.UserId = user.Id;
            Captain peer = new Captain("signal-peer");
            captain = await db.Captains.CreateAsync(captain);
            peer = await db.Captains.CreateAsync(peer);

            Signal outbound = new Signal(SignalTypeEnum.Nudge, "outbound");
            outbound.FromCaptainId = captain.Id;
            outbound.ToCaptainId = peer.Id;
            Signal inbound = new Signal(SignalTypeEnum.Nudge, "inbound");
            inbound.FromCaptainId = peer.Id;
            inbound.ToCaptainId = captain.Id;
            await db.Signals.CreateAsync(outbound);
            await db.Signals.CreateAsync(inbound);

            ScopedCaptainSignals seeded = new ScopedCaptainSignals();
            seeded.TenantId = tenant.Id;
            seeded.OtherTenantId = otherTenant.Id;
            seeded.UserId = user.Id;
            seeded.Captain = captain;
            seeded.Peer = peer;
            seeded.Outbound = outbound;
            seeded.Inbound = inbound;
            return seeded;
        }

        private sealed class ScopedCaptainSignals
        {
            public string TenantId { get; set; } = String.Empty;
            public string OtherTenantId { get; set; } = String.Empty;
            public string UserId { get; set; } = String.Empty;
            public Captain Captain { get; set; } = null!;
            public Captain Peer { get; set; } = null!;
            public Signal Outbound { get; set; } = null!;
            public Signal Inbound { get; set; } = null!;
        }

        #endregion
    }
}
