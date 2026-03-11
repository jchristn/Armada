namespace Armada.Test.Runtimes.Suites
{
    using Armada.Runtimes;
    using Armada.Test.Common;
    using SyslogLogging;

    /// <summary>
    /// Minimal concrete implementation for testing BaseAgentRuntime.
    /// Uses a simple cross-platform command (dotnet --version).
    /// </summary>
    internal class TestAgentRuntime : BaseAgentRuntime
    {
        public override string Name => "TestRuntime";
        public override bool SupportsResume => false;

        public string CommandOverride { get; set; } = "dotnet";
        public List<string> ArgsOverride { get; set; } = new List<string> { "--version" };

        public TestAgentRuntime(LoggingModule logging) : base(logging)
        {
        }

        protected override string GetCommand() => CommandOverride;

        protected override List<string> BuildArguments(string prompt) => ArgsOverride;
    }

    public class BaseAgentRuntimeTests : TestSuite
    {
        public override string Name => "Base Agent Runtime Tests";

        private LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        protected override async Task RunTestsAsync()
        {
            await RunTest("Constructor Null Logging Throws", () =>
            {
                AssertThrows<ArgumentNullException>(() => new TestAgentRuntime(null!));
            });

            await RunTest("StartAsync Null WorkingDirectory Throws", async () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                await AssertThrowsAsync<ArgumentNullException>(() => runtime.StartAsync(null!, "prompt"));
            });

            await RunTest("StartAsync Empty WorkingDirectory Throws", async () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                await AssertThrowsAsync<ArgumentNullException>(() => runtime.StartAsync("", "prompt"));
            });

            await RunTest("StartAsync Null Prompt Throws", async () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                await AssertThrowsAsync<ArgumentNullException>(() => runtime.StartAsync("/tmp", null!));
            });

            await RunTest("StartAsync Empty Prompt Throws", async () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                await AssertThrowsAsync<ArgumentNullException>(() => runtime.StartAsync("/tmp", ""));
            });

            await RunTest("IsRunningAsync Invalid ProcessId Returns False", async () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                bool running = await runtime.IsRunningAsync(99999999);
                AssertFalse(running);
            });

            await RunTest("StopAsync Invalid ProcessId Does Not Throw", async () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                await runtime.StopAsync(99999999);
            });

            await RunTest("Name Returns Expected", () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                AssertEqual("TestRuntime", runtime.Name);
            });

            await RunTest("SupportsResume Returns False", () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                AssertFalse(runtime.SupportsResume);
            });

            await RunTest("StartAsync Valid Command Returns ProcessId", async () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                string tempDir = Path.GetTempPath();

                int pid = await runtime.StartAsync(tempDir, "test prompt");
                AssertTrue(pid > 0);

                // Wait briefly for process to finish (dotnet --version exits quickly)
                await Task.Delay(2000);
            });

            await RunTest("StartAsync Invalid Command Throws", async () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                runtime.CommandOverride = "nonexistent_command_" + Guid.NewGuid().ToString("N");

                string tempDir = Path.GetTempPath();
                bool threw = false;
                try
                {
                    await runtime.StartAsync(tempDir, "test prompt");
                }
                catch
                {
                    threw = true;
                }
                AssertTrue(threw, "Expected exception for invalid command");
            });

            await RunTest("OnOutputReceived Fires For Output", async () =>
            {
                TestAgentRuntime runtime = new TestAgentRuntime(CreateLogging());
                string tempDir = Path.GetTempPath();

                List<string> outputLines = new List<string>();
                runtime.OnOutputReceived += (pid, line) =>
                {
                    lock (outputLines)
                    {
                        outputLines.Add(line);
                    }
                };

                int pid = await runtime.StartAsync(tempDir, "test prompt");

                // Wait for process to complete and events to fire
                await Task.Delay(3000);

                AssertTrue(outputLines.Count > 0, "Expected at least one output line");
            });
        }
    }
}
