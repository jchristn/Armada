namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens;
    using Armada.Tui.Services;
    using Armada.Tui.Services.Credentials;
    using Armada.Tui.Widgets;
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

        private static string WhoAmIOpen()
        {
            string json = TuiFixtures.WhoAmI();
            return json.Substring(0, json.Length - 1);
        }

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
                    AssertEqual("admin@armada", host.Tui.Shell.Login.Email.Value, "localhost prefill: email");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Shell.Login.Step == LoginStepEnum.Password), "password step");
                    TuiCase.Contains(host.Screen(), "Signing in as admin@armada to Default Tenant", "context line");
                    AssertEqual("password", host.Tui.Shell.Login.Password.Value, "localhost prefill: password");
                    host.Press("enter");
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

            cases.Add(TuiCase.Sync(Suite, "tab_order", "Tab moves through the login card top to bottom and wraps; Shift+Tab reverses", () =>
            {
                using (TuiTestHost host = new TuiTestHost(120, 40, TuiFixtures.SignedInServer(1), "http://127.0.0.1:9"))
                {
                    host.Start();
                    LoginView login = host.Tui.Shell.Login;
                    AssertTrue(ReferenceEquals(login.Scope.Focused, login.Email), "starts on email");
                    List<string> order = new List<string>();
                    for (int i = 0; i < 6; i++)
                    {
                        host.Press("tab");
                        order.Add(Name(login, login.Scope.Focused));
                    }

                    AssertEqual("buttons,language,theme,server,modes,email", String.Join(",", order), "tab order");
                    host.Press("shift+tab");
                    AssertEqual("modes", Name(login, login.Scope.Focused), "shift+tab reverses");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "edit_server", "e in the Server picker edits the highlighted server's name and URL", () =>
            {
                using (TuiTestHost host = new TuiTestHost(160, 40, TuiFixtures.SignedInServer(1), "http://127.0.0.1:9"))
                {
                    host.Start();
                    LoginView login = host.Tui.Shell.Login;
                    string before = host.Tui.Context.Session.Profile.Name;
                    PickerModal<string>? picker = login.Server.Open();
                    AssertNotNull(picker, "picker");
                    host.Pump();
                    TuiCase.Contains(host.Screen(), "e Edit", "footer hint");
                    picker!.List.SelectValue("__add__");
                    host.Press("e");
                    AssertTrue(host.PumpUntil(() => !host.Screen().Contains("Edit server")) && picker.List.Current?.Value == "__add__", "add row is not editable");
                    picker.List.SelectValue(before);
                    host.Press("E");
                    AssertTrue(host.WaitForText("Edit server"), "edit form opened");
                    host.Press("ctrl+u").Type("Local Lab").Press("tab").Press("ctrl+u").Type("not a url").Press("ctrl+s");
                    AssertTrue(host.WaitForText("Enter an absolute http or https URL."), "url validation");
                    host.Press("ctrl+u").Type("http://localhost:19/").Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Session.Profile.Url == "http://localhost:19"), "url saved and applied: " + host.Tui.Context.Session.Profile.Url);
                    AssertEqual("Local Lab", host.Tui.Context.Session.Profile.Name, "renamed");
                    AssertEqual("Local Lab", host.Tui.Context.Prefs.Current.ActiveProfile, "active profile follows the rename");
                    AssertNull(host.Tui.Context.Prefs.FindProfile(before), "old name gone");
                    AssertEqual("Local Lab", login.Server.Selected?.Value, "picker shows the new name");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "default_profile_follows_local_admiral", "The auto-created default profile follows the local Admiral's port; a hand-edited one keeps its URL", () =>
            {
                string Prefs(string url, string follows) => "{\"SchemaVersion\":1,\"Profiles\":[{\"Name\":\"default\",\"Url\":\"" + url + "\"" + follows + "}],\"ActiveProfile\":\"default\"}";
                using (TuiTestHost host = new TuiTestHost(120, 40, TuiFixtures.SignedInServer(1), "http://127.0.0.1:9", o =>
                {
                    o.ServerUrl = null;
                    o.DefaultServerUrl = "http://127.0.0.1:9";
                    File.WriteAllText(o.PreferencesPath!, Prefs("http://127.0.0.1:21000", ""));
                }))
                {
                    AssertEqual("http://127.0.0.1:9", host.Tui.Context.Session.Profile.Url, "stale default profile moved to the local port");
                    AssertEqual(true, host.Tui.Context.Session.Profile.FollowsLocalAdmiral, "marked as following");
                }

                using (TuiTestHost host = new TuiTestHost(120, 40, TuiFixtures.SignedInServer(1), "http://127.0.0.1:9", o =>
                {
                    o.ServerUrl = null;
                    o.DefaultServerUrl = "http://127.0.0.1:9";
                    File.WriteAllText(o.PreferencesPath!, Prefs("http://127.0.0.1:8123", ",\"FollowsLocalAdmiral\":false"));
                }))
                {
                    AssertEqual("http://127.0.0.1:8123", host.Tui.Context.Session.Profile.Url, "hand-set URL kept");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "ascii_logo", "The login screen shows the ASCII logo when the terminal is tall enough, else a one-line title", () =>
            {
                using (TuiTestHost host = new TuiTestHost(120, 40, TuiFixtures.SignedInServer(1), "http://127.0.0.1:9"))
                {
                    host.Start();
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "__ _ _ _ _ __  __ _ __| |__ _", "logo line 2");
                    TuiCase.Contains(frame, "\\__,_|_| |_|_|_\\__,_\\__,_\\__,_|", "logo line 4");
                    TuiCase.NotContains(frame, "A R M A D A", "no one-line title when the logo fits");
                    TuiCase.Contains(frame, "Continue", "card still fits under the logo");
                }

                using (TuiTestHost host = new TuiTestHost(80, 24, TuiFixtures.SignedInServer(1), "http://127.0.0.1:9"))
                {
                    host.Start();
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "A R M A D A", "one-line title on a short terminal");
                    TuiCase.NotContains(frame, "__ _ _ _ _ __", "no logo on a short terminal");
                    TuiCase.Contains(frame, "Continue", "card fits at 80x24");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "localhost_prefill", "A localhost server prefills the seeded admin, the default password, and the local API key; a remote one prefills nothing", () =>
            {
                string settingsPath = Path.Combine(Armada.Core.Constants.DefaultDataDirectory, "settings.json");
                Directory.CreateDirectory(Armada.Core.Constants.DefaultDataDirectory);
                File.WriteAllText(settingsPath, "{\"admiralPort\":9,\"apiKey\":\"local-key-123\"}");
                try
                {
                    LocalAdmiralDefaults local = LocalAdmiralDefaults.Load();
                    AssertEqual("http://127.0.0.1:9", local.Url, "url from the settings port");
                    AssertTrue(LocalAdmiralDefaults.IsLoopback("http://localhost:7890"), "localhost");
                    AssertTrue(LocalAdmiralDefaults.IsLoopback("http://[::1]:7890"), "ipv6 loopback");
                    AssertFalse(LocalAdmiralDefaults.IsLoopback("https://armada.example.com"), "remote");
                    AssertEqual(7890, LocalAdmiralDefaults.Load(Path.Combine(Armada.Core.Constants.DefaultDataDirectory, "missing.json")).Port, "missing file falls back");

                    using (TuiTestHost host = new TuiTestHost(120, 40, TuiFixtures.SignedInServer(1), "http://127.0.0.1:9"))
                    {
                        host.Start();
                        LoginView login = host.Tui.Shell.Login;
                        AssertEqual("admin@armada", login.Email.Value, "email");
                        AssertEqual("password", login.Password.Value, "password");
                        AssertEqual("local-key-123", login.ApiKey.Value, "api key");
                        TuiCase.NotContains(host.Screen(), "local-key-123", "api key masked");
                    }

                    using (TuiTestHost host = new TuiTestHost(120, 40, TuiFixtures.SignedInServer(1), "https://armada.example.com"))
                    {
                        host.Start();
                        LoginView login = host.Tui.Shell.Login;
                        AssertEqual("", login.Email.Value, "no email for a remote server");
                        AssertEqual("", login.Password.Value, "no password for a remote server");
                        AssertEqual("", login.ApiKey.Value, "no api key for a remote server");
                    }
                }
                finally
                {
                    File.Delete(settingsPath);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "server_picker_wide", "The Server picker is 50% wider than other pickers", () =>
            {
                using (TuiTestHost host = new TuiTestHost(200, 40, TuiFixtures.SignedInServer(1), "http://127.0.0.1:9"))
                {
                    host.Start();
                    AssertEqual(1.5, host.Tui.Shell.Login.Server.PickerWidthScale, "scale");
                    PickerModal<string>? picker = host.Tui.Shell.Login.Server.Open();
                    AssertNotNull(picker, "opened");
                    AssertEqual(60, picker!.MinContentWidth, "min width");
                    AssertEqual(135, picker.MaxContentWidth, "max width");
                    PickerModal<string> plain = new PickerModal<string>("Plain", new List<SelectOption<string>>());
                    AssertEqual(40, plain.MinContentWidth, "default min width");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "default_password_not_forced", "Signing in with the default password continues straight to the app with a warning, no forced change", () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer(1);
                stub.On("GET", "/api/v1/whoami", body => StubHttpHandler.Response(HttpStatusCode.OK, WhoAmIOpen() + ",\"PasswordChangeRequired\":true,\"DefaultCredentialsInUse\":true}"));
                stub.On("POST", "/api/v1/authenticate", body => StubHttpHandler.Response(HttpStatusCode.OK, "{\"Success\":true,\"Token\":\"tok_session\",\"PasswordChangeRequired\":true}"));
                using (TuiTestHost host = new TuiTestHost(120, 40, stub, "http://127.0.0.1:9", o => o.StartRoute = "/inbox"))
                {
                    host.Start();
                    host.Type("admin@armada").Press("enter");
                    host.PumpUntil(() => host.Tui.Shell.Login.Step == LoginStepEnum.Password);
                    host.Type("password").Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Session.IsSignedIn && host.Tui.Shell.Screen != null), "signed in without a change");
                    AssertEqual("/inbox", host.Tui.Context.Router.Current!.Path, "continued to the start route");
                    AssertTrue(host.WaitForText("Default credentials are in use."), "header warning");
                    AssertEqual(0, stub.Count("PUT /api/v1/account/password"), "no password change requested");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "default_credentials_warning", "whoami's default-credentials flag shows a persistent header warning", () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer(1);
                stub.On("GET", "/api/v1/whoami", body => StubHttpHandler.Response(HttpStatusCode.OK, WhoAmIOpen() + ",\"DefaultCredentialsInUse\":true}"));
                using (TuiTestHost host = TuiCase.SignedIn(140, 40, "/missions", stub))
                {
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "! Default credentials are in use.", "warning");
                    AssertEqual(2, host.Tui.Shell.Header.Rows, "extra header row");
                    host.Tui.Context.Navigate("/inbox");
                    TuiCase.Contains(host.Screen(), "Default credentials are in use.", "persistent across screens");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI login and session", cases: cases);
        }

        private static string Name(LoginView login, object? focused)
        {
            if (ReferenceEquals(focused, login.Email)) return "email";
            if (ReferenceEquals(focused, login.Password)) return "password";
            if (ReferenceEquals(focused, login.ApiKey)) return "apikey";
            if (ReferenceEquals(focused, login.Tenant)) return "tenant";
            if (ReferenceEquals(focused, login.Server)) return "server";
            if (ReferenceEquals(focused, login.Modes)) return "modes";
            if (ReferenceEquals(focused, login.Language)) return "language";
            if (ReferenceEquals(focused, login.ThemePicker)) return "theme";
            return focused == null ? "none" : "buttons";
        }
    }
}
