namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Health;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Operations that need a vessel's checkout outside a mission, on a real in-process Harbor linked over the Harbor
    /// protocol with real git repositories, and with an Admiral data folder that has no working directory for the vessel:
    /// the resolver (Admiral first, Harbor with the vessel's recent docks first, discovered checkouts, owner and tenant
    /// scoping, typed errors when there is none), a check run executed in the Harbor's checkout (output, exit code,
    /// artifacts, audit), Workspace browse, read, write, search, exec, changes, and diff on the Harbor with confinement
    /// (traversal, .git, symbolic links, and roots that are not the vessel's checkout are refused), readiness, and health.
    /// </summary>
    public sealed class VesselHostSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.VesselHost";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("resolver_uses_admiral_working_directory", "A vessel whose working directory exists on the Admiral host is served there, even with a Harbor connected", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_vh_local").ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync("app", git.Origin, git.Checkout).ConfigureAwait(false);

                VesselHost host = await s.Resolver.ResolveAsync(vessel, null).ConfigureAwait(false);

                AssertFalse(host.IsHarbor, "the Admiral host");
                AssertEqual("Admiral", host.HostLabel);
                AssertTrue(PathCanonicalizer.AreEquivalent(git.Checkout, host.WorkingDirectory), "the vessel's working directory");
            }));

            cases.Add(CaseAsync("resolver_finds_mapped_harbor_checkout", "With no working directory on the Admiral, the resolver uses the checkout a connected Harbor maps the vessel to", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_vh_mapped").ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync("app", null, null).ConfigureAwait(false);

                VesselHost host = await s.Resolver.ResolveAsync(vessel, null).ConfigureAwait(false);

                AssertTrue(host.IsHarbor, "a Harbor");
                AssertEqual("hbr_vh_mapped", host.HarborId);
                AssertEqual(HarborRepositorySourceEnum.Mapped, host.Source);
                AssertTrue(PathCanonicalizer.AreEquivalent(git.Checkout, host.WorkingDirectory), "the mapped checkout");
                AssertContains("hbr_vh_mapped", host.HostLabel);
                AssertFalse(HarborDockSettings.IsSameOrUnder(host.WorkingDirectory, git.AdmiralData), "not in the Admiral's data folder");
            }));

            cases.Add(CaseAsync("resolver_finds_discovered_checkout", "A Harbor finds the vessel's checkout under its root folders by the repository URL", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                HarborDockSettings settings = git.Settings.Clone();
                settings.RepositoryRoots.Add(git.CodeRoot);
                using InProcessHarbor harbor = await s.ConnectAsync("hbr_vh_found", settings, null, null).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync("DocConverter", git.Origin, null).ConfigureAwait(false);

                VesselHost host = await s.Resolver.ResolveAsync(vessel, null).ConfigureAwait(false);

                AssertEqual("hbr_vh_found", host.HarborId);
                AssertEqual(HarborRepositorySourceEnum.Discovered, host.Source);
                AssertTrue(PathCanonicalizer.AreEquivalent(git.Checkout, host.WorkingDirectory), "the discovered checkout");
            }));

            cases.Add(CaseAsync("resolver_prefers_harbor_with_recent_docks", "Of two Harbors that can serve the vessel, the one that owns its most recent docks is used", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor first = await s.ConnectMappedAsync("hbr_vh_a_first").ConfigureAwait(false);
                using InProcessHarbor second = await s.ConnectMappedAsync("hbr_vh_b_second").ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync("app", null, null).ConfigureAwait(false);

                VesselHost byName = await s.Resolver.ResolveAsync(vessel, null).ConfigureAwait(false);
                AssertEqual("hbr_vh_a_first", byName.HarborId, "without docks, the first Harbor by name");

                Dock older = new Dock(vessel.Id) { HarborId = "hbr_vh_a_first", WorktreePath = Path.Combine(git.HarborDocks, "older"), RepositoryPath = git.Checkout, Active = false };
                older.LastUpdateUtc = DateTime.UtcNow.AddHours(-2);
                await s.Db.Driver.Docks.CreateAsync(older).ConfigureAwait(false);
                Dock recent = new Dock(vessel.Id) { HarborId = "hbr_vh_b_second", WorktreePath = Path.Combine(git.HarborDocks, "recent"), RepositoryPath = git.Checkout, Active = false };
                recent.LastUpdateUtc = DateTime.UtcNow;
                await s.Db.Driver.Docks.CreateAsync(recent).ConfigureAwait(false);

                VesselHost preferred = await s.Resolver.ResolveAsync(vessel, null).ConfigureAwait(false);
                AssertEqual("hbr_vh_b_second", preferred.HarborId, "the Harbor with the most recent dock");
            }));

            cases.Add(CaseAsync("resolver_respects_owner_and_tenant_scoping", "With requireHarborForLaunch only the requesting user's Harbors count, and another tenant's Harbor never does", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                HarborDockSettings mapped = s.MappedSettings("app");
                using InProcessHarbor owned = await s.ConnectAsync("hbr_vh_owned", mapped, Constants.DefaultTenantId, "usr_vh_owner").ConfigureAwait(false);
                using InProcessHarbor foreign = await s.ConnectAsync("hbr_vh_foreign", s.MappedSettings("app"), "ten_vh_other", null).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync("app", null, null).ConfigureAwait(false);

                s.Settings.RequireHarborForLaunch = true;
                VesselHost forOwner = await s.Resolver.ResolveAsync(vessel, "usr_vh_owner").ConfigureAwait(false);
                AssertEqual("hbr_vh_owned", forOwner.HarborId, "the owner's Harbor");

                VesselCheckoutUnavailableException other = await CatchAsync<VesselCheckoutUnavailableException>(() => s.Resolver.ResolveAsync(vessel, "usr_vh_someone_else")).ConfigureAwait(false);
                AssertEqual(VesselCheckoutErrorCodeEnum.NoHarborConnected, other.Code, "no Harbor of that user, and the other tenant's Harbor is never used");
                AssertEqual(vessel.Id, other.VesselId);
            }));

            cases.Add(CaseAsync("resolver_without_harbor_reports_typed_error", "With no Harbor connected and no working directory, the error is NoHarborConnected", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync("DocConverter", git.Origin, Path.Combine(git.AdmiralData, "not-here")).ConfigureAwait(false);

                VesselHostResolution resolution = await s.Resolver.TryResolveAsync(vessel, null).ConfigureAwait(false);
                VesselCheckoutUnavailableException ex = await CatchAsync<VesselCheckoutUnavailableException>(() => s.Resolver.ResolveAsync(vessel, null)).ConfigureAwait(false);

                AssertNull(resolution.Host);
                AssertEqual(VesselCheckoutErrorCodeEnum.NoHarborConnected, resolution.ErrorCode);
                AssertEqual(VesselCheckoutErrorCodeEnum.NoHarborConnected, ex.Code);
                AssertEqual("DocConverter", ex.VesselName);
                AssertEqual(0, ex.HarborReasons.Count, "no Harbor was asked");
                AssertFalse(String.IsNullOrWhiteSpace(ex.Message), "the message says what to set");
            }));

            cases.Add(CaseAsync("resolver_without_harbor_checkout_reports_typed_error", "With Harbors connected but none with a checkout, the error is NoHarborCheckout with one reason per Harbor", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor clones = await s.ConnectAsync("hbr_vh_clone_only", git.Settings.Clone(), null, null).ConfigureAwait(false);
                using InProcessHarbor nothing = await s.ConnectAsync("hbr_vh_nothing", s.MappedSettings("some-other-vessel"), null, null).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync("DocConverter", git.Origin, null).ConfigureAwait(false);

                VesselCheckoutUnavailableException ex = await CatchAsync<VesselCheckoutUnavailableException>(() => s.Resolver.ResolveAsync(vessel, null)).ConfigureAwait(false);

                AssertEqual(VesselCheckoutErrorCodeEnum.NoHarborCheckout, ex.Code);
                AssertEqual(2, ex.HarborReasons.Count, "one reason per connected Harbor");
                AssertEqual(VesselCheckoutUnavailableException.ErrorCode + "." + VesselCheckoutErrorCodeEnum.NoHarborCheckout, McpToolError.FromException(ex).Code, "the MCP tool error carries the typed code");
                AssertEqual(McpToolErrorCodeEnum.Unavailable, McpToolError.FromException(ex).ErrorCode);
            }));

            cases.Add(CaseAsync("check_run_executes_in_harbor_checkout", "A check run with no working directory on the Admiral runs in the Harbor's checkout and records its output, exit code, artifacts, and audit", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_vh_checks").ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync("app", null, null).ConfigureAwait(false);
                WorkflowProfile profile = new WorkflowProfile
                {
                    TenantId = vessel.TenantId,
                    Name = "Harbor checks",
                    Scope = WorkflowProfileScopeEnum.Vessel,
                    VesselId = vessel.Id,
                    UnitTestCommand = "printf 'Passed!  - Failed: 0, Passed: 3, Skipped: 1, Total: 4, Duration: 1.5 s\\n'"
                        + " && echo on-stderr 1>&2"
                        + " && mkdir -p out"
                        + " && printf '<coverage line-rate=\"0.75\" branch-rate=\"0.5\" lines-covered=\"15\" lines-valid=\"20\" branches-covered=\"4\" branches-valid=\"8\"></coverage>' > out/coverage.cobertura.xml"
                        + " && test -f README.md",
                    ExpectedArtifacts = new List<string> { "out/coverage.cobertura.xml", "out/missing.xml" }
                };
                await s.Db.Driver.WorkflowProfiles.CreateAsync(profile).ConfigureAwait(false);

                CheckRun run = await s.CheckRuns.RunAsync(s.Admin, new CheckRunRequest { VesselId = vessel.Id, Type = CheckRunTypeEnum.UnitTest, Label = "Unit tests" }).ConfigureAwait(false);

                AssertEqual(CheckRunStatusEnum.Passed, run.Status, run.Output);
                AssertEqual(0, run.ExitCode ?? -1);
                AssertTrue(PathCanonicalizer.AreEquivalent(git.Checkout, run.WorkingDirectory!), "ran in the Harbor's checkout");
                AssertContains("on-stderr", run.Output ?? String.Empty, "stderr was streamed back");
                AssertNotNull(run.TestSummary, "test summary parsed from the output");
                AssertEqual(3, run.TestSummary!.Passed ?? -1);
                AssertEqual(1, run.Artifacts.Count, "the artifact that exists on the Harbor");
                AssertEqual("out/coverage.cobertura.xml", run.Artifacts[0].Path);
                AssertTrue((run.Artifacts[0].SizeBytes ?? 0) > 0, "its size came from the Harbor");
                AssertNotNull(run.CoverageSummary, "coverage parsed from the artifact copied from the Harbor");
                AssertEqual(75d, run.CoverageSummary!.Lines?.Percentage ?? -1d);
                AssertTrue(File.Exists(Path.Combine(git.Checkout, "out", "coverage.cobertura.xml")), "the command ran on the Harbor's disk");

                CheckRun? stored = await s.Db.Driver.CheckRuns.ReadAsync(run.Id).ConfigureAwait(false);
                AssertNotNull(stored, "recorded");
                AssertEqual(CheckRunStatusEnum.Passed, stored!.Status);

                List<ArmadaEvent> audits = await s.Db.Driver.Events.EnumerateByTypeAsync(CommandAudit.EventType, 50).ConfigureAwait(false);
                CommandAuditRecord? audit = audits
                    .Where(e => !String.IsNullOrEmpty(e.Payload))
                    .Select(e => JsonHelper.Deserialize<CommandAuditRecord>(e.Payload!))
                    .FirstOrDefault(r => r.Source == "CheckRun");
                AssertNotNull(audit, "an audit.command event");
                AssertContains("hbr_vh_checks", audit!.Host, "the audit names the Harbor");
            }));

            cases.Add(CaseAsync("check_run_failure_on_harbor_is_recorded", "A failing command on the Harbor is recorded as Failed with its exit code", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_vh_fail").ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync("app", null, null).ConfigureAwait(false);

                CheckRun run = await s.CheckRuns.RunAsync(s.Admin, new CheckRunRequest { VesselId = vessel.Id, Type = CheckRunTypeEnum.Build, CommandOverride = "echo broke && exit 3" }).ConfigureAwait(false);

                AssertEqual(CheckRunStatusEnum.Failed, run.Status);
                AssertEqual(3, run.ExitCode ?? -1);
                AssertContains("broke", run.Output ?? String.Empty);
            }));

            cases.Add(CaseAsync("check_run_without_checkout_fails_typed", "A check run for a vessel with no checkout anywhere fails with VesselCheckoutUnavailableException and records nothing", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectAsync("hbr_vh_none", git.Settings.Clone(), null, null).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync("DocConverter", git.Origin, null).ConfigureAwait(false);

                VesselCheckoutUnavailableException ex = await CatchAsync<VesselCheckoutUnavailableException>(() => s.CheckRuns.RunAsync(s.Admin, new CheckRunRequest
                {
                    VesselId = vessel.Id,
                    Type = CheckRunTypeEnum.UnitTest,
                    CommandOverride = "echo should-not-run"
                })).ConfigureAwait(false);

                AssertEqual(VesselCheckoutErrorCodeEnum.NoHarborCheckout, ex.Code);
                EnumerationResult<CheckRun> runs = await s.Db.Driver.CheckRuns.EnumerateAsync(new CheckRunQuery { VesselId = vessel.Id }).ConfigureAwait(false);
                AssertEqual(0, runs.Objects.Count, "no check run was recorded");
            }));

            cases.Add(CaseAsync("workspace_browse_read_write_search_on_harbor", "Workspace lists, reads, saves (with conflict detection), creates, renames, searches, and deletes in the Harbor's checkout", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_vh_ws").ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync("app", null, null).ConfigureAwait(false);
                VesselHost host = await s.Resolver.ResolveAsync(vessel, null).ConfigureAwait(false);
                WorkspaceService workspace = new WorkspaceService();

                WorkspaceTreeResult tree = await workspace.GetTreeAsync(host).ConfigureAwait(false);
                AssertEqual(vessel.Id, tree.VesselId);
                AssertTrue(tree.Entries.Any(e => e.Name == "README.md" && !e.IsDirectory), "README.md listed");
                AssertFalse(tree.Entries.Any(e => e.Name == ".git"), ".git is never listed");

                WorkspaceFileResponse readme = await workspace.GetFileAsync(host, "README.md").ConfigureAwait(false);
                AssertEqual("# app\n", readme.Content);
                AssertTrue(readme.IsEditable);

                await workspace.CreateDirectoryAsync(host, new WorkspaceCreateDirectoryRequest { Path = "notes" }).ConfigureAwait(false);
                WorkspaceSaveResult created = await workspace.SaveFileAsync(host, new WorkspaceSaveRequest { Path = "notes/todo.md", Content = "first findable-line\n" }).ConfigureAwait(false);
                AssertTrue(created.Created, "created");
                AssertEqual("first findable-line\n", await File.ReadAllTextAsync(Path.Combine(git.Checkout, "notes", "todo.md")).ConfigureAwait(false), "written on the Harbor's disk");

                WorkspaceSaveResult edited = await workspace.SaveFileAsync(host, new WorkspaceSaveRequest { Path = "notes/todo.md", Content = "second\n", ExpectedHash = created.ContentHash }).ConfigureAwait(false);
                AssertFalse(edited.Created);
                await AssertThrowsAsync<WorkspaceConflictException>(() => workspace.SaveFileAsync(host, new WorkspaceSaveRequest { Path = "notes/todo.md", Content = "third\n", ExpectedHash = created.ContentHash })).ConfigureAwait(false);

                WorkspaceSearchResult search = await workspace.SearchAsync(host, "# app").ConfigureAwait(false);
                AssertTrue(search.Matches.Any(m => m.Path == "README.md" && m.LineNumber == 1), "search ran on the Harbor");

                WorkspaceOperationResult renamed = await workspace.RenameAsync(host, new WorkspaceRenameRequest { Path = "notes/todo.md", NewPath = "notes/done.md" }).ConfigureAwait(false);
                AssertEqual("notes/done.md", renamed.NewPath);
                await workspace.DeleteAsync(host, "notes").ConfigureAwait(false);
                AssertFalse(Directory.Exists(Path.Combine(git.Checkout, "notes")), "deleted on the Harbor's disk");
                await AssertThrowsAsync<FileNotFoundException>(() => workspace.GetFileAsync(host, "notes/done.md")).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("workspace_exec_changes_and_diff_on_harbor", "Workspace exec, changes, status, and diff run in the Harbor's checkout", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_vh_exec").ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync("app", null, null).ConfigureAwait(false);
                VesselHost host = await s.Resolver.ResolveAsync(vessel, null).ConfigureAwait(false);
                WorkspaceService workspace = new WorkspaceService();

                WorkspaceExecResult exec = await workspace.ExecAsync(host, new WorkspaceExecRequest { Command = "echo exec-marker && pwd", TimeoutSeconds = 30 }).ConfigureAwait(false);
                AssertEqual(0, exec.ExitCode, exec.Stderr);
                AssertContains("exec-marker", exec.Stdout);
                string pwd = exec.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Last();
                AssertTrue(PathCanonicalizer.AreEquivalent(git.Checkout, pwd), "ran in the Harbor's checkout (" + pwd + ")");
                AssertContains("hbr_vh_exec", exec.Host);

                await File.WriteAllTextAsync(Path.Combine(git.Checkout, "README.md"), "# app\nchanged on the harbor\n").ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(git.Checkout, "untracked.txt"), "new\n").ConfigureAwait(false);

                WorkspaceChangesResult changes = await workspace.GetChangesAsync(host).ConfigureAwait(false);
                AssertNull(changes.Error, changes.Error);
                AssertTrue(changes.IsDirty, "dirty");
                AssertTrue(changes.Changes.Any(c => c.Path == "untracked.txt" && c.Status == "??"), "untracked file reported");
                AssertTrue(changes.Changes.Any(c => c.Path == "README.md"), "modified file reported");

                WorkspaceStatusResult status = await workspace.GetStatusAsync(host).ConfigureAwait(false);
                AssertTrue(status.HasWorkingDirectory);
                AssertEqual("hbr_vh_exec", status.HarborId);
                AssertEqual("main", status.BranchName);

                WorkspaceDiffResult diff = await workspace.GetDiffAsync(host, "README.md").ConfigureAwait(false);
                AssertNull(diff.Error, diff.Error);
                AssertContains("+changed on the harbor", diff.Diff ?? String.Empty);
            }));

            cases.Add(CaseAsync("workspace_on_harbor_refuses_paths_outside_checkout", "Path traversal, .git, and symbolic links out of the checkout are refused on the Harbor", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_vh_confine").ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync("app", null, null).ConfigureAwait(false);
                VesselHost host = await s.Resolver.ResolveAsync(vessel, null).ConfigureAwait(false);
                WorkspaceService workspace = new WorkspaceService();

                string outside = Path.Combine(git.Root, "outside");
                Directory.CreateDirectory(outside);
                await File.WriteAllTextAsync(Path.Combine(outside, "secret.txt"), "secret\n").ConfigureAwait(false);

                await AssertThrowsAsync<UnauthorizedAccessException>(() => workspace.GetFileAsync(host, "../../outside/secret.txt"), "traversal read").ConfigureAwait(false);
                await AssertThrowsAsync<UnauthorizedAccessException>(() => workspace.SaveFileAsync(host, new WorkspaceSaveRequest { Path = "../escape.txt", Content = "x" }), "traversal write").ConfigureAwait(false);
                await AssertThrowsAsync<UnauthorizedAccessException>(() => workspace.GetTreeAsync(host, ".git"), ".git listing").ConfigureAwait(false);
                await AssertThrowsAsync<UnauthorizedAccessException>(() => host.Files.ReadTextAsync("../../outside/secret.txt", 1024), "traversal artifact read").ConfigureAwait(false);
                AssertFalse(File.Exists(Path.Combine(git.CodeRoot, "escape.txt")), "nothing was written outside the checkout");

                if (!OperatingSystem.IsWindows())
                {
                    File.CreateSymbolicLink(Path.Combine(git.Checkout, "link"), outside);
                    await AssertThrowsAsync<UnauthorizedAccessException>(() => workspace.GetFileAsync(host, "link/secret.txt"), "symbolic link out of the checkout").ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("harbor_refuses_roots_that_are_not_the_vessel_checkout", "A file request whose root is not the Harbor's checkout of the vessel (or inside its docks folder) is refused with a typed code", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_vh_roots").ConfigureAwait(false);

                HarborFileResult foreignRoot = await s.SendFileAsync("hbr_vh_roots", HarborFileOperationEnum.List, git.Root, "app", null).ConfigureAwait(false);
                AssertFalse(foreignRoot.Success, "a folder that is not the checkout");
                AssertEqual(HarborFileErrorCodeEnum.Refused, foreignRoot.ErrorCode);

                HarborFileResult otherVessel = await s.SendFileAsync("hbr_vh_roots", HarborFileOperationEnum.List, git.Checkout, "someone-else", null).ConfigureAwait(false);
                AssertEqual(HarborFileErrorCodeEnum.Refused, otherVessel.ErrorCode, "the checkout of another vessel");

                HarborFileResult mapped = await s.SendFileAsync("hbr_vh_roots", HarborFileOperationEnum.List, git.Checkout, "app", null).ConfigureAwait(false);
                AssertTrue(mapped.Success, mapped.Message);
                AssertNotNull(mapped.Tree);

                Directory.CreateDirectory(Path.Combine(git.HarborDocks, "app", "msn_x"));
                HarborFileResult dock = await s.SendFileAsync("hbr_vh_roots", HarborFileOperationEnum.Stat, Path.Combine(git.HarborDocks, "app", "msn_x"), null, null).ConfigureAwait(false);
                AssertTrue(dock.Success, "a dock is inside the docks folder");

                HarborFileResult traversal = await s.SendFileAsync("hbr_vh_roots", HarborFileOperationEnum.Read, git.Checkout, "app", "../../remote/app.git/config").ConfigureAwait(false);
                AssertEqual(HarborFileErrorCodeEnum.Refused, traversal.ErrorCode, "traversal out of the root");
            }));

            cases.Add(CaseAsync("readiness_reports_harbor_checkout", "Readiness reports a vessel served by a Harbor as ready, naming the Harbor and its path, with git state probed there", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_vh_ready").ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync("app", null, null).ConfigureAwait(false);

                VesselReadinessResult result = await s.Readiness.EvaluateAsync(s.Admin, vessel, null, CheckRunTypeEnum.Build, null, false).ConfigureAwait(false);

                AssertTrue(result.IsReady, String.Join("; ", result.Issues.Select(i => i.Code + ": " + i.Message)));
                AssertTrue(result.HasWorkingDirectory);
                AssertTrue(result.HasRepositoryContext);
                AssertEqual("hbr_vh_ready", result.HarborId);
                AssertContains("hbr_vh_ready", result.HarborName ?? String.Empty);
                AssertTrue(PathCanonicalizer.AreEquivalent(git.Checkout, result.CheckoutPath!), "the Harbor's path");
                AssertTrue(result.Issues.Any(i => i.Code == "working_directory_on_harbor" && i.Severity == ReadinessSeverityEnum.Info), "an Info issue names the Harbor");
                AssertEqual("main", result.CurrentBranch, "git state came from the Harbor");
                AssertNull(result.CheckoutErrorCode);
            }));

            cases.Add(CaseAsync("readiness_without_checkout_gives_reason", "Readiness for a vessel no Harbor can serve gives the typed reason and blocks a check", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectAsync("hbr_vh_unready", git.Settings.Clone(), null, null).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync("DocConverter", git.Origin, null).ConfigureAwait(false);

                VesselReadinessResult result = await s.Readiness.EvaluateAsync(s.Admin, vessel, null, CheckRunTypeEnum.Build, null, false).ConfigureAwait(false);

                AssertFalse(result.IsReady);
                AssertFalse(result.HasWorkingDirectory);
                AssertEqual(VesselCheckoutErrorCodeEnum.NoHarborCheckout, result.CheckoutErrorCode);
                VesselReadinessIssue? issue = result.Issues.FirstOrDefault(i => i.Code == "working_directory_missing");
                AssertNotNull(issue, "the working directory issue");
                AssertEqual(ReadinessSeverityEnum.Error, issue!.Severity);
            }));

            cases.Add(CaseAsync("health_evaluates_harbor_checkout", "Vessel health evaluates the checkout on a Harbor when the Admiral has no repository for the vessel", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_vh_health").ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync("app", null, null).ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(git.Checkout, "dirty.txt"), "uncommitted\n").ConfigureAwait(false);

                VesselHealthEvaluator evaluator = new VesselHealthEvaluator(
                    s.Db.Driver,
                    new GitService(s.Logging),
                    s.Settings,
                    new List<IVesselHealthCriterion> { new WorkingTreeCriterion(), new CommitRecencyCriterion() },
                    s.Logging);
                evaluator.Hosts = s.Resolver;
                s.Settings.RepositoryHealth.FetchBeforeEvaluate = false;

                VesselHealth health = await evaluator.EvaluateAsync(vessel, false).ConfigureAwait(false);

                AssertNull(health.ErrorCode, "the repository was available");
                AssertTrue(PathCanonicalizer.AreEquivalent(git.Checkout, health.EvaluatedPath!), "evaluated the Harbor's checkout");
                List<VesselHealthFinding> findings = await s.Db.Driver.VesselHealthFindings.ReadByVesselAsync(vessel.TenantId ?? Constants.DefaultTenantId, vessel.Id).ConfigureAwait(false);
                VesselHealthFinding? workingTree = findings.FirstOrDefault(f => f.Criterion == VesselHealthCriterionEnum.WorkingTree);
                AssertNotNull(workingTree, "the working tree criterion ran");
                AssertNotEqual(VesselHealthStatusEnum.Unknown, workingTree!.Status, "it read the Harbor's working tree");
            }));

            cases.Add(CaseAsync("long_command_does_not_block_the_link", "A long command on the Harbor (a check run) does not hold up file requests while it runs", TestTags.Positive, async () =>
            {
                if (OperatingSystem.IsWindows()) return;
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_vh_long").ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync("app", null, null).ConfigureAwait(false);
                VesselHost host = await s.Resolver.ResolveAsync(vessel, null).ConfigureAwait(false);
                string gate = Path.Combine(git.Root, "gate");

                Task<HostCommandResult> running = host.Commands.RunAsync(host.BuildShellCommand("while [ ! -f '" + gate + "' ]; do sleep 0.05; done; echo released", 60000, false));
                VesselCheckoutFileInfo readme = await host.Files.StatAsync("README.md").ConfigureAwait(false);

                AssertTrue(readme.Exists, "the file request was answered");
                AssertFalse(running.IsCompleted, "while the command was still running");
                await File.WriteAllTextAsync(gate, "go").ConfigureAwait(false);
                HostCommandResult result = await running.ConfigureAwait(false);
                AssertEqual(0, result.ExitCode, result.StandardError);
                AssertContains("released", result.StandardOutput);
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Vessel Host (checkout on the Admiral or a Harbor)", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<T> CatchAsync<T>(Func<Task> action) where T : Exception
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (T ex)
            {
                return ex;
            }

            throw new AssertionException("Expected " + typeof(T).Name);
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

        #region Private-Types

        private sealed class Scenario : IDisposable
        {
            public TestDatabase Db { get; }

            public LoggingModule Logging { get; }

            public ArmadaSettings Settings { get; }

            public HarborConnectionManager Manager { get; }

            public VesselHostResolver Resolver { get; }

            public VesselReadinessService Readiness { get; }

            public CheckRunService CheckRuns { get; }

            public AuthContext Admin { get; }

            private readonly HarborDockGitFixture _Git;

            private Scenario(TestDatabase db, HarborDockGitFixture git)
            {
                Db = db;
                _Git = git;
                Logging = new LoggingModule();
                Logging.Settings.EnableConsole = false;
                Settings = new ArmadaSettings();
                Settings.LogDirectory = Path.Combine(git.AdmiralData, "logs");
                Settings.DocksDirectory = Path.Combine(git.AdmiralData, "docks");
                Settings.ReposDirectory = Path.Combine(git.AdmiralData, "repos");

                Manager = new HarborConnectionManager(new HarborService(db.Driver, Logging), Logging, null);
                Resolver = new VesselHostResolver(db.Driver, Settings, Logging, Manager);
                WorkflowProfileService profiles = new WorkflowProfileService(db.Driver, Logging);
                Readiness = new VesselReadinessService(db.Driver, profiles, Logging) { Hosts = Resolver };
                CheckRuns = new CheckRunService(db.Driver, profiles, Readiness, Logging) { Hosts = Resolver };
                Admin = AuthContext.Authenticated(Constants.DefaultTenantId, Constants.DefaultUserId, true, true, "UnitTest");
            }

            public static async Task<Scenario> CreateAsync(HarborDockGitFixture git)
            {
                TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                return new Scenario(db, git);
            }

            public HarborDockSettings MappedSettings(string vesselName)
            {
                HarborDockSettings settings = _Git.Settings.Clone();
                settings.Repositories.Add(new HarborRepositoryMapping(vesselName, _Git.Checkout));
                return settings;
            }

            public Task<InProcessHarbor> ConnectMappedAsync(string harborId)
            {
                return ConnectAsync(harborId, MappedSettings("app"), null, null);
            }

            public Task<InProcessHarbor> ConnectAsync(string harborId, HarborDockSettings settings, string? tenantId, string? userId)
            {
                HarborDockManager docks = new HarborDockManager(() => settings, Logging);
                return InProcessHarbor.ConnectAsync(Manager, harborId, tenantId, userId, new AgentRuntimeFactory(Logging),
                    new List<string> { "ClaudeCode", "git" }, Logging, null, null, docks);
            }

            public async Task<Vessel> CreateVesselAsync(string name, string? repoUrl, string? workingDirectory)
            {
                Vessel vessel = new Vessel(name, repoUrl ?? String.Empty);
                vessel.TenantId = Constants.DefaultTenantId;
                vessel.UserId = Constants.DefaultUserId;
                vessel.DefaultBranch = "main";
                vessel.WorkingDirectory = workingDirectory;
                return await Db.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
            }

            public async Task<HarborFileResult> SendFileAsync(string harborId, HarborFileOperationEnum operation, string root, string? vesselName, string? path)
            {
                HarborFileRequest request = new HarborFileRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Operation = operation,
                    Root = root,
                    Path = path ?? String.Empty,
                    VesselId = "vsl_not_registered",
                    VesselName = vesselName
                };
                HarborFileResult? result = await Manager.SendFileAsync(harborId, request, 30000).ConfigureAwait(false);
                if (result == null) throw new AssertionException("Harbor " + harborId + " did not answer the file request");
                return result;
            }

            public void Dispose()
            {
                Db.Dispose();
            }
        }

        #endregion
    }
}
