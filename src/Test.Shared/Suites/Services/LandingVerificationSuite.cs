namespace Test.Shared.Suites.Services
{
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Final integration evidence, mutation refusal, host routing, and deletion audit regressions.</summary>
    public sealed class LandingVerificationSuite : IArmadaTestSuite
    {
        /// <summary>Build the regression cases.</summary>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new();
            foreach (string outcome in new[] { "Passed", "Failed", "TimedOut", "ChangedHead", "DirtyTree", "CouldNotRun", "Skipped" })
            {
                cases.Add(CaseAsync("receipt_" + outcome, "Final landing verification records and enforces " + outcome, async () =>
                {
                    using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync();
                    StubGitService git = new();
                    FakeHostCommandExecutor commands = new()
                    {
                        Handler = _ =>
                        {
                            if (outcome == "CouldNotRun") return null;
                            if (outcome == "ChangedHead") git.HeadCommitResult = "changed";
                            if (outcome == "DirtyTree") git.WorkingTreeStatusResult = new GitWorkingTreeStatus { ModifiedCount = 1 };
                            return new HostCommandResult { ExitCode = outcome == "Failed" ? 7 : 0, TimedOut = outcome == "TimedOut", StandardOutput = "check evidence" };
                        }
                    };
                    MergeEntry entry = await db.Driver.MergeEntries.CreateAsync(new MergeEntry("feature/test"));
                    string? sha = null;
                    try
                    {
                        sha = await LandingVerification.VerifyAsync(db.Driver, git, commands, "/harbor/worktree", false,
                            outcome == "Skipped" ? Array.Empty<string>() : new[] { "check integration" }, 1, entry: entry);
                    }
                    catch (Exception) when (outcome != "Passed" && outcome != "Skipped") { }
                    AssertEqual(outcome is "Passed" or "Skipped", sha != null, "Only a passing or explicitly skipped check can return the commit.");
                    MergeEntry stored = (await db.Driver.MergeEntries.ReadAsync(entry.Id))!;
                    AssertContains("abc123def456", stored.TestOutput!);
                    AssertContains(outcome is "ChangedHead" or "DirtyTree" ? "Failed" : outcome, stored.TestOutput!);
                    if (outcome == "Passed") AssertEqual(0, stored.TestExitCode!.Value);
                    AssertTrue((await db.Driver.Events.EnumerateRecentAsync(20)).Any(e => e.EventType == "mission.landing_verification"), "Receipt is durable.");
                }));
            }
            cases.Add(CaseAsync("uncommitted_without_checks", "A dock with uncommitted work cannot land even when no checks are configured", async () =>
            {
                using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync();
                StubGitService git = new() { WorkingTreeStatusResult = new GitWorkingTreeStatus { ModifiedCount = 1 } };
                bool refused = false;
                try { await LandingVerification.VerifyAsync(db.Driver, git, new FakeHostCommandExecutor(), "/dock", false, Array.Empty<string>(), 1); }
                catch (InvalidOperationException) { refused = true; }
                AssertTrue(refused, "Uncommitted work must not be reported as published.");
            }));
            cases.Add(CaseAsync("retry_preserves_new_owner", "Landing retry preserves cancellation that occurs during repository fetch", async () =>
            {
                using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync();
                Vessel vessel = await db.Driver.Vessels.CreateAsync(new Vessel("Retry owner", "https://example.invalid/repo") { LocalPath = "/repo" });
                Mission mission = await db.Driver.Missions.CreateAsync(new Mission("Retry") { Status = MissionStatusEnum.LandingFailed, VesselId = vessel.Id, BranchName = "main" });
                StubGitService git = new() { OnFetch = async () =>
                {
                    Mission current = (await db.Driver.Missions.ReadAsync(mission.Id))!;
                    current.Status = MissionStatusEnum.Cancelled;
                    await db.Driver.Missions.UpdateAsync(current);
                } };
                LandingService landing = new(Logging(), db.Driver, new ArmadaSettings(), git);
                bool called = false;
                landing.OnPerformLanding = (_, _) => { called = true; return Task.CompletedTask; };
                AssertFalse(await landing.RetryLandingAsync(mission.Id));
                AssertFalse(called);
                AssertEqual(MissionStatusEnum.Cancelled, (await db.Driver.Missions.ReadAsync(mission.Id))!.Status);
            }));
            cases.Add(CaseAsync("mandatory_armada_gate", "Armada final landing keeps critical integration checks with a smoke-only profile", async () =>
            {
                WorkflowProfile profile = new() { BuildCommand = "build", UnitTestCommand = "smoke" };
                List<string> commands = LandingVerification.Commands(profile, new Vessel("Armada", "https://github.com/jchristn/Armada.git"));
                AssertTrue(commands.Contains("smoke") && commands.Contains(LandingVerification.ArmadaCommand), "Keep the user's checks and add the integration gate.");
                AssertEqual("dotnet test \"src/Test.Smoke/Test.Smoke.csproj\" -c Release -f net10.0", LandingVerification.ArmadaCommand, "The Armada gate is the smoke test run, not the Test.Automated suite list.");
                AssertEqual(LandingVerification.ArmadaCommand, commands[commands.Count - 1], "Workflow profile commands run before the Armada gate.");
                AssertFalse(LandingVerification.Commands(profile, new Vessel("Other", "https://github.com/example/Other")).Contains(LandingVerification.ArmadaCommand));
                await Task.CompletedTask;
            }));
            cases.Add(CaseAsync("harbor_verification", "Final verification runs real git and commands across the Harbor link, then refuses a disconnected Harbor", async () =>
            {
                using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync();
                LoggingModule logging = Logging();
                HarborConnectionManager manager = new(new HarborService(db.Driver, logging), logging, "http://localhost/mcp");
                using InProcessHarbor harbor = await InProcessHarbor.ConnectAsync(manager, "hbr_verified", Constants.DefaultTenantId,
                    Constants.DefaultUserId, new AgentRuntimeFactory(logging), new[] { "git" }, logging);
                DockHost host = new DockHostResolver(new StubGitService(), logging, manager).ForHarbor(harbor.HarborId);
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                string sha = await LandingVerification.VerifyAsync(db.Driver, host.Git, host.Commands, repos.Work,
                    OperatingSystem.IsWindows(), new[] { "git rev-parse HEAD" }, 1);
                AssertEqual(repos.OriginHead("main"), sha);
                harbor.Dispose();
                bool refused = false;
                try { await LandingVerification.VerifyAsync(db.Driver, host.Git, host.Commands, repos.Work, false, new[] { "git rev-parse HEAD" }, 1); }
                catch (Exception) { refused = true; }
                AssertTrue(refused, "Disconnected Harbor cannot fall back to the Admiral.");
            }));
            cases.Add(CaseAsync("harbor_retry_without_admiral_clone", "Landing retry uses the Harbor repository across reconnect without an Admiral-side clone", async () =>
            {
                using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync();
                LoggingModule logging = Logging();
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/retry", "retry.txt", "retry\n");
                GitService git = new(logging);
                string dockPath = TestTemp.NewDirectory("remote_retry") + "_dock";
                await git.CreateWorktreeAsync(repos.VesselRepo, dockPath, "feature/retry");
                Vessel vessel = await db.Driver.Vessels.CreateAsync(new Vessel("Harbor only", repos.Origin) { LocalPath = null });
                Dock dock = await db.Driver.Docks.CreateAsync(new Dock(vessel.Id) { Active = true, HarborId = "hbr_retry", RepositoryPath = repos.VesselRepo,
                    WorktreePath = dockPath, BranchName = "feature/retry" });
                Mission mission = await db.Driver.Missions.CreateAsync(new Mission("Retry remotely") { Status = MissionStatusEnum.LandingFailed,
                    VesselId = vessel.Id, DockId = dock.Id, BranchName = dock.BranchName });
                HarborConnectionManager manager = new(new HarborService(db.Driver, logging), logging, "http://localhost/mcp");
                LandingService landing = new(logging, db.Driver, new ArmadaSettings(), new StubGitService { ShouldThrowOnPush = true });
                landing.DockHosts = new DockHostResolver(new StubGitService(), logging, manager);
                bool invoked = false;
                landing.OnPerformLanding = async (m, d) => { invoked = true; m.Status = MissionStatusEnum.Complete; await db.Driver.Missions.UpdateAsync(m); };
                AssertFalse(await landing.RetryLandingAsync(mission.Id));
                AssertFalse(invoked, "No fallback while Harbor is disconnected.");
                using InProcessHarbor harbor = await InProcessHarbor.ConnectAsync(manager, "hbr_retry", Constants.DefaultTenantId, Constants.DefaultUserId,
                    new AgentRuntimeFactory(logging), new[] { "git", "harbor-docks" }, logging,
                    docks: new HarborDockManager(() => new HarborDockSettings { DocksDirectory = Path.GetDirectoryName(dockPath)!, ReposDirectory = repos.VesselRepo }, logging));
                AssertTrue(await landing.RetryLandingAsync(mission.Id));
                AssertTrue(invoked);
                AssertEqual(MissionStatusEnum.Complete, (await db.Driver.Missions.ReadAsync(mission.Id))!.Status);
            }));
            foreach (int scope in new[] { 0, 1, 2 })
            {
                cases.Add(CaseAsync("archive_delete_scope_" + scope, "Mission deletion preserves failure/attempt evidence and respects scope " + scope, async () =>
                {
                    using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync();
                    Mission mission = await db.Driver.Missions.CreateAsync(new Mission("Preserve failure")
                    {
                        TenantId = Constants.DefaultTenantId, UserId = Constants.DefaultUserId,
                        Status = MissionStatusEnum.LandingFailed, FailureReason = "push rejected", BranchName = "feature/recover", CommitHash = "evidence-sha"
                    });
                    await db.Driver.StageOutcomes.CreateAsync(new StageOutcome { MissionId = mission.Id, TenantId = mission.TenantId, Attempt = 1 });
                    await db.Driver.Missions.DeleteAsync("another-tenant", mission.Id);
                    AssertEqual(0, (await db.Driver.Events.EnumerateByMissionAsync(mission.Id, 20)).Count, "Unauthorized scope produces no archive.");
                    if (scope == 0) await db.Driver.Missions.DeleteAsync(mission.Id);
                    else if (scope == 1) await db.Driver.Missions.DeleteAsync(mission.TenantId!, mission.Id);
                    else await db.Driver.Missions.DeleteAsync(mission.TenantId!, mission.UserId!, mission.Id);
                    await CascadeCleanup.RemoveEventsForMissionAsync(db.Driver, mission.Id);
                    ArmadaEvent archive = (await db.Driver.Events.EnumerateByMissionAsync(mission.Id, 20)).Single(e => e.EventType == "mission.archive");
                    AssertEqual(mission.TenantId, archive.TenantId);
                    AssertTrue(CommandAudit.IsAuditEvent(archive.EventType), "Only a global admin may delete retained failure evidence.");
                    AssertTrue(CommandAudit.IsAuditEvent("mission.landing_verification"), "Verification receipts receive audit protection too.");
                    AssertContains("push rejected", archive.Payload!);
                    AssertContains("evidence-sha", archive.Payload!);
                    using JsonDocument payload = JsonDocument.Parse(archive.Payload!);
                    AssertEqual(1, payload.RootElement.GetProperty("StageOutcomes").GetArrayLength());
                    AssertFalse(payload.RootElement.GetProperty("ResolutionVerified").GetBoolean());
                    AssertNull(await db.Driver.Missions.ReadAsync(mission.Id));
                }));
            }
            cases.Add(CaseAsync("verified_push_dirty_checkout", "Verified push lands the exact SHA while a developer checkout remains dirty", async () =>
            {
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/verified", "verified.txt", "verified\n");
                GitService git = new(Logging());
                string dock = TestTemp.NewDirectory("verified_dock") + "_worktree";
                await git.CreateWorktreeAsync(repos.VesselRepo, dock, "feature/verified");
                string sha = (await git.GetHeadCommitHashAsync(dock))!;
                File.WriteAllText(Path.Combine(repos.Work, "README.md"), "operator edits\n");
                await git.PushVerifiedCommitAsync(dock, sha, "main");
                AssertEqual(sha, repos.OriginHead("main"));
                AssertEqual("operator edits\n", File.ReadAllText(Path.Combine(repos.Work, "README.md")));
                AssertTrue((await git.GetWorkingTreeStatusAsync(repos.Work)).ModifiedCount > 0);
                bool rejected = false;
                try { await git.PushVerifiedCommitAsync(dock, "wrong-sha", "main"); } catch (InvalidOperationException) { rejected = true; }
                AssertTrue(rejected, "A different SHA is never published.");
            }));
            cases.Add(CaseAsync("unpublished_ancestor_refused", "Publication refuses inherited unpublished commits even after the developer branch advances further", async () =>
            {
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                GitService git = new(Logging());
                string candidate = await HarborDockGitFixture.CommitFileAsync(repos.Work, "private.txt", "private\n", "Unpublished user commit");
                await HarborDockGitFixture.CommitFileAsync(repos.Work, "later.txt", "later\n", "Newer unpublished user commit");
                bool refused = false;
                try { await git.ValidatePublishedAncestryAsync(repos.Work, repos.Work, "main", candidate); }
                catch (GitUnpublishedCommitsException) { refused = true; }
                AssertTrue(refused, "Checking only the latest local tip would miss the earlier unpublished ancestor.");
                AssertFalse(repos.OriginHasFile("main", "private.txt"));
            }));
            cases.Add(CaseAsync("target_advanced_after_check", "A target advancing after verification rejects the stale push without overwriting newer work", async () =>
            {
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/stale", "feature.txt", "feature\n");
                GitService git = new(Logging());
                string dock = TestTemp.NewDirectory("stale_dock") + "_worktree";
                await git.CreateWorktreeAsync(repos.VesselRepo, dock, "feature/stale");
                string sha = (await git.GetHeadCommitHashAsync(dock))!;
                repos.AdvanceMain("newer.txt", "newer\n");
                string newer = repos.OriginHead("main");
                bool rejected = false;
                try { await git.PushVerifiedCommitAsync(dock, sha, "main"); } catch (InvalidOperationException) { rejected = true; }
                AssertTrue(rejected);
                AssertEqual(newer, repos.OriginHead("main"));
                AssertTrue(await git.BranchExistsAsync(repos.VesselRepo, "feature/stale"), "Source work remains recoverable.");
            }));
            return new TestSuiteDescriptor(suiteId: "Services.LandingVerification", displayName: "Final Landing Verification", cases: cases);
        }
        private static LoggingModule Logging() { LoggingModule logging = new(); logging.Settings.EnableConsole = false; return logging; }
        private static TestCaseDescriptor CaseAsync(string id, string name, Func<Task> body)
            => new(suiteId: "Services.LandingVerification", caseId: id, displayName: name, tags: new List<string> { TestTags.Positive }, executeAsync: async _ => await body());
    }
}
