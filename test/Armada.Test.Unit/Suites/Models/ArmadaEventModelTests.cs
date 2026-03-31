namespace Armada.Test.Unit.Suites.Models
{
    using System.Text.Json;
    using Armada.Core.Models;
    using Armada.Test.Common;

    public class ArmadaEventModelTests : TestSuite
    {
        public override string Name => "ArmadaEvent Model";

        protected override async Task RunTestsAsync()
        {
            await RunTest("ArmadaEvent DefaultConstructor GeneratesIdWithPrefix", () =>
            {
                ArmadaEvent evt = new ArmadaEvent();
                AssertStartsWith("evt_", evt.Id);
            });

            await RunTest("ArmadaEvent TypeMessageConstructor SetsProperties", () =>
            {
                ArmadaEvent evt = new ArmadaEvent("mission.created", "Mission created");
                AssertEqual("mission.created", evt.EventType);
                AssertEqual("Mission created", evt.Message);
            });

            await RunTest("ArmadaEvent DefaultValues AreCorrect", () =>
            {
                ArmadaEvent evt = new ArmadaEvent();
                AssertEqual("", evt.EventType);
                AssertEqual("", evt.Message);
                AssertNull(evt.EntityType);
                AssertNull(evt.EntityId);
                AssertNull(evt.CaptainId);
                AssertNull(evt.MissionId);
                AssertNull(evt.VesselId);
                AssertNull(evt.VoyageId);
                AssertNull(evt.Payload);
            });

            await RunTest("ArmadaEvent SetId Null Throws", () =>
            {
                ArmadaEvent evt = new ArmadaEvent();
                AssertThrows<ArgumentNullException>(() => evt.Id = null!);
            });

            await RunTest("ArmadaEvent Serialization RoundTrip", () =>
            {
                ArmadaEvent evt = new ArmadaEvent("captain.launched", "Captain launched");
                evt.CaptainId = "cpt_test";
                evt.MissionId = "msn_test";
                evt.VesselId = "vsl_test";
                evt.VoyageId = "vyg_test";
                evt.EntityType = "captain";
                evt.EntityId = "cpt_test";
                evt.Payload = "{\"processId\":12345}";

                string json = JsonSerializer.Serialize(evt);
                ArmadaEvent deserialized = JsonSerializer.Deserialize<ArmadaEvent>(json)!;

                AssertEqual(evt.Id, deserialized.Id);
                AssertEqual(evt.EventType, deserialized.EventType);
                AssertEqual(evt.Message, deserialized.Message);
                AssertEqual(evt.CaptainId, deserialized.CaptainId);
                AssertEqual(evt.Payload, deserialized.Payload);
            });

            await RunTest("ArmadaEvent UniqueIds AcrossInstances", () =>
            {
                ArmadaEvent e1 = new ArmadaEvent();
                ArmadaEvent e2 = new ArmadaEvent();
                AssertNotEqual(e1.Id, e2.Id);
            });
        }
    }
}
