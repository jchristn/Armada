namespace Armada.Test.Unit.Suites.Models
{
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;

    public class VoyageModelTests : TestSuite
    {
        public override string Name => "Voyage Model";

        protected override async Task RunTestsAsync()
        {
            await RunTest("Voyage DefaultConstructor GeneratesIdWithPrefix", () =>
            {
                Voyage voyage = new Voyage();
                AssertStartsWith(Constants.VoyageIdPrefix, voyage.Id);
            });

            await RunTest("Voyage TitleConstructor SetsProperties", () =>
            {
                Voyage voyage = new Voyage("Test Voyage", "Description");
                AssertEqual("Test Voyage", voyage.Title);
                AssertEqual("Description", voyage.Description);
            });

            await RunTest("Voyage DefaultValues AreCorrect", () =>
            {
                Voyage voyage = new Voyage();
                AssertEqual("New Voyage", voyage.Title);
                AssertEqual(VoyageStatusEnum.Open, voyage.Status);
                AssertNull(voyage.Description);
                AssertNull(voyage.CompletedUtc);
            });

            await RunTest("Voyage SetTitle Null Throws", () =>
            {
                Voyage voyage = new Voyage();
                AssertThrows<ArgumentNullException>(() => voyage.Title = null!);
            });

            await RunTest("Voyage Serialization RoundTrip", () =>
            {
                Voyage voyage = new Voyage("Serialization Test", "Desc");
                voyage.Status = VoyageStatusEnum.InProgress;

                string json = JsonSerializer.Serialize(voyage);
                Voyage deserialized = JsonSerializer.Deserialize<Voyage>(json)!;

                AssertEqual(voyage.Id, deserialized.Id);
                AssertEqual(voyage.Title, deserialized.Title);
                AssertEqual(voyage.Status, deserialized.Status);
            });

            await RunTest("Voyage StatusEnum SerializesAsString", () =>
            {
                Voyage voyage = new Voyage();
                voyage.Status = VoyageStatusEnum.Complete;

                string json = JsonSerializer.Serialize(voyage);
                AssertContains("\"Complete\"", json);
            });
        }
    }
}
