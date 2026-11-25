namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Authorization;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Test.Shared.Infrastructure.CrashPoints;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Crash consistency (RELIABILITY_SIMPLICITY_PLAN.md RS-1.3, gate measure G9): an operation is interrupted at a named
    /// crash point, fresh services are built over the same database as an Admiral restart would, and recovery must
    /// reach the same end state as an uninterrupted run. Cases whose recovery is not built yet are skipped with the plan
    /// item that builds it; that item turns the case on.
    /// </summary>
    public sealed class CrashConsistencySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.CrashConsistency";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the crash consistency suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("injector_scope_is_per_flow", "An armed point freezes only its own flow; other flows and code after the install only record", null, async () =>
            {
                CrashPointInjector observer = new CrashPointInjector(null);
                Task other = Task.Run(() => CrashPoints.Hit(CrashPointNames.MergeQueuePushed));
                using (CrashPoints.Install(observer)) CrashPoints.Hit(CrashPointNames.MergeQueuePushed);
                await other.ConfigureAwait(false);
                CrashPoints.Hit(CrashPointNames.MergeQueuePushed);
                AssertEqual(1, observer.Hits.Count, "only the installing flow is observed");

                bool after = false;
                CrashRunOutcomeEnum outcome = await CrashRun.RunAsync(CrashPointNames.MergeQueuePushed, async () =>
                {
                    await Task.Yield();
                    try { CrashPoints.Hit(CrashPointNames.MergeQueuePushed); }
                    finally { after = true; }
                }).ConfigureAwait(false);
                await Task.Delay(100).ConfigureAwait(false);
                AssertEqual(CrashRunOutcomeEnum.Crashed, outcome);
                AssertFalse(after, "nothing after the crash point runs, not even a finally block");
            }));

            cases.Add(Case("mergequeue_crash_after_push_lands_on_restart", "Crash after the merge queue push: after restart the entry is Landed and the mission Complete",
                "Recovery of an entry left Testing needs the configurable lease of RS-2.5.", () => MergeQueueCrashAsync(CrashPointNames.MergeQueuePushed)));

            cases.Add(Case("mergequeue_crash_after_entry_landed_completes_mission", "Crash after the entry is Landed: after restart the mission is Complete",
                "The sweep that completes a mission whose entry is Landed is RS-2.3.", () => MergeQueueCrashAsync(CrashPointNames.MergeQueueEntryLanded)));

            cases.Add(Case("recovery_crash_after_incident_created", "Crash after a recovery incident is created: the next cycle leaves one incident with its failure kind and one rescue",
                "Creating the incident with its failure kind in one write and adopting existing rescues is RS-2.6.", () => RecoveryCrashAsync(CrashPointNames.RecoveryIncidentCreated)));

            cases.Add(Case("recovery_crash_after_rescue_created", "Crash after a rescue is created: the next cycle adopts it instead of dispatching a second rescue",
                "Adopting rescues by RescueOfMissionId in any status is RS-2.6.", () => RecoveryCrashAsync(CrashPointNames.RecoveryRescueCreated)));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Crash consistency", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task MergeQueueCrashAsync(string crashPoint)
        {
            using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
            repos.PushBranch("feature/crash", "crash.txt", "crash change\n");
            Vessel vessel = new Vessel("crash-vessel-" + Guid.NewGuid().ToString("N").Substring(0, 12), repos.Origin);
            vessel.LocalPath = repos.VesselRepo;
            vessel.DefaultBranch = "main";
            vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
            repos.VesselId = vessel.Id;
            Mission mission = new Mission("Crash mission") { VesselId = vessel.Id, Status = MissionStatusEnum.WorkProduced };
            mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

            MergeQueueService before = CreateQueue(testDb.Driver);
            MergeEntry entry = new MergeEntry("feature/crash", "main") { VesselId = vessel.Id, MissionId = mission.Id };
            entry = await before.EnqueueAsync(entry).ConfigureAwait(false);

            CrashRunOutcomeEnum outcome = await CrashRun.RunAsync(crashPoint, () => before.ProcessSingleAsync(entry.Id)).ConfigureAwait(false);
            AssertEqual(CrashRunOutcomeEnum.Crashed, outcome, "the operation reached the crash point");

            // Restart: new services over the same database run the queue and its sweeps.
            MergeQueueService after = CreateQueue(testDb.Driver);
            await after.ProcessQueueAsync().ConfigureAwait(false);

            MergeEntry? finalEntry = await testDb.Driver.MergeEntries.ReadAsync(entry.Id).ConfigureAwait(false);
            Mission? finalMission = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
            AssertTrue(repos.OriginHasFile("main", "crash.txt"), "origin main has the change");
            AssertEqual(MergeStatusEnum.Landed, finalEntry!.Status, "entry after restart");
            AssertEqual(MissionStatusEnum.Complete, finalMission!.Status, "mission after restart");
        }

        private static async Task RecoveryCrashAsync(string crashPoint)
        {
            using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            ArmadaSettings settings = new ArmadaSettings { MaxMissionRecoveryAttempts = 2 };
            Vessel vessel = await testDb.Driver.Vessels.CreateAsync(new Vessel("crash-recovery-vessel", "https://github.com/test/repo.git") { DefaultBranch = "main" }).ConfigureAwait(false);
            Mission failed = new Mission("Do the thing", "Implement the thing correctly.")
            {
                VesselId = vessel.Id,
                Status = MissionStatusEnum.Failed,
                FailureKind = MissionFailureKindEnum.Compile,
                FailureReason = "Definition-of-Done gate failed (Compile): build phase exited 1"
            };
            failed = await testDb.Driver.Missions.CreateAsync(failed).ConfigureAwait(false);

            MissionRecoveryCoordinator before = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, settings);
            CrashRunOutcomeEnum outcome = await CrashRun.RunAsync(crashPoint, () => before.MaintainAsync()).ConfigureAwait(false);
            AssertEqual(CrashRunOutcomeEnum.Crashed, outcome, "the operation reached the crash point");

            // Restart: a new coordinator runs its cycle twice, as the health loop would.
            MissionRecoveryCoordinator after = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, settings);
            await after.MaintainAsync().ConfigureAwait(false);
            await after.MaintainAsync().ConfigureAwait(false);

            AuthContext auth = AuthContext.Authenticated("system", "system", true, false, "UnitTest");
            IncidentService incidents = new IncidentService(testDb.Driver);
            List<Incident> forMission = (await incidents.EnumerateAsync(auth, new IncidentQuery { PageNumber = 1, PageSize = 200 }).ConfigureAwait(false))
                .Objects.Where(i => i.MissionId == failed.Id).ToList();
            List<Mission> rescues = (await testDb.Driver.Missions.EnumerateByVesselAsync(vessel.Id).ConfigureAwait(false))
                .Where(m => m.RescueOfMissionId == failed.Id).ToList();
            AssertEqual(1, forMission.Count, "one recovery incident for the failed mission");
            AssertEqual("Compile", forMission[0].FailureKind, "the incident records the failure kind");
            AssertEqual(1, rescues.Count, "one rescue mission, not a duplicate");
            AssertTrue(forMission[0].RescueMissionIds.Contains(rescues[0].Id), "the incident references the rescue");
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static MergeQueueService CreateQueue(DatabaseDriver db)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = TestTemp.NewDirectory("crash_docks");
            settings.ReposDirectory = TestTemp.NewDirectory("crash_repos");
            settings.MergeQueueTestCommand = null;
            return new MergeQueueService(logging, db, settings, new GitService(logging));
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string? pendingItem, Func<Task> body)
        {
            bool run = pendingItem == null || Environment.GetEnvironmentVariable("ARMADA_TEST_PENDING_CRASH_CASES") == "1";
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.Reliability, TestTags.Process },
                skip: !run,
                skipReason: run ? null : "Pending " + pendingItem + " Set ARMADA_TEST_PENDING_CRASH_CASES=1 to run it.");
        }

        #endregion
    }
}
