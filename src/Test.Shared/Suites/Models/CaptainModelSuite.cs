namespace Test.Shared.Suites.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="Captain"/>: id generation and validation, name validation,
    /// model empty-to-null normalization, planning-support flags, and JSON serialization.
    /// </summary>
    public sealed class CaptainModelSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Captain model suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("default_constructor_generates_id_with_prefix", "Captain default constructor generates id with prefix", TestTags.Positive, () =>
            {
                Captain captain = new Captain();
                AssertStartsWith(Constants.CaptainIdPrefix, captain.Id);
            }));

            cases.Add(Case("name_runtime_constructor_sets_properties", "Captain name/runtime constructor sets properties", TestTags.Positive, () =>
            {
                Captain captain = new Captain("claude-1", AgentRuntimeEnum.ClaudeCode);
                AssertEqual("claude-1", captain.Name);
                AssertEqual(AgentRuntimeEnum.ClaudeCode, captain.Runtime);
            }));

            cases.Add(Case("default_values_are_correct", "Captain default values are correct", TestTags.Positive, () =>
            {
                Captain captain = new Captain();
                AssertEqual("Captain", captain.Name);
                AssertEqual(AgentRuntimeEnum.ClaudeCode, captain.Runtime);
                AssertNull(captain.Model);
                AssertEqual(CaptainStateEnum.Idle, captain.State);
                AssertNull(captain.CurrentMissionId);
                AssertNull(captain.CurrentDockId);
                AssertNull(captain.ProcessId);
                AssertEqual(0, captain.RecoveryAttempts);
                AssertNull(captain.LastHeartbeatUtc);
            }));

            cases.Add(Case("set_id_null_throws", "Captain set id null throws", TestTags.Negative, () =>
            {
                Captain captain = new Captain();
                AssertThrows<ArgumentNullException>(() => captain.Id = null!);
            }));

            cases.Add(Case("set_id_empty_throws", "Captain set id empty throws", TestTags.Negative, () =>
            {
                Captain captain = new Captain();
                AssertThrows<ArgumentNullException>(() => captain.Id = "");
            }));

            cases.Add(Case("set_name_null_throws", "Captain set name null throws", TestTags.Negative, () =>
            {
                Captain captain = new Captain();
                AssertThrows<ArgumentNullException>(() => captain.Name = null!);
            }));

            cases.Add(Case("set_name_empty_throws", "Captain set name empty throws", TestTags.Negative, () =>
            {
                Captain captain = new Captain();
                AssertThrows<ArgumentNullException>(() => captain.Name = "");
            }));

            cases.Add(Case("model_empty_string_resets_to_null", "Captain model empty string resets to null", TestTags.Negative, () =>
            {
                Captain captain = new Captain();
                captain.Model = "gpt-5.4";
                AssertEqual("gpt-5.4", captain.Model);

                captain.Model = "";
                AssertNull(captain.Model);
            }));

            cases.Add(Case("serialization_round_trip", "Captain serialization round trip", TestTags.Positive, () =>
            {
                Captain captain = new Captain("test-captain", AgentRuntimeEnum.Codex);
                captain.Model = "gpt-5.4-mini";
                captain.RuntimeOptionsJson = "{\"schemaVersion\":1,\"endpoint\":\"mux-dev\"}";
                captain.State = CaptainStateEnum.Working;
                captain.CurrentMissionId = "msn_test";
                captain.ProcessId = 12345;
                captain.RecoveryAttempts = 2;

                string json = JsonSerializer.Serialize(captain);
                Captain deserialized = JsonSerializer.Deserialize<Captain>(json)!;

                AssertEqual(captain.Id, deserialized.Id);
                AssertEqual(captain.Name, deserialized.Name);
                AssertEqual(captain.Runtime, deserialized.Runtime);
                AssertEqual(captain.Model, deserialized.Model);
                AssertEqual(captain.RuntimeOptionsJson, deserialized.RuntimeOptionsJson);
                AssertEqual(captain.State, deserialized.State);
                AssertEqual(captain.ProcessId, deserialized.ProcessId);
                AssertEqual(captain.RecoveryAttempts, deserialized.RecoveryAttempts);
            }));

            cases.Add(Case("planning_support_flags_follow_runtime", "Captain planning support flags follow runtime", TestTags.Positive, () =>
            {
                Captain builtIn = new Captain("builtin", AgentRuntimeEnum.Codex);
                Captain custom = new Captain("custom", AgentRuntimeEnum.Custom);

                AssertTrue(builtIn.SupportsPlanningSessions);
                AssertNull(builtIn.PlanningSessionSupportReason);
                AssertFalse(custom.SupportsPlanningSessions);
                AssertContains("built-in ClaudeCode, Codex, Gemini, Cursor, and Mux runtimes", custom.PlanningSessionSupportReason ?? String.Empty);
            }));

            cases.Add(Case("state_enum_serializes_as_string", "Captain state enum serializes as string", TestTags.Positive, () =>
            {
                Captain captain = new Captain();
                captain.State = CaptainStateEnum.Working;

                string json = JsonSerializer.Serialize(captain);
                JsonPropertyShape? state = JsonShape.TopLevel(json).Find(p => p.Name == "State");
                AssertEqual(JsonTokenType.String, state?.ValueToken, "State is a JSON string");
                AssertEqual("Working", state?.ScalarText, "State value");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Models.CaptainModel",
                displayName: "Captain Model",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Models.CaptainModel",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Models.CaptainModel",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
