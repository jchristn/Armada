namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
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
    /// Descriptors for <see cref="MissionRecoveryCoordinator"/> over a live SQLite store. Positive cases
    /// confirm a recoverable failure opens an incident and dispatches a bounded rescue, and that a rescue
    /// success advances the incident Open -&gt; Mitigated -&gt; Closed. Negative cases confirm a non-recoverable
    /// failure opens no incident, a failed rescue re-dispatches until the cap and then leaves the incident
    /// open, and repeated passes never duplicate an incident.
    /// </summary>
    public sealed class MissionRecoveryCoordinatorSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the mission-recovery-coordinator suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("recoverable_failure_opens_incident_and_dispatches_rescue", "A recoverable failure opens an incident and dispatches a rescue", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.MaxMissionRecoveryAttempts = 2;
                MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, settings);
                IncidentService incidents = new IncidentService(testDb.Driver);

                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Mission failed = await CreateFailedMissionAsync(testDb, vessel.Id, "definition_of_done_compile: build phase exited 1").ConfigureAwait(false);

                await coordinator.MaintainAsync().ConfigureAwait(false);

                List<Incident> recovery = await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false);
                AssertEqual(1, recovery.Count, "exactly one recovery incident should open");
                Incident incident = recovery[0];
                AssertEqual("Compile", incident.FailureKind, "the incident should record the classified failure kind");
                AssertEqual(failed.Id, incident.MissionId, "the incident should link the failed mission");
                AssertEqual(IncidentStatusEnum.Open, incident.Status, "a fresh recovery incident is Open");
                AssertEqual(1, incident.RecoveryAttempts, "one rescue should be dispatched");
                AssertEqual(1, incident.RescueMissionIds.Count, "one rescue mission should be linked");

                Mission? rescue = await testDb.Driver.Missions.ReadAsync(incident.RescueMissionIds[0]).ConfigureAwait(false);
                AssertNotNull(rescue, "the rescue mission should exist");
                AssertEqual(MissionStatusEnum.Pending, rescue!.Status, "the rescue mission starts Pending for dispatch");
                AssertEqual(failed.Id, rescue.ParentMissionId, "the rescue links back to the failed mission");
                AssertTrue(rescue.Title.StartsWith("[Rescue]", StringComparison.Ordinal), "the rescue title is marked");
            }));

            cases.Add(CaseAsync("non_recoverable_failure_opens_no_incident", "A non-recoverable failure opens no incident", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.MaxMissionRecoveryAttempts = 2;
                MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, settings);
                IncidentService incidents = new IncidentService(testDb.Driver);

                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                await CreateFailedMissionAsync(testDb, vessel.Id, "Judge verdict: FAIL").ConfigureAwait(false);

                await coordinator.MaintainAsync().ConfigureAwait(false);

                List<Incident> recovery = await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false);
                AssertEqual(0, recovery.Count, "a judge rejection is not auto-rescued and opens no incident");
            }));

            cases.Add(CaseAsync("rescue_success_mitigates_then_closes", "A rescue success advances the incident Open -> Mitigated -> Closed", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.MaxMissionRecoveryAttempts = 2;
                MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, settings);
                IncidentService incidents = new IncidentService(testDb.Driver);

                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                await CreateFailedMissionAsync(testDb, vessel.Id, "definition_of_done_testfail: test phase exited 1").ConfigureAwait(false);

                await coordinator.MaintainAsync().ConfigureAwait(false);
                Incident incident = (await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false))[0];

                // The rescue lands successfully.
                Mission rescue = (await testDb.Driver.Missions.ReadAsync(incident.RescueMissionIds[0]).ConfigureAwait(false))!;
                rescue.Status = MissionStatusEnum.Complete;
                await testDb.Driver.Missions.UpdateAsync(rescue).ConfigureAwait(false);

                await coordinator.MaintainAsync().ConfigureAwait(false);
                Incident mitigated = (await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false))[0];
                AssertEqual(IncidentStatusEnum.Mitigated, mitigated.Status, "a landed rescue mitigates the incident");

                await coordinator.MaintainAsync().ConfigureAwait(false);
                Incident closed = (await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false))[0];
                AssertEqual(IncidentStatusEnum.Closed, closed.Status, "a held mitigation closes the incident");
            }));

            cases.Add(CaseAsync("failed_rescue_redispatches_until_cap", "A failed rescue re-dispatches until the cap and then leaves the incident open", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.MaxMissionRecoveryAttempts = 2;
                MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, settings);
                IncidentService incidents = new IncidentService(testDb.Driver);

                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                await CreateFailedMissionAsync(testDb, vessel.Id, "definition_of_done_compile: build phase exited 1").ConfigureAwait(false);

                await coordinator.MaintainAsync().ConfigureAwait(false);
                Incident incident = (await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false))[0];
                AssertEqual(1, incident.RecoveryAttempts, "first rescue dispatched");

                // First rescue fails -> a second rescue should be dispatched (under the cap of 2).
                await FailMissionAsync(testDb, incident.RescueMissionIds[0], "definition_of_done_compile: build phase exited 1").ConfigureAwait(false);
                await coordinator.MaintainAsync().ConfigureAwait(false);
                incident = (await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false))[0];
                AssertEqual(2, incident.RecoveryAttempts, "second rescue dispatched under the cap");
                AssertEqual(2, incident.RescueMissionIds.Count, "two rescues linked");
                AssertEqual(IncidentStatusEnum.Open, incident.Status, "incident still open while retrying");

                // Second rescue fails -> the cap is reached; the incident stays open for a human.
                await FailMissionAsync(testDb, incident.RescueMissionIds[1], "definition_of_done_compile: build phase exited 1").ConfigureAwait(false);
                await coordinator.MaintainAsync().ConfigureAwait(false);
                incident = (await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false))[0];
                AssertEqual(2, incident.RecoveryAttempts, "no rescue beyond the cap");
                AssertEqual(IncidentStatusEnum.Open, incident.Status, "an exhausted incident is left open for a human");
                AssertContains("exhausted", incident.RecoveryNotes ?? String.Empty, "the incident notes explain the exhausted budget");
            }));

            cases.Add(CaseAsync("repeated_passes_do_not_duplicate_incident", "Repeated passes never duplicate an incident", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.MaxMissionRecoveryAttempts = 2;
                MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, settings);
                IncidentService incidents = new IncidentService(testDb.Driver);

                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                await CreateFailedMissionAsync(testDb, vessel.Id, "definition_of_done_compile: build phase exited 1").ConfigureAwait(false);

                await coordinator.MaintainAsync().ConfigureAwait(false);
                await coordinator.MaintainAsync().ConfigureAwait(false);

                List<Incident> recovery = await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false);
                AssertEqual(1, recovery.Count, "the incident is not duplicated across passes");
                AssertEqual(1, recovery[0].RecoveryAttempts, "no extra rescue while one is still in flight");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.MissionRecoveryCoordinator",
                displayName: "Mission Recovery Coordinator",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static async Task<Vessel> CreateVesselAsync(TestDatabase testDb)
        {
            Vessel vessel = new Vessel("recovery-vessel", "https://github.com/test/repo.git");
            vessel.DefaultBranch = "main";
            return await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
        }

        private static async Task<Mission> CreateFailedMissionAsync(TestDatabase testDb, string vesselId, string failureReason)
        {
            Mission mission = new Mission("Do the thing", "Implement the thing correctly.");
            mission.VesselId = vesselId;
            mission.Status = MissionStatusEnum.Failed;
            mission.FailureReason = failureReason;
            mission.DiffSnapshot = "diff --git a/x.cs b/x.cs\n+++ b/x.cs\n+broken";
            return await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);
        }

        private static async Task FailMissionAsync(TestDatabase testDb, string missionId, string failureReason)
        {
            Mission mission = (await testDb.Driver.Missions.ReadAsync(missionId).ConfigureAwait(false))!;
            mission.Status = MissionStatusEnum.Failed;
            mission.FailureReason = failureReason;
            await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
        }

        private static async Task<List<Incident>> ReadRecoveryIncidentsAsync(IncidentService incidents)
        {
            AuthContext auth = AuthContext.Authenticated("system", "system", true, false, "UnitTest");
            EnumerationResult<Incident> result = await incidents.EnumerateAsync(auth, new IncidentQuery { PageNumber = 1, PageSize = 200 }).ConfigureAwait(false);
            return result.Objects.Where(i => !String.IsNullOrEmpty(i.FailureKind)).ToList();
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.MissionRecoveryCoordinator",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (System.Threading.CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
