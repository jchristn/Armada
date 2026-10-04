namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Ask;
    using Armada.Server.Ask;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Work linking by tool (one mapping table) and the quick action catalog and endpoint behavior.
    /// </summary>
    public sealed class AskWorkLinkingSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.AskWorkLinking";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("dispatch_links_voyage", "dispatch and create_voyage link the created voyage", () =>
            {
                AskWorkLink link = AskWorkLinker.Resolve("dispatch", "{}", "{\"id\":\"vyg_1\",\"title\":\"t\"}").Single();
                AssertEqual(AskTrackedEntityTypeEnum.Voyage, link.EntityType);
                AssertEqual("vyg_1", link.EntityId);
                AssertFalse(link.RefreshOnly, "tracked");
                AssertEqual("vyg_2", AskWorkLinker.Resolve("create_voyage", null, "{\"Id\":\"vyg_2\"}").Single().EntityId, "PascalCase result");
                AssertEqual("vyg_3", AskWorkLinker.Resolve("mcp__armada__dispatch", null, "{\"id\":\"vyg_3\"}").Single().EntityId, "Claude-prefixed name");
            }));

            cases.Add(Case("mission_tools_link_mission", "create_mission, retry_mission, and restart_mission link the mission", () =>
            {
                foreach (string tool in new[] { "create_mission", "retry_mission", "restart_mission" })
                {
                    AskWorkLink link = AskWorkLinker.Resolve(tool, "{\"missionId\":\"msn_9\"}", "{\"id\":\"msn_9\",\"voyageId\":\"vyg_1\"}").Single();
                    AssertEqual(AskTrackedEntityTypeEnum.Mission, link.EntityType, tool);
                    AssertEqual("msn_9", link.EntityId, tool);
                }
            }));

            cases.Add(Case("fleet_action_health_import_links", "run_fleet_action, evaluate_vessel_health, import_vessels, and discover_vessels link their work", () =>
            {
                AskWorkLink run = AskWorkLinker.Resolve("run_fleet_action", "{}", "{\"runId\":\"far_1\",\"status\":\"Pending\"}").Single();
                AssertEqual(AskTrackedEntityTypeEnum.FleetActionRun, run.EntityType);
                AssertEqual("far_1", run.EntityId);

                AskWorkLink job = AskWorkLinker.Resolve("evaluate_vessel_health", "{}", "{\"jobId\":\"job_1\",\"vesselCount\":3}").Single();
                AssertEqual(AskTrackedEntityTypeEnum.Job, job.EntityType);

                List<AskWorkLink> import = AskWorkLinker.Resolve("import_vessels", "{\"batchId\":\"vib_1\"}", "{\"jobId\":\"job_2\"}");
                AssertEqual(2, import.Count, "batch and job");
                AssertTrue(import.Any(l => l.EntityType == AskTrackedEntityTypeEnum.VesselImportBatch && l.EntityId == "vib_1"), "batch from args");
                AssertTrue(import.Any(l => l.EntityType == AskTrackedEntityTypeEnum.Job && l.EntityId == "job_2"), "job from result");

                List<AskWorkLink> discover = AskWorkLinker.Resolve("discover_vessels", "{}", "{\"batch\":{\"id\":\"vib_2\"},\"runsInBackground\":false}");
                AssertEqual("vib_2", discover.Single().EntityId, "inline discovery links the batch only");
            }));

            cases.Add(Case("cancel_refreshes", "cancel_* tools refresh existing tracked work without tracking new work", () =>
            {
                AskWorkLink voyage = AskWorkLinker.Resolve("cancel_voyage", "{\"voyageId\":\"vyg_5\"}", "{\"status\":\"Cancelled\"}").Single();
                AssertTrue(voyage.RefreshOnly, "refresh only");
                AssertEqual("vyg_5", voyage.EntityId);
                AssertEqual("msn_5", AskWorkLinker.Resolve("cancel_mission", "{\"missionId\":\"msn_5\"}", "{}").Single().EntityId);
                AssertEqual("far_5", AskWorkLinker.Resolve("cancel_fleet_action_run", "{\"runId\":\"far_5\"}", "{}").Single().EntityId);
            }));

            cases.Add(Case("errors_and_unlinked_tools", "Errors, wrong prefixes, and unrelated tools link nothing", () =>
            {
                AssertEqual(0, AskWorkLinker.Resolve("dispatch", "{}", "{\"Error\":\"Vessel not found\"}").Count, "error result");
                AssertEqual(0, AskWorkLinker.Resolve("dispatch", "{}", "{\"id\":\"msn_wrong_prefix\"}").Count, "wrong prefix");
                AssertEqual(0, AskWorkLinker.Resolve("update_vessel", "{}", "{\"id\":\"vsl_1\"}").Count, "unrelated tool");
                AssertEqual(0, AskWorkLinker.Resolve("dispatch", "{}", "\"Proposed as aap_1\"").Count, "string result");
                AssertEqual(0, AskWorkLinker.Resolve("dispatch", "{}", "not json").Count, "invalid json");
                AssertEqual(12, AskWorkLinker.LinkedTools().Count, "mapping table size");
            }));

            cases.Add(Case("quick_action_catalog", "Quick actions name real tools with their MCP argument names and a schema", () =>
            {
                List<AskQuickAction> actions = AskQuickActionCatalog.All();
                AssertEqual(String.Join(",", new List<string> { "/dispatch", "/fleet-action", "/status", "/health", "/import" }), String.Join(",", actions.Select(a => a.Command).ToList()));
                AssertEqual(String.Join(",", new List<string> { "dispatch", "fleet-action", "status", "health", "import" }), String.Join(",", actions.Select(a => a.Name).ToList()));
                AskQuickAction dispatch = actions.First(a => a.Name == "dispatch");
                AssertEqual("dispatch", dispatch.ToolName);
                AssertEqual(String.Join(",", new List<string> { "title", "vesselId", "missions", "description", "pipelineId" }), String.Join(",", dispatch.Arguments.Select(a => a.Name).ToList()));
                AssertNotNull(dispatch.ArgumentsSchema, "schema");
                AssertContains("\"vesselId\"", dispatch.ArgumentsSchema!.ToJsonString());
                AssertEqual("run_fleet_action", actions.First(a => a.Name == "fleet-action").ToolName);
                AssertEqual(String.Join(",", new List<string> { "actionId", "vesselIds", "concurrency" }), String.Join(",", actions.First(a => a.Name == "fleet-action").Arguments.Select(a => a.Name).ToList()));
                AssertTrue(actions.First(a => a.Name == "status").ReadOnly, "status is read-only");
                AssertEqual("evaluate_vessel_health", actions.First(a => a.Name == "health").ToolName);
                AssertFalse(actions.First(a => a.Name == "health").Arguments.Any(a => a.Required), "/health runs with {}");
                AssertEqual("discover_vessels", AskQuickActionCatalog.Find("/import")!.ToolName);
                AssertNull(AskQuickActionCatalog.Find("/nope"), "unknown");
            }));

            cases.Add(CaseAsync("quick_action_runs_as_approved_proposal", "A quick action executes immediately as an approved QuickAction proposal", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_qa");
                AskThread thread = await h.Threads.CreateThreadAsync(owner, null).ConfigureAwait(false);

                AskActionRequest request = new AskActionRequest();
                request.ToolName = "dispatch";
                request.Arguments = System.Text.Json.Nodes.JsonNode.Parse("{\"title\":\"QA\",\"vesselId\":\"vsl_1\",\"missions\":[{\"title\":\"m\"}]}") as System.Text.Json.Nodes.JsonObject;
                AskProposalDecision decision = await h.Actions.SubmitQuickActionAsync(owner, thread.Id, request).ConfigureAwait(false);
                AssertEqual(200, decision.StatusCode);
                AssertEqual(AskProposalSourceEnum.QuickAction, decision.Proposal!.Source);
                AssertEqual(AskProposalStatusEnum.Executed, decision.Proposal.Status);
                AssertEqual("usr_qa", decision.Proposal.DecidedByUserId);
                AssertEqual("usr_qa", h.Invocations.Single().Claims["userId"], "runs as the submitting user");
                AssertEqual(1, (await h.Db.Driver.AskTrackedWork.EnumerateByThreadAsync(Armada.Core.Constants.DefaultTenantId, thread.Id).ConfigureAwait(false)).Count, "voyage tracked");
                AssertEqual(0, h.Runner.Calls.Count, "quick actions do not start a captain turn");

                AssertEqual(400, (await h.Actions.SubmitQuickActionAsync(owner, thread.Id, new AskActionRequest { ToolName = "no_such_tool" }).ConfigureAwait(false)).StatusCode, "unknown tool");
                AssertEqual(400, (await h.Actions.SubmitQuickActionAsync(owner, thread.Id, new AskActionRequest()).ConfigureAwait(false)).StatusCode, "missing tool");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Ask Work Linking and Quick Actions", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, Action body)
        {
            return CaseAsync(caseId, displayName, () => { body(); return Task.CompletedTask; });
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.Positive });
        }

        #endregion
    }
}
