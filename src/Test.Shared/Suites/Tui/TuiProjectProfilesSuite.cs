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
    /// Project Profiles tab and project profile detail (W6.2) against a stubbed server: open, KPIs, server filters,
    /// sort, paging, row menu, create validation and submit, persona override toggle and save, the persona prompt
    /// diff, delete with confirmation, and scoped visibility for a regular user.
    /// </summary>
    public sealed class TuiProjectProfilesSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.ProjectProfiles";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "list_opens_with_kpis", "Project Profiles tab shows KPIs, columns, and rows", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=project-profiles"))
                {
                    ProjectProfilesScreen screen = TuiEntityFixtures.Screen<ProjectProfilesScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2 && screen.Kpis.Items.Count == 4, "rows and KPIs");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Total Profiles 2", "kpi total");
                    TuiCase.Contains(frame, "Persona Overrides 1", "kpi overrides");
                    TuiCase.Contains(frame, "Web project", "row");
                    TuiCase.Contains(frame, "1 personas", "overrides column");
                    TuiCase.Contains(frame, "2 skills", "skills column");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "filters_sort_page", "Filters go to the server; paging and sorting work", () =>
            {
                StubHttpHandler stub = Server();
                List<string> many = Enumerable.Range(1, 30).Select(i => Profile("ppr_" + i.ToString("00"), "Project " + i.ToString("00"), "TenantWide")).ToList();
                stub.On("GET", "/api/v1/project-profiles", body => StubHttpHandler.Response(HttpStatusCode.OK, TuiEntityFixtures.PageWithTotal(30, many.Take(25).ToArray())));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=project-profiles&status=active"))
                {
                    ProjectProfilesScreen screen = TuiEntityFixtures.Screen<ProjectProfilesScreen>(host);
                    TuiEntityFixtures.WaitForQuery(host, stub, "GET", "/api/v1/project-profiles", "active", "true");
                    host.Press("/");
                    host.Type("web");
                    TuiEntityFixtures.WaitForQuery(host, stub, "GET", "/api/v1/project-profiles", "search", "web");
                    host.Press("esc");
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 25, "first page");
                    host.Press(">");
                    TuiEntityFixtures.WaitForQuery(host, stub, "GET", "/api/v1/project-profiles", "pageNumber", "2");
                    host.Press("<");
                    screen.Grid.SortBy("name", true);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count > 0 && screen.Grid.Rows[0].Name == "Project 25", "sorted descending");
                    host.Press(".");
                    string frame = host.Screen();
                    foreach (string item in new[] { "Open", "Edit", "View JSON", "Delete" }) TuiCase.Contains(frame, item, "menu item " + item);
                    host.Press("esc");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "create_validates_and_submits", "A blank name keeps the form open; a valid form posts the payload", () =>
            {
                StubHttpHandler stub = Server();
                string? posted = null;
                stub.On("POST", "/api/v1/project-profiles", body =>
                {
                    posted = body;
                    return StubHttpHandler.Response(HttpStatusCode.Created, Profile("ppr_new", "Api project", "TenantWide"));
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=project-profiles"))
                {
                    ProjectProfilesScreen screen = TuiEntityFixtures.Screen<ProjectProfilesScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("n");
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.IsActive && host.Screen().Contains("Create Project Profile"), "form open");
                    FormDialog dialog = (FormDialog)host.App.Modals.Top!;
                    InputField name = (InputField)dialog.Form.Rows.First(r => r.Label == "Name").Field!;
                    name.Value = " ";
                    host.Press("ctrl+s");
                    AssertTrue(host.App.Modals.IsActive, "dialog stays open");
                    TuiCase.Contains(host.Screen(), "This field is required.", "required error");
                    name.Value = "Api project";
                    TextAreaField skills = (TextAreaField)dialog.Form.Rows.First(r => r.Label == "Skills").Field!;
                    skills.Value = "dotnet\ntdd";
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => posted != null && !host.App.Modals.IsActive, "posted and closed");
                    Armada.Core.Models.ProjectProfile sent = JsonHelper.Deserialize<Armada.Core.Models.ProjectProfile>(posted!);
                    AssertEqual("Api project", sent.Name, "name: " + posted);
                    AssertEqual("dotnet,tdd", String.Join(",", sent.Skills), "skills split: " + posted);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "detail_override_diff_delete", "Detail toggles an override and saves, previews the prompt diff, and deletes after confirmation", () =>
            {
                StubHttpHandler stub = Server();
                string? put = null;
                bool deleted = false;
                stub.On("PUT", "/api/v1/project-profiles/ppr_1", body =>
                {
                    put = body;
                    return StubHttpHandler.Response(HttpStatusCode.OK, Profile("ppr_1", "Web project", "TenantWide"));
                });
                stub.Json("GET", "/api/v1/project-profiles/ppr_1/persona-preview/Architect", "{\"PersonaName\":\"Architect\",\"BaseTemplateName\":\"persona.architect\",\"EffectiveTemplateName\":\"persona.architect.web\",\"BasePrompt\":\"You are an architect.\\nPlan the work.\",\"EffectivePrompt\":\"You are an architect.\\nPlan the web work.\",\"IsOverridden\":true}");
                stub.On("DELETE", "/api/v1/project-profiles/ppr_1", body => { deleted = true; return StubHttpHandler.Response(HttpStatusCode.NoContent, ""); });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/project-profiles/ppr_1"))
                {
                    ProjectProfileScreen screen = TuiEntityFixtures.Screen<ProjectProfileScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Entity != null, "loaded");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Web project", "title");
                    TuiCase.Contains(frame, "[x] Architect", "override row");
                    RecordListField<Armada.Core.Models.PersonaOverride> overrides = (RecordListField<Armada.Core.Models.PersonaOverride>)screen.Editor.View.Rows.First(r => r.Label == "Persona Overrides" && r.Field != null).Field!;
                    AssertTrue(overrides.ToggleCurrent(), "toggled");
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => put != null, "saved");
                    AssertTrue(JsonHelper.Deserialize<Armada.Core.Models.ProjectProfile>(put!).PersonaOverrides.Any(o => o.PersonaName == "Architect" && !o.Enabled), "override disabled: " + put);
                    screen.RunAction("preview");
                    TuiEntityFixtures.WaitFor(host, () => screen.Preview != null, "previewed");
                    AssertEqual("diff", screen.ActivePanel, "diff panel shown");
                    frame = host.Screen();
                    TuiCase.Contains(frame, "Overridden: persona.architect to persona.architect.web", "summary");
                    TuiCase.Contains(frame, "-Plan the work.", "removed line");
                    TuiCase.Contains(frame, "+Plan the web work.", "added line");
                    screen.RunAction("delete");
                    AssertTrue(host.App.Modals.Top is ConfirmDialog cd && cd.Message.Contains("Delete this project profile?"), "confirm text");
                    host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => deleted && host.Tui.Context.Router.Current!.Path == "/configuration", "deleted and back");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "regular_user_scoping", "A regular user cannot edit a tenant-wide project profile but can edit a personal one", () =>
            {
                StubHttpHandler stub = TuiEntityFixtures.RegularUserServer();
                stub.Json("GET", "/api/v1/project-profiles", TuiEntityFixtures.Page(Profile("ppr_1", "Web project", "TenantWide"), Profile("ppr_2", "My project", "UserSpecific", "usr_user")));
                stub.Json("GET", "/api/v1/project-profiles/ppr_1", Profile("ppr_1", "Web project", "TenantWide"));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=project-profiles"))
                {
                    ProjectProfilesScreen screen = TuiEntityFixtures.Screen<ProjectProfilesScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press(".");
                    TuiCase.NotContains(host.Screen(), "Delete", "no delete on tenant-wide");
                    host.Press("esc");
                    host.Press("down");
                    host.Press(".");
                    TuiCase.Contains(host.Screen(), "Delete", "delete on own personal profile");
                    host.Press("esc");
                    host.Tui.Context.Navigate("/project-profiles/ppr_1");
                    ProjectProfileScreen detail = TuiEntityFixtures.Screen<ProjectProfileScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => detail.Entity != null, "detail loaded");
                    TuiCase.Contains(host.Screen(), "You can view this project profile, but only tenant administrators can change it.", "notice");
                    AssertFalse(detail.VisibleActions().Contains("Delete"), "delete hidden");
                    AssertFalse(detail.VisibleActions().Contains("Save Changes"), "save hidden");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI project profiles", cases: cases);
        }

        internal static StubHttpHandler Server()
        {
            StubHttpHandler stub = TuiEntityFixtures.Server();
            stub.Json("GET", "/api/v1/project-profiles", TuiEntityFixtures.Page(Profile("ppr_1", "Web project", "TenantWide"), Profile("ppr_2", "Api project", "TenantWide")));
            stub.Json("GET", "/api/v1/project-profiles/ppr_1", Profile("ppr_1", "Web project", "TenantWide"));
            return stub;
        }

        private static string Profile(string id, string name, string ownership, string userId = "usr_admin")
        {
            string overrides = id == "ppr_1" ? "[{\"PersonaName\":\"Architect\",\"PromptTemplateName\":\"persona.architect.web\",\"Enabled\":true}]" : "[]";
            return "{\"Id\":\"" + id + "\",\"TenantId\":\"ten_default\",\"UserId\":\"" + userId + "\",\"OwnershipScope\":\"" + ownership + "\",\"Name\":\"" + name + "\",\"Scope\":\"Global\",\"Active\":true,\"IsDefault\":false,\"PersonaOverrides\":" + overrides
                + ",\"Skills\":[\"dotnet\",\"tdd\"],\"CreatedUtc\":\"2026-10-01T00:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T00:00:00Z\"}";
        }
    }
}
