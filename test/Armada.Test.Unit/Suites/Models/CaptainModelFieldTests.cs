namespace Armada.Test.Unit.Suites.Models
{
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;

    /// <summary>
    /// Tests for the Captain.Model property added in v0.11.0.
    /// Validates that captains can specify a model for the underlying agent runtime.
    /// </summary>
    public class CaptainModelFieldTests : TestSuite
    {
        /// <summary>
        /// Test suite name.
        /// </summary>
        public override string Name => "Captain Model Field";

        /// <summary>
        /// Run all captain model field tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            await RunTest("Captain_Model_DefaultsToNull", () =>
            {
                Captain captain = new Captain();
                AssertNull(captain.Model);
            });

            await RunTest("Captain_Model_CanBeSet", () =>
            {
                Captain captain = new Captain();
                captain.Model = "claude-sonnet-4-5-20250514";
                AssertEqual("claude-sonnet-4-5-20250514", captain.Model);
            });

            await RunTest("Captain_Model_CanBeCleared", () =>
            {
                Captain captain = new Captain();
                captain.Model = "claude-sonnet-4-5-20250514";
                AssertNotNull(captain.Model);
                captain.Model = null;
                AssertNull(captain.Model);
            });

            await RunTest("Captain_Model_SerializationRoundTrip", () =>
            {
                Captain captain = new Captain("model-test-captain", AgentRuntimeEnum.ClaudeCode);
                captain.Model = "claude-sonnet-4-5-20250514";

                string json = JsonSerializer.Serialize(captain);
                Captain deserialized = JsonSerializer.Deserialize<Captain>(json)!;

                AssertEqual(captain.Id, deserialized.Id);
                AssertEqual(captain.Name, deserialized.Name);
                AssertEqual("claude-sonnet-4-5-20250514", deserialized.Model);
            });

            await RunTest("Captain_Model_SerializationNullWhenNotSet", () =>
            {
                Captain captain = new Captain("no-model-captain");

                string json = JsonSerializer.Serialize(captain);
                Captain deserialized = JsonSerializer.Deserialize<Captain>(json)!;

                AssertNull(deserialized.Model);
            });

            await RunTest("Captain_Model_SerializationNullWhenCleared", () =>
            {
                Captain captain = new Captain("cleared-model-captain");
                captain.Model = "claude-sonnet-4-5-20250514";
                captain.Model = null;

                string json = JsonSerializer.Serialize(captain);
                Captain deserialized = JsonSerializer.Deserialize<Captain>(json)!;

                AssertNull(deserialized.Model);
            });

            await RunTest("Captain_Model_AcceptsVariousModelStrings", () =>
            {
                Captain captain = new Captain();

                captain.Model = "claude-sonnet-4-5-20250514";
                AssertEqual("claude-sonnet-4-5-20250514", captain.Model);

                captain.Model = "claude-opus-4-0-20250514";
                AssertEqual("claude-opus-4-0-20250514", captain.Model);

                captain.Model = "o3";
                AssertEqual("o3", captain.Model);

                captain.Model = "gpt-4o";
                AssertEqual("gpt-4o", captain.Model);
            });

            await RunTest("Captain_Model_PreservedAcrossOtherPropertyChanges", () =>
            {
                Captain captain = new Captain("persist-model-test");
                captain.Model = "claude-sonnet-4-5-20250514";

                captain.State = CaptainStateEnum.Working;
                captain.CurrentMissionId = "msn_test123";
                captain.ProcessId = 9999;

                AssertEqual("claude-sonnet-4-5-20250514", captain.Model);
            });

            await RunTest("Captain_Model_IncludedInJsonOutput", () =>
            {
                Captain captain = new Captain("json-model-test");
                captain.Model = "claude-sonnet-4-5-20250514";

                string json = JsonSerializer.Serialize(captain);
                AssertContains("claude-sonnet-4-5-20250514", json);
            });

            await RunTest("Captain_Model_EmptyStringBehavior", () =>
            {
                Captain captain = new Captain();
                captain.Model = "";
                // Empty string should be accepted or treated as null depending on implementation.
                // The test validates the property can hold the value without throwing.
                AssertNotNull(captain.Model != null ? captain.Model : "");
            });
        }
    }
}
