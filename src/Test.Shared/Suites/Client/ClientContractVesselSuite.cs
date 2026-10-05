namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Armada.Client contract against a live server with a scripted stub captain (<see cref="StubCaptainRuntime"/>):
    /// vessels (get, update, git status, branches, landing preview, push and merge, build context), fleet actions and
    /// their runs, vessel health with overrides, vessel import through fleet recommendations, and the workspace.
    /// </summary>
    public sealed class ClientContractVesselSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Client.Contract.Vessels";
        private const int LiveTimeoutMs = 60000;
        private readonly StubCaptainBehavior _Behavior = new StubCaptainBehavior();

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("vessels_and_branches", "Vessel get, list, update, git status, branches, landing preview, push and merge a branch, build context", async (c, fx) =>
            {
                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "vessel");
                string id = setup.Vessel.Id;
                AssertEqual(setup.Vessel.Name, (await c.GetVesselAsync(id))?.Name, "get vessel");
                AssertTrue((await c.ListVesselsAsync(new ArmadaPageQuery(1, 100)))!.Objects.Any(v => v.Id == id), "list vessels");
                Vessel edit = (await c.GetVesselAsync(id))!;
                edit.ProjectContext = "Contract context";
                AssertEqual("Contract context", (await c.UpdateVesselAsync(id, edit))?.ProjectContext, "update vessel");
                AssertEqual(id, (await c.GetVesselGitStatusAsync(id))?.VesselId, "git status");

                string branch = "contract/branch-" + ClientContract.Suffix();
                string work = setup.Vessel.WorkingDirectory!;
                LiveServerSetup.Git(work, out _, "checkout", "-b", branch);
                File.WriteAllText(Path.Combine(work, "branch.txt"), "branch\n");
                LiveServerSetup.Git(work, out _, "add", "-A");
                LiveServerSetup.Git(work, out _, "-c", "user.name=Contract", "-c", "user.email=contract@armada.test", "commit", "-m", "Branch change");
                LiveServerSetup.Git(work, out _, "checkout", "main");
                AssertTrue((await c.GetVesselBranchesAsync(id))!.Branches.Any(b => b.Name == branch), "branches list the new branch");
                AssertEqual(branch, (await c.GetVesselLandingPreviewAsync(id, branch))?.SourceBranch, "landing preview");
                AssertTrue((await c.PushVesselBranchAsync(id, branch))?.Pushed == true, "push");
                // The target must not be the branch checked out in the working directory (the server merges in a
                // temporary worktree of it), so merge into a separate release branch.
                string target = "contract/target-" + ClientContract.Suffix();
                LiveServerSetup.Git(work, out _, "branch", target, "main");
                AssertTrue((await c.MergeVesselBranchAsync(id, branch, target, true))?.Merged == true, "merge");
                AssertEqual(0, LiveServerSetup.Git(null, out string catFile, "--git-dir", setup.BarePath, "cat-file", "-e", target + ":branch.txt"), "merged and pushed to the origin (branch.txt exists on the target): " + catFile);

                Captain captain = await LiveServerSetup.CreateCaptainAsync(c, "contract-context");
                _Behavior.OnPrompt = prompt => "## Project context\nA test repository with one README.";
                Vessel? built = await c.BuildVesselContextAsync(id, new BuildVesselContextRequest { CaptainId = captain.Id });
                AssertEqual(id, built?.Id, "build context");
            }));

            cases.Add(Case("fleet_actions", "Fleet action CRUD; run on a vessel, run detail, targets, target; ad hoc run; cancel; delete", async (c, fx) =>
            {
                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "actions");
                FleetActionUpsertRequest action = new FleetActionUpsertRequest();
                action.Name = "Contract status " + ClientContract.Suffix();
                action.Kind = FleetActionKindEnum.Command;
                action.CommandText = "git status --short";
                action.TimeoutSeconds = 60;
                FleetAction created = (await c.CreateFleetActionAsync(action))!;
                AssertEqual(action.Name, (await c.GetFleetActionAsync(created.Id))?.Name, "get action");
                action.Description = "updated";
                AssertEqual("updated", (await c.UpdateFleetActionAsync(created.Id, action))?.Description, "update action");

                FleetActionRunStartResult run = (await c.RunFleetActionAsync(created.Id, new FleetActionRunRequest { VesselIds = new List<string> { setup.Vessel.Id } }))!;
                AssertTrue(await LiveServerSetup.WaitUntilAsync(async () => (await c.GetFleetActionRunAsync(run.RunId))?.Run.Status == FleetActionRunStatusEnum.Completed, LiveTimeoutMs), "run completed");
                FleetActionRunTargetSummary target = (await c.EnumerateFleetActionRunTargetsAsync(run.RunId))!.Objects.Single();
                AssertEqual(setup.Vessel.Id, target.VesselId, "target vessel");
                AssertEqual(0, (await c.GetFleetActionRunTargetAsync(run.RunId, target.Id))?.ExitCode ?? -1, "target exit code");
                await ClientContract.ExpectErrorAsync(() => c.CancelFleetActionRunAsync(run.RunId), "a finished run cannot be cancelled", 400, 409);

                FleetActionRunRequest adHoc = new FleetActionRunRequest();
                adHoc.VesselIds = new List<string> { setup.Vessel.Id };
                adHoc.Definition = new FleetActionUpsertRequest { Name = "Contract ad hoc", Kind = FleetActionKindEnum.Command, CommandText = "git log -1 --format=%s" };
                FleetActionRunStartResult adHocRun = (await c.RunAdHocFleetActionAsync(adHoc))!;
                AssertNull(adHocRun.ActionId, "ad hoc run has no saved action");
                AssertTrue(await LiveServerSetup.WaitUntilAsync(async () => (await c.GetFleetActionRunAsync(adHocRun.RunId))?.Run.Status == FleetActionRunStatusEnum.Completed, LiveTimeoutMs), "ad hoc run completed");
                await c.DeleteFleetActionAsync(created.Id);
            }));

            cases.Add(Case("vessel_health", "Vessel health evaluate, detail, set and delete an override", async (c, fx) =>
            {
                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "health");
                VesselHealthEvaluationStart? start = await c.EvaluateVesselHealthAsync(new VesselHealthEvaluateRequest { VesselIds = new List<string> { setup.Vessel.Id } });
                AssertTrue(await LiveServerSetup.WaitUntilAsync(async () => (await c.GetJobAsync(start!.JobId))?.Status == JobStatusEnum.Succeeded, LiveTimeoutMs), "evaluation job completed");
                VesselHealthDetail? detail = await c.GetVesselHealthAsync(setup.Vessel.Id);
                AssertEqual(setup.Vessel.Id, detail?.Health.VesselId, "health detail");
                VesselHealthDetail? overridden = await c.SetVesselHealthOverrideAsync(setup.Vessel.Id, VesselHealthCriterionEnum.WorkingTree, VesselHealthStatusEnum.Pass, "contract");
                AssertTrue(overridden!.Overrides.Any(o => o.Criterion == VesselHealthCriterionEnum.WorkingTree), "override set");
                VesselHealthDetail? cleared = await c.DeleteVesselHealthOverrideAsync(setup.Vessel.Id, VesselHealthCriterionEnum.WorkingTree);
                AssertFalse(cleared!.Overrides.Any(o => o.Criterion == VesselHealthCriterionEnum.WorkingTree), "override removed");
            }));

            cases.Add(Case("vessel_import", "Import: discover, import, batch detail, categorize with the captain, apply fleet recommendations", async (c, fx) =>
            {
                string root = TestTemp.NewDirectory("contract-import");
                foreach (string name in new[] { "contract-alpha", "contract-beta" })
                {
                    string repo = Path.Combine(root, name);
                    Directory.CreateDirectory(repo);
                    LiveServerSetup.Git(repo, out _, "init", "-b", "main");
                    File.WriteAllText(Path.Combine(repo, "README.md"), name + "\n");
                    LiveServerSetup.Git(repo, out _, "add", "-A");
                    LiveServerSetup.Git(repo, out _, "-c", "user.name=Contract", "-c", "user.email=contract@armada.test", "commit", "-m", "Initial");
                }

                VesselDiscoveryRequest discover = new VesselDiscoveryRequest();
                discover.Roots = new List<string> { root };
                VesselImportDiscoverResponse found = (await c.DiscoverVesselImportAsync(discover))!;
                AssertEqual(2, found.Candidates.Count, "two repositories found");
                VesselImportRequest import = new VesselImportRequest();
                import.BatchId = found.BatchId;
                import.Paths = found.Candidates.Select(i => i.Path).ToList();
                VesselImportResponse imported = (await c.ImportVesselsAsync(import))!;
                List<string> vesselIds = new List<string>();
                AssertTrue(await LiveServerSetup.WaitUntilAsync(async () =>
                {
                    VesselImportBatchDetail? detail = await c.GetVesselImportBatchAsync(imported.BatchId);
                    vesselIds = detail?.Items.Where(i => !String.IsNullOrEmpty(i.VesselId)).Select(i => i.VesselId!).ToList() ?? new List<string>();
                    return vesselIds.Count == 2;
                }, LiveTimeoutMs), "both vessels created");

                Captain captain = await LiveServerSetup.CreateCaptainAsync(c, "contract-categorizer");
                _Behavior.OnPrompt = prompt =>
                {
                    return "{\"fleets\":[{\"name\":\"Contract Imported\",\"description\":\"From the stub captain\",\"vesselIds\":[\"" + String.Join("\",\"", vesselIds) + "\"]}]}";
                };
                await c.CategorizeVesselImportAsync(imported.BatchId, new VesselImportCategorizationRequest { Enabled = true, CaptainId = captain.Id });
                List<VesselImportFleetRecommendation> recommendations = new List<VesselImportFleetRecommendation>();
                AssertTrue(await LiveServerSetup.WaitUntilAsync(async () =>
                {
                    recommendations = (await c.GetVesselImportBatchAsync(imported.BatchId))?.FleetRecommendations ?? new List<VesselImportFleetRecommendation>();
                    return recommendations.Count > 0;
                }, LiveTimeoutMs), "captain recommended a fleet: " + String.Join("; ", _Behavior.Errors));

                FleetRecommendationApplyRequest apply = new FleetRecommendationApplyRequest();
                string appliedFleetName = "Contract Imported " + ClientContract.Suffix();
                apply.Fleets.Add(new FleetRecommendationApplyFleet { Name = appliedFleetName, VesselIds = vesselIds });
                FleetRecommendationApplyResult? applied = await c.ApplyFleetRecommendationsAsync(imported.BatchId, apply);
                AssertNotNull(applied, "applied");
                Vessel? moved = await c.GetVesselAsync(vesselIds[0]);
                AssertEqual(appliedFleetName, (await c.GetFleetAsync(moved!.FleetId!))?.Fleet?.Name, "vessel moved to the new fleet");
            }));

            cases.Add(Case("workspace", "Workspace status, tree, file read and save, directory, rename, delete, search, changes, exec, diff", async (c, fx) =>
            {
                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "workspace");
                string id = setup.Vessel.Id;
                AssertTrue((await c.GetWorkspaceStatusAsync(id))?.HasWorkingDirectory == true, "status");
                AssertTrue((await c.GetWorkspaceTreeAsync(id))!.Entries.Any(e => e.Name == "README.md"), "tree");
                WorkspaceFileResponse readme = (await c.GetWorkspaceFileAsync(id, "README.md"))!;
                AssertEqual("hello\n", readme.Content, "file content");
                AssertTrue((await c.SaveWorkspaceFileAsync(id, new WorkspaceSaveRequest { Path = "README.md", Content = "hello contract\n", ExpectedHash = readme.ContentHash }))?.SizeBytes > 0, "save");
                await c.CreateWorkspaceDirectoryAsync(id, new WorkspaceCreateDirectoryRequest { Path = "contract-dir" });
                await c.SaveWorkspaceFileAsync(id, new WorkspaceSaveRequest { Path = "contract-dir/a.txt", Content = "needle\n" });
                AssertEqual("contract-dir/b.txt", (await c.RenameWorkspaceEntryAsync(id, new WorkspaceRenameRequest { Path = "contract-dir/a.txt", NewPath = "contract-dir/b.txt" }))?.NewPath, "rename");
                AssertTrue((await c.SearchWorkspaceAsync(id, "needle"))!.TotalMatches >= 1, "search");
                AssertTrue((await c.GetWorkspaceChangesAsync(id))!.IsDirty, "changes");
                AssertContains("hello contract", (await c.GetWorkspaceDiffAsync(id, "README.md"))?.Diff ?? "", "diff");
                AssertNotNull(await c.DeleteWorkspaceEntryAsync(id, "contract-dir"), "delete entry");
                WorkspaceExecResult? exec = await c.ExecWorkspaceCommandAsync(id, "git status --short", 30);
                AssertEqual(0, exec?.ExitCode ?? -1, "exec: " + exec?.Stderr);
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Armada.Client contract: vessels, fleet actions, health, import, workspace (live server, stub captain)", cases: cases);
        }

        #endregion

        #region Private-Methods

        private TestCaseDescriptor Case(string id, string name, Func<ArmadaClient, E2EServerFixture, Task> body)
        {
            return ClientContract.Case(Suite, this, id, name, body, fx => StubCaptainRuntime.Install(fx.Server, _Behavior));
        }

        #endregion
    }
}
