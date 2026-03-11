namespace Armada.Test.Runtimes.Suites
{
    using Armada.Runtimes;
    using Armada.Test.Common;
    using SyslogLogging;

    public class CodexRuntimeTests : TestSuite
    {
        public override string Name => "Codex Runtime Tests";

        private CodexRuntime CreateRuntime()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return new CodexRuntime(logging);
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
                CodexRuntime runtime = CreateRuntime();
                AssertEqual("full-auto", runtime.ApprovalMode);
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
