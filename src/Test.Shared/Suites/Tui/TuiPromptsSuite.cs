namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using Armada.Tui.Screens.Configuration;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Prompts tab and prompt template detail/create (W6.6) against a stubbed server: category filter (deep link),
    /// name filter, sort, page, row menu with Reset to Default and confirmation, create validation and submit, edit
    /// and save, and the parameter palette.
    /// </summary>
    public sealed class TuiPromptsSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Prompts";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "list_category_deep_link", "The category pill filter deep links and narrows the list", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=prompts&category=persona"))
                {
                    PromptTemplatesScreen screen = TuiEntityFixtures.Screen<PromptTemplatesScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.LoadCount > 0 && screen.Grid.Rows.Count == 1, "persona only");
                    AssertEqual("persona.worker", screen.Grid.Rows[0].Name, "row");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Content Length", "column");
                    TuiCase.Contains(frame, "chars", "length");
                    screen.Filters.SetValue("category", "");
                    screen.Reload();
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "all categories");
                    AssertEqual("mission.rules", screen.Grid.Rows[0].Name, "default sort by category");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "filter", "The name filter narrows the list", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=prompts"))
                {
                    PromptTemplatesScreen screen = TuiEntityFixtures.Screen<PromptTemplatesScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("/");
                    host.Press("tab");
                    host.Type("worker");
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 1 && screen.Grid.Rows[0].Name == "persona.worker", "filtered");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "sort_and_page", "Sorting by content length and paging", () =>
            {
                StubHttpHandler stub = TuiEntityFixtures.Server();
                string[] many = Enumerable.Range(1, 30).Select(i => Template("tpl.n" + i.ToString("00"), "mission", new string('x', i), false)).ToArray();
                stub.Json("GET", "/api/v1/prompt-templates", TuiEntityFixtures.Page(many));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=prompts"))
                {
                    PromptTemplatesScreen screen = TuiEntityFixtures.Screen<PromptTemplatesScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 25, "first page");
                    host.Press(">");
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.PageNumber == 2 && screen.Grid.Rows.Count == 5, "second page");
                    host.Press("<");
                    screen.Grid.SortBy("contentLength", true);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count > 0 && screen.Grid.Rows[0].Name == "tpl.n30", "longest first");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "row_menu_reset", "Built-in row menu offers Reset to Default, which confirms and posts", () =>
            {
                StubHttpHandler stub = Server();
                bool reset = false;
                stub.On("POST", "/api/v1/prompt-templates/mission.rules/reset", body =>
                {
                    reset = true;
                    return StubHttpHandler.Response(HttpStatusCode.OK, Template("mission.rules", "mission", "Default", true));
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=prompts"))
                {
                    PromptTemplatesScreen screen = TuiEntityFixtures.Screen<PromptTemplatesScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press(".");
                    string frame = host.Screen();
                    foreach (string item in new[] { "Edit", "Duplicate", "View JSON", "Reset to Default" }) TuiCase.Contains(frame, item, "menu " + item);
                    host.Type("Reset");
                    host.Press("enter");
                    AssertTrue(host.WaitForText("Reset template \"mission.rules\" to its built-in default content?"), "confirm");
                    host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => reset, "reset posted");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "create_validates_and_submits", "Create Prompt Template requires a name and content, then posts and opens it", () =>
            {
                StubHttpHandler stub = Server();
                string? posted = null;
                stub.On("POST", "/api/v1/prompt-templates", body =>
                {
                    posted = body;
                    return StubHttpHandler.Response(HttpStatusCode.Created, Template("mission.custom", "mission", "Hello {MissionId}", false));
                });
                stub.Json("GET", "/api/v1/prompt-templates/mission.custom", Template("mission.custom", "mission", "Hello {MissionId}", false));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=prompts"))
                {
                    PromptTemplatesScreen list = TuiEntityFixtures.Screen<PromptTemplatesScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => list.Grid.Rows.Count == 2, "rows");
                    host.Press("n");
                    PromptTemplateScreen screen = TuiEntityFixtures.Screen<PromptTemplateScreen>(host);
                    AssertTrue(screen.IsCreateMode, "create mode");
                    TuiCase.Contains(host.Screen(), "Create Prompt Template", "title");
                    host.Press("ctrl+s");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Template name is required.", "name required");
                    TuiCase.Contains(frame, "Template content is required.", "content required");
                    AssertTrue(posted == null, "nothing posted");
                    screen.NameField!.Value = "mission.custom";
                    screen.ContentField!.Value = "Hello ";
                    screen.InsertParameter("{MissionId}");
                    AssertEqual("Hello {MissionId}", screen.ContentField.Value, "parameter inserted at cursor");
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => posted != null && host.Tui.Context.Router.Current!.Path == "/prompt-templates/mission.custom", "created and opened");
                    Armada.Client.Models.PromptTemplateCreateRequest sent = JsonHelper.Deserialize<Armada.Client.Models.PromptTemplateCreateRequest>(posted!);
                    AssertEqual("mission.custom", sent.Name, "name: " + posted);
                    AssertEqual("mission", sent.Category, "category: " + posted);
                    AssertEqual("Hello {MissionId}", sent.Content, "content: " + posted);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "detail_edit_save_and_palette", "Template detail: edit content, unsaved marker, Ctrl+S saves, Ctrl+P opens the palette", () =>
            {
                StubHttpHandler stub = Server();
                string? saved = null;
                stub.On("PUT", "/api/v1/prompt-templates/mission.rules", body =>
                {
                    saved = body;
                    return StubHttpHandler.Response(HttpStatusCode.OK, Template("mission.rules", "mission", "Edited", true));
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/prompt-templates/mission.rules"))
                {
                    PromptTemplateScreen screen = TuiEntityFixtures.Screen<PromptTemplateScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Entity != null && screen.ContentField!.Value == "Rules", "loaded");
                    AssertTrue(screen.VisibleActions().Contains("Reset to Default"), "reset for built-in");
                    host.Press("ctrl+p");
                    TuiCase.Contains(host.Screen(), "{MissionId}  Mission identifier", "palette");
                    host.Press("esc");
                    screen.ContentField!.Value = "Edited";
                    TuiCase.Contains(host.Screen(), "Unsaved changes", "dirty marker");
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => saved != null, "saved");
                    AssertEqual("Edited", JsonHelper.Deserialize<Armada.Client.Models.PromptTemplateUpdateRequest>(saved!).Content, "content: " + saved);
                    TuiEntityFixtures.WaitFor(host, () => !screen.Form!.View.IsDirty, "clean after save");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI prompt templates", cases: cases);
        }

        internal static StubHttpHandler Server()
        {
            StubHttpHandler stub = TuiEntityFixtures.Server();
            stub.Json("GET", "/api/v1/prompt-templates", TuiEntityFixtures.Page(
                Template("persona.worker", "persona", "Worker prompt", true),
                Template("mission.rules", "mission", "Rules", true)));
            stub.Json("GET", "/api/v1/prompt-templates/mission.rules", Template("mission.rules", "mission", "Rules", true));
            return stub;
        }

        private static string Template(string name, string category, string content, bool builtIn)
        {
            return "{\"Id\":\"ptm_" + name + "\",\"Name\":\"" + name + "\",\"Category\":\"" + category + "\",\"Content\":\"" + content + "\",\"Description\":\"" + name + " template\",\"IsBuiltIn\":" + (builtIn ? "true" : "false")
                + ",\"Active\":true,\"Scope\":\"TenantWide\",\"TenantId\":\"ten_default\",\"CreatedUtc\":\"2026-10-01T00:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T00:00:00Z\"}";
        }
    }
}
