namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Server;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The command line a replacement Admiral is started with by <c>server/restart</c>, rebuild, and rollback
    /// (<see cref="ReplacementProcessLauncher"/>). An Admiral started as <c>dotnet Armada.Server.dll</c> used to relaunch
    /// a bare <c>dotnet</c> (its <see cref="Environment.ProcessPath"/>) that printed its usage and exited, so the
    /// restart never came back. Pure construction only: no process is started.
    /// </summary>
    public sealed class ReplacementLauncherSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("dotnet_hosted_relaunches_host_with_assembly_and_args", "dotnet-hosted Admiral relaunches as '<same dotnet> <Armada.Server.dll> <original args>', never a bare dotnet", TestTags.Negative, () =>
            {
                ReplacementCommand? command = ReplacementProcessLauncher.BuildForRunningProcess(
                    "/usr/local/share/dotnet/dotnet",
                    "/opt/armada/server/Armada.Server.dll",
                    new[] { "--custom", "value with space" });
                AssertNotNull(command);
                AssertEqual("/usr/local/share/dotnet/dotnet", command!.FileName, "the same host");
                AssertTrue(command.IsDotnetHosted);
                AssertEqual(3, command.Arguments.Count, "assembly then original args: " + command);
                AssertEqual("/opt/armada/server/Armada.Server.dll", command.Arguments[0], "the server assembly comes first");
                AssertEqual("--custom", command.Arguments[1]);
                AssertEqual("value with space", command.Arguments[2]);
                AssertEqual("/opt/armada/server", command.WorkingDirectory);
            }));

            cases.Add(Case("windows_dotnet_exe_host", "dotnet.exe on Windows is recognized as the dotnet host", TestTags.Negative, () =>
            {
                ReplacementCommand? command = ReplacementProcessLauncher.BuildForRunningProcess(
                    "C:\\Program Files\\dotnet\\DOTNET.EXE",
                    "C:\\Armada\\Armada.Server.dll",
                    new string[0]);
                AssertNotNull(command);
                AssertEqual("C:\\Program Files\\dotnet\\DOTNET.EXE", command!.FileName);
                AssertTrue(command.IsDotnetHosted);
                AssertEqual(1, command.Arguments.Count);
                AssertEqual("C:\\Armada\\Armada.Server.dll", command.Arguments[0]);
                AssertEqual("C:\\Armada", command.WorkingDirectory);
            }));

            cases.Add(Case("native_executable_relaunches_itself", "A native Admiral relaunches its own executable with its arguments, as before", TestTags.Positive, () =>
            {
                ReplacementCommand? command = ReplacementProcessLauncher.BuildForRunningProcess(
                    "/opt/armada/server/Armada.Server",
                    "/opt/armada/server/Armada.Server.dll",
                    new[] { "--custom" });
                AssertNotNull(command);
                AssertEqual("/opt/armada/server/Armada.Server", command!.FileName);
                AssertFalse(command.IsDotnetHosted);
                AssertEqual(1, command.Arguments.Count);
                AssertEqual("--custom", command.Arguments[0]);
                AssertEqual("/opt/armada/server", command.WorkingDirectory);
            }));

            cases.Add(Case("windows_native_exe_drops_run_service", "A Windows Armada.Server.exe relaunches directly; --run-service is not passed to the detached replacement", TestTags.Positive, () =>
            {
                ReplacementCommand? command = ReplacementProcessLauncher.BuildForRunningProcess(
                    "C:\\Program Files\\Armada\\Armada.Server.exe",
                    "C:\\Program Files\\Armada\\Armada.Server.dll",
                    new[] { "--run-service" });
                AssertNotNull(command);
                AssertEqual("C:\\Program Files\\Armada\\Armada.Server.exe", command!.FileName);
                AssertFalse(command.IsDotnetHosted, "Armada.Server.exe is not the dotnet host");
                AssertEqual(0, command.Arguments.Count, command.ToString());
                AssertEqual("C:\\Program Files\\Armada", command.WorkingDirectory);
                AssertEqual("\"C:\\Program Files\\Armada\\Armada.Server.exe\"", command.ToString());
            }));

            cases.Add(Case("dotnet_host_without_assembly_is_refused", "The dotnet host with no known server assembly yields no command (restart fails loudly instead of launching a bare dotnet)", TestTags.Negative, () =>
            {
                AssertNull(ReplacementProcessLauncher.BuildForRunningProcess("/usr/bin/dotnet", null, new[] { "x" }));
                AssertNull(ReplacementProcessLauncher.BuildForRunningProcess("/usr/bin/dotnet", "  ", null));
                AssertNull(ReplacementProcessLauncher.BuildForRunningProcess(null, "/opt/Armada.Server.dll", null));
            }));

            cases.Add(Case("embedded_admiral_does_not_repeat_host_command", "An Admiral embedded in another program (the CLI) is not relaunched with that program's command line", TestTags.Negative, () =>
            {
                ReplacementCommand? hosted = ReplacementProcessLauncher.BuildForRunningProcess("/usr/bin/dotnet", "/tools/armada/Armada.Helm.dll", new[] { "mission", "create", "x" });
                AssertNotNull(hosted);
                AssertEqual(1, hosted!.Arguments.Count, hosted.ToString());
                AssertEqual("/tools/armada/Armada.Helm.dll", hosted.Arguments[0]);

                ReplacementCommand? native = ReplacementProcessLauncher.BuildForRunningProcess("/tools/armada/armada", "/tools/armada/Armada.Helm.dll", new[] { "mission", "create", "x" });
                AssertNotNull(native);
                AssertEqual(0, native!.Arguments.Count, native.ToString());
            }));

            cases.Add(Case("slot_executable_or_assembly", "A slot relaunches its apphost when present, else its Armada.Server.dll on the running dotnet host (or dotnet from PATH)", TestTags.Positive, () =>
            {
                string unixSlot = "/data/bin/slots/2026-10-06_120000_abc/Armada.Server.exe";
                string unixDll = "/data/bin/slots/2026-10-06_120000_abc/Armada.Server.dll";
                HashSet<string> files = new HashSet<string>(StringComparer.Ordinal) { unixSlot, unixDll };

                ReplacementCommand? exe = ReplacementProcessLauncher.BuildForExecutable(unixSlot, new[] { "--a" }, "/usr/bin/dotnet", files.Contains);
                AssertNotNull(exe);
                AssertEqual(unixSlot, exe!.FileName);
                AssertFalse(exe.IsDotnetHosted);
                AssertEqual("--a", exe.Arguments[0]);
                AssertEqual("/data/bin/slots/2026-10-06_120000_abc", exe.WorkingDirectory);

                files.Remove(unixSlot);
                ReplacementCommand? sameHost = ReplacementProcessLauncher.BuildForExecutable(unixSlot, new[] { "--a", "--run-service" }, "/usr/local/share/dotnet/dotnet", files.Contains);
                AssertNotNull(sameHost);
                AssertEqual("/usr/local/share/dotnet/dotnet", sameHost!.FileName, "the running dotnet host");
                AssertTrue(sameHost.IsDotnetHosted);
                AssertEqual(2, sameHost.Arguments.Count, sameHost.ToString());
                AssertEqual(unixDll, sameHost.Arguments[0]);
                AssertEqual("--a", sameHost.Arguments[1]);

                ReplacementCommand? pathHost = ReplacementProcessLauncher.BuildForExecutable(unixSlot, null, "/opt/armada/Armada.Server", files.Contains);
                AssertNotNull(pathHost);
                AssertEqual("dotnet", pathHost!.FileName, "a native process falls back to dotnet on the PATH");
                AssertEqual(unixDll, pathHost.Arguments[0]);

                files.Clear();
                AssertNull(ReplacementProcessLauncher.BuildForExecutable(unixSlot, null, "/usr/bin/dotnet", files.Contains), "nothing to launch");

                string winSlot = "C:\\ProgramData\\Armada\\bin\\slots\\s1\\Armada.Server.exe";
                HashSet<string> winFiles = new HashSet<string>(StringComparer.Ordinal) { "C:\\ProgramData\\Armada\\bin\\slots\\s1\\Armada.Server.dll" };
                ReplacementCommand? win = ReplacementProcessLauncher.BuildForExecutable(winSlot, null, "C:\\Program Files\\dotnet\\dotnet.exe", winFiles.Contains);
                AssertNotNull(win);
                AssertEqual("C:\\Program Files\\dotnet\\dotnet.exe", win!.FileName);
                AssertEqual("C:\\ProgramData\\Armada\\bin\\slots\\s1\\Armada.Server.dll", win.Arguments[0]);
                AssertEqual("C:\\ProgramData\\Armada\\bin\\slots\\s1", win.WorkingDirectory);
            }));

            cases.Add(Case("dotnet_host_detection", "IsDotnetHost matches dotnet and dotnet.exe only", TestTags.Positive, () =>
            {
                AssertTrue(ReplacementProcessLauncher.IsDotnetHost("/usr/bin/dotnet"));
                AssertTrue(ReplacementProcessLauncher.IsDotnetHost("dotnet"));
                AssertTrue(ReplacementProcessLauncher.IsDotnetHost("C:\\Program Files\\dotnet\\dotnet.exe"));
                AssertFalse(ReplacementProcessLauncher.IsDotnetHost("/usr/bin/dotnet-armada"));
                AssertFalse(ReplacementProcessLauncher.IsDotnetHost("/opt/dotnet/Armada.Server"));
                AssertFalse(ReplacementProcessLauncher.IsDotnetHost("C:\\dotnet\\Armada.Server.exe"));
                AssertFalse(ReplacementProcessLauncher.IsDotnetHost(null));
            }));

            cases.Add(Case("current_process_command_runs_same_program", "The command for this process names this process's program, with its entry assembly when dotnet-hosted", TestTags.Positive, () =>
            {
                ReplacementCommand? command = ReplacementProcessLauncher.BuildForCurrentProcess();
                AssertNotNull(command);
                AssertEqual(Environment.ProcessPath, command!.FileName);
                if (ReplacementProcessLauncher.IsDotnetHost(Environment.ProcessPath))
                {
                    AssertTrue(command.Arguments.Count >= 1, "the entry assembly is passed to the dotnet host");
                    AssertTrue(command.Arguments[0].EndsWith(".dll", StringComparison.OrdinalIgnoreCase), command.ToString());
                }
                else
                {
                    AssertEqual(0, command.Arguments.Count, "the test runner is not the Admiral server, so its arguments are not repeated");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.ReplacementLauncher",
                displayName: "Replacement Admiral launch command",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.ReplacementLauncher",
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
