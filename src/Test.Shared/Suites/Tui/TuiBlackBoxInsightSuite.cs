namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Build;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The black box prevention, forensics and operations views in the TUI (plan section 13): the dispatch preview
    /// (overlap preview and brief lint) and the response warnings on Dispatch, Create Voyage and Create Mission; the
    /// vessel's Serialize Overlapping Missions toggle, Reservations and Black Box Doctor (Apply through the dry run and
    /// confirm flow); the entry screen's Timeline, Diff and Nearby Successes tabs; the voyage Cascade tab; the Black Box
    /// list's recovery stats view; runbook-linked recovery paths; and the keys of each in the focus hints.
    /// </summary>
    public sealed class TuiBlackBoxInsightSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.BlackBoxInsight";

        private const string PreviewJson = "{\"VesselId\":\"vsl_demo\",\"SerializeOverlappingMissions\":false,\"LikelyFiles\":[\"src/a.cs\"],\"LikelyFileCount\":1,\"WarningCount\":1,"
            + "\"Warnings\":[{\"Kind\":\"ActiveMission\",\"OverlappingMissionId\":\"msn_busy\",\"OverlappingMissionTitle\":\"Busy work\",\"OverlappingMissionStatus\":\"InProgress\",\"FileCount\":1,\"Message\":\"Both touch src/a.cs\"}],"
            + "\"SuggestedDependsOnMissionId\":\"msn_busy\",\"ActiveMissionsCompared\":3,"
            + "\"BriefLint\":{\"FindingCount\":1,\"Clean\":false,\"PathsChecked\":true,\"Findings\":[{\"RuleId\":\"BRIEF-DEP-001\",\"Severity\":\"Warning\",\"Message\":\"Dependency language without a dependency\",\"Excerpt\":\"after mission 2 lands\",\"Suggestion\":\"Set dependsOnMissionId\"}]}}";

        private const string LintJson = "{\"FindingCount\":1,\"Clean\":false,\"PathsChecked\":false,\"Findings\":[{\"RuleId\":\"BRIEF-NOOP-001\",\"Severity\":\"Warning\",\"Message\":\"Verification-only brief\",\"Suggestion\":\"Dispatch it as Audit\"}]}";

        private const string WarningsJson = "\"OverlapWarnings\":[{\"Kind\":\"Hotspot\",\"Directory\":\"src/Landing\",\"HotspotEntryCount\":4,\"Message\":\"src/Landing failed 4 times\"}],"
            + "\"BriefLintWarnings\":[{\"RuleId\":\"BRIEF-HOST-001\",\"Severity\":\"Warning\",\"Message\":\"Host path in the brief\",\"Path\":\"/Users/me/x\"}]";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "dispatch_preview_and_warnings", "Dispatch: Alt+P previews overlaps and brief lint for the vessel; Ctrl+S shows the warnings the voyage response carries", () =>
            {
                StubHttpHandler stub = TuiOpsPlanningSuite.Stub();
                stub.Json("POST", "/api/v1/vessels/vsl_demo/overlap-preview", PreviewJson);
                stub.Json("POST", "/api/v1/voyages", "{\"Id\":\"vyg_9\",\"Title\":\"Fix it\"," + WarningsJson + "}");
                using (TuiTestHost host = TuiCase.SignedIn(150, 55, "/dispatch", stub))
                {
                    AssertTrue(host.WaitForText("Describe the work you want Armada to dispatch"), "dispatch\n" + host.Screen());
                    DispatchScreen screen = (DispatchScreen)((HubScreen)host.Tui.Shell.Screen!).Content;
                    TuiCase.Contains(Status(host), "Alt+P Preview overlaps", "preview key in the hints");
                    AssertTrue(host.PumpUntil(() => screen.Vessel.Options.Any(o => o.Value == "vsl_demo")), "vessels loaded");
                    screen.Vessel.Choose(screen.Vessel.Options.First(o => o.Value == "vsl_demo"));
                    screen.Description.Text = "Fix src/a.cs after mission 2 lands";
                    host.Press("alt+p");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/vessels/vsl_demo/overlap-preview") == 1), "overlap preview called");
                    MissionOverlapPreviewRequest sent = stub.LastBody<MissionOverlapPreviewRequest>("POST", "/api/v1/vessels/vsl_demo/overlap-preview");
                    AssertEqual("Fix src/a.cs after mission 2 lands", sent.Description, "brief sent");
                    AssertEqual(0, stub.CountFor("POST", "/api/v1/missions/lint-brief"), "the preview carried the lint");
                    AssertTrue(host.WaitForText("Preview only: nothing was dispatched."), "preview viewer\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "msn_busy", "overlapping mission");
                    TuiCase.Contains(frame, "BRIEF-DEP-001", "lint finding");
                    AssertEqual(0, stub.CountFor("POST", "/api/v1/voyages"), "nothing dispatched");
                    host.Press("esc");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/voyages") == 1), "dispatched");
                    AssertTrue(host.WaitForText("Dispatch Warnings"), "warnings viewer\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "src/Landing", "hotspot warning");
                    TuiCase.Contains(host.Screen(), "BRIEF-HOST-001", "lint warning");
                    AssertTrue(host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Severity == NotificationSeverityEnum.Warning && t.Text.Contains("2 black box warning")), "warning toast");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "dispatch_preview_without_vessel_or_text", "Without a vessel the preview lints the brief only; with no text it calls nothing; a clean dispatch shows no warnings", () =>
            {
                StubHttpHandler stub = TuiOpsPlanningSuite.Stub();
                stub.Json("POST", "/api/v1/missions/lint-brief", LintJson);
                stub.Json("POST", "/api/v1/voyages", "{\"Id\":\"vyg_9\",\"Title\":\"Fix it\"}");
                using (TuiTestHost host = TuiCase.SignedIn(150, 55, "/dispatch", stub))
                {
                    AssertTrue(host.WaitForText("Describe the work you want Armada to dispatch"), "dispatch");
                    DispatchScreen screen = (DispatchScreen)((HubScreen)host.Tui.Shell.Screen!).Content;
                    host.Press("alt+p");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("to preview"))), "nothing to preview");
                    Settle(host);
                    AssertEqual(0, stub.CountFor("POST", "/api/v1/missions/lint-brief"), "no call without text");
                    screen.Description.Text = "Verify the build passes";
                    screen.Preview();
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/missions/lint-brief") == 1), "lint only");
                    AssertNull(stub.LastBody<BriefLintRequest>("POST", "/api/v1/missions/lint-brief").VesselId, "no vessel");
                    AssertEqual(0, stub.Log.Count(r => r.Path.EndsWith("/overlap-preview", StringComparison.Ordinal)), "no overlap preview without a vessel");
                    AssertTrue(host.WaitForText("Choose a vessel to check overlaps"), "lint viewer\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "BRIEF-NOOP-001", "finding");
                    host.Press("esc");
                    AssertTrue(host.PumpUntil(() => screen.Vessel.Options.Any(o => o.Value == "vsl_demo")), "vessels loaded");
                    screen.Vessel.Choose(screen.Vessel.Options.First(o => o.Value == "vsl_demo"));
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/voyages") == 1), "dispatched");
                    Settle(host);
                    TuiCase.NotContains(host.Screen(), "Dispatch Warnings", "no warnings viewer for a clean dispatch");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "create_mission_dialog_preview_and_warnings", "Create Mission: Alt+P in the dialog previews; the created mission's warnings open in a viewer", () =>
            {
                StubHttpHandler stub = TuiOpsPlanningSuite.Stub();
                stub.Page("/api/v1/missions/summaries", "[]");
                stub.Json("POST", "/api/v1/vessels/vsl_demo/overlap-preview", PreviewJson);
                stub.Json("POST", "/api/v1/missions/lint-brief", LintJson);
                stub.Json("POST", "/api/v1/missions", "{\"Id\":\"msn_new\",\"Title\":\"Fix it\",\"Status\":\"Pending\",\"VesselId\":\"vsl_demo\"," + WarningsJson + "}");
                using (TuiTestHost host = TuiCase.SignedIn(160, 55, "/missions", stub))
                {
                    AssertTrue(host.PumpUntil(() => Inner(host) is MissionsScreen), "missions screen\n" + host.Screen());
                    host.Press("n");
                    AssertTrue(host.WaitForText("Create Mission"), "dialog\n" + host.Screen());
                    OpsFormDialog dialog = (OpsFormDialog)host.App.Modals.Top!;
                    AssertNotNull(dialog.Preview, "preview offered");
                    TuiCase.Contains(host.Screen(), "Alt+P Preview overlaps", "preview key in the dialog footer");
                    host.Type("Fix it");
                    host.Press("alt+p");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/missions/lint-brief") == 1), "no vessel yet: lint only");
                    AssertTrue(host.WaitForText("Preview only: nothing was dispatched."), "viewer\n" + host.Screen());
                    host.Press("esc");
                    AssertTrue(host.PumpUntil(() => ReferenceEquals(host.App.Modals.Top, dialog)), "back on the dialog");
                    SelectField<string> vessel = (SelectField<string>)dialog.Form.Rows.First(r => r.Label == "Vessel").Field!;
                    AssertTrue(host.PumpUntil(() => vessel.Options.Any(o => o.Value == "vsl_demo")), "vessel options");
                    vessel.Choose(vessel.Options.First(o => o.Value == "vsl_demo"));
                    host.Press("alt+p");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/vessels/vsl_demo/overlap-preview") == 1), "overlap preview for the vessel");
                    AssertTrue(host.WaitForText("msn_busy"), "overlap shown\n" + host.Screen());
                    host.Press("esc");
                    AssertTrue(host.PumpUntil(() => ReferenceEquals(host.App.Modals.Top, dialog)), "dialog again");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/missions") == 1), "created");
                    AssertTrue(host.WaitForText("Dispatch Warnings"), "warnings\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "src/Landing", "overlap warning");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "create_voyage_warnings", "Create Voyage: Alt+P previews every mission; the voyage's warnings open after it is created", () =>
            {
                StubHttpHandler stub = TuiOpsPlanningSuite.Stub();
                stub.Json("POST", "/api/v1/vessels/vsl_demo/overlap-preview", PreviewJson);
                stub.Json("POST", "/api/v1/voyages", "{\"Id\":\"vyg_7\",\"Title\":\"Batch\"," + WarningsJson + "}");
                stub.Json("GET", "/api/v1/voyages/vyg_7", "{\"Voyage\":{\"Id\":\"vyg_7\",\"Title\":\"Batch\",\"Status\":\"Open\"},\"TotalMissions\":0,\"MissionCountsByStatus\":{}}");
                stub.Page("/api/v1/missions", "[]");
                using (TuiTestHost host = TuiCase.SignedIn(160, 60, "/voyages/create", stub))
                {
                    AssertTrue(host.PumpUntil(() => Inner(host) is VoyageCreateScreen), "create voyage\n" + host.Screen());
                    VoyageCreateScreen screen = (VoyageCreateScreen)Inner(host)!;
                    TuiCase.Contains(Status(host), "Alt+P Preview overlaps", "hint");
                    AssertTrue(host.PumpUntil(() => screen.Vessel.Options.Any(o => o.Value == "vsl_demo")), "vessels");
                    screen.Vessel.Choose(screen.Vessel.Options.First(o => o.Value == "vsl_demo"));
                    screen.VoyageTitle.Value = "Batch";
                    screen.MissionTitles[0].Value = "First";
                    screen.AddMission();
                    screen.MissionTitles[1].Value = "Second";
                    host.Press("alt+p");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/vessels/vsl_demo/overlap-preview") == 2), "one preview per mission");
                    AssertTrue(host.WaitForText("== First =="), "per-mission sections\n" + host.Screen());
                    host.Press("esc");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/voyages") == 1), "created");
                    AssertTrue(host.WaitForText("Dispatch Warnings"), "warnings\n" + host.Screen());
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "vessel_serialize_reservations_doctor", "Vessel: L toggles Serialize Overlapping Missions; Reservations and Black Box Doctor are paged; A applies an actionable recommendation after its dry run", () =>
            {
                StubHttpHandler stub = Test.Shared.Suites.Tui.Build.TuiBuildVesselsSuite.Stub();
                stub.On("PUT", "/api/v1/vessels/vsl_demo", b => StubHttpHandler.Response(HttpStatusCode.OK, b));
                stub.Json("POST", "/api/v1/vessels/vsl_demo/reservations/enumerate", "{\"VesselId\":\"vsl_demo\",\"Threshold\":3,\"HotDirectoryCount\":1,\"ActiveMissionsScanned\":2,\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":1,\"TotalRecords\":1,"
                    + "\"Objects\":[{\"VesselId\":\"vsl_demo\",\"Directory\":\"src/Landing\",\"MissionId\":\"msn_res\",\"MissionTitle\":\"Reserving mission\",\"MissionStatus\":\"InProgress\",\"HotspotEntryCount\":5,\"FileCount\":2,\"SinceUtc\":\"2026-10-20T10:00:00Z\"}]}");
                stub.Json("POST", "/api/v1/vessels/vsl_demo/blackbox-doctor/enumerate", "{\"VesselId\":\"vsl_demo\",\"WindowDays\":30,\"EntriesInWindow\":9,\"EntriesScanned\":9,\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":1,\"TotalRecords\":2,\"Objects\":["
                    + "{\"Key\":\"k1\",\"RuleId\":\"BBX-LAND-001\",\"RecommendationId\":\"rec_1\",\"Setting\":\"LandingMode\",\"Scope\":\"Vessel\",\"CurrentValue\":\"LocalMerge\",\"SuggestedValue\":\"PullRequest\",\"Count\":6,\"ExampleEntryId\":\"bbx_ex\",\"Applicable\":true},"
                    + "{\"Key\":\"k2\",\"RuleId\":\"BBX-DOCK-002\",\"Setting\":\"MaxCaptains\",\"Scope\":\"Global\",\"SuggestedValue\":\"2\",\"Count\":3,\"ExampleEntryId\":\"bbx_ex2\",\"Applicable\":false}]}");
                stub.On("POST", "/api/v1/blackbox/bbx_ex/recommendations/rec_1/apply", body =>
                {
                    BlackBoxRecommendationApplyRequest? r = System.Text.Json.JsonSerializer.Deserialize<BlackBoxRecommendationApplyRequest>(body, ArmadaJson.Options);
                    return StubHttpHandler.Response(HttpStatusCode.OK, r == null || r.DryRun
                        ? "{\"EntryId\":\"bbx_ex\",\"RecommendationId\":\"rec_1\",\"DryRun\":true,\"ConfirmToken\":\"bbc_rec\",\"Action\":{\"Tool\":\"update_vessel\"},\"Succeeded\":true}"
                        : "{\"EntryId\":\"bbx_ex\",\"RecommendationId\":\"rec_1\",\"DryRun\":false,\"Succeeded\":true,\"Message\":\"LandingMode is PullRequest.\"}");
                });
                using (TuiTestHost host = TuiCase.SignedIn(180, 55, "/vessels/vsl_demo", stub))
                {
                    AssertTrue(host.PumpUntil(() => host.Tui.Shell.Screen is VesselScreen v && v.Vessel != null), "vessel\n" + host.Screen());
                    VesselScreen screen = (VesselScreen)host.Tui.Shell.Screen!;
                    AssertTrue(host.PumpUntil(() => { host.Screen(); return screen.Overview.PlainText.Contains("Serialize Overlapp", StringComparison.Ordinal); }), "field on the overview: " + screen.Overview.PlainText);
                    TuiCase.Contains(Status(host), "L Serialize overlapping missions", "toggle key in the hints");
                    host.Press("L");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("PUT", "/api/v1/vessels/vsl_demo") == 1), "vessel updated");
                    VesselUpsertRequest put = stub.LastBody<VesselUpsertRequest>("PUT", "/api/v1/vessels/vsl_demo");
                    AssertTrue(put.SerializeOverlappingMissions, "serialize turned on");
                    AssertEqual("DemoRepo", put.Name, "the full record is sent");

                    GoTo(host, screen, "reservations");
                    AssertTrue(host.WaitForText("src/Landing"), "reservation row\n" + host.Screen());
                    AssertEqual(25, stub.LastBody<EnumerationQuery>("POST", "/api/v1/vessels/vsl_demo/reservations/enumerate").PageSize, "server paged");
                    TuiCase.Contains(Status(host), "Enter Open mission", "reservation hint");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/missions/msn_res"), "Enter opens the mission");
                }

                using (TuiTestHost host = TuiCase.SignedIn(180, 55, "/vessels/vsl_demo", stub))
                {
                    AssertTrue(host.PumpUntil(() => host.Tui.Shell.Screen is VesselScreen v && v.Vessel != null), "vessel");
                    VesselScreen screen = (VesselScreen)host.Tui.Shell.Screen!;
                    TuiCase.NotContains(Status(host), "A Apply recommendation", "no Apply key off the doctor panel");
                    GoTo(host, screen, "doctor");
                    AssertTrue(host.WaitForText("BBX-LAND-001"), "doctor row\n" + host.Screen());
                    TuiCase.Contains(Status(host), "A Apply recommendation", "Apply in the hints for an actionable finding");
                    host.Press("A");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/blackbox/bbx_ex/recommendations/rec_1/apply") == 1), "dry run");
                    AssertTrue(stub.LastBody<BlackBoxRecommendationApplyRequest>("POST", "/api/v1/blackbox/bbx_ex/recommendations/rec_1/apply").DryRun, "dry run first");
                    AssertTrue(host.WaitForText("Dry run: nothing has changed yet."), "plan\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "update_vessel", "the action");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/blackbox/bbx_ex/recommendations/rec_1/apply") == 2), "applied");
                    BlackBoxRecommendationApplyRequest run = stub.LastBody<BlackBoxRecommendationApplyRequest>("POST", "/api/v1/blackbox/bbx_ex/recommendations/rec_1/apply");
                    AssertFalse(run.DryRun, "real run");
                    AssertEqual("bbc_rec", run.ConfirmToken, "confirm token");
                    AssertEqual(BlackBoxNoteAuthorKindEnum.Operator, run.AuthorKind, "operator");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Severity == NotificationSeverityEnum.Success && t.Text.Contains("LandingMode"))), "applied toast");
                    Settle(host);
                    host.Press("down");
                    host.Screen();
                    AssertEqual("k2", screen.Doctor.Current?.Key, "advice-only finding");
                    TuiCase.NotContains(Status(host), "A Apply recommendation", "no Apply for advice only");
                    host.Press("A");
                    Settle(host);
                    AssertEqual(2, stub.Log.Count(r => r.Path.EndsWith("/apply", StringComparison.Ordinal)), "advice only never calls apply");
                    host.Press("i");
                    AssertTrue(host.WaitForText("Advice only: change the setting by hand."), "details\n" + host.Screen());
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "doctor_refused_dry_run", "A refused recommendation dry run shows the error and applies nothing", () =>
            {
                StubHttpHandler stub = Test.Shared.Suites.Tui.Build.TuiBuildVesselsSuite.Stub();
                stub.Json("POST", "/api/v1/vessels/vsl_demo/blackbox-doctor/enumerate", "{\"VesselId\":\"vsl_demo\",\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":1,\"TotalRecords\":1,\"Objects\":["
                    + "{\"Key\":\"k1\",\"RuleId\":\"BBX-LAND-001\",\"RecommendationId\":\"rec_1\",\"Setting\":\"LandingMode\",\"Count\":6,\"ExampleEntryId\":\"bbx_ex\",\"Applicable\":true}]}");
                stub.Json("POST", "/api/v1/blackbox/bbx_ex/recommendations/rec_1/apply", "{\"Error\":\"Forbidden\",\"Message\":\"Only an admin may change this vessel.\"}", HttpStatusCode.Forbidden);
                using (TuiTestHost host = TuiCase.SignedIn(180, 55, "/vessels/vsl_demo", stub))
                {
                    AssertTrue(host.PumpUntil(() => host.Tui.Shell.Screen is VesselScreen v && v.Vessel != null), "vessel");
                    VesselScreen screen = (VesselScreen)host.Tui.Shell.Screen!;
                    GoTo(host, screen, "doctor");
                    AssertTrue(host.WaitForText("BBX-LAND-001"), "row");
                    host.Press("A");
                    AssertTrue(host.WaitForText("Recommendation dry run failed"), "error\n" + host.Screen());
                    Settle(host);
                    AssertEqual(1, stub.CountFor("POST", "/api/v1/blackbox/bbx_ex/recommendations/rec_1/apply"), "only the refused dry run");
                }
            }, TestTags.Negative));

            cases.Add(TuiCase.Sync(Suite, "entry_timeline_diff_nearby", "Entry: Timeline is paged oldest first and Enter opens the entity; Diff compares with the previous attempt; Nearby Successes is paged and opens the mission", () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load();
                StubHttpHandler stub = EntryStub(fx);
                using (TuiTestHost host = TuiCase.SignedIn(180, 55, "/blackbox/" + fx.EntryId, stub))
                {
                    BlackBoxEntryScreen screen = Entry(host);
                    GoTo(host, screen, "timeline");
                    AssertTrue(host.WaitForText("InProgress -> LandingFailed"), "timeline row\n" + host.Screen());
                    EnumerationQuery q = stub.LastBody<EnumerationQuery>("POST", "/api/v1/blackbox/" + fx.EntryId + "/timeline/enumerate");
                    AssertEqual(EnumerationOrderEnum.CreatedAscending, q.Order, "oldest first");
                    AssertEqual(25, q.PageSize, "paged");
                    string status = Status(host);
                    TuiCase.Contains(status, "Enter Open", "Enter hint");
                    TuiCase.Contains(status, "< > Page", "paging hint");

                    GoTo(host, screen, "diff");
                    AssertTrue(host.PumpUntil(() => screen.AttemptDiff != null), "default diff loaded");
                    AssertEqual("bbx_prev", screen.CompareWithId, "the previous attempt by default");
                    AssertTrue(host.WaitForText("CaptainId"), "difference row\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "+ src/a.cs", "file only here");
                    TuiCase.Contains(Status(host), "D Compare with another attempt", "D in the diff hints");
                    host.Press("D");
                    AssertTrue(host.WaitForText("Enter an entry id..."), "picker\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "bbx_prev", "attempt listed");
                    host.Press("esc");
                    Settle(host);
                    screen.CompareWith("bbx_missing");
                    AssertTrue(host.WaitForText("Entry bbx_missing was not found."), "404 diff\n" + host.Screen());
                    int before = stub.Log.Count(r => r.Path.Contains("/diff/"));
                    screen.CompareWith(fx.EntryId);
                    Settle(host);
                    AssertEqual(before, stub.Log.Count(r => r.Path.Contains("/diff/")), "an entry is never compared with itself");

                    GoTo(host, screen, "nearby");
                    AssertTrue(host.WaitForText("Landed fine"), "nearby row\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "-2.5 h", "hours from the failure");
                    TuiCase.Contains(Status(host), "Enter Open mission", "nearby hint");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/missions/msn_ok"), "Enter opens the mission");
                }

                using (TuiTestHost host = TuiCase.SignedIn(180, 55, "/blackbox/" + fx.EntryId, stub))
                {
                    BlackBoxEntryScreen screen = Entry(host);
                    GoTo(host, screen, "timeline");
                    AssertTrue(host.WaitForText("InProgress -> LandingFailed"), "timeline");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/missions/" + fx.MissionId), "Enter opens the mission of the first event");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "entry_tabs_empty_and_errors", "No other attempt: the Diff tab says so; a failing timeline shows the error in the grid", () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load();
                StubHttpHandler stub = fx.StubEntry(TuiFixtures.SignedInServer());
                stub.Page("/api/v1/blackbox/" + fx.EntryId + "/objects", "[]");
                stub.Page("/api/v1/missions/" + fx.MissionId + "/blackbox/entries/enumerate", "[" + fx.SummaryJson() + "]");
                stub.Json("POST", "/api/v1/blackbox/" + fx.EntryId + "/timeline/enumerate", "{\"Error\":\"Unavailable\",\"Message\":\"Store offline\"}", HttpStatusCode.ServiceUnavailable);
                using (TuiTestHost host = TuiCase.SignedIn(180, 55, "/blackbox/" + fx.EntryId, stub))
                {
                    BlackBoxEntryScreen screen = Entry(host);
                    GoTo(host, screen, "diff");
                    AssertTrue(host.WaitForText("No other attempt of this mission is recorded"), "no attempts\n" + host.Screen());
                    AssertEqual(0, stub.Log.Count(r => r.Path.Contains("/diff/")), "no diff call");
                    GoTo(host, screen, "timeline");
                    AssertTrue(host.PumpUntil(() => screen.Timeline.State == GridStateEnum.Error), "grid error state\n" + host.Screen());
                }
            }, TestTags.Negative));

            cases.Add(TuiCase.Sync(Suite, "voyage_cascade", "Voyage Cascade: the graph as a tree from the root failure; Enter opens the entry, m the mission; no cascade (404) is clear", () =>
            {
                StubHttpHandler stub = VoyageStub();
                stub.Json("GET", "/api/v1/voyages/vyg_bb/blackbox/cascade", "{\"VoyageId\":\"vyg_bb\",\"VoyageStatus\":\"Failed\",\"RootMissionId\":\"msn_a\",\"RootEntryId\":\"bbx_a\",\"MissionCount\":3,"
                    + "\"Nodes\":[{\"MissionId\":\"msn_c\",\"Title\":\"Third\",\"Status\":\"Cancelled\",\"DependsOnMissionId\":\"msn_b\"},{\"MissionId\":\"msn_b\",\"Title\":\"Second\",\"Status\":\"Cancelled\",\"DependsOnMissionId\":\"msn_a\"},"
                    + "{\"MissionId\":\"msn_a\",\"Title\":\"First\",\"Status\":\"Failed\",\"FailureKind\":\"LandingConflict\",\"EntryId\":\"bbx_a\",\"IsRoot\":true}],"
                    + "\"Edges\":[{\"FromMissionId\":\"msn_a\",\"ToMissionId\":\"msn_b\",\"Kind\":\"CancelledBecause\"},{\"FromMissionId\":\"msn_b\",\"ToMissionId\":\"msn_c\",\"Kind\":\"DependsOn\"}]}");
                using (TuiTestHost host = TuiCase.SignedIn(180, 55, "/voyages/vyg_bb", stub))
                {
                    VoyageScreen screen = Voyage(host);
                    AssertEqual(0, stub.CountFor("GET", "/api/v1/voyages/vyg_bb/blackbox/cascade"), "read only when the tab opens");
                    GoTo(host, screen, "cascade");
                    AssertTrue(host.PumpUntil(() => screen.Cascade.Rows.Count == 3), "three rows\n" + host.Screen());
                    AssertEqual("msn_a,msn_b,msn_c", String.Join(",", screen.Cascade.Rows.Select(r => r.Node.MissionId)), "tree order from the root");
                    AssertEqual("0,1,2", String.Join(",", screen.Cascade.Rows.Select(r => r.Depth)), "depths");
                    AssertTrue(host.WaitForText("CancelledBecause msn_a"), "edge column\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "* First", "root marker");
                    string status = Status(host);
                    TuiCase.Contains(status, "Enter Open entry", "Enter hint");
                    TuiCase.Contains(status, "m Open cascade mission", "m hint");
                    host.Press("down");
                    host.Press("m");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/missions/msn_b"), "m opens the mission");
                }

                using (TuiTestHost host = TuiCase.SignedIn(180, 55, "/voyages/vyg_bb", stub))
                {
                    VoyageScreen screen = Voyage(host);
                    GoTo(host, screen, "cascade");
                    AssertTrue(host.PumpUntil(() => screen.Cascade.Rows.Count == 3), "rows");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/blackbox/bbx_a"), "Enter opens the root entry");
                }

                StubHttpHandler none = VoyageStub();
                none.Json("GET", "/api/v1/voyages/vyg_bb/blackbox/cascade", "{\"Error\":\"NotFound\",\"Message\":\"No black box entries\"}", HttpStatusCode.NotFound);
                using (TuiTestHost host = TuiCase.SignedIn(180, 55, "/voyages/vyg_bb", none))
                {
                    VoyageScreen screen = Voyage(host);
                    GoTo(host, screen, "cascade");
                    AssertTrue(host.WaitForText("No cascade: the voyage has no black box failures."), "404\n" + host.Screen());
                    TuiCase.NotContains(Status(host), "m Open cascade mission", "no mission key without a row");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "recovery_stats_view", "Black Box: R shows the recovery stats (with the vessel filter); none tracked is a toast", () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load();
                StubHttpHandler stub = TuiFixtures.SignedInServer();
                stub.Json("POST", "/api/v1/blackbox/enumerate", StubHttpHandler.PageJson("[" + fx.SummaryJson() + "]"));
                stub.Json("GET", "/api/v1/vessels", "{\"Success\":true,\"Objects\":[],\"TotalRecords\":0}");
                stub.Json("POST", "/api/v1/blackbox/recovery-stats/enumerate", StubHttpHandler.PageJson("[{\"RuleId\":\"BBX-LAND-001\",\"PathId\":\"bbx-land-001.retry\",\"Runs\":4,\"Succeeded\":3,\"Failed\":1,\"SuccessRate\":0.75,\"LastRunUtc\":\"2026-10-20T00:00:00Z\"}]"));
                using (TuiTestHost host = TuiCase.SignedIn(180, 50, "/blackbox", stub))
                {
                    AssertTrue(host.WaitForText(fx.Entry.MissionTitle!), "list\n" + host.Screen());
                    TuiCase.Contains(Status(host), "R Recovery stats", "R in the hints");
                    host.Press("R");
                    AssertTrue(host.WaitForText("Recovery Stats"), "viewer\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "BBX-LAND-001  bbx-land-001.retry  75%", "the path's success rate");
                    BlackBoxRecoveryStatsRequest sent = stub.LastBody<BlackBoxRecoveryStatsRequest>("POST", "/api/v1/blackbox/recovery-stats/enumerate");
                    AssertEqual(BlackBoxScreen.RecoveryStatsPageSize, sent.PageSize, "page size");
                    AssertNull(sent.VesselId, "no vessel filter");
                }

                StubHttpHandler empty = TuiFixtures.SignedInServer();
                empty.Json("POST", "/api/v1/blackbox/enumerate", StubHttpHandler.PageJson("[]"));
                empty.Json("POST", "/api/v1/blackbox/recovery-stats/enumerate", StubHttpHandler.PageJson("[]"));
                using (TuiTestHost host = TuiCase.SignedIn(180, 50, "/blackbox", empty))
                {
                    AssertTrue(host.WaitForText("No failures recorded."), "empty list");
                    host.Press("R");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("No recovery runs"))), "toast");
                    AssertFalse(host.App.Modals.IsActive, "no viewer");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "runbook_linked_recovery_path", "A runbook-linked recovery path shows its runbook on the Failure tab, in the menu and in the dry run plan", () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load();
                fx.FirstPath.RunbookId = "rbk_land";
                fx.FirstPath.RunbookTitle = "Land by hand";
                fx.FirstPath.SuccessRate = 0.5;
                fx.FirstPath.TrackedRuns = 4;
                StubHttpHandler stub = fx.StubMission(TuiFixtures.SignedInServer());
                using (TuiTestHost host = TuiCase.SignedIn(170, 60, "/missions/" + fx.MissionId, stub))
                {
                    AssertTrue(host.PumpUntil(() => host.Tui.Shell.Screen is MissionScreen m && m.Mission != null), "mission\n" + host.Screen());
                    MissionScreen screen = (MissionScreen)host.Tui.Shell.Screen!;
                    for (int i = 0; i < 6 && !ReferenceEquals(screen.CurrentPanel, screen.Failure); i++) host.Press("]");
                    AssertTrue(host.PumpUntil(() => screen.Failure.Entry != null), "entry");
                    AssertTrue(host.WaitForText("Runbook: Land by hand  (rbk_land)"), "runbook on the tab\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "50% of 4 tracked runs succeeded", "success rate");
                    host.Press("r");
                    AssertTrue(host.WaitForText("[runbook: Land by"), "menu label\n" + host.Screen());
                    host.Press("enter");
                    AssertTrue(host.WaitForText("Runs runbook Land by hand (rbk_land)"), "plan\n" + host.Screen());
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "insight_text_helpers", "Cascade tree order survives cycles and siblings; entity routes; no warnings is null", () =>
            {
                BlackBoxCascadeGraph g = new BlackBoxCascadeGraph();
                g.RootMissionId = "msn_r";
                g.Nodes.Add(new BlackBoxCascadeNode { MissionId = "msn_x" });
                g.Nodes.Add(new BlackBoxCascadeNode { MissionId = "msn_y" });
                g.Nodes.Add(new BlackBoxCascadeNode { MissionId = "msn_r", IsRoot = true });
                g.Nodes.Add(new BlackBoxCascadeNode { MissionId = "msn_s" });
                g.Edges.Add(new BlackBoxCascadeEdge { FromMissionId = "msn_x", ToMissionId = "msn_y", Kind = BlackBoxCascadeEdgeKindEnum.DependsOn });
                g.Edges.Add(new BlackBoxCascadeEdge { FromMissionId = "msn_y", ToMissionId = "msn_x", Kind = BlackBoxCascadeEdgeKindEnum.DependsOn });
                g.Edges.Add(new BlackBoxCascadeEdge { FromMissionId = "msn_r", ToMissionId = "msn_s", Kind = BlackBoxCascadeEdgeKindEnum.Sibling });
                g.Edges.Add(new BlackBoxCascadeEdge { FromMissionId = "msn_r", ToMissionId = "msn_gone", Kind = BlackBoxCascadeEdgeKindEnum.BlockedBy });
                List<BlackBoxCascadeRow> rows = BlackBoxInsightText.CascadeRows(g);
                AssertEqual(4, rows.Count, "every node once");
                AssertEqual("msn_r", rows[0].Node.MissionId, "root first");
                AssertTrue(rows.Any(r => r.Node.MissionId == "msn_s" && r.Depth == 0), "a sibling is its own root");
                AssertEqual(0, BlackBoxInsightText.CascadeRows(new BlackBoxCascadeGraph()).Count, "empty graph");
                AssertEqual("/missions/msn_1", BlackBoxInsightText.EntityRoute("msn_1"), "mission");
                AssertEqual("/merge-queue/mrg_1", BlackBoxInsightText.EntityRoute("mrg_1"), "merge entry");
                AssertNull(BlackBoxInsightText.EntityRoute("bbn_1"), "a note has no screen");
                AssertNull(BlackBoxInsightText.EntityRoute(null), "null");
                AssertNull(BlackBoxInsightText.DispatchWarnings(null, new List<BriefLintFinding>()), "no warnings");
                AssertContains("chained after it", BlackBoxInsightText.Overlap(new MissionOverlapWarning { OverlappingMissionId = "msn_1", Serialized = true }), "serialized");
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI black box insights", cases: cases);
        }

        private static StubHttpHandler EntryStub(BlackBoxUiFixture fx)
        {
            StubHttpHandler stub = fx.StubEntry(TuiFixtures.SignedInServer());
            string e = fx.EntryId;
            stub.Page("/api/v1/blackbox/" + e + "/objects", "[]");
            stub.Json("POST", "/api/v1/blackbox/" + e + "/timeline/enumerate", "{\"EntryId\":\"" + e + "\",\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":1,\"TotalRecords\":2,\"Objects\":["
                + "{\"TimestampUtc\":\"2026-10-20T11:00:00Z\",\"Source\":\"Mission\",\"Kind\":\"StatusChanged\",\"Message\":\"InProgress -> LandingFailed\",\"EntityId\":\"" + fx.MissionId + "\"},"
                + "{\"TimestampUtc\":\"2026-10-20T12:05:00Z\",\"Source\":\"Note\",\"Kind\":\"Note\",\"Message\":\"Checked the dock\",\"EntityId\":\"bbn_1\"}]}");
            string previous = "{\"Id\":\"bbx_prev\",\"MissionId\":\"" + fx.MissionId + "\",\"Attempt\":0,\"Phase\":\"Landing\",\"Step\":\"DirtyCheckout\",\"CreatedUtc\":\"2026-10-19T11:00:00Z\"}";
            stub.Page("/api/v1/missions/" + fx.MissionId + "/blackbox/entries/enumerate", "[" + fx.SummaryJson() + "," + previous + "]");
            stub.Json("GET", "/api/v1/blackbox/" + e + "/diff/bbx_prev", "{\"EntryId\":\"" + e + "\",\"OtherEntryId\":\"bbx_prev\",\"SameMission\":true,\"Attempt\":1,\"OtherAttempt\":0,\"SameFingerprint\":true,\"DifferenceCount\":1,"
                + "\"Differences\":[{\"Section\":\"Subject\",\"Field\":\"CaptainId\",\"Value\":\"cpt_a\",\"OtherValue\":\"cpt_b\"}],\"ChangedFilesKnown\":true,\"FilesOnlyInEntry\":[\"src/a.cs\"],\"FilesOnlyInEntryCount\":1,\"FilesInBothCount\":2}");
            stub.Json("GET", "/api/v1/blackbox/" + e + "/diff/bbx_missing", "{\"Error\":\"NotFound\",\"Message\":\"No entry bbx_missing\"}", HttpStatusCode.NotFound);
            stub.Json("POST", "/api/v1/blackbox/" + e + "/nearby-successes/enumerate", StubHttpHandler.PageJson("[{\"SuccessId\":\"bbs_1\",\"MissionId\":\"msn_ok\",\"MissionTitle\":\"Landed fine\",\"LandedUtc\":\"2026-10-20T09:00:00Z\",\"HoursFromFailure\":-2.5,\"OverlappingFileCount\":2,\"BaseCommit\":\"abcdef123456\"}]"));
            stub.Json("GET", "/api/v1/missions/msn_ok", "{\"Id\":\"msn_ok\",\"Title\":\"Landed fine\",\"Status\":\"Complete\"}");
            stub.Json("GET", "/api/v1/missions/" + fx.MissionId, "{\"Id\":\"" + fx.MissionId + "\",\"Title\":\"Fixture\",\"Status\":\"LandingFailed\"}");
            return stub;
        }

        private static StubHttpHandler VoyageStub()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            stub.Json("GET", "/api/v1/voyages/vyg_bb", "{\"Voyage\":{\"Id\":\"vyg_bb\",\"Title\":\"Bounded enumeration\",\"Status\":\"Failed\",\"CreatedUtc\":\"2026-10-20T11:00:00Z\",\"LastUpdateUtc\":\"2026-10-20T12:00:00Z\"},\"TotalMissions\":0,\"MissionCountsByStatus\":{}}");
            stub.Json("GET", "/api/v1/missions", StubHttpHandler.PageJson("[]"));
            stub.Json("GET", "/api/v1/voyages/vyg_bb/blackbox", "{\"Error\":\"NotFound\",\"Message\":\"none\"}", HttpStatusCode.NotFound);
            return stub;
        }

        private static BlackBoxEntryScreen Entry(TuiTestHost host)
        {
            AssertTrue(host.PumpUntil(() => host.Tui.Shell.Screen is BlackBoxEntryScreen s && s.Brief.Entry != null), "entry loaded\n" + host.Screen());
            return (BlackBoxEntryScreen)host.Tui.Shell.Screen!;
        }

        private static VoyageScreen Voyage(TuiTestHost host)
        {
            AssertTrue(host.PumpUntil(() => host.Tui.Shell.Screen is VoyageScreen v && v.Voyage != null), "voyage loaded\n" + host.Screen());
            return (VoyageScreen)host.Tui.Shell.Screen!;
        }

        private static void GoTo(TuiTestHost host, OpsDetailScreen screen, string panel)
        {
            for (int i = 0; i < 12 && screen.CurrentPanelKey != panel; i++) host.Press("]");
            AssertEqual(panel, screen.CurrentPanelKey, "panel " + panel);
            host.Screen();
        }

        private static ScreenBase? Inner(TuiTestHost host)
        {
            ScreenBase? s = host.Tui.Shell.Screen;
            return s is HubScreen hub ? hub.Content : s;
        }

        private static void Settle(TuiTestHost host)
        {
            host.PumpUntil(() => false, 150);
        }

        private static string Status(TuiTestHost host)
        {
            host.Screen();
            string[] lines = host.Screen().Split('\n');
            return lines[host.Height - 1];
        }
    }
}
