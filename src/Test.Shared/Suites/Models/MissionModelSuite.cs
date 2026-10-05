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
    /// Descriptors for <see cref="Mission"/>: identity generation, constructor defaults,
    /// title validation, JSON round-tripping, and the derived <see cref="Mission.TotalRuntimeMs"/>
    /// recalculation. Ported from the retired unit suite plus added Id-validation negatives.
    /// </summary>
    public sealed class MissionModelSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Mission model suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("default_constructor_generates_id_with_prefix", "Mission default constructor generates id with prefix", TestTags.Positive, () =>
            {
                Mission mission = new Mission();
                AssertNotNull(mission.Id);
                AssertStartsWith(Constants.MissionIdPrefix, mission.Id);
            }));

            cases.Add(Case("title_constructor_sets_title", "Mission title constructor sets title", TestTags.Positive, () =>
            {
                Mission mission = new Mission("Fix bug", "Fix the critical bug");
                AssertEqual("Fix bug", mission.Title);
                AssertEqual("Fix the critical bug", mission.Description);
            }));

            cases.Add(Case("default_values_are_correct", "Mission default values are correct", TestTags.Positive, () =>
            {
                Mission mission = new Mission();
                AssertEqual("New Mission", mission.Title);
                AssertEqual(MissionStatusEnum.Pending, mission.Status);
                AssertEqual(100, mission.Priority);
                AssertNull(mission.VoyageId);
                AssertNull(mission.VesselId);
                AssertNull(mission.CaptainId);
                AssertNull(mission.ParentMissionId);
                AssertNull(mission.BranchName);
                AssertNull(mission.PrUrl);
                AssertNull(mission.StartedUtc);
                AssertNull(mission.CompletedUtc);
                AssertNull(mission.TotalRuntimeMs);
            }));

            cases.Add(Case("set_title_null_throws", "Mission set title null throws", TestTags.Negative, () =>
            {
                Mission mission = new Mission();
                AssertThrows<ArgumentNullException>(() => mission.Title = null!);
            }));

            cases.Add(Case("set_title_empty_throws", "Mission set title empty throws", TestTags.Negative, () =>
            {
                Mission mission = new Mission();
                AssertThrows<ArgumentNullException>(() => mission.Title = "");
            }));

            // Added audit coverage: the Id setter rejects null/empty but the legacy suite never exercised it.
            cases.Add(Case("set_id_null_throws", "Mission set id null throws", TestTags.Negative, () =>
            {
                Mission mission = new Mission();
                AssertThrows<ArgumentNullException>(() => mission.Id = null!);
            }));

            cases.Add(Case("set_id_empty_throws", "Mission set id empty throws", TestTags.Negative, () =>
            {
                Mission mission = new Mission();
                AssertThrows<ArgumentNullException>(() => mission.Id = "");
            }));

            cases.Add(Case("serialization_round_trip", "Mission serialization round trip", TestTags.Positive, () =>
            {
                Mission mission = new Mission("Test Mission", "Desc");
                mission.Status = MissionStatusEnum.InProgress;
                mission.Priority = 50;
                mission.VoyageId = "vyg_test";
                mission.VesselId = "vsl_test";
                mission.CaptainId = "cpt_test";
                mission.StartedUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                mission.CompletedUtc = mission.StartedUtc.Value.AddMilliseconds(1500);

                string json = JsonSerializer.Serialize(mission);
                Mission deserialized = JsonSerializer.Deserialize<Mission>(json)!;

                AssertEqual(mission.Id, deserialized.Id);
                AssertEqual(mission.Title, deserialized.Title);
                AssertEqual(mission.Status, deserialized.Status);
                AssertEqual(mission.Priority, deserialized.Priority);
                AssertEqual(mission.VoyageId, deserialized.VoyageId);
                long deserializedRuntimeMs = deserialized.TotalRuntimeMs ?? throw new InvalidOperationException("Expected deserialized.TotalRuntimeMs to be populated.");
                AssertEqual(1500L, deserializedRuntimeMs);
            }));

            cases.Add(Case("status_enum_serializes_as_string", "Mission status enum serializes as string", TestTags.Positive, () =>
            {
                Mission mission = new Mission();
                mission.Status = MissionStatusEnum.Testing;

                string json = JsonSerializer.Serialize(mission);
                JsonPropertyShape? status = JsonShape.TopLevel(json).Find(p => p.Name == "Status");
                AssertEqual(JsonTokenType.String, status?.ValueToken, "Status is a JSON string");
                AssertEqual("Testing", status?.ScalarText, "Status value");
            }));

            cases.Add(Case("unique_ids_across_instances", "Mission unique ids across instances", TestTags.Positive, () =>
            {
                Mission m1 = new Mission();
                Mission m2 = new Mission();
                AssertNotEqual(m1.Id, m2.Id);
            }));

            cases.Add(Case("diff_snapshot_defaults_to_null", "Mission diff snapshot defaults to null", TestTags.Positive, () =>
            {
                Mission mission = new Mission();
                AssertNull(mission.DiffSnapshot);
            }));

            cases.Add(Case("diff_snapshot_can_be_set_and_cleared", "Mission diff snapshot can be set and cleared", TestTags.Positive, () =>
            {
                Mission mission = new Mission();
                mission.DiffSnapshot = "diff --git a/file.cs b/file.cs";
                AssertEqual("diff --git a/file.cs b/file.cs", mission.DiffSnapshot);
                mission.DiffSnapshot = null;
                AssertNull(mission.DiffSnapshot);
            }));

            cases.Add(Case("serialization_diff_snapshot_null_when_cleared", "Mission serialization diff snapshot null when cleared", TestTags.Positive, () =>
            {
                Mission mission = new Mission("DiffTest");
                mission.DiffSnapshot = "some diff content";
                mission.DiffSnapshot = null;

                string json = JsonSerializer.Serialize(mission);
                Mission deserialized = JsonSerializer.Deserialize<Mission>(json)!;
                AssertNull(deserialized.DiffSnapshot);
            }));

            cases.Add(Case("total_runtime_ms_calculates_from_started_and_completed", "Mission total runtime ms calculates from started and completed", TestTags.Positive, () =>
            {
                Mission mission = new Mission();
                mission.StartedUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                mission.CompletedUtc = mission.StartedUtc.Value.AddMilliseconds(2500);

                long runtimeMs = mission.TotalRuntimeMs ?? throw new InvalidOperationException("Expected mission.TotalRuntimeMs to be populated.");
                AssertEqual(2500L, runtimeMs);
            }));

            cases.Add(Case("total_runtime_ms_recalculates_regardless_of_assignment_order", "Mission total runtime ms recalculates regardless of assignment order", TestTags.Positive, () =>
            {
                DateTime startedUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                DateTime completedUtc = startedUtc.AddMilliseconds(3750);
                Mission mission = new Mission();

                mission.CompletedUtc = completedUtc;
                mission.StartedUtc = startedUtc;

                long runtimeMs = mission.TotalRuntimeMs ?? throw new InvalidOperationException("Expected mission.TotalRuntimeMs to be populated.");
                AssertEqual(3750L, runtimeMs);
            }));

            cases.Add(Case("total_runtime_ms_clears_for_missing_or_negative_duration", "Mission total runtime ms clears for missing or negative duration", TestTags.Negative, () =>
            {
                DateTime startedUtc = new DateTime(2025, 1, 1, 0, 0, 5, DateTimeKind.Utc);
                DateTime completedUtc = startedUtc.AddMilliseconds(1000);
                Mission mission = new Mission();

                mission.StartedUtc = startedUtc;
                mission.CompletedUtc = completedUtc;
                AssertNotNull(mission.TotalRuntimeMs);

                mission.CompletedUtc = startedUtc.AddMilliseconds(-1);
                AssertNull(mission.TotalRuntimeMs);

                mission.CompletedUtc = completedUtc;
                mission.StartedUtc = null;
                AssertNull(mission.TotalRuntimeMs);
            }));

            return new TestSuiteDescriptor(
                suiteId: "Models.MissionModel",
                displayName: "Mission Model",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Models.MissionModel",
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
                suiteId: "Models.MissionModel",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
