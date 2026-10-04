namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using Armada.Tui.Screens;
    using Armada.Tui.Services;
    using Armada.Tui.Services.Credentials;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Login flows against a stubbed server: email with one tenant, the tenant picker, zero tenants, wrong password,
    /// API key success and failure, resume from the stored token, and a 401 returning to login.
    /// </summary>
    public sealed class TuiLoginSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Login";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "email_single_tenant", "Email login with one tenant skips the picker and signs in", () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer(1);
                using (TuiTestHost host = new TuiTestHost(120, 40, stub, "http://127.0.0.1:9", o => o.StartRoute = "/inbox"))
                {
                    host.Start();
                    TuiCase.Contains(host.Screen(), "Default credentials", "hint");
                    AssertFalse(host.Tui.Context.Session.IsSignedIn, "starts signed out");
                    host.Type("admin@armada").Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Shell.Login.Step == LoginStepEnum.Password), "password step");
                    TuiCase.Contains(host.Screen(), "Signing in as admin@armada to Default Tenant", "context line");
                    host.Type("password").Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Session.IsSignedIn && host.Tui.Shell.Screen != null), "signed in");
                    AssertEqual("/inbox", host.Tui.Context.Router.Current!.Path, "start route");
                    TuiCase.Contains(host.Screen(), "admin@armada", "header user");
                    string stored = host.Tui.Context.Credentials.GetAsync(SessionService.CredentialKey(host.Tui.Context.Session.Profile)).GetAwaiter().GetResult() ?? "";
                    AssertEqual("tok_session", stored, "token stored");
                    AssertTrue(stub.Requests.Any(r => r.StartsWith("GET /api/v1/whoami")), "whoami called");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "email_tenant_picker", "Several tenants show the tenant picker, Back returns to email", () =>
            {
                using (TuiTestHost host = new TuiTestHost(120, 40, TuiFixtures.SignedInServer(2)))
                {
                    host.Start();
                    host.Type("admin@armada").Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Shell.Login.Step == LoginStepEnum.Tenant), "tenant step");
                    TuiCase.Contains(host.Screen(), "Select a tenant...", "picker placeholder");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "picker open");
                    TuiCase.Contains(host.Screen(), "Second Tenant", "options listed");
                    host.Press("down").Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Shell.Login.Tenant.Value == "ten_two"), "chosen");
                    host.Tui.Shell.Login.SubmitTenant();
                    AssertEqual(LoginStepEnum.Password, host.Tui.Shell.Login.Step, "password step");
                    TuiCase.Contains(host.Screen(), "to Second Tenant", "tenant shown");
                    host.Press("esc");
                    AssertEqual(LoginStepEnum.Tenant, host.Tui.Shell.Login.Step, "back to picker");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "zero_tenants", "No tenants shows the dashboard's error", () =>
            {
                using (TuiTestHost host = new TuiTestHost(120, 40, TuiFixtures.SignedInServer(0)))
                {
                    host.Start();
                    host.Type("nobody@armada").Press("enter");
                    AssertTrue(host.WaitForText("No tenants found for this email."), "error");
                    AssertEqual(LoginStepEnum.Email, host.Tui.Shell.Login.Step, "stays on email");
                }
            }, TestTags.Negative));

            cases.Add(TuiCase.Sync(Suite, "wrong_password", "A wrong password shows Authentication failed and clears the field", () =>
            {
                using (TuiTestHost host = new TuiTestHost(120, 40, TuiFixtures.SignedInServer(1)))
                {
                    host.Start();
                    host.Type("admin@armada").Press("enter");
                    host.PumpUntil(() => host.Tui.Shell.Login.Step == LoginStepEnum.Password);
                    host.Type("nope").Press("enter");
                    AssertTrue(host.WaitForText("Authentication failed."), "error");
                    AssertFalse(host.Tui.Context.Session.IsSignedIn, "not signed in");
                    AssertEqual("", host.Tui.Shell.Login.Password.Value, "password cleared");
                    TuiCase.NotContains(host.Screen(), "nope", "password never shown");
                }
            }, TestTags.Negative));

            cases.Add(TuiCase.Sync(Suite, "password_masked_and_reveal", "Passwords are masked and Ctrl+R reveals them", () =>
            {
                using (TuiTestHost host = new TuiTestHost(120, 40, TuiFixtures.SignedInServer(1)))
                {
                    host.Start();
                    host.Type("admin@armada").Press("enter");
                    host.PumpUntil(() => host.Tui.Shell.Login.Step == LoginStepEnum.Password);
                    host.Type("s3cret");
                    string frame = host.Screen();
                    TuiCase.NotContains(frame, "s3cret", "masked");
                    TuiCase.Contains(frame, "******", "mask characters");
                    host.Press("ctrl+r");
                    TuiCase.Contains(host.Screen(), "s3cret", "revealed");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "api_key_login", "The API key tab validates the key with whoami", () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer(1);
                using (TuiTestHost host = new TuiTestHost(120, 40, stub))
                {
                    host.Start();
                    host.Press("f2");
                    AssertTrue(host.Tui.Shell.Login.ApiKeyMode, "api key mode");
                    TuiCase.Contains(host.Screen(), "API Key / Bearer Token", "label");
                    host.Paste("key_123").Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Session.IsSignedIn), "signed in");
                    AssertEqual("apikey", host.Tui.Context.Session.Profile.AuthMethod, "method remembered");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "api_key_rejected", "A rejected API key shows the dashboard's error", () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer(1);
                stub.Json("GET", "/api/v1/whoami", "{\"Error\":\"Unauthorized\",\"Message\":\"Authentication required\"}", HttpStatusCode.Unauthorized);
                using (TuiTestHost host = new TuiTestHost(120, 40, stub))
                {
                    host.Start();
                    host.Press("f2").Type("bad").Press("enter");
                    AssertTrue(host.WaitForText("API key authentication failed."), "error");
                    AssertFalse(host.Tui.Context.Session.IsSignedIn, "not signed in");
                }
            }, TestTags.Negative));

            cases.Add(TuiCase.Sync(Suite, "resume_and_expire", "A stored token resumes the session; a 401 returns to login with a notice", () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer(1);
                using (TuiTestHost host = new TuiTestHost(120, 40, stub))
                {
                    FileCredentialStore store = new FileCredentialStore(Path.Combine(host.TempDir, "creds.json"));
                    store.SetAsync(SessionService.CredentialKey(host.Tui.Context.Session.Profile), "tok_saved").GetAwaiter().GetResult();
                    host.Start();
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Session.IsSignedIn), "resumed");
                    AssertEqual("tok_saved", host.Tui.Context.Session.Token, "stored token used");
                    stub.Json("GET", "/api/v1/inbox", "{\"Message\":\"Authentication required\"}", HttpStatusCode.Unauthorized);
                    host.Tui.Context.Status.PollAllAsync().GetAwaiter().GetResult();
                    AssertTrue(host.PumpUntil(() => !host.Tui.Context.Session.IsSignedIn), "expired");
                    AssertTrue(host.WaitForText("Your session expired. Sign in again."), "notice");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "sign_out_forgets_token", "Sign out removes the stored token", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40))
                {
                    host.Tui.Context.Commands.Execute("file.sign-out");
                    AssertTrue(host.PumpUntil(() => !host.Tui.Context.Session.IsSignedIn), "signed out");
                    string? stored = host.Tui.Context.Credentials.GetAsync(SessionService.CredentialKey(host.Tui.Context.Session.Profile)).GetAwaiter().GetResult();
                    AssertNull(stored, "token removed");
                    TuiCase.Contains(host.Screen(), "Email Login", "login shown");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "language_and_theme_pickers", "The login screen offers language and theme pickers", () =>
            {
                using (TuiTestHost host = new TuiTestHost(120, 40, TuiFixtures.SignedInServer(1)))
                {
                    host.Start();
                    LoginView login = host.Tui.Shell.Login;
                    AssertTrue(login.Language.Options.Count >= 9, "nine locales");
                    login.ThemePicker.Choose(login.ThemePicker.Options.First(o => o.Value == Armada.Tui.Theming.ThemeModeEnum.Light));
                    AssertEqual(Armada.Tui.Theming.ThemeModeEnum.Light, host.Tui.Context.Theme.EffectiveMode, "theme applied");
                    login.Language.Choose(login.Language.Options.First(o => o.Value == "de"));
                    AssertEqual("de", host.Tui.Context.Loc.Locale, "locale applied");
                    AssertEqual("de", host.Tui.Context.Prefs.Current.Locale, "locale persisted");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI login and session", cases: cases);
        }
    }
}
