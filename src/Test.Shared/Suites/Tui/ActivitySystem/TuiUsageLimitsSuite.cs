namespace Test.Shared.Suites.Tui.ActivitySystem
{
    using System;
    using System.Collections.Generic;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Activity;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Terminal account limits and capture cooldown flows.</summary>
    public class TuiUsageLimitsSuite : IArmadaTestSuite
    {
        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            const string suite = "Tui.Usage";
            return new TestSuiteDescriptor(suite, "Terminal usage limits", new List<TestCaseDescriptor>
            {
                TuiCase.Sync(suite, "view_and_cooldown", "Limits render scoped accounts and thresholds; capture cooldown expires while mounted", () =>
                {
                    StubHttpHandler stub = TuiFixtures.SignedInServer();
                    stub.Json("GET", "/api/v1/usage/latest", "{\"Objects\":[{\"HostId\":\"quota-host\",\"Provider\":\"anthropic\",\"Status\":\"Ok\",\"CapturedUtc\":\"2026-11-01T00:00:00Z\",\"CaptainIds\":[\"cpt_usage\"],\"Windows\":[{\"name\":\"five_hour\",\"usedPercent\":95}]}],\"TotalPages\":1}");
                    stub.On("POST", "/api/v1/captains/cpt_usage/usage/capture", body => StubHttpHandler.Response(System.Net.HttpStatusCode.TooManyRequests,
                        "{\"Message\":\"Cooldown\",\"Data\":{\"NextEligibleUtc\":\"" + DateTime.UtcNow.AddMilliseconds(250).ToString("O") + "\"}}"));
                    using TuiTestHost host = TuiCase.SignedIn(140, 44, "/activity?source=limits&captainId=cpt_usage", stub);
                    AssertTrue(host.WaitForText("quota-host"));
                    AssertTrue(host.WaitForText("Near limit"));
                    ScreenBase? current = host.Tui.Shell.Screen;
                    if (current is HubScreen hub) current = hub.Content;
                    AssertTrue(current is UsageLimitsScreen);
                    UsageLimitsScreen screen = (UsageLimitsScreen)current!;
                    AssertTrue(screen.RunAction("capture"));
                    AssertTrue(host.PumpUntil(() => stub.Saw("POST", "/api/v1/captains/cpt_usage/usage/capture")));
                    AssertTrue(host.WaitForText("Capture cooldown"));
                    AssertFalse(screen.RunAction("capture"), "cooldown prevents a second request");
                    AssertTrue(host.PumpUntil(() => screen.Actions.Find(a => a.Id == "capture")!.CanExecute, 3000), "same mounted view becomes eligible");
                    AssertTrue(stub.Saw("GET", "/api/v1/usage/latest", request => request.QueryValue("captainId") == "cpt_usage"));
                })
            });
        }
    }
}
