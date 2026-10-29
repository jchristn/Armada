namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Reflection;
    using System.Runtime.ExceptionServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
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

            cases.Add(CaseAsync("landed_write_failure_after_target_advances_stays_landed", "A failed Landed write after the target advances still records Landed", TestTags.Positive, () =>
                RunAdvancingTargetWriteFailureAsync(1, MergeStatusEnum.Landed, MissionStatusEnum.Complete)));

            cases.Add(CaseAsync("second_landed_write_failure_does_not_record_failed", "A second failed Landed write does not record Failed", TestTags.Negative, () =>
                RunAdvancingTargetWriteFailureAsync(2, MergeStatusEnum.Testing, MissionStatusEnum.WorkProduced)));

            cases.Add(CaseAsync("push_rejection_stays_failed", "A rejected push stays Failed and leaves the target unchanged", TestTags.Negative, RunPushRejectionAsync));

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

        private static MergeQueueService CreateQueue(DatabaseDriver db, int failLandedWrites = 0, Action? beforeLandedWriteFailure = null)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = TestTemp.NewDirectory("mq_docks");
            settings.ReposDirectory = TestTemp.NewDirectory("mq_repos");
            settings.MergeQueueTestCommand = null;
            DatabaseDriver driver = db;
            if (failLandedWrites > 0)
            {
                IMergeEntryMethods hooked = LandedUpdateFailureProxy.Create(db.MergeEntries, failLandedWrites, beforeLandedWriteFailure);
                driver = new MergeEntryHookDriver(db, hooked);
            }

            return new MergeQueueService(logging, driver, settings, new GitService(logging));
        }

        private static async Task RunAdvancingTargetWriteFailureAsync(int failures, MergeStatusEnum expectedStatus, MissionStatusEnum expectedMission)
        {
            using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
            {
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/landed-write", "landed.txt", "landed change\n");
                string? integrationCommit = null;
                MergeQueueService queue = CreateQueue(testDb.Driver, failures, () =>
                {
                    if (integrationCommit != null) return;
                    integrationCommit = repos.OriginHead("main");
                    repos.AdvanceMain("later.txt", "later change\n");
                });
                Mission mission = await CreateMissionAsync(testDb.Driver, repos).ConfigureAwait(false);
                MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/landed-write", mission.Id, null).ConfigureAwait(false);

                MergeEntry? processed = await queue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);

                AssertNotNull(integrationCommit, "the push updated the target before the Landed write");
                AssertEqual(expectedStatus, processed!.Status, "entry status; output: " + processed.TestOutput);
                Mission? updated = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                AssertEqual(expectedMission, updated!.Status, "mission status");
                if (expectedMission == MissionStatusEnum.WorkProduced)
                {
                    AssertNull(updated.FailureReason, "mission failure reason");
                    AssertNull(updated.FailureKind, "mission failure kind");
                }

                AssertTargetContainsIntegration(repos, integrationCommit, "landed.txt", "later.txt");
            }
        }

        private static async Task RunPushRejectionAsync()
        {
            using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
            {
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/push-rejected", "rejected.txt", "rejected change\n");
                string hooks = repos.InstallPushRejectionHook();
                MergeQueueService queue = CreateQueue(testDb.Driver);
                Mission mission = await CreateMissionAsync(testDb.Driver, repos).ConfigureAwait(false);
                MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/push-rejected", mission.Id, null).ConfigureAwait(false);
                string mainBefore = repos.OriginHead("main");
                string? previousCount;
                string? previousKey;
                string? previousValue;
                string index;
                SetHooksPath(hooks, out previousCount, out previousKey, out previousValue, out index);
                try
                {
                    MergeEntry? processed = await queue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);

                    AssertEqual(MergeStatusEnum.Failed, processed!.Status, "entry status; output: " + processed.TestOutput);
                    AssertContains("Landing failed", processed.TestOutput ?? "");
                    AssertEqual(mainBefore, repos.OriginHead("main"), "origin main unchanged");
                    AssertFalse(repos.OriginHasFile("main", "rejected.txt"), "rejected change is not on the target");
                    Mission? updated = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.LandingFailed, updated!.Status, "mission status");
                }
                finally
                {
                    RestoreHooksPath(previousCount, previousKey, previousValue, index);
                }
            }
        }

        private static void AssertTargetContainsIntegration(MergeQueueRepoSet repos, string integrationCommit, string featureFile, string laterFile)
        {
            string tip = repos.OriginHead("main");
            AssertNotEqual(integrationCommit, tip, "a later commit advances the target");
            AssertTrue(repos.HasCommit(integrationCommit), "integration commit remains on origin");
            AssertTrue(repos.HasCommit(tip), "later commit remains on origin");
            AssertTrue(repos.IsAncestor(integrationCommit, tip), "integration commit is an ancestor of the target tip");
            AssertTrue(repos.OriginHasFile("main", featureFile), featureFile + " is on the target");
            AssertTrue(repos.OriginHasFile("main", laterFile), laterFile + " is on the target");
        }

        private static void SetHooksPath(string hooksPath, out string? previousCount, out string? previousKey, out string? previousValue, out string index)
        {
            previousCount = Environment.GetEnvironmentVariable("GIT_CONFIG_COUNT");
            int count = 0;
            if (!String.IsNullOrWhiteSpace(previousCount))
            {
                Int32.TryParse(previousCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out count);
            }

            if (count < 0) count = 0;
            index = count.ToString(CultureInfo.InvariantCulture);
            previousKey = Environment.GetEnvironmentVariable("GIT_CONFIG_KEY_" + index);
            previousValue = Environment.GetEnvironmentVariable("GIT_CONFIG_VALUE_" + index);
            Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_" + index, "core.hooksPath");
            Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_" + index, hooksPath);
            Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", (count + 1).ToString(CultureInfo.InvariantCulture));
        }

        private static void RestoreHooksPath(string? previousCount, string? previousKey, string? previousValue, string index)
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", previousCount);
            Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_" + index, previousKey);
            Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_" + index, previousValue);
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

        /// <summary>
        /// Fails the first <paramref name="failures"/> merge-entry updates whose status is Landed. Other writes pass
        /// through. The hook runs once, before the first failed write.
        /// </summary>
        public class LandedUpdateFailureProxy : DispatchProxy
        {
            private IMergeEntryMethods _Inner = null!;
            private int _RemainingFailures;
            private Action? _BeforeFailure;

            /// <summary>
            /// Wrap merge-entry methods so Landed updates fail the requested number of times.
            /// </summary>
            /// <param name="inner">Real merge-entry methods.</param>
            /// <param name="failures">How many Landed updates throw.</param>
            /// <param name="beforeFailure">Runs once, before the first thrown update.</param>
            /// <returns>The wrapping methods.</returns>
            public static IMergeEntryMethods Create(IMergeEntryMethods inner, int failures, Action? beforeFailure)
            {
                if (inner == null) throw new ArgumentNullException(nameof(inner));
                if (failures < 1) throw new ArgumentOutOfRangeException(nameof(failures));

                IMergeEntryMethods proxy = DispatchProxy.Create<IMergeEntryMethods, LandedUpdateFailureProxy>();
                LandedUpdateFailureProxy state = (LandedUpdateFailureProxy)(object)proxy;
                state._Inner = inner;
                state._RemainingFailures = failures;
                state._BeforeFailure = beforeFailure;
                return proxy;
            }

            /// <summary>
            /// Forward a call, or throw for a Landed update while failures remain.
            /// </summary>
            /// <param name="targetMethod">Interface method.</param>
            /// <param name="args">Arguments.</param>
            /// <returns>The inner result.</returns>
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                if (targetMethod == null) throw new ArgumentNullException(nameof(targetMethod));
                if ((String.Equals(targetMethod.Name, "UpdateAsync", StringComparison.Ordinal)
                    || String.Equals(targetMethod.Name, "TryUpdateIfUnchangedAsync", StringComparison.Ordinal))
                    && args != null
                    && args.Length > 0
                    && args[0] is MergeEntry entry
                    && entry.Status == MergeStatusEnum.Landed
                    && _RemainingFailures > 0)
                {
                    _RemainingFailures--;
                    Action? hook = _BeforeFailure;
                    _BeforeFailure = null;
                    if (hook != null) hook();
                    throw new InvalidOperationException("Landed merge-entry update failed");
                }

                try
                {
                    return targetMethod.Invoke(_Inner, args);
                }
                catch (TargetInvocationException ex) when (ex.InnerException != null)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    throw;
                }
            }
        }

        /// <summary>
        /// Database driver that shares a real driver's accessors except merge entries.
        /// Disposing it leaves the inner driver open.
        /// </summary>
        private sealed class MergeEntryHookDriver : DatabaseDriver
        {
            private readonly DatabaseDriver _Inner;

            /// <summary>
            /// Instantiate over an initialized driver.
            /// </summary>
            /// <param name="inner">Real driver.</param>
            /// <param name="mergeEntries">Merge-entry methods the queue should use.</param>
            public MergeEntryHookDriver(DatabaseDriver inner, IMergeEntryMethods mergeEntries)
            {
                if (inner == null) throw new ArgumentNullException(nameof(inner));
                if (mergeEntries == null) throw new ArgumentNullException(nameof(mergeEntries));
                _Inner = inner;
                foreach (PropertyInfo property in typeof(DatabaseDriver).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    MethodInfo? setter = property.GetSetMethod(true);
                    if (setter == null || property.GetIndexParameters().Length > 0) continue;
                    setter.Invoke(this, new object?[] { property.GetValue(inner) });
                }

                MergeEntries = mergeEntries;
            }

            /// <inheritdoc />
            public override Task InitializeAsync(CancellationToken token = default)
            {
                return _Inner.InitializeAsync(token);
            }

            /// <inheritdoc />
            public override Task<int> GetSchemaVersionAsync(CancellationToken token = default)
            {
                return _Inner.GetSchemaVersionAsync(token);
            }

            /// <inheritdoc />
            public override int GetLatestSchemaVersion()
            {
                return _Inner.GetLatestSchemaVersion();
            }

            /// <inheritdoc />
            public override void Dispose()
            {
            }

            /// <inheritdoc />
            internal override IReadOnlyList<SchemaMigration> GetMigrationsForVerification()
            {
                return _Inner.GetMigrationsForVerification();
            }

            /// <inheritdoc />
            internal override Task ReplayMigrationsAsync(CancellationToken token = default)
            {
                return _Inner.ReplayMigrationsAsync(token);
            }
        }

        #endregion
    }
}
