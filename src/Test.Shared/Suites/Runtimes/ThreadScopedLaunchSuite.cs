namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;
    using Armada.Runtimes;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Launches each CLI runtime for a thread-scoped (Ask) turn against a stub executable that records its arguments,
    /// environment, and the project-local files it can see, then checks the real launch wiring in
    /// <see cref="BaseAgentRuntime"/>: the token arrives in the environment, HOME is not redirected, project files land in
    /// the working directory, and Mux receives its strict config before the positional prompt. POSIX only (the stub is a
    /// shell script); on Windows the cases pass without launching.
    /// </summary>
    public sealed class ThreadScopedLaunchSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Runtimes.ThreadScopedLaunch";
        private const string Token = "tok_launch_secret";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the thread-scoped launch suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("codex_launch_carries_env_token", "Codex thread launch passes -c overrides and the token in the environment", async () =>
            {
                LaunchRecord? record = await LaunchAsync((LoggingModule logging, string stub) => new CodexRuntime(logging) { ExecutablePath = stub }).ConfigureAwait(false);
                if (record == null) return;
                AssertTrue(record.Arguments.Any(a => a.Contains("env_http_headers = { \"X-Token\" = \"ARMADA_MCP_TOKEN\" }")), "expected scoped server override");
                AssertEqual(Token, record.Token);
                AssertEqual(Environment.GetEnvironmentVariable("HOME") ?? String.Empty, record.Home);
                AssertFalse(record.Arguments.Any(a => a.Contains(Token)), "token must not be an argument");
            }));

            cases.Add(Case("gemini_launch_writes_workspace_settings", "Gemini thread launch writes .gemini/settings.json in the working directory", async () =>
            {
                LaunchRecord? record = await LaunchAsync((LoggingModule logging, string stub) => new GeminiRuntime(logging) { ExecutablePath = stub }).ConfigureAwait(false);
                if (record == null) return;
                McpKeyedConfigFile gemini = JsonSerializer.Deserialize<McpKeyedConfigFile>(record.GeminiSettings)!;
                AssertTrue(gemini.McpServers != null && gemini.McpServers.ContainsKey(CaptainThreadMcpPlanner.ServerName), "expected the armada server in workspace settings");
                AssertEqual("$" + CaptainThreadMcpPlanner.TokenEnvironmentVariable, gemini.McpServers![CaptainThreadMcpPlanner.ServerName].Headers?["X-Token"], "expected workspace settings with env header");
                AssertEqual("true", record.TrustWorkspace);
                AssertTrue(record.Arguments.Contains("--allowed-mcp-server-names"), "expected allowed server names");
                AssertEqual(Environment.GetEnvironmentVariable("HOME") ?? String.Empty, record.Home);
            }));

            cases.Add(Case("cursor_launch_writes_project_mcp", "Cursor thread launch writes .cursor/mcp.json in the working directory", async () =>
            {
                LaunchRecord? record = await LaunchAsync((LoggingModule logging, string stub) => new CursorRuntime(logging) { ExecutablePath = stub }).ConfigureAwait(false);
                if (record == null) return;
                McpKeyedConfigFile cursor = JsonSerializer.Deserialize<McpKeyedConfigFile>(record.CursorMcp)!;
                AssertTrue(cursor.McpServers != null && cursor.McpServers.ContainsKey(CaptainThreadMcpPlanner.ServerName), "expected the armada server in project mcp.json");
                AssertEqual("${env:" + CaptainThreadMcpPlanner.TokenEnvironmentVariable + "}", cursor.McpServers![CaptainThreadMcpPlanner.ServerName].Headers?["X-Token"], "expected project mcp.json with env header");
                AssertEqual(Token, record.Token);
                AssertEqual(Environment.GetEnvironmentVariable("HOME") ?? String.Empty, record.Home);
            }));

            cases.Add(Case("cursor_launch_uses_runtime_mcp_host", "A thread launch writes the runtime's McpHost (the bound listener host) into the MCP URL", async () =>
            {
                LaunchRecord? record = await LaunchAsync((LoggingModule logging, string stub) => new CursorRuntime(logging) { ExecutablePath = stub, McpHost = "127.0.0.1" }).ConfigureAwait(false);
                if (record == null) return;
                KeyedMcpServersDocument document = JsonHelper.Deserialize<KeyedMcpServersDocument>(record.CursorMcp);
                AssertEqual("http://127.0.0.1:7891/mcp", document.McpServers!["armada"].Url);
            }));

            cases.Add(Case("opencode_launch_sets_inline_config", "OpenCode thread launch sets OPENCODE_CONFIG_CONTENT", async () =>
            {
                LaunchRecord? record = await LaunchAsync((LoggingModule logging, string stub) => new OpenCodeRuntime(logging) { ExecutablePath = stub }).ConfigureAwait(false);
                if (record == null) return;
                OpenCodeConfigFile openCode = JsonSerializer.Deserialize<OpenCodeConfigFile>(record.OpenCodeContent)!;
                AssertNotNull(openCode.Mcp, "expected an mcp section in the inline config");
                AssertEqual(1, openCode.Mcp!.Values.Count(e => e.Enabled == true && e.Headers != null && e.Headers.TryGetValue("X-Token", out string? header) && header == "{env:" + CaptainThreadMcpPlanner.TokenEnvironmentVariable + "}"), "expected one enabled inline server with env header");
                AssertEqual(Token, record.Token);
            }));

            cases.Add(Case("mux_launch_inserts_strict_config_before_prompt", "Mux thread launch inserts --mcp-config/--strict-mcp-config before the positional prompt", async () =>
            {
                LaunchRecord? record = await LaunchAsync((LoggingModule logging, string stub) => new MuxRuntime(logging) { ExecutablePath = stub }).ConfigureAwait(false);
                if (record == null) return;
                AssertEqual("say hi", record.Arguments[record.Arguments.Count - 1]);
                AssertEqual(1, record.Arguments.Count(a => a == "--mcp-config"));
                int idx = record.Arguments.IndexOf("--mcp-config");
                AssertTrue(record.Arguments[idx + 1].EndsWith("mcp-servers.json", StringComparison.Ordinal), "expected scoped server document");
                MuxServersFile mux = JsonSerializer.Deserialize<MuxServersFile>(record.MuxConfig)!;
                AssertTrue(mux.Servers != null && mux.Servers.Count == 1, "expected the scoped document to exist at launch with one server");
                AssertEqual("X-Token", mux.Servers![0].Auth?.ApiKeyHeader, "expected the X-Token header");
                AssertEqual("${" + CaptainThreadMcpPlanner.TokenEnvironmentVariable + "}", mux.Servers[0].Auth?.ApiKeyValue, "expected the env token reference");
                AssertTrue(record.Arguments.Contains("--strict-mcp-config"), "expected strict");
                AssertEqual(Token, record.Token);
                AssertFalse(record.MuxConfigDirOverridden, "MUX_CONFIG_DIR must not be redirected");
            }));

            // Mission launches with IsolateCaptainLaunch on (McpTokenWithFullIsolation): every MCP-capable CLI runtime must
            // carry the mission-scoped token. OpenCode and Mux used to launch without it.
            cases.Add(Case("isolated_mission_claude_carries_token", "Isolated mission launch: Claude Code strict MCP config carries the token", async () =>
            {
                LaunchRecord? record = await LaunchAsync((LoggingModule logging, string stub) => new ClaudeCodeRuntime(logging) { ExecutablePath = stub }, true).ConfigureAwait(false);
                if (record == null) return;
                AssertTrue(record.Arguments.Contains("--strict-mcp-config"), "expected strict MCP config");
                AssertContains(Token, record.MuxConfig, "expected the token in the --mcp-config document");
            }));

            cases.Add(Case("isolated_mission_codex_carries_token", "Isolated mission launch: Codex scoped CODEX_HOME config carries the token", async () =>
            {
                LaunchRecord? record = await LaunchAsync((LoggingModule logging, string stub) => new CodexRuntime(logging) { ExecutablePath = stub }, true).ConfigureAwait(false);
                if (record == null) return;
                AssertContains(Token, record.CodexHomeConfig, "expected the token in the scoped config.toml");
            }));

            cases.Add(Case("isolated_mission_gemini_carries_token", "Isolated mission launch: Gemini scoped home settings carry the token", async () =>
            {
                LaunchRecord? record = await LaunchAsync((LoggingModule logging, string stub) => new GeminiRuntime(logging) { ExecutablePath = stub }, true).ConfigureAwait(false);
                if (record == null) return;
                AssertContains(Token, record.HomeGeminiSettings, "expected the token in the scoped settings.json");
                AssertEqual(String.Empty, record.GeminiSettings, "nothing may be written into the worktree");
            }));

            cases.Add(Case("isolated_mission_cursor_carries_token", "Isolated mission launch: Cursor scoped home mcp.json carries the token", async () =>
            {
                LaunchRecord? record = await LaunchAsync((LoggingModule logging, string stub) => new CursorRuntime(logging) { ExecutablePath = stub }, true).ConfigureAwait(false);
                if (record == null) return;
                AssertContains(Token, record.HomeCursorMcp, "expected the token in the scoped mcp.json");
                AssertEqual(String.Empty, record.CursorMcp, "nothing may be written into the worktree");
            }));

            cases.Add(Case("isolated_mission_mux_carries_token", "Isolated mission launch: Mux scoped server document authenticates with the token", async () =>
            {
                LaunchRecord? record = await LaunchAsync((LoggingModule logging, string stub) => new MuxRuntime(logging) { ExecutablePath = stub }, true).ConfigureAwait(false);
                if (record == null) return;
                MuxServersFile mux = JsonSerializer.Deserialize<MuxServersFile>(record.MuxHomeConfig)!;
                AssertTrue(mux.Servers != null && mux.Servers.Count == 1, "expected one scoped server in MUX_CONFIG_DIR");
                AssertEqual("X-Token", mux.Servers![0].Auth?.ApiKeyHeader, "expected the X-Token header");
                AssertEqual(Token, mux.Servers[0].Auth?.ApiKeyValue, "expected the mission token");
            }));

            cases.Add(Case("isolated_mission_opencode_carries_token", "Isolated mission launch: OpenCode binds the token through its inline config", async () =>
            {
                LaunchRecord? record = await LaunchAsync((LoggingModule logging, string stub) => new OpenCodeRuntime(logging) { ExecutablePath = stub }, true).ConfigureAwait(false);
                if (record == null) return;
                AssertFalse(String.IsNullOrEmpty(record.OpenCodeContent), "expected OPENCODE_CONFIG_CONTENT");
                OpenCodeConfigFile openCode = JsonSerializer.Deserialize<OpenCodeConfigFile>(record.OpenCodeContent)!;
                AssertNotNull(openCode.Mcp, "expected an mcp section in the inline config");
                AssertEqual(1, openCode.Mcp!.Values.Count(e => e.Enabled == true && e.Headers != null && e.Headers.TryGetValue("X-Token", out string? header) && header == "{env:" + CaptainThreadMcpPlanner.TokenEnvironmentVariable + "}"), "expected one enabled server with the env token header");
                AssertEqual(Token, record.Token);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Thread-Scoped Runtime Launch",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static Task<LaunchRecord?> LaunchAsync(Func<LoggingModule, string, BaseAgentRuntime> factory)
        {
            return LaunchAsync(factory, false);
        }

        private static async Task<LaunchRecord?> LaunchAsync(Func<LoggingModule, string, BaseAgentRuntime> factory, bool missionWithFullIsolation)
        {
            if (OperatingSystem.IsWindows()) return null;

            string root = Path.Combine(Path.GetTempPath(), "armada-thread-launch-" + Guid.NewGuid().ToString("N"));
            string work = Path.Combine(root, "work");
            string outFile = Path.Combine(root, "record.txt");
            Directory.CreateDirectory(work);
            string stub = Path.Combine(root, "stub.sh");
            string script =
                "#!/bin/sh\n"
                + "out='" + outFile + "'\n"
                + "for a in \"$@\"; do printf 'ARG:%s\\n' \"$a\" >> \"$out\"; done\n"
                + "printf 'TOKEN:%s\\n' \"$ARMADA_MCP_TOKEN\" >> \"$out\"\n"
                + "printf 'HOME:%s\\n' \"$HOME\" >> \"$out\"\n"
                + "printf 'TRUST:%s\\n' \"$GEMINI_CLI_TRUST_WORKSPACE\" >> \"$out\"\n"
                + "printf 'MUXDIR:%s\\n' \"$MUX_CONFIG_DIR\" >> \"$out\"\n"
                + "printf 'OPENCODE:%s\\n' \"$OPENCODE_CONFIG_CONTENT\" >> \"$out\"\n"
                + "if [ -f .gemini/settings.json ]; then printf 'GEMINI:%s\\n' \"$(tr -d '\\n' < .gemini/settings.json)\" >> \"$out\"; fi\n"
                + "if [ -f .cursor/mcp.json ]; then printf 'CURSOR:%s\\n' \"$(tr -d '\\n' < .cursor/mcp.json)\" >> \"$out\"; fi\n"
                + "if [ -n \"$CODEX_HOME\" ] && [ -f \"$CODEX_HOME/config.toml\" ]; then printf 'CODEXHOME:%s\\n' \"$(tr -d '\\n' < \"$CODEX_HOME/config.toml\")\" >> \"$out\"; fi\n"
                + "if [ -f \"$HOME/.gemini/settings.json\" ]; then printf 'HOMEGEMINI:%s\\n' \"$(tr -d '\\n' < \"$HOME/.gemini/settings.json\")\" >> \"$out\"; fi\n"
                + "if [ -f \"$HOME/.cursor/mcp.json\" ]; then printf 'HOMECURSOR:%s\\n' \"$(tr -d '\\n' < \"$HOME/.cursor/mcp.json\")\" >> \"$out\"; fi\n"
                + "if [ -n \"$MUX_CONFIG_DIR\" ] && [ -f \"$MUX_CONFIG_DIR/mcp-servers.json\" ]; then printf 'MUXHOME:%s\\n' \"$(tr -d '\\n' < \"$MUX_CONFIG_DIR/mcp-servers.json\")\" >> \"$out\"; fi\n"
                + "prev=''; for a in \"$@\"; do if [ \"$prev\" = '--mcp-config' ] && [ -f \"$a\" ]; then printf 'MUX:%s\\n' \"$(tr -d '\\n' < \"$a\")\" >> \"$out\"; fi; prev=\"$a\"; done\n"
                + "cat > /dev/null\n"
                + "echo done\n";
            await File.WriteAllTextAsync(stub, script).ConfigureAwait(false);
            File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            BaseAgentRuntime runtime = factory(logging, stub);
            runtime.McpSessionToken = Token;
            if (missionWithFullIsolation)
            {
                // Exactly what AgentLifecycleHandler sets for a mission launch with IsolateCaptainLaunch on.
                runtime.McpTokenWithFullIsolation = true;
                runtime.McpAllowWorkingDirectoryFiles = false;
            }
            TaskCompletionSource<bool> exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            runtime.OnProcessExited += (int pid, int? code) => exited.TrySetResult(true);

            try
            {
                await runtime.StartAsync(work, "say hi", isolateLaunch: true, mcpPort: 7891).ConfigureAwait(false);
                Task finished = await Task.WhenAny(exited.Task, Task.Delay(15000)).ConfigureAwait(false);
                AssertTrue(finished == exited.Task, "stub did not exit");

                string[] lines = await File.ReadAllLinesAsync(outFile).ConfigureAwait(false);
                LaunchRecord record = new LaunchRecord();
                foreach (string line in lines)
                {
                    if (line.StartsWith("ARG:", StringComparison.Ordinal)) record.Arguments.Add(line.Substring(4));
                    else if (line.StartsWith("TOKEN:", StringComparison.Ordinal)) record.Token = line.Substring(6);
                    else if (line.StartsWith("HOME:", StringComparison.Ordinal)) record.Home = line.Substring(5);
                    else if (line.StartsWith("TRUST:", StringComparison.Ordinal)) record.TrustWorkspace = line.Substring(6);
                    else if (line.StartsWith("MUXDIR:", StringComparison.Ordinal)) record.MuxConfigDirOverridden = line.Substring(7) != (Environment.GetEnvironmentVariable("MUX_CONFIG_DIR") ?? String.Empty);
                    else if (line.StartsWith("OPENCODE:", StringComparison.Ordinal)) record.OpenCodeContent = line.Substring(9);
                    else if (line.StartsWith("GEMINI:", StringComparison.Ordinal)) record.GeminiSettings = line.Substring(7);
                    else if (line.StartsWith("CURSOR:", StringComparison.Ordinal)) record.CursorMcp = line.Substring(7);
                    else if (line.StartsWith("MUX:", StringComparison.Ordinal)) record.MuxConfig = line.Substring(4);
                    else if (line.StartsWith("CODEXHOME:", StringComparison.Ordinal)) record.CodexHomeConfig = line.Substring(10);
                    else if (line.StartsWith("HOMEGEMINI:", StringComparison.Ordinal)) record.HomeGeminiSettings = line.Substring(11);
                    else if (line.StartsWith("HOMECURSOR:", StringComparison.Ordinal)) record.HomeCursorMcp = line.Substring(11);
                    else if (line.StartsWith("MUXHOME:", StringComparison.Ordinal)) record.MuxHomeConfig = line.Substring(8);
                }

                return record;
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.Positive });
        }

        #endregion

        #region Private-Classes

        private sealed class LaunchRecord
        {
            public List<string> Arguments { get; } = new List<string>();
            public string Token { get; set; } = String.Empty;
            public string Home { get; set; } = String.Empty;
            public string TrustWorkspace { get; set; } = String.Empty;
            public bool MuxConfigDirOverridden { get; set; } = false;
            public string OpenCodeContent { get; set; } = String.Empty;
            public string GeminiSettings { get; set; } = String.Empty;
            public string CursorMcp { get; set; } = String.Empty;
            public string MuxConfig { get; set; } = String.Empty;
            public string CodexHomeConfig { get; set; } = String.Empty;
            public string HomeGeminiSettings { get; set; } = String.Empty;
            public string HomeCursorMcp { get; set; } = String.Empty;
            public string MuxHomeConfig { get; set; } = String.Empty;
        }

        #endregion
    }
}
