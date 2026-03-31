namespace Armada.Test.Unit.Suites.Models
{
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;

    /// <summary>
    /// Extended model tests for Captain covering persona, instruction, and edge case properties.
    /// </summary>
    public class CaptainExtendedModelTests : TestSuite
    {
        /// <summary>
        /// Test suite name.
        /// </summary>
        public override string Name => "Captain Extended Model";

        /// <summary>
        /// Run all extended captain model tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            await RunTest("Captain SystemInstructions DefaultsToNull", () =>
            {
                Captain captain = new Captain();
                AssertNull(captain.SystemInstructions);
            });

            await RunTest("Captain SystemInstructions CanBeSetAndCleared", () =>
            {
                Captain captain = new Captain();
                captain.SystemInstructions = "You are a careful code reviewer.";
                AssertEqual("You are a careful code reviewer.", captain.SystemInstructions);
                captain.SystemInstructions = null;
                AssertNull(captain.SystemInstructions);
            });

            await RunTest("Captain AllowedPersonas DefaultsToNull", () =>
            {
                Captain captain = new Captain();
                AssertNull(captain.AllowedPersonas);
            });

            await RunTest("Captain AllowedPersonas CanBeSetToJsonArray", () =>
            {
                Captain captain = new Captain();
                captain.AllowedPersonas = "[\"Worker\", \"Judge\"]";
                AssertEqual("[\"Worker\", \"Judge\"]", captain.AllowedPersonas);
            });

            await RunTest("Captain PreferredPersona DefaultsToNull", () =>
            {
                Captain captain = new Captain();
                AssertNull(captain.PreferredPersona);
            });

            await RunTest("Captain PreferredPersona CanBeSet", () =>
            {
                Captain captain = new Captain();
                captain.PreferredPersona = "Worker";
                AssertEqual("Worker", captain.PreferredPersona);
            });

            await RunTest("Captain TenantId DefaultsToNull", () =>
            {
                Captain captain = new Captain();
                AssertNull(captain.TenantId);
            });

            await RunTest("Captain UserId DefaultsToNull", () =>
            {
                Captain captain = new Captain();
                AssertNull(captain.UserId);
            });

            await RunTest("Captain UniqueIds AcrossInstances", () =>
            {
                Captain c1 = new Captain();
                Captain c2 = new Captain();
                AssertNotEqual(c1.Id, c2.Id);
            });

            await RunTest("Captain SetId Null Throws", () =>
            {
                Captain captain = new Captain();
                AssertThrows<ArgumentNullException>(() => captain.Id = null!);
            });

            await RunTest("Captain SetId Empty Throws", () =>
            {
                Captain captain = new Captain();
                AssertThrows<ArgumentNullException>(() => captain.Id = "");
            });

            await RunTest("Captain SetName Empty Throws", () =>
            {
                Captain captain = new Captain();
                AssertThrows<ArgumentNullException>(() => captain.Name = "");
            });

            await RunTest("Captain Serialization IncludesAllProperties", () =>
            {
                Captain captain = new Captain("full-captain", AgentRuntimeEnum.Codex);
                captain.TenantId = "tenant_1";
                captain.UserId = "user_1";
                captain.SystemInstructions = "Be careful";
                captain.AllowedPersonas = "[\"Worker\"]";
                captain.PreferredPersona = "Worker";
                captain.State = CaptainStateEnum.Working;
                captain.CurrentMissionId = "msn_test";
                captain.CurrentDockId = "dck_test";
                captain.ProcessId = 9999;
                captain.RecoveryAttempts = 3;

                string json = JsonSerializer.Serialize(captain);
                Captain deserialized = JsonSerializer.Deserialize<Captain>(json)!;

                AssertEqual(captain.Id, deserialized.Id);
                AssertEqual("tenant_1", deserialized.TenantId);
                AssertEqual("user_1", deserialized.UserId);
                AssertEqual("full-captain", deserialized.Name);
                AssertEqual(AgentRuntimeEnum.Codex, deserialized.Runtime);
                AssertEqual("Be careful", deserialized.SystemInstructions);
                AssertEqual("[\"Worker\"]", deserialized.AllowedPersonas);
                AssertEqual("Worker", deserialized.PreferredPersona);
                AssertEqual(CaptainStateEnum.Working, deserialized.State);
                AssertEqual("msn_test", deserialized.CurrentMissionId);
                AssertEqual("dck_test", deserialized.CurrentDockId);
                AssertEqual(9999, deserialized.ProcessId);
                AssertEqual(3, deserialized.RecoveryAttempts);
            });

            await RunTest("Captain RuntimeEnum SerializesAsString", () =>
            {
                Captain captain = new Captain();
                captain.Runtime = AgentRuntimeEnum.Codex;
                string json = JsonSerializer.Serialize(captain);
                AssertContains("\"Codex\"", json);
            });

            await RunTest("Captain RecoveryAttempts DefaultsToZero", () =>
            {
                Captain captain = new Captain();
                AssertEqual(0, captain.RecoveryAttempts);
            });

            await RunTest("Captain CreatedUtc IsSetOnConstruction", () =>
            {
                DateTime before = DateTime.UtcNow.AddSeconds(-1);
                Captain captain = new Captain();
                DateTime after = DateTime.UtcNow.AddSeconds(1);
                AssertTrue(captain.CreatedUtc >= before);
                AssertTrue(captain.CreatedUtc <= after);
            });

            await RunTest("Captain LastUpdateUtc IsSetOnConstruction", () =>
            {
                DateTime before = DateTime.UtcNow.AddSeconds(-1);
                Captain captain = new Captain();
                DateTime after = DateTime.UtcNow.AddSeconds(1);
                AssertTrue(captain.LastUpdateUtc >= before);
                AssertTrue(captain.LastUpdateUtc <= after);
            });
        }
    }
}
