namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Authorization;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
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
                null, () => MergeQueueCrashAsync(CrashPointNames.MergeQueuePushed)));

            cases.Add(Case("mergequeue_crash_after_entry_landed_completes_mission", "Crash after the entry is Landed: after restart the mission is Complete",
                null, () => MergeQueueCrashAsync(CrashPointNames.MergeQueueEntryLanded)));

            cases.Add(Case("recovery_crash_after_incident_created", "Crash after a recovery incident is created: the next cycle leaves one incident with its failure kind and one rescue",
                null, () => RecoveryCrashAsync(CrashPointNames.RecoveryIncidentCreated)));

            cases.Add(Case("recovery_crash_after_rescue_created", "Crash after a rescue is created: the next cycle adopts it instead of dispatching a second rescue",
                null, () => RecoveryCrashAsync(CrashPointNames.RecoveryRescueCreated)));

            foreach (string point in new[] { CrashPointNames.VoyageCreated, CrashPointNames.VoyageMissionCreated, CrashPointNames.VoyageOpened, CrashPointNames.VoyageMissionAssigned })
            {
                string crashPoint = point;
                cases.Add(Case("voyage_crash_at_" + crashPoint.Replace('.', '_').Replace('-', '_'), "Crash at " + crashPoint + " while creating a voyage: after restart it has all of its missions or none, and a voyage that never opened is failed with nothing started",
                    null, () => VoyageCrashAsync(crashPoint)));
            }

            cases.Add(Case("handoff_crash_between_dependents_recovers", "Crash after a stage handoff prepared one of two dependents: restart recovery prepares the other on the same branch",
                null, HandoffCrashAsync));

            foreach (string point in new[] { CrashPointNames.FanOutPlanned, CrashPointNames.FanOutWritten })
            {
                string crashPoint = point;
                cases.Add(Case("fanout_crash_at_" + crashPoint.Replace('.', '_').Replace('-', '_'), "Crash at " + crashPoint + " during a three-slice Architect fan-out: all of it was written or none, and after restart each slice exists once with its Judge stage",
                    null, () => FanOutCrashAsync(crashPoint)));
            }

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

            // Restart: new services over the same database run the queue and its sweeps. The crashed instance's lease
            // (5 seconds in these tests) must expire before another instance re-processes an entry left Testing.
            await Task.Delay(TimeSpan.FromSeconds(6)).ConfigureAwait(false);
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

        private static async Task VoyageCrashAsync(string crashPoint)
        {
            using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            Vessel vessel = await testDb.Driver.Vessels.CreateAsync(new Vessel("crash-voyage-vessel", "https://github.com/test/repo.git") { DefaultBranch = "main" }).ConfigureAwait(false);
            await testDb.Driver.Captains.CreateAsync(new Captain("crash-voyage-idle") { State = CaptainStateEnum.Idle }).ConfigureAwait(false);
            List<MissionDescription> work = new List<MissionDescription>
            {
                new MissionDescription("First", "Do the first part"),
                new MissionDescription("Second", "Do the second part"),
                new MissionDescription("Third", "Do the third part")
            };

            AdmiralService before = CreateAdmiral(testDb.Driver);
            CrashRunOutcomeEnum outcome = await CrashRun.RunAsync(crashPoint,
                () => before.DispatchVoyageAsync("Crash voyage", "Three-part change", vessel.Id, work)).ConfigureAwait(false);
            AssertEqual(CrashRunOutcomeEnum.Crashed, outcome, "the operation reached the crash point");

            // Restart: a new Admiral cleans up and runs its health loop twice.
            AdmiralService after = CreateAdmiral(testDb.Driver);
            await after.CleanupStaleCaptainsAsync().ConfigureAwait(false);
            await after.HealthCheckAsync().ConfigureAwait(false);
            await after.HealthCheckAsync().ConfigureAwait(false);

            List<Voyage> voyages = (await testDb.Driver.Voyages.EnumerateAsync().ConfigureAwait(false)).Where(v => v.Title == "Crash voyage").ToList();
            AssertEqual(1, voyages.Count, "one voyage row");
            List<Mission> missions = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyages[0].Id).ConfigureAwait(false);
            AssertTrue(missions.Count == 0 || missions.Count == work.Count, "all of the missions or none, never part: " + missions.Count + " of " + work.Count);
            bool opened = crashPoint == CrashPointNames.VoyageOpened || crashPoint == CrashPointNames.VoyageMissionAssigned;
            if (opened)
            {
                AssertEqual(work.Count, missions.Count, "an opened voyage is whole");
                AssertTrue(voyages[0].Status != VoyageStatusEnum.Building && voyages[0].Status != VoyageStatusEnum.Failed, "an opened voyage keeps running: " + voyages[0].Status);
            }
            else
            {
                AssertEqual(VoyageStatusEnum.Failed, voyages[0].Status, "a voyage that never opened is failed");
                AssertTrue(missions.All(m => m.Status == MissionStatusEnum.Cancelled && String.IsNullOrEmpty(m.CaptainId)),
                    "none of its missions started, even with an idle captain");
            }
        }

        private static async Task HandoffCrashAsync()
        {
            using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            DatabaseDriver db = testDb.Driver;
            StubGitService git = new StubGitService();
            git.HeadCommitResult = "6666666666666666666666666666666666666666";
            ArmadaSettings settings = new ArmadaSettings
            {
                DocksDirectory = TestTemp.NewDirectory("crash_handoff_docks"),
                ReposDirectory = TestTemp.NewDirectory("crash_handoff_repos"),
                LogDirectory = TestTemp.NewDirectory("crash_handoff_logs")
            };
            MissionService before = CreateMissionService(db, settings, git);
            before.OnGetMissionOutput = _ => "Implemented the change and committed it.";

            Vessel vessel = await db.Vessels.CreateAsync(new Vessel("crash-handoff-vessel", "https://github.com/test/repo.git") { DefaultBranch = "main" }).ConfigureAwait(false);
            Captain captain = await CaptainAssignmentFixture.CreateAsync(db, new Captain("crash-handoff") { State = CaptainStateEnum.Working }).ConfigureAwait(false);
            Voyage voyage = await db.Voyages.CreateAsync(new Voyage("crash handoff") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
            Mission worker = await db.Missions.CreateAsync(new Mission("[Worker] Task", "Do the task")
            {
                VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.InProgress,
                CaptainId = captain.Id, BranchName = "armada/crash/worker"
            }).ConfigureAwait(false);
            Dock dock = new Dock(vessel.Id) { CaptainId = captain.Id, BranchName = worker.BranchName, Active = true };
            dock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, worker.Id);
            Directory.CreateDirectory(dock.WorktreePath);
            dock = await db.Docks.CreateAsync(dock).ConfigureAwait(false);
            worker.DockId = dock.Id;
            await db.Missions.UpdateAsync(worker).ConfigureAwait(false);
            captain.Assignment = worker;
            await CaptainAssignmentFixture.UpdateAsync(db, captain).ConfigureAwait(false);
            Mission testEngineer = await db.Missions.CreateAsync(new Mission("[TestEngineer] Test", "Test it") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "TestEngineer", DependsOnMissionId = worker.Id }).ConfigureAwait(false);
            Mission judge = await db.Missions.CreateAsync(new Mission("[Judge] Review", "Review it") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Judge", DependsOnMissionId = worker.Id }).ConfigureAwait(false);

            CrashRunOutcomeEnum outcome = await CrashRun.RunAsync(CrashPointNames.HandoffDependentUpdated, () => before.HandleCompletionAsync(captain, worker.Id)).ConfigureAwait(false);
            AssertEqual(CrashRunOutcomeEnum.Crashed, outcome, "the operation reached the crash point");

            MissionService after = CreateMissionService(db, settings, git);
            await after.RecoverDanglingHandoffsAsync().ConfigureAwait(false);

            foreach (Mission dependent in new[] { testEngineer, judge })
            {
                Mission? current = await db.Missions.ReadAsync(dependent.Id).ConfigureAwait(false);
                AssertEqual(worker.BranchName, current!.BranchName, dependent.Persona + " is prepared on the worker's branch after restart");
            }
        }

        private static async Task FanOutCrashAsync(string crashPoint)
        {
            using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            DatabaseDriver db = testDb.Driver;
            StubGitService git = new StubGitService();
            git.HeadCommitResult = "7777777777777777777777777777777777777777";
            ArmadaSettings settings = new ArmadaSettings
            {
                DocksDirectory = TestTemp.NewDirectory("crash_fanout_docks"),
                ReposDirectory = TestTemp.NewDirectory("crash_fanout_repos"),
                LogDirectory = TestTemp.NewDirectory("crash_fanout_logs")
            };
            string plan = "Plan follows.\n```armada-plan\n{\"missions\":[{\"title\":\"Alpha\",\"description\":\"do alpha\"},{\"title\":\"Bravo\",\"description\":\"do bravo\"},{\"title\":\"Charlie\",\"description\":\"do charlie\"}]}\n```\n";
            MissionService before = CreateMissionService(db, settings, git);
            before.OnGetMissionOutput = _ => plan;

            Vessel vessel = await db.Vessels.CreateAsync(new Vessel("crash-fanout-vessel", "https://github.com/test/repo.git") { DefaultBranch = "main" }).ConfigureAwait(false);
            Captain captain = await CaptainAssignmentFixture.CreateAsync(db, new Captain("crash-fanout") { State = CaptainStateEnum.Working }).ConfigureAwait(false);
            Voyage voyage = await db.Voyages.CreateAsync(new Voyage("crash fanout") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
            Mission architect = await db.Missions.CreateAsync(new Mission("[Architect] Plan", "Plan the change")
            {
                VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Architect", Status = MissionStatusEnum.InProgress,
                CaptainId = captain.Id, BranchName = "armada/crash/architect"
            }).ConfigureAwait(false);
            Dock dock = new Dock(vessel.Id) { CaptainId = captain.Id, BranchName = architect.BranchName, Active = true };
            dock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, architect.Id);
            Directory.CreateDirectory(dock.WorktreePath);
            dock = await db.Docks.CreateAsync(dock).ConfigureAwait(false);
            architect.DockId = dock.Id;
            await db.Missions.UpdateAsync(architect).ConfigureAwait(false);
            captain.Assignment = architect;
            await CaptainAssignmentFixture.UpdateAsync(db, captain).ConfigureAwait(false);
            Mission worker = await db.Missions.CreateAsync(new Mission("[Worker] Build", "Build it") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", DependsOnMissionId = architect.Id }).ConfigureAwait(false);
            await db.Missions.CreateAsync(new Mission("[Judge] Review", "Review it") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Judge", DependsOnMissionId = worker.Id }).ConfigureAwait(false);

            CrashRunOutcomeEnum outcome = await CrashRun.RunAsync(crashPoint, () => before.HandleCompletionAsync(captain, architect.Id)).ConfigureAwait(false);
            AssertEqual(CrashRunOutcomeEnum.Crashed, outcome, "the operation reached the crash point");

            // RS-4.3: the fan-out is one transaction, so the crash leaves all of it or none of it.
            List<Mission> atCrash = await db.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
            Mission? architectAtCrash = atCrash.Single(m => m.Id == architect.Id);
            if (crashPoint == CrashPointNames.FanOutPlanned)
            {
                AssertEqual(3, atCrash.Count, "nothing of the fan-out was written");
                AssertEqual("[Worker] Build", atCrash.Single(m => m.Id == worker.Id).Title, "the waiting Worker is untouched");
                AssertFalse(architectAtCrash.FanOutCompletedUtc.HasValue, "the Architect is not marked");
            }
            else
            {
                AssertEqual(7, atCrash.Count, "every slice and stage was written");
                AssertTrue(architectAtCrash.FanOutCompletedUtc.HasValue, "the Architect is marked in the same write");
            }

            MissionService after = CreateMissionService(db, settings, git);
            after.OnGetMissionOutput = _ => plan;
            await after.RecoverDanglingHandoffsAsync().ConfigureAwait(false);
            await after.RecoverDanglingHandoffsAsync().ConfigureAwait(false);

            List<Mission> all = await db.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
            foreach (string title in new[] { "Alpha", "Bravo", "Charlie" })
            {
                List<Mission> workers = all.Where(m => m.Persona == "Worker" && m.Title.StartsWith(title, StringComparison.Ordinal)).ToList();
                AssertEqual(1, workers.Count, "exactly one Worker for slice " + title);
                AssertTrue(all.Any(m => m.Persona == "Judge" && m.DependsOnMissionId == workers[0].Id), "slice " + title + " has its Judge stage");
            }
        }

        private static MissionService CreateMissionService(DatabaseDriver db, ArmadaSettings settings, StubGitService git)
        {
            LoggingModule logging = CreateLogging();
            IDockService docks = new DockService(logging, db, settings, git);
            CaptainService captains = new CaptainService(logging, db, settings, git, docks);
            return new MissionService(logging, db, settings, docks, captains, git: git);
        }

        private static AdmiralService CreateAdmiral(DatabaseDriver db)
        {
            LoggingModule logging = CreateLogging();
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = TestTemp.NewDirectory("crash_admiral_docks");
            settings.ReposDirectory = TestTemp.NewDirectory("crash_admiral_repos");
            StubGitService git = new StubGitService();
            IDockService docks = new DockService(logging, db, settings, git);
            ICaptainService captains = new CaptainService(logging, db, settings, git, docks);
            IMissionService missions = new MissionService(logging, db, settings, docks, captains);
            IVoyageService voyages = new VoyageService(logging, db);
            return new AdmiralService(logging, db, settings, captains, missions, voyages, docks);
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
            settings.MergeQueueLeaseSeconds = 5;
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
