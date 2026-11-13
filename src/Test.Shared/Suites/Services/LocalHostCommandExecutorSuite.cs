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

            cases.Add(CaseAsync("git_command_stdout_captured", "RunAsync captures a successful git command's exit code and stdout", TestTags.Positive, async () =>
            {
                // A command whose output the test fully controls (a config value set on the command line), so the
                // check does not depend on git's wording, version, or the host locale.
                LocalHostCommandExecutor executor = new LocalHostCommandExecutor();
                HostCommandResult result = await executor.RunAsync(new HostCommandRequest
                {
                    Executable = "git",
                    Arguments = new List<string> { "-c", "armada.probe=hostcmd-ok", "config", "--get", "armada.probe" }
                }).ConfigureAwait(false);

                AssertTrue(result.Success, "Expected git config --get to succeed.");
                AssertEqual(0, result.ExitCode);
                AssertEqual("hostcmd-ok", result.StandardOutput.Trim(), "stdout captured verbatim");
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

            cases.Add(CaseAsync("nul_separated_output_returned_unchanged", "RunAsync returns NUL-terminated git output without an added line ending", TestTags.Negative, async () =>
            {
                string dir = await CreateRepoWithFilesAsync("hostcmd-nul").ConfigureAwait(false);
                try
                {
                    LocalHostCommandExecutor executor = new LocalHostCommandExecutor();
                    HostCommandResult result = await executor.RunAsync(new HostCommandRequest
                    {
                        Executable = "git",
                        WorkingDirectory = dir,
                        Arguments = new List<string> { "ls-files", "-z" }
                    }).ConfigureAwait(false);

                    AssertTrue(result.Success, "Expected git ls-files -z to succeed: " + result.StandardError);
                    AssertEqual("a.txt\0b.txt\0", result.StandardOutput, "stdout is exactly what git wrote");
                    AssertEqual(2, GitMachineOutputParser.ParsePathListZ(result.StandardOutput).Count, "two paths, no phantom empty path");
                }
                finally
                {
                    TryDeleteDir(dir);
                }
            }));

            cases.Add(CaseAsync("line_output_not_normalized", "RunAsync keeps the command's own line ending and adds none", TestTags.Positive, async () =>
            {
                LocalHostCommandExecutor executor = new LocalHostCommandExecutor();
                HostCommandResult result = await executor.RunAsync(new HostCommandRequest
                {
                    Executable = "git",
                    Arguments = new List<string> { "-c", "armada.probe=hostcmd-raw", "config", "--get", "armada.probe" }
                }).ConfigureAwait(false);

                AssertTrue(result.Success, "Expected git config --get to succeed.");
                AssertEqual("hostcmd-raw\n", result.StandardOutput, "git's LF is returned as written");
            }));

            cases.Add(CaseAsync("timeout_returns_timed_out", "RunAsync stops a command at its timeout and reports TimedOut", TestTags.Negative, async () =>
            {
                if (OperatingSystem.IsWindows()) return;
                LocalHostCommandExecutor executor = new LocalHostCommandExecutor();
                HostCommandResult result = await executor.RunAsync(new HostCommandRequest
                {
                    Executable = "/bin/sh",
                    Arguments = new List<string> { "-c", "printf started; sleep 30" },
                    TimeoutMs = 500
                }).ConfigureAwait(false);

                AssertTrue(result.TimedOut, "Expected the command to time out.");
                AssertEqual(-1, result.ExitCode);
                AssertEqual("started", result.StandardOutput, "output written before the timeout is kept");
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

        internal static async Task<string> CreateRepoWithFilesAsync(string prefix)
        {
            string dir = CreateTempDir(prefix);
            File.WriteAllText(Path.Combine(dir, "a.txt"), "a");
            File.WriteAllText(Path.Combine(dir, "b.txt"), "b");
            LocalHostCommandExecutor executor = new LocalHostCommandExecutor();
            await executor.RunAsync(new HostCommandRequest { Executable = "git", WorkingDirectory = dir, Arguments = new List<string> { "init", "-q" } }).ConfigureAwait(false);
            HostCommandResult added = await executor.RunAsync(new HostCommandRequest { Executable = "git", WorkingDirectory = dir, Arguments = new List<string> { "add", "a.txt", "b.txt" } }).ConfigureAwait(false);
            if (!added.Success) throw new InvalidOperationException("git add failed: " + added.StandardError);
            return dir;
        }

        internal static void TryDeleteDir(string path)
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
