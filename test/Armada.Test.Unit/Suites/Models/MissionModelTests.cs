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

                string json = JsonSerializer.Serialize(mission);
                Mission deserialized = JsonSerializer.Deserialize<Mission>(json)!;

                AssertEqual(mission.Id, deserialized.Id);
                AssertEqual(mission.Title, deserialized.Title);
                AssertEqual(mission.Status, deserialized.Status);
                AssertEqual(mission.Priority, deserialized.Priority);
                AssertEqual(mission.VoyageId, deserialized.VoyageId);
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
        }
    }
}
