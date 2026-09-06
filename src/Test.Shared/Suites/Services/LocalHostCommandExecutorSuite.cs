namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="LocalHostCommandExecutor"/> exercised against real git. Positive cases run
    /// a known-good command and confirm exit code and captured output; negative cases confirm a failing
    /// subcommand yields a non-zero exit with captured stderr rather than throwing. These prove the
    /// host-command seam that Split mode will mirror over the Harbor link.
    /// </summary>
    public sealed class LocalHostCommandExecutorSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the LocalHostCommandExecutor suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("git_version_succeeds", "RunAsync captures a successful git command", TestTags.Positive, async () =>
            {
                LocalHostCommandExecutor executor = new LocalHostCommandExecutor();
                HostCommandResult result = await executor.RunAsync(new HostCommandRequest
                {
                    Executable = "git",
                    Arguments = new List<string> { "--version" }
                }).ConfigureAwait(false);

                AssertTrue(result.Success, "Expected git --version to succeed.");
                AssertEqual(0, result.ExitCode);
                AssertContains("git version", result.StandardOutput);
            }));

            cases.Add(CaseAsync("git_init_in_workdir", "RunAsync runs in the requested working directory", TestTags.Positive, async () =>
            {
                string dir = CreateTempDir("hostcmd-init");
                try
                {
                    LocalHostCommandExecutor executor = new LocalHostCommandExecutor();
                    HostCommandResult result = await executor.RunAsync(new HostCommandRequest
                    {
                        Executable = "git",
                        WorkingDirectory = dir,
                        Arguments = new List<string> { "init" }
                    }).ConfigureAwait(false);

                    AssertTrue(result.Success, "Expected git init to succeed.");
                    AssertTrue(Directory.Exists(Path.Combine(dir, ".git")), "Expected a .git directory to be created in the working directory.");
                }
                finally
                {
                    TryDeleteDir(dir);
                }
            }));

            cases.Add(CaseAsync("failing_subcommand_nonzero_exit", "RunAsync returns a non-zero exit for a failing command", TestTags.Negative, async () =>
            {
                LocalHostCommandExecutor executor = new LocalHostCommandExecutor();
                HostCommandResult result = await executor.RunAsync(new HostCommandRequest
                {
                    Executable = "git",
                    Arguments = new List<string> { "definitely-not-a-command" }
                }).ConfigureAwait(false);

                AssertTrue(!result.Success, "Expected an unknown git subcommand to fail.");
                AssertTrue(result.ExitCode != 0, "Expected a non-zero exit code.");
            }));

            cases.Add(CaseAsync("null_request_throws", "RunAsync rejects a null request", TestTags.Negative, async () =>
            {
                LocalHostCommandExecutor executor = new LocalHostCommandExecutor();
                await AssertThrowsAsync<ArgumentNullException>(() => executor.RunAsync(null!));
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.LocalHostCommandExecutor",
                displayName: "Local Host Command Executor",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string CreateTempDir(string prefix)
        {
            string path = Path.Combine(Path.GetTempPath(), "armada-" + prefix + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void TryDeleteDir(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch
            {
            }
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.LocalHostCommandExecutor",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
