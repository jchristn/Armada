namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Health;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the git-based vessel health criteria (GitDivergence, WorkingTree, Branches, CommitRecency)
    /// against real repositories with a local bare origin: ahead, behind, and diverged counts after fetch, upstream
    /// divergence, fetch failure grading Unknown, dirty versus untracked versus clean, stale and leftover armada/*
    /// branches, and commit age.
    /// </summary>
    public sealed class VesselHealthGitCriteriaSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.VesselHealthGitCriteria";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("divergence_even_after_clone", "A fresh clone is even with origin/main", TestTags.Positive, async () =>
            {
                VesselHealthRepoPair repo = VesselHealthGitHelper.CreateCloneWithOrigin();
                VesselHealthContext context = CreateContext(repo.WorkingPath, false);
                VesselHealthCriterionResult result = await new GitDivergenceCriterion().EvaluateAsync(context).ConfigureAwait(false);
                AssertEqual(VesselHealthStatusEnum.Pass, result.Status);
                AssertEqual(VesselHealthDetailCodes.Even, result.DetailCode);
                AssertEqual(0, context.Health.AheadOfDefault!.Value);
                AssertEqual(0, context.Health.BehindDefault!.Value);
                AssertEqual(0, context.Health.BehindUpstream!.Value);
                AssertEqual("main", context.Health.CurrentBranch);
            }));

            cases.Add(CaseAsync("divergence_behind_only_after_fetch", "Behind counts appear after fetching new origin commits", TestTags.Positive, async () =>
            {
                VesselHealthRepoPair repo = VesselHealthGitHelper.CreateCloneWithOrigin();
                VesselHealthGitHelper.PushCommitsToOrigin(repo.OriginPath, 2);

                VesselHealthContext stale = CreateContext(repo.WorkingPath, false);
                VesselHealthCriterionResult before = await new GitDivergenceCriterion().EvaluateAsync(stale).ConfigureAwait(false);
                AssertEqual(VesselHealthDetailCodes.Even, before.DetailCode, "without fetch the refs are stale");

                await CreateGit().FetchRemotesAsync(repo.WorkingPath).ConfigureAwait(false);
                VesselHealthContext context = CreateContext(repo.WorkingPath, false);
                VesselHealthCriterionResult result = await new GitDivergenceCriterion().EvaluateAsync(context).ConfigureAwait(false);
                AssertEqual(VesselHealthStatusEnum.Warn, result.Status);
                AssertEqual(VesselHealthDetailCodes.Behind, result.DetailCode);
                AssertEqual(0L, result.ValueA!.Value);
                AssertEqual(2L, result.ValueB!.Value);
                AssertEqual(2, context.Health.BehindDefault!.Value);
                AssertEqual(2, context.Health.BehindUpstream!.Value, "upstream divergence");
            }));

            cases.Add(CaseAsync("divergence_behind_fail_threshold", "Behind at or above BehindFail grades Fail", TestTags.Positive, async () =>
            {
                VesselHealthRepoPair repo = VesselHealthGitHelper.CreateCloneWithOrigin();
                VesselHealthGitHelper.PushCommitsToOrigin(repo.OriginPath, 3);
                await CreateGit().FetchRemotesAsync(repo.WorkingPath).ConfigureAwait(false);
                VesselHealthContext context = CreateContext(repo.WorkingPath, false);
                context.Settings.Thresholds.BehindFail = 3;
                VesselHealthCriterionResult result = await new GitDivergenceCriterion().EvaluateAsync(context).ConfigureAwait(false);
                AssertEqual(VesselHealthStatusEnum.Fail, result.Status);
                AssertEqual(3L, result.ValueB!.Value);
            }));

            cases.Add(CaseAsync("divergence_ahead_only", "Local commits are ahead and still Pass", TestTags.Positive, async () =>
            {
                VesselHealthRepoPair repo = VesselHealthGitHelper.CreateCloneWithOrigin();
                VesselHealthGitHelper.Commit(repo.WorkingPath, "local.txt", "x", "local", null);
                VesselHealthContext context = CreateContext(repo.WorkingPath, false);
                VesselHealthCriterionResult result = await new GitDivergenceCriterion().EvaluateAsync(context).ConfigureAwait(false);
                AssertEqual(VesselHealthStatusEnum.Pass, result.Status);
                AssertEqual(VesselHealthDetailCodes.Ahead, result.DetailCode);
                AssertEqual(1L, result.ValueA!.Value);
                AssertEqual(1, context.Health.AheadOfUpstream!.Value);
            }));

            cases.Add(CaseAsync("divergence_diverged_fails", "Ahead and behind at once grades Fail Diverged", TestTags.Negative, async () =>
            {
                VesselHealthRepoPair repo = VesselHealthGitHelper.CreateCloneWithOrigin();
                VesselHealthGitHelper.Commit(repo.WorkingPath, "local.txt", "x", "local", null);
                VesselHealthGitHelper.PushCommitsToOrigin(repo.OriginPath, 1);
                await CreateGit().FetchRemotesAsync(repo.WorkingPath).ConfigureAwait(false);
                VesselHealthContext context = CreateContext(repo.WorkingPath, false);
                VesselHealthCriterionResult result = await new GitDivergenceCriterion().EvaluateAsync(context).ConfigureAwait(false);
                AssertEqual(VesselHealthStatusEnum.Fail, result.Status);
                AssertEqual(VesselHealthDetailCodes.Diverged, result.DetailCode);
                AssertEqual(1L, result.ValueA!.Value);
                AssertEqual(1L, result.ValueB!.Value);
            }));

            cases.Add(CaseAsync("divergence_grade_upstream_diverged", "Diverging from the upstream alone grades Fail", TestTags.Negative, () =>
            {
                VesselHealthCriterionResult result = GitDivergenceCriterion.Grade(new GitDivergenceCounts(0, 0), new GitDivergenceCounts(2, 1), 1, 21);
                AssertEqual(VesselHealthStatusEnum.Fail, result.Status);
                AssertEqual(VesselHealthDetailCodes.Diverged, result.DetailCode);
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("divergence_missing_default_branch_unknown", "A missing default branch grades Unknown", TestTags.Negative, async () =>
            {
                VesselHealthRepoPair repo = VesselHealthGitHelper.CreateCloneWithOrigin();
                VesselHealthContext context = CreateContext(repo.WorkingPath, false);
                context.Vessel.DefaultBranch = "does-not-exist";
                VesselHealthCriterionResult result = await new GitDivergenceCriterion().EvaluateAsync(context).ConfigureAwait(false);
                AssertEqual(VesselHealthStatusEnum.Unknown, result.Status);
                AssertEqual(VesselHealthDetailCodes.DefaultBranchMissing, result.DetailCode);
            }));

            cases.Add(CaseAsync("fetch_failure_grades_unknown", "A failed fetch grades GitDivergence Unknown (FetchFailed), never Pass", TestTags.Negative, async () =>
            {
                VesselHealthRepoPair repo = VesselHealthGitHelper.CreateCloneWithOrigin();
                VesselHealthGitHelper.RunGit(repo.WorkingPath, null, "remote", "set-url", "origin", Path.Combine(Path.GetTempPath(), "armada-missing-origin-" + Guid.NewGuid().ToString("N")));

                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb.Driver, repo.WorkingPath).ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.RepositoryHealth.FetchBeforeEvaluate = true;
                VesselHealthEvaluator evaluator = new VesselHealthEvaluator(testDb.Driver, CreateGit(), settings,
                    new List<IVesselHealthCriterion> { new GitDivergenceCriterion(), new WorkingTreeCriterion() }, CreateLogging());

                VesselHealth health = await evaluator.EvaluateAsync(vessel, false).ConfigureAwait(false);
                AssertEqual(VesselHealthStatusEnum.Unknown, health.DivergenceStatus);
                AssertEqual(VesselHealthStatusEnum.Pass, health.WorkingTreeStatus);
                List<VesselHealthFinding> findings = await testDb.Driver.VesselHealthFindings.ReadByVesselAsync(Constants.DefaultTenantId, vessel.Id).ConfigureAwait(false);
                VesselHealthFinding divergence = findings.Single(f => f.Criterion == VesselHealthCriterionEnum.GitDivergence);
                AssertEqual(VesselHealthDetailCodes.FetchFailed, divergence.DetailCode);
                AssertEqual(repo.WorkingPath, health.EvaluatedPath);
            }));

            cases.Add(CaseAsync("evaluator_fetches_before_divergence", "The evaluator fetches first, so behind counts are current", TestTags.Positive, async () =>
            {
                VesselHealthRepoPair repo = VesselHealthGitHelper.CreateCloneWithOrigin();
                VesselHealthGitHelper.PushCommitsToOrigin(repo.OriginPath, 1);
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb.Driver, repo.WorkingPath).ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                VesselHealthEvaluator evaluator = new VesselHealthEvaluator(testDb.Driver, CreateGit(), settings,
                    new List<IVesselHealthCriterion> { new GitDivergenceCriterion() }, CreateLogging());
                VesselHealth health = await evaluator.EvaluateAsync(vessel, false).ConfigureAwait(false);
                AssertEqual(1, health.BehindDefault!.Value);
                AssertEqual(VesselHealthStatusEnum.Warn, health.DivergenceStatus);
            }));

            cases.Add(CaseAsync("working_tree_clean_untracked_modified", "Clean passes, untracked warns, modified fails", TestTags.Positive, async () =>
            {
                VesselHealthRepoPair repo = VesselHealthGitHelper.CreateCloneWithOrigin();
                WorkingTreeCriterion criterion = new WorkingTreeCriterion();

                VesselHealthContext clean = CreateContext(repo.WorkingPath, false);
                VesselHealthCriterionResult cleanResult = await criterion.EvaluateAsync(clean).ConfigureAwait(false);
                AssertEqual(VesselHealthStatusEnum.Pass, cleanResult.Status);
                AssertEqual(VesselHealthDetailCodes.Clean, cleanResult.DetailCode);
                AssertEqual(false, clean.Health.IsDirty);

                File.WriteAllText(Path.Combine(repo.WorkingPath, "new1.txt"), "a");
                File.WriteAllText(Path.Combine(repo.WorkingPath, "new2.txt"), "b");
                VesselHealthContext untracked = CreateContext(repo.WorkingPath, false);
                VesselHealthCriterionResult untrackedResult = await criterion.EvaluateAsync(untracked).ConfigureAwait(false);
                AssertEqual(VesselHealthStatusEnum.Warn, untrackedResult.Status);
                AssertEqual(VesselHealthDetailCodes.UntrackedOnly, untrackedResult.DetailCode);
                AssertEqual(2L, untrackedResult.ValueB!.Value);
                AssertEqual(2, untracked.Health.UntrackedCount!.Value);
                AssertEqual(false, untracked.Health.IsDirty);

                File.AppendAllText(Path.Combine(repo.WorkingPath, "README.md"), "changed\n");
                VesselHealthContext modified = CreateContext(repo.WorkingPath, false);
                VesselHealthCriterionResult modifiedResult = await criterion.EvaluateAsync(modified).ConfigureAwait(false);
                AssertEqual(VesselHealthStatusEnum.Fail, modifiedResult.Status);
                AssertEqual(VesselHealthDetailCodes.Modified, modifiedResult.DetailCode);
                AssertEqual(1L, modifiedResult.ValueA!.Value);
                AssertEqual(2L, modifiedResult.ValueB!.Value);
                AssertEqual(true, modified.Health.IsDirty);
            }));

            cases.Add(CaseAsync("bare_repository_not_applicable", "A bare clone grades WorkingTree NotApplicable (BareRepository) and records the bare path", TestTags.Positive, async () =>
            {
                string bare = TestGitRepoHelper.CreateBareRepoCopy();
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Vessel vessel = new Vessel("bare-vessel", "https://example.com/bare.git");
                vessel.TenantId = Constants.DefaultTenantId;
                vessel.LocalPath = bare;
                vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada-no-such-dir-" + Guid.NewGuid().ToString("N"));
                vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.RepositoryHealth.FetchBeforeEvaluate = false;
                VesselHealthEvaluator evaluator = new VesselHealthEvaluator(testDb.Driver, CreateGit(), settings,
                    new List<IVesselHealthCriterion> { new GitDivergenceCriterion(), new WorkingTreeCriterion(), new TestInfrastructureCriterion(null), new ContinuousIntegrationCriterion() }, CreateLogging());
                VesselHealth health = await evaluator.EvaluateAsync(vessel, false).ConfigureAwait(false);
                AssertEqual(bare, health.EvaluatedPath);
                AssertEqual(VesselHealthStatusEnum.NotApplicable, health.WorkingTreeStatus);
                AssertEqual(VesselHealthStatusEnum.NotApplicable, health.TestInfraStatus);
                AssertEqual(VesselHealthStatusEnum.Pass, health.DivergenceStatus);
                AssertEqual(true, health.HasReadme, "readme detected from tracked files");
                List<VesselHealthFinding> findings = await testDb.Driver.VesselHealthFindings.ReadByVesselAsync(Constants.DefaultTenantId, vessel.Id).ConfigureAwait(false);
                AssertEqual(VesselHealthDetailCodes.BareRepository, findings.Single(f => f.Criterion == VesselHealthCriterionEnum.WorkingTree).DetailCode);
            }));

            cases.Add(CaseAsync("missing_repository_unknown", "A vessel with no usable repository grades git criteria Unknown (RepositoryUnavailable)", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb.Driver, Path.Combine(Path.GetTempPath(), "armada-missing-" + Guid.NewGuid().ToString("N"))).ConfigureAwait(false);
                VesselHealthEvaluator evaluator = new VesselHealthEvaluator(testDb.Driver, CreateGit(), new ArmadaSettings(),
                    new List<IVesselHealthCriterion> { new GitDivergenceCriterion(), new MissionOutcomesCriterion(testDb.Driver) }, CreateLogging());
                VesselHealth health = await evaluator.EvaluateAsync(vessel, false).ConfigureAwait(false);
                AssertEqual(VesselHealthDetailCodes.RepositoryUnavailable, health.ErrorCode);
                AssertEqual(VesselHealthStatusEnum.Unknown, health.DivergenceStatus);
                AssertEqual(VesselHealthStatusEnum.Pass, health.MissionOutcomeStatus, "criteria that do not need a repository still run");
            }));

            cases.Add(CaseAsync("branches_stale_and_armada_counts", "Stale branches and leftover armada/* branches are counted", TestTags.Positive, async () =>
            {
                VesselHealthRepoPair repo = VesselHealthGitHelper.CreateCloneWithOrigin();
                DateTime old = DateTime.UtcNow.AddDays(-200);
                VesselHealthGitHelper.CreateBranchWithCommit(repo.WorkingPath, "feature/old-one", old);
                VesselHealthGitHelper.CreateBranchWithCommit(repo.WorkingPath, "feature/old-two", old);
                VesselHealthGitHelper.CreateBranchWithCommit(repo.WorkingPath, "feature/fresh", DateTime.UtcNow.AddDays(-1));
                VesselHealthGitHelper.CreateBranchWithCommit(repo.WorkingPath, "armada/captain-1/msn_abc", old);

                VesselHealthContext context = CreateContext(repo.WorkingPath, false);
                context.Settings.Thresholds.StaleBranchWarn = 2;
                context.Settings.Thresholds.StaleBranchFail = 5;
                VesselHealthCriterionResult result = await new BranchesCriterion().EvaluateAsync(context).ConfigureAwait(false);
                AssertEqual(5, context.Health.BranchCount!.Value);
                AssertEqual(2, context.Health.StaleBranchCount!.Value);
                AssertEqual(1, context.Health.ArmadaBranchCount!.Value);
                AssertEqual(VesselHealthStatusEnum.Warn, result.Status);
                AssertEqual(VesselHealthDetailCodes.StaleBranches, result.DetailCode);
                AssertEqual(2L, result.ValueA!.Value);
                AssertEqual(1L, result.ValueB!.Value);
            }));

            cases.Add(CaseAsync("branches_grade_thresholds", "Branch grading: Pass, armada-only Warn, stale Fail", TestTags.Positive, () =>
            {
                AssertEqual(VesselHealthStatusEnum.Pass, BranchesCriterion.Grade(3, 0, 4, 11).Status);
                VesselHealthCriterionResult armada = BranchesCriterion.Grade(0, 2, 4, 11);
                AssertEqual(VesselHealthStatusEnum.Warn, armada.Status);
                AssertEqual(VesselHealthDetailCodes.ArmadaBranches, armada.DetailCode);
                AssertEqual(VesselHealthStatusEnum.Warn, BranchesCriterion.Grade(10, 0, 4, 11).Status);
                AssertEqual(VesselHealthStatusEnum.Fail, BranchesCriterion.Grade(11, 0, 4, 11).Status);
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("commit_recency_reports_age", "CommitRecency records the last commit and its age in days", TestTags.Positive, async () =>
            {
                VesselHealthRepoPair repo = VesselHealthGitHelper.CreateCloneWithOrigin();
                VesselHealthGitHelper.Commit(repo.WorkingPath, "aged.txt", "x", "aged", DateTime.UtcNow.AddDays(-10).AddHours(-1));
                VesselHealthContext context = CreateContext(repo.WorkingPath, false);
                VesselHealthCriterionResult result = await new CommitRecencyCriterion().EvaluateAsync(context).ConfigureAwait(false);
                AssertEqual(VesselHealthStatusEnum.Pass, result.Status);
                AssertEqual(VesselHealthDetailCodes.LastCommitAge, result.DetailCode);
                AssertEqual(10L, result.ValueA!.Value);
                AssertNotNull(context.Health.LastCommitUtc, "LastCommitUtc");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Vessel Health Git Criteria",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static GitService CreateGit()
        {
            return new GitService(CreateLogging());
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static VesselHealthContext CreateContext(string path, bool bare)
        {
            Vessel vessel = new Vessel("health-test", "https://example.com/health.git");
            vessel.TenantId = Constants.DefaultTenantId;
            vessel.WorkingDirectory = path;
            VesselHealthContext context = new VesselHealthContext(vessel, Constants.DefaultTenantId, new RepositoryHealthSettings(), null, CreateGit());
            context.EvaluatedPath = path;
            context.IsBare = bare;
            context.RepositoryAvailable = true;
            return context;
        }

        private static async Task<Vessel> CreateVesselAsync(DatabaseDriver db, string workingDirectory)
        {
            Vessel vessel = new Vessel("health-" + Guid.NewGuid().ToString("N").Substring(0, 8), "https://example.com/health.git");
            vessel.TenantId = Constants.DefaultTenantId;
            vessel.WorkingDirectory = workingDirectory;
            return await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);
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
    }
}
