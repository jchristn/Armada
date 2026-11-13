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
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Server;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Harbor-hosted mission docks across the Admiral and real in-process Harbors linked over the Harbor protocol, with real
    /// git repositories: routing picks a Harbor that can serve the vessel before the dock is created (skipping one that
    /// cannot, waiting or using the Admiral host when none can); every landing mode runs on the Harbor against a real bare
    /// origin (LocalMerge and MergeAndPush land into the user's checkout there, PullRequest pushes and runs gh there, and a
    /// checkout with uncommitted changes is refused); reclaim removes the dock on the Harbor. The end-to-end case runs a
    /// whole mission with the Admiral's data folder and the Harbor's docks folder apart and checks that no Admiral-side
    /// path is used.
    /// </summary>
    public sealed class HarborDockLifecycleSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HarborDockLifecycle";
        private const string? UserId = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("routing_skips_harbor_that_cannot_serve_vessel", "Routing asks each eligible Harbor and places the dock on the one that can serve the vessel", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor without = await s.ConnectAsync("hbr_a_without", new HarborDockSettings { DocksDirectory = git.HarborDocks, ReposDirectory = git.HarborRepos }).ConfigureAwait(false);
                HarborDockSettings mapped = git.Settings.Clone();
                mapped.Repositories.Add(new HarborRepositoryMapping("app", git.Checkout));
                using InProcessHarbor with = await s.ConnectAsync("hbr_b_with", mapped).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(null, LandingModeEnum.LocalMerge).ConfigureAwait(false);
                Captain captain = await s.CreateCaptainAsync().ConfigureAwait(false);
                Mission mission = await s.CreateMissionAsync(vessel).ConfigureAwait(false);

                DockPlacement placement = await s.Handler.ResolveDockPlacementAsync(mission, captain, vessel).ConfigureAwait(false);

                AssertFalse(placement.Wait, placement.Reason);
                AssertEqual("hbr_b_with", placement.HarborId, "the Harbor with a checkout of the vessel");
            }));

            cases.Add(CaseAsync("branch_on_admiral_keeps_dock_on_admiral", "A later stage whose branch an earlier stage built on the Admiral host stays on the Admiral even when a capable Harbor is connected", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                HarborDockSettings mapped = git.Settings.Clone();
                mapped.Repositories.Add(new HarborRepositoryMapping("app", git.Checkout));
                using InProcessHarbor with = await s.ConnectAsync("hbr_capable", mapped).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(null, LandingModeEnum.LocalMerge).ConfigureAwait(false);
                Captain captain = await s.CreateCaptainAsync().ConfigureAwait(false);
                Mission mission = await s.CreateMissionAsync(vessel).ConfigureAwait(false);
                mission.BranchName = "armada/admiral-built";
                await s.Db.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                await s.Db.Driver.Docks.CreateAsync(new Dock(vessel.Id) { BranchName = "armada/admiral-built", WorktreePath = "/admiral/docks/earlier", Active = false }).ConfigureAwait(false);

                DockPlacement placement = await s.Handler.ResolveDockPlacementAsync(mission, captain, vessel).ConfigureAwait(false);
                AssertFalse(placement.Wait, placement.Reason);
                AssertNull(placement.HarborId, "the Admiral host, where the branch is");
                AssertContains("Admiral host's repository", placement.Reason);

                s.Settings.RequireHarborForLaunch = true;
                DockPlacement required = await s.Handler.ResolveDockPlacementAsync(mission, captain, vessel).ConfigureAwait(false);
                AssertEqual("hbr_capable", required.HarborId, "policy wins; the commit-continuity check then guards the stage");
            }));

            cases.Add(CaseAsync("routing_waits_when_required_and_none_can_serve", "With requireHarborForLaunch on and no Harbor able to serve the vessel the mission waits, and the reason says what to set", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                s.Settings.RequireHarborForLaunch = true;
                using InProcessHarbor harbor = await s.ConnectAsync("hbr_none", git.Settings.Clone()).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(null, LandingModeEnum.LocalMerge).ConfigureAwait(false);
                Captain captain = await s.CreateCaptainAsync().ConfigureAwait(false);
                Mission mission = await s.CreateMissionAsync(vessel).ConfigureAwait(false);

                DockPlacement placement = await s.Handler.ResolveDockPlacementAsync(mission, captain, vessel).ConfigureAwait(false);
                bool assigned = await s.Missions.TryAssignAsync(mission, vessel).ConfigureAwait(false);

                AssertTrue(placement.Wait, "the mission waits");
                AssertNull(placement.HarborId);
                AssertContains("hbr_none", placement.Reason, "the reason names the Harbor");
                AssertContains(vessel.Name, placement.Reason, "the reason names the vessel");
                AssertContains("Harbor > Settings > Repositories", placement.Reason, "the reason says what to set");
                AssertFalse(assigned, "nothing was assigned");
                Mission? stored = await s.Db.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Pending, stored!.Status);
                AssertEqual(0, (await s.Db.Driver.Docks.EnumerateByVesselAsync(vessel.Id).ConfigureAwait(false)).Count, "no dock was created anywhere");
            }));

            cases.Add(CaseAsync("routing_uses_admiral_when_none_can_serve_without_policy", "With requireHarborForLaunch off and no Harbor able to serve the vessel the dock goes on the Admiral host", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectAsync("hbr_cannot", git.Settings.Clone()).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(null, LandingModeEnum.LocalMerge).ConfigureAwait(false);

                DockPlacement placement = await s.Handler.ResolveDockPlacementAsync(await s.CreateMissionAsync(vessel).ConfigureAwait(false), await s.CreateCaptainAsync().ConfigureAwait(false), vessel).ConfigureAwait(false);

                AssertFalse(placement.Wait, "no policy requires a Harbor");
                AssertNull(placement.HarborId, "the Admiral host");
            }));

            cases.Add(CaseAsync("older_harbor_keeps_docks_on_admiral", "A Harbor that does not create docks is never asked; the dock goes on the Admiral host as before", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor older = await s.ConnectAsync("hbr_older", null).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(git.Origin, LandingModeEnum.LocalMerge).ConfigureAwait(false);

                DockPlacement placement = await s.Handler.ResolveDockPlacementAsync(await s.CreateMissionAsync(vessel).ConfigureAwait(false), await s.CreateCaptainAsync().ConfigureAwait(false), vessel).ConfigureAwait(false);

                AssertFalse(placement.Wait, placement.Reason);
                AssertNull(placement.HarborId, "the Admiral host");
                AssertFalse(s.Manager.HostsDocks("hbr_older"), "it did not advertise Harbor-side docks");
            }));

            cases.Add(CaseAsync("local_merge_lands_into_harbor_checkout", "LocalMerge on a Harbor dock merges into the user's checkout on the Harbor and does not push", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_land_local").ConfigureAwait(false);
                LandingRun run = await s.RunLandingAsync("hbr_land_local", LandingModeEnum.LocalMerge).ConfigureAwait(false);

                AssertEqual(MissionStatusEnum.Complete, run.Mission.Status, run.Mission.FailureReason);
                AssertTrue(await HarborDockGitFixture.TreeHasFileAsync(git.Checkout, "refs/heads/main", "landed.txt").ConfigureAwait(false), "merged into the checkout's main");
                AssertFalse(await HarborDockGitFixture.TreeHasFileAsync(git.Origin, "refs/heads/main", "landed.txt").ConfigureAwait(false), "LocalMerge does not push");
                AssertNotNull(run.Mission.DiffSnapshot, "the diff was captured on the Harbor");
                AssertEqual(run.WorkCommit, run.Mission.CommitHash, "the commit was captured on the Harbor");
            }));

            cases.Add(CaseAsync("merge_and_push_pushes_from_harbor_checkout", "MergeAndPush on a Harbor dock merges into the checkout and pushes it to the origin", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_land_push").ConfigureAwait(false);
                LandingRun run = await s.RunLandingAsync("hbr_land_push", LandingModeEnum.MergeAndPush).ConfigureAwait(false);

                AssertEqual(MissionStatusEnum.Complete, run.Mission.Status, run.Mission.FailureReason);
                AssertTrue(await HarborDockGitFixture.TreeHasFileAsync(git.Origin, "refs/heads/main", "landed.txt").ConfigureAwait(false), "pushed to the origin");
                AssertNull(await HarborDockGitFixture.ResolveAsync(git.Checkout, "refs/heads/" + run.Dock.BranchName).ConfigureAwait(false), "the mission branch was cleaned up in the user's repository");
            }));

            cases.Add(CaseAsync("pull_request_pushes_and_runs_gh_on_harbor", "PullRequest on a Harbor dock pushes the branch and opens the pull request from the Harbor", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_land_pr").ConfigureAwait(false);
                LandingRun run = await s.RunLandingAsync("hbr_land_pr", LandingModeEnum.PullRequest).ConfigureAwait(false);

                AssertEqual(MissionStatusEnum.PullRequestOpen, run.Mission.Status, run.Mission.FailureReason);
                AssertEqual(FakeGhHostCommandExecutor.PullRequestUrl, run.Mission.PrUrl);
                AssertEqual(run.WorkCommit, await HarborDockGitFixture.ResolveAsync(git.Origin, "refs/heads/" + run.Dock.BranchName).ConfigureAwait(false), "the branch was pushed from the Harbor dock");
                List<HostCommandRequest> gh = s.Gh.GhCalls();
                AssertTrue(gh.Count > 0, "gh ran on the Harbor");
                AssertEqual(run.Dock.WorktreePath, gh[0].WorkingDirectory, "gh ran in the Harbor dock");
                AssertFalse(await HarborDockGitFixture.TreeHasFileAsync(git.Checkout, "refs/heads/main", "landed.txt").ConfigureAwait(false), "nothing was merged locally");
            }));

            cases.Add(CaseAsync("merge_queue_processes_on_harbor", "A merge queue entry for a Harbor dock is merged and pushed from the Harbor's repository, not from an Admiral clone", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_land_queue").ConfigureAwait(false);
                LandingRun run = await s.RunLandingAsync("hbr_land_queue", LandingModeEnum.MergeQueue).ConfigureAwait(false);

                MergeEntry entry = (await s.MergeQueue.ListAsync().ConfigureAwait(false)).Single(e => e.MissionId == run.Mission.Id);
                MergeEntry? processed = await s.MergeQueue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);
                AssertNotNull(processed, "processed");
                AssertEqual(MergeStatusEnum.Landed, processed.Status, processed.TestOutput);
                AssertTrue(await HarborDockGitFixture.TreeHasFileAsync(git.Origin, "refs/heads/main", "landed.txt").ConfigureAwait(false), "pushed to the origin's main");
                AssertFalse(Directory.Exists(Path.Combine(git.AdmiralData, "repos")), "no Admiral-side clone was used");
            }));

            cases.Add(CaseAsync("merge_queue_waits_for_disconnected_harbor", "A merge queue entry whose branch is on a Harbor that is not connected stays queued with a reason naming the Harbor, and lands once it reconnects", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                LandingRun run;
                using (InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_queue_gone").ConfigureAwait(false))
                {
                    run = await s.RunLandingAsync("hbr_queue_gone", LandingModeEnum.MergeQueue).ConfigureAwait(false);
                }

                MergeEntry entry = (await s.MergeQueue.ListAsync().ConfigureAwait(false)).Single(e => e.MissionId == run.Mission.Id);
                MergeEntry? waiting = await s.MergeQueue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);
                AssertNotNull(waiting, "processed");
                AssertEqual(MergeStatusEnum.Queued, waiting!.Status, "left queued while the Harbor is away");
                AssertContains("Waiting for Harbor", waiting.TestOutput ?? "");
                AssertContains("reconnect", waiting.TestOutput ?? "");
                AssertContains("not connected", waiting.TestOutput ?? "");
                Mission? stillProduced = await s.Db.Driver.Missions.ReadAsync(run.Mission.Id).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.WorkProduced, stillProduced!.Status, "the mission is not failed while it waits");

                List<Mission> reconciled = new List<Mission>();
                s.MergeQueue.OnMissionReconciled = (m) => { lock (reconciled) reconciled.Add(m); return Task.CompletedTask; };
                using (InProcessHarbor back = await s.ConnectMappedAsync("hbr_queue_gone").ConfigureAwait(false))
                {
                    await s.MergeQueue.ProcessQueueAsync().ConfigureAwait(false);
                }

                MergeEntry? landed = await s.MergeQueue.GetAsync(entry.Id).ConfigureAwait(false);
                AssertEqual(MergeStatusEnum.Landed, landed!.Status, landed.TestOutput);
                AssertTrue(await HarborDockGitFixture.TreeHasFileAsync(git.Origin, "refs/heads/main", "landed.txt").ConfigureAwait(false), "pushed to the origin's main");
                Mission? complete = await s.Db.Driver.Missions.ReadAsync(run.Mission.Id).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Complete, complete!.Status);
                AssertEqual(1, reconciled.Count, "OnMissionReconciled raised once");
                AssertEqual(MissionStatusEnum.Complete, reconciled[0].Status);
                List<ArmadaEvent> events = await s.Db.Driver.Events.EnumerateRecentAsync(100).ConfigureAwait(false);
                AssertTrue(events.Any(e => e.EventType == "mission.completed" && e.MissionId == run.Mission.Id), "mission.completed recorded");
            }));

            cases.Add(CaseAsync("merge_queue_failure_reconciles_mission", "A merge queue entry the Admiral cannot process fails and moves its mission to LandingFailed with the reason", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(git.Origin, LandingModeEnum.MergeQueue).ConfigureAwait(false);
                vessel.LocalPath = Path.Combine(git.AdmiralData, "repos", "missing.git");
                await s.Db.Driver.Vessels.UpdateAsync(vessel).ConfigureAwait(false);
                Mission mission = await s.CreateMissionAsync(vessel).ConfigureAwait(false);
                mission.Status = MissionStatusEnum.WorkProduced;
                await s.Db.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                MergeEntry entry = new MergeEntry("armada/no-clone", "main") { VesselId = vessel.Id, MissionId = mission.Id };
                entry = await s.MergeQueue.EnqueueAsync(entry).ConfigureAwait(false);

                MergeEntry? processed = await s.MergeQueue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);

                AssertEqual(MergeStatusEnum.Failed, processed!.Status);
                Mission? stored = await s.Db.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.LandingFailed, stored!.Status, "the mission does not stay WorkProduced");
                AssertContains("no clone", stored.FailureReason ?? "");
            }));

            cases.Add(CaseAsync("merge_queue_manual_entry_resolves_harbor_by_branch", "A manual merge queue entry with no mission resolves the Harbor from the most recent dock on its branch", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_queue_manual").ConfigureAwait(false);
                LandingRun run = await s.RunLandingAsync("hbr_queue_manual", LandingModeEnum.None).ConfigureAwait(false);
                Mission cleared = (await s.Db.Driver.Missions.ReadAsync(run.Mission.Id).ConfigureAwait(false))!;
                cleared.DockId = null;
                await s.Db.Driver.Missions.UpdateAsync(cleared).ConfigureAwait(false);

                MergeEntry entry = new MergeEntry(run.Dock.BranchName!, "main") { VesselId = run.Dock.VesselId };
                entry = await s.MergeQueue.EnqueueAsync(entry).ConfigureAwait(false);
                MergeEntry? processed = await s.MergeQueue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);

                AssertEqual(MergeStatusEnum.Landed, processed!.Status, processed.TestOutput);
                AssertTrue(await HarborDockGitFixture.TreeHasFileAsync(git.Origin, "refs/heads/main", "landed.txt").ConfigureAwait(false), "pushed from the Harbor's repository");
                AssertFalse(Directory.Exists(Path.Combine(git.AdmiralData, "repos")), "no Admiral-side clone was used");
            }));

            cases.Add(CaseAsync("merge_queue_delete_failed_entry_keeps_branch", "Deleting a Failed merge queue entry keeps its branch in the user's repository and on the remote", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_queue_delete").ConfigureAwait(false);
                LandingRun run = await s.RunLandingAsync("hbr_queue_delete", LandingModeEnum.MergeQueue).ConfigureAwait(false);
                await HarborDockGitFixture.GitAsync(git.Checkout, "push", "origin", run.Dock.BranchName!).ConfigureAwait(false);
                await s.Docks.ReclaimAsync(run.Dock.Id, forceReason: "test releases the dock").ConfigureAwait(false);
                MergeEntry entry = (await s.MergeQueue.ListAsync().ConfigureAwait(false)).Single(e => e.MissionId == run.Mission.Id);
                entry.Status = MergeStatusEnum.Failed;
                await s.Db.Driver.MergeEntries.UpdateAsync(entry).ConfigureAwait(false);

                bool deleted = await s.MergeQueue.DeleteAsync(entry.Id).ConfigureAwait(false);

                AssertTrue(deleted, "the entry was deleted");
                AssertEqual(run.WorkCommit, await HarborDockGitFixture.ResolveAsync(git.Checkout, "refs/heads/" + run.Dock.BranchName).ConfigureAwait(false), "the local branch is kept");
                AssertEqual(run.WorkCommit, await HarborDockGitFixture.ResolveAsync(git.Origin, "refs/heads/" + run.Dock.BranchName).ConfigureAwait(false), "the remote branch is kept");
            }));

            cases.Add(CaseAsync("merge_queue_delete_cancelled_unmerged_keeps_branch", "Deleting a Cancelled entry whose branch is not merged keeps the branch; a Landed entry's branch is deleted", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_queue_cancel").ConfigureAwait(false);
                LandingRun run = await s.RunLandingAsync("hbr_queue_cancel", LandingModeEnum.MergeQueue).ConfigureAwait(false);
                await s.Docks.ReclaimAsync(run.Dock.Id, forceReason: "test releases the dock").ConfigureAwait(false);
                MergeEntry entry = (await s.MergeQueue.ListAsync().ConfigureAwait(false)).Single(e => e.MissionId == run.Mission.Id);
                await s.MergeQueue.CancelAsync(entry.Id).ConfigureAwait(false);

                AssertTrue(await s.MergeQueue.DeleteAsync(entry.Id).ConfigureAwait(false), "the cancelled entry was deleted");
                AssertEqual(run.WorkCommit, await HarborDockGitFixture.ResolveAsync(git.Checkout, "refs/heads/" + run.Dock.BranchName).ConfigureAwait(false), "an unmerged cancelled branch is kept");

                MergeEntry again = await s.MergeQueue.EnqueueAsync(new MergeEntry(run.Dock.BranchName!, "main") { VesselId = run.Dock.VesselId, MissionId = run.Mission.Id }).ConfigureAwait(false);
                MergeEntry? landed = await s.MergeQueue.ProcessSingleAsync(again.Id).ConfigureAwait(false);
                AssertEqual(MergeStatusEnum.Landed, landed!.Status, landed.TestOutput);
                AssertTrue(await s.MergeQueue.DeleteAsync(again.Id).ConfigureAwait(false), "the landed entry was deleted");
                AssertNull(await HarborDockGitFixture.ResolveAsync(git.Checkout, "refs/heads/" + run.Dock.BranchName).ConfigureAwait(false), "a landed branch is cleaned up");
            }));

            cases.Add(CaseAsync("merge_queue_prunes_leftover_integration_worktrees", "Processing an entry on a Harbor first removes integration worktrees and branches an earlier, interrupted run left there", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_queue_prune").ConfigureAwait(false);
                LandingRun run = await s.RunLandingAsync("hbr_queue_prune", LandingModeEnum.MergeQueue).ConfigureAwait(false);
                string leftoverBranch = "armada/merge-queue/mq_mrg_interrupted_20200101000000";
                string leftoverPath = Path.Combine(git.HarborDocks, "mq_mrg_interrupted_20200101000000");
                await HarborDockGitFixture.GitAsync(git.Checkout, "worktree", "add", "-b", leftoverBranch, leftoverPath, "main").ConfigureAwait(false);
                string orphanBranch = "armada/merge-queue/mq_mrg_orphan_20200101000000";
                await HarborDockGitFixture.GitAsync(git.Checkout, "branch", orphanBranch, "main").ConfigureAwait(false);

                MergeEntry entry = (await s.MergeQueue.ListAsync().ConfigureAwait(false)).Single(e => e.MissionId == run.Mission.Id);
                MergeEntry? processed = await s.MergeQueue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);

                AssertEqual(MergeStatusEnum.Landed, processed!.Status, processed.TestOutput);
                AssertFalse(Directory.Exists(leftoverPath), "the leftover integration worktree was removed");
                AssertNull(await HarborDockGitFixture.ResolveAsync(git.Checkout, "refs/heads/" + leftoverBranch).ConfigureAwait(false), "its branch was deleted");
                AssertNull(await HarborDockGitFixture.ResolveAsync(git.Checkout, "refs/heads/" + orphanBranch).ConfigureAwait(false), "an integration branch whose worktree is gone was deleted");
                string remaining = await HarborDockGitFixture.GitAsync(git.Checkout, "for-each-ref", "refs/heads/armada/merge-queue/").ConfigureAwait(false);
                AssertEqual(String.Empty, remaining, "no integration branches remain");
            }));

            cases.Add(CaseAsync("missing_diff_preserves_real_unlanded_branch", "Missing diff snapshot keeps real Harbor work recoverable without completing or deleting its branch", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_missing_diff").ConfigureAwait(false);
                LandingRun run = await s.RunLandingAsync("hbr_missing_diff", LandingModeEnum.LocalMerge, captureDiff: false).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.WorkProduced, run.Mission.Status, "missing evidence is not successful landing");
                AssertTrue(run.Mission.CommitHash == null || run.Mission.CommitHash == run.WorkCommit, "only the unlanded work commit may be recorded");
                AssertNull(run.Mission.CompletedUtc, "not completed");
                AssertContains("Work remains unlanded", run.Mission.FailureReason!);
                AssertEqual(run.WorkCommit, await HarborDockGitFixture.ResolveAsync(run.Dock.RepositoryPath!, "refs/heads/" + run.Dock.BranchName).ConfigureAwait(false), "the branch still holds the work");
                AssertFalse(await HarborDockGitFixture.TreeHasFileAsync(git.Origin, "refs/heads/main", "landed.txt").ConfigureAwait(false), "work is not on main");
                AssertFalse((await s.Db.Driver.Events.EnumerateByMissionAsync(run.Mission.Id).ConfigureAwait(false)).Any(e => e.EventType == "mission.completed"), "no false completion event");
            }));

            cases.Add(CaseAsync("merge_queue_refuses_already_landed", "Enqueueing a mission that already landed, or a branch already in the target, is refused with a reason", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_queue_landed").ConfigureAwait(false);
                LandingRun run = await s.RunLandingAsync("hbr_queue_landed", LandingModeEnum.MergeQueue).ConfigureAwait(false);
                MergeEntry entry = (await s.MergeQueue.ListAsync().ConfigureAwait(false)).Single(e => e.MissionId == run.Mission.Id);

                AssertNull(await s.MergeQueue.GetAlreadyLandedReasonAsync(new MergeEntry(run.Dock.BranchName!, "main") { VesselId = run.Dock.VesselId, MissionId = run.Mission.Id }).ConfigureAwait(false), "not landed yet");
                string? producedCommit = run.Mission.CommitHash;
                run.Mission.Status = MissionStatusEnum.Complete;
                run.Mission.CommitHash = null;
                await s.Db.Driver.Missions.UpdateAsync(run.Mission).ConfigureAwait(false);
                string? mismatch = await s.MergeQueue.GetAlreadyLandedReasonAsync(new MergeEntry(run.Dock.BranchName!, "main") { VesselId = run.Dock.VesselId, MissionId = run.Mission.Id }).ConfigureAwait(false);
                AssertContains("status mismatch", mismatch!);
                AssertFalse(mismatch!.Contains("already landed", StringComparison.Ordinal), "status alone does not establish landing");
                run.Mission.Status = MissionStatusEnum.WorkProduced;
                run.Mission.CommitHash = producedCommit;
                // A plain update never leaves Complete, so restore the fixture through the explicit status guard.
                AssertTrue(await s.Db.Driver.Missions.TryUpdateIfStatusAsync(run.Mission, new[] { MissionStatusEnum.Complete }).ConfigureAwait(false), "fixture restored");
                await s.MergeQueue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);

                string? missionReason = await s.MergeQueue.GetAlreadyLandedReasonAsync(new MergeEntry(run.Dock.BranchName!, "main") { VesselId = run.Dock.VesselId, MissionId = run.Mission.Id }).ConfigureAwait(false);
                AssertNotNull(missionReason, "a Complete mission is refused");
                AssertContains("already landed", missionReason!);

                // A legacy Complete row without a commit can still have real branch-containment evidence.
                Mission landed = (await s.Db.Driver.Missions.ReadAsync(run.Mission.Id).ConfigureAwait(false))!;
                landed.CommitHash = null;
                await s.Db.Driver.Missions.UpdateAsync(landed).ConfigureAwait(false);
                string? containedReason = await s.MergeQueue.GetAlreadyLandedReasonAsync(new MergeEntry(run.Dock.BranchName!, "main") { VesselId = run.Dock.VesselId, MissionId = run.Mission.Id }).ConfigureAwait(false);
                AssertNotNull(containedReason, "containment is evidence without a recorded commit");
                AssertContains("already contained", containedReason!);

                await HarborDockGitFixture.GitAsync(git.Checkout, "fetch", "origin").ConfigureAwait(false);
                string? branchReason = await s.MergeQueue.GetAlreadyLandedReasonAsync(new MergeEntry(run.Dock.BranchName!, "main") { VesselId = run.Dock.VesselId }).ConfigureAwait(false);
                AssertNotNull(branchReason, "a branch already in the target is refused");
                AssertContains("already contained in main", branchReason!);
            }));

            cases.Add(CaseAsync("merge_and_push_restores_users_branch", "MergeAndPush into a checkout that is on another branch lands on main, pushes main, and returns the checkout to the user's branch", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_land_restore").ConfigureAwait(false);
                await HarborDockGitFixture.GitAsync(git.Checkout, "checkout", "-b", "my-feature").ConfigureAwait(false);
                string featureTip = await HarborDockGitFixture.CommitFileAsync(git.Checkout, "feature.txt", "mine\n", "My work").ConfigureAwait(false);

                LandingRun run = await s.RunLandingAsync("hbr_land_restore", LandingModeEnum.MergeAndPush).ConfigureAwait(false);

                AssertEqual(MissionStatusEnum.Complete, run.Mission.Status, run.Mission.FailureReason);
                AssertEqual("my-feature", await HarborDockGitFixture.GitAsync(git.Checkout, "symbolic-ref", "--short", "HEAD").ConfigureAwait(false), "the checkout is back on the user's branch");
                AssertEqual(featureTip, await HarborDockGitFixture.ResolveAsync(git.Checkout, "refs/heads/my-feature").ConfigureAwait(false), "the user's branch was not changed");
                AssertTrue(await HarborDockGitFixture.TreeHasFileAsync(git.Origin, "refs/heads/main", "landed.txt").ConfigureAwait(false), "main was pushed");
                AssertFalse(await HarborDockGitFixture.TreeHasFileAsync(git.Origin, "refs/heads/main", "feature.txt").ConfigureAwait(false), "the user's branch was not pushed into main");
                AssertNull(await HarborDockGitFixture.ResolveAsync(git.Origin, "refs/heads/my-feature").ConfigureAwait(false), "the user's branch was not pushed");
            }));

            cases.Add(CaseAsync("merge_and_push_fast_forwards_behind_main", "MergeAndPush into a checkout whose main is behind origin fast-forwards it first, so the push succeeds", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_land_behind").ConfigureAwait(false);
                string other = Path.Combine(git.Root, "other");
                await HarborDockGitFixture.GitAsync(git.Root, "clone", git.Origin, other).ConfigureAwait(false);
                await HarborDockGitFixture.CommitFileAsync(other, "upstream.txt", "upstream\n", "Upstream work").ConfigureAwait(false);
                await HarborDockGitFixture.GitAsync(other, "push", "origin", "main").ConfigureAwait(false);

                LandingRun run = await s.RunLandingAsync("hbr_land_behind", LandingModeEnum.MergeAndPush).ConfigureAwait(false);

                AssertEqual(MissionStatusEnum.Complete, run.Mission.Status, run.Mission.FailureReason);
                AssertTrue(await HarborDockGitFixture.TreeHasFileAsync(git.Origin, "refs/heads/main", "landed.txt").ConfigureAwait(false), "the mission landed on the origin");
                AssertTrue(await HarborDockGitFixture.TreeHasFileAsync(git.Origin, "refs/heads/main", "upstream.txt").ConfigureAwait(false), "the upstream work is kept");
            }));

            cases.Add(CaseAsync("merge_and_push_preserves_unpushed_main", "MergeAndPush publishes isolated work without leaking or changing unpublished checkout commits", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_land_ahead").ConfigureAwait(false);
                string localTip = await HarborDockGitFixture.CommitFileAsync(git.Checkout, "local-only.txt", "local\n", "Unpushed work").ConfigureAwait(false);

                LandingRun run = await s.RunLandingAsync("hbr_land_ahead", LandingModeEnum.MergeAndPush).ConfigureAwait(false);

                AssertEqual(MissionStatusEnum.Complete, run.Mission.Status, run.Mission.FailureReason);
                AssertFalse(await HarborDockGitFixture.TreeHasFileAsync(git.Origin, "refs/heads/main", "local-only.txt").ConfigureAwait(false), "Unpublished user work is never published");
                AssertEqual(localTip, await HarborDockGitFixture.ResolveAsync(git.Checkout, "refs/heads/main").ConfigureAwait(false), "main was not merged into");
                AssertTrue(await HarborDockGitFixture.TreeHasFileAsync(git.Origin, "refs/heads/main", "landed.txt").ConfigureAwait(false), "Only isolated mission work is pushed");
            }));

            cases.Add(CaseAsync("local_merge_uses_merge_commit_template", "LocalMerge fast-forwards the verified commit without creating an untested merge commit", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                s.Settings.MessageTemplates.EnableCommitMetadata = true;
                s.Settings.MessageTemplates.MergeCommitTemplate = "Land {MissionId}";
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_land_template").ConfigureAwait(false);

                LandingRun run = await s.RunLandingAsync("hbr_land_template", LandingModeEnum.LocalMerge).ConfigureAwait(false);

                AssertEqual(MissionStatusEnum.Complete, run.Mission.Status, run.Mission.FailureReason);
                string parents = await HarborDockGitFixture.GitAsync(git.Checkout, "rev-list", "--parents", "-n", "1", "refs/heads/main").ConfigureAwait(false);
                AssertEqual(2, parents.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, "No extra merge commit after verification");
                AssertEqual(run.WorkCommit, await HarborDockGitFixture.ResolveAsync(git.Checkout, "refs/heads/main").ConfigureAwait(false), "The target is the verified SHA");
                string subject = await HarborDockGitFixture.GitAsync(git.Checkout, "log", "-1", "--format=%s", "refs/heads/main").ConfigureAwait(false);
                AssertEqual("Mission work", subject, "The verified commit is not rewritten to apply a template");
            }));

            cases.Add(CaseAsync("local_merge_refuses_dirty_checkout", "LocalMerge into a Harbor checkout with uncommitted changes is refused and the changes are kept", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_land_dirty").ConfigureAwait(false);
                string mainBefore = (await HarborDockGitFixture.ResolveAsync(git.Checkout, "refs/heads/main").ConfigureAwait(false))!;
                await File.WriteAllTextAsync(Path.Combine(git.Checkout, "README.md"), "# app, uncommitted\n").ConfigureAwait(false);

                LandingRun run = await s.RunLandingAsync("hbr_land_dirty", LandingModeEnum.LocalMerge).ConfigureAwait(false);

                AssertEqual(MissionStatusEnum.LandingFailed, run.Mission.Status);
                AssertEqual(mainBefore, await HarborDockGitFixture.ResolveAsync(git.Checkout, "refs/heads/main").ConfigureAwait(false), "main was not moved");
                AssertEqual("# app, uncommitted\n", await File.ReadAllTextAsync(Path.Combine(git.Checkout, "README.md")).ConfigureAwait(false), "the user's change is kept");
                AssertNotNull(await HarborDockGitFixture.ResolveAsync(git.Checkout, "refs/heads/" + run.Dock.BranchName).ConfigureAwait(false), "the mission branch is kept for a retry");
            }));

            cases.Add(CaseAsync("late_reclaim_preserves_live_job_worktree", "A late reclaim cannot delete a running replacement job's worktree; reclaim works after exit", TestTags.Reliability, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_late_reclaim").ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(git.Origin, LandingModeEnum.LocalMerge).ConfigureAwait(false);
                Captain captain = await s.CreateCaptainAsync().ConfigureAwait(false);
                Dock dock = await s.Docks.ProvisionOnHarborAsync(vessel, captain, "armada/live-dock", "msn_live", "hbr_late_reclaim").ConfigureAwait(false);
                HarborJobInfo job = HarborJobInfo.FromLaunch(new HarborLaunchRequest { JobId = "replacement", WorkingDirectory = Path.Combine(dock.WorktreePath!, "src") }, DateTime.UtcNow);
                harbor.Client.JobState.AddLiveJob(HarborJobInfo.FromLaunch(new HarborLaunchRequest { JobId = "bad-display-path", WorkingDirectory = "\0" }, DateTime.UtcNow));
                harbor.Client.JobState.AddLiveJob(job);
                HarborDockClient client = new HarborDockClient(s.Manager);
                HarborDockResult? refused = await client.ReclaimAsync(dock, vessel).ConfigureAwait(false);
                AssertNotNull(refused);
                AssertFalse(refused!.Success);
                AssertContains("replacement", refused.Message!);
                AssertTrue(Directory.Exists(dock.WorktreePath!), "The running job keeps its worktree even when stale Admiral cleanup requests it.");
                harbor.Client.JobState.RemoveLiveJob("replacement");
                HarborDockResult? removed = await client.ReclaimAsync(dock, vessel).ConfigureAwait(false);
                AssertTrue(removed!.Success, removed.Message);
                AssertFalse(Directory.Exists(dock.WorktreePath!));
            }));

            cases.Add(CaseAsync("stale_dock_record_cannot_remove_active_path", "Legacy duplicate dock records can be retired or purged without removing a newer active worktree", TestTags.Reliability, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_duplicate_path").ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(git.Origin, LandingModeEnum.LocalMerge).ConfigureAwait(false);
                Captain captain = await s.CreateCaptainAsync().ConfigureAwait(false);
                Dock live = await s.Docks.ProvisionOnHarborAsync(vessel, captain, "armada/duplicate", "msn_duplicate", "hbr_duplicate_path").ConfigureAwait(false);
                Dock stale = await s.Db.Driver.Docks.CreateAsync(new Dock(vessel.Id) { HarborId = live.HarborId, WorktreePath = live.WorktreePath, RepositoryPath = live.RepositoryPath, Active = true }).ConfigureAwait(false);
                await s.Docks.ReclaimAsync(stale.Id).ConfigureAwait(false);
                AssertTrue(Directory.Exists(live.WorktreePath!), "Late reclaim must preserve the replacement dock.");
                AssertFalse((await s.Db.Driver.Docks.ReadAsync(stale.Id).ConfigureAwait(false))!.Active);
                await s.Docks.PurgeAsync(stale.Id).ConfigureAwait(false);
                AssertTrue(Directory.Exists(live.WorktreePath!), "Deleting a retired legacy record must preserve the replacement dock.");
                await s.Docks.ReclaimAsync(live.Id).ConfigureAwait(false);
                AssertFalse(Directory.Exists(live.WorktreePath!), "The actual owner remains reclaimable.");
            }));

            cases.Add(CaseAsync("redispatch_uses_distinct_dock_generation_paths", "Separate attempts of one mission have distinct paths so old cleanup cannot target a replacement", TestTags.Reliability, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_generations").ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(git.Origin, LandingModeEnum.LocalMerge).ConfigureAwait(false);
                Captain captain = await s.CreateCaptainAsync().ConfigureAwait(false);
                Dock old = await s.Docks.ProvisionOnHarborAsync(vessel, captain, "armada/old-generation", "msn_same", "hbr_generations").ConfigureAwait(false);
                Dock current = await s.Docks.ProvisionOnHarborAsync(vessel, captain, "armada/current-generation", "msn_same", "hbr_generations").ConfigureAwait(false);
                AssertFalse(old.WorktreePath == current.WorktreePath, "Mission id alone must not identify a physical attempt.");
                await s.Docks.ReclaimAsync(old.Id).ConfigureAwait(false);
                AssertTrue(Directory.Exists(current.WorktreePath!));
                await s.Docks.ReclaimAsync(current.Id).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("reclaim_removes_dock_on_harbor", "Reclaiming a Harbor dock removes its worktree on the Harbor and releases the dock", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_reclaim").ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(git.Origin, LandingModeEnum.LocalMerge).ConfigureAwait(false);
                Captain captain = await s.CreateCaptainAsync().ConfigureAwait(false);
                Dock dock = await s.Docks.ProvisionOnHarborAsync(vessel, captain, "armada/reclaim", "msn_reclaim", "hbr_reclaim").ConfigureAwait(false);
                AssertTrue(Directory.Exists(dock.WorktreePath!), "created on the Harbor");

                await s.Docks.ReclaimAsync(dock.Id).ConfigureAwait(false);

                AssertFalse(Directory.Exists(dock.WorktreePath!), "removed on the Harbor");
                AssertFalse((await HarborDockGitFixture.WorktreesAsync(git.Checkout).ConfigureAwait(false)).Contains(PathCanonicalizer.Canonicalize(dock.WorktreePath!)), "pruned from the user's repository");
                Dock? stored = await s.Db.Driver.Docks.ReadAsync(dock.Id).ConfigureAwait(false);
                AssertFalse(stored!.Active, "the dock was released");
            }));

            cases.Add(CaseAsync("provision_failure_names_harbor_and_setting", "A Harbor that cannot create the dock fails provisioning with a reason naming the Harbor, the vessel, and the setting", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectAsync("hbr_fail", git.Settings.Clone()).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(null, LandingModeEnum.LocalMerge).ConfigureAwait(false);
                Captain captain = await s.CreateCaptainAsync().ConfigureAwait(false);

                DockProvisioningException failure = await CatchAsync(() => s.Docks.ProvisionOnHarborAsync(vessel, captain, "armada/x", "msn_x", "hbr_fail")).ConfigureAwait(false);

                AssertEqual(vessel.Id, failure.VesselId);
                AssertContains("hbr_fail", failure.Message);
                AssertContains(vessel.Name, failure.Message);
                AssertContains("Harbor > Settings > Repositories", failure.Message);
            }));

            cases.Add(CaseAsync("mission_lifecycle_runs_on_harbor_e2e", "A whole mission runs on the Harbor: dock, instructions, launch, landing, and reclaim, with no Admiral-side path used", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                HarborDockSettings discovered = git.Settings.Clone();
                discovered.RepositoryRoots.Add(git.CodeRoot);
                using InProcessHarbor harbor = await s.ConnectAsync("hbr_e2e_docks", discovered).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(git.Origin, LandingModeEnum.LocalMerge).ConfigureAwait(false);
                await s.CreateCaptainAsync().ConfigureAwait(false);
                Mission mission = await s.CreateMissionAsync(vessel).ConfigureAwait(false);

                bool assigned = await s.Missions.TryAssignAsync(mission, vessel).ConfigureAwait(false);
                AssertTrue(assigned, "the mission was assigned");

                Mission? done = null;
                bool finished = await WaitUntilAsync(async () =>
                {
                    done = await s.Db.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    return done != null && (done.Status == MissionStatusEnum.Complete || done.Status == MissionStatusEnum.Failed || done.Status == MissionStatusEnum.LandingFailed);
                }).ConfigureAwait(false);
                AssertTrue(finished, "the mission finished");
                AssertEqual(MissionStatusEnum.Complete, done!.Status, done.FailureReason);

                // The dock is reclaimed after landing marks the mission Complete.
                List<Dock> docks = new List<Dock>();
                bool released = await WaitUntilAsync(async () =>
                {
                    docks = await s.Db.Driver.Docks.EnumerateByVesselAsync(vessel.Id).ConfigureAwait(false);
                    return docks.Count == 1 && !docks[0].Active;
                }).ConfigureAwait(false);
                AssertEqual(1, docks.Count, "one dock");
                AssertTrue(released, "the dock was released");
                Dock dock = docks[0];
                AssertEqual("hbr_e2e_docks", dock.HarborId);
                AssertEqual(PathCanonicalizer.Canonicalize(git.Checkout), PathCanonicalizer.Canonicalize(dock.CheckoutPath!), "the discovered checkout");
                AssertTrue(HarborDockSettings.IsSameOrUnder(dock.WorktreePath!, git.HarborDocks), "the dock was in the Harbor's docks folder");
                AssertFalse(HarborDockSettings.IsSameOrUnder(dock.WorktreePath!, git.AdmiralData), "not in the Admiral's data folder");

                List<HarborLaunchRequest> launches = s.Runner.LaunchSnapshot();
                AssertEqual(1, launches.Count, "the captain ran on the Harbor");
                AssertEqual(dock.WorktreePath, launches[0].WorkingDirectory, "in the Harbor dock");
                AssertFalse(launches[0].ScratchWorkingDirectory, "never in a scratch directory");

                AssertTrue(await HarborDockGitFixture.TreeHasFileAsync(git.Checkout, "refs/heads/main", CommittingHarborJobRunner.WorkFile).ConfigureAwait(false), "landed into the user's checkout");
                AssertFalse(Directory.Exists(dock.WorktreePath!), "the dock was reclaimed on the Harbor");
                string exclude = await File.ReadAllTextAsync(Path.Combine(git.Checkout, ".git", "info", "exclude")).ConfigureAwait(false);
                AssertTrue(new List<string>(exclude.Split('\n')).Contains("CLAUDE.md"), "the instruction file was excluded in the repository the dock belonged to");

                AssertFalse(Directory.Exists(s.Settings.DocksDirectory), "no dock folder on the Admiral");
                AssertFalse(Directory.Exists(s.Settings.ReposDirectory), "no clone on the Admiral");
                Vessel? storedVessel = await s.Db.Driver.Vessels.ReadAsync(vessel.Id).ConfigureAwait(false);
                AssertNull(storedVessel!.LocalPath, "the vessel has no Admiral-side repository");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Harbor Dock Lifecycle", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<DockProvisioningException> CatchAsync(Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (DockProvisioningException ex)
            {
                return ex;
            }

            throw new AssertionException("Expected DockProvisioningException");
        }

        private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition)
        {
            MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(60));
            while (!deadline.Passed)
            {
                if (await condition().ConfigureAwait(false)) return true;
                await Task.Delay(50).ConfigureAwait(false);
            }

            return await condition().ConfigureAwait(false);
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

        private sealed class LandingRun
        {
            public Mission Mission { get; }

            public Dock Dock { get; }

            public string WorkCommit { get; }

            public LandingRun(Mission mission, Dock dock, string workCommit)
            {
                Mission = mission;
                Dock = dock;
                WorkCommit = workCommit;
            }
        }

        private sealed class Scenario : IDisposable
        {
            public TestDatabase Db { get; }

            public LoggingModule Logging { get; }

            public ArmadaSettings Settings { get; }

            public HarborConnectionManager Manager { get; }

            public DockService Docks { get; }

            public MissionService Missions { get; }

            public AgentLifecycleHandler Handler { get; }

            public MissionLandingHandler Landing { get; }

            public MergeQueueService MergeQueue { get; }

            public CommittingHarborJobRunner Runner { get; } = new CommittingHarborJobRunner();

            public FakeGhHostCommandExecutor Gh { get; } = new FakeGhHostCommandExecutor();

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
                GitService gitService = new GitService(Logging);
                DockHostResolver hosts = new DockHostResolver(gitService, Logging, Manager);
                Docks = new DockService(Logging, db.Driver, Settings, gitService) { Hosts = hosts };
                CaptainService captains = new CaptainService(Logging, db.Driver, Settings, gitService, Docks) { DockHosts = hosts };
                Missions = new MissionService(Logging, db.Driver, Settings, Docks, captains, git: gitService) { DockHosts = hosts };
                AdmiralService admiral = new AdmiralService(Logging, db.Driver, Settings, captains, Missions, new VoyageService(Logging, db.Driver), Docks);

                Handler = new AgentLifecycleHandler(
                    Logging, db.Driver, Settings, new AgentRuntimeFactory(Logging), admiral, new MessageTemplateService(Logging), null, null,
                    (eventType, message, entityType, entityId, captainId, missionId, vesselId, voyageId) => Task.CompletedTask);
                Handler.SetHarborConnections(Manager);

                MergeQueue = new MergeQueueService(Logging, db.Driver, Settings, gitService) { DockHosts = hosts };
                Landing = new MissionLandingHandler(Logging, db.Driver, Settings, gitService, MergeQueue,
                    new MessageTemplateService(Logging), null, Docks, null) { DockHosts = hosts };

                Missions.ResolveDockPlacementAsync = Handler.ResolveDockPlacementAsync;
                admiral.OnLaunchAgent = Handler.HandleLaunchAgentAsync;
                admiral.OnCaptureDiff = Landing.HandleCaptureDiffAsync;
                admiral.OnMissionComplete = Landing.HandleMissionCompleteAsync;
            }

            public static async Task<Scenario> CreateAsync(HarborDockGitFixture git)
            {
                TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                return new Scenario(db, git);
            }

            /// <summary>
            /// Link an in-process Harbor owned by the mission user. With dock settings it creates docks on its host (they
            /// are its live settings); without them it is a Harbor that predates Harbor-side docks.
            /// </summary>
            public Task<InProcessHarbor> ConnectAsync(string harborId, HarborDockSettings? settings)
            {
                HarborDockManager? docks = settings != null ? new HarborDockManager(() => settings, Logging) : null;
                return InProcessHarbor.ConnectAsync(Manager, harborId, null, UserId, new AgentRuntimeFactory(Logging),
                    new List<string> { "ClaudeCode", "git" }, Logging, Gh, Runner, docks);
            }

            public Task<InProcessHarbor> ConnectMappedAsync(string harborId)
            {
                HarborDockSettings settings = _Git.Settings.Clone();
                settings.Repositories.Add(new HarborRepositoryMapping("app", _Git.Checkout));
                return ConnectAsync(harborId, settings);
            }

            public async Task<Vessel> CreateVesselAsync(string? repoUrl, LandingModeEnum landingMode)
            {
                Vessel vessel = new Vessel("app", repoUrl ?? String.Empty);
                vessel.DefaultBranch = "main";
                vessel.LandingMode = landingMode;
                return await Db.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
            }

            public async Task<Captain> CreateCaptainAsync()
            {
                Captain captain = new Captain("dock-captain-" + Guid.NewGuid().ToString("N").Substring(0, 8), AgentRuntimeEnum.ClaudeCode);
                captain.State = CaptainStateEnum.Idle;
                return await Db.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);
            }

            public async Task<Mission> CreateMissionAsync(Vessel vessel)
            {
                Mission mission = new Mission("Add a file", "Add a file to the repository");
                mission.VesselId = vessel.Id;
                mission.UserId = UserId;
                mission.Status = MissionStatusEnum.Pending;
                return await Db.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);
            }

            /// <summary>
            /// Create a dock on the Harbor, commit one file in it (as a captain would), capture the diff, and land it.
            /// </summary>
            public async Task<LandingRun> RunLandingAsync(string harborId, LandingModeEnum landingMode, bool captureDiff = true)
            {
                Vessel vessel = await CreateVesselAsync(_Git.Origin, landingMode).ConfigureAwait(false);
                Captain captain = await CreateCaptainAsync().ConfigureAwait(false);
                Mission mission = await CreateMissionAsync(vessel).ConfigureAwait(false);
                mission.BranchName = "armada/land-" + landingMode.ToString().ToLowerInvariant();
                Dock dock = await Docks.ProvisionOnHarborAsync(vessel, captain, mission.BranchName, mission.Id, harborId).ConfigureAwait(false);
                mission.DockId = dock.Id;
                mission.CaptainId = captain.Id;
                mission.Status = MissionStatusEnum.WorkProduced;
                mission = await Db.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);

                string workCommit = await HarborDockGitFixture.CommitFileAsync(dock.WorktreePath!, "landed.txt", "landed\n", "Mission work").ConfigureAwait(false);
                if (captureDiff) await Landing.HandleCaptureDiffAsync(mission, dock).ConfigureAwait(false);
                await Landing.HandleMissionCompleteAsync(mission, dock).ConfigureAwait(false);

                Mission stored = (await Db.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                return new LandingRun(stored, dock, workCommit);
            }

            public void Dispose()
            {
                Db.Dispose();
            }
        }

        #endregion
    }
}
