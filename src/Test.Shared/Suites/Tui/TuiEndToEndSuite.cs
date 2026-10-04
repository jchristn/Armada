namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end: the TUI against a live in-process server (<see cref="E2EServerFixture"/>), signing in through the
    /// login screen's API key path, then the live header (health, WebSocket), the i18n catalog, and navigation.
    /// </summary>
    public sealed class TuiEndToEndSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.EndToEnd";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Async(Suite, "api_key_login_live", "Sign in with an API key on the login screen against a live server", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                using (TuiTestHost host = new TuiTestHost(120, 40, null, fx.BaseUrl, o => { o.Live = true; o.StartRoute = "/missions"; }))
                {
                    host.Start();
                    AssertFalse(host.Tui.Context.Session.IsSignedIn, "starts signed out");
                    AssertTrue(host.Tui.Context.Loc.Catalog != null, "catalog loaded from the server");
                    host.Press("f2").Paste(fx.ApiKey).Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Session.IsSignedIn && host.Tui.Shell.Screen != null, 15000), "signed in");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Events.IsLive, 15000), "websocket live");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Status.HealthChecked, 15000), "health polled");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "* Live", "live indicator");
                    TuiCase.Contains(frame, "* Healthy", "health indicator");
                    TuiCase.Contains(frame, "Merge Queue", "missions hub");
                    host.Press("g").Press("i");
                    AssertEqual("/inbox", host.Tui.Context.Router.Current!.Path, "navigated");
                    host.Tui.Context.Events.Stop();
                    host.Tui.Context.Status.Stop();
                }
            }, TestTags.EndToEnd));

            cases.Add(TuiCase.Async(Suite, "password_login_live", "Sign in with email and password against a live server", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                using (TuiTestHost host = new TuiTestHost(120, 40, null, fx.BaseUrl, o => o.StartRoute = "/jobs"))
                {
                    host.Start();
                    host.Type("admin@armada").Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Shell.Login.Step != Armada.Tui.Screens.LoginStepEnum.Email, 10000), "lookup finished");
                    if (host.Tui.Shell.Login.Step == Armada.Tui.Screens.LoginStepEnum.Tenant)
                    {
                        host.Tui.Shell.Login.Tenant.SetValue("default");
                        host.Tui.Shell.Login.Tenant.Choose(host.Tui.Shell.Login.Tenant.Selected);
                        host.Tui.Shell.Login.SubmitTenant();
                    }

                    host.Type("password").Press("enter");

                    // The seeded admin still has the default password, so the server allows only the password change.
                    // The fixture is shared, so this case checks the forced step and its validation without changing
                    // the password (Tui.Login covers the full change against a stub, and the real run covers it live).
                    AssertTrue(host.PumpUntil(() => host.Tui.Shell.Login.Step == Armada.Tui.Screens.LoginStepEnum.ChangePassword, 10000), "change-password step");
                    AssertFalse(host.Tui.Context.Session.IsSignedIn, "not signed in until the password changes");
                    AssertEqual("admin@armada", host.Tui.Context.Session.PendingIdentity!.User!.Email, "pending identity");
                    TuiCase.Contains(host.Screen(), "Change the default password", "change step shown");
                    host.Tui.Shell.Login.CurrentPassword.Value = "password";
                    host.Tui.Shell.Login.NewPassword.Value = "a-new-password";
                    host.Tui.Shell.Login.ConfirmPassword.Value = "a-different-one";
                    host.Tui.Shell.Login.SubmitPasswordChange();
                    AssertTrue(host.WaitForText("The new passwords do not match."), "validation");
                    await host.Tui.Context.Session.SignOutAsync();
                    AssertTrue(host.PumpUntil(() => host.Tui.Shell.Login.Step == Armada.Tui.Screens.LoginStepEnum.Email, 5000), "sign out returns to email");
                }
            }, TestTags.EndToEnd));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI end to end (live server)", cases: cases);
        }
    }
}
