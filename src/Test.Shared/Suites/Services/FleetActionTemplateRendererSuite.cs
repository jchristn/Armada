namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="FleetActionTemplateRenderer"/> and <see cref="FleetActionHealthSummaryBuilder"/>:
    /// every supported variable renders, unknown variables fail naming the variable, substituted values are never
    /// re-expanded, whitespace and case are tolerated, and the health summary text covers findings, packages, and
    /// the no-data case.
    /// </summary>
    public sealed class FleetActionTemplateRendererSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.FleetActionTemplateRenderer";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("renders_all_variables", "Every supported variable renders", TestTags.Positive, () =>
            {
                FleetActionTemplateContext ctx = NewContext();
                ctx.HealthSummary = "all good";
                string rendered = FleetActionTemplateRenderer.Render(
                    "{{vessel.name}}|{{vessel.id}}|{{vessel.defaultBranch}}|{{vessel.workingDirectory}}|{{vessel.buildCommand}}|{{health.summary}}", ctx);
                AssertEqual("api|vsl_1|main|/src/api|dotnet build|all good", rendered);
            }));

            cases.Add(Case("tolerates_whitespace_and_case", "Whitespace inside braces and variable case are tolerated", TestTags.Positive, () =>
            {
                string rendered = FleetActionTemplateRenderer.Render("x {{ vessel.name }} {{VESSEL.DEFAULTBRANCH}} y", NewContext());
                AssertEqual("x api main y", rendered);
            }));

            cases.Add(Case("unknown_variable_names_it", "An unknown variable throws and names the variable", TestTags.Negative, () =>
            {
                FleetActionTemplateException? caught = null;
                try
                {
                    FleetActionTemplateRenderer.Validate("git pull {{vessel.remote}}");
                }
                catch (FleetActionTemplateException e)
                {
                    caught = e;
                }

                AssertNotNull(caught, "exception");
                AssertEqual("vessel.remote", caught!.VariableName);
                AssertContains("vessel.remote", caught.Message);
                AssertTrue(caught is ArgumentException, "maps to 400 as an ArgumentException");
            }));

            cases.Add(Case("render_rejects_unknown_variable", "Render rejects an unknown variable too", TestTags.Negative, () =>
            {
                AssertThrows<FleetActionTemplateException>(() => FleetActionTemplateRenderer.Render("{{secrets.token}}", NewContext()));
            }));

            cases.Add(Case("values_not_reexpanded", "A substituted value containing {{ is not re-expanded", TestTags.Positive, () =>
            {
                FleetActionTemplateContext ctx = NewContext();
                ctx.VesselName = "{{vessel.id}}";
                ctx.HealthSummary = "{{unknown.var}}";
                string rendered = FleetActionTemplateRenderer.Render("{{vessel.name}} / {{health.summary}}", ctx);
                AssertEqual("{{vessel.id}} / {{unknown.var}}", rendered);
            }));

            cases.Add(Case("plain_text_and_empty", "Text without variables, null and empty templates are unchanged", TestTags.Positive, () =>
            {
                AssertEqual("echo { not a var }", FleetActionTemplateRenderer.Render("echo { not a var }", NewContext()));
                AssertEqual(String.Empty, FleetActionTemplateRenderer.Render(null, NewContext()));
                FleetActionTemplateRenderer.Validate(null);
                FleetActionTemplateRenderer.Validate(String.Empty);
            }));

            cases.Add(Case("referenced_variables", "GetReferencedVariables and References report canonical names", TestTags.Positive, () =>
            {
                List<string> names = FleetActionTemplateRenderer.GetReferencedVariables("{{Vessel.Name}} {{health.summary}} {{vessel.name}}");
                AssertEqual(2, names.Count);
                AssertEqual("vessel.name", names[0]);
                AssertTrue(FleetActionTemplateRenderer.References("{{ health.summary }}", FleetActionTemplateRenderer.HealthSummaryVariable), "references health.summary");
                AssertFalse(FleetActionTemplateRenderer.References("git pull", FleetActionTemplateRenderer.BuildCommandVariable), "no buildCommand");
            }));

            cases.Add(Case("context_from_vessel", "FromVessel maps vessel fields including the build command", TestTags.Positive, () =>
            {
                Vessel vessel = new Vessel("web", "https://example.com/web.git");
                vessel.DefaultBranch = "develop";
                vessel.WorkingDirectory = "/w/web";
                vessel.DefinitionOfDoneBuildCommand = "npm run build";
                FleetActionTemplateContext ctx = FleetActionTemplateContext.FromVessel(vessel);
                AssertEqual("web", ctx.VesselName);
                AssertEqual(vessel.Id, ctx.VesselId);
                AssertEqual("develop", ctx.DefaultBranch);
                AssertEqual("/w/web", ctx.WorkingDirectory);
                AssertEqual("npm run build", ctx.BuildCommand);
            }));

            cases.Add(Case("health_summary_no_data", "Health summary reports when no data exists", TestTags.Positive, () =>
            {
                AssertEqual(FleetActionHealthSummaryBuilder.NoHealthDataText, FleetActionHealthSummaryBuilder.Format(null, null));
            }));

            cases.Add(Case("health_summary_findings_and_packages", "Health summary lists non-passing findings and flagged packages", TestTags.Positive, () =>
            {
                List<VesselHealthFinding> findings = new List<VesselHealthFinding>
                {
                    new VesselHealthFinding { Criterion = VesselHealthCriterionEnum.Dependencies, Status = VesselHealthStatusEnum.Fail, DetailCode = "OutdatedPackages", ValueA = 7, ValueB = 2 },
                    new VesselHealthFinding { Criterion = VesselHealthCriterionEnum.WorkingTree, Status = VesselHealthStatusEnum.Pass },
                    new VesselHealthFinding { Criterion = VesselHealthCriterionEnum.ContinuousIntegration, Status = VesselHealthStatusEnum.NotApplicable },
                    new VesselHealthFinding { Criterion = VesselHealthCriterionEnum.Overall, Status = VesselHealthStatusEnum.Fail }
                };
                List<VesselDependency> deps = new List<VesselDependency>
                {
                    new VesselDependency { Ecosystem = "nuget", PackageName = "Newtonsoft.Json", CurrentVersion = "12.0.1", LatestVersion = "13.0.3", Drift = DependencyDriftEnum.Major },
                    new VesselDependency { Ecosystem = "npm", PackageName = "lodash", CurrentVersion = "4.17.20", IsVulnerable = true, Severity = VulnerabilitySeverityEnum.High },
                    new VesselDependency { Ecosystem = "npm", PackageName = "uptodate", CurrentVersion = "1.0.0", LatestVersion = "1.0.0" }
                };

                string summary = FleetActionHealthSummaryBuilder.Format(findings, deps);
                AssertContains("Outdated dependencies: failing (OutdatedPackages, value 7, secondary value 2)", summary);
                AssertFalse(summary.Contains("Working tree"), "passing finding omitted");
                AssertFalse(summary.Contains("Continuous integration"), "not-applicable finding omitted");
                AssertContains("Newtonsoft.Json 12.0.1 -> 13.0.3 (nuget; major update)", summary);
                AssertContains("lodash 4.17.20 (npm; vulnerable, high severity)", summary);
                AssertFalse(summary.Contains("uptodate"), "current package omitted");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Fleet Action Template Renderer",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static FleetActionTemplateContext NewContext()
        {
            return new FleetActionTemplateContext
            {
                VesselName = "api",
                VesselId = "vsl_1",
                DefaultBranch = "main",
                WorkingDirectory = "/src/api",
                BuildCommand = "dotnet build"
            };
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
