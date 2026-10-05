namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="MissionService.EvaluateAutoLandAsync"/>, the auto-land dry-run. Positive
    /// cases confirm a small in-scope change reports Land; negative cases confirm an over-threshold change
    /// reports a hold with a reason and a missing vessel reports null.
    /// </summary>
    public sealed class EvaluateAutoLandSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the evaluate-auto-land suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("small_change_reports_land", "A small in-scope change reports Land", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                MissionService service = CreateService(testDb);

                Vessel vessel = new Vessel("autoland-vessel", "https://github.com/test/repo.git");
                vessel.AutoLandEnabled = true;
                vessel.AutoLandMaxFiles = 10;
                vessel.AutoLandMaxLines = 400;
                vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                Mission mission = new Mission("Small change", "desc");
                mission.VesselId = vessel.Id;
                mission.DiffSnapshot = AddedFileDiff("src/a.cs", "one line");
                mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                AutoLandDecision? decision = await service.EvaluateAutoLandAsync(mission.Id).ConfigureAwait(false);
                AssertNotNull(decision, "a mission with a vessel should yield a decision");
                AssertTrue(decision!.Land, "a small change under thresholds should auto-land");
            }));

            cases.Add(CaseAsync("over_threshold_reports_hold", "An over-threshold change reports a hold with a reason", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                MissionService service = CreateService(testDb);

                Vessel vessel = new Vessel("autoland-vessel", "https://github.com/test/repo.git");
                vessel.AutoLandEnabled = true;
                vessel.AutoLandMaxFiles = 1;
                vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                Mission mission = new Mission("Big change", "desc");
                mission.VesselId = vessel.Id;
                mission.DiffSnapshot = AddedFileDiff("src/a.cs", "x") + AddedFileDiff("src/b.cs", "y") + AddedFileDiff("src/c.cs", "z");
                mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                AutoLandDecision? decision = await service.EvaluateAutoLandAsync(mission.Id).ConfigureAwait(false);
                AssertNotNull(decision, "a mission with a vessel should yield a decision");
                AssertFalse(decision!.Land, "an over-file-limit change should hold");
                AssertNotNull(decision.HoldReason, "a hold should carry a reason");
            }));

            cases.Add(CaseAsync("no_vessel_reports_null", "A mission with no vessel reports null", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                MissionService service = CreateService(testDb);

                Mission mission = new Mission("Orphan", "desc");
                mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                AutoLandDecision? decision = await service.EvaluateAutoLandAsync(mission.Id).ConfigureAwait(false);
                AssertNull(decision, "a mission with no vessel yields no decision");
            }));


            cases.Add(CaseAsync("header_like_added_lines_count_toward_line_limit", "Added lines rendered as \"+++ ...\" count toward the line limit", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                MissionService service = CreateService(testDb);

                Vessel vessel = new Vessel("autoland-vessel", "https://github.com/test/repo.git");
                vessel.AutoLandEnabled = true;
                vessel.AutoLandMaxFiles = 10;
                vessel.AutoLandMaxLines = 2;
                vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                // Three added lines whose content starts with "++ " render as "+++ ..."; a header-text counter
                // skipped them and saw zero changed lines.
                Mission mission = new Mission("Header-like lines", "desc");
                mission.VesselId = vessel.Id;
                mission.DiffSnapshot = AddedFileDiff("notes.md", "++ one", "++ two", "++ three");
                mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                AutoLandDecision? decision = await service.EvaluateAutoLandAsync(mission.Id).ConfigureAwait(false);
                AssertNotNull(decision, "a mission with a vessel should yield a decision");
                AssertFalse(decision!.Land, "three changed lines exceed a two-line limit");
                AssertNotNull(decision.HoldReason, "a hold should carry a reason");
            }));

            cases.Add(CaseAsync("deleted_denied_path_holds", "Deleting a file under a denied path holds", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                MissionService service = CreateService(testDb);

                Vessel vessel = new Vessel("autoland-vessel", "https://github.com/test/repo.git");
                vessel.AutoLandEnabled = true;
                vessel.AutoLandMaxFiles = 10;
                vessel.AutoLandMaxLines = 400;
                vessel.AutoLandPathDenyGlobs = new List<string> { "migrations/**" };
                vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                Mission mission = new Mission("Delete migration", "desc");
                mission.VesselId = vessel.Id;
                mission.DiffSnapshot =
                    "diff --git a/migrations/001.sql b/migrations/001.sql\n" +
                    "deleted file mode 100644\n" +
                    "index 1111111..0000000\n" +
                    "--- a/migrations/001.sql\n" +
                    "+++ /dev/null\n" +
                    "@@ -1 +0,0 @@\n" +
                    "-create table t (id int);\n";
                mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                AutoLandDecision? decision = await service.EvaluateAutoLandAsync(mission.Id).ConfigureAwait(false);
                AssertNotNull(decision, "a mission with a vessel should yield a decision");
                AssertFalse(decision!.Land, "deleting a denied path must hold");
            }));

            cases.Add(CaseAsync("git_branch_changes_union_with_snapshot", "Paths git reports for the dock branch are checked even when the snapshot omits them", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                StubGitService git = new StubGitService();
                git.BranchChangesResult = new List<GitChangedFile>
                {
                    new GitChangedFile { Kind = GitChangeKindEnum.Modified, Path = "src/a.cs", AddedLines = 1, DeletedLines = 0 },
                    new GitChangedFile { Kind = GitChangeKindEnum.Deleted, Path = "secrets/keys.txt", AddedLines = 0, DeletedLines = 1 }
                };
                MissionService service = CreateService(testDb, git);
                string worktree = TestTemp.NewDirectory("autoland-dock");

                try
                {
                    Vessel vessel = new Vessel("autoland-vessel", "https://github.com/test/repo.git");
                    vessel.AutoLandEnabled = true;
                    vessel.AutoLandMaxFiles = 10;
                    vessel.AutoLandMaxLines = 400;
                    vessel.AutoLandPathDenyGlobs = new List<string> { "secrets/**" };
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Dock dock = new Dock(vessel.Id);
                    dock.WorktreePath = worktree;
                    dock = await testDb.Driver.Docks.CreateAsync(dock).ConfigureAwait(false);

                    Mission mission = new Mission("Partial snapshot", "desc");
                    mission.VesselId = vessel.Id;
                    mission.DockId = dock.Id;
                    mission.DiffSnapshot = AddedFileDiff("src/a.cs", "one line");
                    mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                    AutoLandDecision? decision = await service.EvaluateAutoLandAsync(mission.Id).ConfigureAwait(false);
                    AssertNotNull(decision, "a mission with a vessel should yield a decision");
                    AssertEqual(1, git.BranchChangesCalls.Count, "the dock worktree should be asked for its branch changes");
                    AssertFalse(decision!.Land, "a denied path reported by git must hold even when the snapshot omits it");
                }
                finally
                {
                    TestTemp.TryDelete(worktree);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.EvaluateAutoLand",
                displayName: "Evaluate AutoLand",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static MissionService CreateService(TestDatabase testDb, StubGitService? missionGit = null)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            ArmadaSettings settings = new ArmadaSettings();
            StubGitService git = new StubGitService();
            DockService dockService = new DockService(logging, testDb.Driver, settings, git);
            CaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
            return new MissionService(logging, testDb.Driver, settings, dockService, captainService, null, missionGit);
        }

        /// <summary>
        /// Build a well-formed git diff that adds a new file with the given lines.
        /// </summary>
        private static string AddedFileDiff(string path, params string[] lines)
        {
            string diff = "diff --git a/" + path + " b/" + path + "\n" +
                "new file mode 100644\n" +
                "index 0000000..1111111\n" +
                "--- /dev/null\n" +
                "+++ b/" + path + "\n" +
                "@@ -0,0 +1," + lines.Length + " @@\n";
            foreach (string line in lines) diff += "+" + line + "\n";
            return diff;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.EvaluateAutoLand",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (System.Threading.CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
