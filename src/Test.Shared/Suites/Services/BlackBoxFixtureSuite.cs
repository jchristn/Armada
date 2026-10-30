namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Regression fixtures from real failures (src/Test.Shared/Fixtures/BlackBox, plan 13.2 item 8 and 16.10): the advisor
    /// still produces each fixture's expected rule ids and triage, every fixture record is sanitized and validates against
    /// the record schema, and ExportFixtureAsync writes a sanitized fixture that round-trips.
    /// </summary>
    public sealed class BlackBoxFixtureSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.BlackBoxFixtures";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("every_fixture", "Every fixture still produces its expected rule ids and triage, is sanitized and validates against the record schema", TestTags.Positive, async () =>
            {
                string dir = Path.Combine(DashboardSource.RepoRoot(), "src", "Test.Shared", "Fixtures", "BlackBox");
                List<string> files = Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToList();
                AssertTrue(files.Count >= 5, "fixtures present: " + files.Count);
                BlackBoxJsonSchemaNode root = BlackBoxSchemaGenerator.Generate();
                HashSet<string> rulesCovered = new HashSet<string>(StringComparer.Ordinal);
                foreach (string file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    BlackBoxFixture fixture = BlackBoxFixtureExporter.Parse(await File.ReadAllTextAsync(file).ConfigureAwait(false));
                    AssertEqual(name, fixture.Name, "file name matches the fixture name");
                    AssertTrue(fixture.ExpectedRuleIds.Count > 0, name + " expects rules");
                    BlackBoxAnalysis a = BlackBoxAdvisor.Analyze(fixture.Record, BlackBoxRuleCatalog.Default);
                    foreach (string id in fixture.ExpectedRuleIds) AssertTrue(a.Why.Any(w => w.RuleId == id), name + ": expected " + id + ", got " + String.Join(", ", a.Why.Select(w => w.RuleId)));
                    AssertEqual(fixture.ExpectedRuleIds[0], a.Why[0].RuleId, name + ": the first expected rule leads");
                    AssertEqual(fixture.ExpectedTriage, a.Triage!.Verdict, name + " triage");
                    rulesCovered.UnionWith(fixture.ExpectedRuleIds);
                    string json = JsonSerializer.Serialize(fixture.Record, BlackBoxJson.Options);
                    AssertFalse(json.Contains("/Users/", StringComparison.Ordinal) || json.Contains("/home/", StringComparison.Ordinal), name + " has no host paths");
                    AssertNull(fixture.Record.Subject?.TenantId, name + " has no tenant id");
                    List<string> errors = BlackBoxSchemaValidator.Validate(JsonNode.Parse(json), root);
                    AssertEqual(0, errors.Count, name + " validates: " + String.Join("; ", errors.Take(5)));
                }
                foreach (string required in new[] { "BBX-LAND-001", "BBX-LAND-002", "BBX-SCOPE-001", "BBX-DEP-001" }) AssertTrue(rulesCovered.Contains(required), "a fixture covers " + required);
            }));

            cases.Add(Case("named_fixtures", "The four real failures land on their plan 16.10 verdicts", TestTags.Positive, async () =>
            {
                string dir = Path.Combine(DashboardSource.RepoRoot(), "src", "Test.Shared", "Fixtures", "BlackBox");
                await Expect(dir, "land-001-dirty-checkout", "BBX-LAND-001", BlackBoxTriageEnum.UserConfig, "msn_mvskboi0_vJGaqsNlGqU").ConfigureAwait(false);
                await Expect(dir, "land-002-dock-deleted", "BBX-LAND-002", BlackBoxTriageEnum.ArmadaDefect, "msn_mvsjeweh_tmCfX52gt3z").ConfigureAwait(false);
                await Expect(dir, "scope-001-false-violation", "BBX-SCOPE-001", BlackBoxTriageEnum.ArmadaDefect, "msn_mvsqzal6_b1BfR4tnZ29").ConfigureAwait(false);
                await Expect(dir, "dep-001-parallel-dependency-mission-2", "BBX-DEP-001", BlackBoxTriageEnum.MissionBrief, null).ConfigureAwait(false);
                await Expect(dir, "dep-001-parallel-dependency-mission-4", "BBX-DEP-001", BlackBoxTriageEnum.MissionBrief, null).ConfigureAwait(false);
            }));

            cases.Add(Case("export_fixture", "ExportFixtureAsync sanitizes the record (secrets, tenant, host paths, repository URL) and records the current findings; WriteAsync writes a file that parses back", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.Infra, "Landing refused in /Users/someone/Code/app: token=ghp_abcdefghijklmnopqrstuvwxyz0123456789", MissionStatusEnum.LandingFailed).ConfigureAwait(false);
                mission.TenantId = "ten_export";
                mission.UserId = "usr_export";
                string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.WorkProduced, BlackBoxStepEnum.DirtyCheckout, c =>
                {
                    c.GitFacts = new BlackBoxGitFacts { CheckoutPath = "/Users/someone/Code/app" };
                    c.GitFacts.DirtyPaths.Add("src/a.cs");
                }).ConfigureAwait(false))!;
                await bb.Pipeline.ProcessAsync(id).ConfigureAwait(false);
                BlackBoxFixture fixture = await bb.Service.ExportFixtureAsync(BlackBoxTestKit.Admin(), id).ConfigureAwait(false);
                string json = BlackBoxFixtureExporter.Serialize(fixture);
                AssertFalse(json.Contains("/Users/someone"), "host paths shortened");
                AssertFalse(json.Contains("ghp_abcdefghijklmnopqrstuvwxyz0123456789"), "secrets redacted");
                AssertFalse(json.Contains("ten_export") || json.Contains("usr_export"), "tenant and user removed");
                AssertNull(fixture.Record.Analysis, "analysis left out (the suite recomputes it)");
                AssertContains("BBX-LAND-001", String.Join(",", fixture.ExpectedRuleIds), "expectations recorded");
                AssertEqual(BlackBoxTriageEnum.UserConfig, fixture.ExpectedTriage);
                AssertEqual(BlackBoxRuleCatalog.CurrentRulesVersion, fixture.RulesVersion);
                AssertEqual(mission.Id, fixture.Source);
                AssertEqual("/sanitized/Code/app", fixture.Record.Workspace!.CheckoutPath, "last two path segments kept");
                string dir = Path.Combine(Path.GetTempPath(), "armada-bb-fixture-" + Guid.NewGuid().ToString("N"));
                try
                {
                    string path = await BlackBoxFixtureExporter.WriteAsync(fixture, dir).ConfigureAwait(false);
                    AssertTrue(File.Exists(path), "written");
                    BlackBoxFixture back = BlackBoxFixtureExporter.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false));
                    AssertEqual(fixture.Name, back.Name); AssertEqual(fixture.ExpectedTriage, back.ExpectedTriage);
                    AssertEqual(Path.GetFileNameWithoutExtension(path), back.Name, "file named after the fixture");
                }
                finally
                {
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                }
                await AssertThrowsAsync<BlackBoxNotFoundException>(() => bb.Service.ExportFixtureAsync(BlackBoxTestKit.User("ten_other", "usr_other"), id), "hidden from other tenants").ConfigureAwait(false);
                AssertThrows<JsonException>(() => BlackBoxFixtureExporter.Parse("null"), "empty fixture");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box regression fixtures", cases: cases);
        }

        private static async Task Expect(string dir, string name, string ruleId, BlackBoxTriageEnum triage, string? missionId)
        {
            BlackBoxFixture fixture = BlackBoxFixtureExporter.Parse(await File.ReadAllTextAsync(Path.Combine(dir, name + ".json")).ConfigureAwait(false));
            BlackBoxAnalysis a = BlackBoxAdvisor.Analyze(fixture.Record, BlackBoxRuleCatalog.Default);
            AssertEqual(ruleId, a.Why[0].RuleId, name + " rule");
            AssertEqual(triage, a.Triage!.Verdict, name + " triage");
            if (missionId != null) AssertEqual(missionId, fixture.Record.Subject!.MissionId, name + " mission");
            AssertTrue(a.RecoveryPaths.Count > 0, name + " offers recovery");
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}
