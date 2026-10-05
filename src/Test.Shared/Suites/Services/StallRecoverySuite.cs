namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The heartbeat watchdog's stall branch in <see cref="AdmiralService.HealthCheckAsync"/>: a captain whose process
    /// is alive but has produced no output for longer than <see cref="ArmadaSettings.StallThresholdMinutes"/>. The
    /// test process itself stands in for the "alive" agent process, and OnStopAgent is a recorder, so nothing is killed.
    /// </summary>
    public sealed class StallRecoverySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.StallRecovery";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("stall_under_limit_stops_and_relaunches", "Stalled captain under the recovery limit is stopped and relaunched", TestTags.Reliability, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxRecoveryAttempts = 2;
                    StallTestFixture fixture = await StallTestFixture.CreateAsync(testDb.Driver, settings, DateTime.UtcNow.AddMinutes(-(settings.StallThresholdMinutes + 5))).ConfigureAwait(false);

                    await fixture.Admiral.HealthCheckAsync().ConfigureAwait(false);

                    Captain? captain = await testDb.Driver.Captains.ReadAsync(fixture.Captain.Id).ConfigureAwait(false);
                    Mission? mission = await testDb.Driver.Missions.ReadAsync(fixture.Mission.Id).ConfigureAwait(false);
                    AssertEqual(1, fixture.Stopped.Count, "stalled process is stopped before recovery");
                    AssertEqual(1, fixture.Launched.Count, "agent is relaunched in the same dock");
                    AssertEqual(1, captain!.RecoveryAttempts, "recovery attempt counted");
                    AssertEqual(CaptainStateEnum.Working, captain.State);
                    AssertEqual(MissionStatusEnum.InProgress, mission!.Status, "mission keeps running after recovery");
                    AssertEqual(4242, mission.ProcessId, "mission records the relaunched process");
                }
            }));

            cases.Add(CaseAsync("stall_recovery_exhausted_fails_mission_and_releases", "Stalled captain with recovery exhausted fails the mission and is released", TestTags.Reliability, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxRecoveryAttempts = 1;
                    StallTestFixture fixture = await StallTestFixture.CreateAsync(testDb.Driver, settings, DateTime.UtcNow.AddMinutes(-(settings.StallThresholdMinutes + 5))).ConfigureAwait(false);
                    fixture.Captain.RecoveryAttempts = 1;
                    await testDb.Driver.Captains.UpdateAsync(fixture.Captain).ConfigureAwait(false);

                    await fixture.Admiral.HealthCheckAsync().ConfigureAwait(false);

                    Captain? captain = await testDb.Driver.Captains.ReadAsync(fixture.Captain.Id).ConfigureAwait(false);
                    Mission? mission = await testDb.Driver.Missions.ReadAsync(fixture.Mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Failed, mission!.Status);
                    AssertEqual("Captain stalled, recovery exhausted", mission.FailureReason);
                    AssertEqual(MissionFailureKindEnum.StallRecoveryExhausted, mission.FailureKind, "failure kind is recorded where the failure happens");
                    AssertNull(mission.ProcessId, "process id cleared");
                    AssertNotNull(mission.CompletedUtc, "completion time recorded");
                    AssertEqual(1, fixture.Stopped.Count, "stalled process is stopped");
                    AssertEqual(0, fixture.Launched.Count, "no relaunch after exhaustion");
                    AssertEqual(CaptainStateEnum.Idle, captain!.State, "captain released for new work");
                    AssertNull(captain.CurrentMissionId);

                    List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateByMissionAsync(fixture.Mission.Id).ConfigureAwait(false);
                    AssertTrue(events.Exists(e => e.EventType == "mission.failed"), "mission.failed event emitted");
                }
            }));

            cases.Add(CaseAsync("fresh_heartbeat_is_left_alone", "Captain with a recent heartbeat is not treated as stalled", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ArmadaSettings settings = CreateSettings();
                    StallTestFixture fixture = await StallTestFixture.CreateAsync(testDb.Driver, settings, DateTime.UtcNow.AddSeconds(-30)).ConfigureAwait(false);

                    await fixture.Admiral.HealthCheckAsync().ConfigureAwait(false);

                    Captain? captain = await testDb.Driver.Captains.ReadAsync(fixture.Captain.Id).ConfigureAwait(false);
                    AssertEqual(0, fixture.Stopped.Count);
                    AssertEqual(0, fixture.Launched.Count);
                    AssertEqual(0, captain!.RecoveryAttempts);
                    AssertEqual(CaptainStateEnum.Working, captain.State);
                }
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Stalled Captain Recovery", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static ArmadaSettings CreateSettings()
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_test_docks_" + Guid.NewGuid().ToString("N"));
            settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_test_repos_" + Guid.NewGuid().ToString("N"));
            settings.MaxMissionRuntimeMinutes = 0;
            return settings;
        }

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
