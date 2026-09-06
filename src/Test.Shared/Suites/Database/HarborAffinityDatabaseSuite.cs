namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the v63 Harbor routing/affinity columns: the dock's owning Harbor, a mission's
    /// assigned Harbor, and a vessel's preferred Harbor and required capabilities must round-trip through the
    /// database. These are the columns the router reads for affinity and eligibility.
    /// </summary>
    public sealed class HarborAffinityDatabaseSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Harbor affinity suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("dock_harbor_id_roundtrips", "Dock.HarborId round-trips", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await SetupAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Vessel vessel = await SeedVesselAsync(db).ConfigureAwait(false);

                Dock dock = new Dock(vessel.Id);
                dock.HarborId = "hbr_owner";
                await db.Docks.CreateAsync(dock).ConfigureAwait(false);

                Dock? reloaded = await db.Docks.ReadAsync(dock.Id).ConfigureAwait(false);
                AssertNotNull(reloaded, "Expected the dock to reload.");
                AssertEqual("hbr_owner", reloaded!.HarborId);
            }));

            cases.Add(CaseAsync("vessel_routing_hints_roundtrip", "Vessel routing hints round-trip", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await SetupAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Vessel vessel = await SeedVesselAsync(db).ConfigureAwait(false);

                vessel.PreferredHarborId = "hbr_pref";
                vessel.RequiredCapabilities = "claude,gh";
                await db.Vessels.UpdateAsync(vessel).ConfigureAwait(false);

                Vessel? reloaded = await db.Vessels.ReadAsync(vessel.Id).ConfigureAwait(false);
                AssertNotNull(reloaded, "Expected the vessel to reload.");
                AssertEqual("hbr_pref", reloaded!.PreferredHarborId);
                AssertEqual("claude,gh", reloaded.RequiredCapabilities);
            }));

            cases.Add(CaseAsync("mission_assigned_harbor_roundtrips", "Mission.AssignedHarborId round-trips", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await SetupAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Vessel vessel = await SeedVesselAsync(db).ConfigureAwait(false);

                Mission mission = new Mission("Routed mission");
                mission.VesselId = vessel.Id;
                mission.AssignedHarborId = "hbr_assigned";
                await db.Missions.CreateAsync(mission).ConfigureAwait(false);

                Mission? reloaded = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                AssertNotNull(reloaded, "Expected the mission to reload.");
                AssertEqual("hbr_assigned", reloaded!.AssignedHarborId);
            }));

            cases.Add(CaseAsync("dock_harbor_id_defaults_null", "Dock.HarborId defaults to null in Local mode", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await SetupAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Vessel vessel = await SeedVesselAsync(db).ConfigureAwait(false);

                Dock dock = new Dock(vessel.Id);
                await db.Docks.CreateAsync(dock).ConfigureAwait(false);

                Dock? reloaded = await db.Docks.ReadAsync(dock.Id).ConfigureAwait(false);
                AssertNotNull(reloaded, "Expected the dock to reload.");
                AssertNull(reloaded!.HarborId, "Expected HarborId to be null when no Harbor owns the dock.");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Database.HarborAffinity",
                displayName: "Harbor Affinity Columns",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static Task<TestDatabase> SetupAsync()
        {
            return TestDatabaseHelper.CreateDatabaseAsync();
        }

        private static async Task<Vessel> SeedVesselAsync(DatabaseDriver db)
        {
            Fleet fleet = new Fleet("HarborAffinityFleet");
            await db.Fleets.CreateAsync(fleet).ConfigureAwait(false);
            Vessel vessel = new Vessel("HarborAffinityVessel", "https://github.com/test/repo");
            vessel.FleetId = fleet.Id;
            await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);
            return vessel;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Database.HarborAffinity",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
