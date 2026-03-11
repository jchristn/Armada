namespace Armada.Test.Unit.Suites.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Services;
    using Armada.Test.Common;

    public class RuntimeDetectionServiceTests : TestSuite
    {
        public override string Name => "Runtime Detection Service";

        protected override async Task RunTestsAsync()
        {
            await RunTest("IsCommandAvailable Git ReturnsTrue", () =>
            {
                bool result = RuntimeDetectionService.IsCommandAvailable("git");
                AssertTrue(result);
            });

            await RunTest("IsCommandAvailable NonExistentCommand ReturnsFalse", () =>
            {
                bool result = RuntimeDetectionService.IsCommandAvailable("armada_definitely_nonexistent_cmd_xyz");
                AssertFalse(result);
            });

            await RunTest("DetectAllRuntimes DoesNotThrow", () =>
            {
                List<AgentRuntimeEnum> runtimes = RuntimeDetectionService.DetectAllRuntimes();
                AssertNotNull(runtimes);
            });

            await RunTest("DetectDefaultRuntime DoesNotThrow", () =>
            {
                AgentRuntimeEnum? runtime = RuntimeDetectionService.DetectDefaultRuntime();
                // Result is environment-dependent, no assertion on value
            });

            await RunTest("GetInstallHint ClaudeCode ReturnsNpmCommand", () =>
            {
                string hint = RuntimeDetectionService.GetInstallHint(AgentRuntimeEnum.ClaudeCode);
                AssertContains("npm install", hint);
                AssertContains("claude-code", hint);
            });

            await RunTest("GetInstallHint Codex ReturnsNpmCommand", () =>
            {
                string hint = RuntimeDetectionService.GetInstallHint(AgentRuntimeEnum.Codex);
                AssertContains("npm install", hint);
                AssertContains("codex", hint);
            });

            await RunTest("GetInstallHint Custom ReturnsGenericMessage", () =>
            {
                string hint = RuntimeDetectionService.GetInstallHint(AgentRuntimeEnum.Custom);
                AssertContains("documentation", hint);
            });
        }
    }
}
