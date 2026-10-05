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
    /// Workflow Profiles tab and workflow profile detail (W6.1) against a stubbed server: open, KPIs, server filters,
    /// sort, paging, row menu, quick-create validation and submit, duplicate, detail save, Validate with errors,
    /// delete with confirmation, and scoped visibility for a regular user.
    /// </summary>
    public sealed class TuiWorkflowProfilesSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.WorkflowProfiles";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "list_opens_with_kpis", "Workflow Profiles tab shows KPIs, columns, and rows", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=workflow-profiles"))
                {
                    WorkflowProfilesScreen screen = TuiEntityFixtures.Screen<WorkflowProfilesScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2 && screen.Kpis.Items.Count == 4, "rows and KPIs");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Total Profiles 2", "kpi total");
                    TuiCase.Contains(frame, "Environment Targets 1", "kpi targets");
                    TuiCase.Contains(frame, "Dotnet build", "row");
                    TuiCase.Contains(frame, "3 commands", "capabilities");
                    TuiCase.Contains(frame, "Tenant-wide", "visibility");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "filters_go_to_server", "Search, scope, and status filters are sent to the server", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=workflow-profiles&scope=Fleet"))
                {
                    WorkflowProfilesScreen screen = TuiEntityFixtures.Screen<WorkflowProfilesScreen>(host);
                    TuiEntityFixtures.WaitForRequest(host, stub, "scope=Fleet");
                    host.Press("/");
                    host.Type("dot");
                    TuiEntityFixtures.WaitForRequest(host, stub, "search=dot");
                    host.Press("esc");
                    SelectField<string> status = (SelectField<string>)screen.Filters.Field("status")!;
                    status.Choose(status.Options.First(o => o.Value == "inactive"));
                    TuiEntityFixtures.WaitForRequest(host, stub, "active=false");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "sort_and_page", "Paging asks the server for the next page; sorting orders every match", () =>
            {
                StubHttpHandler stub = Server();
                List<string> many = Enumerable.Range(1, 30).Select(i => Profile("wfp_" + i.ToString("00"), "Profile " + i.ToString("00"), "TenantWide", "usr_admin")).ToList();
                stub.On("GET", "/api/v1/workflow-profiles", body => StubHttpHandler.Response(HttpStatusCode.OK, TuiEntityFixtures.PageWithTotal(30, many.Take(25).ToArray())));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=workflow-profiles"))
                {
                    WorkflowProfilesScreen screen = TuiEntityFixtures.Screen<WorkflowProfilesScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 25, "first page");
                    host.Press(">");
                    TuiEntityFixtures.WaitForRequest(host, stub, "GET /api/v1/workflow-profiles?pageNumber=2");
                    host.Press("<");
                    screen.Grid.SortBy("name", true);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count > 0 && screen.Grid.Rows[0].Name == "Profile 25", "sorted descending");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "row_menu_and_duplicate", "The row menu offers Duplicate, which posts a copy", () =>
            {
                StubHttpHandler stub = Server();
                string? posted = null;
                stub.On("POST", "/api/v1/workflow-profiles", body =>
                {
                    posted = body;
                    return StubHttpHandler.Response(HttpStatusCode.Created, Profile("wfp_copy", "Dotnet build (Copy)", "TenantWide", "usr_admin"));
                });
                stub.Json("GET", "/api/v1/workflow-profiles/wfp_copy", Profile("wfp_copy", "Dotnet build (Copy)", "TenantWide", "usr_admin"));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=workflow-profiles"))
                {
                    WorkflowProfilesScreen screen = TuiEntityFixtures.Screen<WorkflowProfilesScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press(".");
                    string frame = host.Screen();
                    foreach (string item in new[] { "Open", "Edit", "Duplicate", "View JSON", "Copy ID", "Delete" }) TuiCase.Contains(frame, item, "menu item " + item);
                    host.Type("Duplicate");
                    host.Press("enter");
                    TuiEntityFixtures.WaitFor(host, () => posted != null && host.Tui.Context.Router.Current!.Path == "/workflow-profiles/wfp_copy", "duplicated and opened");
                    AssertTrue(posted!.Contains("\"Name\":\"Dotnet build (Copy)\""), "copy name: " + posted);
                    AssertTrue(posted.Contains("\"IsDefault\":false"), "copy is not default");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "create_validates_and_submits", "A blank name keeps the form open; a valid form posts and opens the profile", () =>
            {
                StubHttpHandler stub = Server();
                string? posted = null;
                stub.On("POST", "/api/v1/workflow-profiles", body =>
                {
                    posted = body;
                    return StubHttpHandler.Response(HttpStatusCode.Created, Profile("wfp_new", "Node", "TenantWide", "usr_admin"));
                });
                stub.Json("GET", "/api/v1/workflow-profiles/wfp_new", Profile("wfp_new", "Node", "TenantWide", "usr_admin"));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=workflow-profiles"))
                {
                    WorkflowProfilesScreen screen = TuiEntityFixtures.Screen<WorkflowProfilesScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("n");
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.IsActive && host.Screen().Contains("Create Workflow Profile"), "form open");
                    FormDialog dialog = (FormDialog)host.App.Modals.Top!;
                    InputField name = (InputField)dialog.Form.Rows.First(r => r.Label == "Name").Field!;
                    name.Value = "";
                    host.Press("ctrl+s");
                    AssertTrue(host.App.Modals.IsActive, "dialog stays open");
                    TuiCase.Contains(host.Screen(), "This field is required.", "required error");
                    AssertNull(posted, "nothing posted");
                    name.Value = "Node";
                    TextAreaField build = (TextAreaField)dialog.Form.Rows.First(r => r.Label == "Build Command").Field!;
                    build.Value = "npm run build";
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => posted != null && host.Tui.Context.Router.Current!.Path == "/workflow-profiles/wfp_new", "posted and opened");
                    AssertTrue(posted!.Contains("\"Name\":\"Node\""), "name: " + posted);
                    AssertTrue(posted.Contains("\"BuildCommand\":\"npm run build\""), "build command");
                    AssertTrue(posted.Contains("\"OwnershipScope\":\"TenantWide\""), "admin default visibility");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "detail_save_validate_delete", "Detail saves edits, shows validation errors, and deletes after confirmation", () =>
            {
                StubHttpHandler stub = Server();
                string? put = null;
                bool deleted = false;
                stub.On("PUT", "/api/v1/workflow-profiles/wfp_1", body =>
                {
                    put = body;
                    return StubHttpHandler.Response(HttpStatusCode.OK, Profile("wfp_1", "Dotnet build", "TenantWide", "usr_admin"));
                });
                stub.Json("POST", "/api/v1/workflow-profiles/validate", "{\"IsValid\":false,\"Errors\":[\"Build command is required for vessel scope.\"],\"Warnings\":[\"No test command.\"],\"AvailableCheckTypes\":[\"Build\"],\"CommandPreviews\":[{\"CheckType\":\"Build\",\"EnvironmentName\":null,\"Command\":\"dotnet build\"}]}");
                stub.On("DELETE", "/api/v1/workflow-profiles/wfp_1", body => { deleted = true; return StubHttpHandler.Response(HttpStatusCode.NoContent, ""); });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/workflow-profiles/wfp_1"))
                {
                    WorkflowProfileScreen screen = TuiEntityFixtures.Screen<WorkflowProfileScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Entity != null, "loaded");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Dotnet build", "title");
                    TuiCase.Contains(frame, "Required Inputs", "inputs section");
                    TuiCase.Contains(frame, "Vessel Preview", "preview panel tab");
                    TextAreaField lint = (TextAreaField)screen.Editor.View.Rows.First(r => r.Label == "Lint Command").Field!;
                    lint.Value = "dotnet format --verify-no-changes";
                    AssertTrue(screen.Editor.View.IsDirty, "dirty");
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => put != null, "saved");
                    AssertTrue(put!.Contains("\"LintCommand\":\"dotnet format --verify-no-changes\""), "lint saved: " + put);
                    AssertTrue(put.Contains("\"Key\":\"AWS_PROFILE\""), "required inputs kept");
                    screen.RunAction("validate");
                    TuiEntityFixtures.WaitFor(host, () => screen.Validation != null, "validated");
                    AssertEqual("validation", screen.ActivePanel, "validation panel shown");
                    frame = host.Screen();
                    TuiCase.Contains(frame, "Needs Attention", "status");
                    TuiCase.Contains(frame, "Build command is required for vessel scope.", "error");
                    TuiCase.Contains(frame, "No test command.", "warning");
                    TuiCase.Contains(frame, "dotnet build", "resolved command");
                    screen.RunAction("delete");
                    AssertTrue(host.App.Modals.Top is ConfirmDialog cd && cd.Message.Contains("future runs will not be able to resolve this profile"), "confirm text");
                    host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => deleted && host.Tui.Context.Router.Current!.Path == "/configuration", "deleted and back to the list");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "regular_user_scoping", "A regular user cannot edit or delete a tenant-wide profile", () =>
            {
                StubHttpHandler stub = TuiEntityFixtures.RegularUserServer();
                AddProfiles(stub);
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=workflow-profiles"))
                {
                    WorkflowProfilesScreen screen = TuiEntityFixtures.Screen<WorkflowProfilesScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press(".");
                    string menu = host.Screen();
                    TuiCase.Contains(menu, "Duplicate", "duplicate offered");
                    TuiCase.NotContains(menu, "Delete", "no delete");
                    TuiCase.NotContains(menu, "Edit", "no edit");
                    host.Press("esc");
                    host.Tui.Context.Navigate("/workflow-profiles/wfp_1");
                    WorkflowProfileScreen detail = TuiEntityFixtures.Screen<WorkflowProfileScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => detail.Entity != null, "detail loaded");
                    TuiCase.Contains(host.Screen(), "You can view this workflow profile, but only its owner or a tenant administrator can change it.", "notice");
                    AssertFalse(detail.VisibleActions().Contains("Delete"), "delete hidden");
                    AssertFalse(detail.VisibleActions().Contains("Save Changes"), "save hidden");
                    AssertTrue(detail.VisibleActions().Contains("Validate"), "validate offered");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI workflow profiles", cases: cases);
        }

        private static StubHttpHandler Server()
        {
            StubHttpHandler stub = TuiEntityFixtures.Server();
            AddProfiles(stub);
            return stub;
        }

        private static void AddProfiles(StubHttpHandler stub)
        {
            stub.Json("GET", "/api/v1/workflow-profiles", TuiEntityFixtures.Page(
                Profile("wfp_1", "Dotnet build", "TenantWide", "usr_admin"),
                Profile("wfp_2", "Node deploy", "TenantWide", "usr_admin")));
            stub.Json("GET", "/api/v1/workflow-profiles/wfp_1", Profile("wfp_1", "Dotnet build", "TenantWide", "usr_admin"));
        }

        private static string Profile(string id, string name, string ownership, string userId)
        {
            string environments = id == "wfp_1" ? "[{\"EnvironmentName\":\"prod\",\"DeployCommand\":\"./deploy.sh\"}]" : "[]";
            return "{\"Id\":\"" + id + "\",\"TenantId\":\"ten_default\",\"UserId\":\"" + userId + "\",\"OwnershipScope\":\"" + ownership + "\",\"Name\":\"" + name + "\",\"Scope\":\"Global\",\"Active\":true,\"IsDefault\":" + (id == "wfp_1" ? "true" : "false")
                + ",\"LintCommand\":\"dotnet format\",\"BuildCommand\":\"dotnet build\",\"LanguageHints\":[\"dotnet\"],\"RequiredInputs\":[{\"Provider\":\"EnvironmentVariable\",\"Key\":\"AWS_PROFILE\"}],\"ExpectedArtifacts\":[],\"Environments\":" + environments
                + ",\"CreatedUtc\":\"2026-10-01T00:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T00:00:00Z\"}";
        }
    }
}
