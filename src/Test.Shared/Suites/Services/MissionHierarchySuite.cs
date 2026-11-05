namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
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
    /// Mission hierarchy (CONCURRENCY_IMPROVEMENTS.md section 7): children wait for their parent's terminal stage, continue
    /// or fork its branch, are cancelled with it and restored by its restart; trees are validated and dispatched by key.
    /// </summary>
    public sealed class MissionHierarchySuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.MissionHierarchy";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("chain_shares_one_branch_and_runs_in_order", "A -> B -> C plus D: A and D run together, then B, then C; the only-child chain shares A's branch and D has its own", ChainAsync),
                Case("siblings_continue_and_fork", "Two children of one parent: the first continues its branch, the second forks from its head, even if it asks to continue", SiblingsAsync),
                Case("child_waits_for_terminal_stage", "A child waits while its parent's pipeline is still at a later stage", TerminalStageAsync),
                Case("failure_cancels_descendants_and_restart_restores_them", "A failed parent cancels its descendants with ParentFailed; restarting it restores them but not a hand-cancelled one", PropagationAsync),
                Case("validation_rules", "Cross-voyage parents, cycles, excess depth and a reparent with started descendants are rejected; a stage parent becomes its root", ValidationAsync),
                Case("dispatch_builds_tree_by_key", "Dispatch with Key and ParentKey builds the tree in one call; an unknown ParentKey creates nothing", DispatchTreeAsync)
            };
            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Mission hierarchy", cases: cases);
        }

        private static async Task ChainAsync(CancellationToken token)
        {
            using Harness h = await Harness.CreateAsync(captains: 4).ConfigureAwait(false);
            Mission a = await h.MissionAsync("A");
            Mission b = await h.MissionAsync("B", a);
            Mission c = await h.MissionAsync("C", b);
            Mission d = await h.MissionAsync("D");
            foreach (Mission m in new[] { a, b, c, d }) await h.Missions.TryAssignAsync(m, h.Vessel);
            AssertEqual(MissionStatusEnum.InProgress, (await h.ReadAsync(a)).Status, "A runs");
            AssertEqual(MissionStatusEnum.InProgress, (await h.ReadAsync(d)).Status, "D runs beside A");
            AssertEqual(MissionStatusEnum.Pending, (await h.ReadAsync(b)).Status, "B waits for A");
            AssertEqual(MissionAssignmentBlockerReasonEnum.ParentNotFinished, (await h.Missions.GetAssignmentBlockerAsync(await h.ReadAsync(b)))!.Reason);
            string aBranch = (await h.ReadAsync(a)).BranchName!;
            await h.FinishAsync(a);
            AssertTrue(await h.Missions.TryAssignAsync(await h.ReadAsync(b), h.Vessel), "B starts once A finished");
            Mission bRunning = await h.ReadAsync(b);
            AssertEqual(MissionBranchStrategyEnum.ContinueParent, bRunning.BranchStrategy);
            AssertEqual(aBranch, bRunning.BranchName, "an only child continues its parent's branch");
            AssertFalse(await h.Missions.TryAssignAsync(await h.ReadAsync(c), h.Vessel), "C waits for B");
            await h.FinishAsync(b);
            AssertTrue(await h.Missions.TryAssignAsync(await h.ReadAsync(c), h.Vessel));
            AssertEqual(aBranch, (await h.ReadAsync(c)).BranchName, "the whole chain is one branch");
            AssertTrue((await h.ReadAsync(d)).BranchName != aBranch, "D has its own branch");
        }

        private static async Task SiblingsAsync(CancellationToken token)
        {
            using Harness h = await Harness.CreateAsync(captains: 3).ConfigureAwait(false);
            Mission p = await h.MissionAsync("P");
            Mission x = await h.MissionAsync("X", p);
            Mission y = await h.MissionAsync("Y", p, MissionBranchStrategyEnum.ContinueParent);
            AssertTrue(await h.Missions.TryAssignAsync(p, h.Vessel));
            string pBranch = (await h.ReadAsync(p)).BranchName!;
            await h.FinishAsync(p);
            AssertTrue(await h.Missions.TryAssignAsync(await h.ReadAsync(x), h.Vessel));
            AssertTrue(await h.Missions.TryAssignAsync(await h.ReadAsync(y), h.Vessel), "siblings run in parallel");
            Mission xRun = await h.ReadAsync(x), yRun = await h.ReadAsync(y);
            AssertEqual(MissionBranchStrategyEnum.ContinueParent, xRun.BranchStrategy); AssertEqual(pBranch, xRun.BranchName);
            AssertEqual(MissionBranchStrategyEnum.ForkFromParent, yRun.BranchStrategy, "an explicit continue on a later sibling is downgraded");
            AssertEqual(pBranch, yRun.BaseBranchName, "the fork starts at the parent's branch head");
            AssertTrue(yRun.BranchName != pBranch, "siblings never share a branch");
            AssertTrue(h.Git.WorktreeBases.Contains(pBranch), "the fork's worktree is created from the parent's branch");
        }

        private static async Task TerminalStageAsync(CancellationToken token)
        {
            using Harness h = await Harness.CreateAsync(captains: 3).ConfigureAwait(false);
            Mission p = await h.MissionAsync("P");
            Mission judge = await h.Db.Missions.CreateAsync(new Mission("[Judge] P") { VesselId = h.Vessel.Id, VoyageId = h.Voyage.Id, DependsOnMissionId = p.Id, ParentMissionId = null });
            Mission child = await h.MissionAsync("child", p);
            AssertTrue(await h.Missions.TryAssignAsync(p, h.Vessel));
            await h.FinishAsync(p, MissionStatusEnum.WorkProduced);
            AssertFalse(await h.Missions.TryAssignAsync(await h.ReadAsync(child), h.Vessel), "the parent's Judge stage has not run");
            MissionAssignmentBlocker blocker = (await h.Missions.GetAssignmentBlockerAsync(await h.ReadAsync(child)))!;
            AssertEqual(MissionAssignmentBlockerReasonEnum.ParentNotFinished, blocker.Reason);
            Mission judgeRow = await h.ReadAsync(judge);
            judgeRow.Status = MissionStatusEnum.Complete; judgeRow.BranchName = (await h.ReadAsync(p)).BranchName; await h.Db.Missions.UpdateAsync(judgeRow);
            AssertTrue(await h.Missions.TryAssignAsync(await h.ReadAsync(child), h.Vessel), "the child starts once the terminal stage completes");
        }

        private static async Task PropagationAsync(CancellationToken token)
        {
            using Harness h = await Harness.CreateAsync(captains: 3).ConfigureAwait(false);
            Mission a = await h.MissionAsync("A");
            Mission b = await h.MissionAsync("B", a);
            Mission c = await h.MissionAsync("C", b);
            Mission manual = await h.MissionAsync("manual", a);
            Mission d = await h.MissionAsync("D");
            Mission manualRow = await h.ReadAsync(manual);
            manualRow.Status = MissionStatusEnum.Cancelled; await h.Db.Missions.UpdateAsync(manualRow);
            AssertTrue(await h.Missions.TryAssignAsync(a, h.Vessel));
            await h.FinishAsync(a, MissionStatusEnum.Failed);
            AssertFalse(await h.Missions.TryAssignAsync(await h.ReadAsync(b), h.Vessel));
            Mission bRow = await h.ReadAsync(b), cRow = await h.ReadAsync(c);
            AssertEqual(MissionStatusEnum.Cancelled, bRow.Status); AssertEqual(MissionFailureKindEnum.ParentFailed, bRow.FailureKind);
            AssertEqual(MissionStatusEnum.Cancelled, cRow.Status, "the whole subtree is cancelled"); AssertEqual(MissionFailureKindEnum.ParentFailed, cRow.FailureKind);
            AssertEqual(MissionStatusEnum.Pending, (await h.ReadAsync(d)).Status, "an unrelated root is unaffected");
            Mission restarted = await h.Missions.RestartAsync(await h.ReadAsync(a));
            AssertEqual(b.Id + "," + c.Id, String.Join(",", (restarted.RestoredMissionIds ?? new List<string>()).OrderBy(id => id == b.Id ? 0 : 1)), "the restart lists the descendants it re-pended");
            AssertEqual(MissionStatusEnum.Pending, (await h.ReadAsync(b)).Status, "restart restores B");
            AssertEqual(MissionStatusEnum.Pending, (await h.ReadAsync(c)).Status, "and C");
            AssertNull((await h.ReadAsync(c)).FailureKind);
            AssertEqual(MissionStatusEnum.Cancelled, (await h.ReadAsync(manual)).Status, "a hand-cancelled child stays cancelled");
        }

        private static async Task ValidationAsync(CancellationToken token)
        {
            using Harness h = await Harness.CreateAsync(captains: 1).ConfigureAwait(false);
            MissionHierarchyValidator validator = new MissionHierarchyValidator(h.Db, h.Settings);
            Mission root = await h.MissionAsync("root");
            Mission stage = await h.Db.Missions.CreateAsync(new Mission("[Judge] root") { VesselId = h.Vessel.Id, VoyageId = h.Voyage.Id, DependsOnMissionId = root.Id });
            Mission onStage = new Mission("child of a stage") { VesselId = h.Vessel.Id, VoyageId = h.Voyage.Id, ParentMissionId = stage.Id };
            await validator.NormalizeAsync(onStage, false, null);
            AssertEqual(root.Id, onStage.ParentMissionId, "a stage parent is normalized to its root");
            AssertEqual(1, onStage.Depth);

            Voyage other = await h.Db.Voyages.CreateAsync(new Voyage("other") { Status = VoyageStatusEnum.InProgress });
            await AssertThrowsAsync<ArgumentException>(() => validator.NormalizeAsync(new Mission("cross") { VesselId = h.Vessel.Id, VoyageId = other.Id, ParentMissionId = root.Id }, false, null));

            h.Settings.Concurrency.MaxHierarchyDepth = 2;
            Mission level1 = await h.MissionAsync("level1", root);
            Mission level2 = await h.MissionAsync("level2", level1);
            await AssertThrowsAsync<ArgumentException>(() => validator.NormalizeAsync(new Mission("level3") { VesselId = h.Vessel.Id, VoyageId = h.Voyage.Id, ParentMissionId = level2.Id }, false, null));

            Mission cyclic = await h.ReadAsync(root);
            cyclic.ParentMissionId = level2.Id;
            await AssertThrowsAsync<ArgumentException>(() => validator.NormalizeAsync(cyclic, true, null), "a mission under its own descendant is a cycle");

            Mission started = await h.ReadAsync(level2);
            started.Status = MissionStatusEnum.InProgress; await h.Db.Missions.UpdateAsync(started);
            Mission moving = await h.ReadAsync(level1);
            string? previous = moving.ParentMissionId;
            moving.ParentMissionId = null;
            await AssertThrowsAsync<ArgumentException>(() => validator.NormalizeAsync(moving, true, previous), "reparenting with a started descendant is rejected");

            await AssertThrowsAsync<ArgumentException>(() => validator.NormalizeAsync(new Mission("both") { VesselId = h.Vessel.Id, VoyageId = h.Voyage.Id, ParentMissionId = root.Id, DependsOnMissionId = level2.Id }, false, null),
                "a child root cannot also depend on a mission outside its own logical mission");
        }

        private static async Task DispatchTreeAsync(CancellationToken token)
        {
            using Harness h = await Harness.CreateAsync(captains: 2).ConfigureAwait(false);
            List<MissionDescription> plan = new List<MissionDescription>
            {
                new MissionDescription("Design", "design") { Key = "design" },
                new MissionDescription("Build", "build") { Key = "build", ParentKey = "design" },
                new MissionDescription("Docs", "docs") { ParentKey = "design", BranchStrategy = MissionBranchStrategyEnum.ForkFromParent }
            };
            Voyage voyage = await h.Admiral.DispatchVoyageAsync("tree", "tree", h.Vessel.Id, plan);
            List<Mission> missions = await h.Db.Missions.EnumerateByVoyageAsync(voyage.Id);
            Mission design = missions.Single(m => m.Title == "Design"), build = missions.Single(m => m.Title == "Build"), docs = missions.Single(m => m.Title == "Docs");
            AssertNull(design.ParentMissionId); AssertEqual(0, design.Depth);
            AssertEqual(design.Id, build.ParentMissionId); AssertEqual(1, build.Depth);
            AssertEqual(design.Id, docs.ParentMissionId); AssertEqual(MissionBranchStrategyEnum.ForkFromParent, docs.BranchStrategy);
            AssertEqual(MissionStatusEnum.Pending, build.Status, "children wait for the parent");

            int before = (await h.Db.Voyages.EnumerateAsync()).Count;
            await AssertThrowsAsync<ArgumentException>(() => h.Admiral.DispatchVoyageAsync("bad", "bad", h.Vessel.Id,
                new List<MissionDescription> { new MissionDescription("orphan", "x") { ParentKey = "missing" } }));
            AssertEqual(before, (await h.Db.Voyages.EnumerateAsync()).Count, "an unknown ParentKey creates no voyage");
        }

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
            => new TestCaseDescriptor(SuiteId, id, name, body, tags: new List<string> { TestTags.Positive });

        // One vessel, one voyage, idle captains; the git stub records the base of every new worktree.
        private sealed class Harness : IDisposable
        {
            private TestDatabase _Fixture = null!;
            internal DatabaseDriver Db => _Fixture.Driver;
            internal ArmadaSettings Settings = null!;
            internal MissionService Missions = null!;
            internal AdmiralService Admiral = null!;
            internal StubGitService Git = null!;
            internal Vessel Vessel = null!;
            internal Voyage Voyage = null!;

            internal static async Task<Harness> CreateAsync(int captains)
            {
                Harness h = new Harness();
                h._Fixture = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                h.Settings = new ArmadaSettings { DocksDirectory = TestTemp.NewDirectory("hierarchy_docks"), ReposDirectory = TestTemp.NewDirectory("hierarchy_repos") };
                LoggingModule logging = new LoggingModule(); logging.Settings.EnableConsole = false;
                h.Git = new StubGitService();
                IDockService docks = new DockService(logging, h.Db, h.Settings, h.Git);
                CaptainService captainService = new CaptainService(logging, h.Db, h.Settings, h.Git, docks);
                int pid = 5000;
                captainService.OnLaunchAgent = (_, _, _) => Task.FromResult(Interlocked.Increment(ref pid));
                h.Missions = new MissionService(logging, h.Db, h.Settings, docks, captainService);
                h.Admiral = new AdmiralService(logging, h.Db, h.Settings, captainService, h.Missions, new VoyageService(logging, h.Db), docks);
                h.Vessel = await h.Db.Vessels.CreateAsync(new Vessel("hierarchy", "https://github.com/test/repo") { MaxParallelVoyages = 0, MaxParallelMissionsPerVoyage = 0 }).ConfigureAwait(false);
                h.Voyage = await h.Db.Voyages.CreateAsync(new Voyage("hierarchy") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                for (int i = 0; i < captains; i++)
                {
                    Captain captain = new Captain("hierarchy-" + i) { State = CaptainStateEnum.Idle };
                    await CaptainAssignmentFixture.CreateAsync(h.Db, captain).ConfigureAwait(false);
                }
                return h;
            }

            internal async Task<Mission> MissionAsync(string title, Mission? parent = null, MissionBranchStrategyEnum strategy = MissionBranchStrategyEnum.New)
            {
                Mission mission = new Mission(title, title) { VesselId = Vessel.Id, VoyageId = Voyage.Id, ParentMissionId = parent?.Id, Depth = parent == null ? 0 : parent.Depth + 1, BranchStrategy = strategy };
                await Task.Delay(5).ConfigureAwait(false);
                return await Db.Missions.CreateAsync(mission).ConfigureAwait(false);
            }

            internal async Task<Mission> ReadAsync(Mission mission) => (await Db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;

            internal async Task FinishAsync(Mission mission, MissionStatusEnum status = MissionStatusEnum.Complete)
            {
                Mission row = await ReadAsync(mission).ConfigureAwait(false);
                row.Status = status; row.ProcessId = null;
                await Db.Missions.UpdateAsync(row).ConfigureAwait(false);
                if (row.CaptainId != null) await Db.Captains.TryReleaseFromMissionAsync(row.CaptainId, row.Id).ConfigureAwait(false);
            }

            public void Dispose() => _Fixture.Dispose();
        }
    }
}
