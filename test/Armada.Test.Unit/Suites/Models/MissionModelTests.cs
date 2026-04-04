namespace Armada.Test.Unit.Suites.Models
{
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;

    public class MissionModelTests : TestSuite
    {
        public override string Name => "Mission Model";

        protected override async Task RunTestsAsync()
        {
            await RunTest("Mission DefaultConstructor GeneratesIdWithPrefix", () =>
            {
                Mission mission = new Mission();
                AssertNotNull(mission.Id);
                AssertStartsWith(Constants.MissionIdPrefix, mission.Id);
            });

            await RunTest("Mission TitleConstructor SetsTitle", () =>
            {
                Mission mission = new Mission("Fix bug", "Fix the critical bug");
                AssertEqual("Fix bug", mission.Title);
                AssertEqual("Fix the critical bug", mission.Description);
            });

            await RunTest("Mission DefaultValues AreCorrect", () =>
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
            });

            await RunTest("Mission SetTitle Null Throws", () =>
            {
                Mission mission = new Mission();
                AssertThrows<ArgumentNullException>(() => mission.Title = null!);
            });

            await RunTest("Mission SetTitle Empty Throws", () =>
            {
                Mission mission = new Mission();
                AssertThrows<ArgumentNullException>(() => mission.Title = "");
            });

            await RunTest("Mission Serialization RoundTrip", () =>
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
            });

            await RunTest("Mission StatusEnum SerializesAsString", () =>
            {
                Mission mission = new Mission();
                mission.Status = MissionStatusEnum.Testing;

                string json = JsonSerializer.Serialize(mission);
                AssertContains("\"Testing\"", json);
            });

            await RunTest("Mission UniqueIds AcrossInstances", () =>
            {
                Mission m1 = new Mission();
                Mission m2 = new Mission();
                AssertNotEqual(m1.Id, m2.Id);
            });

            await RunTest("Mission DiffSnapshot DefaultsToNull", () =>
            {
                Mission mission = new Mission();
                AssertNull(mission.DiffSnapshot);
            });

            await RunTest("Mission DiffSnapshot CanBeSetAndCleared", () =>
            {
                Mission mission = new Mission();
                mission.DiffSnapshot = "diff --git a/file.cs b/file.cs";
                AssertEqual("diff --git a/file.cs b/file.cs", mission.DiffSnapshot);
                mission.DiffSnapshot = null;
                AssertNull(mission.DiffSnapshot);
            });

            await RunTest("Mission Serialization DiffSnapshotNullWhenCleared", () =>
            {
                Mission mission = new Mission("DiffTest");
                mission.DiffSnapshot = "some diff content";
                mission.DiffSnapshot = null;

                string json = JsonSerializer.Serialize(mission);
                Mission deserialized = JsonSerializer.Deserialize<Mission>(json)!;
                AssertNull(deserialized.DiffSnapshot);
            });

            await RunTest("Mission TotalRuntimeMs CalculatesFromStartedAndCompleted", () =>
            {
                Mission mission = new Mission();
                mission.StartedUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                mission.CompletedUtc = mission.StartedUtc.Value.AddMilliseconds(2500);

                long runtimeMs = mission.TotalRuntimeMs ?? throw new InvalidOperationException("Expected mission.TotalRuntimeMs to be populated.");
                AssertEqual(2500L, runtimeMs);
            });

            await RunTest("Mission TotalRuntimeMs RecalculatesRegardlessOfAssignmentOrder", () =>
            {
                DateTime startedUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                DateTime completedUtc = startedUtc.AddMilliseconds(3750);
                Mission mission = new Mission();

                mission.CompletedUtc = completedUtc;
                mission.StartedUtc = startedUtc;

                long runtimeMs = mission.TotalRuntimeMs ?? throw new InvalidOperationException("Expected mission.TotalRuntimeMs to be populated.");
                AssertEqual(3750L, runtimeMs);
            });

            await RunTest("Mission TotalRuntimeMs ClearsForMissingOrNegativeDuration", () =>
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
            });
        }
    }
}
