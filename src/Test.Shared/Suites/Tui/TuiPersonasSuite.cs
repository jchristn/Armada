namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Configuration;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Personas tab and persona detail (W6.4) against a stubbed server: open, filter, sort, page, row menu, create
    /// form validation and submit, built-in personas not deletable, and the backing prompt editor (save and reset).
    /// </summary>
    public sealed class TuiPersonasSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Personas";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "list_opens", "Personas tab shows columns and rows sorted by name", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=personas"))
                {
                    PersonasScreen screen = TuiEntityFixtures.Screen<PersonasScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    AssertEqual("Judge", screen.Grid.Rows[0].Name, "name ascending");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Prompt Template", "column");
                    TuiCase.Contains(frame, "persona.worker", "template");
                    TuiCase.Contains(frame, "Tenant-wide", "visibility");
                    TuiCase.Contains(frame, "Built-in", "built-in");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "filter", "The name filter narrows the list", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=personas"))
                {
                    PersonasScreen screen = TuiEntityFixtures.Screen<PersonasScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("/");
                    host.Type("wor");
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 1 && screen.Grid.Rows[0].Name == "Worker", "filtered");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "sort_and_page", "Sorting and paging over every persona", () =>
            {
                StubHttpHandler stub = TuiEntityFixtures.Server();
                string[] many = Enumerable.Range(1, 30).Select(i => Persona("prs_" + i.ToString("00"), "Persona " + i.ToString("00"), false)).ToArray();
                stub.Json("GET", "/api/v1/personas", TuiEntityFixtures.Page(many));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=personas"))
                {
                    PersonasScreen screen = TuiEntityFixtures.Screen<PersonasScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 25, "first page");
                    host.Press(">");
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.PageNumber == 2 && screen.Grid.Rows.Count == 5, "second page");
                    TuiCase.Contains(host.Screen(), "Showing 26-30 of 30", "paging bar");
                    screen.Grid.SortBy("name", true);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count > 0 && screen.Grid.Rows[0].Name == "Persona 30", "sorted descending");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "row_menu", "Row menu offers the dashboard actions; built-ins have no Delete", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=personas"))
                {
                    PersonasScreen screen = TuiEntityFixtures.Screen<PersonasScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press(".");
                    string frame = host.Screen();
                    foreach (string item in new[] { "View Detail", "Edit", "Duplicate", "Edit Backing Prompt", "View JSON", "Delete" }) TuiCase.Contains(frame, item, "menu " + item);
                    host.Press("esc");
                    host.Press("down");
                    host.Press(".");
                    TuiCase.NotContains(host.Screen(), "Delete", "built-in has no delete");
                    host.Press("esc");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "create_validates_and_submits", "Create Persona requires a name and template, then posts", () =>
            {
                StubHttpHandler stub = Server();
                string? posted = null;
                stub.On("POST", "/api/v1/personas", body =>
                {
                    posted = body;
                    return StubHttpHandler.Response(HttpStatusCode.Created, Persona("prs_new", "Reviewer", false));
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=personas"))
                {
                    PersonasScreen screen = TuiEntityFixtures.Screen<PersonasScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("n");
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.IsActive && host.Screen().Contains("Create Persona"), "form open");
                    host.Press("ctrl+s");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "This field is required.", "name required");
                    TuiCase.Contains(frame, "A selection is required.", "template required");
                    AssertTrue(host.App.Modals.IsActive, "dialog stays open");
                    FormDialog dialog = (FormDialog)host.App.Modals.Top!;
                    ((InputField)dialog.Form.Rows.First(r => r.Label == "Name").Field!).Value = "Reviewer";
                    SelectField<string> template = (SelectField<string>)dialog.Form.Rows.First(r => r.Label == "Prompt Template Name").Field!;
                    template.Choose(template.Options.First(o => o.Value == "persona.worker"));
                    RecordListField<Armada.Core.Models.PersonaCaptainPriority> priorities = (RecordListField<Armada.Core.Models.PersonaCaptainPriority>)dialog.Form.Rows.First(r => r.Label == "Captain priorities").Field!;
                    priorities.SetItems(new[] { new Armada.Core.Models.PersonaCaptainPriority { Runtime = Armada.Core.Enums.AgentRuntimeEnum.Mux, Endpoint = "local", Model = "gpt-oss:120b" } });
                    SelectField<string> availability = (SelectField<string>)dialog.Form.Rows.First(r => r.Label == "When a priority is unavailable").Field!;
                    availability.Choose(availability.Options.First(o => o.Value == "WaitForFirstPriority"));
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => posted != null && !host.App.Modals.IsActive, "posted");
                    Armada.Core.Models.Persona sent = JsonHelper.Deserialize<Armada.Core.Models.Persona>(posted!);
                    AssertEqual("Reviewer", sent.Name, "name: " + posted);
                    AssertEqual("persona.worker", sent.PromptTemplateName, "template: " + posted);
                    AssertEqual(1, sent.CaptainPriorities.Count);
                    AssertEqual("local", sent.CaptainPriorities[0].Endpoint);
                    AssertEqual(Armada.Core.Enums.PersonaPriorityAvailabilityEnum.WaitForFirstPriority, sent.PriorityAvailability);
                    AssertEqual("TenantWide", JsonShape.TopLevelProperty(posted!, "Scope")?.ScalarText, "scope sent explicitly (TenantWide is also the model default): " + posted);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "specific_api_endpoint_picker", "Persona priority picker selects a specific API endpoint", () =>
            {
                StubHttpHandler stub = Server();
                stub.Page("/api/v1/model-endpoints/enumerate", "[{\"Id\":\"mep_one\",\"Name\":\"First API\",\"Kind\":\"Inference\",\"Provider\":\"XAI\"},{\"Id\":\"mep_embed\",\"Name\":\"Embedding only\",\"Kind\":\"Embedding\"}]");
                using TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=personas");
                PersonasScreen screen = TuiEntityFixtures.Screen<PersonasScreen>(host);
                TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                host.Press("n");
                FormDialog form = TuiConfigTestHelpers.Dialog(host, "Create Persona");
                RecordListField<Armada.Core.Models.PersonaCaptainPriority> priorities = TuiConfigTestHelpers.Field<RecordListField<Armada.Core.Models.PersonaCaptainPriority>>(form, "Captain priorities");
                priorities.AddNew();
                TuiEntityFixtures.WaitFor(host, () => host.App.Modals.Top is FormDialog top && top != form, "priority picker");
                FormDialog picker = (FormDialog)host.App.Modals.Top!;
                SelectField<string> runtime = TuiConfigTestHelpers.Field<SelectField<string>>(picker, "Runtime");
                AssertTrue(runtime.Options.Any(o => o.Value == "endpoint:mep_one"));
                AssertFalse(runtime.Options.Any(o => o.Value == "endpoint:mep_embed"));
                runtime.Choose(runtime.Options.First(o => o.Value == "endpoint:mep_one"));
                host.Press("ctrl+s");
                TuiEntityFixtures.WaitFor(host, () => priorities.Items.Count == 1, "priority added");
                AssertEqual(Armada.Core.Enums.AgentRuntimeEnum.ApiEndpoint, priorities.Items[0].Runtime);
                AssertEqual("mep_one", priorities.Items[0].Endpoint);
                host.Press("esc");
            }));

            cases.Add(TuiCase.Sync(Suite, "escalation_ladder_rung_added", "The persona form edits the escalation ladder with the same rung picker, apart from the priorities (RS-5.6)", () =>
            {
                StubHttpHandler stub = Server();
                stub.Page("/api/v1/model-endpoints/enumerate", "[]");
                using TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=personas");
                PersonasScreen screen = TuiEntityFixtures.Screen<PersonasScreen>(host);
                TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                host.Press("n");
                FormDialog form = TuiConfigTestHelpers.Dialog(host, "Create Persona");
                RecordListField<Armada.Core.Models.PersonaCaptainPriority> ladder = TuiConfigTestHelpers.Field<RecordListField<Armada.Core.Models.PersonaCaptainPriority>>(form, "Escalation ladder");
                RecordListField<Armada.Core.Models.PersonaCaptainPriority> priorities = TuiConfigTestHelpers.Field<RecordListField<Armada.Core.Models.PersonaCaptainPriority>>(form, "Captain priorities");
                ladder.AddNew();
                TuiEntityFixtures.WaitFor(host, () => host.App.Modals.Top is FormDialog top && top != form, "rung picker");
                FormDialog picker = (FormDialog)host.App.Modals.Top!;
                SelectField<string> runtime = TuiConfigTestHelpers.Field<SelectField<string>>(picker, "Runtime");
                runtime.Choose(runtime.Options.First(o => o.Value == "Codex"));
                host.Press("ctrl+s");
                TuiEntityFixtures.WaitFor(host, () => ladder.Items.Count == 1, "rung added");
                AssertEqual(Armada.Core.Enums.AgentRuntimeEnum.Codex, ladder.Items[0].Runtime);
                AssertEqual(0, priorities.Items.Count, "the priorities are a separate list");
                host.Press("esc");
            }));

            cases.Add(TuiCase.Sync(Suite, "detail_builtin_prompt_save_and_reset", "Built-in persona detail: no Delete; Save Prompt and Reset to Default with confirmation", () =>
            {
                StubHttpHandler stub = Server();
                string? saved = null;
                bool reset = false;
                stub.On("PUT", "/api/v1/prompt-templates/persona.worker", body =>
                {
                    saved = body;
                    return StubHttpHandler.Response(HttpStatusCode.OK, Template("persona.worker", "Edited content"));
                });
                stub.On("POST", "/api/v1/prompt-templates/persona.worker/reset", body =>
                {
                    reset = true;
                    return StubHttpHandler.Response(HttpStatusCode.OK, Template("persona.worker", "Default content"));
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/personas/Worker"))
                {
                    PersonaScreen screen = TuiEntityFixtures.Screen<PersonaScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Entity != null && screen.Template != null, "loaded");
                    AssertFalse(screen.VisibleActions().Contains("Delete"), "built-in not deletable");
                    AssertTrue(screen.VisibleActions().Contains("Open Backing Prompt"), "open prompt");
                    TuiCase.Contains(host.Screen(), "None (default routing)", "default captain");
                    host.Press("]");
                    AssertEqual("prompt", screen.ActivePanel, "prompt panel");
                    TuiCase.Contains(host.Screen(), "Original content", "content shown");
                    screen.PromptContent!.Value = "Edited content";
                    AssertTrue(screen.VisibleActions().Contains("Save Prompt"), "save offered when dirty");
                    screen.RunAction("save-prompt");
                    TuiEntityFixtures.WaitFor(host, () => saved != null, "saved");
                    AssertEqual("Edited content", JsonHelper.Deserialize<Armada.Client.Models.PromptTemplateUpdateRequest>(saved!).Content, "content: " + saved);
                    screen.RunAction("reset-prompt");
                    TuiCase.Contains(host.Screen(), "Your customizations will be lost.", "confirm");
                    host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => reset && screen.PromptContent.Value == "Default content", "reset");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI personas", cases: cases);
        }

        internal static StubHttpHandler Server()
        {
            StubHttpHandler stub = TuiEntityFixtures.Server();
            stub.Json("GET", "/api/v1/personas", TuiEntityFixtures.Page(Persona("prs_1", "Worker", true), Persona("prs_2", "Judge", false)));
            stub.Json("GET", "/api/v1/personas/Worker", Persona("prs_1", "Worker", true));
            stub.Json("GET", "/api/v1/prompt-templates", TuiEntityFixtures.Page(Template("persona.worker", "Original content")));
            stub.Json("GET", "/api/v1/prompt-templates/persona.worker", Template("persona.worker", "Original content"));
            return stub;
        }

        private static string Persona(string id, string name, bool builtIn)
        {
            return "{\"Id\":\"" + id + "\",\"Name\":\"" + name + "\",\"Description\":\"" + name + " persona\",\"PromptTemplateName\":\"persona.worker\",\"IsBuiltIn\":" + (builtIn ? "true" : "false")
                + ",\"Active\":true,\"Scope\":\"TenantWide\",\"TenantId\":\"ten_default\",\"CreatedUtc\":\"2026-10-01T00:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T00:00:00Z\"}";
        }

        private static string Template(string name, string content)
        {
            return "{\"Id\":\"ptm_1\",\"Name\":\"" + name + "\",\"Category\":\"persona\",\"Content\":\"" + content + "\",\"Description\":\"Worker prompt\",\"IsBuiltIn\":true,\"Active\":true,\"Scope\":\"TenantWide\",\"TenantId\":\"ten_default\",\"CreatedUtc\":\"2026-10-01T00:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T00:00:00Z\"}";
        }
    }
}
