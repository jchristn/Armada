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
}
