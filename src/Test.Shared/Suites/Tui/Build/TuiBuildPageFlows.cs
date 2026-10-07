namespace Test.Shared.Suites.Tui.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Build;
    using Test.Shared.Infrastructure;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Keyboard flows for the BUILD screens that are not plain lists (Tui.KeyboardFlows.Build): the import wizard and
    /// the vessel page with its onboarding page. Each runs the table-driven flow's steps by hand (open, filter, select,
    /// row menu, form, open the row, back) and checks the requests with the structured stub helpers.
    /// </summary>
    internal static class TuiBuildPageFlows
    {
        #region Public-Methods

        /// <summary>
        /// Vessels, I opens the wizard; paste a path and discover; review: filter with /, select all new, open the
        /// options panel, import; Enter on the created row opens the vessel; Alt+Left goes back to the wizard and Esc to
        /// Vessels.
        /// </summary>
        public static void ImportWizard()
        {
            StubHttpHandler stub = TuiBuildImportSuite.Stub(() => 0);
            stub.Json("GET", "/api/v1/vessels/vsl_new", BuildStubs.Vessel("vsl_new", "api-service", "flt_web", "LocalMerge"));
            using (TuiTestHost host = TuiCase.SignedIn(170, 50, "/vessels", stub))
            {
                // Open from Vessels.
                TuiEntityFixtures.WaitFor(host, () => host.Screen().Contains("DemoRepo", StringComparison.Ordinal), "import: vessels listed");
                host.Press("I");
                TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.Path == "/vessels/import", "import: I opens the wizard");
                TuiEntityFixtures.WaitFor(host, () => host.Screen().Contains("Folders on the Admiral host, one per line", StringComparison.Ordinal), "import: source step");
                ImportWizard wizard = (ImportWizard)host.Tui.Shell.Screen!;
                wizard.PollMilliseconds = 50;

                // Paths and discover.
                host.Type("/repos");
                host.Press("ctrl+s");
                TuiEntityFixtures.WaitFor(host, () => host.Screen().Contains("[2 Review]", StringComparison.Ordinal), "import: review step", 8000);

                // Filter: / focuses the search; only the matching candidate stays; Enter returns to the grid.
                host.Press("/");
                host.Type("api");
                TuiEntityFixtures.WaitFor(host, () => !host.Screen().Contains("/repos/demo", StringComparison.Ordinal) && host.Screen().Contains("api-service", StringComparison.Ordinal), "import: search filters the candidates");
                host.Press("enter");
                AssertTrue(ReferenceEquals(wizard.Scope.Focused, wizard.CandidateGrid), "import: Enter in the search returns to the grid");

                // Select: clear, then a selects every new candidate.
                host.Press("C");
                AssertEqual(0, wizard.CandidateGrid.Marked.Count, "import: C clears the selection");
                host.Press("a");
                AssertEqual(1, wizard.CandidateGrid.Marked.Count, "import: a selects all new");

                // The options panel stands in for the create form; [ returns to the candidates.
                host.Press("]");
                TuiEntityFixtures.WaitFor(host, () => host.Screen().Contains("Defaults for the new vessels", StringComparison.Ordinal), "import: ] opens the options");
                host.Press("[");

                // Import and open the created vessel.
                host.Press("ctrl+s");
                TuiEntityFixtures.WaitFor(host, () => host.Screen().Contains("[3 Results]", StringComparison.Ordinal), "import: results step", 8000);
                TuiEntityFixtures.WaitFor(host, () => wizard.ResultGrid.Rows.Any(i => i.VesselId == "vsl_new"), "import: created vessel in the results", 8000);
                wizard.Scope.Focus(wizard.ResultGrid);
                host.Press("home").Press("enter");
                TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.Path == "/vessels/vsl_new", "import: Enter opens the created vessel (now " + host.Tui.Context.Router.Current!.FullPath + ")");

                // Back.
                host.Press("alt+left");
                TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.Path == "/vessels/import", "import: Alt+Left returns to the wizard");
                // Esc first leaves the paths field for the source mode, then closes the wizard.
                ImportWizard again = (ImportWizard)host.Tui.Shell.Screen!;
                AssertTrue(again.PasteArea.IsFocused, "import: the wizard opens in the paths field");
                host.Press("esc");
                AssertTrue(again.SourceMode.IsFocused, "import: Esc leaves the paths field for the source mode");
                host.Press("esc");
                TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.FullPath == "/vessels", "import: Esc closes the wizard to Vessels (now " + host.Tui.Context.Router.Current!.FullPath + ")");
            }

            VesselDiscoveryRequest discover = stub.LastBody<VesselDiscoveryRequest>("POST", "/api/v1/vessels/import/discover");
            AssertEqual("/repos", String.Join("|", discover.Directories), "import: discovered directories");
            AssertEqual(1, stub.CountFor("POST", "/api/v1/vessels/import"), "import: one import call");
            VesselImportRequest import = stub.LastBody<VesselImportRequest>("POST", "/api/v1/vessels/import");
            AssertEqual("vib_1", import.BatchId, "import: batch");
            AssertEqual("/repos/api-service", String.Join("|", import.Paths), "import: only the selected new candidate");
            AssertNull(import.Categorization, "import: no categorization unless asked");
        }

        /// <summary>
        /// d on a Vessels row and d on the vessel page both open Dispatch with that vessel pre-selected and no
        /// pre-fill banner.
        /// </summary>
        public static void VesselDispatch()
        {
            StubHttpHandler stub = TuiBuildVesselsSuite.Stub();
            stub.Json("GET", "/api/v1/vessels/vsl_demo", BuildStubs.Vessel("vsl_demo", "DemoRepo", "flt_web", "LocalMerge"));
            using (TuiTestHost host = TuiCase.SignedIn(180, 60, "/vessels", stub))
            {
                TuiEntityFixtures.WaitFor(host, () => host.Screen().Contains("DemoRepo", StringComparison.Ordinal), "dispatch: vessels listed");
                VesselsScreen list = (VesselsScreen)((Armada.Tui.Screens.HubScreen)host.Tui.Shell.Screen!).Content;
                host.Press("home");
                for (int i = 0; i < 5 && list.Grid.Current?.Id != "vsl_demo"; i++) host.Press("down");
                AssertEqual("vsl_demo", list.Grid.Current?.Id, "dispatch: row selected");

                host.Press("d");
                AssertDispatchFor(host, "vsl_demo", "dispatch: d on the row");

                host.Press("alt+left");
                TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.FullPath == "/vessels", "dispatch: Alt+Left returns to Vessels (now " + host.Tui.Context.Router.Current!.FullPath + ")");
                VesselsScreen back = (VesselsScreen)((Armada.Tui.Screens.HubScreen)host.Tui.Shell.Screen!).Content;
                TuiEntityFixtures.WaitFor(host, () => back.Grid.Rows.Any(r => r.Id == "vsl_demo"), "dispatch: vessels listed again");
                host.Press("home");
                for (int i = 0; i < 5 && back.Grid.Current?.Id != "vsl_demo"; i++) host.Press("down");
                host.Press("enter");
                TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.FullPath == "/vessels/vsl_demo", "dispatch: Enter opens the vessel page (now " + host.Tui.Context.Router.Current!.FullPath + ")");
                TuiEntityFixtures.WaitFor(host, () => host.Screen().Contains("Needs Attention", StringComparison.Ordinal), "dispatch: vessel page loaded");

                host.Press("d");
                AssertDispatchFor(host, "vsl_demo", "dispatch: d on the vessel page");
            }
        }

        /// <summary>
        /// Vessels, Enter opens the vessel page; ] moves through the tabs to the missions; the action menu and the edit
        /// form open and close; g opens onboarding, n follows the next step; Alt+Left walks back to Vessels.
        /// </summary>
        public static void VesselDetailAndOnboarding()
        {
            StubHttpHandler stub = TuiBuildVesselsSuite.Stub();
            stub.Json("GET", "/api/v1/vessels/vsl_demo", BuildStubs.Vessel("vsl_demo", "DemoRepo", "flt_web", "LocalMerge"));
            using (TuiTestHost host = TuiCase.SignedIn(180, 60, "/vessels", stub))
            {
                // Open the row.
                TuiEntityFixtures.WaitFor(host, () => host.Screen().Contains("DemoRepo", StringComparison.Ordinal), "vessel: listed");
                VesselsScreen list = (VesselsScreen)((Armada.Tui.Screens.HubScreen)host.Tui.Shell.Screen!).Content;
                host.Press("home");
                for (int i = 0; i < 5 && list.Grid.Current?.Id != "vsl_demo"; i++) host.Press("down");
                AssertEqual("vsl_demo", list.Grid.Current?.Id, "vessel: row selected");
                host.Press("enter");
                TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.FullPath == "/vessels/vsl_demo", "vessel: Enter opens the page");
                TuiEntityFixtures.WaitFor(host, () => host.Screen().Contains("Needs Attention", StringComparison.Ordinal), "vessel: readiness shown");

                // Tabs to the missions.
                host.Press("]").Press("]");
                TuiEntityFixtures.WaitFor(host, () => host.Screen().Contains("Fix parser task", StringComparison.Ordinal), "vessel: ] reaches the missions");

                // Action menu.
                host.Press(".");
                TuiEntityFixtures.WaitFor(host, () => host.App.Modals.IsActive, "vessel: . opens the action menu");
                host.Press("esc");
                TuiEntityFixtures.WaitFor(host, () => !host.App.Modals.IsActive, "vessel: action menu closed");

                // Edit form.
                host.Press("e");
                TuiEntityFixtures.WaitFor(host, () => host.Screen().Contains("Edit Vessel", StringComparison.Ordinal), "vessel: e opens the edit form");
                host.Press("esc");
                if (host.App.Modals.IsActive) host.Press("y");
                TuiEntityFixtures.WaitFor(host, () => !host.App.Modals.IsActive, "vessel: edit form dismissed");

                // Onboarding and its next step.
                host.Press("g");
                TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.FullPath == "/vessels/vsl_demo/onboarding", "vessel: g opens onboarding");
                TuiEntityFixtures.WaitFor(host, () => host.Screen().Contains("Next Recommended Step", StringComparison.Ordinal), "onboarding: next step shown");
                host.Press("n");
                TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.Path == "/workflow-profiles/new", "onboarding: n follows the next step");

                // Back to the list.
                host.Press("esc");
                if (host.App.Modals.IsActive) host.Press("y");
                foreach (string expected in new[] { "/vessels/vsl_demo/onboarding", "/vessels/vsl_demo", "/vessels" })
                {
                    if (host.Tui.Context.Router.Current!.FullPath != expected) host.Press("alt+left");
                    string target = expected;
                    TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.FullPath == target, "back to " + target + " (now " + host.Tui.Context.Router.Current!.FullPath + ")");
                }

                TuiEntityFixtures.WaitFor(host, () => host.Screen().Contains("DemoRepo", StringComparison.Ordinal), "vessel: list shows the row again");
            }

            AssertEqual(0, stub.CountFor("PUT", "/api/v1/vessels/vsl_demo"), "vessel: dismissing the edit form saves nothing");
            AssertTrue(stub.CountFor("GET", "/api/v1/vessels/vsl_demo/readiness") >= 1, "vessel: readiness loaded");
            AssertTrue(stub.CountFor("GET", "/api/v1/vessels/vsl_demo/landing-preview") >= 1, "vessel: landing preview loaded");
            List<StubRequest> missions = stub.RequestsFor("GET", "/api/v1/missions/summaries");
            AssertTrue(missions.Any(r => r.QueryValue("vesselId") == "vsl_demo"), "vessel: missions scoped to the vessel: " + String.Join(", ", missions.Select(r => r.Query)));
        }

        #endregion

        #region Private-Methods

        private static void AssertDispatchFor(TuiTestHost host, string vesselId, string label)
        {
            TuiEntityFixtures.WaitFor(host, () => host.Tui.Context.Router.Current!.Path == "/dispatch", label + " opens Dispatch (now " + host.Tui.Context.Router.Current!.FullPath + ")");
            Armada.Tui.Screens.Operations.DispatchScreen dispatch = (Armada.Tui.Screens.Operations.DispatchScreen)((Armada.Tui.Screens.HubScreen)host.Tui.Shell.Screen!).Content;
            TuiEntityFixtures.WaitFor(host, () => dispatch.Vessel.Value == vesselId, label + " pre-selects the vessel (now " + (dispatch.Vessel.Value ?? "none") + ")");
            AssertFalse(host.Screen().Contains("Prefilled from", StringComparison.Ordinal), label + " shows no pre-fill banner\n" + host.Screen());
        }

        #endregion
    }
}
