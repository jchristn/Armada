namespace Test.Shared.Suites.Tui.ActivitySystem
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using Armada.Core.Enums;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Admin;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Test.Shared.Suites.Tui.Bodies;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Headless keyboard flows for the setup wizard against a stubbed server: every step with its POST body,
    /// validation, Back, Skip Setup, existing-record reuse, dispatch warnings, and the auto-open rule.
    /// </summary>
    public sealed class TuiSetupWizardSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.System.Setup";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "walk_all_steps", "Keyboard walk creates fleet, vessel, captain, dispatches, and hands off", () =>
            {
                StubHttpHandler stub = EmptyServer();
                using (TuiTestHost host = TuiCase.SignedIn(140, 44, "/setup", stub))
                {
                    SetupWizardScreen screen = Current(host);
                    AssertTrue(host.PumpUntil(() => !screen.Loading), "resources loaded");
                    string frame = host.Screen();
                    TuiScreenDump.Write("setup-objective", frame);
                    TuiCase.Contains(frame, "Launch Armada With One Mission", "title");
                    TuiCase.Contains(frame, "Step 1 of 6", "step count");
                    TuiCase.Contains(frame, "Pick a fleet", "objective text");

                    host.Press("tab");
                    AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.Navigation), "navigation focused");
                    host.Press("right").Press("enter");
                    AssertEqual(1, screen.Current, "fleet step");
                    AssertEqual(SetupWizardModeEnum.New, screen.FleetMode, "new fleet on empty server");
                    TuiScreenDump.Write("setup-fleet", host.Screen());

                    host.Press("ctrl+u").Type("Lab Fleet").Press("tab").Press("ctrl+u").Type("Setup lab");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => screen.Current == 2), "advanced to vessel");
                    AssertEqual(1, stub.CountFor("POST", "/api/v1/fleets"), "one fleet post");
                    SetupWizardFleetBody fleetBody = stub.LastBody<SetupWizardFleetBody>("POST", "/api/v1/fleets");
                    AssertEqual("Lab Fleet", fleetBody.Name, "fleet name");
                    AssertEqual("Setup lab", fleetBody.Description, "fleet description");
                    TuiCase.Contains(host.Screen(), "Created fleet \"Lab Fleet\".", "fleet result");
                    AssertEqual("flt_new", screen.ActiveFleetId, "active fleet");

                    host.Type("armada").Press("tab").Press("tab").Type("/tmp/repo");
                    TuiScreenDump.Write("setup-vessel", host.Screen());
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => screen.Current == 3), "advanced to captain");
                    AssertEqual(1, stub.CountFor("POST", "/api/v1/vessels"), "one vessel post");
                    SetupWizardVesselBody vesselBody = stub.LastBody<SetupWizardVesselBody>("POST", "/api/v1/vessels");
                    AssertEqual("armada", vesselBody.Name, "vessel name");
                    AssertEqual("/tmp/repo", vesselBody.RepoUrl, "repo url");
                    AssertEqual("flt_new", vesselBody.FleetId, "fleet id");
                    AssertEqual("main", vesselBody.DefaultBranch, "default branch");
                    AssertEqual("None", vesselBody.LandingMode, "landing mode");
                    AssertEqual(true, vesselBody.EnableModelContext, "model context toggle");
                    AssertEqual(false, vesselBody.AllowConcurrentMissions, "concurrency toggle");

                    TuiScreenDump.Write("setup-captain", host.Screen());
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => screen.Current == 4), "advanced to dispatch");
                    AssertEqual(1, stub.CountFor("POST", "/api/v1/captains"), "one captain post");
                    StubRequest captainPost = stub.Last("POST", "/api/v1/captains");
                    SetupWizardCaptainBody captainBody = captainPost.BodyAs<SetupWizardCaptainBody>();
                    AssertEqual("Setup Captain", captainBody.Name, "captain name");
                    AssertEqual("ClaudeCode", captainBody.Runtime, "captain runtime");
                    AssertEqual("Standard", captainBody.Tier, "captain tier");
                    AssertEqual("For setup missions, prefer read-only repository inspection unless the mission explicitly asks for code changes.", captainBody.SystemInstructions, "captain instructions");
                    AssertFalse(JsonShape.HasPropertyAnywhere(captainPost.Body, "RuntimeOptionsJson"), "no mux options: " + captainPost.Body);

                    string dispatchFrame = host.Screen();
                    TuiScreenDump.Write("setup-dispatch", dispatchFrame);
                    TuiCase.Contains(dispatchFrame, "Available Captain: Setup Captain", "summary");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => screen.Current == 5), "advanced to handoff");
                    AssertEqual(1, stub.CountFor("POST", "/api/v1/missions"), "one mission post");
                    SetupWizardDispatchBody dispatchBody = stub.LastBody<SetupWizardDispatchBody>("POST", "/api/v1/missions");
                    AssertEqual("vsl_new", dispatchBody.VesselId, "dispatch vessel");
                    AssertEqual("Repository onboarding survey", dispatchBody.Title, "dispatch title");
                    AssertEqual(100, dispatchBody.Priority, "dispatch priority");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("GET", "/api/v1/vessels/vsl_new/readiness") == 1), "readiness loaded");
                    AssertTrue(host.WaitForText("Readiness: 1/3"), "readiness shown");
                    string handoff = host.Screen();
                    TuiScreenDump.Write("setup-handoff", handoff);
                    TuiCase.Contains(handoff, "Dispatched mission \"Repository onboarding survey\".", "dispatch result");
                    TuiCase.Contains(handoff, "msn_setup1", "mission id");
                    TuiCase.Contains(handoff, "Configure a workflow profile", "next recommended step");
                    TuiCase.Contains(handoff, "Open Vessel Onboarding", "handoff link");
                    TuiCase.Contains(handoff, "Create Workflow Profile", "no profiles yet");

                    host.Press("tab");
                    SetupWizardPanel panel = screen.Panels[5];
                    AssertTrue(ReferenceEquals(panel.Scope.Focused, panel.Actions), "handoff actions focused");
                    for (int i = 0; i < 20 && !Focused(panel.Actions, "Finish Setup"); i++) host.Press("right");
                    AssertTrue(Focused(panel.Actions, "Finish Setup"), "finish focused");
                    host.Press("enter");
                    AssertTrue(host.Tui.Context.Prefs.Current.SetupCompleted, "completed flag");
                    AssertEqual("/missions", host.Tui.Context.Router.Current!.Path, "lands on missions");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "dispatch_wrapped_warning", "A mission no captain can take yet: the wrapped { Mission, Warning } reply shows the warning and the mission id", () =>
            {
                StubHttpHandler stub = EmptyServer();
                stub.On("POST", "/api/v1/missions", body => StubHttpHandler.Response(HttpStatusCode.Created,
                    "{\"Mission\":{\"Id\":\"msn_wait1\",\"Title\":\"Repository onboarding survey\",\"Status\":\"Pending\",\"VesselId\":\"vsl_new\"},\"Warning\":\"Mission created but could not be assigned to any captain.\"}"));
                stub.Json("GET", "/api/v1/missions/msn_wait1", "{\"Id\":\"msn_wait1\",\"Title\":\"Repository onboarding survey\",\"Status\":\"Pending\",\"VesselId\":\"vsl_new\"}");
                using (TuiTestHost host = TuiCase.SignedIn(140, 44, "/setup", stub))
                {
                    SetupWizardScreen screen = Current(host);
                    AssertTrue(host.PumpUntil(() => !screen.Loading), "resources loaded");
                    TuiCase.Contains(host.Screen(), "Step 1 of 6", "objective step drawn");
                    host.Press("tab");
                    AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.Navigation), "navigation focused");
                    host.Press("right").Press("enter");
                    AssertEqual(1, screen.Current, "fleet step");
                    host.Press("ctrl+u").Type("Lab Fleet").Press("tab").Press("ctrl+u").Type("Setup lab");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => screen.Current == 2), "vessel step\n" + host.Screen());
                    host.Type("armada").Press("tab").Press("tab").Type("/tmp/repo").Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => screen.Current == 3), "captain step");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => screen.Current == 4), "dispatch step");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => screen.Current == 5), "handoff");
                    AssertEqual("msn_wait1", screen.DispatchedMission?.Id, "mission id read from the wrapped reply");
                    AssertEqual("Mission created but could not be assigned to any captain.", screen.DispatchWarning, "warning kept");
                    AssertTrue(host.WaitForText("could not be assigned to any captain"), "warning shown\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "msn_wait1", "mission id shown");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "handoff_explains_failed_mission", "A failed first mission: the handoff shows the full id, the failure reason, the rescue missions, and a View Mission Log action", () =>
            {
                StubHttpHandler stub = EmptyServer();
                string failed = "{\"Id\":\"msn_setupfail1\",\"Title\":\"Repository onboarding survey\",\"Status\":\"Failed\",\"VesselId\":\"vsl_new\",\"FailureReason\":\"Agent exited with code 1: nothing to commit\"}";
                stub.On("POST", "/api/v1/missions", body => StubHttpHandler.Response(HttpStatusCode.Created, failed));
                stub.Json("GET", "/api/v1/missions/msn_setupfail1", failed);
                stub.Json("GET", "/api/v1/incidents", "{\"Objects\":[{\"Id\":\"inc_1\",\"Title\":\"Mission failed\",\"MissionId\":\"msn_setupfail1\",\"RescueMissionIds\":[\"msn_rescue1\"]}],\"TotalRecords\":1}");
                stub.Json("GET", "/api/v1/missions/msn_rescue1", "{\"Id\":\"msn_rescue1\",\"Title\":\"[Rescue] Repository onboarding survey\",\"Status\":\"InProgress\",\"VesselId\":\"vsl_new\"}");
                stub.Json("GET", "/api/v1/missions/msn_setupfail1/log", "{\"Log\":\"cloning\\nnothing to commit, working tree clean\",\"Lines\":2,\"TotalLines\":2}");
                using (TuiTestHost host = TuiCase.SignedIn(140, 60, "/setup", stub))
                {
                    SetupWizardScreen screen = Current(host);
                    AssertTrue(host.PumpUntil(() => !screen.Loading), "resources loaded");
                    TuiCase.Contains(host.Screen(), "Step 1 of 6", "objective step drawn");
                    host.Press("tab");
                    host.Press("right").Press("enter");
                    AssertEqual(1, screen.Current, "fleet step");
                    host.Press("ctrl+u").Type("Lab Fleet").Press("tab").Press("ctrl+u").Type("Setup lab");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => screen.Current == 2), "vessel step");
                    host.Type("armada").Press("tab").Press("tab").Type("/tmp/repo").Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => screen.Current == 3), "captain step");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => screen.Current == 4), "dispatch step");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => screen.Current == 5), "handoff");

                    AssertTrue(host.PumpUntil(() => screen.RescueMissions.Count == 1), "rescue missions loaded");
                    AssertTrue(host.WaitForText("[Rescue] Repository onboarding survey"), "rescue shown\n" + host.Screen());
                    string handoff = host.Screen();
                    TuiScreenDump.Write("setup-handoff-failed", handoff);
                    TuiCase.Contains(handoff, "msn_setupfail1", "full mission id");
                    TuiCase.Contains(handoff, "The mission failed.", "failure headline");
                    TuiCase.Contains(handoff, "Agent exited with code 1: nothing to commit", "failure reason");
                    TuiCase.Contains(handoff, "Armada started a rescue mission to retry this work:", "rescue mentioned");
                    TuiCase.Contains(handoff, "View Mission Log", "log action");
                    AssertTrue(stub.Saw("GET", "/api/v1/incidents", r => r.Query.Contains("missionId=msn_setupfail1", StringComparison.Ordinal)), "incidents queried by mission");

                    SetupWizardPanel panel = screen.Panels[5];
                    panel.Actions.Buttons.First(b => b.Label == "View Mission Log").Press();
                    AssertTrue(host.WaitForText("nothing to commit, working tree clean"), "log shown\n" + host.Screen());
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "validation_and_back", "Validation blocks the step with the dashboard message and Back keeps entered values", () =>
            {
                StubHttpHandler stub = EmptyServer();
                using (TuiTestHost host = TuiCase.SignedIn(140, 44, "/setup", stub))
                {
                    SetupWizardScreen screen = Current(host);
                    AssertTrue(host.PumpUntil(() => !screen.Loading), "loaded");
                    screen.GoTo(1);
                    host.Press("ctrl+u").Press("ctrl+s");
                    AssertTrue(host.WaitForText("Fleet name is required."), "fleet validation");
                    AssertFalse(screen.CanAdvance(), "next disabled without a fleet");
                    host.Type("Fleet A").Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => screen.Current == 2), "vessel step");

                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Vessel name is required."), "vessel name validation");
                    host.Type("svc");
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Repository URL is required."), "repo validation");
                    screen.RepoUrl.Value = "/tmp/svc";
                    screen.LandingMode.SetValue("LocalMerge");
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Local Merge needs a working directory to merge into."), "local merge validation");
                    AssertEqual(0, stub.CountFor("POST", "/api/v1/vessels"), "no vessel post");

                    host.Press("tab");
                    AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.Panels[2]) || ReferenceEquals(screen.Scope.Focused, screen.Navigation), "focus moves");
                    screen.Navigation.Buttons.First(b => b.Label == "Back").Press();
                    host.Pump();
                    AssertEqual(1, screen.Current, "back to fleet");
                    TuiCase.Contains(host.Screen(), "Use Fleet", "fleet step shows existing mode");
                    screen.Navigation.Buttons.First(b => b.Label == "Next").Press();
                    host.Pump();
                    AssertEqual(2, screen.Current, "forward again");
                    AssertEqual("svc", screen.VesselName.Value, "vessel name kept");
                    AssertEqual("/tmp/svc", screen.RepoUrl.Value, "repo kept");
                    TuiCase.Contains(host.Screen(), "svc", "value rendered");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "merge_and_push", "Merge and Push is offered after Local Merge, needs a working directory, and is sent as MergeAndPush", () =>
            {
                StubHttpHandler stub = EmptyServer();
                using (TuiTestHost host = TuiCase.SignedIn(140, 44, "/setup", stub))
                {
                    SetupWizardScreen screen = Current(host);
                    AssertTrue(host.PumpUntil(() => !screen.Loading), "loaded");
                    AssertEqual("|None|LocalMerge|MergeAndPush|PullRequest|MergeQueue", String.Join("|", screen.LandingMode.Options.Select(o => o.Value)), "landing options");
                    AssertEqual("Merge and Push", screen.LandingMode.Options.First(o => o.Value == "MergeAndPush").Label, "short name");
                    screen.GoTo(1);
                    host.Press("ctrl+u").Type("Fleet A").Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => screen.Current == 2), "vessel step");
                    screen.VesselName.Value = "svc";
                    screen.RepoUrl.Value = "/tmp/svc";
                    screen.LandingMode.Choose(screen.LandingMode.Options.First(o => o.Value == "LocalMerge"));
                    AssertTrue(host.WaitForText("Finished work is merged into the working directory. Nothing is pushed."), "LocalMerge hint says nothing is pushed\n" + host.Screen());
                    screen.LandingMode.Choose(screen.LandingMode.Options.First(o => o.Value == "MergeAndPush"));
                    AssertTrue(host.WaitForText("pushed to its origin remote"), "MergeAndPush hint\n" + host.Screen());
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Merge and Push needs a working directory to merge into."), "MergeAndPush validation\n" + host.Screen());
                    AssertEqual(0, stub.CountFor("POST", "/api/v1/vessels"), "no vessel post without a working directory");
                    screen.WorkingDirectory.Value = "/work/svc";
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => screen.Current == 3), "advanced to captain");
                    AssertEqual(1, stub.CountFor("POST", "/api/v1/vessels"), "one vessel post");
                    SetupWizardVesselBody vesselBody = stub.LastBody<SetupWizardVesselBody>("POST", "/api/v1/vessels");
                    AssertEqual("MergeAndPush", vesselBody.LandingMode, "landing mode");
                    AssertEqual("/work/svc", vesselBody.WorkingDirectory, "working directory");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "skip_setup", "Skip Setup sets the completed preference and lands on Missions", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/setup", EmptyServer()))
                {
                    SetupWizardScreen screen = Current(host);
                    AssertFalse(host.Tui.Context.Prefs.Current.SetupCompleted, "not completed");
                    host.Press("tab");
                    AssertTrue(Focused(screen.Navigation, "Skip Setup"), "skip focused");
                    host.Press("enter");
                    AssertTrue(host.Tui.Context.Prefs.Current.SetupCompleted, "completed after skip");
                    AssertEqual("/missions", host.Tui.Context.Router.Current!.Path, "missions");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "existing_records", "Existing fleets, vessels, and idle captains are reused without creating records", () =>
            {
                StubHttpHandler stub = PopulatedServer();
                using (TuiTestHost host = TuiCase.SignedIn(140, 44, "/setup", stub))
                {
                    SetupWizardScreen screen = Current(host);
                    AssertTrue(host.PumpUntil(() => !screen.Loading), "loaded");
                    AssertEqual(SetupWizardModeEnum.Existing, screen.FleetMode, "fleet existing");
                    AssertEqual(SetupWizardModeEnum.Existing, screen.VesselMode, "vessel existing");
                    AssertEqual(SetupWizardModeEnum.Existing, screen.CaptainMode, "captain existing");
                    AssertEqual(1, screen.IdleCaptains().Count, "only idle captains offered");
                    screen.GoTo(1);
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => screen.Current == 2), "fleet reused");
                    TuiCase.Contains(host.Screen(), "Using fleet \"Main\".", "using fleet");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => screen.Current == 3), "vessel reused");
                    AssertEqual("flt_main", screen.ActiveFleetId, "vessel fleet adopted");
                    TuiScreenDump.Write("setup-captain-existing", host.Screen());
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => screen.Current == 4), "captain reused");
                    AssertEqual("cpt_idle", screen.ActiveCaptainId, "idle captain");
                    AssertEqual(0, stub.CountFor("POST", "/api/v1/fleets") + stub.CountFor("POST", "/api/v1/vessels") + stub.CountFor("POST", "/api/v1/captains"), "nothing created");

                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => screen.Current == 5), "dispatched");
                    AssertTrue(host.WaitForText("could not be assigned"), "dispatch warning shown");
                    AssertEqual(SetupWizardResultKindEnum.Info, screen.ResultKind, "info result");
                    AssertEqual("msn_wrapped", screen.DispatchedMission!.Id, "wrapped mission parsed");
                    AssertTrue(host.WaitForText("Open Workflow Profiles"), "profiles exist");
                    TuiCase.Contains(host.Screen(), "Workflow Profiles: 2", "global and vessel profiles count, other vessel excluded");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "mux_captain", "A Mux captain requires an endpoint and sends the runtime options the dashboard builds", () =>
            {
                StubHttpHandler stub = EmptyServer();
                stub.Json("GET", "/api/v1/runtimes/mux/endpoints", "{\"Success\":true,\"Endpoints\":[{\"Name\":\"local\",\"AdapterType\":\"openai\",\"Model\":\"gpt\"}]}");
                using (TuiTestHost host = TuiCase.SignedIn(140, 50, "/setup", stub))
                {
                    SetupWizardScreen screen = Current(host);
                    AssertTrue(host.PumpUntil(() => !screen.Loading), "loaded");
                    screen.GoTo(3);
                    screen.Runtime.Choose(screen.Runtime.Options.First(o => o.Value == AgentRuntimeEnum.Mux));
                    AssertTrue(host.WaitForText("1 saved Mux endpoint(s) available."), "endpoint hint");
                    screen.SubmitCaptain();
                    AssertTrue(host.WaitForText("Mux captains require a named Mux endpoint."), "mux validation");
                    screen.MuxEndpoint.Value = "local";
                    screen.MuxTemperature.Value = "0.5";
                    screen.MuxMaxTokens.Value = "2048x";
                    screen.SubmitCaptain();
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/captains") == 1), "captain posted");
                    SetupWizardCaptainBody body = stub.LastBody<SetupWizardCaptainBody>("POST", "/api/v1/captains");
                    AssertEqual("Mux", body.Runtime, "runtime");
                    AssertNotNull(body.RuntimeOptionsJson, "runtime options sent");
                    AssertEqual("local", JsonHelper.Deserialize<SetupWizardMuxOptionsBody>(body.RuntimeOptionsJson!).Endpoint, "runtime options endpoint: " + body.RuntimeOptionsJson);
                    string json = SetupWizardScreen.BuildMuxRuntimeOptionsJson(AgentRuntimeEnum.Mux, "", "local", "", "", "0.5", "2048x", "", "deny")!;
                    SetupWizardMuxOptionsBody options = JsonHelper.Deserialize<SetupWizardMuxOptionsBody>(json);
                    AssertEqual(1, options.SchemaVersion, "schema version: " + json);
                    AssertEqual(0.5, options.Temperature, "temperature: " + json);
                    AssertEqual(2048, options.MaxTokens, "max tokens: " + json);
                    AssertEqual("deny", options.ApprovalPolicy, "approval policy: " + json);
                    AssertNull(SetupWizardScreen.BuildMuxRuntimeOptionsJson(AgentRuntimeEnum.ClaudeCode, "", "x", "", "", "", "", "", ""), "not mux");
                    AssertEqual(100, SetupWizardScreen.ParsePriority("abc"), "invalid priority");
                    AssertEqual(7, SetupWizardScreen.ParsePriority("7days"), "parseInt prefix");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "dispatch_guards", "Dispatch requires a vessel, a captain, and a title and description", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/setup", EmptyServer()))
                {
                    SetupWizardScreen screen = Current(host);
                    AssertTrue(host.PumpUntil(() => !screen.Loading), "loaded");
                    screen.GoTo(4);
                    screen.SubmitDispatch();
                    AssertTrue(host.WaitForText("Choose or create a vessel before dispatching."), "vessel guard");
                    AssertFalse(screen.CanAdvance(), "next disabled before dispatch");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "auto_open_rule", "The auto-open rule matches the dashboard", () =>
            {
                SetupWizardDecision empty = SetupWizardAutoOpen.Decide(false, false, false, true);
                AssertTrue(empty.Open && empty.ClearCompleted, "empty deployment opens and clears the flag");
                AssertTrue(SetupWizardAutoOpen.Decide(true, false, true, false).Open, "missing vessel opens");
                AssertFalse(SetupWizardAutoOpen.Decide(true, false, true, true).Open, "completed suppresses");
                AssertFalse(SetupWizardAutoOpen.Decide(true, true, true, false).Open, "complete deployment does not open");
                AssertTrue(SetupWizardAutoOpen.Decide(null, null, null, true).Open, "failed check opens");
                AssertFalse(SetupWizardAutoOpen.Decide(true, false, true, true).ClearCompleted, "flag kept");
            }));

            cases.Add(TuiCase.Sync(Suite, "auto_open_host", "After sign-in an empty server opens the wizard and a populated one does not", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/missions", EmptyServer()))
                {
                    host.Tui.Context.Prefs.Current.SetupCompleted = true;
                    SetupWizardAutoOpen.CheckAsync(host.Tui.Context);
                    AssertTrue(host.PumpUntil(() => host.Tui.Shell.Screen is SetupWizardScreen), "wizard opened");
                    AssertFalse(host.Tui.Context.Prefs.Current.SetupCompleted, "stale flag cleared");
                    TuiCase.Contains(host.Screen(), "Launch Armada With One Mission", "wizard rendered");
                }

                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/missions", PopulatedServer()))
                {
                    System.Threading.Tasks.Task check = SetupWizardAutoOpen.CheckAsync(host.Tui.Context);
                    AssertTrue(host.PumpUntil(() => check.IsCompleted), "check finished");
                    host.Pump();
                    AssertFalse(host.Tui.Shell.Screen is SetupWizardScreen, "not opened");
                    AssertEqual("/missions", host.Tui.Context.Router.Current!.Path, "stays");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI setup wizard", cases: cases);
        }

        private static StubHttpHandler EmptyServer()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            string empty = "{\"Objects\":[],\"TotalRecords\":0}";
            stub.Json("GET", "/api/v1/fleets", empty);
            stub.Json("GET", "/api/v1/vessels", empty);
            stub.Json("GET", "/api/v1/captains", empty);
            stub.On("POST", "/api/v1/fleets", body => StubHttpHandler.Response(HttpStatusCode.Created, JsonHelper.Deserialize<SetupWizardFleetBody>(body).Name == "Fleet A" ? "{\"Id\":\"flt_new\",\"Name\":\"Fleet A\"}" : "{\"Id\":\"flt_new\",\"Name\":\"Lab Fleet\"}"));
            stub.Json("POST", "/api/v1/vessels", "{\"Id\":\"vsl_new\",\"Name\":\"armada\",\"FleetId\":\"flt_new\",\"RepoUrl\":\"/tmp/repo\",\"DefaultBranch\":\"main\"}");
            stub.Json("POST", "/api/v1/captains", "{\"Id\":\"cpt_new\",\"Name\":\"Setup Captain\",\"Runtime\":\"ClaudeCode\",\"State\":\"Idle\"}");
            stub.On("POST", "/api/v1/missions", body => StubHttpHandler.Response(HttpStatusCode.Created, "{\"Id\":\"msn_setup1\",\"Title\":\"Repository onboarding survey\",\"Status\":\"Assigned\",\"VesselId\":\"vsl_new\",\"CaptainId\":\"cpt_new\",\"BranchName\":\"armada/setup\"}"));
            stub.Json("GET", "/api/v1/missions/msn_setup1", "{\"Id\":\"msn_setup1\",\"Title\":\"Repository onboarding survey\",\"Status\":\"InProgress\",\"VesselId\":\"vsl_new\"}");
            stub.Json("GET", "/api/v1/vessels/vsl_new/readiness", "{\"VesselId\":\"vsl_new\",\"SetupChecklistSatisfiedCount\":1,\"SetupChecklistTotalCount\":3,\"ErrorCount\":0,\"SetupChecklist\":[{\"Code\":\"a\",\"Title\":\"Working directory\",\"IsSatisfied\":true},{\"Code\":\"b\",\"Title\":\"Configure a workflow profile\",\"Message\":\"Teach Armada how to build.\",\"IsSatisfied\":false}]}");
            stub.Json("GET", "/api/v1/workflow-profiles", empty);
            stub.Json("GET", "/api/v1/environments", empty);
            return stub;
        }

        internal static StubHttpHandler PopulatedServer()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            stub.Json("GET", "/api/v1/fleets", "{\"Objects\":[{\"Id\":\"flt_main\",\"Name\":\"Main\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/vessels", "{\"Objects\":[{\"Id\":\"vsl_main\",\"Name\":\"armada\",\"FleetId\":\"flt_main\",\"RepoUrl\":\"/tmp/a\",\"DefaultBranch\":\"main\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/captains", "{\"Objects\":[{\"Id\":\"cpt_busy\",\"Name\":\"Busy\",\"State\":\"Working\"},{\"Id\":\"cpt_idle\",\"Name\":\"Idle One\",\"State\":\"Idle\"}],\"TotalRecords\":2}");
            stub.On("POST", "/api/v1/missions", body => StubHttpHandler.Response(HttpStatusCode.Created, "{\"Mission\":{\"Id\":\"msn_wrapped\",\"Title\":\"Repository onboarding survey\",\"Status\":\"Pending\",\"VesselId\":\"vsl_main\"},\"Warning\":\"Mission created but could not be assigned to any captain. It will be retried on the next health check cycle.\"}"));
            stub.Json("GET", "/api/v1/missions/msn_wrapped", "{\"Id\":\"msn_wrapped\",\"Title\":\"Repository onboarding survey\",\"Status\":\"Pending\"}");
            stub.Json("GET", "/api/v1/vessels/vsl_main/readiness", "{\"VesselId\":\"vsl_main\",\"SetupChecklistSatisfiedCount\":3,\"SetupChecklistTotalCount\":3}");
            stub.Json("GET", "/api/v1/workflow-profiles", "{\"Objects\":[{\"Id\":\"wfp_1\",\"Name\":\"Global\",\"Scope\":\"Global\"},{\"Id\":\"wfp_2\",\"Name\":\"Mine\",\"Scope\":\"Vessel\",\"VesselId\":\"vsl_main\"},{\"Id\":\"wfp_3\",\"Name\":\"Other\",\"Scope\":\"Vessel\",\"VesselId\":\"vsl_other\"}],\"TotalRecords\":3}");
            stub.Json("GET", "/api/v1/environments", "{\"Objects\":[],\"TotalRecords\":0}");
            return stub;
        }

        private static SetupWizardScreen Current(TuiTestHost host)
        {
            host.PumpUntil(() => host.Tui.Shell.Screen is SetupWizardScreen);
            if (host.Tui.Shell.Screen is SetupWizardScreen screen) return screen;
            throw new AssertionException("current screen is " + (host.Tui.Shell.Screen?.GetType().Name ?? "null"));
        }

        private static bool Focused(SetupWizardActionBar bar, string label)
        {
            return bar.Scope.Focused is Button b && b.Label == label;
        }
    }
}
