namespace Armada.Test.Unit.Suites.Models
{
    using System;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;

    /// <summary>
    /// Tests for the Mission.TotalRuntimeSeconds property added in v0.11.0.
    /// Validates that missions track total runtime duration.
    /// </summary>
    public class MissionTotalRuntimeTests : TestSuite
    {
        /// <summary>
        /// Test suite name.
        /// </summary>
        public override string Name => "Mission TotalRuntime";

        /// <summary>
        /// Run all mission total runtime tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            await RunTest("Mission_TotalRuntimeSeconds_DefaultsToNull", () =>
            {
                Mission mission = new Mission();
                AssertNull(mission.TotalRuntimeSeconds);
            });

            await RunTest("Mission_TotalRuntimeSeconds_CanBeSet", () =>
            {
                Mission mission = new Mission();
                mission.TotalRuntimeSeconds = 120.5;
                AssertEqual(120.5, mission.TotalRuntimeSeconds);
            });

            await RunTest("Mission_TotalRuntimeSeconds_CanBeCleared", () =>
            {
                Mission mission = new Mission();
                mission.TotalRuntimeSeconds = 300.0;
                AssertNotNull(mission.TotalRuntimeSeconds);
                mission.TotalRuntimeSeconds = null;
                AssertNull(mission.TotalRuntimeSeconds);
            });

            await RunTest("Mission_TotalRuntimeSeconds_ZeroIsValid", () =>
            {
                Mission mission = new Mission();
                mission.TotalRuntimeSeconds = 0.0;
                AssertEqual(0.0, mission.TotalRuntimeSeconds);
            });

            await RunTest("Mission_TotalRuntimeSeconds_LargeValueIsValid", () =>
            {
                Mission mission = new Mission();
                mission.TotalRuntimeSeconds = 86400.0; // 24 hours
                AssertEqual(86400.0, mission.TotalRuntimeSeconds);
            });

            await RunTest("Mission_TotalRuntimeSeconds_SerializationRoundTrip", () =>
            {
                Mission mission = new Mission("Runtime Test Mission", "Testing runtime tracking");
                mission.TotalRuntimeSeconds = 245.75;

                string json = JsonSerializer.Serialize(mission);
                Mission deserialized = JsonSerializer.Deserialize<Mission>(json)!;

                AssertEqual(mission.Id, deserialized.Id);
                AssertEqual(245.75, deserialized.TotalRuntimeSeconds);
            });

            await RunTest("Mission_TotalRuntimeSeconds_SerializationNullWhenNotSet", () =>
            {
                Mission mission = new Mission("No Runtime Mission");

                string json = JsonSerializer.Serialize(mission);
                Mission deserialized = JsonSerializer.Deserialize<Mission>(json)!;

                AssertNull(deserialized.TotalRuntimeSeconds);
            });

            await RunTest("Mission_TotalRuntimeSeconds_SerializationNullWhenCleared", () =>
            {
                Mission mission = new Mission("Cleared Runtime Mission");
                mission.TotalRuntimeSeconds = 100.0;
                mission.TotalRuntimeSeconds = null;

                string json = JsonSerializer.Serialize(mission);
                Mission deserialized = JsonSerializer.Deserialize<Mission>(json)!;

                AssertNull(deserialized.TotalRuntimeSeconds);
            });

            await RunTest("Mission_TotalRuntimeSeconds_PreservedWithOtherChanges", () =>
            {
                Mission mission = new Mission("Preserve Runtime Test");
                mission.TotalRuntimeSeconds = 500.0;

                mission.Status = MissionStatusEnum.Complete;
                mission.CaptainId = "cpt_test123";
                mission.CompletedUtc = DateTime.UtcNow;

                AssertEqual(500.0, mission.TotalRuntimeSeconds);
            });

            await RunTest("Mission_TotalRuntimeSeconds_ConsistentWithTimestamps", () =>
            {
                Mission mission = new Mission("Timestamp Consistency Test");
                DateTime start = new DateTime(2025, 6, 15, 10, 0, 0, DateTimeKind.Utc);
                DateTime end = new DateTime(2025, 6, 15, 10, 5, 30, DateTimeKind.Utc);

                mission.StartedUtc = start;
                mission.CompletedUtc = end;
                mission.TotalRuntimeSeconds = (end - start).TotalSeconds;

                AssertEqual(330.0, mission.TotalRuntimeSeconds);
            });

            await RunTest("Mission_TotalRuntimeSeconds_IncludedInJsonOutput", () =>
            {
                Mission mission = new Mission("JSON Runtime Test");
                mission.TotalRuntimeSeconds = 42.5;

                string json = JsonSerializer.Serialize(mission);
                AssertContains("42.5", json);
            });
        }
    }
}
