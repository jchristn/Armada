namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Client.Socket;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Tui.Approvals;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Admin;
    using Armada.Tui.Screens.Ask;
    using Armada.Tui.Screens.Build;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// CLI tool permissions in the TUI beyond the Approvals center, against a stubbed client: the CLI Tool Permissions hub
    /// (Rules: list, scope filter, create with the dashboard's validation and pickers, edit, delete with confirmation,
    /// read-only for non-admins; Requests: status filter, a decision, the inbox deep link), the server settings section
    /// (values, save, the Bypass warning, read-only for non-global-admins) and the retention field, the captain form and
    /// page policy (Bypass only after the warning, saved through its own endpoint), the Ask header policy picker, Ask
    /// CLI permission cards (a / A / d, clickable buttons, inline keys, the pending strip and hints, live sync), deciding
    /// from wherever the user highlights (the reply of the turn that proposed an action, every pending card as its own
    /// stop, a chooser for several, a hint instead of a silent no-op), toasts for new requests, the mission's policy
    /// note, and the refused tool row.
    /// </summary>
    public sealed class TuiCliPermissionFlowsSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.CliPermissionFlows";
        private const string RulesPath = "/api/v1/cli-permissions/rules";
        private const string DecidePath = "/api/v1/cli-permissions/requests/cpr_1/decide";
        private const string ThreadPolicyPath = "/api/v1/ask/threads/ath_1/cli-permission-policy";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            // ---------------------------------------------------------------- rules

            cases.Add(TuiCase.Sync(Suite, "rules_list_and_filter", "The Rules tab lists pattern, action, target, and description, and the scope filter asks the server", () =>
            {
                StubHttpHandler stub = RulesStub();
                using (TuiTestHost host = TuiCase.SignedIn(170, 48, "/cli-permissions?tab=rules", stub))
                {
                    CliPermissionRulesScreen screen = Content<CliPermissionRulesScreen>(host);
                    AssertTrue(WaitForRules(host, screen), "rule listed\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Requests", "requests tab");
                    TuiCase.Contains(frame, "Vessel web", "vessel target by name");
                    TuiCase.Contains(frame, "Global (all tenants)", "all-tenant rule");
                    TuiCase.Contains(frame, "no pushes", "description");
                    TuiCase.Contains(frame, "[ + Rule n ]", "create button for admins");
                    screen.ScopeFilter.Choose(screen.ScopeFilter.Options.First(o => o.Value == "Vessel"));
                    AssertTrue(host.PumpUntil(() => stub.Saw("GET", RulesPath, r => r.QueryValue("scope") == "Vessel")), "scope sent: " + String.Join("\n", stub.Requests));
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "rules_create_validates_and_posts", "New rule validates pattern and the scope target, then posts the rule with the picked vessel", () =>
            {
                StubHttpHandler stub = RulesStub();
                using (TuiTestHost host = TuiCase.SignedIn(170, 48, "/cli-permissions?tab=rules", stub))
                {
                    CliPermissionRulesScreen screen = Content<CliPermissionRulesScreen>(host);
                    AssertTrue(WaitForRules(host, screen), "loaded");
                    AssertTrue(host.PumpUntil(() => screen.Vessels.Count == 1 && screen.Captains.Count == 1), "pickers loaded");
                    host.Press("n");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.Top is FormModal), "form");
                    FormModal form = (FormModal)host.App.Modals.Top!;
                    TuiCase.Contains(host.Screen(), "New rule", "form title");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => form.Error == "Enter a rule pattern."), "pattern required");
                    Field<InputField>(form, "Pattern").Value = "Bash(npm test:*)";
                    SelectField<string> scope = Field<SelectField<string>>(form, "Scope");
                    scope.Choose(scope.Options.First(o => o.Value == "Vessel"));
                    SelectField<string> vessel = Field<SelectField<string>>(form, "Vessel");
                    AssertTrue(vessel.Visible, "vessel picker shown for a Vessel rule");
                    AssertFalse(Field<SelectField<string>>(form, "Captain").Visible, "captain picker hidden");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => form.Error == "Choose a vessel."), "vessel required");
                    vessel.Choose(vessel.Options.First(o => o.Value == "vsl_1"));
                    SelectField<string> action = Field<SelectField<string>>(form, "Action");
                    action.Choose(action.Options.First(o => o.Value == "Deny"));
                    Field<InputField>(form, "Description").Value = "tests only";
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", RulesPath) == 1), "create call");
                    CliPermissionRule body = stub.LastBody<CliPermissionRule>("POST", RulesPath);
                    AssertEqual("Bash(npm test:*)", body.Pattern, "pattern");
                    AssertEqual(CliPermissionRuleActionEnum.Deny, body.Action, "action");
                    AssertEqual(CliPermissionRuleScopeEnum.Vessel, body.Scope, "scope");
                    AssertEqual("vsl_1", body.VesselId, "vessel");
                    AssertNull(body.CaptainId, "no captain");
                    AssertEqual("tests only", body.Description, "description");
                    AssertTrue(TuiToasts.WaitForSuccess(host, "Rule created."), "toast");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("GET", RulesPath) >= 2), "reloaded");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "rules_edit_and_delete", "Enter edits a rule's pattern, action, and description; Del asks with the dashboard's message and deletes", () =>
            {
                StubHttpHandler stub = RulesStub();
                using (TuiTestHost host = TuiCase.SignedIn(170, 48, "/cli-permissions?tab=rules", stub))
                {
                    CliPermissionRulesScreen screen = Content<CliPermissionRulesScreen>(host);
                    AssertTrue(WaitForRules(host, screen), "loaded");
                    screen.Grid.MoveCursor(screen.Grid.Rows.ToList().FindIndex(r => r.Id == "cpl_1"));
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.Top is FormModal), "edit form");
                    FormModal form = (FormModal)host.App.Modals.Top!;
                    TuiCase.Contains(host.Screen(), "Edit rule", "edit title");
                    AssertFalse(form.Form.Rows.Any(r => r.Label == "Scope"), "the scope of an existing rule does not change");
                    InputField pattern = Field<InputField>(form, "Pattern");
                    AssertEqual("Bash(git status:*)", pattern.Value, "prefilled");
                    pattern.Value = "Bash(git status --short:*)";
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("PUT", RulesPath + "/cpl_1") == 1), "update call");
                    CliPermissionRule body = stub.LastBody<CliPermissionRule>("PUT", RulesPath + "/cpl_1");
                    AssertEqual("Bash(git status --short:*)", body.Pattern, "pattern");
                    AssertEqual(CliPermissionRuleActionEnum.Allow, body.Action, "action kept");
                    AssertTrue(TuiToasts.WaitForSuccess(host, "Rule saved."), "toast");

                    host.Press("del");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.Top is ConfirmDialog), "confirm");
                    TuiCase.Contains(host.Screen(), "Delete the Allow rule Bash(git status:*)?", "dashboard message");
                    host.Press("n");
                    host.Pump();
                    AssertEqual(0, stub.CountFor("DELETE", RulesPath + "/cpl_1"), "cancel sends nothing");
                    host.Press("del");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.Top is ConfirmDialog), "confirm again");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("DELETE", RulesPath + "/cpl_1") == 1), "delete call");
                    AssertTrue(TuiToasts.WaitForSuccess(host, "Rule deleted."), "toast");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "rules_read_only_for_users", "A user who is not an admin sees the rules read-only: no create, no edit form, no delete", () =>
            {
                StubHttpHandler stub = RulesStub();
                NotAdmin(stub);
                using (TuiTestHost host = TuiCase.SignedIn(170, 48, "/cli-permissions?tab=rules", stub))
                {
                    CliPermissionRulesScreen screen = Content<CliPermissionRulesScreen>(host);
                    AssertTrue(WaitForRules(host, screen), "loaded");
                    AssertFalse(screen.CanEdit, "read-only");
                    TuiCase.NotContains(host.Screen(), "+ Rule", "no create button");
                    host.Press("n");
                    host.Pump();
                    AssertFalse(host.App.Modals.Top is FormModal, "no form");
                    AssertNull(screen.OpenForm(null), "form refused");
                    AssertNull(screen.ConfirmDelete(screen.Items[0]), "delete refused");
                    AssertEqual(0, stub.CountFor("POST", RulesPath) + stub.CountFor("DELETE", RulesPath + "/cpl_1"), "nothing sent");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "rules_wait_for_rows_not_header_example", "Rules tests wait for the rows: the header's example pattern is on screen while the list is still loading, and a user who is not an admin gets no create control before or after it loads", () =>
            {
                // Regression (full net10.0 run under load): the Rules cases waited for "Bash(git status:*)" in the frame,
                // but the header help quotes that pattern as its example, so the wait passed before the list loaded and
                // the case then read an empty grid (Items[0], the edit form for a missing row). Hold the list to force it.
                StubHttpHandler stub = RulesStub();
                NotAdmin(stub);
                string rulesJson = ArmadaJson.Serialize(Rules());
                using (ManualResetEventSlim releaseRules = new ManualResetEventSlim(false))
                {
                    stub.On("GET", RulesPath, body =>
                    {
                        releaseRules.Wait(TimeSpan.FromSeconds(30));
                        return StubHttpHandler.Response(HttpStatusCode.OK, rulesJson);
                    });

                    using (TuiTestHost host = TuiCase.SignedIn(170, 48, "/cli-permissions?tab=rules", stub))
                    {
                        try
                        {
                            CliPermissionRulesScreen screen = Content<CliPermissionRulesScreen>(host);
                            AssertTrue(host.PumpUntil(() => stub.CountFor("GET", RulesPath) == 1), "list call held");
                            AssertTrue(host.WaitForText("Bash(git status:*)"), "the header example is on screen while the list is held\n" + host.Screen());
                            AssertFalse(RulesShown(screen), "not loaded while held");
                            AssertEqual(0, screen.Items.Count, "no rules yet");
                            AssertFalse(screen.CanEdit, "role known before the list loads");
                            TuiCase.NotContains(host.Screen(), "+ Rule", "no create button while loading");
                            host.Press("n");
                            AssertFalse(host.App.Modals.Top is FormModal, "no form while loading");

                            releaseRules.Set();
                            AssertTrue(WaitForRules(host, screen), "loaded after release\n" + host.Screen());
                            TuiCase.Contains(host.Screen(), "no pushes", "rows rendered");
                            TuiCase.NotContains(host.Screen(), "+ Rule", "no create button after the load");
                            AssertNull(screen.ConfirmDelete(screen.Items[0]), "delete refused");
                        }
                        finally
                        {
                            releaseRules.Set();
                        }
                    }
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "requests_tab_decides_and_deep_links", "The Requests tab lists pending requests, a allows the selected one, and ?request= selects the linked request", () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer();
                CliPermissionRequest other = Request("cpr_0", null, true, true);
                other.SummaryText = "ls";
                other.CreatedUtc = DateTime.UtcNow.AddMinutes(-1);
                CliPermissionRequest linked = Request("cpr_1", null, true, true);
                linked.CreatedUtc = DateTime.UtcNow.AddMinutes(-5);
                stub.Json("GET", "/api/v1/cli-permissions/requests", ArmadaJson.Serialize(new List<CliPermissionRequest> { other, linked }));
                stub.Json("POST", DecidePath, ArmadaJson.Serialize(Decided(linked, CliPermissionRequestStatusEnum.Allowed)));
                using (TuiTestHost host = TuiCase.SignedIn(170, 48, "/cli-permissions?request=cpr_1", stub))
                {
                    CliPermissionRequestsScreen screen = Content<CliPermissionRequestsScreen>(host);
                    AssertTrue(host.WaitForText("Bash: git push origin main"), "listed\n" + host.Screen());
                    AssertTrue(stub.Saw("GET", "/api/v1/cli-permissions/requests", r => r.QueryValue("status") == "Pending"), "pending by default");
                    AssertTrue(host.PumpUntil(() => screen.Grid.Current?.Id == "cpr_1"), "the linked request is selected");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "[ Allow once a ]", "allow once button");
                    TuiCase.Contains(frame, "[ Allow and remember A ]", "remember button");
                    TuiCase.Contains(frame, "[ Deny d ]", "deny button");
                    AssertTrue(screen.Hints.Any(h => h.Key == "a") && screen.Hints.Any(h => h.Key == "d"), "decision hints");
                    host.Press("a");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", DecidePath) == 1), "decide call");
                    AssertEqual(CliPermissionDecisionEnum.AllowOnce, stub.LastBody<CliPermissionDecisionRequest>("POST", DecidePath).Decision, "allow once");
                    AssertTrue(TuiToasts.WaitForSuccess(host, "Allowed Bash once."), "toast");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "nav_and_palette", "CLI Tool Permissions is in the SYSTEM section, the palette, and the route map", () =>
            {
                Armada.Tui.Routing.NavItem? item = Armada.Tui.Routing.NavCatalog.Sections.First(s => s.Label == "SYSTEM").Items.FirstOrDefault(i => i.To == "/cli-permissions");
                AssertNotNull(item, "sidebar item");
                AssertEqual("CLI Tool Permissions", item!.Label, "label");
                Armada.Tui.Routing.RouteMatch match = Armada.Tui.Routing.Router.Resolve("/cli-permissions?tab=rules");
                AssertNull(match.Route.RedirectTo, "a screen, not a redirect");
                AssertEqual("rules", match.Tab?.Key, "rules tab");
                using (TuiTestHost host = TuiCase.SignedIn(170, 48, "/missions", RulesStub()))
                {
                    AssertNotNull(host.Tui.Context.Commands.All().FirstOrDefault(c => c.Id == "go.clipermissions"), "go command");
                }
            }));

            // ---------------------------------------------------------------- settings

            cases.Add(TuiCase.Sync(Suite, "settings_permissions_section", "The CLI Tool Permissions section shows the server values and saves the Permissions group", () =>
            {
                StubHttpHandler stub = SettingsStub();
                using (TuiTestHost host = OpenSettings(stub, out ServerSettingsScreen screen))
                {
                    AssertTrue(screen.Form.Rows.Any(r => r.IsSection && r.Label == "CLI Tool Permissions"), "section");
                    AssertEqual("ApproveInArmada", screen.PermissionsAskDefault.Value, "ask default");
                    AssertEqual("Bypass", screen.PermissionsMissionDefault.Value, "mission default");
                    AssertEqual("300", screen.PermissionsTimeout.Value, "timeout");
                    AssertFalse(screen.PermissionsOwnerApproval.Value, "owner approval");
                    AssertEqual("30", screen.RetentionCliPermissions.Value, "retention");
                    screen.PermissionsAskDefault.Choose(screen.PermissionsAskDefault.Options.First(o => o.Value == "Refuse"));
                    screen.Form.Scope.Focus(screen.PermissionsTimeout);
                    host.Press("ctrl+u").Type("5");
                    host.Press("ctrl+s");
                    host.Pump();
                    AssertEqual(0, stub.CountFor("PUT", "/api/v1/settings"), "an out-of-range timeout blocks the save");
                    TuiCase.Contains(host.Screen(), "Must be a whole number from 10 to 3,600.", "range message");
                    host.Press("ctrl+u").Type("120");
                    host.Screen();
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("PUT", "/api/v1/settings") == 1), "PUT");
                    SettingsData body = stub.LastBody<SettingsData>("PUT", "/api/v1/settings");
                    AssertNotNull(body.Permissions, "permissions group sent");
                    AssertEqual(CliPermissionPolicyEnum.Refuse, body.Permissions!.AskDefaultPolicy, "ask default");
                    AssertEqual(CliPermissionPolicyEnum.Bypass, body.Permissions.MissionDefaultPolicy, "mission default kept");
                    AssertEqual(120, body.Permissions.PromptTimeoutSeconds, "timeout");
                    AssertNull(body.Retention, "only the group");
                    AssertTrue(TuiToasts.WaitForSuccess(host, "CLI tool permission settings saved."), "toast");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "settings_bypass_needs_the_warning", "Choosing Bypass shows the strong warning; cancelling keeps the previous value, confirming sets Bypass", () =>
            {
                StubHttpHandler stub = SettingsStub();
                using (TuiTestHost host = OpenSettings(stub, out ServerSettingsScreen screen))
                {
                    SelectField<string> ask = screen.PermissionsAskDefault;
                    ask.Choose(ask.Options.First(o => o.Value == "Bypass"));
                    AssertTrue(host.PumpUntil(() => host.App.Modals.Top is ConfirmDialog), "warning");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Allow every CLI tool call without asking?", "title");
                    TuiCase.Contains(frame, "Use Bypass", "confirm label");
                    AssertEqual("ApproveInArmada", ask.Value, "unchanged while the warning is open");
                    host.Press("n");
                    AssertTrue(host.PumpUntil(() => !host.App.Modals.IsActive), "closed");
                    AssertEqual("ApproveInArmada", ask.Value, "cancel keeps the previous value");
                    AssertFalse(screen.Groups["permissions"].IsDirty, "not dirty");
                    ask.Choose(ask.Options.First(o => o.Value == "Bypass"));
                    AssertTrue(host.PumpUntil(() => host.App.Modals.Top is ConfirmDialog), "warning again");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => ask.Value == "Bypass"), "confirmed");
                    AssertTrue(screen.Groups["permissions"].IsDirty, "dirty");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "settings_read_only_for_tenant_admins", "A tenant admin sees the section read-only with Bypass disabled", () =>
            {
                StubHttpHandler stub = SettingsStub();
                stub.On("GET", "/api/v1/whoami", body => StubHttpHandler.Response(HttpStatusCode.OK, "{\"Tenant\":{\"Id\":\"ten_default\",\"Name\":\"Default Tenant\",\"Active\":true},\"User\":{\"Id\":\"usr_ta\",\"TenantId\":\"ten_default\",\"Email\":\"ta@armada\",\"IsAdmin\":false,\"IsTenantAdmin\":true,\"Active\":true}}"));
                using (TuiTestHost host = OpenSettings(stub, out ServerSettingsScreen screen))
                {
                    AssertFalse(screen.Groups["permissions"].Editable, "not editable");
                    AssertFalse(screen.PermissionsAskDefault.CanFocus || screen.PermissionsTimeout.CanFocus || screen.PermissionsOwnerApproval.CanFocus, "fields read-only");
                    AssertFalse(screen.PermissionsMissionDefault.Options.First(o => o.Value == "Bypass").Enabled, "Bypass disabled");
                    AssertEqual("Bypass", screen.PermissionsMissionDefault.Value, "a stored Bypass still shows");
                    screen.PermissionsAskDefault.Choose(screen.PermissionsAskDefault.Options.First(o => o.Value == "Bypass"));
                    host.Pump();
                    AssertFalse(host.App.Modals.Top is ConfirmDialog, "no warning: not allowed");
                    AssertEqual("ApproveInArmada", screen.PermissionsAskDefault.Value, "unchanged");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "settings_retention_field", "Data Retention saves the CLI permission request retention days", () =>
            {
                StubHttpHandler stub = SettingsStub();
                using (TuiTestHost host = OpenSettings(stub, out ServerSettingsScreen screen))
                {
                    screen.Form.Scope.Focus(screen.RetentionCliPermissions);
                    host.Press("ctrl+u").Type("14");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("PUT", "/api/v1/settings") == 1), "PUT");
                    SettingsData body = stub.LastBody<SettingsData>("PUT", "/api/v1/settings");
                    AssertEqual(14, body.Retention!.CliPermissionRequestRetentionDays, "retention days");
                    AssertEqual(45, body.Retention.JobRetentionDays, "other retention values kept");
                }
            }));

            // ---------------------------------------------------------------- captains

            cases.Add(TuiCase.Sync(Suite, "captain_policy_bypass_confirm", "The captain form's CLI tool permissions field asks before Bypass and saves it through its own endpoint", () =>
            {
                StubHttpHandler stub = CaptainStub();
                using (TuiTestHost host = TuiCase.SignedIn(170, 50, "/captains/cpt_1", stub))
                {
                    AssertTrue(host.WaitForText("Inherit (auto-approve option, then server default)"), "policy row on the captain page\n" + host.Screen());
                    host.Press("e");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.Top is OpsFormDialog), "form");
                    OpsFormDialog dialog = (OpsFormDialog)host.App.Modals.Top!;
                    SelectField<string> policy = (SelectField<string>)dialog.Form.Rows.First(r => r.Label == "CLI tool permissions").Field!;
                    AssertEqual("", policy.Value, "inherit");
                    policy.Choose(policy.Options.First(o => o.Value == "Bypass"));
                    AssertTrue(host.PumpUntil(() => host.App.Modals.Top is ConfirmDialog), "warning");
                    TuiCase.Contains(host.Screen(), "Bypass lets the captain run any command", "warning text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => policy.Value == "Bypass" && host.App.Modals.Top is OpsFormDialog), "Bypass chosen");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("PUT", "/api/v1/captains/cpt_1/cli-permission-policy") == 1), "policy endpoint");
                    AssertEqual(1, stub.CountFor("PUT", "/api/v1/captains/cpt_1"), "captain update first");
                    AssertNull(stub.LastBody<Captain>("PUT", "/api/v1/captains/cpt_1").CliPermissionPolicy, "the update keeps the stored policy");
                    AssertEqual(CliPermissionPolicyEnum.Bypass, stub.LastBody<CliPermissionPolicyUpdateRequest>("PUT", "/api/v1/captains/cpt_1/cli-permission-policy").Policy, "Bypass sent");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "captain_policy_users_cannot_change", "For a user who is not an admin the policy field is read-only and Bypass is disabled", () =>
            {
                StubHttpHandler stub = CaptainStub();
                NotAdmin(stub);
                using (TuiTestHost host = TuiCase.SignedIn(170, 50, "/captains/cpt_1", stub))
                {
                    AssertTrue(host.WaitForText("claude-1"), "loaded");
                    host.Press("e");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.Top is OpsFormDialog), "form");
                    OpsFormDialog dialog = (OpsFormDialog)host.App.Modals.Top!;
                    Armada.Tui.Widgets.FormRow row = dialog.Form.Rows.First(r => r.Label == "CLI tool permissions");
                    SelectField<string> policy = (SelectField<string>)row.Field!;
                    AssertFalse(policy.CanFocus, "read-only");
                    AssertFalse(policy.Options.Any(o => o.Value == "Bypass" && o.Enabled), "Bypass not offered");
                    TuiCase.Contains(row.Hint ?? "", "Only admins can change this.", "says why");
                }
            }));

            // ---------------------------------------------------------------- Ask

            cases.Add(TuiCase.Sync(Suite, "ask_card_allow_once_strip_and_hints", "A pending CLI permission card counts in the strip, Esc lands on it with inline keys and hints, and a allows it once", () =>
            {
                AskFixtures fx = CliThread(out CliPermissionRequest request);
                fx.Stub.Json("POST", DecidePath, ArmadaJson.Serialize(Decided(request, CliPermissionRequestStatusEnum.Allowed)));
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/ask/ath_1", fx.Stub))
                {
                    AskScreen screen = LoadedCli(host);
                    string flat = Flat(host.Screen());
                    TuiCase.Contains(flat, "! 1 CLI tool request waiting for permission: Esc, then a to allow once, A to allow and remember, or d to deny (Ctrl+A for all)", "strip from the composer");
                    AssertTrue(Status(host).StartsWith(" Esc Leave the message box (then a allow, d deny)", StringComparison.Ordinal), "composer hint: " + Status(host));
                    host.Press("esc");
                    AssertEqual("amg_c1", screen.Transcript.SelectedKey, "Esc lands on the pending card");
                    flat = Flat(host.Screen());
                    TuiCase.Contains(flat, "1 CLI tool request waiting for permission: a to allow once, A to allow and remember, or d to deny", "strip on the card");
                    TuiCase.Contains(flat, "[Allow once] (a) [Allow and remember] (A) [Deny] (d)", "buttons");
                    TuiCase.Contains(flat, "> a Allow once A Allow and remember d Deny", "inline keys on the selected card");
                    AssertTrue(Status(host).StartsWith(" a Allow once  A Allow and remember  d Deny", StringComparison.Ordinal), "card hints: " + Status(host));
                    host.Press("a");
                    AssertTrue(host.PumpUntil(() => fx.Stub.CountFor("POST", DecidePath) == 1), "decide call");
                    AssertEqual(CliPermissionDecisionEnum.AllowOnce, fx.Stub.LastBody<CliPermissionDecisionRequest>("POST", DecidePath).Decision, "allow once");
                    AssertTrue(host.PumpUntil(() => Flat(host.Screen()).Contains("+ Allowed", StringComparison.Ordinal)), "card shows Allowed\n" + host.Screen());
                    AssertTrue(host.PumpUntil(() => !host.Screen().Contains("waiting for permission", StringComparison.Ordinal)), "strip gone");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "ask_card_remember_and_deny", "A on a card opens the rule dialog; d opens the deny dialog with an optional message", () =>
            {
                AskFixtures fx = CliThread(out CliPermissionRequest request);
                fx.Stub.Json("POST", DecidePath, ArmadaJson.Serialize(Decided(request, CliPermissionRequestStatusEnum.Allowed)));
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/ask/ath_1", fx.Stub))
                {
                    AskScreen screen = LoadedCli(host);
                    host.Press("esc").Press("A");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.Top is CliPermissionDecisionModal), "rule dialog");
                    TuiCase.Contains(host.Screen(), "Bash(git push:*)", "suggested rule");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => fx.Stub.CountFor("POST", DecidePath) == 1), "decide call");
                    CliPermissionDecisionRequest body = fx.Stub.LastBody<CliPermissionDecisionRequest>("POST", DecidePath);
                    AssertEqual(CliPermissionDecisionEnum.AllowAndRemember, body.Decision, "remember");
                    AssertEqual("Bash(git push:*)", body.RulePattern, "pattern");
                }

                AskFixtures fx2 = CliThread(out CliPermissionRequest request2);
                fx2.Stub.Json("POST", DecidePath, ArmadaJson.Serialize(Decided(request2, CliPermissionRequestStatusEnum.Denied)));
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/ask/ath_1", fx2.Stub))
                {
                    LoadedCli(host);
                    host.Press("esc").Press("d");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.Top is CliPermissionDecisionModal), "deny dialog");
                    host.Type("use the test script").Press("enter");
                    AssertTrue(host.PumpUntil(() => fx2.Stub.CountFor("POST", DecidePath) == 1), "decide call");
                    CliPermissionDecisionRequest body = fx2.Stub.LastBody<CliPermissionDecisionRequest>("POST", DecidePath);
                    AssertEqual(CliPermissionDecisionEnum.Deny, body.Decision, "deny");
                    AssertEqual("use the test script", body.Message, "message");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "ask_card_buttons_click", "Clicking [Deny] on a CLI permission card opens the deny dialog whatever has focus", () =>
            {
                AskFixtures fx = CliThread(out CliPermissionRequest request);
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/ask/ath_1", fx.Stub))
                {
                    AskScreen screen = LoadedCli(host);
                    AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.Composer), "composer focused");
                    string[] lines = host.Screen().Split('\n');
                    int y = Array.FindIndex(lines, l => l.Contains("[Allow once] (a)", StringComparison.Ordinal));
                    AssertTrue(y >= 0, "button row on screen");
                    int x = lines[y].IndexOf("[Deny]", StringComparison.Ordinal) + 2;
                    host.Click(x, y);
                    AssertTrue(host.PumpUntil(() => host.App.Modals.Top is CliPermissionDecisionModal), "deny dialog from the click");
                    AssertFalse(((CliPermissionDecisionModal)host.App.Modals.Top!).Remember, "the deny dialog");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "ask_live_sync", "cli_permission.resolved updates the card in place", () =>
            {
                AskFixtures fx = CliThread(out CliPermissionRequest request);
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/ask/ath_1", fx.Stub))
                {
                    LoadedCli(host);
                    CliPermissionRequest resolved = Decided(request, CliPermissionRequestStatusEnum.Denied);
                    resolved.CaptainName = null;
                    resolved.DecisionSource = CliPermissionDecisionSourceEnum.Approver;
                    host.Tui.Context.Events.Inject(AskFixtures.Event("cli_permission.resolved", new CliPermissionEvent { RequestId = "cpr_1", Status = CliPermissionRequestStatusEnum.Denied, Request = resolved }));
                    AssertTrue(host.PumpUntil(() => Flat(host.Screen()).Contains("x Denied", StringComparison.Ordinal)), "denied on the card\n" + host.Screen());
                    TuiCase.Contains(Flat(host.Screen()), "Captain claude-1", "names kept from the earlier copy");
                    AssertEqual(0, host.Tui.Ask.Conversation.PendingCliPermissions().Count, "no longer pending");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "ask_reply_of_the_turn_approves", "Esc, then Up/Down to the reply of the turn that proposed an action, then a approves it", () =>
            {
                AskFixtures fx = ProposalThread(1, out List<AskActionProposal> proposals);
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/ask/ath_1", fx.Stub))
                {
                    AskScreen screen = LoadedProposals(host, 1);
                    host.Press("esc");
                    AssertEqual("amg_p1", screen.Transcript.SelectedKey, "the card");
                    host.Press("up");
                    AssertEqual("amg_1", screen.Transcript.SelectedKey, "the user message");
                    host.Press("down").Press("down");
                    AssertEqual("amg_r", screen.Transcript.SelectedKey, "the captain's reply that proposed it");
                    AssertTrue(Status(host).StartsWith(" a Approve  r Reject", StringComparison.Ordinal), "the reply shows the decision keys: " + Status(host));
                    TuiCase.Contains(Flat(host.Screen()), "1 action waiting for approval: a to approve or r to reject", "strip says a works here");
                    host.Press("a");
                    AssertTrue(host.PumpUntil(() => fx.Stub.CountFor("POST", "/api/v1/ask/threads/ath_1/proposals/aap_1/approve") == 1), "approve call from the reply");
                    AssertEqual("amg_r", screen.Transcript.SelectedKey, "selection kept");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "ask_nothing_to_approve_hint", "a on a block with nothing to decide shows a hint, sends nothing, and keeps the selection", () =>
            {
                AskFixtures fx = ProposalThread(1, out List<AskActionProposal> proposals);
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/ask/ath_1", fx.Stub))
                {
                    AskScreen screen = LoadedProposals(host, 1);
                    host.Press("esc").Press("up");
                    AssertEqual("amg_1", screen.Transcript.SelectedKey, "the user message");
                    host.Press("a");
                    AssertTrue(host.PumpUntil(() => TuiToasts.Has(host, NotificationSeverityEnum.Info, "Nothing to approve here. Pending: Alt+Down jumps to it")), "hint");
                    AssertEqual(0, fx.Stub.CountFor("POST", "/api/v1/ask/threads/ath_1/proposals/aap_1/approve"), "nothing sent");
                    AssertEqual("amg_1", screen.Transcript.SelectedKey, "selection unchanged");
                    host.Press("d");
                    AssertTrue(host.PumpUntil(() => TuiToasts.Has(host, NotificationSeverityEnum.Info, "No CLI permission request here.")), "d hint");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "ask_each_pending_card_is_a_stop", "Up/Down stops on every pending card (proposal and CLI permission) and each shows its keys", () =>
            {
                AskFixtures fx = new AskFixtures();
                AskActionProposal p = AskFixtures.Proposal("aap_1", "ath_1", "dispatch", AskProposalStatusEnum.Pending);
                fx.Decisions(p);
                AskMessage card = AskFixtures.Message("amg_p1", "ath_1", 2, AskMessageRoleEnum.Assistant, AskMessageKindEnum.ActionProposal, "");
                card.ProposalId = p.Id;
                card.Proposal = p;
                CliPermissionRequest request = Request("cpr_1", "ath_1", true, true);
                AskMessage cli = CliCard("amg_c1", request, 5);
                fx.AddThread(AskFixtures.Thread("ath_1", "Two waits"),
                    AskFixtures.Message("amg_1", "ath_1", 1, AskMessageRoleEnum.User, AskMessageKindEnum.Text, "dispatch"),
                    card,
                    AskFixtures.Message("amg_2", "ath_1", 3, AskMessageRoleEnum.Assistant, AskMessageKindEnum.Text, "proposed"),
                    AskFixtures.Message("amg_3", "ath_1", 4, AskMessageRoleEnum.User, AskMessageKindEnum.Text, "run tests"),
                    cli,
                    AskFixtures.Message("amg_4", "ath_1", 6, AskMessageRoleEnum.Assistant, AskMessageKindEnum.Text, "waiting"));
                fx.Pending["ath_1"] = new List<AskActionProposal> { p };
                using (TuiTestHost host = TuiCase.SignedIn(160, 60, "/ask/ath_1", fx.Stub))
                {
                    AskScreen screen = (AskScreen)host.Tui.Shell.Screen!;
                    AssertTrue(host.PumpUntil(() => host.Tui.Ask.Conversation.Messages.Count == 6 && host.Tui.Ask.Conversation.PendingCliPermissions().Count == 1 && host.Tui.Ask.Conversation.PendingProposals().Count == 1), "loaded");
                    host.Screen();
                    screen.FocusOldestPending();
                    host.Pump();
                    AssertEqual("amg_p1", screen.Transcript.SelectedKey, "Alt+Down: the oldest pending card");
                    TuiCase.Contains(Flat(host.Screen()), "> a Approve r Reject x Arguments", "inline keys on the selected proposal card");
                    List<string> stops = new List<string>();
                    for (int i = 0; i < 4; i++)
                    {
                        host.Press("down");
                        stops.Add(screen.Transcript.SelectedKey ?? "");
                        if (screen.Transcript.SelectedKey == "amg_c1")
                        {
                            AssertTrue(Status(host).StartsWith(" a Allow once", StringComparison.Ordinal), "CLI card hints: " + Status(host));
                            TuiCase.Contains(Flat(host.Screen()), "> a Allow once A Allow and remember d Deny", "inline keys on the selected CLI card");
                        }
                    }

                    AssertEqual("amg_2,amg_3,amg_c1,amg_4", String.Join(",", stops), "every block, the CLI card as its own stop");
                    TuiCase.Contains(Flat(host.Screen()), "! 1 action waiting for approval:", "proposal strip line");
                    TuiCase.Contains(Flat(host.Screen()), "! 1 CLI tool request waiting for permission:", "CLI strip line");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "ask_chooser_for_several", "a on a reply whose turn proposed two actions opens a chooser; choosing one approves it", () =>
            {
                AskFixtures fx = ProposalThread(2, out List<AskActionProposal> proposals);
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/ask/ath_1", fx.Stub))
                {
                    AskScreen screen = LoadedProposals(host, 2);
                    host.Press("esc").Press("down");
                    AssertEqual("amg_r", screen.Transcript.SelectedKey, "the reply");
                    host.Press("a");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "chooser");
                    TuiCase.Contains(host.Screen(), "Choose what to decide", "chooser title");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => fx.Stub.CountFor("POST", "/api/v1/ask/threads/ath_1/proposals/aap_1/approve") == 1), "the first approved");
                    AssertEqual(0, fx.Stub.CountFor("POST", "/api/v1/ask/threads/ath_1/proposals/aap_2/approve"), "only the chosen one");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "ask_header_policy", "The header shows the conversation's CLI tools policy; p changes it, and Bypass needs the warning", () =>
            {
                AskFixtures fx = CliThread(out CliPermissionRequest request);
                AskThread thread = fx.Threads[0];
                fx.Stub.On("PUT", ThreadPolicyPath, body =>
                {
                    thread.CliPermissionPolicy = JsonHelper.Deserialize<CliPermissionPolicyUpdateRequest>(body).Policy;
                    return StubHttpHandler.Response(HttpStatusCode.OK, ArmadaJson.Serialize(thread));
                });
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/ask/ath_1", fx.Stub))
                {
                    AskScreen screen = LoadedCli(host);
                    string flat = Flat(host.Screen());
                    TuiCase.Contains(flat, "CLI tools: Inherit [Esc p] Effective: Approve in Armada (from the server default).", "header line from the composer");
                    host.Press("esc");
                    TuiCase.Contains(Flat(host.Screen()), "CLI tools: Inherit [p]", "p works outside the composer");
                    host.Press("p");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.Top is PickerModal<string>), "picker");
                    PickerModal<string> picker = (PickerModal<string>)host.App.Modals.Top!;
                    picker.List.SelectValue("Refuse");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => fx.Stub.CountFor("PUT", ThreadPolicyPath) == 1), "policy call");
                    AssertEqual(CliPermissionPolicyEnum.Refuse, fx.Stub.LastBody<CliPermissionPolicyUpdateRequest>("PUT", ThreadPolicyPath).Policy, "Refuse");
                    AssertTrue(host.PumpUntil(() => Flat(host.Screen()).Contains("CLI tools: Refuse", StringComparison.Ordinal)), "header updated");

                    screen.PickCliPolicy();
                    picker = (PickerModal<string>)host.App.Modals.Top!;
                    picker.List.SelectValue("Bypass");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.Top is ConfirmDialog), "warning");
                    host.Press("n");
                    host.Pump();
                    AssertEqual(1, fx.Stub.CountFor("PUT", ThreadPolicyPath), "cancelled: nothing sent");
                    screen.PickCliPolicy();
                    ((PickerModal<string>)host.App.Modals.Top!).List.SelectValue("Bypass");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.Top is ConfirmDialog), "warning again");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => fx.Stub.CountFor("PUT", ThreadPolicyPath) == 2), "Bypass sent");
                    AssertEqual(CliPermissionPolicyEnum.Bypass, fx.Stub.LastBody<CliPermissionPolicyUpdateRequest>("PUT", ThreadPolicyPath).Policy, "Bypass");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "ask_header_policy_users", "A user who is not an admin cannot pick Bypass for a conversation", () =>
            {
                AskFixtures fx = CliThread(out CliPermissionRequest request);
                NotAdmin(fx.Stub);
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/ask/ath_1", fx.Stub))
                {
                    AskScreen screen = LoadedCli(host);
                    PickerModal<string>? picker = screen.PickCliPolicy();
                    AssertNotNull(picker, "picker");
                    AssertFalse(picker!.List.Visible.Any(o => o.Value == "Bypass" && o.Enabled), "Bypass not offered");
                    AssertTrue(picker.List.Visible.Any(o => o.Value == "Refuse"), "Refuse offered");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "toast_for_new_requests", "A new CLI permission request toasts with Open and rings; one in the conversation being watched does not", () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer();
                stub.Json("GET", "/api/v1/inbox", "[]");
                using (TuiTestHost host = TuiCase.SignedIn(140, 45, "/missions", stub))
                {
                    int arrived = 0;
                    host.Tui.Context.Approvals.Arrived += (s, e) => arrived++;
                    CliPermissionRequest request = Request("cpr_1", null, true, true);
                    host.Tui.Context.Events.Inject(AskFixtures.Event("cli_permission.requested", new CliPermissionEvent { RequestId = "cpr_1", Status = CliPermissionRequestStatusEnum.Pending, Request = request }));
                    AssertTrue(host.PumpUntil(() => TuiToasts.Has(host, NotificationSeverityEnum.Warning, "Permission needed: Bash: git push origin main")), "toast\n" + host.Screen());
                    AssertEqual(1, arrived, "attention (bell and OS notification)");
                    ToastEntry toast = host.Tui.Context.Notifications.ActiveToasts().First(t => t.Text.Contains("Permission needed", StringComparison.Ordinal));
                    AssertEqual("Open", toast.ActionLabel, "Open action");
                }

                AskFixtures fx = CliThread(out CliPermissionRequest pending);
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/ask/ath_1", fx.Stub))
                {
                    LoadedCli(host);
                    host.Tui.Context.Notifications.TerminalFocused = true;
                    CliPermissionRequest another = Request("cpr_2", "ath_1", true, true);
                    host.Tui.Context.Events.Inject(AskFixtures.Event("cli_permission.requested", new CliPermissionEvent { RequestId = "cpr_2", Status = CliPermissionRequestStatusEnum.Pending, Request = another }));
                    host.Pump();
                    host.Pump();
                    AssertFalse(TuiToasts.Has(host, NotificationSeverityEnum.Warning, "Permission needed"), "no toast for the conversation being watched");
                    AssertNotNull(host.Tui.Context.Approvals.Find(ApprovalKindEnum.CliPermission, "cpr_2"), "still queued");
                }
            }));

            // ---------------------------------------------------------------- mission note and refused row

            cases.Add(TuiCase.Sync(Suite, "mission_policy_note", "The mission page shows the CLI tool permission note the Admiral wrote at the top of the log", () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer();
                stub.Json("GET", "/api/v1/missions/msn_n", "{\"Id\":\"msn_n\",\"Title\":\"Noted\",\"Status\":\"InProgress\",\"Priority\":100,\"CreatedUtc\":\"2026-10-04T08:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T08:00:00Z\"}");
                string note = CliPermissionPolicyResolverNote();
                stub.Json("GET", "/api/v1/missions/msn_n/log", ArmadaJson.Serialize(new LogResult { Log = "[2026-10-04 08:00:00] Armada: " + note + "\nclaude started\n", Lines = 2, TotalLines = 2 }));
                using (TuiTestHost host = TuiCase.SignedIn(170, 50, "/missions/msn_n", stub))
                {
                    MissionScreen screen = (MissionScreen)host.Tui.Shell.Screen!;
                    AssertTrue(host.PumpUntil(() => screen.CliPermissionNote != null), "note loaded");
                    AssertEqual(note, screen.CliPermissionNote, "note text");
                    AssertTrue(stub.Saw("GET", "/api/v1/missions/msn_n/log", r => r.QueryValue("lines") == MissionScreen.NoteLogLines.ToString(System.Globalization.CultureInfo.InvariantCulture)), "reads the top of the log");
                    AssertTrue(host.WaitForText("CLI Tool Permissions"), "section\n" + host.Screen());
                    TuiCase.Contains(Flat(host.Screen()), "CLI tool permissions: Refuse (from the captain's CLI tool permission policy).", "note shown");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "refused_row_is_a_failure", "A tool call the CLI refused shows [x!] with the explanation even when its ok flag was not false", () =>
            {
                AskFixtures fx = new AskFixtures();
                AskThread thread = AskFixtures.Thread("ath_1", "Refused");
                thread.CliPermission = new CliPermissionResolution { Requested = CliPermissionPolicyEnum.Refuse, Effective = CliPermissionPolicyEnum.Refuse, Source = CliPermissionPolicySourceEnum.ServerDefault };
                AskMessage reply = AskFixtures.Message("amg_2", "ath_1", 2, AskMessageRoleEnum.Assistant, AskMessageKindEnum.Text, "I could not run it.");
                reply.ToolCalls.Add(new AskMessageToolCall { ToolName = "Bash", Ok = null, ResultText = "This command requires approval", PermissionDenied = true });
                fx.AddThread(thread, AskFixtures.Message("amg_1", "ath_1", 1, AskMessageRoleEnum.User, AskMessageKindEnum.Text, "run it"), reply);
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/ask/ath_1", fx.Stub))
                {
                    AskScreen screen = (AskScreen)host.Tui.Shell.Screen!;
                    AssertTrue(host.PumpUntil(() => host.Tui.Ask.Conversation.Messages.Count == 2 && host.Tui.Ask.Conversation.Thread?.CliPermission != null), "loaded");
                    screen.Transcript.Layout(150);
                    string text = String.Join("\n", screen.Transcript.PlainLines());
                    TuiCase.Contains(text, "[x!] Bash", "refused row");
                    TuiCase.NotContains(text, "[ok] Bash", "not shown as success");
                    TuiCase.Contains(Flat(text), "Refused: CLI tools run with policy Refuse (from the server default). Change it in the conversation header (CLI tools), on the captain, or in Settings > CLI Tool Permissions.", "explanation and where to change it");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI CLI tool permission flows", cases: cases);
        }

        // -------------------------------------------------------------------- fixtures

        private static string CliPermissionPolicyResolverNote()
        {
            Captain captain = new Captain("c") { CliPermissionPolicy = CliPermissionPolicyEnum.Refuse };
            return Armada.Core.Services.CliPermissionPolicyResolver.ResolveForMission(new ArmadaSettings(), captain, null, true, false).Note;
        }

        private static CliPermissionRequest Request(string id, string? threadId, bool canDecide, bool canRemember)
        {
            CliPermissionRequest request = new CliPermissionRequest();
            request.Id = id;
            request.TenantId = "ten_default";
            request.UserId = "usr_admin";
            request.CaptainId = "cpt_1";
            request.CaptainName = "claude-1";
            if (threadId != null)
            {
                request.ThreadId = threadId;
            }
            else
            {
                request.MissionId = "msn_1";
                request.MissionTitle = "Fix tables";
                request.VesselId = "vsl_1";
                request.VesselName = "web";
            }

            request.ToolName = "Bash";
            request.InputText = "{\"command\":\"git push origin main\"}";
            request.SummaryText = "git push origin main";
            request.SuggestedRule = "Bash(git push:*)";
            request.Status = CliPermissionRequestStatusEnum.Pending;
            request.CreatedUtc = DateTime.UtcNow.AddMinutes(-1);
            request.LastUpdateUtc = request.CreatedUtc;
            request.ExpiresUtc = DateTime.UtcNow.AddMinutes(9);
            request.CanDecide = canDecide;
            request.CanRemember = canRemember;
            return request;
        }

        private static CliPermissionRequest Decided(CliPermissionRequest request, CliPermissionRequestStatusEnum status)
        {
            CliPermissionRequest copy = ArmadaJson.Deserialize<CliPermissionRequest>(ArmadaJson.Serialize(request))!;
            copy.Status = status;
            copy.DecisionSource = CliPermissionDecisionSourceEnum.Approver;
            copy.DecidedUtc = DateTime.UtcNow;
            copy.LastUpdateUtc = DateTime.UtcNow;
            copy.CanDecide = false;
            copy.CanRemember = false;
            return copy;
        }

        private static AskMessage CliCard(string id, CliPermissionRequest request, int sequence)
        {
            AskMessage card = AskFixtures.Message(id, "ath_1", sequence, AskMessageRoleEnum.System, AskMessageKindEnum.CliPermission, "Bash: git push origin main");
            card.CliPermissionRequest = request;
            request.MessageId = id;
            return card;
        }

        private static AskFixtures CliThread(out CliPermissionRequest request)
        {
            AskFixtures fx = new AskFixtures();
            AskThread thread = AskFixtures.Thread("ath_1", "Release prep");
            thread.UserId = "usr_admin";
            thread.CliPermission = new CliPermissionResolution { Requested = CliPermissionPolicyEnum.ApproveInArmada, Effective = CliPermissionPolicyEnum.ApproveInArmada, Source = CliPermissionPolicySourceEnum.ServerDefault };
            request = Request("cpr_1", "ath_1", true, true);
            fx.AddThread(thread,
                AskFixtures.Message("amg_1", "ath_1", 1, AskMessageRoleEnum.User, AskMessageKindEnum.Text, "push it"),
                CliCard("amg_c1", request, 2));
            return fx;
        }

        private static AskFixtures ProposalThread(int count, out List<AskActionProposal> proposals)
        {
            AskFixtures fx = new AskFixtures();
            List<AskMessage> messages = new List<AskMessage> { AskFixtures.Message("amg_1", "ath_1", 1, AskMessageRoleEnum.User, AskMessageKindEnum.Text, "dispatch please") };
            proposals = new List<AskActionProposal>();
            for (int i = 1; i <= count; i++)
            {
                AskActionProposal p = AskFixtures.Proposal("aap_" + i, "ath_1", "dispatch", AskProposalStatusEnum.Pending);
                p.CreatedUtc = DateTime.UtcNow.AddMinutes(-10 + i);
                fx.Decisions(p);
                proposals.Add(p);
                AskMessage m = AskFixtures.Message("amg_p" + i, "ath_1", 1 + i, AskMessageRoleEnum.Assistant, AskMessageKindEnum.ActionProposal, "");
                m.ProposalId = p.Id;
                m.Proposal = p;
                messages.Add(m);
            }

            AskMessage reply = AskFixtures.Message("amg_r", "ath_1", 10, AskMessageRoleEnum.Assistant, AskMessageKindEnum.Text, "I proposed the dispatch; approve it to start.");
            reply.ToolCalls.Add(new AskMessageToolCall { ToolName = "mcp__armada__dispatch", Ok = true, ResultText = "proposed" });
            messages.Add(reply);
            fx.AddThread(AskFixtures.Thread("ath_1", "TUIKit fixes"), messages.ToArray());
            fx.Pending["ath_1"] = proposals;
            return fx;
        }

        private static AskScreen LoadedCli(TuiTestHost host)
        {
            AskScreen screen = (AskScreen)host.Tui.Shell.Screen!;
            AssertTrue(host.PumpUntil(() => host.Tui.Ask.Conversation.Messages.Count > 0 && host.Tui.Ask.Conversation.PendingCliPermissions().Count == 1 && host.Tui.Ask.Captains.Count > 0), "conversation loaded");
            host.Screen();
            return screen;
        }

        private static AskScreen LoadedProposals(TuiTestHost host, int pending)
        {
            AskScreen screen = (AskScreen)host.Tui.Shell.Screen!;
            AssertTrue(host.PumpUntil(() => host.Tui.Ask.Conversation.Messages.Count > 0 && host.Tui.Ask.Conversation.PendingProposals().Count == pending && host.Tui.Ask.Captains.Count > 0), "conversation loaded");
            host.Screen();
            return screen;
        }

        private static StubHttpHandler RulesStub()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            List<CliPermissionRule> rules = Rules();
            stub.Json("GET", RulesPath, ArmadaJson.Serialize(rules));
            stub.Json("POST", RulesPath, ArmadaJson.Serialize(rules[0]));
            stub.Json("PUT", RulesPath + "/cpl_1", ArmadaJson.Serialize(rules[0]));
            stub.On("DELETE", RulesPath + "/cpl_1", body => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            stub.Json("GET", "/api/v1/vessels", "{\"Objects\":[{\"Id\":\"vsl_1\",\"Name\":\"web\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/captains", "{\"Objects\":[{\"Id\":\"cpt_1\",\"Name\":\"claude-1\",\"Runtime\":\"ClaudeCode\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/cli-permissions/requests", "[]");
            return stub;
        }

        private static List<CliPermissionRule> Rules()
        {
            return new List<CliPermissionRule>
            {
                new CliPermissionRule { Id = "cpl_1", TenantId = "ten_default", Pattern = "Bash(git status:*)", Action = CliPermissionRuleActionEnum.Allow, Scope = CliPermissionRuleScopeEnum.Vessel, VesselId = "vsl_1", CreatedUtc = DateTime.UtcNow.AddHours(-1) },
                new CliPermissionRule { Id = "cpl_2", TenantId = null, Pattern = "Bash(git push:*)", Action = CliPermissionRuleActionEnum.Deny, Scope = CliPermissionRuleScopeEnum.Global, Description = "no pushes", CreatedUtc = DateTime.UtcNow.AddHours(-2) },
            };
        }

        private static StubHttpHandler SettingsStub()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            stub.Json("GET", "/api/v1/status/health", "{\"Status\":\"healthy\",\"Version\":\"0.9.0\",\"Uptime\":\"1.02:03:04\",\"Ports\":{\"Admiral\":7890,\"Mcp\":7891},\"RemoteTunnel\":{\"Enabled\":false,\"State\":\"Disabled\"}}");
            string settings = "{\"AdmiralPort\":7890,\"McpPort\":7891,\"MaxCaptains\":12,\"HeartbeatIntervalSeconds\":30,\"StallThresholdMinutes\":10,\"IdleCaptainTimeoutSeconds\":0,"
                + "\"Retention\":{\"AskThreadArchiveAfterDays\":90,\"AskThreadDeleteAfterDays\":0,\"JobRetentionDays\":45,\"ImportBatchRetentionDays\":90,\"CliPermissionRequestRetentionDays\":30},"
                + "\"Permissions\":{\"AskDefaultPolicy\":\"ApproveInArmada\",\"MissionDefaultPolicy\":\"Bypass\",\"AllowOwnerApproval\":false,\"PromptTimeoutSeconds\":300}}";
            stub.Json("GET", "/api/v1/settings", settings);
            stub.On("PUT", "/api/v1/settings", body => StubHttpHandler.Response(HttpStatusCode.OK, settings));
            stub.Json("GET", "/api/v1/vessels", "{\"Objects\":[],\"TotalRecords\":0}");
            return stub;
        }

        private static TuiTestHost OpenSettings(StubHttpHandler stub, out ServerSettingsScreen screen)
        {
            TuiTestHost host = TuiCase.SignedIn(160, 50, "/server?tab=server", stub);
            ServerSettingsScreen? found = null;
            host.PumpUntil(() =>
            {
                ScreenBase? s = host.Tui.Shell.Screen;
                if (s is HubScreen hub) s = hub.Content;
                found = s as ServerSettingsScreen;
                return found != null && found.Settings != null;
            }, 5000);
            if (found == null) throw new AssertionException("ServerSettingsScreen did not open");
            host.Pump();
            screen = found;
            return host;
        }

        private static StubHttpHandler CaptainStub()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            string captain = "{\"Id\":\"cpt_1\",\"Name\":\"claude-1\",\"Runtime\":\"ClaudeCode\",\"State\":\"Idle\",\"CreatedUtc\":\"2026-10-02T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T10:00:00Z\"}";
            stub.Json("GET", "/api/v1/captains/cpt_1", captain);
            stub.Json("PUT", "/api/v1/captains/cpt_1", captain);
            stub.Json("PUT", "/api/v1/captains/cpt_1/cli-permission-policy", "{\"Id\":\"cpt_1\",\"Name\":\"claude-1\",\"Runtime\":\"ClaudeCode\",\"State\":\"Idle\",\"CliPermissionPolicy\":\"Bypass\"}");
            stub.Json("GET", "/api/v1/missions/summaries", "{\"Success\":true,\"Objects\":[],\"TotalRecords\":0}");
            stub.Json("GET", "/api/v1/model-endpoints", "[]");
            return stub;
        }

        /// <summary>
        /// True once the Rules grid shows the two stubbed rules. The frame text alone does not say so: the screen's
        /// header help quotes Bash(git status:*) as its example pattern, so that text is on screen before the load.
        /// </summary>
        private static bool RulesShown(CliPermissionRulesScreen screen)
        {
            return screen.Grid.State == GridStateEnum.Ready && screen.Grid.Rows.Count == 2 && screen.Items.Count == 2;
        }

        private static bool WaitForRules(TuiTestHost host, CliPermissionRulesScreen screen)
        {
            return host.PumpUntil(() => RulesShown(screen));
        }

        private static void NotAdmin(StubHttpHandler stub)
        {
            stub.On("GET", "/api/v1/whoami", body => StubHttpHandler.Response(HttpStatusCode.OK, "{\"Tenant\":{\"Id\":\"ten_default\",\"Name\":\"Default Tenant\",\"Active\":true},\"User\":{\"Id\":\"usr_admin\",\"TenantId\":\"ten_default\",\"Email\":\"user@armada\",\"IsAdmin\":false,\"IsTenantAdmin\":false,\"Active\":true}}"));
        }

        private static T Content<T>(TuiTestHost host) where T : ScreenBase
        {
            T? found = null;
            host.PumpUntil(() =>
            {
                ScreenBase? s = host.Tui.Shell.Screen;
                if (s is HubScreen hub) s = hub.Content;
                found = s as T;
                return found != null;
            }, 5000);
            if (found == null) throw new AssertionException(typeof(T).Name + " did not open: " + host.Tui.Shell.Screen?.GetType().Name);
            return found;
        }

        private static T Field<T>(FormModal form, string label) where T : class
        {
            return (form.Form.Rows.First(r => r.Label == label).Field as T) ?? throw new AssertionException("field " + label);
        }

        private static string Status(TuiTestHost host)
        {
            string[] lines = host.Screen().Split('\n');
            return lines[host.Height - 1];
        }

        private static string Flat(string text)
        {
            return System.Text.RegularExpressions.Regex.Replace(text.Replace("|", " ").Replace("\u2502", " ").Replace("\u2503", " "), "\\s+", " ");
        }
    }
}
