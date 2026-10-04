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
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => posted != null && !host.App.Modals.IsActive, "posted");
                    AssertTrue(posted!.Contains("\"Name\":\"Reviewer\""), "name: " + posted);
                    AssertTrue(posted.Contains("\"PromptTemplateName\":\"persona.worker\""), "template: " + posted);
                    AssertTrue(posted.Contains("\"Scope\":\"TenantWide\""), "scope: " + posted);
                }
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
                    AssertTrue(saved!.Contains("\"Content\":\"Edited content\""), "content: " + saved);
                    screen.RunAction("reset-prompt");
                    TuiCase.Contains(host.Screen(), "Your customizations will be lost.", "confirm");
                    host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => reset && screen.PromptContent.Value == "Default content", "reset");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI personas", cases: cases);
        }

        private static StubHttpHandler Server()
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
