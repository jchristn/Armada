namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="WorkspaceService"/>: safe browsing/editing over a vessel working
    /// directory. Positive cases cover search that skips hidden directories; negative cases cover a
    /// missing working directory, stale optimistic-concurrency saves, argument validation, and
    /// path-escape rejection (.git access and parent traversal).
    /// </summary>
    public sealed class WorkspaceServiceSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Workspace Service suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("get_status_async_reports_missing_working_directory_cleanly", "GetStatusAsync reports missing working directory cleanly", TestTags.Negative, async () =>
            {
                WorkspaceService service = new WorkspaceService();
                Vessel vessel = new Vessel
                {
                    Id = "vsl_workspace_missing",
                    Name = "Workspace Missing",
                    WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada-missing-" + Guid.NewGuid().ToString("N"))
                };

                WorkspaceStatusResult result = await service.GetStatusAsync(vessel).ConfigureAwait(false);
                AssertFalse(result.HasWorkingDirectory);
                AssertContains("No working directory configured or directory does not exist.", result.Error ?? String.Empty);
            }));

            cases.Add(CaseAsync("save_file_async_rejects_stale_optimistic_concurrency_hash", "SaveFileAsync rejects stale optimistic concurrency hash", TestTags.Negative, async () =>
            {
                string root = Path.Combine(Path.GetTempPath(), "armada-workspace-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);

                try
                {
                    string filePath = Path.Combine(root, "notes.txt");
                    await File.WriteAllTextAsync(filePath, "original").ConfigureAwait(false);

                    WorkspaceService service = new WorkspaceService();
                    Vessel vessel = CreateVessel(root);
                    WorkspaceFileResponse initial = await service.GetFileAsync(vessel, "notes.txt").ConfigureAwait(false);

                    await File.WriteAllTextAsync(filePath, "changed externally").ConfigureAwait(false);

                    await AssertThrowsAsync<WorkspaceConflictException>(() => service.SaveFileAsync(vessel, new WorkspaceSaveRequest
                    {
                        Path = "notes.txt",
                        Content = "new content",
                        ExpectedHash = initial.ContentHash
                    })).ConfigureAwait(false);
                }
                finally
                {
                    TryDeleteDirectory(root);
                }
            }));

            cases.Add(CaseAsync("search_async_skips_hidden_workspace_directories", "SearchAsync skips hidden workspace directories", TestTags.Positive, async () =>
            {
                string root = Path.Combine(Path.GetTempPath(), "armada-workspace-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);

                try
                {
                    Directory.CreateDirectory(Path.Combine(root, "src"));
                    Directory.CreateDirectory(Path.Combine(root, ".git"));
                    await File.WriteAllTextAsync(Path.Combine(root, "src", "visible.txt"), "Workspace visible token").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(root, ".git", "hidden.txt"), "Workspace hidden token").ConfigureAwait(false);

                    WorkspaceService service = new WorkspaceService();
                    Vessel vessel = CreateVessel(root);
                    WorkspaceSearchResult result = await service.SearchAsync(vessel, "Workspace").ConfigureAwait(false);

                    AssertEqual(1, result.TotalMatches);
                    AssertEqual("src/visible.txt", result.Matches[0].Path);
                }
                finally
                {
                    TryDeleteDirectory(root);
                }
            }));

            cases.Add(CaseAsync("get_status_async_null_vessel_throws", "GetStatusAsync NullVessel Throws", TestTags.Negative, async () =>
            {
                WorkspaceService service = new WorkspaceService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.GetStatusAsync(null!)).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("save_file_async_null_request_throws", "SaveFileAsync NullRequest Throws", TestTags.Negative, async () =>
            {
                WorkspaceService service = new WorkspaceService();
                Vessel vessel = CreateVessel(Path.GetTempPath());
                await AssertThrowsAsync<ArgumentNullException>(() => service.SaveFileAsync(vessel, null!)).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("save_file_async_empty_path_throws", "SaveFileAsync EmptyPath Throws", TestTags.Negative, async () =>
            {
                WorkspaceService service = new WorkspaceService();
                Vessel vessel = CreateVessel(Path.GetTempPath());
                await AssertThrowsAsync<ArgumentException>(() => service.SaveFileAsync(vessel, new WorkspaceSaveRequest
                {
                    Path = "",
                    Content = "content"
                })).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("search_async_empty_query_throws", "SearchAsync EmptyQuery Throws", TestTags.Negative, async () =>
            {
                WorkspaceService service = new WorkspaceService();
                Vessel vessel = CreateVessel(Path.GetTempPath());
                await AssertThrowsAsync<ArgumentException>(() => service.SearchAsync(vessel, "")).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("delete_async_empty_path_throws", "DeleteAsync EmptyPath Throws", TestTags.Negative, async () =>
            {
                WorkspaceService service = new WorkspaceService();
                Vessel vessel = CreateVessel(Path.GetTempPath());
                await AssertThrowsAsync<ArgumentException>(() => service.DeleteAsync(vessel, "")).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("get_file_async_rejects_dot_git_path", "GetFileAsync RejectsDotGitPath", TestTags.Negative, async () =>
            {
                string root = Path.Combine(Path.GetTempPath(), "armada-workspace-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);

                try
                {
                    WorkspaceService service = new WorkspaceService();
                    Vessel vessel = CreateVessel(root);
                    await AssertThrowsAsync<UnauthorizedAccessException>(() => service.GetFileAsync(vessel, ".git/config")).ConfigureAwait(false);
                }
                finally
                {
                    TryDeleteDirectory(root);
                }
            }));

            cases.Add(CaseAsync("get_file_async_rejects_parent_traversal", "GetFileAsync RejectsParentTraversal", TestTags.Negative, async () =>
            {
                string root = Path.Combine(Path.GetTempPath(), "armada-workspace-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);

                try
                {
                    WorkspaceService service = new WorkspaceService();
                    Vessel vessel = CreateVessel(root);
                    await AssertThrowsAsync<UnauthorizedAccessException>(() => service.GetFileAsync(vessel, "../escape.txt")).ConfigureAwait(false);
                }
                finally
                {
                    TryDeleteDirectory(root);
                }
            }));


            cases.Add(CaseAsync("get_changes_async_reads_porcelain_v2", "GetChangesAsync reports renames, arrow-named files, branch, and ahead count from porcelain v2", TestTags.Positive, async () =>
            {
                string bareDir = TestGitRepoHelper.CreateBareRepoCopy();
                string root = TestTemp.NewDirectory("workspace-changes");
                string cloneDir = Path.Combine(root, "clone");
                try
                {
                    RunGit(root, "clone", "-q", bareDir, cloneDir);
                    RunGit(cloneDir, "config", "user.name", "Armada Tests");
                    RunGit(cloneDir, "config", "user.email", "armada-tests@example.com");
                    await File.WriteAllTextAsync(Path.Combine(cloneDir, "local.txt"), "local\n").ConfigureAwait(false);
                    RunGit(cloneDir, "add", "local.txt");
                    RunGit(cloneDir, "commit", "-q", "-m", "local commit");

                    RunGit(cloneDir, "mv", "README.md", "READ ME.md");
                    // Porcelain v1 prints this untracked name unquoted, and its " -> " was read as a rename.
                    // Windows does not allow ">" in file names; there the unusual untracked name has spaces and non-ASCII instead.
                    string unusualName = OperatingSystem.IsWindows() ? "caf\u00e9 a - b.txt" : "a -> b.txt";
                    await File.WriteAllTextAsync(Path.Combine(cloneDir, unusualName), "x\n").ConfigureAwait(false);

                    WorkspaceService service = new WorkspaceService();
                    WorkspaceChangesResult result = await service.GetChangesAsync(CreateVessel(cloneDir)).ConfigureAwait(false);

                    AssertNull(result.Error, "status should succeed");
                    AssertEqual("main", result.BranchName);
                    AssertEqual(1, result.CommitsAhead);
                    AssertEqual(0, result.CommitsBehind);
                    AssertTrue(result.IsDirty, "working tree is dirty");
                    AssertEqual(2, result.Changes.Count);

                    WorkspaceChangeEntry? rename = result.Changes.Find(c => c.Path == "READ ME.md");
                    AssertNotNull(rename, "rename target reported");
                    AssertEqual("R", rename!.Status);
                    AssertEqual("README.md", rename.OriginalPath);

                    WorkspaceChangeEntry? arrow = result.Changes.Find(c => c.Path == unusualName);
                    AssertNotNull(arrow, "arrow-named untracked file reported under its full name");
                    AssertEqual("??", arrow!.Status);
                    AssertNull(arrow.OriginalPath);
                }
                finally
                {
                    TestTemp.TryDelete(root);
                    TestTemp.TryDelete(bareDir);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.WorkspaceService",
                displayName: "Workspace Service",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static void RunGit(string workingDirectory, params string[] args)
        {
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (string arg in args) startInfo.ArgumentList.Add(arg);
            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!;
            string stderr = process.StandardError.ReadToEnd();
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException("git failed (exit " + process.ExitCode + "): " + stderr);
        }

        private static Vessel CreateVessel(string workingDirectory)
        {
            return new Vessel
            {
                Id = "vsl_workspace",
                Name = "Workspace Vessel",
                WorkingDirectory = workingDirectory
            };
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // ignore temp cleanup failures
            }
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.WorkspaceService",
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
                suiteId: "Services.WorkspaceService",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
