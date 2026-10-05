namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the structured git output readers: <see cref="UnifiedDiffParser"/> (hunk-range parsing),
    /// <see cref="GitMachineOutputParser"/> (-z formats), <see cref="GitPathUnquoter"/>, <see cref="DiffChangeSummary"/>,
    /// and <see cref="GitProcessEnvironment"/>. Cases include inputs that a header-text parser misreads.
    /// </summary>
    public sealed class GitStructuredOutputSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the structured git output suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("diff_counts_header_like_lines_as_content", "Hunk lines rendered as +++/--- are counted as content", TestTags.Positive, () =>
            {
                string diff =
                    "diff --git a/a.txt b/a.txt\n" +
                    "index 1111111..2222222 100644\n" +
                    "--- a/a.txt\n" +
                    "+++ b/a.txt\n" +
                    "@@ -1,3 +1,3 @@\n" +
                    " keep\n" +
                    "--- b/evil.txt\n" +
                    "-plain removed\n" +
                    "+++ b/evil.txt\n" +
                    "+plain added\n";
                List<UnifiedDiffFile> files = UnifiedDiffParser.Parse(diff);
                AssertEqual(1, files.Count);
                AssertEqual("a.txt", files[0].NewPath);
                AssertEqual(2, files[0].AddedLineCount);
                AssertEqual(2, files[0].DeletedLineCount);
                AssertEqual("++ b/evil.txt", files[0].AddedLines[0].Content);
                AssertEqual(2, files[0].AddedLines[0].NewLineNumber);
                AssertEqual(3, files[0].AddedLines[1].NewLineNumber);
            }));

            cases.Add(Case("diff_multiple_files_and_kinds", "Added, deleted, renamed, binary, and mode-only sections are typed", TestTags.Positive, () =>
            {
                string diff =
                    "diff --git a/new.txt b/new.txt\n" +
                    "new file mode 100644\n" +
                    "index 0000000..1111111\n" +
                    "--- /dev/null\n" +
                    "+++ b/new.txt\n" +
                    "@@ -0,0 +1 @@\n" +
                    "+hello\n" +
                    "\\ No newline at end of file\n" +
                    "diff --git a/old.txt b/old.txt\n" +
                    "deleted file mode 100644\n" +
                    "index 1111111..0000000\n" +
                    "--- a/old.txt\n" +
                    "+++ /dev/null\n" +
                    "@@ -1,2 +0,0 @@\n" +
                    "-x\n" +
                    "-y\n" +
                    "diff --git a/dir one/f.txt b/dir two/f.txt\n" +
                    "similarity index 100%\n" +
                    "rename from dir one/f.txt\n" +
                    "rename to dir two/f.txt\n" +
                    "diff --git a/img.png b/img.png\n" +
                    "index 1111111..2222222 100644\n" +
                    "Binary files a/img.png and b/img.png differ\n" +
                    "diff --git a/run.sh b/run.sh\n" +
                    "old mode 100644\n" +
                    "new mode 100755\n";
                List<UnifiedDiffFile> files = UnifiedDiffParser.Parse(diff);
                AssertEqual(5, files.Count);
                AssertEqual(GitChangeKindEnum.Added, files[0].Kind);
                AssertNull(files[0].OldPath);
                AssertEqual(1, files[0].AddedLineCount);
                AssertEqual(GitChangeKindEnum.Deleted, files[1].Kind);
                AssertEqual("old.txt", files[1].OldPath);
                AssertNull(files[1].NewPath);
                AssertEqual(2, files[1].DeletedLineCount);
                AssertEqual(GitChangeKindEnum.Renamed, files[2].Kind);
                AssertEqual("dir one/f.txt", files[2].OldPath);
                AssertEqual("dir two/f.txt", files[2].NewPath);
                AssertTrue(files[3].IsBinary, "binary section should be flagged");
                AssertEqual("img.png", files[3].NewPath);
                AssertEqual(GitChangeKindEnum.Modified, files[4].Kind);
                AssertEqual("run.sh", files[4].NewPath);

                DiffChangeSummary summary = DiffChangeSummary.FromUnifiedDiff(files);
                AssertEqual(5, summary.FileCount);
                AssertEqual(3, summary.ChangedLineCount);
                AssertTrue(summary.ChangedPaths.Contains("old.txt"), "deleted path listed");
                AssertTrue(summary.ChangedPaths.Contains("dir one/f.txt"), "rename source listed");
                AssertTrue(summary.ChangedPaths.Contains("dir two/f.txt"), "rename target listed");
            }));

            cases.Add(Case("diff_unquotes_c_quoted_paths", "C-quoted paths in headers are decoded", TestTags.Positive, () =>
            {
                string diff =
                    "diff --git \"a/caf\\303\\251 \\\"x\\\".txt\" \"b/caf\\303\\251 \\\"x\\\".txt\"\n" +
                    "index 1111111..2222222 100644\n" +
                    "--- \"a/caf\\303\\251 \\\"x\\\".txt\"\n" +
                    "+++ \"b/caf\\303\\251 \\\"x\\\".txt\"\n" +
                    "@@ -1 +1 @@\n" +
                    "-a\n" +
                    "+b\n";
                List<UnifiedDiffFile> files = UnifiedDiffParser.Parse(diff);
                AssertEqual(1, files.Count);
                AssertEqual("caf\u00e9 \"x\".txt", files[0].OldPath);
                AssertEqual("caf\u00e9 \"x\".txt", files[0].NewPath);
            }));

            cases.Add(Case("unquote_handles_escapes_and_plain_input", "GitPathUnquoter decodes escapes and passes plain text through", TestTags.Positive, () =>
            {
                AssertEqual("plain/path.txt", GitPathUnquoter.Unquote("plain/path.txt"));
                AssertEqual("tab\there", GitPathUnquoter.Unquote("\"tab\\there\""));
                AssertEqual("back\\slash", GitPathUnquoter.Unquote("\"back\\\\slash\""));
                AssertEqual("\u00e9", GitPathUnquoter.Unquote("\"\\303\\251\""));
            }));

            cases.Add(Case("empty_diff_yields_empty_summary", "Null or empty diff yields an empty summary", TestTags.Negative, () =>
            {
                AssertEqual(0, UnifiedDiffParser.Parse(null).Count);
                DiffChangeSummary summary = DiffChangeSummary.FromUnifiedDiff(String.Empty);
                AssertEqual(0, summary.FileCount);
                AssertEqual(0, summary.ChangedLineCount);
                AssertEqual(0, summary.ChangedPaths.Count);
            }));

            cases.Add(Case("name_status_z_parses_rename_and_newline_path", "--name-status -z keeps verbatim paths, including newlines", TestTags.Positive, () =>
            {
                string output = "M\0src/a b.cs\0D\0line\nbreak.txt\0R087\0old.cs\0new.cs\0A\0caf\u00e9.txt\0";
                List<GitChangedFile> files = GitMachineOutputParser.ParseNameStatusZ(output);
                AssertEqual(4, files.Count);
                AssertEqual(GitChangeKindEnum.Modified, files[0].Kind);
                AssertEqual("src/a b.cs", files[0].Path);
                AssertEqual(GitChangeKindEnum.Deleted, files[1].Kind);
                AssertEqual("line\nbreak.txt", files[1].Path);
                AssertEqual(GitChangeKindEnum.Renamed, files[2].Kind);
                AssertEqual("old.cs", files[2].OldPath);
                AssertEqual("new.cs", files[2].Path);
                AssertEqual("caf\u00e9.txt", files[3].Path);
            }));

            cases.Add(Case("numstat_z_parses_counts_binary_and_rename", "--numstat -z reports counts, binary files, and renames", TestTags.Positive, () =>
            {
                string output = "3\t1\tsrc/a.cs\0-\t-\timg.png\0" + "2\t0\t\0old name.cs\0new name.cs\0";
                List<GitChangedFile> files = GitMachineOutputParser.ParseNumstatZ(output);
                AssertEqual(3, files.Count);
                AssertEqual(3, files[0].AddedLines);
                AssertEqual(1, files[0].DeletedLines);
                AssertTrue(files[1].IsBinary, "binary flagged");
                AssertNull(files[1].AddedLines);
                AssertEqual("old name.cs", files[2].OldPath);
                AssertEqual("new name.cs", files[2].Path);

                List<GitChangedFile> merged = GitMachineOutputParser.MergeNameStatusAndNumstat(
                    GitMachineOutputParser.ParseNameStatusZ("M\0src/a.cs\0D\0gone.txt\0"),
                    GitMachineOutputParser.ParseNumstatZ("3\t1\tsrc/a.cs\0" + "0\t4\tgone.txt\0"));
                DiffChangeSummary summary = DiffChangeSummary.FromGitChanges(merged);
                AssertEqual(2, summary.FileCount);
                AssertEqual(8, summary.ChangedLineCount);
                AssertEqual(GitChangeKindEnum.Deleted, merged[1].Kind);
            }));

            cases.Add(Case("worktree_list_parses_both_forms", "worktree list --porcelain parses in -z and newline forms", TestTags.Positive, () =>
            {
                string nul = "worktree /repo/bare.git\0bare\0\0worktree /docks/a b\0HEAD abc\0branch refs/heads/armada/x\0\0worktree /docks/c\0HEAD def\0detached\0\0";
                List<GitWorktreeEntry> entries = GitMachineOutputParser.ParseWorktreeList(nul, true);
                AssertEqual(3, entries.Count);
                AssertTrue(entries[0].IsBare, "bare entry");
                AssertEqual("/docks/a b", entries[1].Path);
                AssertEqual("refs/heads/armada/x", entries[1].BranchRef);
                AssertTrue(entries[2].IsDetached, "detached entry");

                string lines = "worktree /repo\nHEAD abc\nbranch refs/heads/main\n\nworktree /docks/a\nHEAD def\nbranch refs/heads/feature\n";
                List<GitWorktreeEntry> fromLines = GitMachineOutputParser.ParseWorktreeList(lines, false);
                AssertEqual(2, fromLines.Count);
                AssertEqual("refs/heads/feature", fromLines[1].BranchRef);
            }));

            cases.Add(Case("summary_combine_is_conservative", "Combining summaries unions paths and keeps the larger counts", TestTags.Positive, () =>
            {
                DiffChangeSummary first = new DiffChangeSummary { FileCount = 1, ChangedLineCount = 10, ChangedPaths = new List<string> { "a.cs" } };
                DiffChangeSummary second = new DiffChangeSummary { FileCount = 2, ChangedLineCount = 4, ChangedPaths = new List<string> { "a.cs", "b.cs" } };
                DiffChangeSummary combined = DiffChangeSummary.Combine(first, second);
                AssertEqual(2, combined.FileCount);
                AssertEqual(10, combined.ChangedLineCount);
                AssertEqual(2, combined.ChangedPaths.Count);
            }));

            cases.Add(Case("process_environment_sets_c_locale", "Git and gh launches use the C locale", TestTags.Positive, () =>
            {
                ProcessStartInfo startInfo = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true };
                startInfo.Environment["LANGUAGE"] = "de";
                GitProcessEnvironment.Apply(startInfo);
                AssertEqual("C", startInfo.Environment["LC_ALL"]);
                AssertEqual("C", startInfo.Environment["LANG"]);
                AssertFalse(startInfo.Environment.ContainsKey("LANGUAGE"), "LANGUAGE should be cleared");
                AssertTrue(GitProcessEnvironment.IsGitTool("git"), "git");
                AssertTrue(GitProcessEnvironment.IsGitTool("/usr/bin/gh"), "gh path");
                AssertTrue(GitProcessEnvironment.IsGitTool("C:\\Program Files\\Git\\cmd\\git.exe"), "git.exe");
                AssertFalse(GitProcessEnvironment.IsGitTool("dotnet"), "dotnet");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.GitStructuredOutput",
                displayName: "Git Structured Output",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.GitStructuredOutput",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
