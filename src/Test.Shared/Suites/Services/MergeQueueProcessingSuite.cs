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
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end merge queue processing against real git repositories: an "origin" bare repository, the vessel's bare
    /// repository (cloned from origin, as Armada keeps it), and branches pushed into the vessel repository the way a
    /// captain's dock does. Covers a clean land (origin's main advances, linked mission Complete), a merge conflict, a
    /// failing test command, and that a failure marks the linked mission LandingFailed instead of leaving it WorkProduced.
    /// </summary>
    public sealed class MergeQueueProcessingSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.MergeQueueProcessing";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("clean_branch_lands_and_completes_mission", "Clean branch lands on origin main and completes the mission", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                    repos.PushBranch("feature/clean", "clean.txt", "clean change\n");
                    MergeQueueService queue = CreateQueue(testDb.Driver);
                    Mission mission = await CreateMissionAsync(testDb.Driver, repos).ConfigureAwait(false);
                    MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/clean", mission.Id, null).ConfigureAwait(false);

                    MergeEntry? processed = await queue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);

                    AssertEqual(MergeStatusEnum.Landed, processed!.Status, "entry status; output: " + processed.TestOutput);
                    AssertTrue(repos.OriginHasFile("main", "clean.txt"), "origin main contains the landed change");
                    Mission? updated = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Complete, updated!.Status);
                }
            }));

            cases.Add(CaseAsync("conflict_fails_entry_and_mission", "Merge conflict fails the entry and marks the mission LandingFailed", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                    repos.PushBranch("feature/conflict", "README.md", "branch version\n");
                    repos.AdvanceMain("README.md", "main version\n");
                    MergeQueueService queue = CreateQueue(testDb.Driver);
                    Mission mission = await CreateMissionAsync(testDb.Driver, repos).ConfigureAwait(false);
                    MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/conflict", mission.Id, null).ConfigureAwait(false);
                    string mainBefore = repos.OriginHead("main");

                    MergeEntry? processed = await queue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);

                    AssertEqual(MergeStatusEnum.Failed, processed!.Status);
                    AssertContains("Merge conflict", processed.TestOutput ?? "");
                    AssertEqual(mainBefore, repos.OriginHead("main"), "origin main unchanged");
                    Mission? updated = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.LandingFailed, updated!.Status, "mission must not stay WorkProduced");
                    AssertContains("merge conflict", updated.FailureReason ?? "");
                    AssertEqual(MissionFailureKindEnum.LandingConflict, updated.FailureKind, "failure kind is recorded where the failure happens");
                }
            }));

            cases.Add(CaseAsync("failing_tests_block_landing", "Failing test command fails the entry without landing", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                    repos.PushBranch("feature/tests-fail", "tested.txt", "change\n");
                    MergeQueueService queue = CreateQueue(testDb.Driver);
                    Mission mission = await CreateMissionAsync(testDb.Driver, repos).ConfigureAwait(false);
                    MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/tests-fail", mission.Id, "exit 3").ConfigureAwait(false);
                    string mainBefore = repos.OriginHead("main");

                    MergeEntry? processed = await queue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);

                    AssertEqual(MergeStatusEnum.Failed, processed!.Status);
                    AssertEqual(3, processed.TestExitCode);
                    AssertEqual(mainBefore, repos.OriginHead("main"), "nothing landed");
                    Mission? updated = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.LandingFailed, updated!.Status);
                }
            }));

            cases.Add(CaseAsync("queue_processes_in_order_and_continues_after_failure", "Queue lands in priority order and continues past a failed entry", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                    repos.PushBranch("feature/first", "first.txt", "1\n");
                    repos.PushBranch("feature/broken", "README.md", "conflicting\n");
                    repos.PushBranch("feature/second", "second.txt", "2\n");
                    repos.AdvanceMain("README.md", "main moved\n");
                    MergeQueueService queue = CreateQueue(testDb.Driver);
                    MergeEntry first = await EnqueueAsync(queue, testDb.Driver, repos, "feature/first", null, null, 1).ConfigureAwait(false);
                    MergeEntry broken = await EnqueueAsync(queue, testDb.Driver, repos, "feature/broken", null, null, 2).ConfigureAwait(false);
                    MergeEntry second = await EnqueueAsync(queue, testDb.Driver, repos, "feature/second", null, null, 3).ConfigureAwait(false);

                    await queue.ProcessQueueAsync().ConfigureAwait(false);

                    AssertEqual(MergeStatusEnum.Landed, (await queue.GetAsync(first.Id).ConfigureAwait(false))!.Status);
                    AssertEqual(MergeStatusEnum.Failed, (await queue.GetAsync(broken.Id).ConfigureAwait(false))!.Status);
                    AssertEqual(MergeStatusEnum.Landed, (await queue.GetAsync(second.Id).ConfigureAwait(false))!.Status);
                    AssertTrue(repos.OriginHasFile("main", "first.txt") && repos.OriginHasFile("main", "second.txt"), "both good branches landed");
                }
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Merge Queue Processing", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static MergeQueueService CreateQueue(DatabaseDriver db)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = TestTemp.NewDirectory("mq_docks");
            settings.ReposDirectory = TestTemp.NewDirectory("mq_repos");
            settings.MergeQueueTestCommand = null;
            return new MergeQueueService(logging, db, settings, new GitService(logging));
        }

        private static async Task<Mission> CreateMissionAsync(DatabaseDriver db, MergeQueueRepoSet repos)
        {
            Vessel vessel = await EnsureVesselAsync(db, repos).ConfigureAwait(false);
            Mission mission = new Mission("Queued mission");
            mission.VesselId = vessel.Id;
            mission.Status = MissionStatusEnum.WorkProduced;
            return await db.Missions.CreateAsync(mission).ConfigureAwait(false);
        }

        private static async Task<MergeEntry> EnqueueAsync(MergeQueueService queue, DatabaseDriver db, MergeQueueRepoSet repos, string branch, string? missionId, string? testCommand, int priority = 0)
        {
            Vessel vessel = await EnsureVesselAsync(db, repos).ConfigureAwait(false);
            MergeEntry entry = new MergeEntry(branch, "main");
            entry.VesselId = vessel.Id;
            entry.MissionId = missionId;
            entry.TestCommand = testCommand;
            entry.Priority = priority;
            return await queue.EnqueueAsync(entry).ConfigureAwait(false);
        }

        private static async Task<Vessel> EnsureVesselAsync(DatabaseDriver db, MergeQueueRepoSet repos)
        {
            if (repos.VesselId != null)
            {
                Vessel? existing = await db.Vessels.ReadAsync(repos.VesselId).ConfigureAwait(false);
                if (existing != null) return existing;
            }

            Vessel vessel = new Vessel("mq-vessel-" + Guid.NewGuid().ToString("N").Substring(0, 12), repos.Origin);
            vessel.LocalPath = repos.VesselRepo;
            vessel.DefaultBranch = "main";
            vessel = await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);
            repos.VesselId = vessel.Id;
            return vessel;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag, TestTags.Process });
        }

        #endregion
    }
}
