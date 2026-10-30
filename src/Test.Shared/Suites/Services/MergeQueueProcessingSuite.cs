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
                    AssertContains("README.md", updated.FailureReason ?? "", "the conflicted file is named, read before the merge was aborted");
                    AssertContains("README.md", processed.TestOutput ?? "");
                    MergeQueueFailure failure = await ReadFailureAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertEqual(MergeQueueFailureStepEnum.Merge, failure.Step);
                    AssertEqual(MissionFailureKindEnum.LandingConflict, failure.Kind);
                    AssertEqual("README.md", String.Join(",", failure.ConflictedFiles));
                    AssertEqual(entry.Id, failure.MergeEntryId);
                    AssertEqual(mainBefore, failure.TargetCommit, "the target commit the entry integrated against");
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
                    AssertEqual(MissionFailureKindEnum.TestFail, updated.FailureKind, "a failing check is a test failure, not a landing conflict");
                    MergeQueueFailure failure = await ReadFailureAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertEqual(MergeQueueFailureStepEnum.Verification, failure.Step);
                    AssertEqual(MissionFailureKindEnum.TestFail, failure.Kind);
                    AssertEqual(0, failure.ConflictedFiles.Count, "no conflicted files for a test failure");
                }
            }));

            cases.Add(CaseAsync("push_failure_is_infra", "A refused push fails the entry as Infra at the publication step, not as a landing conflict", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/push-refused", "pushed.txt", "change\n");
                string hook = Path.Combine(repos.Origin, "hooks", "pre-receive");
                Directory.CreateDirectory(Path.GetDirectoryName(hook)!);
                File.WriteAllText(hook, "#!/bin/sh\necho refused by test hook >&2\nexit 1\n");
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                MergeQueueService queue = CreateQueue(testDb.Driver);
                Mission mission = await CreateMissionAsync(testDb.Driver, repos).ConfigureAwait(false);
                MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/push-refused", mission.Id, null).ConfigureAwait(false);
                string mainBefore = repos.OriginHead("main");

                MergeEntry? processed = await queue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);

                AssertEqual(MergeStatusEnum.Failed, processed!.Status, processed.TestOutput);
                AssertEqual(mainBefore, repos.OriginHead("main"), "nothing landed");
                Mission updated = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.LandingFailed, updated.Status);
                AssertEqual(MissionFailureKindEnum.Infra, updated.FailureKind, updated.FailureReason);
                MergeQueueFailure failure = await ReadFailureAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                AssertEqual(MergeQueueFailureStepEnum.Publication, failure.Step);
                AssertEqual(MissionFailureKindEnum.Infra, failure.Kind);
            }));

            cases.Add(CaseAsync("unresolvable_repository_is_infra", "An entry whose repository cannot be resolved fails as Infra at the resolution step", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                MergeQueueService queue = CreateQueue(testDb.Driver);
                Vessel vessel = new Vessel("mq-missing-" + Guid.NewGuid().ToString("N").Substring(0, 12), "https://github.com/test/missing");
                vessel.LocalPath = Path.Combine(TestTemp.NewDirectory("mq_missing"), "absent.git");
                vessel.DefaultBranch = "main";
                vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                Mission mission = new Mission("Unresolvable") { VesselId = vessel.Id, Status = MissionStatusEnum.WorkProduced };
                mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);
                MergeEntry entry = new MergeEntry("feature/nowhere", "main") { VesselId = vessel.Id, MissionId = mission.Id };
                entry = await queue.EnqueueAsync(entry).ConfigureAwait(false);

                MergeEntry? processed = await queue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);

                AssertEqual(MergeStatusEnum.Failed, processed!.Status);
                Mission updated = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.LandingFailed, updated.Status);
                AssertEqual(MissionFailureKindEnum.Infra, updated.FailureKind, updated.FailureReason);
                MergeQueueFailure failure = await ReadFailureAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                AssertEqual(MergeQueueFailureStepEnum.Resolution, failure.Step);
            }));

            cases.Add(CaseAsync("mutating_check_is_infra", "A check that rewrites the integration commit fails the mission as Infra, not as a test failure", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/mutating", "mutating.txt", "mutating\n");
                MergeQueueService queue = CreateQueue(testDb.Driver);
                Mission mission = await CreateMissionAsync(testDb.Driver, repos).ConfigureAwait(false);
                MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/mutating", mission.Id, "git reset --hard HEAD^").ConfigureAwait(false);

                await queue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);

                Mission updated = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.LandingFailed, updated.Status);
                AssertEqual(MissionFailureKindEnum.Infra, updated.FailureKind, updated.FailureReason);
                AssertEqual(MergeQueueFailureStepEnum.Verification, (await ReadFailureAsync(testDb.Driver, mission.Id).ConfigureAwait(false)).Step);
            }));

            cases.Add(CaseAsync("failure_classifier_maps_steps_to_kinds", "The merge queue failure classifier maps each failing step to the kind that describes it", TestTags.Positive, async () =>
            {
                AssertEqual(MissionFailureKindEnum.LandingConflict, MergeQueueFailureClassifier.ClassifyMerge(2));
                AssertEqual(MissionFailureKindEnum.Infra, MergeQueueFailureClassifier.ClassifyMerge(0), "a merge that failed without conflicts is not a conflict");
                AssertEqual(MissionFailureKindEnum.TestFail, MergeQueueFailureClassifier.Classify(MergeQueueFailureStepEnum.Verification,
                    new LandingVerificationException("failed", "Failed", "exit 3", 3, "abc", false)));
                AssertEqual(MissionFailureKindEnum.Timeout, MergeQueueFailureClassifier.Classify(MergeQueueFailureStepEnum.Verification,
                    new LandingVerificationException("timed out", "TimedOut", "sleep 999", null, "abc", false)));
                AssertEqual(MissionFailureKindEnum.Timeout, MergeQueueFailureClassifier.Classify(MergeQueueFailureStepEnum.Verification, new TimeoutException("budget")));
                AssertEqual(MissionFailureKindEnum.Infra, MergeQueueFailureClassifier.Classify(MergeQueueFailureStepEnum.Verification,
                    new LandingVerificationException("mutated", "Failed", "git reset", 0, "abc", true)));
                AssertEqual(MissionFailureKindEnum.Infra, MergeQueueFailureClassifier.Classify(MergeQueueFailureStepEnum.Verification, new InvalidOperationException("could not start /bin/sh")));
                AssertEqual(MissionFailureKindEnum.Infra, MergeQueueFailureClassifier.Classify(MergeQueueFailureStepEnum.Publication, new InvalidOperationException("push rejected")));
                AssertEqual(MissionFailureKindEnum.Infra, MergeQueueFailureClassifier.Classify(MergeQueueFailureStepEnum.Preparation, new TimeoutException("fetch timed out")),
                    "a timeout outside verification is an environment failure");
                AssertEqual(MissionFailureKindEnum.LandingConflict, MergeQueueFailureClassifier.Classify(MergeQueueFailureStepEnum.Preparation,
                    new GitMergeConflictException("conflict", new[] { "a.txt" })));
                await Task.CompletedTask.ConfigureAwait(false);
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

            cases.Add(CaseAsync("passing_integration_receipt", "Merge queue persists passing checks against the exact SHA pushed to origin", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/receipt", "receipt.txt", "receipt\n");
                MergeQueueService queue = CreateQueue(testDb.Driver);
                Mission mission = await CreateMissionAsync(testDb.Driver, repos);
                MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/receipt", mission.Id, "echo verified");
                await queue.ProcessQueueAsync();
                MergeEntry stored = (await queue.GetAsync(entry.Id))!;
                AssertEqual(MergeStatusEnum.Landed, stored.Status);
                AssertEqual(0, stored.TestExitCode!.Value);
                AssertContains("verified", stored.TestOutput!);
                AssertContains(repos.OriginHead("main"), stored.TestOutput!);
                AssertEqual(repos.OriginHead("main"), (await testDb.Driver.Missions.ReadAsync(mission.Id))!.CommitHash);
            }));
            cases.Add(CaseAsync("merged_result_must_pass", "Merge queue refuses a branch whose checks fail only after integrating current main", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/integration", "feature.txt", "feature\n");
                repos.AdvanceMain("target-only.txt", "new target\n");
                string before = repos.OriginHead("main");
                MergeQueueService queue = CreateQueue(testDb.Driver);
                string command = OperatingSystem.IsWindows() ? "if exist target-only.txt (exit /b 7) else (exit /b 0)" : "test ! -f target-only.txt";
                MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/integration", null, command);
                await queue.ProcessQueueAsync();
                AssertEqual(MergeStatusEnum.Failed, (await queue.GetAsync(entry.Id))!.Status);
                AssertEqual(before, repos.OriginHead("main"));
                AssertFalse(repos.OriginHasFile("main", "feature.txt"), "Unverified integration is never published.");
            }));
            cases.Add(CaseAsync("test_mutation_refused", "A passing command that changes integration HEAD cannot land a different commit", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/mutates", "mutates.txt", "mutates\n");
                string before = repos.OriginHead("main");
                MergeQueueService queue = CreateQueue(testDb.Driver);
                MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/mutates", null, "git reset --hard HEAD^");
                await queue.ProcessQueueAsync();
                AssertEqual(MergeStatusEnum.Failed, (await queue.GetAsync(entry.Id))!.Status);
                AssertEqual(before, repos.OriginHead("main"));
            }));

            cases.Add(CaseAsync("cancel_during_check", "Cancelling a merge entry during its check cannot be overwritten or publish the commit", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/cancel", "cancel.txt", "cancel\n");
                string before = repos.OriginHead("main");
                MergeQueueService queue = CreateQueue(testDb.Driver);
                MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/cancel", null, "passing check");
                queue.LocalVerificationCommands = new FakeHostCommandExecutor { Handler = _ =>
                {
                    queue.CancelAsync(entry.Id).GetAwaiter().GetResult();
                    return new HostCommandResult { ExitCode = 0 };
                } };
                await queue.ProcessQueueAsync();
                AssertEqual(MergeStatusEnum.Cancelled, (await queue.GetAsync(entry.Id))!.Status);
                AssertEqual(before, repos.OriginHead("main"));
            }));
            cases.Add(CaseAsync("stale_queue_update", "A stale processing result cannot replace a newer merge-entry version", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                MergeEntry entry = await testDb.Driver.MergeEntries.CreateAsync(new MergeEntry("feature/stale"));
                DateTime version = entry.LastUpdateUtc;
                MergeEntry newer = (await testDb.Driver.MergeEntries.ReadAsync(entry.Id))!;
                newer.Status = MergeStatusEnum.Cancelled;
                await testDb.Driver.MergeEntries.UpdateAsync(newer);
                entry.Status = MergeStatusEnum.Landed;
                AssertFalse(await testDb.Driver.MergeEntries.TryUpdateIfUnchangedAsync(entry, version));
                AssertEqual(MergeStatusEnum.Cancelled, (await testDb.Driver.MergeEntries.ReadAsync(entry.Id))!.Status);
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Merge Queue Processing", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<MergeQueueFailure> ReadFailureAsync(DatabaseDriver db, string missionId)
        {
            List<ArmadaEvent> events = await db.Events.EnumerateRecentAsync(200).ConfigureAwait(false);
            ArmadaEvent? failed = events.Find(e => e.EventType == "mission.landing_failed" && e.MissionId == missionId);
            AssertNotNull(failed, "the merge queue records mission.landing_failed");
            AssertNotNull(failed!.Payload, "the landing failure event carries the structured failure");
            return System.Text.Json.JsonSerializer.Deserialize<MergeQueueFailure>(failed.Payload!)!;
        }

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
