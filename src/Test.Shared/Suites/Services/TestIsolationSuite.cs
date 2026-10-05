namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Settings;
    using Armada.Tui.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Guards the test run against the developer's real profile: every default path Armada derives (data directory,
    /// settings file, database, TUI preferences and credentials) must resolve inside the per-run sandbox that
    /// <see cref="TestTemp"/> sets up, and the TUI must use the file credential store instead of the OS keychain.
    /// </summary>
    public sealed class TestIsolationSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the test isolation suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("defaults_resolve_inside_sandbox", "Default data, settings, and TUI paths resolve inside the test sandbox, never ~/.armada", () =>
            {
                string? sandbox = TestTemp.ProfileSandbox;
                AssertNotNull(sandbox, "sandbox initialized");
                string realProfile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".armada");

                AssertEqual(sandbox!, Constants.DefaultDataDirectory, "default data directory");
                AssertTrue(ArmadaSettings.DefaultSettingsPath.StartsWith(sandbox!, StringComparison.Ordinal), "default settings path");
                AssertTrue(TuiPaths.PreferencesFile().StartsWith(sandbox!, StringComparison.Ordinal), "TUI preferences");
                AssertTrue(TuiPaths.CredentialsFile().StartsWith(sandbox!, StringComparison.Ordinal), "TUI credentials");
                AssertEqual("file", Environment.GetEnvironmentVariable("ARMADA_TUI_CREDENTIAL_STORE"), "no OS keychain in tests");

                foreach (string path in new string[] { Constants.DefaultDataDirectory, ArmadaSettings.DefaultSettingsPath, TuiPaths.PreferencesFile(), TuiPaths.CredentialsFile() })
                {
                    AssertFalse(path.StartsWith(realProfile, StringComparison.Ordinal), "must not touch the real profile: " + path);
                }
            }));

            cases.Add(Case("unsaved_settings_save_into_sandbox", "Settings saved without an explicit path land in the sandbox", () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                AssertTrue(settings.EffectiveSettingsFilePath.StartsWith(TestTemp.ProfileSandbox!, StringComparison.Ordinal), "effective settings path");
            }));

            cases.Add(Case("git_line_endings_pinned", "Test git repositories check files out with LF even when the host git config converts line endings", () =>
            {
                // Reproduces the GitHub Windows runner (core.autocrlf=true system-wide) on any host: a global config that
                // asks for CRLF conversion. Without the pinned settings the clone below checks README.md out with CRLF.
                string root = TestTemp.NewDirectory("git_eol");
                string globalConfig = Path.Combine(root, "host.gitconfig");
                File.WriteAllText(globalConfig, "[core]\n\tautocrlf = true\n\teol = crlf\n[user]\n\tname = Armada Tests\n\temail = armada-tests@example.com\n[init]\n\tdefaultBranch = main\n");
                string source = Path.Combine(root, "source");
                string clone = Path.Combine(root, "clone");
                Directory.CreateDirectory(source);

                RunGitWithHostConfig(source, globalConfig, "init");
                File.WriteAllText(Path.Combine(source, "README.md"), "base\nworker change\n");
                RunGitWithHostConfig(source, globalConfig, "add", "README.md");
                RunGitWithHostConfig(source, globalConfig, "commit", "-m", "Initial commit");
                RunGitWithHostConfig(root, globalConfig, "clone", source, clone);

                AssertEqual("false", RunGitWithHostConfig(clone, globalConfig, "config", "--get", "core.autocrlf").Trim(), "effective core.autocrlf");
                AssertEqual("base\nworker change\n", File.ReadAllText(Path.Combine(clone, "README.md")), "checked-out content keeps LF");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.TestIsolation",
                displayName: "Test isolation from the real profile",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string RunGitWithHostConfig(string workingDirectory, string globalConfig, params string[] arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Stand-in for the host's configuration: this file replaces ~/.gitconfig, and the system file is skipped so
            // the result does not depend on the machine running the test either way.
            startInfo.Environment["GIT_CONFIG_GLOBAL"] = globalConfig;
            startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);

            using (Process process = new Process { StartInfo = startInfo })
            {
                process.Start();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                string stdout = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("git " + String.Join(" ", arguments) + " exited " + process.ExitCode + ": " + stderr.Result.Trim());
                }

                return stdout;
            }
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.TestIsolation",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { TestTags.Positive });
        }

        #endregion
    }
}
