namespace Armada.Test.Unit.Suites.Models
{
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;

    public class CaptainModelTests : TestSuite
    {
        public override string Name => "Captain Model";

        protected override async Task RunTestsAsync()
        {
            await RunTest("Captain DefaultConstructor GeneratesIdWithPrefix", () =>
            {
                Captain captain = new Captain();
                AssertStartsWith(Constants.CaptainIdPrefix, captain.Id);
            });

            await RunTest("Captain NameRuntimeConstructor SetsProperties", () =>
            {
                Captain captain = new Captain("claude-1", AgentRuntimeEnum.ClaudeCode);
                AssertEqual("claude-1", captain.Name);
                AssertEqual(AgentRuntimeEnum.ClaudeCode, captain.Runtime);
            });

            await RunTest("Captain DefaultValues AreCorrect", () =>
            {
                Captain captain = new Captain();
                AssertEqual("Captain", captain.Name);
                AssertEqual(AgentRuntimeEnum.ClaudeCode, captain.Runtime);
                AssertEqual(CaptainStateEnum.Idle, captain.State);
                AssertNull(captain.CurrentMissionId);
                AssertNull(captain.CurrentDockId);
                AssertNull(captain.ProcessId);
                AssertEqual(0, captain.RecoveryAttempts);
                AssertNull(captain.LastHeartbeatUtc);
            });

            await RunTest("Captain SetName Null Throws", () =>
            {
                Captain captain = new Captain();
                AssertThrows<ArgumentNullException>(() => captain.Name = null!);
            });

            await RunTest("Captain Serialization RoundTrip", () =>
            {
                Captain captain = new Captain("test-captain", AgentRuntimeEnum.Codex);
                captain.State = CaptainStateEnum.Working;
                captain.CurrentMissionId = "msn_test";
                captain.ProcessId = 12345;
                captain.RecoveryAttempts = 2;

                string json = JsonSerializer.Serialize(captain);
                Captain deserialized = JsonSerializer.Deserialize<Captain>(json)!;

                AssertEqual(captain.Id, deserialized.Id);
                AssertEqual(captain.Name, deserialized.Name);
                AssertEqual(captain.Runtime, deserialized.Runtime);
                AssertEqual(captain.State, deserialized.State);
                AssertEqual(captain.ProcessId, deserialized.ProcessId);
                AssertEqual(captain.RecoveryAttempts, deserialized.RecoveryAttempts);
            });

            await RunTest("Captain StateEnum SerializesAsString", () =>
            {
                Captain captain = new Captain();
                captain.State = CaptainStateEnum.Working;

                string json = JsonSerializer.Serialize(captain);
                AssertContains("\"Working\"", json);
            });
        }
    }
}
