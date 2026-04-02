namespace Armada.Test.Runtimes.Suites
{
    using Armada.Runtimes;
    using Armada.Test.Common;
    using SyslogLogging;

    public class ClaudeCodeRuntimeTests : TestSuite
    {
        public override string Name => "Claude Code Runtime Tests";

        private sealed class InspectableClaudeCodeRuntime : ClaudeCodeRuntime
        {
            public InspectableClaudeCodeRuntime(LoggingModule logging) : base(logging)
            {
            }

            public List<string> Args(string prompt, string? model = null) => BuildArguments(prompt, model);
        }

        private InspectableClaudeCodeRuntime CreateRuntime()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return new InspectableClaudeCodeRuntime(logging);
        }

        protected override async Task RunTestsAsync()
        {
            await RunTest("Name Returns Claude Code", () =>
            {
                ClaudeCodeRuntime runtime = CreateRuntime();
                AssertEqual("Claude Code", runtime.Name);
            });

            await RunTest("SupportsResume Returns True", () =>
            {
                ClaudeCodeRuntime runtime = CreateRuntime();
                AssertTrue(runtime.SupportsResume);
            });

            await RunTest("ExecutablePath Default Is Claude", () =>
            {
                ClaudeCodeRuntime runtime = CreateRuntime();
                AssertEqual("claude", runtime.ExecutablePath);
            });

            await RunTest("ExecutablePath Set Null Throws", () =>
            {
                ClaudeCodeRuntime runtime = CreateRuntime();
                AssertThrows<ArgumentNullException>(() => runtime.ExecutablePath = null!);
            });

            await RunTest("ExecutablePath Set Empty Throws", () =>
            {
                ClaudeCodeRuntime runtime = CreateRuntime();
                AssertThrows<ArgumentNullException>(() => runtime.ExecutablePath = "");
            });

            await RunTest("SkipPermissions Default Is True", () =>
            {
                ClaudeCodeRuntime runtime = CreateRuntime();
                AssertTrue(runtime.SkipPermissions);
            });

            await RunTest("BuildArguments Includes Model When Supplied", () =>
            {
                InspectableClaudeCodeRuntime runtime = CreateRuntime();
                List<string> args = runtime.Args("test prompt", "sonnet");
                int modelIndex = args.IndexOf("--model");
                AssertTrue(modelIndex >= 0);
                AssertEqual("sonnet", args[modelIndex + 1]);
            });

            await RunTest("IsRunningAsync Invalid ProcessId Returns False", async () =>
            {
                ClaudeCodeRuntime runtime = CreateRuntime();
                bool running = await runtime.IsRunningAsync(-1);
                AssertFalse(running);
            });
        }
    }
}
