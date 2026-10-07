namespace Test.Shared.Suites.Tui.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// One keyboard flow per BUILD screen (W8.2), in the Tui.KeyboardFlows table-driven style: open, filter, select,
    /// row menu, create form or page, open the row, back. The list screens (Vessels, Vessel Health, Fleets, Workspace,
    /// Captains, Docks) run through <see cref="TuiFlowRunner"/>; the import wizard and the vessel page with onboarding
    /// are not lists, so they run the same steps by hand (<see cref="TuiBuildPageFlows"/>). Requests are checked with
    /// the structured stub helpers, never by matching request text.
    /// </summary>
    public sealed class TuiKeyboardFlowBuildSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Tui.KeyboardFlows.Build";
        private const string Stamp = "\"CreatedUtc\":\"2026-10-01T00:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T00:00:00Z\"";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            foreach (TuiFlowSpec spec in Specs())
            {
                TuiFlowSpec captured = spec;
                cases.Add(TuiCase.Sync(Suite, captured.Id, "Keyboard flow: " + captured.Route + " open, filter, select, row menu, form, open, back", () => TuiFlowRunner.Run(captured)));
            }

            cases.Add(TuiCase.Sync(Suite, "import_wizard", "Keyboard flow: /vessels/import from Vessels (I), paths, discover, review filter and selection, import, open a result, back", TuiBuildPageFlows.ImportWizard));
            cases.Add(TuiCase.Sync(Suite, "vessel_dispatch", "Keyboard flow: d on a Vessels row and on the vessel page opens Dispatch with the vessel pre-selected", TuiBuildPageFlows.VesselDispatch));
            cases.Add(TuiCase.Sync(Suite, "vessel_history", "Keyboard flow: /vessels/:id/history from the vessel page (H), a heatmap day (arrows, Enter), the list (Tab), a commit (Enter), the date prompt (t), back", TuiBuildPageFlows.VesselHistory));
            cases.Add(TuiCase.Sync(Suite, "vessel_detail_onboarding", "Keyboard flow: /vessels/:id tabs, edit form, missions row, onboarding (g), next step, back", TuiBuildPageFlows.VesselDetailAndOnboarding));
            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI keyboard flows per screen (Build)", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static List<TuiFlowSpec> Specs()
        {
            List<TuiFlowSpec> specs = new List<TuiFlowSpec>();

            string vessel = BuildStubs.Vessel("vsl_alpha", "Alpharepo", "flt_web", "LocalMerge");
            TuiFlowSpec vessels = TuiFlowSpec.Create("vessels", "/vessels", "/api/v1/vessels", "vsl_alpha", "Alpharepo", "/vessels/vsl_alpha", vessel);
            vessels.ServerFactory = BuildStubs.Server;
            vessels.DetailPath = null;
            vessels.ExtraRoutes.Add(new[] { "GET", "/api/v1/missions/summaries", "{\"Success\":true,\"Objects\":[],\"TotalRecords\":0}" });
            vessels.VerifyRequests = stub =>
            {
                AssertTrue(stub.CountFor("GET", "/api/v1/vessels") >= 2, "the list and then the vessel page (like the dashboard) load the vessels");
                AssertTrue(stub.RequestsFor("GET", "/api/v1/missions/summaries").Any(r => r.QueryValue("vesselId") == "vsl_alpha"), "the vessel page loads the vessel's missions: " + String.Join("\n", stub.Requests));
                AssertEqual(0, stub.CountFor("POST", "/api/v1/vessels"), "dismissing the create form creates nothing");
            };
            specs.Add(vessels);

            string health = "{\"Id\":\"vh_1\",\"VesselId\":\"vsl_alpha\",\"VesselName\":\"Alpharepo\",\"FleetName\":\"Web\",\"CurrentBranch\":\"main\",\"OverallStatus\":\"Warn\",\"DependencyStatus\":\"Pass\",\"TestInfraStatus\":\"Pass\",\"EvaluatedUtc\":\"2026-10-04T09:00:00Z\"}";
            TuiFlowSpec healthSpec = TuiFlowSpec.Create("vessel_health", "/vessels/health", "/api/v1/vessel-health/enumerate", "vsl_alpha", "Alpharepo", null, health);
            healthSpec.ServerFactory = BuildStubs.Server;
            healthSpec.ListMethod = "POST";
            healthSpec.NewKey = null;
            healthSpec.Width = 190;
            healthSpec.Height = 50;
            healthSpec.ExtraRoutes.Add(new[] { "GET", "/api/v1/vessel-health/summary", "{\"TotalVessels\":1,\"Pass\":0,\"Warn\":1,\"Fail\":0,\"Unknown\":0,\"NotApplicable\":0,\"NotEvaluated\":0}" });
            healthSpec.ExtraRoutes.Add(new[] { "GET", "/api/v1/jobs", "{\"Success\":true,\"Objects\":[],\"TotalRecords\":0}" });
            healthSpec.ExtraRoutes.Add(new[] { "GET", "/api/v1/vessels/vsl_alpha/health", "{\"Health\":" + health + ",\"Findings\":[],\"Dependencies\":[],\"Overrides\":[]}" });
            healthSpec.ServerFilterSeen = stub => stub.BodiesFor<Armada.Core.Models.VesselHealthEnumerateRequest>("POST", "/api/v1/vessel-health/enumerate").Any(b => b.NameContains == "Alp");
            healthSpec.VerifyRequests = stub =>
            {
                List<Armada.Core.Models.VesselHealthEnumerateRequest> bodies = stub.BodiesFor<Armada.Core.Models.VesselHealthEnumerateRequest>("POST", "/api/v1/vessel-health/enumerate");
                AssertTrue(bodies.Count >= 2, "enumerated on open and on the filter: " + bodies.Count);
                AssertNull(bodies[0].NameContains, "first load is unfiltered");
                AssertTrue(bodies.Any(b => b.NameContains == "Alp"), "the / filter is sent to the server as NameContains: " + String.Join(" | ", stub.RequestsFor("POST", "/api/v1/vessel-health/enumerate").Select(r => r.Body)));
                AssertTrue(stub.CountFor("GET", "/api/v1/vessels/vsl_alpha/health") >= 1, "Enter opens the health inspector for the row");
            };
            specs.Add(healthSpec);

            string fleet = "{\"Id\":\"flt_alpha\",\"Name\":\"Alphafleet\",\"Description\":\"Alpha repos\",\"Active\":true," + Stamp + "}";
            TuiFlowSpec fleets = TuiFlowSpec.Create("fleets", "/vessels?tab=fleets", "/api/v1/fleets", "flt_alpha", "Alphafleet", "/fleets/flt_alpha", fleet);
            fleets.ServerFactory = BuildStubs.Server;
            fleets.DetailJson = "{\"Fleet\":" + fleet + ",\"Vessels\":[]}";
            fleets.VerifyRequests = stub =>
            {
                AssertTrue(stub.CountFor("GET", "/api/v1/fleets") >= 2, "the fleet page loads the fleets (like the dashboard) after the list did");
                AssertEqual(0, stub.CountFor("POST", "/api/v1/fleets"), "dismissing the create form creates nothing");
            };
            specs.Add(fleets);

            TuiFlowSpec workspace = TuiFlowSpec.Create("workspace", "/vessels?tab=workspace", "/api/v1/vessels", "vsl_alpha", "Alpharepo", "/workspace/vsl_alpha", vessel);
            workspace.ServerFactory = BuildStubs.Server;
            workspace.NewKey = null;
            workspace.ExtraRoutes.Add(new[] { "GET", "/api/v1/workspace/vessels/vsl_alpha/status", "{\"VesselId\":\"vsl_alpha\",\"HasWorkingDirectory\":true,\"BranchName\":\"main\"}" });
            workspace.ExtraRoutes.Add(new[] { "GET", "/api/v1/workspace/vessels/vsl_alpha/tree", "{\"Path\":\"\",\"Entries\":[{\"Name\":\"README.md\",\"Path\":\"README.md\",\"IsDirectory\":false}]}" });
            // The workspace opens with focus in the file tree; wait for it to load so Alt+Left goes through the tree.
            workspace.DetailText = "README.md";
            workspace.VerifyRequests = stub =>
            {
                AssertTrue(stub.CountFor("GET", "/api/v1/vessels") >= 1, "the picker lists vessels");
                AssertTrue(stub.Log.Any(r => r.Method == "GET" && r.Path.StartsWith("/api/v1/workspace/vessels/vsl_alpha/", StringComparison.Ordinal)), "the workspace loads the chosen vessel: " + String.Join("\n", stub.Requests));
            };
            specs.Add(workspace);

            string captain = "{\"Id\":\"cpt_alpha\",\"Name\":\"alphacaptain\",\"Runtime\":\"ClaudeCode\",\"State\":\"Idle\"," + Stamp + "}";
            TuiFlowSpec captains = TuiFlowSpec.Create("captains", "/captains", "/api/v1/captains", "cpt_alpha", "alphacaptain", "/captains/cpt_alpha", captain);
            captains.ServerFactory = BuildStubs.Server;
            captains.VerifyRequests = stub =>
            {
                AssertTrue(stub.CountFor("GET", "/api/v1/captains/cpt_alpha") >= 1, "the captain page loads the captain");
                AssertEqual(0, stub.CountFor("POST", "/api/v1/captains"), "dismissing the create form creates nothing");
            };
            specs.Add(captains);

            string dock = "{\"Id\":\"dck_alpha\",\"VesselId\":\"vsl_demo\",\"CaptainId\":\"cpt_1\",\"BranchName\":\"armada/alphabranch\",\"WorktreePath\":\"/tmp/docks/alpha\",\"Active\":true," + Stamp + "}";
            TuiFlowSpec docks = TuiFlowSpec.Create("docks", "/captains?tab=docks", "/api/v1/docks", "dck_alpha", "armada/alphabranch", "/docks/dck_alpha", dock);
            docks.ServerFactory = BuildStubs.Server;
            docks.NewKey = null;
            docks.VerifyRequests = stub =>
            {
                StubRequest list = stub.RequestsFor("GET", "/api/v1/docks").First();
                AssertNotNull(list.QueryValue("pageSize"), "docks are paged on the server: " + list.Query);
                AssertTrue(stub.CountFor("GET", "/api/v1/docks/dck_alpha") >= 1, "the dock page loads the dock");
            };
            specs.Add(docks);

            return specs;
        }

        #endregion
    }
}
