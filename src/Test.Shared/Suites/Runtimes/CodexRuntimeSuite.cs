namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Runtimes;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="CodexRuntime"/> metadata and argument construction. Cases verify the
    /// runtime name and resume support, the default executable path and approval mode, exec-mode argument
    /// building with platform-appropriate auto/dangerous flags, model and final-message-path inclusion,
    /// Windows command wrapper resolution, null executable-path rejection, and invalid process-id
    /// liveness handling. Argument construction is exercised through an inspectable subclass.
    /// </summary>
    public sealed class CodexRuntimeSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Runtimes.CodexRuntime";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Codex Runtime suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("name_returns_codex", "Name Returns Codex", TestTags.Positive, () =>
            {
                CodexRuntime runtime = CreateRuntime();
                AssertEqual("Codex", runtime.Name);
            }));

            cases.Add(Case("supports_resume_returns_false", "SupportsResume Returns False", TestTags.Positive, () =>
            {
                CodexRuntime runtime = CreateRuntime();
                AssertFalse(runtime.SupportsResume);
            }));

            cases.Add(Case("executable_path_default_is_codex", "ExecutablePath Default Is Codex", TestTags.Positive, () =>
            {
                CodexRuntime runtime = CreateRuntime();
                AssertEqual("codex", runtime.ExecutablePath);
            }));

            cases.Add(Case("approval_mode_default_is_full_auto", "ApprovalMode Default Is FullAuto", TestTags.Positive, () =>
            {
                InspectableCodexRuntime runtime = CreateRuntime();
                AssertEqual("full-auto", runtime.ApprovalMode);
            }));

            cases.Add(Case("build_arguments_uses_exec_with_platform_appropriate_auto_mode", "BuildArguments Uses Exec With Platform Appropriate Auto Mode", TestTags.Positive, () =>
            {
                InspectableCodexRuntime runtime = CreateRuntime();
                List<string> args = runtime.Args("test prompt");
                AssertEqual("exec", args[0]);
                AssertTrue(args.Contains("--skip-git-repo-check"), "chat turns run outside a git repository");
                if (OperatingSystem.IsWindows())
                    AssertTrue(args.Contains("--dangerously-bypass-approvals-and-sandbox"));
                else
                {
                    AssertFalse(args.Contains("--full-auto"), "codex exec no longer accepts --full-auto");
                    int sandbox = args.IndexOf("--sandbox");
                    AssertTrue(sandbox >= 0, "expected --sandbox");
                    AssertEqual("workspace-write", args[sandbox + 1]);
                }
                // The prompt is delivered on stdin, not as a CLI argument (avoids Windows cmd.exe
                // multi-line-argument truncation), so it must not appear in the argument list.
                AssertFalse(args.Contains("test prompt"));
            }));

            cases.Add(Case("build_arguments_dangerous_uses_dangerous_flag", "BuildArguments Dangerous Uses Dangerous Flag", TestTags.Positive, () =>
            {
                InspectableCodexRuntime runtime = CreateRuntime();
                runtime.ApprovalMode = "dangerous";
                List<string> args = runtime.Args("test prompt");
                AssertEqual("exec", args[0]);
                AssertTrue(args.Contains("--dangerously-bypass-approvals-and-sandbox"));
                AssertFalse(args.Contains("test prompt"));
            }));

            cases.Add(Case("delivers_prompt_via_stdin_not_argument", "Delivers Prompt Via Stdin Not Argument", TestTags.Positive, () =>
            {
                InspectableCodexRuntime runtime = CreateRuntime();
                AssertTrue(runtime.PromptViaStdin);
                List<string> args = runtime.Args("line one\nline two\nUser: real question");
                AssertFalse(args.Contains("line one\nline two\nUser: real question"));
            }));

            cases.Add(Case("build_arguments_includes_model_when_supplied", "BuildArguments Includes Model When Supplied", TestTags.Positive, () =>
            {
                InspectableCodexRuntime runtime = CreateRuntime();
                List<string> args = runtime.Args("test prompt", "gpt-5.4");
                int modelIndex = args.IndexOf("--model");
                AssertTrue(modelIndex >= 0);
                AssertEqual("gpt-5.4", args[modelIndex + 1]);
            }));

            cases.Add(Case("build_arguments_includes_final_message_path_when_supplied", "BuildArguments Includes Final Message Path When Supplied", TestTags.Positive, () =>
            {
                InspectableCodexRuntime runtime = CreateRuntime();
                List<string> args = runtime.Args("test prompt", "gpt-5.4", "C:/temp/final-message.txt");
                int outputIndex = args.IndexOf("--output-last-message");
                AssertTrue(outputIndex >= 0);
                AssertEqual("C:/temp/final-message.txt", args[outputIndex + 1]);
            }));

            cases.Add(Case("windows_command_resolves_cmd_wrapper", "Windows Command Resolves Cmd Wrapper", TestTags.Positive, () =>
            {
                InspectableCodexRuntime runtime = CreateRuntime();
                string command = runtime.Command();

                if (OperatingSystem.IsWindows())
                    AssertTrue(command.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || command.Equals("codex", StringComparison.OrdinalIgnoreCase), "Expected codex command to resolve to .cmd or codex");
                else
                    AssertEqual("codex", command);
            }));

            cases.Add(Case("executable_path_set_null_throws", "ExecutablePath Set Null Throws", TestTags.Negative, () =>
            {
                CodexRuntime runtime = CreateRuntime();
                AssertThrows<ArgumentNullException>(() => runtime.ExecutablePath = null!);
            }));

            cases.Add(CaseAsync("is_running_async_invalid_process_id_returns_false", "IsRunningAsync Invalid ProcessId Returns False", TestTags.Negative, async () =>
            {
                CodexRuntime runtime = CreateRuntime();
                bool running = await runtime.IsRunningAsync(-1);
                AssertFalse(running);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Codex Runtime",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static InspectableCodexRuntime CreateRuntime()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return new InspectableCodexRuntime(logging);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
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
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion

        #region Private-Types

        /// <summary>
        /// Inspectable subclass exposing protected command and argument construction for assertions.
        /// </summary>
        private sealed class InspectableCodexRuntime : CodexRuntime
        {
            public InspectableCodexRuntime(LoggingModule logging) : base(logging)
            {
            }

            public string Command() => GetCommand();

            public bool PromptViaStdin => UsePromptStdin;

            public List<string> Args(string prompt, string? model = null, string? finalMessageFilePath = null) =>
                BuildArguments(Path.GetTempPath(), prompt, model, finalMessageFilePath, null);
        }

        #endregion
    }
}
