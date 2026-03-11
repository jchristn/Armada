namespace Armada.Test.Runtimes
{
    using Armada.Test.Common;
    using Armada.Test.Runtimes.Suites;

    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            TestRunner runner = new TestRunner("ARMADA RUNTIME TEST SUITE");

            runner.AddSuite(new AgentRuntimeFactoryTests());
            runner.AddSuite(new BaseAgentRuntimeTests());
            runner.AddSuite(new ClaudeCodeRuntimeTests());
            runner.AddSuite(new CodexRuntimeTests());

            int exitCode = await runner.RunAllAsync().ConfigureAwait(false);
            return exitCode;
        }
    }
}
