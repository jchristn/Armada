namespace Test.Shared.Suites.Tui.ActivitySystem
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Admin;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Test.Shared.Suites.Tui.Bodies;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Headless flows for the Tenants, Users, and Credentials tabs against a stubbed server.
    /// </summary>
    public sealed class TuiAdminSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.System.Admin";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "users_list_search", "Users lists accounts with tenant names and the email search narrows them", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 40, "/server?tab=users", stub))
                {
                    AssertTrue(host.WaitForText("ops@armada"), "rows");
                    string frame = host.Screen();
                    TuiScreenDump.Write("users", frame);
                    TuiCase.Contains(frame, "Default Tenant", "tenant name");
                    TuiCase.Contains(frame, "Manage user accounts across all tenants.", "admin subtitle");
                    UsersScreen screen = Current<UsersScreen>(host);
                    screen.EmailFilter.Value = "ops";
                    AssertTrue(host.PumpUntil(() => screen.Grid.Rows.Count == 1), "one row");
                    AssertEqual("usr_ops", screen.Grid.Rows[0].Id, "filtered");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "users_create_validation", "Create user blocks a missing email and mismatched passwords, then posts a valid user", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 40, "/server?tab=users", stub))
                {
                    AssertTrue(host.WaitForText("ops@armada"), "rows");
                    host.Press("n");
                    AssertTrue(host.WaitForText("Create User"), "modal");
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Email is required."), "email required");
                    host.Type("new@armada").Press("tab").Type("New").Press("tab").Type("Person").Press("tab").Type("pw1").Press("tab").Type("pw2");
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Passwords do not match."), "mismatch");
                    TuiScreenDump.Write("users-create", host.Screen());
                    AssertEqual(0, stub.CountFor("POST", "/api/v1/users"), "nothing posted");
                    host.Press("ctrl+u").Type("pw1").Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/users") == 1), "posted");
                    AdminUserBody body = stub.LastBody<AdminUserBody>("POST", "/api/v1/users");
                    AssertEqual("new@armada", body.Email, "email");
                    AssertEqual("pw1", body.Password, "password");
                    AssertEqual("New", body.FirstName, "first name");
                    AssertEqual("ten_default", body.TenantId, "tenant");
                    AssertTrue(host.WaitForText("created"), "toast");
                    AssertTrue(TuiToasts.WaitForSuccess(host, "created"), "success toast");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "users_edit_blank_password", "Editing with a blank password keeps it (no Password in the request)", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 40, "/server?tab=users", stub))
                {
                    AssertTrue(host.WaitForText("ops@armada"), "rows");
                    UsersScreen screen = Current<UsersScreen>(host);
                    FormModal? modal = screen.OpenForm(screen.Items.First(u => u.Id == "usr_ops"));
                    AssertNotNull(modal, "modal");
                    host.Pump();
                    TuiCase.Contains(host.Screen(), "Leave blank to keep current password", "placeholder");
                    modal!.RunSubmit();
                    AssertTrue(host.PumpUntil(() => stub.CountFor("PUT", "/api/v1/users/usr_ops") == 1), "put");
                    StubRequest put = stub.Last("PUT", "/api/v1/users/usr_ops");
                    AssertEqual("ops@armada", put.BodyAs<AdminUserBody>().Email, "edited user: " + put.Body);
                    AssertFalse(JsonShape.HasPropertyAnywhere(put.Body, "Password"), "no password: " + put.Body);
                    AssertNull(UsersScreen.ValidatePasswords(true, "", ""), "blank ok on edit");
                    AssertEqual("Password is required when creating a user.", UsersScreen.ValidatePasswords(false, "", ""), "required on create");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "users_tenant_admin_form", "A tenant admin does not get the Global Admin field or other tenants", () =>
            {
                StubHttpHandler stub = Stub();
                stub.On("GET", "/api/v1/whoami", body => StubHttpHandler.Response(HttpStatusCode.OK, TenantAdminWhoAmI()));
                using (TuiTestHost host = TuiCase.SignedIn(160, 40, "/server?tab=users", stub))
                {
                    AssertTrue(host.WaitForText("ops@armada"), "rows");
                    UsersScreen screen = Current<UsersScreen>(host);
                    TuiCase.Contains(host.Screen(), "Manage user accounts within your tenant.", "tenant admin subtitle");
                    FormModal? modal = screen.OpenForm(null);
                    AssertNotNull(modal, "modal");
                    host.Pump();
                    string frame = host.Screen();
                    TuiScreenDump.Write("users-tenant-admin-form", frame);
                    AssertFalse(modal!.Form.Rows.Any(r => r.Label == "Global Admin"), "no Global Admin row");
                    AssertTrue(modal.Form.Rows.Any(r => r.Label == "Tenant Admin"), "tenant admin row");
                    SelectField<string> tenant = (SelectField<string>)modal.Form.Rows.First(r => r.Label == "Tenant").Field!;
                    AssertFalse(tenant.CanFocus, "tenant not editable");
                    AssertEqual(1, tenant.Options.Count, "own tenant only");
                    AssertEqual(0, stub.CountFor("GET", "/api/v1/tenants"), "tenants not listed");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "users_bulk_delete", "Bulk delete needs the typed confirmation and deletes each selected user", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 40, "/server?tab=users", stub))
                {
                    AssertTrue(host.WaitForText("ops@armada"), "rows");
                    host.Press("space").Press("down").Press("space");
                    AssertTrue(host.WaitForText("Delete Selected"), "bulk button");
                    host.Press("del");
                    AssertTrue(host.WaitForText("Delete 2 user(s)?"), "confirm");
                    host.Press("enter");
                    AssertEqual(0, stub.Log.Count(r => r.Method == "DELETE"), "blocked until typed");
                    host.Type("delete").Press("enter");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("DELETE", "/api/v1/users/usr_admin") == 1 && stub.CountFor("DELETE", "/api/v1/users/usr_ops") == 1), "two deletes");
                    AssertEqual(2, stub.Log.Count(r => r.Method == "DELETE"), "only the two selected users deleted");
                    AssertTrue(host.WaitForText("Deleted 2 users."), "toast");
                    AssertTrue(TuiToasts.WaitForSuccess(host, "Deleted 2 users."), "success toast");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "credentials_create_token", "Creating a credential shows the token once and y copies it", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 40, "/server?tab=credentials", stub))
                {
                    AssertTrue(host.WaitForText("CI token"), "rows");
                    string frame = host.Screen();
                    TuiScreenDump.Write("credentials", frame);
                    TuiCase.Contains(frame, "****WXYZ", "masked token");
                    TuiCase.Contains(frame, "admin@armada", "user email");
                    CredentialsScreen screen = Current<CredentialsScreen>(host);
                    FormModal? modal = screen.OpenForm(null);
                    AssertNotNull(modal, "modal");
                    host.Pump();
                    TuiCase.Contains(host.Screen(), "Create Credential", "title");
                    ((InputField)modal!.Form.Rows.First(r => r.Label == "Name (optional)").Field!).Value = "Deploy";
                    modal.RunSubmit();
                    AssertTrue(host.WaitForText("tok_NEWTOKEN1234"), "token shown");
                    TuiScreenDump.Write("credentials-token", host.Screen());
                    AdminCredentialBody body = stub.LastBody<AdminCredentialBody>("POST", "/api/v1/credentials");
                    AssertEqual("Deploy", body.Name, "credential name");
                    AssertEqual("usr_admin", body.UserId, "user");
                    AssertEqual("ten_default", body.TenantId, "tenant");
                    host.Press("y");
                    AssertEqual("tok_NEWTOKEN1234", host.Tui.Context.Clipboard.LastCopied, "copied");
                    AssertEqual("tok_NEWTOKEN1234", screen.NewToken, "remembered");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "credentials_filter_bulk_delete", "Credential user filter narrows rows and bulk delete removes the selection", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 40, "/server?tab=credentials", stub))
                {
                    AssertTrue(host.WaitForText("CI token"), "rows");
                    CredentialsScreen screen = Current<CredentialsScreen>(host);
                    screen.UserFilter.Choose(screen.UserFilter.Options.First(o => o.Value == "usr_ops"));
                    AssertTrue(host.PumpUntil(() => screen.Grid.Rows.Count == 1), "filtered");
                    screen.UserFilter.Choose(screen.UserFilter.Options.First(o => o.Value == ""));
                    AssertTrue(host.PumpUntil(() => screen.Grid.Rows.Count == 2), "unfiltered");
                    host.Press("ctrl+a");
                    host.Press("del");
                    AssertTrue(host.WaitForText("Delete 2 credential(s)?"), "confirm");
                    host.Type("delete").Press("enter");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("DELETE", "/api/v1/credentials/crd_1") == 1 && stub.CountFor("DELETE", "/api/v1/credentials/crd_2") == 1), "two deletes");
                    AssertEqual(2, stub.Log.Count(r => r.Method == "DELETE"), "only the two credentials deleted");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "tenants_crud", "Tenants create, edit, and delete (typed confirmation) call the API", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 40, "/server?tab=tenants", stub))
                {
                    AssertTrue(host.WaitForText("Second Tenant"), "rows");
                    TuiScreenDump.Write("tenants", host.Screen());
                    TenantsScreen screen = Current<TenantsScreen>(host);
                    host.Press("down");
                    host.Press("del");
                    AssertTrue(host.App.Modals.IsActive, "early del opens confirm");
                    host.Press("esc");
                    host.Press("del");
                    AssertTrue(host.App.Modals.IsActive, "del after esc opens confirm");
                    host.Press("esc");
                    FormModal? create = screen.OpenForm(null);
                    AssertNotNull(create, "create modal");
                    AssertFalse(create!.Form.Rows.Any(r => r.Label == "Active"), "no Active on create");
                    host.Pump();
                    host.Type("Third").Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/tenants") == 1), "created");
                    AssertTrue(stub.LastBody<AdminTenantBody>("POST", "/api/v1/tenants").Name == "Third", "create body");
                    host.Pump();
                    FormModal? edit = screen.OpenForm(screen.Items.First(t => t.Id == "ten_two"));
                    AssertTrue(edit!.Form.Rows.Any(r => r.Label == "Active"), "Active on edit");
                    host.Pump();
                    ((ToggleField)edit.Form.Rows.First(r => r.Label == "Active").Field!).SetValue(false);
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("PUT", "/api/v1/tenants/ten_two") == 1), "updated");
                    AssertTrue(stub.LastBody<AdminTenantBody>("PUT", "/api/v1/tenants/ten_two").Name == "Second Tenant" && stub.LastBody<AdminTenantBody>("PUT", "/api/v1/tenants/ten_two").Active == false, "edit body");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("GET", "/api/v1/tenants") >= 3 && screen.Grid.State == GridStateEnum.Ready), "reloaded");
                    host.Pump();
                    screen.Grid.MoveCursor(screen.Grid.Rows.ToList().FindIndex(t => t.Id == "ten_two"));
                    host.Press("del");
                    AssertTrue(host.WaitForText("Delete tenant"), "confirm");
                    host.Type("delete").Press("enter");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("DELETE", "/api/v1/tenants/ten_two") == 1), "deleted");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "tenants_non_admin", "A non-global admin sees only their tenant and no write actions", () =>
            {
                StubHttpHandler stub = Stub();
                stub.On("GET", "/api/v1/whoami", body => StubHttpHandler.Response(HttpStatusCode.OK, TenantAdminWhoAmI()));
                using (TuiTestHost host = TuiCase.SignedIn(160, 40, "/server?tab=users", stub))
                {
                    TenantsScreen screen = new TenantsScreen(Armada.Tui.Routing.Router.Resolve("/server?tab=tenants"), host.Tui.Context);
                    AssertEqual(1, screen.Items.Count, "own tenant");
                    AssertFalse(screen.CanWrite, "read only");
                    AssertNull(screen.OpenForm(null), "no form");
                    AssertEqual(0, stub.CountFor("GET", "/api/v1/tenants"), "no tenant list");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI Tenants, Users, Credentials", cases: cases);
        }

        private static string TenantAdminWhoAmI()
        {
            return "{\"Tenant\":{\"Id\":\"ten_default\",\"Name\":\"Default Tenant\",\"Active\":true},\"User\":{\"Id\":\"usr_ta\",\"TenantId\":\"ten_default\",\"Email\":\"ta@armada\",\"IsAdmin\":false,\"IsTenantAdmin\":true,\"Active\":true}}";
        }

        private static StubHttpHandler Stub()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            stub.Json("GET", "/api/v1/users", "{\"Objects\":["
                + "{\"Id\":\"usr_admin\",\"TenantId\":\"ten_default\",\"Email\":\"admin@armada\",\"FirstName\":\"Admin\",\"IsAdmin\":true,\"IsTenantAdmin\":true,\"Active\":true,\"CreatedUtc\":\"2026-10-01T00:00:00Z\"},"
                + "{\"Id\":\"usr_ops\",\"TenantId\":\"ten_two\",\"Email\":\"ops@armada\",\"FirstName\":\"Ops\",\"LastName\":\"Person\",\"IsAdmin\":false,\"IsTenantAdmin\":false,\"Active\":true,\"CreatedUtc\":\"2026-10-02T00:00:00Z\"}"
                + "],\"TotalRecords\":2}");
            stub.Json("GET", "/api/v1/tenants", "{\"Objects\":["
                + "{\"Id\":\"ten_default\",\"Name\":\"Default Tenant\",\"Active\":true,\"CreatedUtc\":\"2026-10-01T00:00:00Z\"},"
                + "{\"Id\":\"ten_two\",\"Name\":\"Second Tenant\",\"Active\":true,\"CreatedUtc\":\"2026-10-02T00:00:00Z\"}"
                + "],\"TotalRecords\":2}");
            stub.Json("GET", "/api/v1/credentials", "{\"Objects\":["
                + "{\"Id\":\"crd_1\",\"TenantId\":\"ten_default\",\"UserId\":\"usr_admin\",\"Name\":\"CI token\",\"BearerToken\":\"****WXYZ\",\"Active\":true},"
                + "{\"Id\":\"crd_2\",\"TenantId\":\"ten_two\",\"UserId\":\"usr_ops\",\"Name\":\"Ops token\",\"BearerToken\":\"****ABCD\",\"Active\":true}"
                + "],\"TotalRecords\":2}");
            stub.On("POST", "/api/v1/users", body => StubHttpHandler.Response(HttpStatusCode.Created, "{\"Id\":\"usr_new\",\"TenantId\":\"ten_default\",\"Email\":\"new@armada\"}"));
            stub.On("PUT", "/api/v1/users/usr_ops", body => StubHttpHandler.Response(HttpStatusCode.OK, "{\"Id\":\"usr_ops\",\"TenantId\":\"ten_two\",\"Email\":\"ops@armada\"}"));
            stub.On("DELETE", "/api/v1/users/usr_admin", body => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            stub.On("DELETE", "/api/v1/users/usr_ops", body => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            stub.On("POST", "/api/v1/credentials", body => StubHttpHandler.Response(HttpStatusCode.Created, "{\"Id\":\"crd_new\",\"TenantId\":\"ten_default\",\"UserId\":\"usr_admin\",\"Name\":\"Deploy\",\"BearerToken\":\"tok_NEWTOKEN1234\",\"Active\":true}"));
            stub.On("DELETE", "/api/v1/credentials/crd_1", body => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            stub.On("DELETE", "/api/v1/credentials/crd_2", body => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            stub.On("POST", "/api/v1/tenants", body => StubHttpHandler.Response(HttpStatusCode.Created, "{\"Id\":\"ten_three\",\"Name\":\"Third\",\"Active\":true}"));
            stub.On("PUT", "/api/v1/tenants/ten_two", body => StubHttpHandler.Response(HttpStatusCode.OK, "{\"Id\":\"ten_two\",\"Name\":\"Second Tenant\",\"Active\":false}"));
            stub.On("DELETE", "/api/v1/tenants/ten_two", body => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            return stub;
        }

        private static T Current<T>(TuiTestHost host) where T : ScreenBase
        {
            ScreenBase? screen = host.Tui.Shell.Screen;
            if (screen is HubScreen hub) screen = hub.Content;
            if (screen is T typed) return typed;
            throw new AssertionException("current screen is " + (screen?.GetType().Name ?? "null") + ", expected " + typeof(T).Name);
        }
    }
}
