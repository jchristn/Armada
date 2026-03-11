namespace Armada.Test.Unit.Suites.Models
{
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;

    public class SignalModelTests : TestSuite
    {
        public override string Name => "Signal Model";

        protected override async Task RunTestsAsync()
        {
            await RunTest("Signal DefaultConstructor GeneratesIdWithPrefix", () =>
            {
                Signal signal = new Signal();
                AssertStartsWith(Constants.SignalIdPrefix, signal.Id);
            });

            await RunTest("Signal TypeConstructor SetsProperties", () =>
            {
                Signal signal = new Signal(SignalTypeEnum.Assignment, "{\"missionId\":\"msn_test\"}");
                AssertEqual(SignalTypeEnum.Assignment, signal.Type);
                AssertEqual("{\"missionId\":\"msn_test\"}", signal.Payload);
            });

            await RunTest("Signal DefaultValues AreCorrect", () =>
            {
                Signal signal = new Signal();
                AssertEqual(SignalTypeEnum.Nudge, signal.Type);
                AssertNull(signal.Payload);
                AssertNull(signal.FromCaptainId);
                AssertNull(signal.ToCaptainId);
                AssertFalse(signal.Read);
            });

            await RunTest("Signal Serialization RoundTrip", () =>
            {
                Signal signal = new Signal(SignalTypeEnum.Progress, "{\"pct\":50}");
                signal.FromCaptainId = "cpt_sender";
                signal.ToCaptainId = "cpt_receiver";
                signal.Read = true;

                string json = JsonSerializer.Serialize(signal);
                Signal deserialized = JsonSerializer.Deserialize<Signal>(json)!;

                AssertEqual(signal.Id, deserialized.Id);
                AssertEqual(signal.Type, deserialized.Type);
                AssertEqual(signal.Payload, deserialized.Payload);
                AssertEqual(signal.Read, deserialized.Read);
            });

            await RunTest("Signal TypeEnum SerializesAsString", () =>
            {
                Signal signal = new Signal(SignalTypeEnum.Heartbeat);
                string json = JsonSerializer.Serialize(signal);
                AssertContains("\"Heartbeat\"", json);
            });
        }
    }
}
