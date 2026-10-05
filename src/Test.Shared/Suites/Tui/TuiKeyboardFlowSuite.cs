namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// One keyboard flow per built list screen in Delivery and Configuration (W8.2): open the tab, filter with /,
    /// select the row, open its row-action menu (.), open and dismiss the create form (n), open the row with Enter, and
    /// go back with Alt+Left to the list, which keeps its tab and row.
    /// </summary>
    public sealed class TuiKeyboardFlowSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Tui.KeyboardFlows";
        private const string Stamp = "\"CreatedUtc\":\"2026-10-01T00:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T00:00:00Z\"";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            foreach (TuiFlowSpec spec in Specs())
            {
                TuiFlowSpec captured = spec;
                cases.Add(TuiCase.Sync(Suite, captured.Id, "Keyboard flow: " + captured.Route + " open, filter, select, row menu, form, open, back", () => TuiFlowRunner.Run(captured)));
            }

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI keyboard flows per screen (Delivery, Configuration)", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static List<TuiFlowSpec> Specs()
        {
            List<TuiFlowSpec> specs = new List<TuiFlowSpec>();
            specs.Add(Spec("deployments", "/delivery?tab=deployments", "/api/v1/deployments", "dpl_1", "Alpha deploy", "/deployments/dpl_1",
                "{\"Id\":\"dpl_1\",\"Title\":\"Alpha deploy\",\"Status\":\"Succeeded\",\"VerificationStatus\":\"Passed\",\"VesselId\":\"vsl_1\",\"EnvironmentName\":\"staging\",\"CheckRunIds\":[]," + Stamp + "}"));
            specs.Add(Spec("environments", "/delivery?tab=environments", "/api/v1/environments", "env_1", "alpha-env", "/environments/env_1",
                "{\"Id\":\"env_1\",\"Name\":\"alpha-env\",\"Kind\":\"Staging\",\"VesselId\":\"vsl_1\",\"RequiresApproval\":false,\"IsDefault\":true,\"Active\":true,\"VerificationDefinitions\":[]," + Stamp + "}"));
            specs.Add(Spec("releases", "/delivery?tab=releases", "/api/v1/releases", "rel_1", "Alpha release", "/releases/rel_1",
                "{\"Id\":\"rel_1\",\"Title\":\"Alpha release\",\"Status\":\"Draft\",\"VesselId\":\"vsl_1\",\"Version\":\"1.0.0\",\"VoyageIds\":[],\"MissionIds\":[],\"CheckRunIds\":[],\"Artifacts\":[]," + Stamp + "}"));
            specs.Add(Spec("incidents", "/delivery?tab=incidents", "/api/v1/incidents", "inc_1", "Alpha outage", "/incidents/inc_1",
                "{\"Id\":\"inc_1\",\"Title\":\"Alpha outage\",\"Status\":\"Open\",\"Severity\":\"High\",\"VesselId\":\"vsl_1\",\"RescueMissionIds\":[],\"DetectedUtc\":\"2026-10-01T00:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T00:00:00Z\"}"));
            specs.Add(Spec("checks", "/delivery?tab=checks", "/api/v1/check-runs", "chk_1", "Alpha checks", "/checks/chk_1",
                "{\"Id\":\"chk_1\",\"Label\":\"Alpha checks\",\"Type\":\"UnitTest\",\"Source\":\"Armada\",\"Status\":\"Passed\",\"VesselId\":\"vsl_1\",\"Command\":\"make test\",\"ExitCode\":0,\"Artifacts\":[],\"DurationMs\":1500," + Stamp + "}"));
            specs.Add(Spec("runbooks", "/delivery?tab=runbooks", "/api/v1/runbooks", "rbk_1", "Alpha runbook", "/runbooks/rbk_1",
                "{\"Id\":\"rbk_1\",\"PlaybookId\":\"pbk_1\",\"Scope\":\"TenantWide\",\"FileName\":\"ALPHA.md\",\"Title\":\"Alpha runbook\",\"Parameters\":[],\"Steps\":[{\"Id\":\"rbs_1\",\"Title\":\"Step\",\"Instructions\":\"Do it\"}],\"OverviewMarkdown\":\"# Alpha\",\"Active\":true," + Stamp + "}"));
            specs.Add(Spec("workflow_profiles", "/configuration?tab=workflow-profiles", "/api/v1/workflow-profiles", "wfp_1", "Alpha workflow", "/workflow-profiles/wfp_1",
                "{\"Id\":\"wfp_1\",\"Name\":\"Alpha workflow\",\"Scope\":\"Global\",\"IsDefault\":false,\"Active\":true,\"LanguageHints\":[],\"RequiredSecrets\":[],\"ExpectedArtifacts\":[],\"Environments\":[]," + Stamp + "}"));
            specs.Add(Spec("project_profiles", "/configuration?tab=project-profiles", "/api/v1/project-profiles", "prp_1", "Alpha project", "/project-profiles/prp_1",
                "{\"Id\":\"prp_1\",\"Name\":\"Alpha project\",\"Scope\":\"Global\",\"IsDefault\":false,\"Active\":true,\"PersonaOverrides\":[],\"Skills\":[]," + Stamp + "}"));
            specs.Add(Spec("skills", "/configuration?tab=skills", "/api/v1/skills", "skl_1", "Alpha skill", "/skills/skl_1",
                "{\"Id\":\"skl_1\",\"TenantId\":\"ten_default\",\"Name\":\"Alpha skill\",\"Category\":\"general\",\"Content\":\"Write tests.\",\"Scope\":\"TenantWide\",\"Active\":true," + Stamp + "}"));
            specs.Add(Spec("playbooks", "/configuration?tab=playbooks", "/api/v1/playbooks", "pbk_1", "ALPHA.md", "/playbooks/pbk_1",
                "{\"Id\":\"pbk_1\",\"TenantId\":\"ten_default\",\"FileName\":\"ALPHA.md\",\"Description\":\"Alpha rules\",\"Content\":\"# Rules\",\"Scope\":\"TenantWide\",\"Active\":true," + Stamp + "}"));
            specs.Add(Spec("personas", "/configuration?tab=personas", "/api/v1/personas", "AlphaPersona", "AlphaPersona", "/personas/AlphaPersona",
                "{\"Id\":\"prs_1\",\"Name\":\"AlphaPersona\",\"Description\":\"Alpha\",\"PromptTemplateName\":\"persona.worker\",\"IsBuiltIn\":false,\"Active\":true,\"Scope\":\"TenantWide\",\"TenantId\":\"ten_default\"," + Stamp + "}"));
            specs.Add(Spec("pipelines", "/configuration?tab=pipelines", "/api/v1/pipelines", "AlphaPipeline", "AlphaPipeline", "/pipelines/AlphaPipeline",
                "{\"Id\":\"ppl_1\",\"Name\":\"AlphaPipeline\",\"Description\":\"Alpha\",\"IsBuiltIn\":false,\"Active\":true,\"Scope\":\"TenantWide\",\"Stages\":[{\"Id\":\"pps_1\",\"Order\":1,\"PersonaName\":\"Worker\",\"IsOptional\":false,\"RequiresReview\":false,\"ReviewDenyAction\":\"RetryStage\"}]," + Stamp + "}"));
            TuiFlowSpec prompts = Spec("prompts", "/configuration?tab=prompts", "/api/v1/prompt-templates", "alpha.prompt", "alpha.prompt", "/prompt-templates/alpha.prompt",
                "{\"Id\":\"ptp_1\",\"Name\":\"alpha.prompt\",\"Category\":\"mission\",\"Description\":\"Alpha\",\"Content\":\"Hello\",\"IsBuiltIn\":false,\"Active\":true,\"Scope\":\"TenantWide\"," + Stamp + "}");
            prompts.NewRoute = "/prompt-templates/create";
            specs.Add(prompts);

            TuiFlowSpec memory = Spec("memory", "/configuration?tab=memory", "/api/v1/memories", "mem_1", "alphatopic", null,
                "{\"Id\":\"mem_1\",\"TenantId\":\"ten_default\",\"Scope\":\"TenantWide\",\"Type\":\"Semantic\",\"Topic\":\"alphatopic\",\"Summary\":\"Alpha summary\",\"Content\":\"Alpha content\",\"Salience\":0.5,\"Tags\":[]," + Stamp + "}");
            specs.Add(memory);
            TuiFlowSpec endpoints = Spec("endpoints", "/configuration?tab=endpoints", "/api/v1/model-endpoints", "mep_1", "alpha-endpoint", null,
                "{\"Id\":\"mep_1\",\"TenantId\":\"ten_default\",\"Name\":\"alpha-endpoint\",\"Kind\":\"Inference\",\"Provider\":\"OpenAI\",\"BaseUrl\":\"https://api.example.com\",\"Model\":\"m\",\"Scope\":\"TenantWide\",\"Enabled\":true,\"HealthStatus\":\"Healthy\",\"HealthHistory\":[]," + Stamp + "}");
            endpoints.BareArray = true;
            specs.Add(endpoints);
            TuiFlowSpec harbors = Spec("harbors", "/configuration?tab=harbors", "/api/v1/harbors", "hbr_1", "alpha-harbor", null,
                "{\"Id\":\"hbr_1\",\"Name\":\"alpha-harbor\",\"ConnectionStatus\":\"Connected\",\"Enabled\":true,\"MaxConcurrentJobs\":4,\"Capabilities\":[]," + Stamp + "}");
            harbors.BareArray = true;
            specs.Add(harbors);
            return specs;
        }

        private static TuiFlowSpec Spec(string id, string route, string listPath, string key, string rowText, string? detailRoute, string rowJson)
        {
            return TuiFlowSpec.Create(id, route, listPath, key, rowText, detailRoute, rowJson);
        }

        #endregion
    }
}
