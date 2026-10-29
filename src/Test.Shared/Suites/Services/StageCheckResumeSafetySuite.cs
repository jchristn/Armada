namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text.Json;
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
    /// Stage-check failure and orphan recovery must not turn an in-flight resume into completion.
    /// </summary>
    public sealed class StageCheckResumeSafetySuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.StageCheckResumeSafety";

        /// <summary>
        /// Build focused regressions for the stage-check/resume race.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case("deserialized_checks_block_only_on_confirmed_failure", async () =>
            {
                StageCheckResult failed = JsonSerializer.Deserialize<StageCheckResult>("{\"Status\":\"Failed\"}")!;
                AssertNull(failed.FailureMessage, "minimal input omits the message");
                AssertTrue(failed.IsBlocking, "a confirmed failure blocks without a message");
                foreach (string json in new[] { "{\"Status\":\"CouldNotRun\",\"Reason\":\"TimedOut\"}", "{\"Reason\":\"TimedOut\"}", "{\"FailureMessage\":\"warning\"}" })
                    AssertFalse(JsonSerializer.Deserialize<StageCheckResult>(json)!.IsBlocking, "unverified warnings do not request recovery: " + json);
                AssertFalse(new StageCheckResult().IsBlocking, "default skipped result retains compatibility");
                AssertFalse(new StageCheckResult { Status = StageCheckStatusEnum.Passed }.IsBlocking, "passing result");
                AssertTrue(new StageCheckResult { Status = StageCheckStatusEnum.Failed, FailureMessage = "regression" }.IsBlocking, "explicit failure remains blocking");
                await Task.CompletedTask;
            }));

            cases.Add(Case("timeout_check_never_completes_without_landing", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                ArmadaSettings settings = new ArmadaSettings { MaxRecoveryAttempts = 2 };
                StubGitService git = new StubGitService();
                DockService docks = new DockService(logging, db, settings, git);
                CaptainService captains = new CaptainService(logging, db, settings, git, docks);
                MissionService missions = new MissionService(logging, db, settings, docks, captains);
                AdmiralService admiral = new AdmiralService(logging, db, settings, captains, missions, new VoyageService(logging, db), docks);
                FakeHostCommandExecutor commands = new FakeHostCommandExecutor
                {
                    Handler = request => request.Kind == HostCommandKindEnum.CheckRun
                        ? new HostCommandResult { TimedOut = true, ExitCode = -1 }
                        : new HostCommandResult()
                };
                typeof(MissionService).GetField("_LocalCommands", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(missions, commands);
                string worktree = TestTemp.NewDirectory("stage_timeout_unverified");
                try
                {
                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("timeout-unverified", "https://example.invalid/repo.git")).ConfigureAwait(false);
                    await db.WorkflowProfiles.CreateAsync(new WorkflowProfile { Name = "timeout", Scope = WorkflowProfileScopeEnum.Vessel, VesselId = vessel.Id, IsDefault = true, Active = true, BuildCommand = "build-timeout" }).ConfigureAwait(false);
                    Dock dock = await db.Docks.CreateAsync(new Dock(vessel.Id) { WorktreePath = worktree, Active = true }).ConfigureAwait(false);
                    Captain captain = await db.Captains.CreateAsync(new Captain("timeout-unverified", AgentRuntimeEnum.Mux) { State = CaptainStateEnum.Working }).ConfigureAwait(false);
                    Mission mission = await db.Missions.CreateAsync(new Mission("timeout") { CaptainId = captain.Id, VesselId = vessel.Id, DockId = dock.Id, Status = MissionStatusEnum.InProgress, StartedUtc = DateTime.UtcNow, ProcessId = 4343 }).ConfigureAwait(false);
                    captain.CurrentMissionId = mission.Id;
                    captain.CurrentDockId = dock.Id;
                    captain.ProcessId = 4343;
                    await db.Captains.UpdateAsync(captain).ConfigureAwait(false);
                    int launches = 0;
                    int landings = 0;
                    captains.OnLaunchAgent = (_, _, _) => { launches++; return Task.FromResult(9001); };
                    // The landing handler declines to land (as when no diff or branch is confirmed).
                    missions.OnMissionComplete = (_, _) => { landings++; return Task.CompletedTask; };
                    await admiral.HandleProcessExitAsync(4343, 0, captain.Id, mission.Id).ConfigureAwait(false);
                    Mission after = (await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    AssertTrue(commands.Requests.Any(r => r.Kind == HostCommandKindEnum.CheckRun && r.Arguments.Contains("build-timeout")), "capture saw the configured check");
                    List<ArmadaEvent> events = await db.Events.EnumerateByMissionAsync(mission.Id).ConfigureAwait(false);
                    StageCheckResult? recorded = StageCheckEvents.Read(events.FirstOrDefault(e => e.EventType == StageCheckEvents.EventType));
                    AssertNotNull(recorded, "the timeout is recorded");
                    AssertEqual(StageCheckStatusEnum.CouldNotRun, recorded!.Status, "a timeout is never recorded as passing");
                    AssertEqual(StageCheckReasonEnum.TimedOut, recorded.Reason);
                    AssertEqual(0, launches, "an unverified check does not resume the captain");
                    AssertNotEqual(MissionStatusEnum.Complete, after.Status, "no Complete without a confirmed landing");
                    AssertNull(after.CompletedUtc, "not completed");
                    AssertNull(after.CommitHash, "no landing evidence was invented");
                    AssertFalse(events.Any(e => e.EventType == "mission.completed"), "no completion event");
                }
                finally { TestTemp.TryDelete(worktree); }
            }));

            cases.Add(Case("failed_check_resume_excludes_orphan_completion", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                ArmadaSettings settings = new ArmadaSettings { MaxRecoveryAttempts = 2 };
                StubGitService git = new StubGitService();
                DockService docks = new DockService(logging, db, settings, git);
                CaptainService captains = new CaptainService(logging, db, settings, git, docks);
                MissionService missions = new MissionService(logging, db, settings, docks, captains);
                AdmiralService admiral = new AdmiralService(logging, db, settings, captains, missions, new VoyageService(logging, db), docks);
                FakeHostCommandExecutor commands = new FakeHostCommandExecutor
                {
                    // The head fails and the base commit passes, so the real classifier reports a confirmed regression.
                    Handler = request => request.Kind == HostCommandKindEnum.CheckRun && request.Arguments.Any(a => a.Contains("build-broken", StringComparison.Ordinal))
                        && !request.WorkingDirectory.EndsWith(".base-check", StringComparison.Ordinal)
                        ? new HostCommandResult { ExitCode = 1, StandardOutput = "error CS0001: broken build" }
                        : new HostCommandResult()
                };
                // Inject at the command boundary: the real stage-check implementation still classifies the failure.
                typeof(MissionService).GetField("_LocalCommands", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(missions, commands);
                string worktree = TestTemp.NewDirectory("stage_failed_resume");
                settings.LogDirectory = TestTemp.NewDirectory("stage_failed_resume_logs");
                try
                {
                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("timeout", "https://example.invalid/repo.git")).ConfigureAwait(false);
                    await db.WorkflowProfiles.CreateAsync(new WorkflowProfile { Name = "timeout", Scope = WorkflowProfileScopeEnum.Vessel, VesselId = vessel.Id, IsDefault = true, Active = true, BuildCommand = "build-broken" }).ConfigureAwait(false);
                    Dock dock = await db.Docks.CreateAsync(new Dock(vessel.Id) { WorktreePath = worktree, Active = true }).ConfigureAwait(false);
                    Directory.CreateDirectory(Path.Combine(settings.LogDirectory, "docks"));
                    await File.WriteAllTextAsync(Path.Combine(settings.LogDirectory, "docks", dock.Id + ".start"), "0123456789abcdef0123456789abcdef01234567").ConfigureAwait(false);
                    Captain captain = await db.Captains.CreateAsync(new Captain("timeout", AgentRuntimeEnum.Mux) { State = CaptainStateEnum.Working }).ConfigureAwait(false);
                    Mission mission = await db.Missions.CreateAsync(new Mission("timeout") { CaptainId = captain.Id, VesselId = vessel.Id, DockId = dock.Id, Status = MissionStatusEnum.InProgress, StartedUtc = DateTime.UtcNow, ProcessId = 4242, Mode = MissionModeEnum.Research }).ConfigureAwait(false);
                    captain.CurrentMissionId = mission.Id;
                    captain.CurrentDockId = dock.Id;
                    captain.ProcessId = 4242;
                    await db.Captains.UpdateAsync(captain).ConfigureAwait(false);
                    int launches = 0;
                    captains.OnLaunchAgent = async (holder, resumed, _) =>
                    {
                        launches++;
                        // Reproduce a concurrent stale captain release before the new PID has been persisted.
                        holder.CurrentMissionId = null;
                        await db.Captains.UpdateAsync(holder).ConfigureAwait(false);
                        Mission snapshot = (await db.Missions.ReadAsync(resumed.Id).ConfigureAwait(false))!;
                        AssertNull(snapshot.ProcessId, "launch is still in flight");
                        await RecoverOrphanAsync(admiral, snapshot).ConfigureAwait(false);
                        Mission during = (await db.Missions.ReadAsync(resumed.Id).ConfigureAwait(false))!;
                        AssertEqual(MissionStatusEnum.InProgress, during.Status, "orphan recovery must not complete a resume");
                        AssertNull(during.CommitHash, "no landing occurred");
                        holder.CurrentMissionId = resumed.Id;
                        await db.Captains.UpdateAsync(holder).ConfigureAwait(false);
                        return 8001;
                    };
                    await admiral.HandleProcessExitAsync(4242, 0, captain.Id, mission.Id).ConfigureAwait(false);
                    Mission after = (await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    AssertEqual(1, launches, "exactly one new run");
                    AssertEqual(MissionStatusEnum.InProgress, after.Status, after.FailureReason);
                    AssertEqual(8001, after.ProcessId, "new process attached");
                    AssertContains("CS0001", after.Description ?? "");
                    AssertTrue(commands.Requests.Any(r => r.Kind == HostCommandKindEnum.CheckRun && r.Arguments.Any(a => a.Contains("build-broken", StringComparison.Ordinal))), "capture saw the configured check");
                    AssertFalse((await db.Events.EnumerateByMissionAsync(mission.Id).ConfigureAwait(false)).Any(e => e.EventType == "mission.orphan_recovered" || e.EventType == "mission.completed"), "no false completion or orphan event");
                    // A stale completion must still be blocked after the resume guard has been released.
                    await missions.HandleCompletionAsync(captain, mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.InProgress, (await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.Status, "a failed check blocks a stale completion");
                    after.ProcessId = null;
                    await db.Missions.UpdateAsync(after).ConfigureAwait(false);
                    await db.Events.CreateAsync(StageCheckEvents.Build(after, captain, new StageCheckResult { Status = StageCheckStatusEnum.Passed, CheckedUtc = DateTime.UtcNow.AddSeconds(1) })).ConfigureAwait(false);
                    await missions.HandleCompletionAsync(captain, mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Complete, (await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.Status, "a later passing check permits the existing read-only completion contract");
                }
                finally { TestTemp.TryDelete(worktree); TestTemp.TryDelete(settings.LogDirectory); }
            }));

            foreach (string handoff in new[] { "enqueue", "throw", "silent", "conflict" })
            {
                cases.Add(Case("passed_check_completion_" + handoff, async () =>
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    DatabaseDriver db = testDb.Driver;
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;
                    ArmadaSettings settings = new ArmadaSettings();
                    StubGitService git = new StubGitService();
                    DockService docks = new DockService(logging, db, settings, git);
                    CaptainService captains = new CaptainService(logging, db, settings, git, docks);
                    MissionService missions = new MissionService(logging, db, settings, docks, captains);
                    MergeQueueService queue = new MergeQueueService(logging, db, settings, git);
                    string worktree = TestTemp.NewDirectory("passed_handoff");
                    try
                    {
                        Vessel vessel = await db.Vessels.CreateAsync(new Vessel("passed-handoff", "https://example.invalid/repo.git") { LandingMode = LandingModeEnum.MergeQueue }).ConfigureAwait(false);
                        Dock dock = await db.Docks.CreateAsync(new Dock(vessel.Id) { WorktreePath = worktree, Active = true }).ConfigureAwait(false);
                        Captain captain = await db.Captains.CreateAsync(new Captain("passed", AgentRuntimeEnum.Mux)).ConfigureAwait(false);
                        Mission mission = await db.Missions.CreateAsync(new Mission("passed")
                        {
                            VesselId = vessel.Id, CaptainId = captain.Id, DockId = dock.Id, Status = MissionStatusEnum.InProgress,
                            BranchName = "work", DiffSnapshot = "diff --git a/work.txt b/work.txt\n+work", CommitHash = "abc123"
                        }).ConfigureAwait(false);
                        await db.Events.CreateAsync(StageCheckEvents.Build(mission, captain,
                            JsonSerializer.Deserialize<StageCheckResult>("{\"Status\":\"Passed\"}")!)).ConfigureAwait(false);
                        int calls = 0;
                        missions.OnMissionComplete = async (m, _) =>
                        {
                            calls++;
                            if (handoff == "throw") throw new InvalidOperationException("enqueue unavailable");
                            if (handoff == "conflict") throw new GitMergeConflictException("conflict integrating the target", new[] { "work.txt" });
                            if (handoff == "silent") return;
                            await queue.EnqueueAsync(new MergeEntry { MissionId = m.Id, VesselId = vessel.Id, BranchName = "work", TargetBranch = "main" }).ConfigureAwait(false);
                        };
                        await missions.HandleCompletionAsync(captain, mission.Id).ConfigureAwait(false);
                        await missions.HandleCompletionAsync(captain, mission.Id).ConfigureAwait(false);
                        AssertEqual(1, calls, "completion hands off exactly once");
                        Mission stored = (await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                        AssertEqual(handoff != "enqueue" ? MissionStatusEnum.LandingFailed : MissionStatusEnum.WorkProduced, stored.Status);
                        AssertEqual(handoff != "enqueue" ? 0 : 1, (await db.MergeEntries.EnumerateAsync(new EnumerationQuery { MissionId = mission.Id })).Objects.Count);
                        if (handoff == "throw") AssertContains("enqueue unavailable", stored.FailureReason ?? "");
                        // A stranded handoff records what failed: a real merge conflict, otherwise an environment failure.
                        if (handoff != "enqueue")
                            AssertEqual(handoff == "conflict" ? MissionFailureKindEnum.LandingConflict : MissionFailureKindEnum.Infra, stored.FailureKind, handoff);
                    }
                    finally { TestTemp.TryDelete(worktree); }
                }));
            }

            foreach (string scenario in new[] { "missing", "queued", "fresh", "manual", "blocked", "override", "default", "skipped", "review", "readonly", "dependent", "unchecked", "full" })
            {
                cases.Add(Case("passed_check_merge_handoff_" + scenario, async () =>
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    DatabaseDriver db = testDb.Driver;
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;
                    ArmadaSettings settings = new ArmadaSettings { LandingMode = LandingModeEnum.MergeQueue };
                    StubGitService git = new StubGitService();
                    DockService docks = new DockService(logging, db, settings, git);
                    CaptainService captains = new CaptainService(logging, db, settings, git, docks);
                    MissionService missions = new MissionService(logging, db, settings, docks, captains);
                    AdmiralService admiral = new AdmiralService(logging, db, settings, captains, missions, new VoyageService(logging, db), docks);
                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("handoff-" + scenario, "https://example.invalid/repo.git")
                    {
                        LandingMode = scenario == "manual" ? LandingModeEnum.None : scenario == "default" ? null : LandingModeEnum.MergeQueue
                    }).ConfigureAwait(false);
                    Voyage voyage = await db.Voyages.CreateAsync(new Voyage("handoff")
                    {
                        LandingMode = scenario == "override" ? LandingModeEnum.None : null
                    }).ConfigureAwait(false);
                    Mission mission = await db.Missions.CreateAsync(new Mission("handoff")
                    {
                        VesselId = vessel.Id, VoyageId = voyage.Id, Status = MissionStatusEnum.WorkProduced,
                        LastUpdateUtc = scenario == "fresh" ? DateTime.UtcNow : DateTime.UtcNow.AddMinutes(-10)
                    }).ConfigureAwait(false);
                    if (scenario == "review" || scenario == "readonly")
                    {
                        mission.RequiresReview = scenario == "review";
                        if (scenario == "readonly") mission.Mode = MissionModeEnum.Research;
                        await db.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    }
                    if (scenario == "dependent") await db.Missions.CreateAsync(new Mission("next")
                    {
                        VoyageId = voyage.Id, DependsOnMissionId = mission.Id, Status = MissionStatusEnum.Pending
                    }).ConfigureAwait(false);
                    Captain captain = new Captain("check", AgentRuntimeEnum.Mux);
                    StageCheckResult check = JsonSerializer.Deserialize<StageCheckResult>(scenario == "blocked"
                        ? "{\"Status\":\"Failed\"}" : scenario == "skipped" ? "{}" : "{\"Status\":\"Passed\"}")!;
                    if (scenario == "full") check = new StageCheckResult
                    {
                        Status = StageCheckStatusEnum.Passed, CheckedUtc = DateTime.UtcNow, DurationMs = 100,
                        Commands = new List<string> { "dotnet build" }
                    };
                    if (scenario != "unchecked") await db.Events.CreateAsync(StageCheckEvents.Build(mission, captain, check)).ConfigureAwait(false);
                    if (scenario == "queued") await db.MergeEntries.CreateAsync(new MergeEntry
                    {
                        MissionId = mission.Id, VesselId = vessel.Id, BranchName = "work", TargetBranch = "main"
                    }).ConfigureAwait(false);
                    for (int i = 0; i < 2; i++)
                        await (Task)typeof(AdmiralService).GetMethod("FlagMissingMergeHandoffsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                            .Invoke(admiral, new object[] { scenario == "fresh" ? DateTime.UtcNow : DateTime.UtcNow.AddMinutes(10), CancellationToken.None })!;
                    bool flagged = scenario == "missing" || scenario == "default" || scenario == "skipped" || scenario == "full";
                    Mission stored = (await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    AssertEqual(flagged ? MissionStatusEnum.LandingFailed : MissionStatusEnum.WorkProduced, stored.Status, scenario);
                    // A lost handoff is Infra: left kindless it would classify as LandingConflict and be auto-rescued.
                    if (flagged) AssertEqual(MissionFailureKindEnum.Infra, stored.FailureKind, scenario);
                    AssertEqual(flagged ? 1 : 0, (await db.Events.EnumerateByMissionAsync(mission.Id).ConfigureAwait(false))
                        .Count(e => e.EventType == "mission.landing_handoff_missing"), "one persistent flag, no repeated notifications");
                }));
            }

            cases.Add(Case("unknown_orphan_exit_preserves_unlanded_work", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                ArmadaSettings settings = new ArmadaSettings();
                StubGitService git = new StubGitService();
                DockService docks = new DockService(logging, db, settings, git);
                CaptainService captains = new CaptainService(logging, db, settings, git, docks);
                MissionService missions = new MissionService(logging, db, settings, docks, captains);
                AdmiralService admiral = new AdmiralService(logging, db, settings, captains, missions, new VoyageService(logging, db), docks);
                Captain captain = await db.Captains.CreateAsync(new Captain("moved on")).ConfigureAwait(false);
                admiral.OnIsProcessTracked = pid => pid == 1234;
                foreach (int? processId in new int?[] { null, Int32.MaxValue, 1234 })
                {
                    Mission mission = await db.Missions.CreateAsync(new Mission("unverified orphan") { CaptainId = captain.Id, Status = MissionStatusEnum.InProgress, StartedUtc = DateTime.UtcNow, ProcessId = processId, Mode = MissionModeEnum.Research }).ConfigureAwait(false);
                    await RecoverOrphanAsync(admiral, mission).ConfigureAwait(false);
                    Mission after = (await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    AssertEqual(processId == Int32.MaxValue ? MissionStatusEnum.WorkProduced : MissionStatusEnum.InProgress, after.Status, "an unconfirmed exit cannot complete");
                    AssertNull(after.CompletedUtc, "not completed");
                    AssertNull(after.CommitHash, "no landing evidence");
                    if (processId == Int32.MaxValue) AssertContains("not verified", after.FailureReason!);
                    if (processId == Int32.MaxValue) AssertEqual(MissionFailureKindEnum.Infra, after.FailureKind, "an unverified orphan exit records a kind");
                    Mission stale = after;
                    if (processId == Int32.MaxValue)
                    {
                        after.Status = MissionStatusEnum.InProgress;
                        after.ProcessId = 1234;
                        await db.Missions.UpdateAsync(after).ConfigureAwait(false);
                        stale = JsonSerializer.Deserialize<Mission>(JsonSerializer.Serialize(after))!;
                        stale.ProcessId = Int32.MaxValue;
                        await RecoverOrphanAsync(admiral, stale).ConfigureAwait(false);
                        AssertEqual(1234, (await db.Missions.ReadAsync(after.Id).ConfigureAwait(false))!.ProcessId, "a stale snapshot cannot claim a newer run");
                    }
                }
            }));
            return new TestSuiteDescriptor(SuiteId, "Stage-check resume safety", cases);
        }

        private static Task RecoverOrphanAsync(AdmiralService service, Mission mission)
        {
            return (Task)typeof(AdmiralService).GetMethod("RecoverOrphanedMissionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(service, new object[] { mission, CancellationToken.None })!;
        }

        private static TestCaseDescriptor Case(string id, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: id, displayName: id, executeAsync: _ => body(), tags: new List<string> { TestTags.Reliability });
        }
    }
}
