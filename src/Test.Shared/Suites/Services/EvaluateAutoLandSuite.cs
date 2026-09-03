namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="MissionService.EvaluateAutoLandAsync"/>, the auto-land dry-run. Positive
    /// cases confirm a small in-scope change reports Land; negative cases confirm an over-threshold change
    /// reports a hold with a reason and a missing vessel reports null.
    /// </summary>
    public sealed class EvaluateAutoLandSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the evaluate-auto-land suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("small_change_reports_land", "A small in-scope change reports Land", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                MissionService service = CreateService(testDb);

                Vessel vessel = new Vessel("autoland-vessel", "https://github.com/test/repo.git");
                vessel.AutoLandEnabled = true;
                vessel.AutoLandMaxFiles = 10;
                vessel.AutoLandMaxLines = 400;
                vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                Mission mission = new Mission("Small change", "desc");
                mission.VesselId = vessel.Id;
                mission.DiffSnapshot = "diff --git a/src/a.cs b/src/a.cs\n+++ b/src/a.cs\n+one line\n";
                mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                AutoLandDecision? decision = await service.EvaluateAutoLandAsync(mission.Id).ConfigureAwait(false);
                AssertNotNull(decision, "a mission with a vessel should yield a decision");
                AssertTrue(decision!.Land, "a small change under thresholds should auto-land");
            }));

            cases.Add(CaseAsync("over_threshold_reports_hold", "An over-threshold change reports a hold with a reason", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                MissionService service = CreateService(testDb);

                Vessel vessel = new Vessel("autoland-vessel", "https://github.com/test/repo.git");
                vessel.AutoLandEnabled = true;
                vessel.AutoLandMaxFiles = 1;
                vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                Mission mission = new Mission("Big change", "desc");
                mission.VesselId = vessel.Id;
                mission.DiffSnapshot =
                    "diff --git a/src/a.cs b/src/a.cs\n+++ b/src/a.cs\n+x\n" +
                    "diff --git a/src/b.cs b/src/b.cs\n+++ b/src/b.cs\n+y\n" +
                    "diff --git a/src/c.cs b/src/c.cs\n+++ b/src/c.cs\n+z\n";
                mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                AutoLandDecision? decision = await service.EvaluateAutoLandAsync(mission.Id).ConfigureAwait(false);
                AssertNotNull(decision, "a mission with a vessel should yield a decision");
                AssertFalse(decision!.Land, "an over-file-limit change should hold");
                AssertNotNull(decision.HoldReason, "a hold should carry a reason");
            }));

            cases.Add(CaseAsync("no_vessel_reports_null", "A mission with no vessel reports null", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                MissionService service = CreateService(testDb);

                Mission mission = new Mission("Orphan", "desc");
                mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                AutoLandDecision? decision = await service.EvaluateAutoLandAsync(mission.Id).ConfigureAwait(false);
                AssertNull(decision, "a mission with no vessel yields no decision");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.EvaluateAutoLand",
                displayName: "Evaluate AutoLand",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static MissionService CreateService(TestDatabase testDb)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            ArmadaSettings settings = new ArmadaSettings();
            StubGitService git = new StubGitService();
            DockService dockService = new DockService(logging, testDb.Driver, settings, git);
            CaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
            return new MissionService(logging, testDb.Driver, settings, dockService, captainService);
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.EvaluateAutoLand",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (System.Threading.CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
