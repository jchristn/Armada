namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Protocol;
    using Armada.Core.Services;
    using Armada.Runtimes;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the pi runtime: the <c>pi -p --mode json</c> argument builder, the effort-to-thinking mapping,
    /// the runtime-name aliases, and launches of a recording stub <c>pi</c> that prove the real command line, stdin
    /// prompt delivery and working directory.
    /// </summary>
    public sealed class PiRuntimeSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the pi runtime suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            // ---- Command builder ----
            cases.Add(Case("args_default_shape", "Default pi args are exactly -p --mode json --no-session; the prompt is not an argument", TestTags.Positive, () =>
            {
                List<string> args = PiCommandBuilder.BuildRunArguments("/tmp/wd", "do the thing", null, null, false, false);
                AssertEqual("-p|--mode|json|--no-session", String.Join("|", args));
            }));

            cases.Add(Case("args_model_and_effort", "Model and thinking level are passed as --model and --thinking", TestTags.Positive, () =>
            {
                List<string> args = PiCommandBuilder.BuildRunArguments("/tmp/wd", "hi", " anthropic/claude-opus-4-8 ", "high", false, false);
                AssertEqual("-p|--mode|json|--no-session|--model|anthropic/claude-opus-4-8|--thinking|high", String.Join("|", args));
            }));

            cases.Add(Case("args_refuse_excludes_bash", "Refuse adds --exclude-tools bash,powershell; --approve is added only on request", TestTags.Positive, () =>
            {
                List<string> args = PiCommandBuilder.BuildRunArguments("/tmp/wd", "hi", null, null, true, true);
                AssertEqual("-p|--mode|json|--no-session|--approve|--exclude-tools|bash,powershell", String.Join("|", args));
            }));

            cases.Add(Case("args_refuse_covers_every_shell_tool", "Refuse removes both of pi's shell tools (bash, and powershell on native Windows) and no file tool", TestTags.Negative, () =>
            {
                List<string> args = PiCommandBuilder.BuildRunArguments("/tmp/wd", "hi", null, null, true, false);
                List<string> excluded = new List<string>(args[args.IndexOf("--exclude-tools") + 1].Split(','));
                AssertTrue(excluded.Contains("bash") && excluded.Contains("powershell"), "bash and powershell are excluded: " + String.Join(",", excluded));
                foreach (string fileTool in new[] { "read", "edit", "write", "grep", "find", "ls" })
                    AssertFalse(excluded.Contains(fileTool), fileTool + " stays available under Refuse");
                AssertEqual(2, PiCommandBuilder.ShellToolNames.Count);
            }));

            cases.Add(Case("args_permission_gate_replaces_exclusion", "Approve in Armada adds --extension <gate> instead of excluding the shell tools", TestTags.Positive, () =>
            {
                List<string> args = PiCommandBuilder.BuildRunArguments("/tmp/wd", "hi", null, null, true, true, "/opt/armada/Pi/armada-permission-gate.ts", null);
                AssertEqual("-p|--mode|json|--no-session|--approve|--extension|/opt/armada/Pi/armada-permission-gate.ts", String.Join("|", args));
                List<string> bypass = PiCommandBuilder.BuildRunArguments("/tmp/wd", "hi", null, null, false, false, null, null);
                AssertEqual("-p|--mode|json|--no-session", String.Join("|", bypass), "no gate and no exclusion under Bypass");
                List<string> blankGate = PiCommandBuilder.BuildRunArguments("/tmp/wd", "hi", null, null, true, false, "  ", null);
                AssertEqual("-p|--mode|json|--no-session|--exclude-tools|bash,powershell", String.Join("|", blankGate), "a blank gate path keeps the Refuse exclusion");
            }));

            cases.Add(Case("args_prompt_file_is_last", "The Windows @file prompt path passes @<file> last; the stdin path passes no prompt argument", TestTags.Positive, () =>
            {
                List<string> file = PiCommandBuilder.BuildRunArguments("C:\\work", "line one\nline two", "openai/gpt-5.2", "low", true, true, null, "C:\\Temp\\armada\\pi-prompts\\prompt-1.md");
                AssertEqual("-p|--mode|json|--no-session|--approve|--model|openai/gpt-5.2|--thinking|low|--exclude-tools|bash,powershell|@C:\\Temp\\armada\\pi-prompts\\prompt-1.md", String.Join("|", file));
                AssertFalse(file.Exists(a => a.Contains("line one", StringComparison.Ordinal)), "the prompt text is never an argument");
                List<string> stdin = PiCommandBuilder.BuildRunArguments("/work", "line one\nline two", null, null, false, false, null, null);
                AssertFalse(stdin.Exists(a => a.StartsWith("@", StringComparison.Ordinal)), "no @file argument on the stdin path");
                List<string> gated = PiCommandBuilder.BuildRunArguments("/work", "hi", null, null, true, false, "/gate.ts", "/tmp/p.md");
                AssertEqual("-p|--mode|json|--no-session|--extension|/gate.ts|@/tmp/p.md", String.Join("|", gated));
            }));

            cases.Add(Case("prompt_file_defaults_to_windows_only", "PiRuntime delivers the prompt with @file only on Windows by default", TestTags.Positive, () =>
            {
                AssertEqual(OperatingSystem.IsWindows(), new PiRuntime(Logging()).UsePromptFile);
            }));

            cases.Add(Case("permission_setup_configures_gate", "CliPermissionPromptSetup points a pi launch at the shipped gate for Approve in Armada and clears it for every other policy", TestTags.Positive, () =>
            {
                PiRuntime runtime = new PiRuntime(Logging());
                AssertTrue(CliPermissionPromptSetup.Configure(runtime, CliPermissionPolicyEnum.ApproveInArmada, 600), "pi sends its prompts to Armada");
                AssertNotNull(runtime.PermissionGateExtensionPath, "the gate path is set");
                AssertTrue(File.Exists(runtime.PermissionGateExtensionPath!), "the gate ships in the build output: " + runtime.PermissionGateExtensionPath);
                AssertEqual(Path.Combine(AppContext.BaseDirectory, "Pi", PiRuntime.PermissionGateFileName), runtime.PermissionGateExtensionPath);
                AssertEqual(600 + CliPermissionPromptSetup.CliTimeoutMarginSeconds, runtime.PermissionPromptTimeoutSeconds ?? -1, "the gate outwaits Armada's own prompt timeout");
                foreach (CliPermissionPolicyEnum other in new[] { CliPermissionPolicyEnum.Refuse, CliPermissionPolicyEnum.Bypass })
                {
                    CliPermissionPromptSetup.Configure(runtime, CliPermissionPolicyEnum.ApproveInArmada, 600);
                    AssertFalse(CliPermissionPromptSetup.Configure(runtime, other, 600), other + " does not prompt");
                    AssertNull(runtime.PermissionGateExtensionPath, other + " clears the gate");
                    AssertNull(runtime.PermissionPromptTimeoutSeconds, other + " clears the timeout");
                }
            }));

            cases.Add(Case("permission_gate_install_layouts", "Pi permission gate resolves portable and signed macOS bundle resource layouts", TestTags.Positive, () =>
            {
                string root = TestTemp.NewDirectory("pi_gate_layout");
                string portable = Path.Combine(root, "portable");
                Directory.CreateDirectory(Path.Combine(portable, "Pi"));
                string portableGate = Path.Combine(portable, "Pi", PiRuntime.PermissionGateFileName);
                File.WriteAllText(portableGate, "// portable extension");
                AssertEqual(portableGate, PiRuntime.FindPermissionGateExtension(portable));
                string contents = Path.Combine(root, "Armada Harbor.app", "Contents");
                string macOs = Path.Combine(contents, "MacOS");
                string resources = Path.Combine(contents, "Resources", "Pi");
                Directory.CreateDirectory(macOs);
                Directory.CreateDirectory(resources);
                string bundledGate = Path.Combine(resources, PiRuntime.PermissionGateFileName);
                File.WriteAllText(bundledGate, "// bundled extension");
                AssertEqual(bundledGate, PiRuntime.FindPermissionGateExtension(macOs));
                AssertFalse(Directory.Exists(Path.Combine(macOs, "Pi")), "Resource lookup does not require unsigned nested code.");
                File.Delete(bundledGate);
                AssertNull(PiRuntime.FindPermissionGateExtension(macOs), "Missing extension must still fail closed.");
                string unrelated = Path.Combine(root, "unrelated", "MacOS");
                Directory.CreateDirectory(unrelated);
                AssertNull(PiRuntime.FindPermissionGateExtension(unrelated), "Only an app bundle uses the Resources fallback.");
            }));

            cases.Add(Case("args_reject_blank", "Blank working directory or prompt throws", TestTags.Negative, () =>
            {
                AssertThrows<ArgumentNullException>(() => PiCommandBuilder.BuildRunArguments("", "hi", null, null, false, false));
                AssertThrows<ArgumentNullException>(() => PiCommandBuilder.BuildRunArguments("  ", "hi", null, null, false, false));
                AssertThrows<ArgumentNullException>(() => PiCommandBuilder.BuildRunArguments("/tmp/wd", "", null, null, false, false));
            }));

            // ---- Reasoning effort mapping ----
            cases.Add(Case("thinking_maps_levels", "Every Armada effort maps 1:1 to a pi thinking level; null omits the flag", TestTags.Positive, () =>
            {
                AssertEqual("off", ReasoningEffortTranslator.ToPiThinkingLevel(ReasoningEffortEnum.Off));
                AssertEqual("minimal", ReasoningEffortTranslator.ToPiThinkingLevel(ReasoningEffortEnum.Minimal));
                AssertEqual("low", ReasoningEffortTranslator.ToPiThinkingLevel(ReasoningEffortEnum.Low));
                AssertEqual("medium", ReasoningEffortTranslator.ToPiThinkingLevel(ReasoningEffortEnum.Medium));
                AssertEqual("high", ReasoningEffortTranslator.ToPiThinkingLevel(ReasoningEffortEnum.High));
                AssertNull(ReasoningEffortTranslator.ToPiThinkingLevel(null));
            }));

            // ---- Runtime name aliases ----
            cases.Add(Case("parser_aliases", "pi, Pi and pi-coding-agent parse to Pi; unknown names are still rejected", TestTags.Positive, () =>
            {
                foreach (string alias in new string[] { "pi", "Pi", "PI", "pi-coding-agent", " pi " })
                {
                    AssertTrue(AgentRuntimeParser.TryParse(alias, out AgentRuntimeEnum runtime), "expected '" + alias + "' to parse");
                    AssertEqual(AgentRuntimeEnum.Pi, runtime);
                }

                AssertFalse(AgentRuntimeParser.TryParse("pie", out AgentRuntimeEnum _), "unknown runtime rejected");
                AssertFalse(AgentRuntimeParser.TryParse("pi-agent", out AgentRuntimeEnum _), "unknown runtime rejected");
                AssertContains("pi", AgentRuntimeParser.AcceptedValues);
            }));

            cases.Add(Case("enum_serializes_as_pi", "The runtime serializes as \"Pi\" and is appended after Custom", TestTags.Positive, () =>
            {
                AssertEqual("\"Pi\"", System.Text.Json.JsonSerializer.Serialize(AgentRuntimeEnum.Pi));
                AssertEqual(AgentRuntimeEnum.Pi, System.Text.Json.JsonSerializer.Deserialize<AgentRuntimeEnum>("\"Pi\""));
                AssertTrue((int)AgentRuntimeEnum.Pi > (int)AgentRuntimeEnum.Custom, "appended last so ordinals do not shift");
                AssertTrue(AgentRuntimeCapabilities.SupportsPlanningSessions(AgentRuntimeEnum.Pi), "pi supports planning sessions");
                AssertFalse(CaptainLaunchIsolationPlanner.CarriesSessionToken(AgentRuntimeEnum.Pi), "pi has no isolation plan");
                AssertEqual("pi", Armada.Core.Harbor.HarborRuntimeDetector.CommandFor(AgentRuntimeEnum.Pi));
                AssertContains("pi.dev", RuntimeDetectionService.GetInstallHint(AgentRuntimeEnum.Pi));
            }));

            // ---- Launch through a recording stub ----
            cases.Add(CaseAsync("stub_launch_records_command_line", "A launch runs pi with the expected argv, prompt on stdin and --no-session in the working directory", TestTags.Positive, async () =>
            {
                if (OperatingSystem.IsWindows()) return;
                string root = TestTemp.NewDirectory("pi_launch");
                try
                {
                    string work = Path.Combine(root, "work");
                    Directory.CreateDirectory(work);
                    string stub = StubAgentProcesses.WritePiStub(Path.Combine(root, "stub"));
                    File.WriteAllText(Path.Combine(root, "stub", "pi-replay.jsonl"), String.Join("\n", PiStreamFixtures.PlainReply) + "\n");
                    Captain captain = new Captain("pi", AgentRuntimeEnum.Pi) { ReasoningEffort = ReasoningEffortEnum.Medium };
                    string prompt = "line one\nline two with 'quotes' and $HOME\nline three";
                    int? code = await LaunchAsync(new PiRuntime(Logging()) { ExecutablePath = stub }, work, prompt, "openai/gpt-5.2", captain).ConfigureAwait(false);

                    AssertEqual(0, code ?? -1);
                    string[] argv = File.ReadAllLines(Path.Combine(root, "stub", "pi-argv.txt"));
                    AssertEqual("-p|--mode|json|--no-session|--model|openai/gpt-5.2|--thinking|medium", String.Join("|", argv));
                    AssertEqual(prompt, File.ReadAllText(Path.Combine(root, "stub", "pi-stdin.txt")).TrimEnd('\n'), "the multi-line prompt arrives on stdin intact");
                    AssertEqual(Path.GetFullPath(work).TrimEnd('/'), ResolveRealPath(File.ReadAllText(Path.Combine(root, "stub", "pi-cwd.txt")).Trim()), "pi runs in the working directory");
                }
                finally
                {
                    try { Directory.Delete(root, true); } catch { }
                }
            }));

            cases.Add(CaseAsync("stub_launch_refuse_excludes_bash", "A captain without auto-approve launches pi with --exclude-tools bash,powershell", TestTags.Negative, async () =>
            {
                if (OperatingSystem.IsWindows()) return;
                string root = TestTemp.NewDirectory("pi_refuse");
                try
                {
                    string work = Path.Combine(root, "work");
                    Directory.CreateDirectory(work);
                    string stub = StubAgentProcesses.WritePiStub(Path.Combine(root, "stub"));
                    Captain refuse = CaptainRuntimeOptions.WithEffectiveAutoApprove(new Captain("pi", AgentRuntimeEnum.Pi), false);
                    await LaunchAsync(new PiRuntime(Logging()) { ExecutablePath = stub }, work, "hi", null, refuse).ConfigureAwait(false);
                    string[] argv = File.ReadAllLines(Path.Combine(root, "stub", "pi-argv.txt"));
                    AssertEqual("-p|--mode|json|--no-session|--exclude-tools|bash,powershell", String.Join("|", argv));
                }
                finally
                {
                    try { Directory.Delete(root, true); } catch { }
                }
            }));

            cases.Add(CaseAsync("stub_launch_approve_in_armada_loads_gate", "Approve in Armada launches pi with --extension pointing at the gate file in the bin, keeps bash, and passes the gate timeout; Refuse afterwards drops it", TestTags.Positive, async () =>
            {
                if (OperatingSystem.IsWindows()) return;
                string root = TestTemp.NewDirectory("pi_gate_launch");
                try
                {
                    string work = Path.Combine(root, "work");
                    Directory.CreateDirectory(work);
                    string stub = StubAgentProcesses.WritePiStub(Path.Combine(root, "stub"));
                    PiRuntime runtime = new PiRuntime(Logging()) { ExecutablePath = stub };
                    // The server launches an ApproveInArmada captain with its effective auto-approve off.
                    Captain approve = CaptainRuntimeOptions.WithEffectiveAutoApprove(new Captain("pi", AgentRuntimeEnum.Pi), false);
                    AssertTrue(CliPermissionPromptSetup.Configure(runtime, CliPermissionPolicyEnum.ApproveInArmada, 300));
                    await LaunchAsync(runtime, work, "hi", null, approve).ConfigureAwait(false);

                    string[] argv = File.ReadAllLines(Path.Combine(root, "stub", "pi-argv.txt"));
                    int at = Array.IndexOf(argv, "--extension");
                    AssertTrue(at >= 0 && at + 1 < argv.Length, "argv has --extension <path>: " + String.Join(" ", argv));
                    AssertTrue(File.Exists(argv[at + 1]), "the extension path exists: " + argv[at + 1]);
                    AssertEqual(PiRuntime.PermissionGateFileName, Path.GetFileName(argv[at + 1]));
                    AssertEqual(Path.Combine(AppContext.BaseDirectory, "Pi"), Path.GetDirectoryName(argv[at + 1]), "the extension is the copy in the bin");
                    AssertFalse(Array.Exists(argv, a => a == "--exclude-tools"), "the gate replaces the Refuse exclusion");
                    AssertEqual("420", File.ReadAllText(Path.Combine(root, "stub", "pi-permission-timeout.txt")), "Armada's 300 s timeout plus the CLI margin");

                    CliPermissionPromptSetup.Configure(runtime, CliPermissionPolicyEnum.Refuse, 300);
                    await LaunchAsync(runtime, work, "hi", null, approve).ConfigureAwait(false);
                    AssertEqual("-p|--mode|json|--no-session|--exclude-tools|bash,powershell", String.Join("|", File.ReadAllLines(Path.Combine(root, "stub", "pi-argv.txt"))), "a reused runtime keeps no gate");
                    AssertEqual("", File.ReadAllText(Path.Combine(root, "stub", "pi-permission-timeout.txt")), "no gate timeout without the gate");
                }
                finally
                {
                    try { Directory.Delete(root, true); } catch { }
                }
            }));

            cases.Add(CaseAsync("stub_launch_prompt_file_path", "With UsePromptFile (the Windows default) the prompt goes to a temp file passed as @file, stdin stays empty, and the file is removed after exit", TestTags.Positive, async () =>
            {
                if (OperatingSystem.IsWindows()) return;
                string root = TestTemp.NewDirectory("pi_prompt_file");
                try
                {
                    string work = Path.Combine(root, "work");
                    Directory.CreateDirectory(work);
                    string stub = StubAgentProcesses.WritePiStub(Path.Combine(root, "stub"));
                    File.WriteAllText(Path.Combine(root, "stub", "pi-replay.jsonl"), String.Join("\n", PiStreamFixtures.PlainReply) + "\n");
                    string prompt = "line one\nline two with 'quotes' and $HOME\nline three";
                    PiRuntime runtime = new PiRuntime(Logging()) { ExecutablePath = stub, UsePromptFile = true };
                    int? code = await LaunchAsync(runtime, work, prompt, "openai/gpt-5.2", new Captain("pi", AgentRuntimeEnum.Pi)).ConfigureAwait(false);

                    AssertEqual(0, code ?? -1);
                    string[] argv = File.ReadAllLines(Path.Combine(root, "stub", "pi-argv.txt"));
                    AssertEqual("-p|--mode|json|--no-session|--model|openai/gpt-5.2", String.Join("|", argv, 0, argv.Length - 1));
                    string fileArg = argv[argv.Length - 1];
                    AssertTrue(fileArg.StartsWith("@", StringComparison.Ordinal), "the last argument is @file: " + fileArg);
                    AssertEqual(prompt, File.ReadAllText(Path.Combine(root, "stub", "pi-prompt-file.txt")), "the file holds the multi-line prompt intact");
                    AssertEqual("", File.ReadAllText(Path.Combine(root, "stub", "pi-stdin.txt")), "nothing is written to stdin");
                    AssertFalse(File.Exists(fileArg.Substring(1)), "the prompt file is removed after the run");
                }
                finally
                {
                    try { Directory.Delete(root, true); } catch { }
                }
            }));

            // ---- Stream records (task 2.2) ----
            cases.Add(Case("fixture_lines_parse", "Every captured and derived fixture line parses as a pi record", TestTags.Positive, () =>
            {
                foreach (KeyValuePair<string, string[]> fixture in AllFixtures())
                {
                    foreach (string line in fixture.Value)
                    {
                        AssertTrue(PiStreamEvent.TryParse(line, out PiStreamEvent? evt) && evt != null, fixture.Key + ": " + line.Substring(0, Math.Min(80, line.Length)));
                        AssertTrue(PiRuntime.IsProtocolEventLine(line + "\r"), "a trailing CR is ignored");
                    }
                }
            }));

            cases.Add(Case("malformed_lines_rejected", "Malformed JSON, non-objects, unknown types and plain text are not pi records", TestTags.Negative, () =>
            {
                foreach (string line in new string[] { "", "   ", "hello", "{\"type\":\"message_end\"", "[{\"type\":\"agent_end\"}]", "\"agent_end\"", "42",
                    "{\"type\":\"object\",\"properties\":{}}", "{\"message\":{\"role\":\"assistant\"}}", "{\"type\":\"text\",\"part\":{\"text\":\"x\"}}" })
                {
                    AssertFalse(PiStreamEvent.TryParse(line, out PiStreamEvent? _), "rejected: " + line);
                    AssertFalse(PiRuntime.IsProtocolEventLine(line), "not protocol: " + line);
                }

                AssertFalse(PiStreamEvent.TryParse(null, out PiStreamEvent? none));
                AssertNull(none);
            }));

            cases.Add(Case("records_typed", "Session header, text deltas, thinking, tool executions and the final assistant message are typed", TestTags.Positive, () =>
            {
                AssertTrue(PiStreamEvent.TryParse(PiStreamFixtures.PlainReply[0], out PiStreamEvent? session));
                AssertEqual(PiStreamEvent.TypeSession, session!.Type);
                AssertEqual(3, session.Version ?? 0);

                PiMessage? final = null;
                List<string> tools = new List<string>();
                string text = String.Empty;
                foreach (string line in PiStreamFixtures.ToolCalls)
                {
                    PiStreamEvent evt = Parse(line);
                    if (evt.AssistantMessageEnd != null) final = evt.AssistantMessageEnd;
                    if (evt.Type == PiStreamEvent.TypeToolExecutionEnd) tools.Add(evt.ToolName + ":" + (evt.IsError == true ? "error" : "ok") + ":" + (evt.DurationMs.HasValue ? "ms" : "none"));
                    text += PiRuntime.TryExtractAssistantText(line) ?? String.Empty;
                }

                AssertEqual("bash:ok:ms|write:ok:ms|edit:ok:ms", String.Join("|", tools));
                AssertNotNull(final);
                AssertEqual("DONE", final!.Text);
                AssertEqual(PiMessage.StopReasonStop, final.StopReason);
                AssertContains("DONE", text, "joined text deltas include the final reply");
                AssertNull(PiRuntime.TryExtractAssistantText(PiStreamFixtures.PlainReply[0]), "the session header carries no reply text");

                string? bashResult = null;
                foreach (string line in PiStreamFixtures.ToolCalls)
                {
                    PiStreamEvent evt = Parse(line);
                    if (evt.Type == PiStreamEvent.TypeToolExecutionEnd && evt.ToolName == "bash") bashResult = PiToolResult.TextOf(evt.Result);
                }

                AssertEqual("hi\n", bashResult, "tool result text");
            }));

            // ---- Final message and exit code (task 2.3) ----
            cases.Add(Case("final_message_and_exit_codes", "Exit 0 is kept only for a run that reached agent_end with a normal stop; failed stops and EOF become nonzero", TestTags.Positive, () =>
            {
                AssertEqual("0:hello", Resolve(PiStreamFixtures.PlainReply, 0));
                AssertEqual("0:DONE", Resolve(PiStreamFixtures.ToolCalls, 0));
                AssertEqual("0:hello", Resolve(PiStreamFixtures.RetryRecovered, 0), "a recovered retry succeeds");
                AssertEqual("0:DONE", Resolve(PiStreamFixtures.McpToolCall, 0));
                AssertEqual("1:", Resolve(PiStreamFixtures.BadKey, 0), "stopReason error with exit 0");
                AssertEqual("1:", Resolve(PiStreamFixtures.RateLimited, 0));
                AssertEqual("1:", Resolve(PiStreamFixtures.RetryExhausted, 0));
                AssertEqual("1:", Resolve(PiStreamFixtures.LengthStop, 0), "stopReason length");
                AssertEqual("1:", Resolve(WithStop(PiStreamFixtures.PlainReply, PiMessage.StopReasonAborted), 0), "stopReason aborted");
                AssertEqual("143:", Resolve(PiStreamFixtures.Aborted, 143), "a signal exit is kept");
                AssertEqual("1:", Resolve(PiStreamFixtures.Aborted, 0), "EOF without agent_end");
                string[] cut = new string[PiStreamFixtures.PlainReply.Length - 2];
                Array.Copy(PiStreamFixtures.PlainReply, cut, cut.Length);
                AssertEqual("1:", Resolve(cut, 0), "a complete final message without agent_end is still a failure");
                AssertEqual("1:", Resolve(new string[] { PiStreamFixtures.PlainReply[0] }, 0), "no assistant message");
                AssertEqual("7:", Resolve(PiStreamFixtures.PlainReply, 7), "a nonzero exit is unchanged and writes nothing");
            }));

            cases.Add(Case("final_message_keeps_existing_file", "The final-message file is written only when missing or empty", TestTags.Negative, () =>
            {
                string root = TestTemp.NewDirectory("pi_final");
                try
                {
                    string path = Path.Combine(root, "final.txt");
                    File.WriteAllText(path, "written by the CLI");
                    PiFinalMessage final = new PiFinalMessage();
                    final.Begin(path);
                    foreach (string line in PiStreamFixtures.PlainReply) final.Observe(line);
                    AssertEqual(0, final.Complete(0) ?? -1);
                    AssertEqual("written by the CLI", File.ReadAllText(path));
                    final.Begin(null);
                    AssertEqual(String.Empty, final.Text(), "Begin drops the previous launch");
                    AssertNull(final.LastStopReason());
                }
                finally
                {
                    try { Directory.Delete(root, true); } catch { }
                }
            }));

            cases.Add(CaseAsync("stub_replay_completes_with_final_message", "A stub pi replaying the plain-reply fixture exits 0 with the reply as the final message", TestTags.Positive, async () =>
            {
                ReplayResult result = await ReplayAsync(PiStreamFixtures.ToolCalls, 0).ConfigureAwait(false);
                if (result.Skipped) return;
                AssertEqual(0, result.ExitCode ?? -1);
                AssertEqual("DONE", result.FinalMessage);
                AssertNull(result.ProviderError, "no provider error for a clean run");
            }));

            cases.Add(CaseAsync("stub_replay_bad_key_fails_classified", "A stub pi replaying the bad-key fixture exits 0 but the launch fails with a classified 401", TestTags.Negative, async () =>
            {
                ReplayResult result = await ReplayAsync(PiStreamFixtures.BadKey, 0).ConfigureAwait(false);
                if (result.Skipped) return;
                AssertEqual(PiFinalMessage.FailedRunExitCode, result.ExitCode ?? -1, "exit 0 overridden");
                AssertNull(result.FinalMessage, "no final message for a failed run");
                AssertNotNull(result.ProviderError);
                AssertEqual(401, result.ProviderError!.HttpStatusCode ?? 0);
                AssertEqual("authentication_error", result.ProviderError.ErrorType);
                AssertEqual(1, result.ProviderErrorCount, "raised once");
                AssertEqual(RuntimeFailureKindEnum.AuthFailure, RuntimeFailureClassifier.Classify(result.ExitCode, result.ProviderError));
            }));

            cases.Add(CaseAsync("stub_replay_exhausted_retry_raises_once", "agent_end and auto_retry_end report the same failure; the launch raises it once", TestTags.Negative, async () =>
            {
                ReplayResult result = await ReplayAsync(PiStreamFixtures.RetryExhausted, 0).ConfigureAwait(false);
                if (result.Skipped) return;
                AssertEqual(1, result.ProviderErrorCount);
                AssertEqual(529, result.ProviderError!.HttpStatusCode ?? 0);
                AssertEqual(PiFinalMessage.FailedRunExitCode, result.ExitCode ?? -1);
            }));

            cases.Add(CaseAsync("stub_replay_recovered_retry_is_clean", "A retried 529 that recovers raises no provider error and succeeds", TestTags.Positive, async () =>
            {
                ReplayResult result = await ReplayAsync(PiStreamFixtures.RetryRecovered, 0).ConfigureAwait(false);
                if (result.Skipped) return;
                AssertNull(result.ProviderError);
                AssertEqual(0, result.ExitCode ?? -1);
                AssertEqual("hello", result.FinalMessage);
            }));

            // ---- Mission streaming, log formatting, tool waits (tasks 2.6, 2.8) ----
            cases.Add(Case("mission_decoder_joins_text_and_tools", "The mission decoder joins text deltas into lines and turns tool executions into activities", TestTags.Positive, () =>
            {
                MissionStreamDecoder? decoder = MissionStreamDecoder.For(AgentRuntimeEnum.Pi);
                AssertNotNull(decoder);
                AssertTrue(MissionStreamDecoder.Supports(AgentRuntimeEnum.Pi));
                List<string> output = new List<string>();
                List<string> activities = new List<string>();
                foreach (string line in PiStreamFixtures.ToolCalls)
                {
                    MissionStreamDecodeResult decoded = decoder!.Decode(line, DateTime.UtcNow);
                    AssertTrue(decoded.Structured, "every pi record is structured");
                    output.AddRange(decoded.OutputLines);
                    foreach (Armada.Core.Models.RuntimeActivity activity in decoded.Activities) activities.Add(activity.Summary ?? String.Empty);
                }

                output.AddRange(decoder!.Flush(DateTime.UtcNow).OutputLines);
                AssertEqual("I'll execute those commands in sequence.|Now I'll write the file:|Now I'll edit the file to change x to y:|DONE", String.Join("|", output));
                AssertTrue(activities.Contains("Running echo hi"), String.Join(" / ", activities));
                AssertTrue(activities.Contains("Writing a.txt"), String.Join(" / ", activities));
                AssertTrue(activities.Contains("Editing a.txt"), String.Join(" / ", activities));
                AssertTrue(activities.Contains("Finished bash"), String.Join(" / ", activities));
                AssertFalse(decoder.Decode("plain warning", DateTime.UtcNow).Structured, "plain text stays plain");
            }));

            cases.Add(Case("mission_decoder_reports_failed_stop", "A provider error stop is written to the transcript", TestTags.Negative, () =>
            {
                MissionStreamDecoder decoder = MissionStreamDecoder.For(AgentRuntimeEnum.Pi)!;
                List<string> transcript = new List<string>();
                foreach (string line in PiStreamFixtures.BadKey) transcript.AddRange(decoder.Decode(line, DateTime.UtcNow).TranscriptLines);
                AssertTrue(transcript.Exists(t => t.StartsWith("Error: pi stopped (error): 401", StringComparison.Ordinal)), String.Join(" / ", transcript));
            }));

            cases.Add(Case("log_formatter_pi_events", "The runtime-log formatter shows finished text blocks and tool lines and drops deltas and lifecycle records", TestTags.Positive, () =>
            {
                List<string> shown = new List<string>();
                foreach (string line in PiStreamFixtures.ToolCalls)
                {
                    FormattedLogLine formatted = RuntimeLogFormatter.Format(line, AgentRuntimeEnum.Pi);
                    if (!formatted.Dropped) shown.Add(formatted.Text);
                }

                AssertEqual("I'll execute those commands in sequence.|-> tool bash|<- tool bash ok|Now I'll write the file:|-> tool write|<- tool write ok|Now I'll edit the file to change x to y:|-> tool edit|<- tool edit ok|DONE", String.Join("|", shown));
                FormattedLogLine error = new FormattedLogLine();
                foreach (string line in PiStreamFixtures.BadKey)
                {
                    FormattedLogLine formatted = RuntimeLogFormatter.Format(line, AgentRuntimeEnum.Pi);
                    if (!formatted.Dropped && formatted.Text.StartsWith("(error)", StringComparison.Ordinal)) error = formatted;
                }

                AssertContains("401", error.Text, "the provider error is shown");
            }));

            cases.Add(Case("tool_wait_tracker_pi", "The tool-wait tracker starts on tool_execution_start and clears on tool_execution_end", TestTags.Positive, () =>
            {
                RuntimeToolWaitTracker tracker = new RuntimeToolWaitTracker();
                DateTime now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
                foreach (string line in PiStreamFixtures.Aborted) tracker.Observe(AgentRuntimeEnum.Pi, line, now);
                AssertEqual(now, tracker.StartedUtc, "the aborted run is still waiting on its bash call");
                RuntimeToolWaitTracker clean = new RuntimeToolWaitTracker();
                foreach (string line in PiStreamFixtures.ToolCalls) clean.Observe(AgentRuntimeEnum.Pi, line, now);
                AssertNull(clean.StartedUtc, "every tool call ended");
            }));

            // ---- Mission instructions file (task 2.9) ----
            cases.Add(Case("instructions_file_is_agents_override", "pi missions write AGENTS.override.md", TestTags.Positive, () =>
            {
                AssertEqual("AGENTS.override.md", MissionPromptBuilder.GetInstructionsFileName("Pi"));
                AssertEqual("CLAUDE.md", MissionPromptBuilder.GetInstructionsFileName("ClaudeCode"), "other runtimes keep their own file");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.PiRuntime",
                displayName: "pi Runtime",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static PiStreamEvent Parse(string line)
        {
            AssertTrue(PiStreamEvent.TryParse(line, out PiStreamEvent? evt) && evt != null, "unparsed: " + line);
            return evt!;
        }

        private static Dictionary<string, string[]> AllFixtures()
        {
            return new Dictionary<string, string[]>
            {
                { "PlainReply", PiStreamFixtures.PlainReply },
                { "ToolCalls", PiStreamFixtures.ToolCalls },
                { "BadKey", PiStreamFixtures.BadKey },
                { "Aborted", PiStreamFixtures.Aborted },
                { "LengthStop", PiStreamFixtures.LengthStop },
                { "RateLimited", PiStreamFixtures.RateLimited },
                { "Overloaded", PiStreamFixtures.Overloaded },
                { "RetryRecovered", PiStreamFixtures.RetryRecovered },
                { "RetryExhausted", PiStreamFixtures.RetryExhausted },
                { "McpToolCall", PiStreamFixtures.McpToolCall }
            };
        }

        private static string[] WithStop(string[] lines, string stopReason)
        {
            string[] result = new string[lines.Length];
            for (int i = 0; i < lines.Length; i++) result[i] = lines[i].Replace("\"stopReason\":\"stop\"", "\"stopReason\":\"" + stopReason + "\"");
            return result;
        }

        private static string Resolve(string[] lines, int exitCode)
        {
            string root = TestTemp.NewDirectory("pi_resolve");
            try
            {
                string path = Path.Combine(root, "final.txt");
                PiFinalMessage final = new PiFinalMessage();
                final.Begin(path);
                foreach (string line in lines) final.Observe(line);
                int? code = final.Complete(exitCode);
                string text = File.Exists(path) ? File.ReadAllText(path) : String.Empty;
                return (code?.ToString() ?? "null") + ":" + text;
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static async Task<ReplayResult> ReplayAsync(string[] lines, int stubExitCode)
        {
            ReplayResult result = new ReplayResult();
            if (OperatingSystem.IsWindows())
            {
                result.Skipped = true;
                return result;
            }

            string root = TestTemp.NewDirectory("pi_replay");
            try
            {
                string work = Path.Combine(root, "work");
                Directory.CreateDirectory(work);
                string stubDirectory = Path.Combine(root, "stub");
                string stub = StubAgentProcesses.WritePiStub(stubDirectory);
                File.WriteAllText(Path.Combine(stubDirectory, "pi-replay.jsonl"), String.Join("\n", lines) + "\n");
                File.WriteAllText(Path.Combine(stubDirectory, "pi-exit.txt"), stubExitCode.ToString());
                string finalMessage = Path.Combine(root, "final.txt");
                PiRuntime runtime = new PiRuntime(Logging()) { ExecutablePath = stub };
                runtime.OnProviderError += (int pid, RuntimeProviderError error) =>
                {
                    lock (result)
                    {
                        result.ProviderErrorCount++;
                        result.ProviderError = RuntimeFailureClassifier.KeepMoreSignificant(result.ProviderError, error);
                    }
                };
                result.ExitCode = await LaunchAsync(runtime, work, "replay", null, null, finalMessage).ConfigureAwait(false);
                result.FinalMessage = File.Exists(finalMessage) ? File.ReadAllText(finalMessage) : null;
                return result;
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static LoggingModule Logging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static string ResolveRealPath(string path)
        {
            // macOS temp directories are reached through /var -> /private/var; compare the resolved forms.
            if (path.StartsWith("/private/", StringComparison.Ordinal) && !Path.GetTempPath().StartsWith("/private/", StringComparison.Ordinal))
                return path.Substring("/private".Length).TrimEnd('/');
            return path.TrimEnd('/');
        }

        private static async Task<int?> LaunchAsync(PiRuntime runtime, string work, string prompt, string? model, Captain? captain, string? finalMessageFile = null)
        {
            TaskCompletionSource<int?> exited = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
            runtime.OnProcessExited += (int pid, int? code) => exited.TrySetResult(code);
            await runtime.StartAsync(work, prompt, finalMessageFilePath: finalMessageFile, model: model, captain: captain).ConfigureAwait(false);
            Task finished = await Task.WhenAny(exited.Task, Task.Delay(30000)).ConfigureAwait(false);
            AssertTrue(finished == exited.Task, "stub pi did not exit");
            return await exited.Task.ConfigureAwait(false);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return CaseAsync(caseId, displayName, tag, () =>
            {
                body();
                return Task.CompletedTask;
            });
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.PiRuntime",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion

        #region Private-Classes

        private sealed class ReplayResult
        {
            public bool Skipped { get; set; } = false;
            public int? ExitCode { get; set; } = null;
            public string? FinalMessage { get; set; } = null;
            public RuntimeProviderError? ProviderError { get; set; } = null;
            public int ProviderErrorCount { get; set; } = 0;
        }

        #endregion
    }
}
