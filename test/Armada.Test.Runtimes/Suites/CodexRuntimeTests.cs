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

            public List<string> Args(string prompt) => BuildArguments(prompt);

            public List<string> Args(string prompt, string? model) => BuildArguments(prompt, model);
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

            await RunTest("BuildArguments Uses Exec With Platform Appropriate Auto Mode", () =>
            {
                InspectableCodexRuntime runtime = CreateRuntime();
                List<string> args = runtime.Args("test prompt");
                AssertEqual("exec", args[0]);
                if (OperatingSystem.IsWindows())
                    AssertTrue(args.Contains("--dangerously-bypass-approvals-and-sandbox"));
                else
                    AssertTrue(args.Contains("--full-auto"));
                AssertEqual("test prompt", args[args.Count - 1]);
            });

            await RunTest("BuildArguments Dangerous Uses Dangerous Flag", () =>
            {
                InspectableCodexRuntime runtime = CreateRuntime();
                runtime.ApprovalMode = "dangerous";
                List<string> args = runtime.Args("test prompt");
                AssertEqual("exec", args[0]);
                AssertTrue(args.Contains("--dangerously-bypass-approvals-and-sandbox"));
                AssertEqual("test prompt", args[args.Count - 1]);
            });

            await RunTest("BuildArguments Includes Model When Specified", () =>
            {
                InspectableCodexRuntime runtime = CreateRuntime();
                List<string> args = runtime.Args("test prompt", "gpt-test");
                AssertEqual("exec", args[0]);
                AssertEqual("--model", args[1]);
                AssertEqual("gpt-test", args[2]);
                AssertEqual("test prompt", args[args.Count - 1]);
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
