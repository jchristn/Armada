namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Server;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Dock leases and dock integrity: DockService refuses to reclaim a dock that still holds unlanded work unless the
    /// caller forces it, and records every reclaim; a landing racing a reclaim keeps its dock; a missing worktree is
    /// recreated from its branch by the landing-integrity sweep; and a LocalMerge landing deferred on a dirty checkout is
    /// retried once the checkout is clean, until its deadline.
    /// </summary>
    public sealed class DockLeaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.DockLease";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            foreach (MissionStatusEnum leased in DockLeaseState.LeaseStatuses)
            {
                MissionStatusEnum status = leased;
                cases.Add(CaseAsync("reclaim_refused_while_" + status.ToString().ToLowerInvariant(), "A dock whose mission is " + status + " is kept and the refusal is recorded", TestTags.Negative, async () =>
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    StubGitService git = new StubGitService();
                    DockService docks = new DockService(CreateLogging(), testDb.Driver, CreateSettings(), git);
                    LeaseEntities entities = await CreateLeaseEntitiesAsync(testDb.Driver, status).ConfigureAwait(false);

                    bool reclaimed = await docks.ReclaimAsync(entities.Dock.Id, caller: "TestCaller").ConfigureAwait(false);

                    AssertFalse(reclaimed, "the lease refuses the reclaim");
                    AssertTrue(Directory.Exists(entities.Dock.WorktreePath), "the worktree survives");
                    AssertTrue((await testDb.Driver.Docks.ReadAsync(entities.Dock.Id).ConfigureAwait(false))!.Active, "the dock stays active");
                    DockReclaimRecord record = await ReadRecordAsync(testDb.Driver, "dock.reclaim_refused").ConfigureAwait(false);
                    AssertEqual("TestCaller", record.Caller);
                    AssertEqual(entities.Mission.Id, record.MissionId);
                    AssertEqual(status.ToString(), record.MissionStatus);
                    AssertTrue(record.Refused, "recorded as refused");
                }));
            }

            cases.Add(CaseAsync("forced_reclaim_records_caller_and_status", "A forced reclaim removes a leased dock and records the caller, the mission status and the reason", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DockService docks = new DockService(CreateLogging(), testDb.Driver, CreateSettings(), new StubGitService());
                LeaseEntities entities = await CreateLeaseEntitiesAsync(testDb.Driver, MissionStatusEnum.WorkProduced).ConfigureAwait(false);

                AssertTrue(await docks.ReclaimAsync(entities.Dock.Id, forceReason: "pipeline handoff").ConfigureAwait(false));

                AssertFalse(Directory.Exists(entities.Dock.WorktreePath), "the worktree is removed");
                AssertFalse((await testDb.Driver.Docks.ReadAsync(entities.Dock.Id).ConfigureAwait(false))!.Active);
                DockReclaimRecord record = await ReadRecordAsync(testDb.Driver, "dock.reclaimed").ConfigureAwait(false);
                AssertEqual("pipeline handoff", record.ForceReason);
                AssertEqual("WorkProduced", record.MissionStatus);
                AssertFalse(String.IsNullOrEmpty(record.Caller), "the caller is recorded (CallerMemberName)");
            }));

            cases.Add(CaseAsync("unleased_reclaim_records_event", "A dock whose mission landed is reclaimed and the reclaim names the caller", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DockService docks = new DockService(CreateLogging(), testDb.Driver, CreateSettings(), new StubGitService());
                LeaseEntities entities = await CreateLeaseEntitiesAsync(testDb.Driver, MissionStatusEnum.Complete).ConfigureAwait(false);

                AssertTrue(await docks.ReclaimAsync(entities.Dock.Id, caller: "AfterLanding").ConfigureAwait(false));

                AssertFalse(Directory.Exists(entities.Dock.WorktreePath));
                DockReclaimRecord record = await ReadRecordAsync(testDb.Driver, "dock.reclaimed").ConfigureAwait(false);
                AssertEqual("AfterLanding", record.Caller);
                AssertEqual("Complete", record.MissionStatus);
                AssertNull(record.LeaseHolder, "nothing held it");
            }));

            foreach (MergeStatusEnum entryStatus in new[] { MergeStatusEnum.Queued, MergeStatusEnum.Testing })
            {
                MergeStatusEnum queued = entryStatus;
                cases.Add(CaseAsync("merge_entry_" + queued.ToString().ToLowerInvariant() + "_holds_lease", "A " + queued + " merge queue entry for the dock's mission keeps the dock", TestTags.Negative, async () =>
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    DockService docks = new DockService(CreateLogging(), testDb.Driver, CreateSettings(), new StubGitService());
                    LeaseEntities entities = await CreateLeaseEntitiesAsync(testDb.Driver, MissionStatusEnum.Cancelled).ConfigureAwait(false);
                    MergeEntry entry = new MergeEntry(entities.Dock.BranchName!, "main") { MissionId = entities.Mission.Id, VesselId = entities.Mission.VesselId, Status = queued };
                    await testDb.Driver.MergeEntries.CreateAsync(entry).ConfigureAwait(false);

                    AssertFalse(await docks.ReclaimAsync(entities.Dock.Id).ConfigureAwait(false), "the queued entry holds the lease");
                    AssertTrue(Directory.Exists(entities.Dock.WorktreePath));
                    DockReclaimRecord record = await ReadRecordAsync(testDb.Driver, "dock.reclaim_refused").ConfigureAwait(false);
                    AssertContains("merge queue entry", record.LeaseHolder ?? "");
                }));
            }

            cases.Add(CaseAsync("landing_racing_reclaim_keeps_dock", "A reclaim that runs while landing is integrating the target is refused, and the landing completes from the dock", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                StubGitService git = new StubGitService();
                git.ResolveBranchTipResult = git.HeadCommitResult;
                LoggingModule logging = CreateLogging();
                ArmadaSettings settings = CreateSettings();
                DockService docks = new DockService(logging, testDb.Driver, settings, git);
                MissionLandingHandler handler = new MissionLandingHandler(logging, testDb.Driver, settings, git, new RecordingMergeQueueService(),
                    new MessageTemplateService(logging), null, docks, null);
                LeaseEntities entities = await CreateLeaseEntitiesAsync(testDb.Driver, MissionStatusEnum.WorkProduced, LandingModeEnum.MergeAndPush).ConfigureAwait(false);
                git.ExistingBranches.Add(entities.Dock.BranchName!);

                bool? reclaimedDuringLanding = null;
                bool dockPresentDuringLanding = false;
                git.OnFetchRemotes = async () =>
                {
                    if (reclaimedDuringLanding.HasValue) return;
                    // Another path (orphan sweep, captain recovery, max-runtime watchdog) reclaims concurrently.
                    reclaimedDuringLanding = await Task.Run(() => docks.ReclaimAsync(entities.Dock.Id, caller: "RacingReclaim")).ConfigureAwait(false);
                    dockPresentDuringLanding = Directory.Exists(entities.Dock.WorktreePath);
                };

                await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                AssertEqual(false, reclaimedDuringLanding, "the concurrent reclaim is refused");
                AssertTrue(dockPresentDuringLanding, "the worktree is there for the rest of the landing");
                Mission landed = (await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.Complete, landed.Status, landed.FailureReason);
                AssertEqual(entities.Dock.WorktreePath, git.PushCalls.Single(), "published from the dock");
                AssertTrue(await docks.ReclaimAsync(entities.Dock.Id, caller: "AfterLanding").ConfigureAwait(false), "after landing the dock is released");
            }));

            cases.Add(CaseAsync("restore_recreates_missing_worktree", "A vanished dock worktree is recreated from its branch at its recorded path and recorded as dock.repaired", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RealLandingFixture f = await RealLandingFixture.CreateAsync(testDb.Driver, LandingModeEnum.MergeAndPush).ConfigureAwait(false);
                Directory.Delete(f.Dock.WorktreePath!, true);

                DockRestoreResult result = await f.Docks.RestoreWorktreeAsync(f.Dock.Id, "test").ConfigureAwait(false);

                AssertTrue(result.Restored, result.Reason);
                AssertTrue(Directory.Exists(f.Dock.WorktreePath), "the worktree is back");
                AssertEqual(f.WorkCommit, result.HeadCommit, "on the mission's commit");
                List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateRecentAsync(50).ConfigureAwait(false);
                AssertTrue(events.Any(e => e.EventType == "dock.repaired" && e.EntityId == f.Dock.Id), "dock.repaired recorded");
            }));

            cases.Add(CaseAsync("restore_never_invents_branch", "A vanished dock whose branch is also gone is not recreated from the base branch", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RealLandingFixture f = await RealLandingFixture.CreateAsync(testDb.Driver, LandingModeEnum.MergeAndPush).ConfigureAwait(false);
                Directory.Delete(f.Dock.WorktreePath!, true);
                await HarborDockGitFixture.GitAsync(f.Repos.VesselRepo, "worktree", "prune").ConfigureAwait(false);
                await HarborDockGitFixture.GitAsync(f.Repos.VesselRepo, "branch", "-D", f.Dock.BranchName!).ConfigureAwait(false);

                DockRestoreResult result = await f.Docks.RestoreWorktreeAsync(f.Dock.Id, "test").ConfigureAwait(false);

                AssertFalse(result.Restored);
                AssertContains(f.Dock.BranchName!, result.Reason ?? "");
                AssertFalse(Directory.Exists(f.Dock.WorktreePath), "nothing was created");
                AssertNull(await HarborDockGitFixture.ResolveAsync(f.Repos.VesselRepo, "refs/heads/" + f.Dock.BranchName), "no branch was invented");
            }));

            cases.Add(CaseAsync("integrity_sweep_repairs_unlanded_dock", "The landing-integrity sweep recreates the missing dock of a LandingFailed mission, and leaves a reclaimed dock alone", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RealLandingFixture f = await RealLandingFixture.CreateAsync(testDb.Driver, LandingModeEnum.MergeAndPush).ConfigureAwait(false);
                f.Mission.Status = MissionStatusEnum.LandingFailed;
                f.Mission.FailureKind = MissionFailureKindEnum.Infra;
                await testDb.Driver.Missions.UpdateAsync(f.Mission).ConfigureAwait(false);
                Directory.Delete(f.Dock.WorktreePath!, true);
                LandingIntegrityService sweep = new LandingIntegrityService(f.Logging, testDb.Driver, f.Settings, f.Docks, new RecordingLandingService(), f.Git);

                AssertEqual(1, await sweep.RepairMissingDocksAsync().ConfigureAwait(false), "one dock repaired");
                AssertTrue(Directory.Exists(f.Dock.WorktreePath));
                AssertEqual(0, await sweep.RepairMissingDocksAsync().ConfigureAwait(false), "a present dock is not repaired again");

                Dock reclaimed = (await testDb.Driver.Docks.ReadAsync(f.Dock.Id).ConfigureAwait(false))!;
                Directory.Delete(f.Dock.WorktreePath!, true);
                reclaimed.Active = false;
                await testDb.Driver.Docks.UpdateAsync(reclaimed).ConfigureAwait(false);
                LandingIntegrityService fresh = new LandingIntegrityService(f.Logging, testDb.Driver, f.Settings, f.Docks, new RecordingLandingService(), f.Git);
                AssertEqual(0, await fresh.RepairMissingDocksAsync().ConfigureAwait(false), "a reclaimed dock was released on purpose");
                AssertFalse(Directory.Exists(f.Dock.WorktreePath));
            }));

            cases.Add(CaseAsync("dirty_local_merge_deferred_then_retried", "LocalMerge into a dirty checkout fails as Infra, is recorded as deferred, and lands automatically once the checkout is clean", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RealLandingFixture f = await RealLandingFixture.CreateAsync(testDb.Driver, LandingModeEnum.LocalMerge).ConfigureAwait(false);
                string readme = Path.Combine(f.Repos.Work, "README.md");
                string original = File.ReadAllText(readme);
                File.WriteAllText(readme, original + "operator edit\n");

                await f.Handler.HandleMissionCompleteAsync(f.Mission, f.Dock).ConfigureAwait(false);

                Mission failed = (await testDb.Driver.Missions.ReadAsync(f.Mission.Id).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.LandingFailed, failed.Status);
                AssertEqual(MissionFailureKindEnum.Infra, failed.FailureKind, "a dirty checkout is not a landing conflict");
                LandingIntegrityService sweep = CreateSweep(f, testDb.Driver, out LandingService landing);
                AssertEqual(0, await sweep.RetryDeferredLandingsAsync().ConfigureAwait(false), "not retried while the checkout is dirty");

                File.WriteAllText(readme, original);
                AssertEqual(1, await sweep.RetryDeferredLandingsAsync().ConfigureAwait(false), "retried once clean");

                Mission landed = (await testDb.Driver.Missions.ReadAsync(f.Mission.Id).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.Complete, landed.Status, landed.FailureReason);
                AssertEqual(landed.CommitHash, await HarborDockGitFixture.ResolveAsync(f.Repos.Work, "refs/heads/main"), "the checkout's main is the landed commit");
            }));

            cases.Add(CaseAsync("deferred_landing_stops_at_deadline", "A deferred landing past its deadline is not retried, and the expiry is recorded once", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string checkout = TestTemp.NewDirectory("deferred_clean");
                Mission mission = await testDb.Driver.Missions.CreateAsync(new Mission("Deferred")
                {
                    Status = MissionStatusEnum.LandingFailed,
                    FailureKind = MissionFailureKindEnum.Infra,
                    FailureReason = "Error merging locally: dirty"
                }).ConfigureAwait(false);
                LandingDeferral deferral = new LandingDeferral
                {
                    CheckoutPath = checkout,
                    FirstDeferredUtc = DateTime.UtcNow.AddHours(-30),
                    DeadlineUtc = DateTime.UtcNow.AddHours(-6)
                };
                await testDb.Driver.Events.CreateAsync(new ArmadaEvent(LandingIntegrityService.DeferredEventType, "deferred")
                {
                    MissionId = mission.Id, EntityType = "mission", EntityId = mission.Id, Payload = JsonSerializer.Serialize(deferral)
                }).ConfigureAwait(false);
                RecordingLandingService landing = new RecordingLandingService();
                LandingIntegrityService sweep = new LandingIntegrityService(CreateLogging(), testDb.Driver, new ArmadaSettings(), new DockService(CreateLogging(), testDb.Driver, CreateSettings(), new StubGitService()),
                    landing, new StubGitService());

                AssertEqual(0, await sweep.RetryDeferredLandingsAsync().ConfigureAwait(false));
                AssertEqual(0, await sweep.RetryDeferredLandingsAsync().ConfigureAwait(false));

                AssertEqual(0, landing.Retried.Count, "no retry after the deadline");
                List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateByMissionAsync(mission.Id).ConfigureAwait(false);
                AssertEqual(1, events.Count(e => e.EventType == LandingIntegrityService.DeferralExpiredEventType), "expiry recorded once");
            }));

            cases.Add(CaseAsync("other_infra_failure_not_retried", "A LandingFailed Infra mission without a deferral (for example a missing dock) is never retried automatically", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await testDb.Driver.Missions.CreateAsync(new Mission("Infra")
                {
                    Status = MissionStatusEnum.LandingFailed,
                    FailureKind = MissionFailureKindEnum.Infra,
                    FailureReason = "Dock missing"
                }).ConfigureAwait(false);
                RecordingLandingService landing = new RecordingLandingService();
                LandingIntegrityService sweep = new LandingIntegrityService(CreateLogging(), testDb.Driver, new ArmadaSettings(), new DockService(CreateLogging(), testDb.Driver, CreateSettings(), new StubGitService()),
                    landing, new StubGitService());

                AssertEqual(0, await sweep.RetryDeferredLandingsAsync().ConfigureAwait(false));
                AssertEqual(0, landing.Retried.Count);
            }));

            foreach (int maxRetries in new[] { 2, 0 })
            {
                cases.Add(CaseAsync("deferred_landing_retries_capped_by_max_landing_retries_" + maxRetries, "Automatic retries of one deferred landing stop at maxLandingRetries (" + maxRetries + "), and the exhaustion is recorded once", maxRetries == 0 ? TestTags.Negative : TestTags.Positive, async () =>
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    string checkout = TestTemp.NewDirectory("deferred_capped");
                    Mission mission = await testDb.Driver.Missions.CreateAsync(new Mission("Deferred and capped")
                    {
                        Status = MissionStatusEnum.LandingFailed,
                        FailureKind = MissionFailureKindEnum.Infra,
                        FailureReason = "Error merging locally: dirty"
                    }).ConfigureAwait(false);
                    LandingDeferral deferral = new LandingDeferral
                    {
                        CheckoutPath = checkout,
                        FirstDeferredUtc = DateTime.UtcNow.AddMinutes(-5),
                        DeadlineUtc = DateTime.UtcNow.AddHours(12)
                    };
                    await testDb.Driver.Events.CreateAsync(new ArmadaEvent(LandingIntegrityService.DeferredEventType, "deferred")
                    {
                        MissionId = mission.Id, EntityType = "mission", EntityId = mission.Id, Payload = JsonSerializer.Serialize(deferral)
                    }).ConfigureAwait(false);
                    ArmadaSettings settings = new ArmadaSettings();
                    settings.MaxLandingRetries = maxRetries;
                    RecordingLandingService landing = new RecordingLandingService();
                    // The recording landing service leaves the mission LandingFailed, as a landing that is refused again would.
                    LandingIntegrityService sweep = new LandingIntegrityService(CreateLogging(), testDb.Driver, settings, new DockService(CreateLogging(), testDb.Driver, CreateSettings(), new StubGitService()),
                        landing, new StubGitService());
                    sweep.LandingRetryInterval = TimeSpan.Zero;

                    int total = 0;
                    for (int i = 0; i < maxRetries + 3; i++) total += await sweep.RetryDeferredLandingsAsync().ConfigureAwait(false);

                    AssertEqual(maxRetries, total, "retries stop at the configured maximum");
                    AssertEqual(maxRetries, landing.Retried.Count);
                    List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateByMissionAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(maxRetries, events.Count(e => e.EventType == LandingIntegrityService.AutoRetryEventType), "each automatic retry is recorded");
                    AssertEqual(1, events.Count(e => e.EventType == LandingIntegrityService.RetriesExhaustedEventType), "exhaustion recorded once");
                }));
            }

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Dock Leases and Integrity", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LandingIntegrityService CreateSweep(RealLandingFixture f, DatabaseDriver db, out LandingService landing)
        {
            landing = new LandingService(f.Logging, db, f.Settings, f.Git);
            landing.Docks = f.Docks;
            landing.OnPerformLanding = f.Handler.HandleMissionCompleteAsync;
            LandingIntegrityService sweep = new LandingIntegrityService(f.Logging, db, f.Settings, f.Docks, landing, f.Git);
            sweep.LandingRetryInterval = TimeSpan.Zero;
            return sweep;
        }

        private static async Task<LeaseEntities> CreateLeaseEntitiesAsync(DatabaseDriver db, MissionStatusEnum status, LandingModeEnum? mode = null)
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 12);
            Vessel vessel = new Vessel("lease-vessel-" + suffix, "https://github.com/test/repo.git");
            vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_lease_bare_" + suffix);
            vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_lease_work_" + suffix);
            vessel.DefaultBranch = "main";
            vessel.LandingMode = mode;
            vessel = await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);

            Dock dock = new Dock(vessel.Id);
            dock.WorktreePath = TestTemp.NewDirectory("lease_dock");
            dock.BranchName = "armada/lease/" + suffix;
            dock.Active = true;
            dock = await db.Docks.CreateAsync(dock).ConfigureAwait(false);

            Mission mission = new Mission("Lease mission " + suffix);
            mission.Status = status;
            mission.VesselId = vessel.Id;
            mission.DockId = dock.Id;
            mission.BranchName = dock.BranchName;
            mission.DiffSnapshot = "diff --git a/file.txt b/file.txt";
            mission = await db.Missions.CreateAsync(mission).ConfigureAwait(false);
            return new LeaseEntities(dock, mission);
        }

        private static async Task<DockReclaimRecord> ReadRecordAsync(DatabaseDriver db, string eventType)
        {
            List<ArmadaEvent> events = await db.Events.EnumerateRecentAsync(50).ConfigureAwait(false);
            ArmadaEvent? evt = events.FirstOrDefault(e => e.EventType == eventType);
            AssertNotNull(evt, eventType + " is recorded");
            DockReclaimRecord? record = JsonSerializer.Deserialize<DockReclaimRecord>(evt!.Payload ?? "");
            AssertNotNull(record, eventType + " carries a reclaim record");
            return record!;
        }

        private static ArmadaSettings CreateSettings()
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = TestTemp.NewDirectory("lease_docks");
            settings.ReposDirectory = TestTemp.NewDirectory("lease_repos");
            settings.LogDirectory = TestTemp.NewDirectory("lease_logs");
            return settings;
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
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

        #region Private-Classes

        private sealed class LeaseEntities
        {
            public Dock Dock { get; }
            public Mission Mission { get; }

            public LeaseEntities(Dock dock, Mission mission)
            {
                Dock = dock;
                Mission = mission;
            }
        }

        private sealed class RecordingLandingService : ILandingService
        {
            public List<string> Retried { get; } = new List<string>();

            public Task<bool> RetryLandingAsync(string missionId, string? tenantId = null, CancellationToken token = default)
            {
                Retried.Add(missionId);
                return Task.FromResult(false);
            }
        }

        #endregion
    }
}
