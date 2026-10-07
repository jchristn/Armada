namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Vessel history reads in <see cref="GitService"/> against real temporary repositories with fixed commit dates:
    /// per-day activity bucketing in a UTC offset (across midnight), zero-filled days, first and last commit dates,
    /// commit pages (order, merge stats against the first parent, renames, binary files, the files cap, the before
    /// bound, paging from a pinned tip while new commits land), bare repositories, and the history cursor and log parser.
    /// </summary>
    public sealed class GitHistorySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.GitHistory";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("activity_buckets_by_offset_across_midnight", "Activity buckets commits by committer date in the requested offset, zero-filling every day", TestTags.Positive, async () =>
            {
                string repo = DatedGitRepo.Create();
                DatedGitRepo.Commit(repo, "late", At("2026-03-10T23:30:00Z"), "a.txt", "1\n");
                DatedGitRepo.Commit(repo, "after midnight", At("2026-03-11T00:30:00Z"), "a.txt", "2\n");
                DatedGitRepo.Commit(repo, "morning", At("2026-03-11T06:00:00Z"), "a.txt", "3\n");
                GitService git = CreateService();

                VesselCommitActivity utc = await git.GetCommitActivityAsync(repo, "main", Day("2026-03-08"), Day("2026-03-14"), 0).ConfigureAwait(false);
                AssertNull(utc.Error, "no error");
                AssertEqual(7, utc.Days.Count, "one entry per day, inclusive");
                AssertEqual("2026-03-08", utc.Days[0].Date);
                AssertEqual("2026-03-14", utc.Days[6].Date);
                AssertEqual(1, CountOn(utc, "2026-03-10"), "UTC: one commit on the 10th");
                AssertEqual(2, CountOn(utc, "2026-03-11"), "UTC: two on the 11th");
                AssertEqual(0, CountOn(utc, "2026-03-12"), "zero-filled");
                AssertEqual(3, utc.TotalCommits);
                AssertEqual(2, utc.MaxDayCount);
                AssertEqual("2026-03-08", utc.From);
                AssertEqual("2026-03-14", utc.To);
                AssertEqual("main", utc.Branch);

                VesselCommitActivity pacific = await git.GetCommitActivityAsync(repo, "main", Day("2026-03-08"), Day("2026-03-14"), -420).ConfigureAwait(false);
                AssertEqual(3, CountOn(pacific, "2026-03-10"), "UTC-7: all three are on the 10th");
                AssertEqual(0, CountOn(pacific, "2026-03-11"));
                AssertEqual(3, pacific.MaxDayCount);
                AssertEqual(-420, pacific.UtcOffsetMinutes);

                VesselCommitActivity east = await git.GetCommitActivityAsync(repo, "main", Day("2026-03-08"), Day("2026-03-14"), 60).ConfigureAwait(false);
                AssertEqual(0, CountOn(east, "2026-03-10"), "UTC+1: 23:30Z is 00:30 on the 11th");
                AssertEqual(3, CountOn(east, "2026-03-11"));

                // The range edge: a one-day range in UTC+1 holds only the commits of that local day.
                VesselCommitActivity edge = await git.GetCommitActivityAsync(repo, "main", Day("2026-03-10"), Day("2026-03-10"), 60).ConfigureAwait(false);
                AssertEqual(1, edge.Days.Count);
                AssertEqual(0, edge.TotalCommits, "nothing on the 10th in UTC+1");
            }));

            cases.Add(CaseAsync("activity_first_last_cover_whole_branch", "First and last commit dates span the whole branch, not just the range; commits outside the range are not counted", TestTags.Positive, async () =>
            {
                string repo = DatedGitRepo.Create();
                DatedGitRepo.Commit(repo, "ancient", At("2019-05-01T12:00:00Z"), "a.txt", "1\n");
                DatedGitRepo.Commit(repo, "in range", At("2026-02-01T12:00:00Z"), "a.txt", "2\n");
                DatedGitRepo.Commit(repo, "newest", At("2026-09-01T12:00:00Z"), "a.txt", "3\n");
                GitService git = CreateService();

                VesselCommitActivity activity = await git.GetCommitActivityAsync(repo, "main", Day("2026-01-01"), Day("2026-03-31"), 0).ConfigureAwait(false);
                AssertEqual(90, activity.Days.Count);
                AssertEqual(1, activity.TotalCommits, "only the February commit is in range");
                AssertEqual(At("2019-05-01T12:00:00Z").UtcDateTime, activity.FirstCommitUtc, "first commit is the root, outside the range");
                AssertEqual(At("2026-09-01T12:00:00Z").UtcDateTime, activity.LastCommitUtc, "last commit is the tip, outside the range");
                AssertEqual(DateTimeKind.Utc, activity.FirstCommitUtc!.Value.Kind);
            }));

            cases.Add(CaseAsync("activity_unknown_branch_and_empty_repo_set_error", "An unknown branch or a repository without commits sets Error and still zero-fills the days", TestTags.Negative, async () =>
            {
                GitService git = CreateService();
                string repo = DatedGitRepo.Create();
                DatedGitRepo.Commit(repo, "one", At("2026-03-10T10:00:00Z"), "a.txt", "1\n");
                VesselCommitActivity missing = await git.GetCommitActivityAsync(repo, "no-such-branch", Day("2026-03-01"), Day("2026-03-03"), 0).ConfigureAwait(false);
                AssertNotNull(missing.Error, "unknown branch");
                AssertEqual(3, missing.Days.Count);
                AssertTrue(missing.Days.All(d => d.Count == 0));
                AssertNull(missing.FirstCommitUtc);

                string empty = DatedGitRepo.Create();
                VesselCommitActivity none = await git.GetCommitActivityAsync(empty, "main", Day("2026-03-01"), Day("2026-03-01"), 0).ConfigureAwait(false);
                AssertNotNull(none.Error, "no commits");
                AssertNull(await git.ResolveBranchTipAsync(empty, "main").ConfigureAwait(false), "an unborn branch does not resolve");

                await AssertThrowsAsync<ArgumentOutOfRangeException>(() => git.GetCommitActivityAsync(repo, "main", Day("2026-03-02"), Day("2026-03-01"), 0));
                await AssertThrowsAsync<ArgumentOutOfRangeException>(() => git.GetCommitActivityAsync(repo, "main", Day("2026-03-01"), Day("2026-03-01"), 841));
                await AssertThrowsAsync<ArgumentException>(() => git.ResolveBranchTipAsync(repo, "--output=/tmp/x"));
            }));

            cases.Add(CaseAsync("log_commit_fields_and_order", "A page lists commits newest first with SHAs, parents, author and committer, dates, subject, and body; the root commit lists its files", TestTags.Positive, async () =>
            {
                string repo = DatedGitRepo.Create();
                DatedGitRepo.Commit(repo, "Root commit", At("2026-01-01T08:00:00Z"), "README.md", "hello\n");
                DatedGitRepo.CommitWith(repo, "Second subject\n\nBody line one.\nBody line two.\n", At("2026-01-02T09:00:00Z"), "Ada Author", "ada@example.com", At("2026-01-02T08:00:00Z"), "README.md", "hello\nworld\n");
                GitService git = CreateService();
                string? tip = await git.ResolveBranchTipAsync(repo, "main").ConfigureAwait(false);
                AssertNotNull(tip);
                AssertTrue(GitRevisionNames.IsFullCommitId(tip));
                AssertEqual(tip, await git.ResolveBranchTipAsync(repo, tip!).ConfigureAwait(false), "a full commit id resolves to itself");

                GitCommitLogPage page = await git.GetCommitLogAsync(repo, tip!, null, 0, 50).ConfigureAwait(false);
                AssertEqual(2, page.Commits.Count);
                AssertFalse(page.HasMore);
                VesselCommit second = page.Commits[0];
                AssertEqual(tip, second.Sha);
                AssertTrue(second.Sha.StartsWith(second.ShortSha, StringComparison.Ordinal) && second.ShortSha.Length >= 7, "short SHA");
                AssertEqual("Second subject", second.Subject);
                AssertEqual("Body line one.\nBody line two.", second.Body);
                AssertEqual("Ada Author", second.AuthorName);
                AssertEqual("ada@example.com", second.AuthorEmail);
                AssertEqual("History Test", second.CommitterName);
                AssertEqual("history@armada.test", second.CommitterEmail);
                AssertEqual(At("2026-01-02T08:00:00Z").UtcDateTime, second.AuthoredUtc);
                AssertEqual(At("2026-01-02T09:00:00Z").UtcDateTime, second.CommittedUtc);
                AssertEqual(DateTimeKind.Utc, second.CommittedUtc.Kind);
                AssertEqual(1, second.ParentShas.Count);
                AssertEqual(page.Commits[1].Sha, second.ParentShas[0]);
                AssertFalse(second.IsMerge);
                AssertEqual(1, second.FilesChanged);
                AssertEqual(1, second.AddedLines);
                AssertEqual(0, second.DeletedLines);
                AssertEqual(GitChangeKindEnum.Modified, second.Files[0].Kind);

                VesselCommit root = page.Commits[1];
                AssertEqual(0, root.ParentShas.Count);
                AssertEqual("", root.Body);
                AssertEqual(1, root.FilesChanged, "the root commit is diffed against the empty tree");
                AssertEqual(GitChangeKindEnum.Added, root.Files[0].Kind);
                AssertEqual("README.md", root.Files[0].Path);
            }));

            cases.Add(CaseAsync("log_merge_stats_against_first_parent", "A merge commit is flagged with both parents and its files and lines are measured against the first parent", TestTags.Positive, async () =>
            {
                string repo = DatedGitRepo.Create();
                DatedGitRepo.Commit(repo, "base", At("2026-04-01T10:00:00Z"), "a.txt", "one\n");
                DatedGitRepo.Git(repo, null, "checkout", "-q", "-b", "feature");
                DatedGitRepo.Commit(repo, "feature work", At("2026-04-02T10:00:00Z"), "a.txt", "one\ntwo\nthree\n");
                DatedGitRepo.Git(repo, null, "checkout", "-q", "main");
                DatedGitRepo.Commit(repo, "main work", At("2026-04-03T10:00:00Z"), "b.txt", "b\n");
                DatedGitRepo.Git(repo, At("2026-04-04T10:00:00Z"), "merge", "--no-ff", "-q", "-m", "Merge feature", "feature");

                GitService git = CreateService();
                string tip = (await git.ResolveBranchTipAsync(repo, "main").ConfigureAwait(false))!;
                GitCommitLogPage page = await git.GetCommitLogAsync(repo, tip, null, 0, 50).ConfigureAwait(false);
                AssertEqual(4, page.Commits.Count);
                AssertEqual("Merge feature,main work,feature work,base", Subjects(page.Commits), "commit date order");
                VesselCommit merge = page.Commits[0];
                AssertTrue(merge.IsMerge);
                AssertEqual(2, merge.ParentShas.Count);
                AssertEqual(1, merge.FilesChanged, "only the feature's change, not main's");
                AssertEqual("a.txt", merge.Files[0].Path);
                AssertEqual(2, merge.AddedLines);
                AssertEqual(0, merge.DeletedLines);
            }));

            cases.Add(CaseAsync("log_renames_and_binary_files", "Renames report the source path; binary files report no line counts and count zero lines", TestTags.Positive, async () =>
            {
                string repo = DatedGitRepo.Create();
                DatedGitRepo.Commit(repo, "add", At("2026-05-01T10:00:00Z"), "old/name.txt", "a\nb\nc\nd\ne\nf\n");
                Directory.CreateDirectory(Path.Combine(repo, "new"));
                DatedGitRepo.Git(repo, null, "mv", "old/name.txt", "new/name.txt");
                File.WriteAllBytes(Path.Combine(repo, "image.bin"), new byte[] { 0, 1, 2, 3, 0, 255, 0, 9 });
                File.WriteAllText(Path.Combine(repo, "notes.txt"), "x\ny\n");
                DatedGitRepo.Git(repo, null, "add", "-A");
                DatedGitRepo.Git(repo, At("2026-05-02T10:00:00Z"), "commit", "-q", "-m", "rename and binary");

                GitService git = CreateService();
                string tip = (await git.ResolveBranchTipAsync(repo, "main").ConfigureAwait(false))!;
                VesselCommit commit = (await git.GetCommitLogAsync(repo, tip, null, 0, 1).ConfigureAwait(false)).Commits.Single();
                AssertEqual(3, commit.FilesChanged);
                GitChangedFile renamed = commit.Files.Single(f => f.Path == "new/name.txt");
                AssertEqual(GitChangeKindEnum.Renamed, renamed.Kind);
                AssertEqual("old/name.txt", renamed.OldPath);
                AssertEqual(0, renamed.AddedLines);
                GitChangedFile binary = commit.Files.Single(f => f.Path == "image.bin");
                AssertTrue(binary.IsBinary);
                AssertEqual(GitChangeKindEnum.Added, binary.Kind);
                AssertNull(binary.AddedLines);
                AssertEqual(2, commit.AddedLines, "binary counts as zero; notes.txt adds two");
                AssertEqual(0, commit.DeletedLines);
            }));

            cases.Add(CaseAsync("log_files_cap_truncates", "A commit with more than 200 files lists 200, counts all of them, and sets FilesTruncated", TestTags.Positive, async () =>
            {
                string repo = DatedGitRepo.Create();
                DatedGitRepo.Commit(repo, "seed", At("2026-06-01T10:00:00Z"), "seed.txt", "s\n");
                for (int i = 0; i < 205; i++) File.WriteAllText(Path.Combine(repo, "f" + i.ToString("D3") + ".txt"), "line\n");
                DatedGitRepo.Git(repo, null, "add", "-A");
                DatedGitRepo.Git(repo, At("2026-06-02T10:00:00Z"), "commit", "-q", "-m", "many files");

                GitService git = CreateService();
                string tip = (await git.ResolveBranchTipAsync(repo, "main").ConfigureAwait(false))!;
                GitCommitLogPage page = await git.GetCommitLogAsync(repo, tip, null, 0, 10).ConfigureAwait(false);
                VesselCommit many = page.Commits[0];
                AssertEqual(205, many.FilesChanged);
                AssertEqual(Constants.VesselHistoryMaxFilesPerCommit, many.Files.Count);
                AssertTrue(many.FilesTruncated);
                AssertEqual(205, many.AddedLines, "totals cover every file, listed or not");
                AssertFalse(page.Commits[1].FilesTruncated);
            }));

            cases.Add(CaseAsync("log_until_bound_is_inclusive_seconds", "The until bound (before minus one second) keeps commits at or before it and drops later ones", TestTags.Positive, async () =>
            {
                string repo = DatedGitRepo.Create();
                DatedGitRepo.Commit(repo, "c1", At("2026-07-01T10:00:00Z"), "a.txt", "1\n");
                DatedGitRepo.Commit(repo, "c2", At("2026-07-02T10:00:00Z"), "a.txt", "2\n");
                DatedGitRepo.Commit(repo, "c3", At("2026-07-03T10:00:00Z"), "a.txt", "3\n");
                GitService git = CreateService();
                string tip = (await git.ResolveBranchTipAsync(repo, "main").ConfigureAwait(false))!;
                long c2 = At("2026-07-02T10:00:00Z").ToUnixTimeSeconds();

                GitCommitLogPage strictly = await git.GetCommitLogAsync(repo, tip, c2 - 1, 0, 50).ConfigureAwait(false);
                AssertEqual("c1", Subjects(strictly.Commits), "before c2's instant excludes c2");
                GitCommitLogPage inclusive = await git.GetCommitLogAsync(repo, tip, c2, 0, 50).ConfigureAwait(false);
                AssertEqual("c2,c1", Subjects(inclusive.Commits));
                GitCommitLogPage none = await git.GetCommitLogAsync(repo, tip, At("2020-01-01T00:00:00Z").ToUnixTimeSeconds(), 0, 50).ConfigureAwait(false);
                AssertEqual(0, none.Commits.Count);
                AssertFalse(none.HasMore);
            }));

            cases.Add(CaseAsync("log_paging_stable_when_new_commit_lands", "Paging from the pinned tip is unaffected by a commit that lands after the first page", TestTags.Reliability, async () =>
            {
                string repo = DatedGitRepo.Create();
                for (int i = 1; i <= 5; i++) DatedGitRepo.Commit(repo, "c" + i, At("2026-08-0" + i + "T10:00:00Z"), "a.txt", i + "\n");
                GitService git = CreateService();
                string tip = (await git.ResolveBranchTipAsync(repo, "main").ConfigureAwait(false))!;

                GitCommitLogPage first = await git.GetCommitLogAsync(repo, tip, null, 0, 2).ConfigureAwait(false);
                AssertEqual("c5,c4", Subjects(first.Commits));
                AssertTrue(first.HasMore);

                DatedGitRepo.Commit(repo, "c6", At("2026-08-06T10:00:00Z"), "a.txt", "6\n");
                AssertNotEqual(tip, await git.ResolveBranchTipAsync(repo, "main").ConfigureAwait(false), "the branch moved");

                GitCommitLogPage second = await git.GetCommitLogAsync(repo, tip, null, 2, 2).ConfigureAwait(false);
                AssertEqual("c3,c2", Subjects(second.Commits), "no duplicate and no gap");
                AssertTrue(second.HasMore);
                GitCommitLogPage third = await git.GetCommitLogAsync(repo, tip, null, 4, 2).ConfigureAwait(false);
                AssertEqual("c1", Subjects(third.Commits));
                AssertFalse(third.HasMore, "last page");

                await AssertThrowsAsync<ArgumentException>(() => git.GetCommitLogAsync(repo, "main", null, 0, 2));
                await AssertThrowsAsync<ArgumentOutOfRangeException>(() => git.GetCommitLogAsync(repo, tip, null, -1, 2));
                await AssertThrowsAsync<ArgumentOutOfRangeException>(() => git.GetCommitLogAsync(repo, tip, null, 0, 0));
                await AssertThrowsAsync<InvalidOperationException>(() => git.GetCommitLogAsync(repo, new string('0', 40), null, 0, 2));
            }));

            cases.Add(CaseAsync("bare_repository_history", "Activity and pages work on a bare clone (the vessel's LocalPath)", TestTags.Positive, async () =>
            {
                string repo = DatedGitRepo.Create();
                DatedGitRepo.Commit(repo, "c1", At("2026-09-01T10:00:00Z"), "a.txt", "1\n");
                DatedGitRepo.Commit(repo, "c2", At("2026-09-02T10:00:00Z"), "a.txt", "2\n");
                string bare = Path.Combine(TestTemp.NewDirectory("git-history-bare"), "repo.git");
                DatedGitRepo.Git(null, null, "clone", "-q", "--bare", repo, bare);
                GitService git = CreateService();
                AssertTrue(await git.IsBareRepositoryAsync(bare).ConfigureAwait(false));

                VesselCommitActivity activity = await git.GetCommitActivityAsync(bare, "main", Day("2026-09-01"), Day("2026-09-02"), 0).ConfigureAwait(false);
                AssertNull(activity.Error);
                AssertEqual(2, activity.TotalCommits);
                string tip = (await git.ResolveBranchTipAsync(bare, "main").ConfigureAwait(false))!;
                GitCommitLogPage page = await git.GetCommitLogAsync(bare, tip, null, 0, 1).ConfigureAwait(false);
                AssertEqual("c2", page.Commits.Single().Subject);
                AssertTrue(page.HasMore);
                AssertEqual(1, page.Commits[0].AddedLines);

                // A working tree whose branch exists only as a remote-tracking ref resolves through origin.
                string clone = Path.Combine(TestTemp.NewDirectory("git-history-clone"), "work");
                DatedGitRepo.Git(null, null, "clone", "-q", bare, clone);
                DatedGitRepo.Git(bare, null, "branch", "only-remote", "main");
                DatedGitRepo.Git(clone, null, "fetch", "-q", "origin");
                AssertEqual(tip, await git.ResolveBranchTipAsync(clone, "only-remote").ConfigureAwait(false));
            }));

            cases.Add(Case("cursor_roundtrip_and_malformed", "The cursor round-trips through base64url JSON and rejects malformed, tampered, or out-of-range payloads", TestTags.Negative, () =>
            {
                VesselCommitCursor cursor = new VesselCommitCursor { Branch = "feature/x", Tip = new string('a', 40), Until = 1700000000, Skip = 150 };
                string encoded = cursor.Encode();
                AssertTrue(encoded.All(c => Char.IsLetterOrDigit(c) || c == '-' || c == '_'), "base64url without padding");
                AssertTrue(VesselCommitCursor.TryDecode(encoded, out VesselCommitCursor? decoded));
                AssertEqual("feature/x", decoded!.Branch);
                AssertEqual(new string('a', 40), decoded.Tip);
                AssertEqual(1700000000L, decoded.Until);
                AssertEqual(150, decoded.Skip);

                VesselCommitCursor noUntil = new VesselCommitCursor { Branch = "main", Tip = new string('b', 64), Skip = 0 };
                AssertTrue(VesselCommitCursor.TryDecode(noUntil.Encode(), out VesselCommitCursor? sha256));
                AssertNull(sha256!.Until);

                string[] bad = new[]
                {
                    "",
                    "!!!!",
                    "abc=",
                    "a",
                    Base64Url("not json"),
                    Base64Url("null"),
                    Base64Url("[]"),
                    Base64Url("{\"V\":2,\"Branch\":\"main\",\"Tip\":\"" + new string('a', 40) + "\",\"Skip\":0}"),
                    Base64Url("{\"V\":1,\"Branch\":\"main\",\"Tip\":\"HEAD\",\"Skip\":0}"),
                    Base64Url("{\"V\":1,\"Branch\":\"main\",\"Tip\":\"" + new string('A', 40) + "\",\"Skip\":0}"),
                    Base64Url("{\"V\":1,\"Branch\":\"main\",\"Tip\":\"" + new string('a', 40) + "\",\"Skip\":-1}"),
                    Base64Url("{\"V\":1,\"Branch\":\"--upload-pack=x\",\"Tip\":\"" + new string('a', 40) + "\",\"Skip\":0}"),
                    Base64Url("{\"V\":1,\"Branch\":\"\",\"Tip\":\"" + new string('a', 40) + "\",\"Skip\":0}"),
                    Base64Url("{\"V\":1,\"Branch\":\"main\",\"Tip\":\"" + new string('a', 40) + "\",\"Skip\":\"x\"}"),
                    Base64Url("{\"V\":1,\"Branch\":\"main\",\"Tip\":\"" + new string('a', 40) + "\",\"Skip\":0,\"Until\":999999999999999}"),
                    new string('A', VesselCommitCursor.MaxEncodedLength + 4)
                };
                foreach (string text in bad)
                {
                    AssertFalse(VesselCommitCursor.TryDecode(text, out VesselCommitCursor? rejected), "rejected: " + (text.Length > 40 ? text.Substring(0, 40) : text));
                    AssertNull(rejected);
                }
                AssertFalse(VesselCommitCursor.TryDecode(null, out _));
            }));

            cases.Add(Case("branch_name_validation", "Branch names that git could read as options or that git forbids are rejected", TestTags.Negative, () =>
            {
                foreach (string ok in new[] { "main", "feature/x", "release-1.0", "armada/msn_abc", "HEAD" })
                    AssertTrue(GitRevisionNames.IsSafeBranchName(ok), ok);
                foreach (string bad in new[] { "", "-x", "--all", "/x", "x/", "a..b", "a b", "a~1", "a^", "a:b", "a?", "a*", "a[", "a\\b", "x@{1}", "a//b", "a.", "a\tb", new string('a', 256) })
                    AssertFalse(GitRevisionNames.IsSafeBranchName(bad), "rejected: " + bad);
                AssertTrue(GitRevisionNames.IsFullCommitId(new string('f', 40)));
                AssertFalse(GitRevisionNames.IsFullCommitId(new string('f', 39)));
                AssertFalse(GitRevisionNames.IsFullCommitId(new string('g', 40)));
            }));

            cases.Add(Case("parser_reads_paths_that_look_like_headers", "The log parser reads a path starting with the header separator as a path, and an empty commit as a commit with no files", TestTags.Negative, () =>
            {
                string sha1 = new string('1', 40);
                string sha2 = new string('2', 40);
                StringBuilder output = new StringBuilder();
                output.Append('\u001e').Append(sha1).Append("\u001f1111111\u001f").Append(sha2).Append("\u001fA\u001fa@x\u001f1700000000\u001fC\u001fc@x\u001f1700000100\u001fSubject\u001f\0");
                output.Append("\n:100644 100644 abc def M\0").Append("\u001eweird.txt\0");
                output.Append("3\t1\t\u001eweird.txt\0");
                output.Append('\u001e').Append(sha2).Append("\u001f2222222\u001f\u001fA\u001fa@x\u001f1600000000\u001fC\u001fc@x\u001f1600000000\u001fEmpty\u001fBody text\n\0");
                List<VesselCommit> commits = GitMachineOutputParser.ParseCommitLogZ(output.ToString(), 200);
                AssertEqual(2, commits.Count);
                AssertEqual("\u001eweird.txt", commits[0].Files.Single().Path);
                AssertEqual(3, commits[0].AddedLines);
                AssertEqual(1, commits[0].DeletedLines);
                AssertEqual(DateTimeOffset.FromUnixTimeSeconds(1700000100).UtcDateTime, commits[0].CommittedUtc);
                AssertEqual(0, commits[1].FilesChanged);
                AssertEqual("Body text", commits[1].Body);
                AssertEqual(0, commits[1].ParentShas.Count);
                AssertEqual(0, GitMachineOutputParser.ParseCommitLogZ("", 200).Count);
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Git history (vessel commit activity and pages)", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static GitService CreateService()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return new GitService(logging);
        }

        private static DateTimeOffset At(string iso)
        {
            return DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal);
        }

        private static DateTime Day(string day)
        {
            return DateTime.ParseExact(day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static int CountOn(VesselCommitActivity activity, string day)
        {
            return activity.Days.Single(d => d.Date == day).Count;
        }

        private static string Subjects(List<VesselCommit> commits)
        {
            return String.Join(",", commits.Select(c => c.Subject));
        }

        private static string Base64Url(string text)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
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
