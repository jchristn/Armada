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
    /// Descriptors for <see cref="Signal"/>: identity generation, constructor defaults,
    /// and JSON round-tripping including string enum encoding. Ported from the retired unit
    /// suite plus added Id-validation negatives.
    /// </summary>
    public sealed class SignalModelSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Signal model suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("default_constructor_generates_id_with_prefix", "Signal default constructor generates id with prefix", TestTags.Positive, () =>
            {
                Signal signal = new Signal();
                AssertStartsWith(Constants.SignalIdPrefix, signal.Id);
            }));

            cases.Add(Case("type_constructor_sets_properties", "Signal type constructor sets properties", TestTags.Positive, () =>
            {
                Signal signal = new Signal(SignalTypeEnum.Assignment, "{\"missionId\":\"msn_test\"}");
                AssertEqual(SignalTypeEnum.Assignment, signal.Type);
                AssertEqual("{\"missionId\":\"msn_test\"}", signal.Payload);
            }));

            cases.Add(Case("default_values_are_correct", "Signal default values are correct", TestTags.Positive, () =>
            {
                Signal signal = new Signal();
                AssertEqual(SignalTypeEnum.Nudge, signal.Type);
                AssertNull(signal.Payload);
                AssertNull(signal.FromCaptainId);
                AssertNull(signal.ToCaptainId);
                AssertFalse(signal.Read);
            }));

            cases.Add(Case("serialization_round_trip", "Signal serialization round trip", TestTags.Positive, () =>
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
            }));

            cases.Add(Case("type_enum_serializes_as_string", "Signal type enum serializes as string", TestTags.Positive, () =>
            {
                Signal signal = new Signal(SignalTypeEnum.Heartbeat);
                string json = JsonSerializer.Serialize(signal);
                JsonPropertyShape? type = JsonShape.TopLevel(json).Find(p => p.Name == "Type");
                AssertEqual(JsonTokenType.String, type?.ValueToken, "Type is a JSON string");
                AssertEqual("Heartbeat", type?.ScalarText, "Type value");
            }));

            // Added audit coverage: the Id setter rejects null/empty but the legacy suite never exercised it.
            cases.Add(Case("set_id_null_throws", "Signal set id null throws", TestTags.Negative, () =>
            {
                Signal signal = new Signal();
                AssertThrows<ArgumentNullException>(() => signal.Id = null!);
            }));

            cases.Add(Case("set_id_empty_throws", "Signal set id empty throws", TestTags.Negative, () =>
            {
                Signal signal = new Signal();
                AssertThrows<ArgumentNullException>(() => signal.Id = "");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Models.SignalModel",
                displayName: "Signal Model",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Models.SignalModel",
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
                suiteId: "Models.SignalModel",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
