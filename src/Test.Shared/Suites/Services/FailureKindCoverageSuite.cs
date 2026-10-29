namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Server;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Failure writes that used to set a mission's FailureReason with no FailureKind now record one: a deferred stop the
    /// owning host never confirmed, a deferred recall, and a landing that was not attempted are Infra. A kindless
    /// LandingFailed mission classifies as LandingConflict and is auto-rescued, so a missing kind is not harmless.
    /// Other sites are covered next to their existing tests (StageCheckResumeSafety, CaptainStopRoute, PiCrashRecovery).
    /// </summary>
    public sealed class FailureKindCoverageSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.FailureKindCoverage";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("unconfirmed_stop_records_infra", "A stop the owning host never confirmed records the deferral reason with an Infra kind", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                AdmiralService admiral = CreateAdmiral(testDb.Driver);
                Captain captain = await testDb.Driver.Captains.CreateAsync(new Captain("unconfirmed")).ConfigureAwait(false);
                Mission mission = await testDb.Driver.Missions.CreateAsync(new Mission("held") { CaptainId = captain.Id, Status = MissionStatusEnum.InProgress }).ConfigureAwait(false);

                await InvokeUnconfirmedStopAsync(admiral, captain, mission).ConfigureAwait(false);

                Mission stored = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.InProgress, stored.Status, "ownership is retained, not failed");
                AssertContains("has not confirmed its exit", stored.FailureReason ?? "");
                AssertEqual(MissionFailureKindEnum.Infra, stored.FailureKind);
            }));

            cases.Add(CaseAsync("unconfirmed_stop_without_mission_writes_no_mission", "A stop that is not confirmed for a captain with no mission records only the captain event", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                AdmiralService admiral = CreateAdmiral(testDb.Driver);
                Captain captain = await testDb.Driver.Captains.CreateAsync(new Captain("unconfirmed-idle")).ConfigureAwait(false);

                await InvokeUnconfirmedStopAsync(admiral, captain, null).ConfigureAwait(false);

                List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateRecentAsync(20).ConfigureAwait(false);
                AssertTrue(events.Exists(e => e.EventType == "captain.stop_unconfirmed" && e.EntityId == captain.Id), "the captain event is recorded");
            }));

            cases.Add(CaseAsync("landing_not_attempted_records_infra", "A landing that is not attempted because the source branch cannot be confirmed keeps the work WorkProduced with an Infra kind", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                StubGitService git = new StubGitService();
                git.HeadCommitResult = "abc123";
                LoggingModule logging = CreateLogging();
                ArmadaSettings settings = CreateSettings();
                MissionLandingHandler handler = new MissionLandingHandler(logging, db, settings, git, new RecordingMergeQueueService(),
                    new MessageTemplateService(logging), null, new DockService(logging, db, settings, git), null);
                Vessel vessel = await db.Vessels.CreateAsync(new Vessel("Unconfirmed Branch", "https://github.com/test/unconfirmed")
                {
                    LocalPath = TestTemp.NewDirectory("unconfirmed_bare"),
                    WorkingDirectory = TestTemp.NewDirectory("unconfirmed_work"),
                    LandingMode = LandingModeEnum.LocalMerge
                }).ConfigureAwait(false);
                Dock dock = await db.Docks.CreateAsync(new Dock(vessel.Id) { Active = true, BranchName = "armada/unconfirmed", WorktreePath = TestTemp.NewDirectory("unconfirmed_dock") }).ConfigureAwait(false);
                Mission mission = await db.Missions.CreateAsync(new Mission("Unconfirmed")
                {
                    Status = MissionStatusEnum.WorkProduced, VesselId = vessel.Id, DockId = dock.Id, BranchName = dock.BranchName
                }).ConfigureAwait(false);

                await handler.HandleMissionCompleteAsync(mission, dock).ConfigureAwait(false);

                Mission stored = (await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.WorkProduced, stored.Status, stored.FailureReason);
                AssertContains("Landing was not attempted", stored.FailureReason ?? "");
                AssertEqual(MissionFailureKindEnum.Infra, stored.FailureKind);
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Failure Kind Coverage", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static AdmiralService CreateAdmiral(DatabaseDriver db)
        {
            LoggingModule logging = CreateLogging();
            ArmadaSettings settings = CreateSettings();
            StubGitService git = new StubGitService();
            DockService docks = new DockService(logging, db, settings, git);
            CaptainService captains = new CaptainService(logging, db, settings, git, docks);
            MissionService missions = new MissionService(logging, db, settings, docks, captains);
            return new AdmiralService(logging, db, settings, captains, missions, new VoyageService(logging, db), docks);
        }

        private static async Task InvokeUnconfirmedStopAsync(AdmiralService admiral, Captain captain, Mission? mission)
        {
            MethodInfo method = typeof(AdmiralService).GetMethod("RecordUnconfirmedStopAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await ((Task)method.Invoke(admiral, new object?[] { captain, mission, CancellationToken.None })!).ConfigureAwait(false);
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static ArmadaSettings CreateSettings()
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = TestTemp.NewDirectory("failure_kind_docks");
            settings.ReposDirectory = TestTemp.NewDirectory("failure_kind_repos");
            settings.LogDirectory = TestTemp.NewDirectory("failure_kind_logs");
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
