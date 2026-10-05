namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Operations;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Dispatch (W3.5) against a stubbed client: pre-fill from a handoff with its banner, pipeline step captain
    /// assignments seeded from persona defaults, validation, the createVoyage body, the toast, and the delayed
    /// navigation to the voyage.
    /// </summary>
    public sealed class TuiOpsDispatchSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Ops.Dispatch";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "prefill_and_dispatch", "A planning handoff pre-fills the form; Ctrl+S creates the voyage with the pipeline and step captains", () =>
            {
                StubHttpHandler stub = TuiOpsPlanningSuite.Stub();
                stub.Json("POST", "/api/v1/voyages", "{\"Id\":\"vyg_9\",\"Title\":\"My voyage\"}");
                string playbooks = Uri.EscapeDataString("[{\"PlaybookId\":\"pbk_1\",\"DeliveryMode\":\"AttachIntoWorktree\"}]");
                using (TuiTestHost host = TuiCase.SignedIn(150, 55, "/dispatch?from=planning&vesselId=vsl_demo&prompt=Fix%20it&voyageTitle=My%20voyage&pipelineName=Reviewed&playbooks=" + playbooks, stub))
                {
                    AssertTrue(host.WaitForText("Prefilled from a planning session. Review the draft below and dispatch when ready."), "banner\n" + host.Screen());
                    DispatchScreen screen = (DispatchScreen)((HubScreen)host.Tui.Shell.Screen!).Content;
                    AssertTrue(host.PumpUntil(() => screen.Vessel.Value == "vsl_demo" && screen.Pipeline.Value == "Reviewed"), "vessel and pipeline pre-filled");
                    AssertTrue(host.PumpUntil(() => screen.StepCaptains.ContainsKey("Judge")), "pipeline steps");
                    AssertTrue(host.WaitForText("Worker: Preferred Captain"), "step rows\n" + host.Screen());
                    AssertEqual("cpt_1", screen.StepCaptains["Worker"].Value, "persona default captain seeded");
                    TuiCase.Contains(host.Screen(), "style.md (Attach Into Worktree)", "playbooks pre-filled");
                    TuiCase.Contains(host.Screen(), "Vessel Readiness", "readiness");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/voyages") == 1), "create voyage");
                    string body = stub.Bodies.Last(b => b.Contains("Fix it"));
                    AssertTrue(body.Contains("\"Title\":\"My voyage\"") && body.Contains("\"Pipeline\":\"Reviewed\"") && body.Contains("\"Priority\":100") && body.Contains("\"Persona\":\"Worker\"") && body.Contains("pbk_1"), "voyage body: " + body);
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Dispatched voyage with 2 pipeline stages"))), "toast");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/voyages/vyg_9", 4000), "navigates to the voyage");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "validation_and_failure", "Dispatch needs a description and a vessel; a failure shows the dashboard's message", () =>
            {
                StubHttpHandler stub = TuiOpsPlanningSuite.Stub();
                stub.On("POST", "/api/v1/voyages", b => StubHttpHandler.Response(System.Net.HttpStatusCode.BadRequest, "{\"Error\":\"BadRequest\",\"Message\":\"Vessel is busy\"}"));
                using (TuiTestHost host = TuiCase.SignedIn(150, 55, "/dispatch", stub))
                {
                    AssertTrue(host.WaitForText("Describe the work you want Armada to dispatch"), "subtitle\n" + host.Screen());
                    DispatchScreen screen = (DispatchScreen)((HubScreen)host.Tui.Shell.Screen!).Content;
                    TuiCase.Contains(host.Screen(), "All steps: Preferred Captain", "inherited pipeline has one row");
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Describe what you need done."), "description required");
                    screen.Description.Text = "Add tests";
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Please select a vessel."), "vessel required\n" + host.Screen());
                    AssertTrue(host.PumpUntil(() => screen.Vessel.Options.Count > 0), "vessels loaded");
                    screen.Vessel.Choose(screen.Vessel.Options.First(o => o.Value == "vsl_demo"));
                    screen.StepTiers["*"].Choose(screen.StepTiers["*"].Options.First(o => o.Value == "Premium"));
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/voyages") == 1), "create voyage");
                    AssertTrue(stub.Bodies.Any(b => b.Contains("\"Persona\":\"*\"") && b.Contains("Premium")), "wildcard assignment with fallback tier");
                    AssertTrue(host.WaitForText("Failed: Vessel is busy"), "failure message\n" + host.Screen());
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI dispatch", cases: cases);
        }
    }
}
