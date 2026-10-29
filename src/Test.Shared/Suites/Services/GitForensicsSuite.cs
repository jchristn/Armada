namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Forensic git reads for the black box (<see cref="GitService.GetMergeBaseAsync"/>,
    /// <see cref="GitService.GetCommitRangeAsync"/>, <see cref="GitTrailerParser"/>) against real temporary
    /// repositories: merge bases, ranges with authors, trailers and changed paths, the limit and TotalCount, missing
    /// revisions, argument validation, and the same reads carried over a stand-in Harbor command seam.
    /// </summary>
    public sealed class GitForensicsSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.GitForensics";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("merge_base_of_diverged_branches", "GetMergeBaseAsync returns the fork point of two diverged branches", TestTags.Positive, async () =>
            {
                string repo = DatedGitRepo.Create();
                DatedGitRepo.Commit(repo, "base", At("2026-01-01T00:00:00Z"), "a.txt", "1\n");
                string fork = DatedGitRepo.Git(repo, null, "rev-parse", "HEAD").Trim();
                DatedGitRepo.Git(repo, null, "checkout", "-q", "-b", "feature");
                DatedGitRepo.Commit(repo, "feature work", At("2026-01-02T00:00:00Z"), "b.txt", "1\n");
                DatedGitRepo.Git(repo, null, "checkout", "-q", "main");
                DatedGitRepo.Commit(repo, "main work", At("2026-01-03T00:00:00Z"), "c.txt", "1\n");

                GitService git = CreateService();
                AssertEqual(fork, await git.GetMergeBaseAsync(repo, "main", "feature").ConfigureAwait(false), "fork point");
                AssertEqual(fork, await git.GetMergeBaseAsync(repo, "feature", "main").ConfigureAwait(false), "symmetric");
            }));

            cases.Add(CaseAsync("merge_base_unknown_ref_null", "GetMergeBaseAsync returns null for an unknown revision and rejects option-like revisions", TestTags.Negative, async () =>
            {
                string repo = DatedGitRepo.Create();
                DatedGitRepo.Commit(repo, "base", At("2026-01-01T00:00:00Z"), "a.txt", "1\n");
                GitService git = CreateService();
                AssertNull(await git.GetMergeBaseAsync(repo, "main", "no-such-branch").ConfigureAwait(false), "unknown ref");
                await AssertThrowsAsync<ArgumentException>(() => git.GetMergeBaseAsync(repo, "--all", "main")).ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentNullException>(() => git.GetMergeBaseAsync(repo, "", "main")).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("range_reads_authors_trailers_paths", "GetCommitRangeAsync lists the commits after the start, newest first, with author, dates, subject, trailers and changed paths", TestTags.Positive, async () =>
            {
                string repo = DatedGitRepo.Create();
                DatedGitRepo.Commit(repo, "start", At("2026-02-01T00:00:00Z"), "README.md", "start\n");
                string start = DatedGitRepo.Git(repo, null, "rev-parse", "HEAD").Trim();
                DatedGitRepo.CommitWith(repo, "Land mission one\n\nBody text.\n\nArmada-Mission-Id: msn_one\nArmada-Voyage-Id: vyg_one", At("2026-02-02T00:00:00Z"), "Armada", "armada@example.com", null, "src/core/a.cs", "a\n");
                DatedGitRepo.CommitWith(repo, "Hotfix by hand", At("2026-02-03T00:00:00Z"), "Dana Human", "dana@example.com", null, "src/core/b.cs", "b\n");

                GitService git = CreateService();
                GitCommitRange? range = await git.GetCommitRangeAsync(repo, start, "main", 50).ConfigureAwait(false);
                AssertNotNull(range, "range");
                AssertEqual(2, range!.TotalCount);
                AssertEqual(2, range.Commits.Count);
                AssertFalse(range.Truncated, "not truncated");
                AssertEqual(start, range.FromSha);

                GitCommitInfo newest = range.Commits[0];
                AssertEqual("Hotfix by hand", newest.Subject, "newest first");
                AssertEqual("Dana Human", newest.AuthorName);
                AssertEqual("dana@example.com", newest.AuthorEmail);
                AssertNull(newest.MissionId, "no trailer: outside Armada");
                AssertEqual("src/core/b.cs", newest.ChangedPaths.Single());
                AssertEqual(At("2026-02-03T00:00:00Z").UtcDateTime, newest.CommittedUtc);

                GitCommitInfo landed = range.Commits[1];
                AssertEqual("msn_one", landed.MissionId, "Armada-Mission-Id trailer");
                AssertEqual(2, landed.Trailers.Count);
                AssertEqual("Armada-Voyage-Id", landed.Trailers[1].Key);
                AssertEqual("vyg_one", landed.Trailers[1].Value);
                AssertEqual("src/core/a.cs", landed.ChangedPaths.Single());
                AssertEqual(1, landed.ChangedPathTotal);
            }));

            cases.Add(CaseAsync("range_limit_keeps_total", "A limit smaller than the range returns the newest commits, the full TotalCount and Truncated", TestTags.Positive, async () =>
            {
                string repo = DatedGitRepo.Create();
                DatedGitRepo.Commit(repo, "start", At("2026-02-01T00:00:00Z"), "README.md", "start\n");
                string start = DatedGitRepo.Git(repo, null, "rev-parse", "HEAD").Trim();
                for (int i = 1; i <= 7; i++) DatedGitRepo.Commit(repo, "c" + i, At("2026-02-01T00:00:00Z").AddHours(i), "f" + i + ".txt", i + "\n");

                GitCommitRange? range = await CreateService().GetCommitRangeAsync(repo, start, "main", 3).ConfigureAwait(false);
                AssertEqual(7, range!.TotalCount);
                AssertEqual(3, range.Commits.Count);
                AssertTrue(range.Truncated, "truncated");
                AssertEqual("c7,c6,c5", String.Join(",", range.Commits.Select(c => c.Subject)));
            }));

            cases.Add(CaseAsync("range_empty_when_no_movement", "A range from the tip to itself is empty, not null", TestTags.Positive, async () =>
            {
                string repo = DatedGitRepo.Create();
                DatedGitRepo.Commit(repo, "start", At("2026-02-01T00:00:00Z"), "README.md", "start\n");
                GitCommitRange? range = await CreateService().GetCommitRangeAsync(repo, "main", "main", 10).ConfigureAwait(false);
                AssertNotNull(range, "range");
                AssertEqual(0, range!.TotalCount);
                AssertEqual(0, range.Commits.Count);
            }));

            cases.Add(CaseAsync("range_missing_start_null", "A start commit that does not exist in the repository gives null; bad limits and option-like revisions are rejected", TestTags.Negative, async () =>
            {
                string repo = DatedGitRepo.Create();
                DatedGitRepo.Commit(repo, "start", At("2026-02-01T00:00:00Z"), "README.md", "start\n");
                GitService git = CreateService();
                AssertNull(await git.GetCommitRangeAsync(repo, "0123456789abcdef0123456789abcdef01234567", "main", 10).ConfigureAwait(false), "missing start");
                AssertNull(await git.GetCommitRangeAsync(repo, "main", "gone-branch", 10).ConfigureAwait(false), "missing tip");
                await AssertThrowsAsync<ArgumentOutOfRangeException>(() => git.GetCommitRangeAsync(repo, "main", "main", 0)).ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentOutOfRangeException>(() => git.GetCommitRangeAsync(repo, "main", "main", GitCommitRange.MaxLimit + 1)).ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => git.GetCommitRangeAsync(repo, "--output=/tmp/x", "main", 10)).ConfigureAwait(false);
                AssertNull(await git.GetCommitRangeAsync("/nonexistent/armada/repo", "main", "main", 10).ConfigureAwait(false), "not a repository");
            }));

            cases.Add(CaseAsync("range_over_harbor_seam", "On a Harbor-hosted repository the same reads run through the host command seam; an unreachable Harbor gives null, not an exception", TestTags.Positive, async () =>
            {
                string repo = DatedGitRepo.Create();
                DatedGitRepo.Commit(repo, "start", At("2026-02-01T00:00:00Z"), "README.md", "start\n");
                string start = DatedGitRepo.Git(repo, null, "rev-parse", "HEAD").Trim();
                DatedGitRepo.Commit(repo, "remote work\n\nArmada-Mission-Id: msn_remote", At("2026-02-02T00:00:00Z"), "src/x.cs", "x\n");

                RecordingHostCommandExecutor harbor = new RecordingHostCommandExecutor();
                GitService remote = new GitService(Quiet(), harbor);
                GitCommitRange? range = await remote.GetCommitRangeAsync(repo, start, "main", 10).ConfigureAwait(false);
                AssertEqual("msn_remote", range!.Commits.Single().MissionId);
                AssertTrue(harbor.Commands.Any(c => c.StartsWith("git log", StringComparison.Ordinal)), "git log ran through the seam: " + String.Join(" | ", harbor.Commands));
                AssertEqual(start, await remote.GetMergeBaseAsync(repo, start, "main").ConfigureAwait(false));

                harbor.Unreachable = true;
                AssertNull(await remote.GetCommitRangeAsync(repo, start, "main", 10).ConfigureAwait(false), "unreachable range");
                AssertNull(await remote.GetMergeBaseAsync(repo, start, "main").ConfigureAwait(false), "unreachable merge base");
            }));

            cases.Add(Case("trailer_parser_blocks", "Trailers come only from a last paragraph made of trailer lines; continuations join; prose is not a trailer block", TestTags.Positive, () =>
            {
                List<GitTrailer> t = GitTrailerParser.Parse("Body.\n\nArmada-Mission-Id: msn_a\nCo-Authored-By: Someone\n  <someone@example.com>\n");
                AssertEqual(2, t.Count);
                AssertEqual("msn_a", t[0].Value);
                AssertEqual("Someone <someone@example.com>", t[1].Value, "continuation joined");
                AssertEqual(0, GitTrailerParser.Parse("Mentions Armada-Mission-Id: msn_a in prose\nand more prose").Count, "prose");
                AssertEqual(0, GitTrailerParser.Parse("Armada-Mission-Id: msn_a\n\nThen a closing paragraph.").Count, "not the last paragraph");
                AssertEqual(0, GitTrailerParser.Parse(null).Count, "null");
                AssertEqual(2, GitTrailerParser.Parse("A: 1\nB: 2\nC: 3", 2).Count, "max");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Git forensics (merge base, commit ranges, trailers)", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule Quiet()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static GitService CreateService()
        {
            return new GitService(Quiet());
        }

        private static DateTimeOffset At(string iso)
        {
            return DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
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
