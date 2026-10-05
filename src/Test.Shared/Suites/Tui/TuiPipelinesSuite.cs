namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Configuration;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Pipelines tab and pipeline detail (W6.5) against a stubbed server: open, filter, sort, page, row menu, create
    /// form validation with the stage editor, Run Pipeline launching a voyage, and delete with confirmation.
    /// </summary>
    public sealed class TuiPipelinesSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Pipelines";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "list_opens", "Pipelines tab shows the stage chain", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=pipelines"))
                {
                    PipelinesScreen screen = TuiEntityFixtures.Screen<PipelinesScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Worker -> Judge", "chain");
                    TuiCase.Contains(frame, "Reviewed", "row");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "filter", "The name filter narrows the list", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=pipelines"))
                {
                    PipelinesScreen screen = TuiEntityFixtures.Screen<PipelinesScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("/");
                    host.Type("rev");
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 1 && screen.Grid.Rows[0].Name == "Reviewed", "filtered");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "sort_and_page", "Sorting by stage count and paging", () =>
            {
                StubHttpHandler stub = TuiEntityFixtures.Server();
                string[] many = Enumerable.Range(1, 30).Select(i => Pipeline("ppl_" + i.ToString("00"), "Pipe " + i.ToString("00"), false, i % 3 + 1)).ToArray();
                stub.Json("GET", "/api/v1/pipelines", TuiEntityFixtures.Page(many));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=pipelines"))
                {
                    PipelinesScreen screen = TuiEntityFixtures.Screen<PipelinesScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 25, "first page");
                    host.Press(">");
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.PageNumber == 2 && screen.Grid.Rows.Count == 5, "second page");
                    host.Press("<");
                    screen.Grid.SortBy("stages", true);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count > 0 && screen.Grid.Rows[0].Stages.Count == 3, "sorted by stages");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "row_menu", "Row menu offers View Detail, Edit, Duplicate, View JSON, Delete", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=pipelines"))
                {
                    PipelinesScreen screen = TuiEntityFixtures.Screen<PipelinesScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("down");
                    host.Press(".");
                    string frame = host.Screen();
                    foreach (string item in new[] { "View Detail", "Edit", "Duplicate", "View JSON", "Delete" }) TuiCase.Contains(frame, item, "menu " + item);
                    host.Press("esc");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "create_with_stage", "Create Pipeline requires a name; the stage editor adds a stage; Save posts", () =>
            {
                StubHttpHandler stub = Server();
                string? posted = null;
                stub.On("POST", "/api/v1/pipelines", body =>
                {
                    posted = body;
                    return StubHttpHandler.Response(HttpStatusCode.Created, Pipeline("ppl_new", "Mine", false, 1));
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=pipelines"))
                {
                    PipelinesScreen screen = TuiEntityFixtures.Screen<PipelinesScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("n");
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.IsActive && host.Screen().Contains("Create Pipeline"), "form open");
                    FormDialog dialog = (FormDialog)host.App.Modals.Top!;
                    host.Press("ctrl+s");
                    TuiCase.Contains(host.Screen(), "This field is required.", "name required");
                    AssertTrue(ReferenceEquals(host.App.Modals.Top, dialog), "dialog stays open");
                    ((InputField)dialog.Form.Rows.First(r => r.Label == "Name").Field!).Value = "Mine";
                    RecordListField<PipelineStage> stages = (RecordListField<PipelineStage>)dialog.Form.Rows.First(r => r.Label == "Stages").Field!;
                    AssertTrue(stages.AddNew(), "stage editor opens");
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.Top is FormDialog d && !ReferenceEquals(d, dialog), "stage dialog");
                    FormDialog stageDialog = (FormDialog)host.App.Modals.Top!;
                    SelectField<string> persona = (SelectField<string>)stageDialog.Form.Rows.First(r => r.Label == "Persona").Field!;
                    persona.Choose(persona.Options.First(o => o.Value == "Worker"));
                    ((CheckField)stageDialog.Form.Rows.First(r => r.Field is CheckField c && c.Text == "Review gate").Field!).SetValue(true);
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => stages.Items.Count == 1 && ReferenceEquals(host.App.Modals.Top, dialog), "stage added");
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => posted != null && !host.App.Modals.IsActive, "posted");
                    Pipeline sent = JsonHelper.Deserialize<Pipeline>(posted!);
                    AssertEqual("Mine", sent.Name, "name: " + posted);
                    AssertEqual(1, sent.Stages.Count, "one stage: " + posted);
                    AssertEqual("Worker", sent.Stages[0].PersonaName, "stage persona: " + posted);
                    AssertTrue(sent.Stages[0].RequiresReview, "review gate: " + posted);
                    AssertEqual(1, sent.Stages[0].Order, "order: " + posted);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "detail_run_pipeline", "Run Pipeline posts a voyage and opens it", () =>
            {
                StubHttpHandler stub = Server();
                string? posted = null;
                stub.On("POST", "/api/v1/voyages", body =>
                {
                    posted = body;
                    return StubHttpHandler.Response(HttpStatusCode.Created, "{\"Id\":\"vyg_run\",\"Title\":\"Run: Reviewed\"}");
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/pipelines/Reviewed"))
                {
                    PipelineScreen screen = TuiEntityFixtures.Screen<PipelineScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Entity != null, "loaded");
                    host.Press("]");
                    TuiCase.Contains(host.Screen(), "Fail pipeline", "on deny column");
                    host.Press("r");
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.IsActive && host.Screen().Contains("Run Pipeline"), "run dialog");
                    TuiCase.Contains(host.Screen(), "Run: Reviewed", "default title");
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => posted != null && host.Tui.Context.Router.Current!.Path == "/voyages/vyg_run", "voyage opened");
                    Armada.Client.Models.VoyageCreateRequest voyage = JsonHelper.Deserialize<Armada.Client.Models.VoyageCreateRequest>(posted!);
                    AssertEqual("Reviewed", voyage.Pipeline, "pipeline: " + posted);
                    AssertEqual("vsl_1", voyage.VesselId, "vessel: " + posted);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "detail_delete_with_confirm", "Deleting a custom pipeline asks first; built-ins offer no Delete", () =>
            {
                StubHttpHandler stub = Server();
                bool deleted = false;
                stub.On("DELETE", "/api/v1/pipelines/Reviewed", body =>
                {
                    deleted = true;
                    return StubHttpHandler.Response(HttpStatusCode.NoContent, "");
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/pipelines/Reviewed"))
                {
                    PipelineScreen screen = TuiEntityFixtures.Screen<PipelineScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Entity != null, "loaded");
                    AssertTrue(screen.VisibleActions().Contains("Delete"), "delete offered");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "Delete pipeline \"Reviewed\"? This cannot be undone.", "confirm");
                    host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => deleted, "deleted");
                }

                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/pipelines/Default"))
                {
                    PipelineScreen screen = TuiEntityFixtures.Screen<PipelineScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Entity != null, "loaded built-in");
                    AssertFalse(screen.VisibleActions().Contains("Delete"), "built-in not deletable");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI pipelines", cases: cases);
        }

        private static StubHttpHandler Server()
        {
            StubHttpHandler stub = TuiEntityFixtures.Server();
            stub.Json("GET", "/api/v1/pipelines", TuiEntityFixtures.Page(Pipeline("ppl_1", "Default", true, 1), Pipeline("ppl_2", "Reviewed", false, 2)));
            stub.Json("GET", "/api/v1/pipelines/Reviewed", Pipeline("ppl_2", "Reviewed", false, 2));
            stub.Json("GET", "/api/v1/pipelines/Default", Pipeline("ppl_1", "Default", true, 1));
            stub.Json("GET", "/api/v1/personas", TuiEntityFixtures.Page(
                "{\"Id\":\"prs_1\",\"Name\":\"Worker\",\"PromptTemplateName\":\"persona.worker\",\"Scope\":\"TenantWide\",\"TenantId\":\"ten_default\"}",
                "{\"Id\":\"prs_2\",\"Name\":\"Judge\",\"PromptTemplateName\":\"persona.judge\",\"Scope\":\"TenantWide\",\"TenantId\":\"ten_default\"}"));
            stub.Json("GET", "/api/v1/vessels", TuiEntityFixtures.Page("{\"Id\":\"vsl_1\",\"Name\":\"web-app\"}"));
            return stub;
        }

        private static string Pipeline(string id, string name, bool builtIn, int stageCount)
        {
            List<string> stages = new List<string>();
            stages.Add("{\"Id\":\"pps_" + id + "a\",\"Order\":1,\"PersonaName\":\"Worker\",\"IsOptional\":false,\"RequiresReview\":false,\"ReviewDenyAction\":\"RetryStage\"}");
            if (stageCount >= 2) stages.Add("{\"Id\":\"pps_" + id + "b\",\"Order\":2,\"PersonaName\":\"Judge\",\"IsOptional\":false,\"RequiresReview\":true,\"ReviewDenyAction\":\"FailPipeline\",\"Description\":\"Final review\"}");
            if (stageCount >= 3) stages.Add("{\"Id\":\"pps_" + id + "c\",\"Order\":3,\"PersonaName\":\"Worker\",\"IsOptional\":true,\"RequiresReview\":false,\"ReviewDenyAction\":\"RetryStage\"}");
            return "{\"Id\":\"" + id + "\",\"Name\":\"" + name + "\",\"Description\":\"" + name + " pipeline\",\"IsBuiltIn\":" + (builtIn ? "true" : "false")
                + ",\"Active\":true,\"Scope\":\"TenantWide\",\"TenantId\":\"ten_default\",\"Stages\":[" + String.Join(",", stages) + "],\"CreatedUtc\":\"2026-10-01T00:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T00:00:00Z\"}";
        }
    }
}
