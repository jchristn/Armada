namespace Armada.Test.Runtimes.Suites
{
    using Armada.Runtimes;
    using Armada.Test.Common;
    using SyslogLogging;

    public class CodexRuntimeTests : TestSuite
    {
        public override string Name => "Codex Runtime Tests";

        private sealed class InspectableCodexRuntime : CodexRuntime
        {
            public InspectableCodexRuntime(LoggingModule logging) : base(logging)
            {
            }

            public string Command() => GetCommand();

            public List<string> Args(string prompt, bool includePrompt) => BuildArguments(prompt, includePrompt);

            public bool UsesStandardInput(string prompt) => UseStandardInputForPrompt(prompt);
        }

        private InspectableCodexRuntime CreateRuntime()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return new InspectableCodexRuntime(logging);
        }

        protected override async Task RunTestsAsync()
        {
            await RunTest("Name Returns Codex", () =>
            {
                CodexRuntime runtime = CreateRuntime();
                AssertEqual("Codex", runtime.Name);
            });

            await RunTest("SupportsResume Returns False", () =>
            {
                CodexRuntime runtime = CreateRuntime();
                AssertFalse(runtime.SupportsResume);
            });

            await RunTest("ExecutablePath Default Is Codex", () =>
            {
                CodexRuntime runtime = CreateRuntime();
                AssertEqual("codex", runtime.ExecutablePath);
            });

            await RunTest("ApprovalMode Default Is FullAuto", () =>
            {
                InspectableCodexRuntime runtime = CreateRuntime();
                AssertEqual("full-auto", runtime.ApprovalMode);
            });

            await RunTest("BuildArguments Uses Exec FullAuto", () =>
            {
                InspectableCodexRuntime runtime = CreateRuntime();
                List<string> args = runtime.Args("test prompt", includePrompt: true);
                AssertEqual("exec", args[0]);
                AssertTrue(args.Contains("--full-auto"));
                AssertEqual("test prompt", args[args.Count - 1]);
            });

            await RunTest("BuildArguments Uses Dash Placeholder When Prompt Comes From Stdin", () =>
            {
                InspectableCodexRuntime runtime = CreateRuntime();
                List<string> args = runtime.Args("test prompt", includePrompt: false);
                AssertEqual("exec", args[0]);
                AssertEqual("-", args[args.Count - 1]);
            });

            await RunTest("UseStandardInputForPrompt Returns True", () =>
            {
                InspectableCodexRuntime runtime = CreateRuntime();
                AssertTrue(runtime.UsesStandardInput("test prompt"));
            });

            await RunTest("Windows Command Resolves Cmd Wrapper", () =>
            {
                InspectableCodexRuntime runtime = CreateRuntime();
                string command = runtime.Command();

                if (OperatingSystem.IsWindows())
                    AssertTrue(command.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || command.Equals("codex", StringComparison.OrdinalIgnoreCase), "Expected codex command to resolve to .cmd or codex");
                else
                    AssertEqual("codex", command);
            });

            await RunTest("ExecutablePath Set Null Throws", () =>
            {
                CodexRuntime runtime = CreateRuntime();
                AssertThrows<ArgumentNullException>(() => runtime.ExecutablePath = null!);
            });

            await RunTest("IsRunningAsync Invalid ProcessId Returns False", async () =>
            {
                CodexRuntime runtime = CreateRuntime();
                bool running = await runtime.IsRunningAsync(-1);
                AssertFalse(running);
            });
        }
    }
}
