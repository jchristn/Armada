namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared.Infrastructure;
    using Test.Shared.Infrastructure.ApiSurface;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The API surface comparer judges enum-typed settings from the stored value list, not by parsing the type label.
    /// </summary>
    public sealed class ApiSurfaceSettingEnumSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.ApiSurfaceSettingEnums";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>Descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("removed_value_is_breaking", "Removing an enum value is breaking and names the value", TestTags.Negative, () =>
            {
                ApiSurfaceDiff diff = Compare(Key("Mode", "A", "B", "C"), Key("Mode", "A", "C"));
                AssertEqual(1, diff.Breaking.Count, "one breaking change");
                AssertEqual(0, diff.Additions.Count, "no additions");
                AssertTrue(diff.Breaking[0].EndsWith(": enum values removed: B", StringComparison.Ordinal), diff.Breaking[0]);
            }));

            cases.Add(Case("added_value_is_compatible", "Adding an enum value (or making it nullable) is an addition", TestTags.Positive, () =>
            {
                ApiSurfaceDiff added = Compare(Key("Mode", "A", "B"), Key("Mode", "A", "B", "C"));
                AssertEqual(0, added.Breaking.Count, "adding a value is not breaking");
                AssertEqual(1, added.Additions.Count, "reported as an addition");

                ApiSettingKey nullable = Key("Mode", "A", "B");
                nullable.Type += "?";
                ApiSurfaceDiff madeNullable = Compare(Key("Mode", "A", "B"), nullable);
                AssertEqual(0, madeNullable.Breaking.Count, "nullability change is not breaking");
            }));

            cases.Add(Case("renamed_enum_or_non_enum_is_breaking", "A different enum type, or an enum that became a string, is breaking", TestTags.Negative, () =>
            {
                AssertEqual(1, Compare(Key("Mode", "A", "B"), Key("Kind", "A", "B")).Breaking.Count, "enum type renamed");
                ApiSettingKey asString = new ApiSettingKey { Key = "x.mode", Type = "string", Default = "\"A\"" };
                AssertEqual(1, Compare(Key("Mode", "A", "B"), asString).Breaking.Count, "enum became a string");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "API surface: enum settings", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static ApiSettingKey Key(string enumName, params string[] values)
        {
            return new ApiSettingKey
            {
                Key = "x.mode",
                Type = "enum " + enumName + " (" + String.Join("|", values) + ")",
                Default = "\"" + values[0] + "\"",
                EnumName = enumName,
                EnumValues = new List<string>(values)
            };
        }

        private static ApiSurfaceDiff Compare(ApiSettingKey baseline, ApiSettingKey live)
        {
            ApiSurfaceDocument before = new ApiSurfaceDocument();
            before.Settings.Add(baseline);
            ApiSurfaceDocument after = new ApiSurfaceDocument();
            after.Settings.Add(live);
            return ApiSurfaceComparer.Compare(before, after);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
