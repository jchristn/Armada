namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Vessel cleanup inventory and garbage collection (VesselCleanupService) against real git repositories and a real
    /// database: classification of active, failed, LandingFailed, merge-queue, orphaned, dirty, unmerged, shared,
    /// unreachable, and ambiguous resources; read-only scans; revalidation of stale selections; path safeguards; and
    /// per-item outcomes including partial failures.
    /// </summary>
    public sealed class VesselCleanupSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.VesselCleanup";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("active_dock_in_use", "An active dock held by a captain on an InProgress mission, and its checked-out branch, are in use", TestTags.Positive, async () =>
            {
                using Harness h = await Harness.CreateAsync().ConfigureAwait(false);
                Captain captain = await CaptainAssignmentFixture.CreateAsync(h.Db.Driver, new Captain("c1") { State = CaptainStateEnum.Working }).ConfigureAwait(false);
                Dock dock = await h.AddDockAsync("armada/c1/active", active: true, captainId: captain.Id).ConfigureAwait(false);
                await h.AddMissionAsync(MissionStatusEnum.InProgress, "armada/c1/active", dock.Id).ConfigureAwait(false);
                captain.Assignment ??= new Mission("fixture assignment"); captain.Assignment.DockId = dock.Id;
                await CaptainAssignmentFixture.UpdateAsync(h.Db.Driver, captain).ConfigureAwait(false);

                VesselCleanupInventory inv = await h.Service.ScanAsync(h.Vessel).ConfigureAwait(false);

                List<VesselCleanupResource> invResources = await AllAsync(h).ConfigureAwait(false);
                VesselCleanupResource d = Find(invResources, "dock:" + dock.Id);
                AssertEqual(VesselCleanupDispositionEnum.InUse, d.Disposition, d.Reason);
                AssertContains("captain", d.Reason);
                AssertEqual(captain.Id, d.CaptainId);
                VesselCleanupResource b = FindBranch(invResources, "armada/c1/active");
                AssertEqual(VesselCleanupDispositionEnum.InUse, b.Disposition, b.Reason);
            }));

            cases.Add(CaseAsync("queued_mission_protects_inactive_dock", "A Pending (queued) mission referencing an inactive dock keeps it in use", TestTags.Positive, async () =>
            {
                using Harness h = await Harness.CreateAsync().ConfigureAwait(false);
                Dock dock = await h.AddDockAsync("armada/c1/queued").ConfigureAwait(false);
                Mission m = await h.AddMissionAsync(MissionStatusEnum.Pending, "armada/c1/queued", dock.Id).ConfigureAwait(false);
                VesselCleanupResource d = Find(await AllAsync(h).ConfigureAwait(false), "dock:" + dock.Id);
                AssertEqual(VesselCleanupDispositionEnum.InUse, d.Disposition, d.Reason);
                AssertEqual(m.Id, d.MissionId);
                AssertEqual(MissionStatusEnum.Pending, d.MissionStatus);
            }));

            cases.Add(CaseAsync("failed_clean_merged_dock_safe", "A Failed mission's inactive dock is safe only with evidence: clean worktree whose HEAD is merged; size is measured", TestTags.Positive, async () =>
            {
                using Harness h = await Harness.CreateAsync().ConfigureAwait(false);
                Dock dock = await h.AddDockAsync("armada/c1/failed").ConfigureAwait(false);
                await h.AddMissionAsync(MissionStatusEnum.Failed, "armada/c1/failed", dock.Id).ConfigureAwait(false);
                VesselCleanupResource d = Find(await AllAsync(h).ConfigureAwait(false), "dock:" + dock.Id);
                AssertEqual(VesselCleanupDispositionEnum.Safe, d.Disposition, d.Reason);
                AssertEqual(MissionStatusEnum.Failed, d.MissionStatus);
                AssertTrue(d.SizeBytes.HasValue && d.SizeBytes.Value > 0, "local worktree size is measured");
                AssertEqual(dock.WorktreePath, d.Path);
            }));

            cases.Add(CaseAsync("failed_dirty_dock_unknown", "Failed status alone is not enough: a Failed mission's dirty worktree is unknown", TestTags.Negative, async () =>
            {
                using Harness h = await Harness.CreateAsync().ConfigureAwait(false);
                Dock dock = await h.AddDockAsync("armada/c1/dirty").ConfigureAwait(false);
                await h.AddMissionAsync(MissionStatusEnum.Failed, "armada/c1/dirty", dock.Id).ConfigureAwait(false);
                File.WriteAllText(Path.Combine(dock.WorktreePath!, "scratch.txt"), "work in progress");
                VesselCleanupResource d = Find(await AllAsync(h).ConfigureAwait(false), "dock:" + dock.Id);
                AssertEqual(VesselCleanupDispositionEnum.Unknown, d.Disposition, d.Reason);
                AssertContains("uncommitted", d.Reason);
            }));

            cases.Add(CaseAsync("unmerged_detached_dock_unknown", "A worktree whose detached HEAD carries unmerged commits not kept on any branch is unknown", TestTags.Negative, async () =>
            {
                using Harness h = await Harness.CreateAsync().ConfigureAwait(false);
                Dock dock = await h.AddDockAsync("armada/c1/detached").ConfigureAwait(false);
                await h.AddMissionAsync(MissionStatusEnum.Cancelled, "armada/c1/detached", dock.Id).ConfigureAwait(false);
                await GitAsync(dock.WorktreePath!, "checkout", "-q", "--detach").ConfigureAwait(false);
                await CommitAsync(dock.WorktreePath!, "detached.txt").ConfigureAwait(false);
                VesselCleanupResource d = Find(await AllAsync(h).ConfigureAwait(false), "dock:" + dock.Id);
                AssertEqual(VesselCleanupDispositionEnum.Unknown, d.Disposition, d.Reason);
            }));

            cases.Add(CaseAsync("unmerged_commits_kept_on_branch", "A clean dock whose unmerged commits stay on its branch is safe, and the unmerged branch itself is unknown", TestTags.Positive, async () =>
            {
                using Harness h = await Harness.CreateAsync().ConfigureAwait(false);
                Dock dock = await h.AddDockAsync("armada/c1/kept").ConfigureAwait(false);
                await h.AddMissionAsync(MissionStatusEnum.Complete, "armada/c1/kept", dock.Id).ConfigureAwait(false);
                await CommitAsync(dock.WorktreePath!, "kept.txt").ConfigureAwait(false);
                VesselCleanupResult result = await h.Service.CleanupAsync(h.Vessel, new VesselCleanupRequest { Keys = new List<string> { "dock:" + dock.Id } }).ConfigureAwait(false);
                AssertEqual(VesselCleanupItemStatusEnum.Deleted, result.Results[0].Status, result.Results[0].Message);
                VesselCleanupResource b = FindBranch(await AllAsync(h).ConfigureAwait(false), "armada/c1/kept");
                AssertEqual(VesselCleanupDispositionEnum.Unknown, b.Disposition, b.Reason);
                AssertContains("unmerged", b.Reason);
            }));

            cases.Add(CaseAsync("landing_failed_in_use", "A LandingFailed mission keeps its dock and branch in use (landing can be retried)", TestTags.Positive, async () =>
            {
                using Harness h = await Harness.CreateAsync().ConfigureAwait(false);
                Dock dock = await h.AddDockAsync("armada/c1/landing").ConfigureAwait(false);
                await h.AddMissionAsync(MissionStatusEnum.LandingFailed, "armada/c1/landing", dock.Id).ConfigureAwait(false);
                await h.CreateBranchAsync("armada/c1/landing-copy").ConfigureAwait(false);
                await h.AddMissionAsync(MissionStatusEnum.LandingFailed, "armada/c1/landing-copy", null).ConfigureAwait(false);
                VesselCleanupInventory inv = await h.Service.ScanAsync(h.Vessel).ConfigureAwait(false);
                List<VesselCleanupResource> invResources = await AllAsync(h).ConfigureAwait(false);
                VesselCleanupResource d = Find(invResources, "dock:" + dock.Id);
                AssertEqual(VesselCleanupDispositionEnum.InUse, d.Disposition, d.Reason);
                AssertContains("LandingFailed", d.Reason);
                VesselCleanupResource b = FindBranch(invResources, "armada/c1/landing-copy");
                AssertEqual(VesselCleanupDispositionEnum.InUse, b.Disposition, b.Reason);
            }));

            cases.Add(CaseAsync("merge_queue_entries_protect", "Queued and Failed merge-queue entries keep their branches in use; a Landed entry does not", TestTags.Positive, async () =>
            {
                using Harness h = await Harness.CreateAsync().ConfigureAwait(false);
                await h.CreateBranchAsync("armada/mq/queued").ConfigureAwait(false);
                await h.CreateBranchAsync("armada/mq/failed").ConfigureAwait(false);
                await h.CreateBranchAsync("armada/mq/landed").ConfigureAwait(false);
                await h.Db.Driver.MergeEntries.CreateAsync(new MergeEntry("armada/mq/queued") { VesselId = h.Vessel.Id, Status = MergeStatusEnum.Queued }).ConfigureAwait(false);
                await h.Db.Driver.MergeEntries.CreateAsync(new MergeEntry("armada/mq/failed") { VesselId = h.Vessel.Id, Status = MergeStatusEnum.Failed }).ConfigureAwait(false);
                await h.Db.Driver.MergeEntries.CreateAsync(new MergeEntry("armada/mq/landed") { VesselId = h.Vessel.Id, Status = MergeStatusEnum.Landed }).ConfigureAwait(false);
                VesselCleanupInventory inv = await h.Service.ScanAsync(h.Vessel).ConfigureAwait(false);
                List<VesselCleanupResource> invResources = await AllAsync(h).ConfigureAwait(false);
                AssertEqual(VesselCleanupDispositionEnum.InUse, FindBranch(invResources, "armada/mq/queued").Disposition);
                VesselCleanupResource failed = FindBranch(invResources, "armada/mq/failed");
                AssertEqual(VesselCleanupDispositionEnum.InUse, failed.Disposition, failed.Reason);
                AssertContains("retried", failed.Reason);
                AssertEqual(VesselCleanupDispositionEnum.Safe, FindBranch(invResources, "armada/mq/landed").Disposition);
            }));

            cases.Add(CaseAsync("active_voyage_protects", "A terminal mission in a voyage that is still InProgress keeps its branch in use", TestTags.Positive, async () =>
            {
                using Harness h = await Harness.CreateAsync().ConfigureAwait(false);
                Voyage voyage = await h.Db.Driver.Voyages.CreateAsync(new Voyage("v") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                await h.CreateBranchAsync("armada/v/stage1").ConfigureAwait(false);
                Mission m = await h.AddMissionAsync(MissionStatusEnum.Complete, "armada/v/stage1", null, voyage.Id).ConfigureAwait(false);
                VesselCleanupResource b = FindBranch(await AllAsync(h).ConfigureAwait(false), "armada/v/stage1");
                AssertEqual(VesselCleanupDispositionEnum.InUse, b.Disposition, b.Reason);
                AssertEqual(voyage.Id, b.VoyageId);
                AssertEqual(m.Id, b.MissionId);
            }));

            cases.Add(CaseAsync("branches_merged_unmerged_protected", "Merged armada/ branches are safe, unmerged ones unknown; default and non-Armada branches are never listed", TestTags.Positive, async () =>
            {
                using Harness h = await Harness.CreateAsync().ConfigureAwait(false);
                await h.CreateBranchAsync("armada/x/merged").ConfigureAwait(false);
                await h.CreateBranchAsync("feature/user-branch").ConfigureAwait(false);
                await h.CreateUnmergedBranchAsync("armada/x/unmerged").ConfigureAwait(false);
                VesselCleanupInventory inv = await h.Service.ScanAsync(h.Vessel).ConfigureAwait(false);
                List<VesselCleanupResource> invResources = await AllAsync(h).ConfigureAwait(false);
                AssertEqual(VesselCleanupDispositionEnum.Safe, FindBranch(invResources, "armada/x/merged").Disposition);
                VesselCleanupResource unmerged = FindBranch(invResources, "armada/x/unmerged");
                AssertEqual(VesselCleanupDispositionEnum.Unknown, unmerged.Disposition, unmerged.Reason);
                AssertNull(unmerged.SizeBytes, "branch sizes are not invented");
                AssertFalse(invResources.Any(r => r.BranchName == "main" && r.ResourceType == VesselCleanupResourceTypeEnum.Branch), "default branch is not listed");
                AssertFalse(invResources.Any(r => r.BranchName == "feature/user-branch"), "non-Armada branch is not listed");
                AssertEqual(1, inv.SafeCount);
                AssertEqual(1, inv.UnknownCount);
            }));

            cases.Add(CaseAsync("orphaned_worktrees", "Worktrees with no dock record: clean inside docks is safe, stale registrations are safe, outside docks is unknown, dirty is unknown", TestTags.Positive, async () =>
            {
                using Harness h = await Harness.CreateAsync().ConfigureAwait(false);
                string clean = Path.Combine(h.VesselDocks, "orphan-clean");
                string dirty = Path.Combine(h.VesselDocks, "orphan-dirty");
                string stale = Path.Combine(h.VesselDocks, "orphan-stale");
                string outside = Path.Combine(h.Root, "elsewhere", "user-tree");
                await h.AddWorktreeAsync(clean, "armada/o/clean").ConfigureAwait(false);
                await h.AddWorktreeAsync(dirty, "armada/o/dirty").ConfigureAwait(false);
                await h.AddWorktreeAsync(stale, "armada/o/stale").ConfigureAwait(false);
                await h.AddWorktreeAsync(outside, "armada/o/outside").ConfigureAwait(false);
                File.WriteAllText(Path.Combine(dirty, "wip.txt"), "x");
                Directory.Delete(stale, true);

                VesselCleanupInventory inv = await h.Service.ScanAsync(h.Vessel).ConfigureAwait(false);

                List<VesselCleanupResource> invResources = await AllAsync(h).ConfigureAwait(false);
                VesselCleanupResource c = FindWorktree(invResources, clean);
                AssertEqual(VesselCleanupDispositionEnum.Safe, c.Disposition, c.Reason);
                AssertTrue(c.SizeBytes.HasValue, "local orphan size measured");
                AssertEqual(VesselCleanupDispositionEnum.Unknown, FindWorktree(invResources, dirty).Disposition);
                VesselCleanupResource s = FindWorktree(invResources, stale);
                AssertEqual(VesselCleanupDispositionEnum.Safe, s.Disposition, s.Reason);
                AssertNull(s.SizeBytes);
                VesselCleanupResource o = FindWorktree(invResources, outside);
                AssertEqual(VesselCleanupDispositionEnum.Unknown, o.Disposition, o.Reason);
                AssertContains("outside", o.Reason);

                VesselCleanupResult result = await h.Service.CleanupAsync(h.Vessel, new VesselCleanupRequest { Keys = new List<string> { s.Key, c.Key, o.Key } }).ConfigureAwait(false);
                AssertEqual(2, result.Deleted);
                AssertEqual(1, result.Skipped);
                AssertFalse(Directory.Exists(clean), "clean orphan removed");
                AssertTrue(Directory.Exists(outside), "worktree outside docks is never removed");
                AssertFalse((await AllAsync(h).ConfigureAwait(false)).Any(r => r.Key == s.Key), "stale registration pruned");
                AssertEqual(VesselCleanupDispositionEnum.Safe, FindBranch(await AllAsync(h).ConfigureAwait(false), "armada/o/clean").Disposition, "freed branch becomes eligible after refresh");
            }));

            cases.Add(CaseAsync("shared_path_ambiguous", "Two dock records sharing one worktree path are not safe", TestTags.Negative, async () =>
            {
                using Harness h = await Harness.CreateAsync().ConfigureAwait(false);
                Dock first = await h.AddDockAsync("armada/s/shared").ConfigureAwait(false);
                Dock second = await h.Db.Driver.Docks.CreateAsync(new Dock(h.Vessel.Id) { WorktreePath = first.WorktreePath, BranchName = "armada/s/shared", Active = false }).ConfigureAwait(false);
                VesselCleanupInventory inv = await h.Service.ScanAsync(h.Vessel).ConfigureAwait(false);
                List<VesselCleanupResource> invResources = await AllAsync(h).ConfigureAwait(false);
                AssertEqual(VesselCleanupDispositionEnum.Unknown, Find(invResources, "dock:" + first.Id).Disposition);
                VesselCleanupResource s = Find(invResources, "dock:" + second.Id);
                AssertEqual(VesselCleanupDispositionEnum.Unknown, s.Disposition);
                AssertContains("shared", s.Reason);
            }));

            cases.Add(CaseAsync("harbor_unreachable_unknown", "A dock on a disconnected Harbor and its repository are unknown and cannot be cleaned", TestTags.Negative, async () =>
            {
                using Harness h = await Harness.CreateAsync().ConfigureAwait(false);
                h.Service.HarborHostOverride = id => null;
                Dock dock = await h.Db.Driver.Docks.CreateAsync(new Dock(h.Vessel.Id)
                {
                    HarborId = "hbr_offline",
                    RepositoryPath = "/harbor/repos/project",
                    WorktreePath = "/harbor/docks/project/msn_1",
                    BranchName = "armada/h/one",
                    Active = false
                }).ConfigureAwait(false);
                VesselCleanupInventory inv = await h.Service.ScanAsync(h.Vessel).ConfigureAwait(false);
                List<VesselCleanupResource> invResources = await AllAsync(h).ConfigureAwait(false);
                VesselCleanupResource d = Find(invResources, "dock:" + dock.Id);
                AssertEqual(VesselCleanupDispositionEnum.Unknown, d.Disposition, d.Reason);
                AssertEqual("hbr_offline", d.HarborId);
                AssertNull(d.SizeBytes, "unreachable sizes are not invented");
                VesselCleanupResource repo = invResources.Single(r => r.ResourceType == VesselCleanupResourceTypeEnum.Repository);
                AssertEqual(VesselCleanupDispositionEnum.Unknown, repo.Disposition);
                AssertContains("not connected", repo.Reason);

                VesselCleanupResult result = await h.Service.CleanupAsync(h.Vessel, new VesselCleanupRequest { Keys = new List<string> { d.Key, repo.Key } }).ConfigureAwait(false);
                AssertEqual(2, result.Skipped);
                AssertNotNull(await h.Db.Driver.Docks.ReadAsync(dock.Id).ConfigureAwait(false), "dock record kept");
            }));

            cases.Add(CaseAsync("path_safeguards", "Docks pointing at the primary checkout or outside the docks directory are never cleaned, even when selected", TestTags.Negative, async () =>
            {
                using Harness h = await Harness.CreateAsync().ConfigureAwait(false);
                Dock primary = await h.Db.Driver.Docks.CreateAsync(new Dock(h.Vessel.Id) { WorktreePath = h.Vessel.WorkingDirectory, Active = false }).ConfigureAwait(false);
                string outside = Path.Combine(h.Root, "user-files");
                Directory.CreateDirectory(outside);
                File.WriteAllText(Path.Combine(outside, "keep.txt"), "user data");
                Dock stray = await h.Db.Driver.Docks.CreateAsync(new Dock(h.Vessel.Id) { WorktreePath = outside, Active = false }).ConfigureAwait(false);
                Dock bare = await h.Db.Driver.Docks.CreateAsync(new Dock(h.Vessel.Id) { WorktreePath = h.Vessel.LocalPath, Active = false }).ConfigureAwait(false);

                VesselCleanupInventory inv = await h.Service.ScanAsync(h.Vessel).ConfigureAwait(false);

                List<VesselCleanupResource> invResources = await AllAsync(h).ConfigureAwait(false);
                foreach (Dock d in new[] { primary, stray, bare })
                    AssertEqual(VesselCleanupDispositionEnum.Unknown, Find(invResources, "dock:" + d.Id).Disposition, d.WorktreePath);
                AssertContains("primary checkout", Find(invResources, "dock:" + primary.Id).Reason);

                VesselCleanupResult result = await h.Service.CleanupAsync(h.Vessel, new VesselCleanupRequest { Keys = new List<string> { "dock:" + primary.Id, "dock:" + stray.Id, "dock:" + bare.Id } }).ConfigureAwait(false);
                AssertEqual(3, result.Skipped);
                AssertEqual(0, result.Deleted);
                AssertTrue(File.Exists(Path.Combine(outside, "keep.txt")), "user file kept");
                AssertTrue(Directory.Exists(h.Vessel.WorkingDirectory!), "primary checkout kept");
                AssertTrue(Directory.Exists(h.Vessel.LocalPath!), "bare repository kept");
            }));

            cases.Add(CaseAsync("scan_is_read_only", "Scanning changes nothing: docks, worktrees, and branches all remain", TestTags.Positive, async () =>
            {
                using Harness h = await Harness.CreateAsync().ConfigureAwait(false);
                Dock dock = await h.AddDockAsync("armada/r/one").ConfigureAwait(false);
                await h.AddMissionAsync(MissionStatusEnum.Complete, "armada/r/one", dock.Id).ConfigureAwait(false);
                await h.CreateBranchAsync("armada/r/two").ConfigureAwait(false);
                VesselCleanupInventory first = await h.Service.ScanAsync(h.Vessel).ConfigureAwait(false);
                List<VesselCleanupResource> firstResources = await AllAsync(h).ConfigureAwait(false);
                VesselCleanupInventory second = await h.Service.ScanAsync(h.Vessel).ConfigureAwait(false);
                List<VesselCleanupResource> secondResources = await AllAsync(h).ConfigureAwait(false);
                AssertTrue(first.SafeCount >= 2, "safe resources found");
                AssertEqual(firstResources.Count, secondResources.Count);
                AssertTrue(Directory.Exists(dock.WorktreePath!), "worktree kept");
                AssertNotNull(await h.Db.Driver.Docks.ReadAsync(dock.Id).ConfigureAwait(false), "dock record kept");
                AssertTrue((await GitAsync(h.Vessel.LocalPath!, "branch", "--list", "armada/r/two").ConfigureAwait(false)).Contains("armada/r/two"), "branch kept");
            }));

            cases.Add(CaseAsync("stale_scan_revalidated", "A resource that was safe at scan time but was re-dispatched before cleanup is skipped and kept", TestTags.Reliability, async () =>
            {
                using Harness h = await Harness.CreateAsync().ConfigureAwait(false);
                Dock dock = await h.AddDockAsync("armada/race/one").ConfigureAwait(false);
                await h.AddMissionAsync(MissionStatusEnum.Failed, "armada/race/one", dock.Id).ConfigureAwait(false);
                await h.CreateBranchAsync("armada/race/two").ConfigureAwait(false);
                VesselCleanupInventory inv = await h.Service.ScanAsync(h.Vessel).ConfigureAwait(false);
                List<VesselCleanupResource> invResources = await AllAsync(h).ConfigureAwait(false);
                AssertEqual(VesselCleanupDispositionEnum.Safe, Find(invResources, "dock:" + dock.Id).Disposition);
                string branchKey = FindBranch(invResources, "armada/race/two").Key;
                AssertEqual(VesselCleanupDispositionEnum.Safe, FindBranch(invResources, "armada/race/two").Disposition);

                // Concurrent dispatch and landing after the scan.
                await h.AddMissionAsync(MissionStatusEnum.Assigned, "armada/race/one", dock.Id).ConfigureAwait(false);
                await h.Db.Driver.MergeEntries.CreateAsync(new MergeEntry("armada/race/two") { VesselId = h.Vessel.Id, Status = MergeStatusEnum.Testing }).ConfigureAwait(false);

                VesselCleanupResult result = await h.Service.CleanupAsync(h.Vessel, new VesselCleanupRequest { Keys = new List<string> { "dock:" + dock.Id, branchKey } }).ConfigureAwait(false);
                AssertEqual(2, result.Skipped);
                AssertTrue(result.Results.All(r => r.Message.StartsWith("No longer safe")), "skips explain revalidation");
                AssertTrue(Directory.Exists(dock.WorktreePath!), "worktree kept");
                AssertTrue((await GitAsync(h.Vessel.LocalPath!, "branch", "--list", "armada/race/two").ConfigureAwait(false)).Contains("armada/race/two"), "branch kept");
            }));

            cases.Add(CaseAsync("cleanup_dock_then_branch", "Selecting a safe dock and its branch removes both (dock first), and the refreshed inventory no longer lists them", TestTags.Positive, async () =>
            {
                using Harness h = await Harness.CreateAsync().ConfigureAwait(false);
                Dock dock = await h.AddDockAsync("armada/gc/one").ConfigureAwait(false);
                await h.AddMissionAsync(MissionStatusEnum.Complete, "armada/gc/one", dock.Id).ConfigureAwait(false);
                VesselCleanupInventory inv = await h.Service.ScanAsync(h.Vessel).ConfigureAwait(false);
                List<VesselCleanupResource> invResources = await AllAsync(h).ConfigureAwait(false);
                VesselCleanupResource b = FindBranch(invResources, "armada/gc/one");
                AssertEqual(VesselCleanupDispositionEnum.InUse, b.Disposition, "the branch is checked out by the dock before cleanup");

                VesselCleanupResult result = await h.Service.CleanupAsync(h.Vessel, new VesselCleanupRequest { Keys = new List<string> { b.Key, "dock:" + dock.Id, "dock:dck_missing" } }).ConfigureAwait(false);
                AssertEqual("dock:" + dock.Id, result.Results[0].Key, "docks run first");
                AssertEqual(VesselCleanupItemStatusEnum.Deleted, result.Results[0].Status, result.Results[0].Message);
                VesselCleanupItemResult branchResult = result.Results.Single(r => r.Key == b.Key);
                AssertEqual(VesselCleanupItemStatusEnum.Deleted, branchResult.Status, branchResult.Message);
                AssertEqual(VesselCleanupItemStatusEnum.Skipped, result.Results.Single(r => r.Key == "dock:dck_missing").Status);
                AssertFalse(Directory.Exists(dock.WorktreePath!), "worktree removed");
                AssertNull(await h.Db.Driver.Docks.ReadAsync(dock.Id).ConfigureAwait(false), "dock record removed");
                AssertFalse((await AllAsync(h).ConfigureAwait(false)).Any(r => r.Key == b.Key || r.Key == "dock:" + dock.Id), "refreshed inventory");
                AssertTrue((await GitAsync(h.Vessel.LocalPath!, "branch", "--list", "main").ConfigureAwait(false)).Contains("main"), "default branch kept");
            }));

            cases.Add(CaseAsync("partial_failure_reported", "One failing removal is reported per item and does not stop the others", TestTags.Reliability, async () =>
            {
                using Harness h = await Harness.CreateAsync(failingPurge: true).ConfigureAwait(false);
                Dock dock = await h.AddDockAsync("armada/pf/one").ConfigureAwait(false);
                await h.AddMissionAsync(MissionStatusEnum.Complete, "armada/pf/one", dock.Id).ConfigureAwait(false);
                await h.CreateBranchAsync("armada/pf/two").ConfigureAwait(false);
                VesselCleanupInventory inv = await h.Service.ScanAsync(h.Vessel).ConfigureAwait(false);
                List<VesselCleanupResource> invResources = await AllAsync(h).ConfigureAwait(false);
                string branchKey = FindBranch(invResources, "armada/pf/two").Key;

                VesselCleanupResult result = await h.Service.CleanupAsync(h.Vessel, new VesselCleanupRequest { Keys = new List<string> { "dock:" + dock.Id, branchKey } }).ConfigureAwait(false);
                AssertEqual(1, result.Failed);
                AssertEqual(1, result.Deleted);
                VesselCleanupItemResult failed = result.Results.Single(r => r.Key == "dock:" + dock.Id);
                AssertEqual(VesselCleanupItemStatusEnum.Failed, failed.Status);
                AssertContains("simulated purge failure", failed.Message);
                AssertEqual(VesselCleanupDispositionEnum.Safe, Find(await AllAsync(h).ConfigureAwait(false), "dock:" + dock.Id).Disposition, "failed item stays actionable");
            }));

            cases.Add(CaseAsync("empty_selection_rejected", "Cleanup with no keys is rejected before touching anything", TestTags.Negative, async () =>
            {
                using Harness h = await Harness.CreateAsync().ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => h.Service.CleanupAsync(h.Vessel, new VesselCleanupRequest())).ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => h.Service.CleanupAsync(h.Vessel, new VesselCleanupRequest { Keys = new List<string> { " " } })).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("resources_page_with_exact_totals", "The inventory summary embeds no resource list; resources page without gaps or duplicates, a page past the end is empty with the same total, status filters by disposition, and oversized pages are capped", TestTags.Positive, async () =>
            {
                using Harness h = await Harness.CreateAsync().ConfigureAwait(false);
                for (int i = 0; i < 5; i++) await h.AddDockAsync("armada/p" + i + "/work").ConfigureAwait(false);
                Captain captain = await CaptainAssignmentFixture.CreateAsync(h.Db.Driver, new Captain("cp") { State = CaptainStateEnum.Working }).ConfigureAwait(false);
                await h.AddDockAsync("armada/p9/active", active: true, captainId: captain.Id).ConfigureAwait(false);

                VesselCleanupInventory inv = await h.Service.ScanAsync(h.Vessel).ConfigureAwait(false);
                AssertTrue(inv.ResourceCount >= 6, "six docks at least: " + inv.ResourceCount);
                AssertEqual(inv.ResourceCount, inv.SafeCount + inv.InUseCount + inv.UnknownCount, "every resource is classified");
                string json = JsonSerializer.Serialize(inv);
                AssertFalse(json.Contains("\"Resources\"", StringComparison.Ordinal), "summary has no embedded resource list: " + json);

                List<VesselCleanupResource> all = await AllAsync(h).ConfigureAwait(false);
                AssertEqual(inv.ResourceCount, all.Count, "pages of two add up to ResourceCount");
                AssertEqual(all.Count, all.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count(), "no duplicates across pages");

                EnumerationResult<VesselCleanupResource> first = await h.Service.EnumerateResourcesAsync(h.Vessel, new EnumerationQuery { PageNumber = 1, PageSize = 4 }).ConfigureAwait(false);
                AssertEqual(4, first.Objects.Count, "first page is full");
                AssertEqual((long)inv.ResourceCount, first.TotalRecords, "exact total");
                AssertEqual((int)Math.Ceiling(inv.ResourceCount / 4.0), first.TotalPages, "pages of four");
                EnumerationResult<VesselCleanupResource> past = await h.Service.EnumerateResourcesAsync(h.Vessel, new EnumerationQuery { PageNumber = 99, PageSize = 4 }).ConfigureAwait(false);
                AssertEqual(0, past.Objects.Count, "past the end is empty");
                AssertEqual((long)inv.ResourceCount, past.TotalRecords, "and keeps the total");
                EnumerationResult<VesselCleanupResource> capped = await h.Service.EnumerateResourcesAsync(h.Vessel, new EnumerationQuery { PageSize = 1000000 }).ConfigureAwait(false);
                AssertEqual(1000, capped.PageSize, "page size is capped at 1000");

                EnumerationResult<VesselCleanupResource> inUse = await h.Service.EnumerateResourcesAsync(h.Vessel, new EnumerationQuery { PageSize = 100, Status = "inuse" }).ConfigureAwait(false);
                AssertEqual((long)inv.InUseCount, inUse.TotalRecords, "status filter counts InUse only");
                AssertTrue(inUse.Objects.All(r => r.Disposition == VesselCleanupDispositionEnum.InUse), "only InUse rows");

                bool rejected = false;
                try { await h.Service.EnumerateResourcesAsync(h.Vessel, new EnumerationQuery { Status = "Gone" }).ConfigureAwait(false); }
                catch (ArgumentException) { rejected = true; }
                AssertTrue(rejected, "an unknown status is rejected");
            }));

            cases.Add(CaseAsync("cleanup_rejects_too_many_keys", "A cleanup request with more than MaxRequestItems keys is rejected with CollectionLimitExceeded and removes nothing", TestTags.Negative, async () =>
            {
                using Harness h = await Harness.CreateAsync().ConfigureAwait(false);
                Dock dock = await h.AddDockAsync("armada/k/one").ConfigureAwait(false);
                List<string> keys = Enumerable.Range(0, CollectionLimits.MaxRequestItems).Select(i => "dock:dck_missing" + i).ToList();
                keys.Add("dock:" + dock.Id);
                CollectionLimitExceededException? error = null;
                try { await h.Service.CleanupAsync(h.Vessel, new VesselCleanupRequest { Keys = keys }).ConfigureAwait(false); }
                catch (CollectionLimitExceededException ex) { error = ex; }
                AssertNotNull(error, "over the limit is rejected");
                AssertEqual("Keys", error!.Field, "field named");
                AssertEqual(CollectionLimits.MaxRequestItems, error.Limit, "limit reported");
                AssertNotNull(await h.Db.Driver.Docks.ReadAsync(dock.Id).ConfigureAwait(false), "nothing removed");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Vessel cleanup inventory and garbage collection",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<List<VesselCleanupResource>> AllAsync(Harness h)
        {
            // Walk the paged resource enumeration with a small page so multi-page traversal is exercised everywhere.
            List<VesselCleanupResource> all = new List<VesselCleanupResource>();
            for (int page = 1; page < 500; page++)
            {
                EnumerationResult<VesselCleanupResource> result = await h.Service.EnumerateResourcesAsync(h.Vessel, new EnumerationQuery { PageNumber = page, PageSize = 2 }).ConfigureAwait(false);
                all.AddRange(result.Objects);
                if (page >= result.TotalPages) break;
            }

            return all;
        }

        private static VesselCleanupResource Find(List<VesselCleanupResource> inv, string key)
        {
            VesselCleanupResource? r = inv.FirstOrDefault(x => x.Key == key);
            if (r == null) throw new InvalidOperationException("resource " + key + " not listed; have: " + String.Join(", ", inv.Select(x => x.Key)));
            return r;
        }

        private static VesselCleanupResource FindBranch(List<VesselCleanupResource> inv, string branch)
        {
            VesselCleanupResource? r = inv.FirstOrDefault(x => x.ResourceType == VesselCleanupResourceTypeEnum.Branch && x.BranchName == branch);
            if (r == null) throw new InvalidOperationException("branch " + branch + " not listed; have: " + String.Join(", ", inv.Select(x => x.Key)));
            return r;
        }

        private static VesselCleanupResource FindWorktree(List<VesselCleanupResource> inv, string path)
        {
            VesselCleanupResource? r = inv.FirstOrDefault(x => x.ResourceType == VesselCleanupResourceTypeEnum.Worktree
                && Armada.Core.PathCanonicalizer.AreEquivalent(x.Path!, path));
            if (r == null) throw new InvalidOperationException("worktree " + path + " not listed; have: " + String.Join(", ", inv.Select(x => x.Key)));
            return r;
        }

        private static async Task<string> GitAsync(string directory, params string[] args)
        {
            ProcessStartInfo info = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string arg in args) info.ArgumentList.Add(arg);
            info.Environment["GIT_AUTHOR_NAME"] = "Test";
            info.Environment["GIT_AUTHOR_EMAIL"] = "test@example.com";
            info.Environment["GIT_COMMITTER_NAME"] = "Test";
            info.Environment["GIT_COMMITTER_EMAIL"] = "test@example.com";
            using Process process = Process.Start(info)!;
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);
            if (process.ExitCode != 0) throw new InvalidOperationException("git " + String.Join(" ", args) + " exited " + process.ExitCode + ": " + await stderr.ConfigureAwait(false));
            return output;
        }

        private static async Task CommitAsync(string worktree, string file)
        {
            File.WriteAllText(Path.Combine(worktree, file), Guid.NewGuid().ToString("N"));
            await GitAsync(worktree, "add", file).ConfigureAwait(false);
            await GitAsync(worktree, "commit", "-q", "-m", "add " + file).ConfigureAwait(false);
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

        private sealed class Harness : IDisposable
        {
            public TestDatabase Db { get; private set; } = null!;
            public Vessel Vessel { get; private set; } = null!;
            public VesselCleanupService Service { get; private set; } = null!;
            public string Root { get; private set; } = string.Empty;
            public string VesselDocks { get; private set; } = string.Empty;

            public static async Task<Harness> CreateAsync(bool failingPurge = false)
            {
                Harness h = new Harness();
                h.Root = Armada.Core.PathCanonicalizer.Canonicalize(Path.Combine(Path.GetTempPath(), "armada_cleanup_" + Guid.NewGuid().ToString("N")));
                Directory.CreateDirectory(h.Root);
                h.Db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);

                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                ArmadaSettings settings = new ArmadaSettings();
                settings.DocksDirectory = Path.Combine(h.Root, "docks");
                settings.ReposDirectory = Path.Combine(h.Root, "repos");
                settings.LogDirectory = Path.Combine(h.Root, "logs");

                // Primary checkout with one commit on main, and the Admiral's bare clone of it.
                string checkout = Path.Combine(h.Root, "checkout");
                Directory.CreateDirectory(checkout);
                await GitAsync(checkout, "init", "-q", "-b", "main").ConfigureAwait(false);
                await CommitAsync(checkout, "README.md").ConfigureAwait(false);
                string bare = Path.Combine(settings.ReposDirectory, "project.git");
                Directory.CreateDirectory(settings.ReposDirectory);
                await GitAsync(settings.ReposDirectory, "clone", "-q", "--bare", checkout, bare).ConfigureAwait(false);

                Vessel vessel = new Vessel("project", "https://example.invalid/project.git") { LocalPath = bare, WorkingDirectory = checkout, DefaultBranch = "main" };
                h.Vessel = await h.Db.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                h.VesselDocks = Path.Combine(settings.DocksDirectory, "project");
                Directory.CreateDirectory(h.VesselDocks);

                IGitService git = new GitService(logging);
                IDockService docks = new DockService(logging, h.Db.Driver, settings, git);
                if (failingPurge) docks = new FailingPurgeDockService(docks);
                h.Service = new VesselCleanupService(h.Db.Driver, settings, docks, new DockHostResolver(git, logging, null), logging);
                return h;
            }

            public async Task<Dock> AddDockAsync(string branch, bool active = false, string? captainId = null)
            {
                string path = Path.Combine(VesselDocks, branch.Replace('/', '_'));
                await AddWorktreeAsync(path, branch).ConfigureAwait(false);
                return await Db.Driver.Docks.CreateAsync(new Dock(Vessel.Id)
                {
                    WorktreePath = path,
                    BranchName = branch,
                    Active = active,
                    CaptainId = captainId
                }).ConfigureAwait(false);
            }

            public async Task AddWorktreeAsync(string path, string branch)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await GitAsync(Vessel.LocalPath!, "worktree", "add", "-q", "-b", branch, path, "main").ConfigureAwait(false);
            }

            public async Task CreateBranchAsync(string branch)
            {
                await GitAsync(Vessel.LocalPath!, "branch", branch, "main").ConfigureAwait(false);
            }

            public async Task CreateUnmergedBranchAsync(string branch)
            {
                string temp = Path.Combine(Root, "tmp-" + Guid.NewGuid().ToString("N"));
                await GitAsync(Vessel.LocalPath!, "worktree", "add", "-q", "-b", branch, temp, "main").ConfigureAwait(false);
                await CommitAsync(temp, "unmerged.txt").ConfigureAwait(false);
                await GitAsync(Vessel.LocalPath!, "worktree", "remove", "--force", temp).ConfigureAwait(false);
            }

            public async Task<Mission> AddMissionAsync(MissionStatusEnum status, string branch, string? dockId, string? voyageId = null)
            {
                Mission m = new Mission("cleanup " + status, "test") { VesselId = Vessel.Id, Status = status, BranchName = branch, DockId = dockId, VoyageId = voyageId };
                return await Db.Driver.Missions.CreateAsync(m).ConfigureAwait(false);
            }

            public void Dispose()
            {
                Db?.Dispose();
                try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch (Exception) { }
            }
        }

        private sealed class FailingPurgeDockService : IDockService
        {
            private readonly IDockService _Inner;

            public FailingPurgeDockService(IDockService inner) { _Inner = inner; }

            public Task<Dock?> ProvisionAsync(Vessel vessel, Captain captain, string branchName, string? missionId = null, CancellationToken token = default) => _Inner.ProvisionAsync(vessel, captain, branchName, missionId, token);
            public Task<Dock> ProvisionOnHarborAsync(Vessel vessel, Captain captain, string branchName, string missionId, string harborId, CancellationToken token = default) => _Inner.ProvisionOnHarborAsync(vessel, captain, branchName, missionId, harborId, token);
            public Task<bool> ReclaimAsync(string dockId, string? tenantId = null, CancellationToken token = default, string? forceReason = null, string caller = "") => _Inner.ReclaimAsync(dockId, tenantId, token, forceReason, caller);
            public Task RepairAsync(string dockId, string? tenantId = null, CancellationToken token = default) => _Inner.RepairAsync(dockId, tenantId, token);
            public Task UnstickAsync(string dockId, string? tenantId = null, CancellationToken token = default) => _Inner.UnstickAsync(dockId, tenantId, token);
            public Task<bool> DeleteAsync(string dockId, string? tenantId = null, CancellationToken token = default) => _Inner.DeleteAsync(dockId, tenantId, token);
            public Task PurgeAsync(string dockId, string? tenantId = null, CancellationToken token = default) => throw new InvalidOperationException("simulated purge failure");
        }

        #endregion
    }
}
