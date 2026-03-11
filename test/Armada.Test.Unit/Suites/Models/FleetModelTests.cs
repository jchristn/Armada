namespace Armada.Test.Unit.Suites.Models
{
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Test.Common;

    public class FleetModelTests : TestSuite
    {
        public override string Name => "Fleet Model";

        protected override async Task RunTestsAsync()
        {
            await RunTest("Fleet DefaultConstructor GeneratesIdWithPrefix", () =>
            {
                Fleet fleet = new Fleet();
                AssertNotNull(fleet.Id);
                AssertStartsWith(Constants.FleetIdPrefix, fleet.Id);
            });

            await RunTest("Fleet NameConstructor SetsName", () =>
            {
                Fleet fleet = new Fleet("TestFleet");
                AssertEqual("TestFleet", fleet.Name);
            });

            await RunTest("Fleet DefaultValues AreCorrect", () =>
            {
                Fleet fleet = new Fleet();
                AssertEqual("My Fleet", fleet.Name);
                AssertNull(fleet.Description);
                AssertTrue(fleet.Active);
                AssertTrue(fleet.CreatedUtc <= DateTime.UtcNow);
                AssertTrue(fleet.LastUpdateUtc <= DateTime.UtcNow);
            });

            await RunTest("Fleet SetId Null Throws", () =>
            {
                Fleet fleet = new Fleet();
                AssertThrows<ArgumentNullException>(() => fleet.Id = null!);
            });

            await RunTest("Fleet SetId Empty Throws", () =>
            {
                Fleet fleet = new Fleet();
                AssertThrows<ArgumentNullException>(() => fleet.Id = "");
            });

            await RunTest("Fleet SetName Null Throws", () =>
            {
                Fleet fleet = new Fleet();
                AssertThrows<ArgumentNullException>(() => fleet.Name = null!);
            });

            await RunTest("Fleet SetName Empty Throws", () =>
            {
                Fleet fleet = new Fleet();
                AssertThrows<ArgumentNullException>(() => fleet.Name = "");
            });

            await RunTest("Fleet Serialization RoundTrip", () =>
            {
                Fleet fleet = new Fleet("SerializationTest");
                fleet.Description = "Test description";
                fleet.Active = false;

                string json = JsonSerializer.Serialize(fleet);
                Fleet deserialized = JsonSerializer.Deserialize<Fleet>(json)!;

                AssertEqual(fleet.Id, deserialized.Id);
                AssertEqual(fleet.Name, deserialized.Name);
                AssertEqual(fleet.Description, deserialized.Description);
                AssertEqual(fleet.Active, deserialized.Active);
            });

            await RunTest("Fleet UniqueIds AcrossInstances", () =>
            {
                Fleet fleet1 = new Fleet();
                Fleet fleet2 = new Fleet();
                AssertNotEqual(fleet1.Id, fleet2.Id);
            });
        }
    }
}
