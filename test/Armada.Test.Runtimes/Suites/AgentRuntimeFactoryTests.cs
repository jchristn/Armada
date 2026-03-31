namespace Armada.Test.Runtimes.Suites
{
    using Armada.Core.Enums;
    using Armada.Runtimes;
    using Armada.Runtimes.Interfaces;
    using Armada.Test.Common;
    using SyslogLogging;

    public class AgentRuntimeFactoryTests : TestSuite
    {
        public override string Name => "Agent Runtime Factory Tests";

        private AgentRuntimeFactory CreateFactory()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return new AgentRuntimeFactory(logging);
        }

        protected override async Task RunTestsAsync()
        {
            await RunTest("Create ClaudeCode Returns ClaudeCodeRuntime", () =>
            {
                AgentRuntimeFactory factory = CreateFactory();
                IAgentRuntime runtime = factory.Create(AgentRuntimeEnum.ClaudeCode);
                AssertNotNull(runtime);
                AssertEqual("Claude Code", runtime.Name);
            });

            await RunTest("Create Codex Returns CodexRuntime", () =>
            {
                AgentRuntimeFactory factory = CreateFactory();
                IAgentRuntime runtime = factory.Create(AgentRuntimeEnum.Codex);
                AssertNotNull(runtime);
                AssertEqual("Codex", runtime.Name);
            });

            await RunTest("Create Custom Without Registration Throws", () =>
            {
                AgentRuntimeFactory factory = CreateFactory();
                AssertThrows<InvalidOperationException>(() => factory.Create(AgentRuntimeEnum.Custom));
            });

            await RunTest("Create Custom By Name With Registration Succeeds", () =>
            {
                AgentRuntimeFactory factory = CreateFactory();
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;

                factory.Register("test-runtime", () => new ClaudeCodeRuntime(logging));

                IAgentRuntime runtime = factory.Create("test-runtime");
                AssertNotNull(runtime);
            });

            await RunTest("Create Custom By Name Not Registered Throws", () =>
            {
                AgentRuntimeFactory factory = CreateFactory();
                AssertThrows<InvalidOperationException>(() => factory.Create("nonexistent"));
            });

            await RunTest("Register Null Name Throws", () =>
            {
                AgentRuntimeFactory factory = CreateFactory();
                AssertThrows<ArgumentNullException>(() => factory.Register(null!, () => null!));
            });

            await RunTest("Register Null Factory Throws", () =>
            {
                AgentRuntimeFactory factory = CreateFactory();
                AssertThrows<ArgumentNullException>(() => factory.Register("test", null!));
            });
        }
    }
}
