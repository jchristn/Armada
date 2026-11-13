namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Server.Routes;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>JSON binding failures name the field and what it accepts, never a .NET type (UT-011).</summary>
    public sealed class JsonValueErrorTranslatorSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.JsonValueErrorTranslator";
        private static readonly JsonSerializerOptions _Options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("enum_lists_values", "An unknown enum value lists the accepted names", () =>
                {
                    JsonValueError error = Describe<JsonValueErrorProbe>("{\"UsageSelection\":{\"Mode\":\"Bogus\"}}");
                    AssertEqual("UsageSelection.Mode", error.Field);
                    AssertEqual("UsageSelection.Mode must be one of Disabled, Observe or Enforce.", error.Message);
                    AssertEqual(JsonValueError.InvalidValueCode, error.Code);
                }),
                Case("nullable_number", "A nullable number names the field without System.Nullable", () =>
                {
                    JsonValueError error = Describe<JsonValueErrorProbe>("{\"ExpectedUsageSelectionRevision\":\"x\"}");
                    AssertEqual("ExpectedUsageSelectionRevision must be a number.", error.Message);
                    AssertFalse(error.Message.Contains("System.", StringComparison.Ordinal));
                }),
                Case("boolean_and_text", "Booleans and text say what they accept", () =>
                {
                    AssertEqual("Flag must be true or false.", Describe<JsonValueErrorProbe>("{\"Flag\":\"maybe\"}").Message);
                    AssertEqual("Name must be text.", Describe<JsonValueErrorProbe>("{\"Name\":12}").Message);
                }),
                Case("malformed_json", "A truncated body is MalformedJson without exception text", () =>
                {
                    JsonValueError error = Describe<JsonValueErrorProbe>("{\"Name\":");
                    AssertEqual(JsonValueError.MalformedCode, error.Code);
                    AssertTrue(error.Message.StartsWith("The request body is not valid JSON", StringComparison.Ordinal), error.Message);
                }),
                Case("whole_body_wrong_shape", "A body of the wrong shape names no field", () =>
                {
                    JsonValueError error = Describe<JsonValueErrorProbe>("[1,2]");
                    AssertNull(error.Field);
                    AssertEqual("The request body must be a JSON object.", error.Message);
                })
            };
            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "JSON value error messages", cases: cases);
        }

        private static JsonValueError Describe<T>(string json)
        {
            try { JsonSerializer.Deserialize<T>(json, _Options); }
            catch (JsonException error) { return JsonValueErrorTranslator.Describe(error); }
            throw new InvalidOperationException("Expected the body to fail binding: " + json);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Action body)
            => new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: token => { body(); return Task.CompletedTask; }, tags: new List<string> { TestTags.Negative });
    }
}
