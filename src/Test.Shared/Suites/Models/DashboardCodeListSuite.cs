namespace Test.Shared.Suites.Models
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Health;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Keeps the dashboard's backend code fixture (src/Armada.Dashboard/src/test/fixtures/backendCodes.json) equal to
    /// the C# code constants and enum members, read by reflection. The dashboard label tests read that fixture instead
    /// of scanning C# source text with regular expressions. Set ARMADA_WRITE_CODE_FIXTURES=1 to rewrite the fixture.
    /// </summary>
    public sealed class DashboardCodeListSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string _SuiteId = "Models.DashboardCodeList";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>Descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("reflection_finds_codes", "Reflection reads the code constants and enum members", TestTags.Positive, () =>
            {
                DashboardCodeFixture expected = Expected();
                AssertTrue(expected.FleetActionReasonCodes.Contains(FleetActionReasonCodes.DirtyTree), "DirtyTree");
                AssertTrue(expected.VesselImportCodes.Contains(VesselImportCodes.NotSelected), "NotSelected");
                AssertTrue(expected.VesselHealthDetailCodes.Contains(VesselHealthDetailCodes.EvaluationError), "EvaluationError");
                AssertEqual(Enum.GetNames(typeof(FleetActionRunStatusEnum)).Length, expected.FleetActionRunStatusEnum.Count, "run status members");
            }));

            cases.Add(Case("fixture_matches_backend", "The dashboard code fixture equals the C# codes and enums", TestTags.Positive, () =>
            {
                DashboardCodeFixture expected = Expected();
                string path = FixturePath();
                if (Environment.GetEnvironmentVariable("ARMADA_WRITE_CODE_FIXTURES") == "1")
                {
                    JsonSerializerOptions write = new JsonSerializerOptions { WriteIndented = true };
                    File.WriteAllText(path, JsonSerializer.Serialize(expected, write).Replace("\r\n", "\n") + "\n");
                }

                AssertTrue(File.Exists(path), "fixture exists at " + path);
                DashboardCodeFixture actual = JsonHelper.Deserialize<DashboardCodeFixture>(File.ReadAllText(path));
                foreach (PropertyInfo property in typeof(DashboardCodeFixture).GetProperties())
                {
                    List<string> want = (List<string>)property.GetValue(expected)!;
                    List<string> have = (List<string>)property.GetValue(actual)!;
                    if (!want.SequenceEqual(have, StringComparer.Ordinal))
                    {
                        throw new AssertionException(
                            "backendCodes.json " + property.Name + " differs from the C# definitions. Expected [" + String.Join(", ", want)
                            + "] but the fixture has [" + String.Join(", ", have) + "]. Regenerate with ARMADA_WRITE_CODE_FIXTURES=1 "
                            + "ARMADA_TEST_SUITES=" + _SuiteId + " dotnet run --project src/Test.Automated --framework net10.0, then add dashboard labels for new codes.");
                    }
                }
            }));

            return new TestSuiteDescriptor(suiteId: _SuiteId, displayName: "Dashboard backend code fixture matches C# codes and enums", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static DashboardCodeFixture Expected()
        {
            DashboardCodeFixture fixture = new DashboardCodeFixture();
            fixture.FleetActionReasonCodes = Constants(typeof(FleetActionReasonCodes));
            fixture.VesselImportCodes = Constants(typeof(VesselImportCodes));
            fixture.VesselHealthDetailCodes = Constants(typeof(VesselHealthDetailCodes));
            fixture.FleetActionRunStatusEnum = Members(typeof(FleetActionRunStatusEnum));
            fixture.FleetActionTargetStatusEnum = Members(typeof(FleetActionTargetStatusEnum));
            fixture.VesselImportCandidateStatusEnum = Members(typeof(VesselImportCandidateStatusEnum));
            fixture.VesselImportOutcomeEnum = Members(typeof(VesselImportOutcomeEnum));
            fixture.VesselImportBatchStatusEnum = Members(typeof(VesselImportBatchStatusEnum));
            fixture.VesselHealthCriterionEnum = Members(typeof(VesselHealthCriterionEnum));
            fixture.VesselHealthStatusEnum = Members(typeof(VesselHealthStatusEnum));
            fixture.VulnerabilitySeverityEnum = Members(typeof(VulnerabilitySeverityEnum));
            fixture.DependencyDriftEnum = Members(typeof(DependencyDriftEnum));
            return fixture;
        }

        private static List<string> Constants(Type type)
        {
            return type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue()!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToList();
        }

        private static List<string> Members(Type enumType)
        {
            return Enum.GetNames(enumType).OrderBy(v => v, StringComparer.Ordinal).ToList();
        }

        private static string FixturePath()
        {
            return Path.Combine(DashboardSource.Src(), "test", "fixtures", "backendCodes.json");
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: _SuiteId,
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
