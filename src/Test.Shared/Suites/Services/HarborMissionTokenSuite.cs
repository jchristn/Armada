namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Runtimes;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// O-04: a Harbor binds the captain's Armada MCP connection to the mission-scoped token the Admiral ships in the
    /// launch request, against the MCP URL the Admiral advertised in the handshake. A shim stands in for the Claude Code
    /// CLI and records its arguments, the MCP configuration it was given, and the token environment variable.
    /// </summary>
    public sealed class HarborMissionTokenSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HarborMissionToken";
        private const string RecordVariable = "ARMADA_TEST_SHIM_RECORD";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("harbor_launch_binds_mission_token", "A Harbor launch with a mission token binds the CLI's Armada MCP connection to it", TestTags.Positive, async () =>
            {
                string record = await RunShimLaunchAsync("tok-harbor-mission", "http://127.0.0.1:7891/mcp").ConfigureAwait(false);
                AssertContains("TOKEN=tok-harbor-mission", record, "the token reaches the captain process");
                AssertContains("URL=http://127.0.0.1:7891/mcp", record, "the advertised MCP URL reaches the captain process");
                AssertContains("ARG=--strict-mcp-config", record, "only the bound Armada MCP server is used");
                AssertContains("\"X-Token\": \"tok-harbor-mission\"", record, "the MCP configuration carries the token as X-Token");
                AssertContains("http://127.0.0.1:7891/mcp", record, "the MCP configuration points at the advertised URL");
            }));

            cases.Add(CaseAsync("harbor_launch_without_token_is_unchanged", "A Harbor launch without a token adds no MCP binding", TestTags.Negative, async () =>
            {
                string record = await RunShimLaunchAsync(null, "http://127.0.0.1:7891/mcp").ConfigureAwait(false);
                AssertContains("TOKEN=\n", record.Replace("\r\n", "\n"), "no token in the environment");
                AssertFalse(record.Contains("ARG=--strict-mcp-config", StringComparison.Ordinal), "no MCP binding arguments");
            }));

            cases.Add(CaseAsync("harbor_launch_with_unbindable_url_keeps_token_in_environment", "An advertised URL the planners cannot express (https) is not bound, but the token is still provided", TestTags.Negative, async () =>
            {
                string record = await RunShimLaunchAsync("tok-https", "https://admiral.example/mcp").ConfigureAwait(false);
                AssertContains("TOKEN=tok-https", record, "the token reaches the captain process");
                AssertFalse(record.Contains("ARG=--strict-mcp-config", StringComparison.Ordinal), "no per-launch binding for an https URL");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Harbor Mission Tokens (O-04)",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<string> RunShimLaunchAsync(string? token, string mcpUrl)
        {
            string root = TestTemp.NewDirectory("harbor_mission_token");
            string recordFile = Path.Combine(root, "record.txt");
            string workDir = Path.Combine(root, "work");
            Directory.CreateDirectory(workDir);
            string shim = WriteShim(root);
            Environment.SetEnvironmentVariable(RecordVariable, recordFile);
            try
            {
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                AgentRuntimeFactory factory = new AgentRuntimeFactory(logging);
                factory.Override(AgentRuntimeEnum.ClaudeCode, () => new ClaudeCodeRuntime(logging) { ExecutablePath = shim });
                LocalHarborJobRunner runner = new LocalHarborJobRunner(logging, factory);

                TaskCompletionSource<int> exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                HarborLaunchRequest request = new HarborLaunchRequest
                {
                    JobId = Guid.NewGuid().ToString("N"),
                    Runtime = AgentRuntimeEnum.ClaudeCode.ToString(),
                    WorkingDirectory = workDir,
                    Prompt = "Respond with OK.",
                    McpSessionToken = token
                };

                await runner.StartAsync(request, mcpUrl, _ => { }, (_, _) => { }, code => exited.TrySetResult(code), CancellationToken.None).ConfigureAwait(false);
                Task finished = await Task.WhenAny(exited.Task, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
                AssertTrue(finished == exited.Task, "the shim exits");

                MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(10));
                while (!deadline.Passed)
                {
                    if (File.Exists(recordFile))
                    {
                        string contents = await File.ReadAllTextAsync(recordFile).ConfigureAwait(false);
                        if (contents.Contains("DONE", StringComparison.Ordinal)) return contents;
                    }

                    await Task.Delay(50).ConfigureAwait(false);
                }

                throw new TimeoutException("the shim did not record its launch");
            }
            finally
            {
                Environment.SetEnvironmentVariable(RecordVariable, null);
                TestTemp.TryDelete(root);
            }
        }

        private static string WriteShim(string directory)
        {
            if (OperatingSystem.IsWindows())
            {
                string path = Path.Combine(directory, "claude.cmd");
                File.WriteAllText(path,
                    "@echo off\r\n" +
                    "setlocal EnableExtensions EnableDelayedExpansion\r\n" +
                    "set \"F=%" + RecordVariable + "%\"\r\n" +
                    ">> \"!F!\" echo TOKEN=!ARMADA_MCP_TOKEN!\r\n" +
                    ">> \"!F!\" echo URL=!ARMADA_MCP_URL!\r\n" +
                    "set \"PREV=\"\r\n" +
                    ":loop\r\n" +
                    "if \"%~1\"==\"\" goto done\r\n" +
                    ">> \"!F!\" echo ARG=%~1\r\n" +
                    "if \"!PREV!\"==\"--mcp-config\" type \"%~1\" >> \"!F!\"\r\n" +
                    "set \"PREV=%~1\"\r\n" +
                    "shift\r\n" +
                    "goto loop\r\n" +
                    ":done\r\n" +
                    ">> \"!F!\" echo DONE\r\n" +
                    "exit /b 0\r\n");
                return path;
            }

            string shimPath = Path.Combine(directory, "claude");
            File.WriteAllText(shimPath,
                "#!/usr/bin/env sh\n" +
                "f=\"$" + RecordVariable + "\"\n" +
                "printf 'TOKEN=%s\\n' \"$ARMADA_MCP_TOKEN\" >> \"$f\"\n" +
                "printf 'URL=%s\\n' \"$ARMADA_MCP_URL\" >> \"$f\"\n" +
                "prev=\"\"\n" +
                "for a in \"$@\"; do\n" +
                "  printf 'ARG=%s\\n' \"$a\" >> \"$f\"\n" +
                "  if [ \"$prev\" = \"--mcp-config\" ]; then cat \"$a\" >> \"$f\"; printf '\\n' >> \"$f\"; fi\n" +
                "  prev=\"$a\"\n" +
                "done\n" +
                "printf 'DONE\\n' >> \"$f\"\n" +
                "exit 0\n");
            File.SetUnixFileMode(shimPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            return shimPath;
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
