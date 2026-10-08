namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors that verify the platform startup-script layout on disk and the documentation
    /// that references it: the required per-OS scripts exist, the scripts root stays organized,
    /// and the docs point operators at the scripted install/update/health-check workflow.
    /// </summary>
    public sealed class StartupScriptSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Startup Scripts suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("script_layout_exists", "Script Layout Exists", TestTags.Positive, () =>
            {
                string root = FindRepositoryRoot();
                string[] files =
                {
                    Path.Combine("scripts", "common", "publish-server.sh"),
                    Path.Combine("scripts", "common", "healthcheck-server.sh"),
                    Path.Combine("scripts", "windows", "publish-server.bat"),
                    Path.Combine("scripts", "windows", "healthcheck-server.bat"),
                    Path.Combine("scripts", "windows", "start-armada-server.ps1"),
                    Path.Combine("scripts", "windows", "stop-armada-server.ps1"),
                    Path.Combine("scripts", "linux", "install.sh"),
                    Path.Combine("scripts", "linux", "publish-server.sh"),
                    Path.Combine("scripts", "linux", "healthcheck-server.sh"),
                    Path.Combine("scripts", "macos", "install.sh"),
                    Path.Combine("scripts", "macos", "publish-server.sh"),
                    Path.Combine("scripts", "macos", "healthcheck-server.sh"),
                    Path.Combine("scripts", "windows", "install-windows-task.bat"),
                    Path.Combine("scripts", "windows", "update-windows-task.bat"),
                    Path.Combine("scripts", "windows", "remove-windows-task.bat"),
                    Path.Combine("scripts", "linux", "install-systemd-user.sh"),
                    Path.Combine("scripts", "linux", "update-systemd-user.sh"),
                    Path.Combine("scripts", "linux", "remove-systemd-user.sh"),
                    Path.Combine("scripts", "macos", "install-launchd-agent.sh"),
                    Path.Combine("scripts", "macos", "update-launchd-agent.sh"),
                    Path.Combine("scripts", "macos", "remove-launchd-agent.sh"),
                };

                foreach (string relativePath in files)
                {
                    AssertTrue(
                        File.Exists(Path.Combine(root, relativePath)),
                        relativePath + " should exist");
                }
            }));

            cases.Add(Case("scripts_root_has_no_bat_or_sh_files", "Scripts Root Has No Bat Or Sh Files", TestTags.Positive, () =>
            {
                string scriptsRoot = Path.Combine(FindRepositoryRoot(), "scripts");

                AssertEqual(0, Directory.GetFiles(scriptsRoot, "*.bat", SearchOption.TopDirectoryOnly).Length, "scripts/ root should not contain .bat files");
                AssertEqual(0, Directory.GetFiles(scriptsRoot, "*.sh", SearchOption.TopDirectoryOnly).Length, "scripts/ root should not contain .sh files");
            }));

            cases.Add(Case("startup_docs_reference_scripted_workflow", "Startup Docs Reference Scripted Workflow", TestTags.Positive, () =>
            {
                string root = FindRepositoryRoot();
                // Windows commands are written with backslashes (scripts\windows\install.bat); compare paths with either separator.
                string readmeContents = File.ReadAllText(Path.Combine(root, "README.md")).Replace('\\', '/');
                string gettingStartedContents = File.ReadAllText(Path.Combine(root, "GETTING_STARTED.md")).Replace('\\', '/');
                string startupDocContents = File.ReadAllText(Path.Combine(root, "docs", "RUN_ON_STARTUP.md"));

                AssertContains("docs/RUN_ON_STARTUP.md", readmeContents, "README should link to the run-on-startup guide");
                AssertContains("scripts/linux/install-systemd-user.sh", readmeContents, "README should reference the Linux local deployment installer");
                AssertContains("scripts/macos/install-launchd-agent.sh", readmeContents, "README should reference the macOS local deployment installer");
                AssertContains("scripts/windows/install-windows-task.bat", readmeContents, "README should reference the Windows local deployment installer");
                AssertContains("scripts/linux/update-systemd-user.sh", readmeContents, "README should reference the Linux local deployment updater");
                AssertContains("scripts/macos/update-launchd-agent.sh", readmeContents, "README should reference the macOS local deployment updater");
                AssertContains("scripts/windows/update-windows-task.bat", readmeContents, "README should reference the Windows local deployment updater");
                AssertContains("scripts/linux/healthcheck-server.sh", readmeContents, "README should reference the Linux health-check helper");
                AssertContains("scripts/macos/healthcheck-server.sh", readmeContents, "README should reference the macOS health-check helper");
                AssertContains("scripts/windows/healthcheck-server.bat", readmeContents, "README should reference the Windows health-check helper");
                AssertContains("scripts/linux/install-systemd-user.sh", gettingStartedContents, "Getting Started should reference the Linux local deployment installer");
                AssertContains("scripts/macos/install-launchd-agent.sh", gettingStartedContents, "Getting Started should reference the macOS local deployment installer");
                AssertContains("scripts/windows/install-windows-task.bat", gettingStartedContents, "Getting Started should reference the Windows local deployment installer");
                AssertContains("scripts/linux/update-systemd-user.sh", gettingStartedContents, "Getting Started should reference the Linux local deployment updater");
                AssertContains("scripts/macos/update-launchd-agent.sh", gettingStartedContents, "Getting Started should reference the macOS local deployment updater");
                AssertContains("scripts/windows/update-windows-task.bat", gettingStartedContents, "Getting Started should reference the Windows local deployment updater");
                AssertContains("scripts/linux/healthcheck-server.sh", gettingStartedContents, "Getting Started should reference the Linux health-check helper");
                AssertContains("scripts/macos/healthcheck-server.sh", gettingStartedContents, "Getting Started should reference the macOS health-check helper");
                AssertContains("scripts/windows/healthcheck-server.bat", gettingStartedContents, "Getting Started should reference the Windows health-check helper");
                AssertContains("scripts/windows/install-windows-task.bat", startupDocContents, "Startup guide should reference the Windows installer script");
                AssertContains("scripts/linux/install-systemd-user.sh", startupDocContents, "Startup guide should reference the Linux installer script");
                AssertContains("scripts/macos/install-launchd-agent.sh", startupDocContents, "Startup guide should reference the macOS installer script");
                AssertContains("scripts/common/publish-server.sh", startupDocContents, "Startup guide should reference the shared shell publish helper");
                AssertContains("scripts/windows/healthcheck-server.bat", startupDocContents, "Startup guide should reference the Windows health-check helper");
            }));

            cases.Add(Case("windows_startup_registration_is_user_scoped", "Windows Startup Registration Is User Scoped", TestTags.Positive, () =>
            {
                string root = FindRepositoryRoot();
                string installTaskContents = File.ReadAllText(Path.Combine(root, "scripts", "windows", "install-windows-task.bat"));
                string startupDocContents = File.ReadAllText(Path.Combine(root, "docs", "RUN_ON_STARTUP.md"));

                AssertContains(@"CurrentVersion\Run", installTaskContents, "Windows installer should register a current-user Run entry");
                AssertFalse(installTaskContents.Contains("schtasks /create"), "Windows installer should not depend on schtasks task creation");
                AssertContains("does not require elevation", startupDocContents, "Startup guide should document the non-elevated Windows task flow");
            }));

            cases.Add(Case("unix_factory_reset_stops_the_server_by_pid", "Linux and macOS factory-reset stop the Admiral by PID, never by a command-line pattern", TestTags.Negative, () =>
            {
                string root = FindRepositoryRoot();
                AssertTrue(File.Exists(Path.Combine(root, "scripts", "common", "stop-armada-server.sh")), "the shared PID-based stop helper exists");
                foreach (string platform in new string[] { "linux", "macos" })
                {
                    string contents = File.ReadAllText(Path.Combine(root, "scripts", platform, "factory-reset.sh"));
                    AssertFalse(contents.Contains("pkill"), platform + " factory-reset must not use pkill");
                    AssertFalse(contents.Contains("pgrep"), platform + " factory-reset must not use pgrep");
                    AssertContains("common/stop-armada-server.sh", contents, platform + " factory-reset stops the server through the PID helper");
                }

                string helper = File.ReadAllText(Path.Combine(root, "scripts", "common", "stop-armada-server.sh"));
                AssertFalse(helper.Contains("pkill") || helper.Contains("pgrep"), "the stop helper must not match processes by pattern");
            }));

            cases.Add(Case("publish_server_dashboard_failure_rule_matches_on_every_platform", "publish-server.sh, like publish-server.bat, fails a dashboard deploy unless a deployed dashboard already exists", TestTags.Negative, () =>
            {
                string root = FindRepositoryRoot();
                string shell = File.ReadAllText(Path.Combine(root, "scripts", "common", "publish-server.sh"));
                string batch = File.ReadAllText(Path.Combine(root, "scripts", "windows", "publish-server.bat"));
                AssertContains("ERROR: Dashboard deploy failed and no deployed React dashboard is available.", batch);
                AssertContains("ERROR: Dashboard deploy failed and no deployed React dashboard is available.", shell, "the shell script fails when no dashboard is deployed");
                AssertContains(".armada/dashboard/index.html", shell, "the shell script keeps going only over an existing deployed dashboard");
                AssertFalse(shell.Contains("fall back to the embedded dashboard"), "there is no embedded dashboard to fall back to");
            }));

            cases.Add(Case("update_scripts_try_helm_fallbacks_in_the_same_order", "update.sh and update.bat try the built Helm dll, then dotnet run, then the installed armada tool", TestTags.Positive, () =>
            {
                string root = FindRepositoryRoot();
                string shell = File.ReadAllText(Path.Combine(root, "scripts", "common", "update.sh"));
                string batch = File.ReadAllText(Path.Combine(root, "scripts", "windows", "update.bat"));
                string shellHelm = shell.Substring(shell.IndexOf("run_helm() {", StringComparison.Ordinal));
                string batchHelm = batch.Substring(batch.IndexOf(":run_helm", StringComparison.Ordinal));

                AssertTrue(IsInOrder(shellHelm, "HELM_DLL", "dotnet run", "command -v armada"), "update.sh prefers the repo-targeted Helm, like update.bat");
                AssertTrue(IsInOrder(batchHelm, "HELM_DLL", "dotnet run", "where armada"), "update.bat order");
            }));

            cases.Add(Case("windows_scripts_reject_unknown_arguments", "factory-reset.bat and generate-api-surface.bat reject unknown arguments like their shell versions", TestTags.Negative, () =>
            {
                string root = FindRepositoryRoot();
                string reset = File.ReadAllText(Path.Combine(root, "scripts", "windows", "factory-reset.bat"));
                string surface = File.ReadAllText(Path.Combine(root, "scripts", "windows", "generate-api-surface.bat"));
                AssertContains("Unknown argument", reset);
                AssertContains("exit /b 2", reset, "factory-reset.bat exits 2 on an unknown argument, as factory-reset.sh does");
                AssertContains("unknown argument", surface);
                AssertFalse(surface.Contains("set \"FRAMEWORK=%~1\""), "generate-api-surface.bat must not take any unknown token as the framework");
            }));

            cases.Add(Case("db_parity_script_fails_fast_when_a_container_cannot_start", "run-db-parity-tests.sh fails a provider at once when docker run fails", TestTags.Negative, () =>
            {
                string root = FindRepositoryRoot();
                string contents = File.ReadAllText(Path.Combine(root, "scripts", "common", "run-db-parity-tests.sh"));
                AssertContains("start_container", contents);
                AssertContains("CONTAINER FAILED TO START", contents);
                AssertFalse(contents.Contains("\"$POSTGRES_IMAGE\" >/dev/null"), "docker run for a provider must be checked, not discarded");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.StartupScript",
                displayName: "Startup Scripts",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static bool IsInOrder(string text, params string[] markers)
        {
            int position = 0;
            foreach (string marker in markers)
            {
                int index = text.IndexOf(marker, position, StringComparison.Ordinal);
                if (index < 0) return false;
                position = index + marker.Length;
            }

            return true;
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                // Repo root is the directory that holds src/ alongside the top-level README and docs/.
                if (Directory.Exists(Path.Combine(current.FullName, "src")) &&
                    Directory.Exists(Path.Combine(current.FullName, "docs")) &&
                    File.Exists(Path.Combine(current.FullName, "README.md")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.StartupScript",
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
                suiteId: "Services.StartupScript",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
