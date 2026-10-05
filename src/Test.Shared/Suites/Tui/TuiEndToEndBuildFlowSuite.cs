namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Models;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Build;
    using Armada.Tui.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end BUILD flows (W8.3): the TUI against a live in-process server (<see cref="E2EServerFixture"/>) with the
    /// stub captain runtime installed, so no agent CLI ever runs. Imports two local git repositories through the import
    /// wizard (paths, discovery, review, options, import, results) and checks the vessels on the server; evaluates a
    /// vessel's health from Vessel Health and opens the inspector on the live result.
    /// </summary>
    public sealed class TuiEndToEndBuildFlowSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Tui.EndToEnd.Build";
        private const int LiveTimeoutMs = 60000;
        private readonly StubCaptainBehavior _Behavior = new StubCaptainBehavior();

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Async(Suite, "import_wizard_live", "Import: paste a folder, discover, review, pick the fleet, import, and the vessels exist on the server", async () =>
            {
                E2EServerFixture fx = await AcquireAsync();
                using (ArmadaClient admin = LiveServerSetup.Admin(fx))
                {
                    string suffix = Guid.NewGuid().ToString("N").Substring(0, 6);
                    Fleet fleet = (await admin.CreateFleetAsync(new Fleet { Name = "tui-import-fleet-" + suffix }))!;
                    string root = TestTemp.NewDirectory("tui-import");
                    List<string> names = new List<string> { "tui-imp-alpha-" + suffix, "tui-imp-beta-" + suffix };
                    foreach (string name in names) CreateRepository(Path.Combine(root, name));

                    using (TuiTestHost host = SignIn(fx, "/vessels/import"))
                    {
                        AssertTrue(host.WaitForText("Folders on the Admiral host, one per line", 15000), "source step\n" + host.Screen());
                        ImportWizard wizard = (ImportWizard)host.Tui.Shell.Screen!;
                        wizard.PollMilliseconds = 100;
                        host.Paste(root);
                        AssertTrue(host.WaitForText("1 path", 5000), "path parsed\n" + host.Screen());
                        host.Press("ctrl+s");
                        AssertTrue(host.PumpUntil(() => wizard.Step == "review" && wizard.Candidates.Count == 2, LiveTimeoutMs), "both repositories discovered (step " + wizard.Step + ")\n" + host.Screen());
                        AssertTrue(names.All(n => wizard.Candidates.Any(c => c.ProposedName == n)), "candidate names: " + String.Join(", ", wizard.Candidates.Select(c => c.ProposedName)));
                        AssertEqual(2, wizard.CandidateGrid.Marked.Count, "new repositories are selected by default");

                        host.Press("]");
                        AssertTrue(host.WaitForText("Defaults for the new vessels", 5000), "options\n" + host.Screen());
                        AssertTrue(host.PumpUntil(() => wizard.DefaultFleet.Options.Any(o => o.Value == fleet.Id), 15000), "live fleets offered");
                        wizard.DefaultFleet.Choose(wizard.DefaultFleet.Options.First(o => o.Value == fleet.Id));
                        host.Press("ctrl+s");

                        AssertTrue(host.PumpUntil(() => wizard.Step == "results", 15000), "results step\n" + host.Screen());
                        AssertTrue(host.PumpUntil(() => wizard.ResultGrid.Rows.Count(i => !String.IsNullOrEmpty(i.VesselId)) == 2, LiveTimeoutMs), "both vessels created\n" + host.Screen());
                        AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Severity == NotificationSeverityEnum.Success && t.Text.Contains("Import finished: 2 vessels created.", StringComparison.Ordinal)), 15000), "finished toast");
                        List<string> vesselIds = wizard.ResultGrid.Rows.Where(i => !String.IsNullOrEmpty(i.VesselId)).Select(i => i.VesselId!).ToList();

                        foreach (string id in vesselIds)
                        {
                            Vessel? vessel = await admin.GetVesselAsync(id);
                            AssertNotNull(vessel, "vessel " + id + " on the server");
                            AssertTrue(names.Contains(vessel!.Name), "vessel name " + vessel.Name);
                            AssertEqual(fleet.Id, vessel.FleetId, "vessel " + vessel.Name + " in the chosen fleet");
                            AssertEqual(vessel.Name, Path.GetFileName((vessel.WorkingDirectory ?? "").TrimEnd('/', '\\')), "working directory is the discovered folder");
                        }

                        host.Press("v");
                        AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.FullPath == "/vessels", 5000), "v opens Vessels");
                        AssertTrue(host.WaitForText(names[0], 15000), "the imported vessel is listed\n" + host.Screen());
                        StopLive(host);
                    }
                }
            }, TestTags.EndToEnd));

            cases.Add(TuiCase.Async(Suite, "health_evaluation_live", "Vessel Health: filter to a vessel, re-evaluate it, follow the job, and open the inspector on the live result", async () =>
            {
                E2EServerFixture fx = await AcquireAsync();
                using (ArmadaClient admin = LiveServerSetup.Admin(fx))
                {
                    VesselSetup setup = await LiveServerSetup.CreateVesselAsync(admin, "tui-health");
                    using (TuiTestHost host = SignIn(fx, "/vessels/health", 190, 50))
                    {
                        VesselHealthScreen screen = HubContent<VesselHealthScreen>(host);
                        AssertTrue(host.PumpUntil(() => screen.HasLoaded, 15000), "health loaded");
                        host.Press("/").Type(setup.Vessel.Name).Press("esc");
                        AssertTrue(host.PumpUntil(() => screen.Grid.Rows.Count == 1 && screen.Grid.Rows[0].VesselId == setup.Vessel.Id, 15000), "filtered to the vessel on the server\n" + host.Screen());
                        AssertTrue(host.Tui.Context.Router.Current!.Query.ContainsKey("q"), "the filter is kept in the route: " + host.Tui.Context.Router.Current!.FullPath);

                        host.Press("home").Press("e");
                        AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Evaluation started", StringComparison.Ordinal)) || screen.Evaluation.Running, 15000), "evaluation started\n" + host.Screen());
                        VesselHealthDetail? detail = null;
                        AssertTrue(LiveServerSetup.PumpUntilServer(host, async () =>
                        {
                            detail = await admin.GetVesselHealthAsync(setup.Vessel.Id);
                            return detail?.Health.EvaluatedUtc != null;
                        }, LiveTimeoutMs), "the server evaluated the vessel");
                        AssertTrue(host.PumpUntil(() => !screen.Evaluation.Running && screen.Grid.Rows.Count == 1 && screen.Grid.Rows[0].EvaluatedUtc != null, LiveTimeoutMs), "the grid shows the evaluated row\n" + host.Screen());

                        host.Press("home").Press("enter");
                        AssertTrue(host.PumpUntil(() => host.App.Modals.Top is VesselHealthDialog, 10000), "Enter opens the health inspector");
                        AssertTrue(host.WaitForText(setup.Vessel.Name, 10000), "inspector names the vessel\n" + host.Screen());
                        host.Press("o");
                        AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/vessels/" + setup.Vessel.Id, 10000), "o opens the vessel");
                        StopLive(host);
                    }
                }
            }, TestTags.EndToEnd));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI end-to-end Build flows (live server, stub runtimes)", cases: cases);
        }

        #endregion

        #region Private-Methods

        private async Task<E2EServerFixture> AcquireAsync()
        {
            E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
            StubCaptainRuntime.Install(fx.Server, _Behavior);
            return fx;
        }

        private static void CreateRepository(string path)
        {
            Directory.CreateDirectory(path);
            Git(path, "init", "-b", "main");
            File.WriteAllText(Path.Combine(path, "README.md"), Path.GetFileName(path) + "\n");
            Git(path, "add", "-A");
            Git(path, "-c", "user.name=Armada Test", "-c", "user.email=test@armada.invalid", "commit", "-m", "Initial");
        }

        private static void Git(string path, params string[] args)
        {
            int code = LiveServerSetup.Git(path, out string output, args);
            if (code != 0) throw new InvalidOperationException("git " + String.Join(" ", args) + " failed: " + output);
        }

        private static TuiTestHost SignIn(E2EServerFixture fx, string route, int width = 160, int height = 48)
        {
            TuiTestHost host = new TuiTestHost(width, height, null, fx.BaseUrl, o => { o.Live = true; o.StartRoute = route; });
            host.Start();
            host.Press("f2").Paste(fx.ApiKey).Press("enter");
            AssertTrue(host.PumpUntil(() => host.Tui.Context.Session.IsSignedIn && host.Tui.Shell.Screen != null, 15000), "signed in");
            AssertTrue(host.PumpUntil(() => host.Tui.Context.Events.IsLive, 15000), "websocket live");
            return host;
        }

        private static void StopLive(TuiTestHost host)
        {
            host.Tui.Context.Events.Stop();
            host.Tui.Context.Status.Stop();
        }

        private static T HubContent<T>(TuiTestHost host) where T : class
        {
            ScreenBase? screen = host.Tui.Shell.Screen;
            T? content = screen is HubScreen hub ? hub.Content as T : screen as T;
            if (content == null) throw new AssertionException("expected " + typeof(T).Name + " but the screen is " + (screen?.GetType().Name ?? "none"));
            return content;
        }

        #endregion
    }
}
