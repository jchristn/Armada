namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Admin;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The black box in the TUI (plan 11.2 and 16.9): the mission and voyage Failure tabs rendered from a real failure
    /// fixture, their keys in the focus hints, copy (OSC 52) and export, the recovery flow (dry run, confirm, data-loss
    /// acknowledgement, refusals), Ask Armada, JSON, resolve, pin and note, the empty, error and 404 states, the Black Box
    /// list with its fingerprints view, the entry screen's paged object viewer, and the Storage screen's black box rows
    /// and GC dry run.
    /// </summary>
    public sealed class TuiBlackBoxSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.BlackBox";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "failure_tab_renders_fixture", "The mission Failure tab shows the fixture's status, triage, recovery paths, and user brief", () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load();
                StubHttpHandler stub = fx.StubMission(TuiFixtures.SignedInServer());
                using (TuiTestHost host = TuiCase.SignedIn(160, 60, "/missions/" + fx.MissionId, stub))
                {
                    MissionScreen screen = OpenFailure(host);
                    AssertTrue(host.WaitForText("Recovery Paths"), "recovery section\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, fx.EntryId, "entry id");
                    TuiCase.Contains(frame, "Landing / ", "where");
                    TuiCase.Contains(frame, fx.Entry.Triage.ToString(), "triage");
                    TuiCase.Contains(frame, "1. " + fx.FirstPath.Title, "best recovery path");
                    AssertEqual(fx.UserBrief, screen.Failure.UserBrief, "the user audience brief is shown");
                    AssertTrue(stub.Saw("GET", "/api/v1/blackbox/" + fx.EntryId + "/brief", r => r.Query.Contains("audience=user")), "user brief read");
                    host.Press("/").Type("Brief").Press("enter");
                    TuiCase.Contains(host.Screen(), "Brief", "brief section");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "keys_in_focus_hints", "The Failure tab's keys show in the status bar only while it has focus", () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load();
                using (TuiTestHost host = TuiCase.SignedIn(220, 60, "/missions/" + fx.MissionId, fx.StubMission(TuiFixtures.SignedInServer())))
                {
                    AssertTrue(host.WaitForText(fx.Entry.MissionTitle ?? fx.MissionId), "mission loaded");
                    TuiCase.NotContains(Status(host), "c Copy FIX THIS", "overview: no failure keys");
                    MissionScreen screen = OpenFailure(host);
                    AssertTrue(host.PumpUntil(() => screen.Failure.Entry != null), "entry loaded");
                    string status = Status(host);
                    foreach (string hint in new[] { "c Copy FIX THIS", "x Export", "r Recovery", "a Ask Armada", "j JSON", "s Resolve", "p Pin", "n Note", "o Open entry" })
                        TuiCase.Contains(status, hint, "hint " + hint);
                    TuiCase.Contains(host.Screen(), "c Copy FIX THIS   x Export   r Recovery", "inline key line in the panel");
                    host.Press("[");
                    TuiCase.NotContains(Status(host), "r Recovery", "another panel: r is Restart again");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "copy_and_export", "c copies the FIX THIS brief; x exports it to the prompted path (default blackbox-{id}.md)", () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load();
                string dir = Path.Combine(Path.GetTempPath(), "armada-tui-bb-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                string? previous = Environment.GetEnvironmentVariable("ARMADA_TUI_SAVE_DIR");
                Environment.SetEnvironmentVariable("ARMADA_TUI_SAVE_DIR", dir);
                try
                {
                    using (TuiTestHost host = TuiCase.SignedIn(160, 60, "/missions/" + fx.MissionId, fx.StubMission(TuiFixtures.SignedInServer())))
                    {
                        MissionScreen screen = OpenFailure(host);
                        AssertTrue(host.PumpUntil(() => screen.Failure.Entry != null), "entry loaded");
                        host.Press("c");
                        AssertEqual(fx.AgentBrief, host.Tui.Context.Clipboard.LastCopied, "the agent brief is copied");
                        host.Press("x");
                        AssertTrue(host.WaitForText("File path"), "path prompt");
                        TuiCase.Contains(host.Screen(), "blackbox-" + fx.EntryId + ".md", "default file name");
                        host.Press("ctrl+s");
                        string file = Path.Combine(dir, "blackbox-" + fx.EntryId + ".md");
                        AssertTrue(host.PumpUntil(() => File.Exists(file)), "brief written");
                        AssertEqual(fx.AgentBrief, File.ReadAllText(file), "file content");
                        AssertNull(screen.Failure.ExportTo(Path.Combine(dir, "missing-dir-is-created", "..", "\0bad")), "an invalid path is refused with a toast, not a crash");
                    }
                }
                finally
                {
                    Environment.SetEnvironmentVariable("ARMADA_TUI_SAVE_DIR", previous);
                    try { Directory.Delete(dir, true); } catch (Exception) { }
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "recovery_dry_run_then_run", "r lists recovery paths; choosing one dry runs (Operator), confirm runs it with the confirm token", () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load();
                StubHttpHandler stub = fx.StubMission(TuiFixtures.SignedInServer());
                string route = "/api/v1/blackbox/" + fx.EntryId + "/recovery/" + fx.FirstPath.Id;
                using (TuiTestHost host = TuiCase.SignedIn(160, 60, "/missions/" + fx.MissionId, stub))
                {
                    MissionScreen screen = OpenFailure(host);
                    AssertTrue(host.PumpUntil(() => screen.Failure.Entry != null), "entry loaded");
                    host.Press("r");
                    AssertTrue(host.WaitForText("Recovery Paths"), "menu");
                    AssertFalse(host.Screen().Contains("Restart Mission", StringComparison.Ordinal), "r is recovery here, not Restart");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", route) == 1), "dry run");
                    BlackBoxRecoveryRunRequest dry = stub.LastBody<BlackBoxRecoveryRunRequest>("POST", route);
                    AssertTrue(dry.DryRun, "dry run first");
                    AssertEqual(BlackBoxNoteAuthorKindEnum.Operator, dry.AuthorKind, "operator author");
                    AssertTrue(host.WaitForText("nothing has run yet"), "plan shown\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "Retry the landing", "plan step");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", route) == 2), "confirmed run");
                    BlackBoxRecoveryRunRequest run = stub.LastBody<BlackBoxRecoveryRunRequest>("POST", route);
                    AssertFalse(run.DryRun, "real run");
                    AssertEqual("bbc_token1", run.ConfirmToken, "confirm token from the dry run");
                    AssertFalse(run.AcknowledgeDataLoss, "no data-loss acknowledgement needed");
                    AssertEqual(BlackBoxNoteAuthorKindEnum.Operator, run.AuthorKind, "operator author");
                    AssertTrue(host.WaitForText("Landed."), "result shown\n" + host.Screen());
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "recovery_data_loss_acknowledgement", "A data-loss path asks for the typed acknowledgement; without it nothing runs", () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load(dataLossRisk: true);
                StubHttpHandler stub = fx.StubMission(TuiFixtures.SignedInServer());
                string route = "/api/v1/blackbox/" + fx.EntryId + "/recovery/" + fx.FirstPath.Id;
                using (TuiTestHost host = TuiCase.SignedIn(160, 60, "/missions/" + fx.MissionId, stub))
                {
                    MissionScreen screen = OpenFailure(host);
                    AssertTrue(host.PumpUntil(() => screen.Failure.Entry != null), "entry loaded");
                    TuiCase.Contains(host.Screen(), "data loss risk", "risk shown on the path");
                    host.Press("r").Press("enter");
                    AssertTrue(host.WaitForText("nothing has run yet"), "plan");
                    host.Press("y");
                    AssertTrue(host.WaitForText("Type " + BlackBoxText.DataLossWord), "acknowledgement prompt\n" + host.Screen());
                    host.Press("enter");
                    Settle(host);
                    AssertEqual(1, stub.CountFor("POST", route), "Enter without the word runs nothing");
                    AssertTrue(host.App.Modals.Top is ConfirmDialog, "the prompt stays open");
                    host.Type(BlackBoxText.DataLossWord).Press("enter");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", route) == 2), "run after the acknowledgement");
                    BlackBoxRecoveryRunRequest run = stub.LastBody<BlackBoxRecoveryRunRequest>("POST", route);
                    AssertTrue(run.AcknowledgeDataLoss, "acknowledgeDataLoss sent");
                    AssertEqual("bbc_token1", run.ConfirmToken, "token");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "recovery_refusals", "No paths: a warning; a cancelled confirm and a refused dry run run nothing", () =>
            {
                BlackBoxUiFixture none = BlackBoxUiFixture.Load(withPaths: false);
                using (TuiTestHost host = TuiCase.SignedIn(160, 60, "/missions/" + none.MissionId, none.StubMission(TuiFixtures.SignedInServer())))
                {
                    MissionScreen screen = OpenFailure(host);
                    AssertTrue(host.PumpUntil(() => screen.Failure.Entry != null), "entry loaded");
                    host.Press("r");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Severity == NotificationSeverityEnum.Warning && t.Text.Contains("No recovery paths"))), "warning toast");
                    AssertFalse(host.App.Modals.IsActive, "no menu");
                }

                BlackBoxUiFixture fx = BlackBoxUiFixture.Load();
                StubHttpHandler stub = fx.StubMission(TuiFixtures.SignedInServer());
                string route = "/api/v1/blackbox/" + fx.EntryId + "/recovery/" + fx.FirstPath.Id;
                using (TuiTestHost host = TuiCase.SignedIn(160, 60, "/missions/" + fx.MissionId, stub))
                {
                    MissionScreen screen = OpenFailure(host);
                    AssertTrue(host.PumpUntil(() => screen.Failure.Entry != null), "entry loaded");
                    host.Press("r").Press("enter");
                    AssertTrue(host.WaitForText("nothing has run yet"), "plan");
                    host.Press("n");
                    Settle(host);
                    AssertEqual(1, stub.CountFor("POST", route), "cancelled: only the dry run");

                    stub.Json("POST", route, "{\"Error\":\"Conflict\",\"Code\":\"BlackBoxConfirmationRequired\",\"Message\":\"The mission changed since the dry run.\"}", HttpStatusCode.Conflict);
                    host.Press("r").Press("enter");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", route) == 2), "second dry run");
                    AssertTrue(host.WaitForText("Recovery dry run failed"), "error dialog\n" + host.Screen());
                    Settle(host);
                    AssertEqual(2, stub.CountFor("POST", route), "a refused dry run never runs");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "ask_json_pin_note_resolve", "a pre-fills Ask Armada; j shows JSON; p pins; n adds an Operator note; s resolves", () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load();
                StubHttpHandler stub = fx.StubMission(TuiFixtures.SignedInServer());
                using (TuiTestHost host = TuiCase.SignedIn(160, 60, "/missions/" + fx.MissionId, stub))
                {
                    MissionScreen screen = OpenFailure(host);
                    AssertTrue(host.PumpUntil(() => screen.Failure.Entry != null), "entry loaded");
                    host.Press("j");
                    AssertTrue(host.WaitForText("Black Box: " + fx.EntryId), "json viewer\n" + host.Screen());
                    host.Press("esc");
                    host.Press("p");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/blackbox/" + fx.EntryId + "/pin") == 1), "pin");
                    host.Press("n");
                    AssertTrue(host.WaitForText("Add Note"), "note dialog");
                    host.Type("Checked the dock.");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/blackbox/" + fx.EntryId + "/notes") == 1), "note posted");
                    BlackBoxNoteAddRequest note = stub.LastBody<BlackBoxNoteAddRequest>("POST", "/api/v1/blackbox/" + fx.EntryId + "/notes");
                    AssertEqual("Checked the dock.", note.Body, "note body");
                    AssertEqual(BlackBoxNoteAuthorKindEnum.Operator, note.AuthorKind, "operator note");
                    Settle(host);
                    host.Press("s");
                    AssertTrue(host.WaitForText("Resolve Failure"), "resolve dialog");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/blackbox/" + fx.EntryId + "/resolve") == 1), "resolve posted");
                    AssertEqual(BlackBoxResolutionEnum.Resolved, stub.LastBody<BlackBoxResolveRequest>("POST", "/api/v1/blackbox/" + fx.EntryId + "/resolve").Resolution, "resolved by default");
                    Settle(host);
                    host.Press("a");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Route.ScreenName == "AskScreen"), "Ask opened");
                    string prompt = "Show me the pertinent details from the black box for mission " + fx.MissionId + " and give me your opinion on why it failed and what could have been done differently";
                    AssertTrue(host.PumpUntil(() => host.Tui.Ask.ComposerDraft == prompt), "canonical prompt pre-filled: " + host.Tui.Ask.ComposerDraft);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "empty_error_and_404_states", "No entry (404): a plain note and the screen's own keys; a server error: a retryable message", () =>
            {
                StubHttpHandler stub = TuiOpsMissionDetailSuite.Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 60, "/missions/msn_r", stub))
                {
                    MissionScreen screen = OpenFailure(host);
                    AssertTrue(host.WaitForText("No black box entry for this mission."), "404 state\n" + host.Screen());
                    AssertTrue(screen.Failure.NotFound, "not found");
                    TuiCase.NotContains(Status(host), "c Copy FIX THIS", "no failure keys without an entry");
                    AssertFalse(screen.Failure.HandlePanelKey(TUIKit.Input.KeyEvent.Char('c')), "the panel does not claim keys without an entry");
                }

                StubHttpHandler failing = TuiOpsMissionDetailSuite.Stub();
                failing.Json("GET", "/api/v1/missions/msn_r/blackbox", "{\"Error\":\"Unavailable\",\"Code\":\"BlackBoxStoreUnavailable\",\"Message\":\"Store offline\"}", HttpStatusCode.ServiceUnavailable);
                using (TuiTestHost host = TuiCase.SignedIn(160, 60, "/missions/msn_r", failing))
                {
                    MissionScreen screen = OpenFailure(host);
                    AssertTrue(host.WaitForText("The black box could not be loaded"), "error state\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "F5 Retry", "retry hint");
                    AssertFalse(screen.Failure.NotFound, "an error is not a 404");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "voyage_failure_tab", "The voyage Failure tab reads the voyage entry and shows its brief", () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load("dep-001-parallel-dependency-mission-2", "bbx_vy1");
                StubHttpHandler stub = fx.StubEntry(TuiFixtures.SignedInServer());
                stub.Json("GET", "/api/v1/voyages/vyg_bb", "{\"Voyage\":{\"Id\":\"vyg_bb\",\"Title\":\"Bounded enumeration\",\"Status\":\"Failed\",\"LastBlackBoxEntryId\":\"bbx_vy1\",\"CreatedUtc\":\"2026-10-20T11:00:00Z\",\"LastUpdateUtc\":\"2026-10-20T12:00:00Z\"},\"TotalMissions\":0,\"MissionCountsByStatus\":{}}");
                stub.Json("GET", "/api/v1/voyages/vyg_bb/blackbox", fx.EntryJson());
                stub.Json("GET", "/api/v1/missions", StubHttpHandler.PageJson("[]"));
                using (TuiTestHost host = TuiCase.SignedIn(160, 60, "/voyages/vyg_bb", stub))
                {
                    AssertTrue(host.PumpUntil(() => host.Tui.Shell.Screen is VoyageScreen v && v.Voyage != null), "voyage loaded\n" + host.Screen());
                    VoyageScreen screen = (VoyageScreen)host.Tui.Shell.Screen!;
                    for (int i = 0; i < 4 && !ReferenceEquals(screen.CurrentPanel, screen.Failure); i++) host.Press("]");
                    AssertTrue(ReferenceEquals(screen.CurrentPanel, screen.Failure), "failure tab");
                    AssertTrue(host.PumpUntil(() => screen.Failure.Entry != null), "entry loaded");
                    AssertTrue(stub.CountFor("GET", "/api/v1/voyages/vyg_bb/blackbox") >= 1, "voyage route");
                    TuiCase.Contains(Status(host), "x Export", "x is export here, not Cancel Voyage");
                    host.Press("c");
                    AssertEqual(fx.AgentBrief, host.Tui.Context.Clipboard.LastCopied, "copy");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "list_filters_and_fingerprints", "Black Box lists entries with server filters; F opens fingerprints and choosing one filters the list", () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load();
                StubHttpHandler stub = TuiFixtures.SignedInServer();
                stub.Json("POST", "/api/v1/blackbox/enumerate", StubHttpHandler.PageJson("[" + fx.SummaryJson() + "]"));
                stub.Json("POST", "/api/v1/blackbox/fingerprints/enumerate", StubHttpHandler.PageJson("[{\"Fingerprint\":\"" + fx.Entry.Fingerprint + "\",\"Count\":3,\"OpenCount\":2,\"Phase\":\"Landing\",\"Step\":\"DirtyCheckout\",\"FirstSeenUtc\":\"2026-10-01T00:00:00Z\",\"LastSeenUtc\":\"2026-10-20T00:00:00Z\"}]"));
                stub.Json("GET", "/api/v1/vessels", "{\"Success\":true,\"Objects\":[],\"TotalRecords\":0}");
                using (TuiTestHost host = TuiCase.SignedIn(180, 50, "/blackbox", stub))
                {
                    AssertTrue(host.WaitForText(fx.Entry.MissionTitle!), "row\n" + host.Screen());
                    AssertEqual("/missions?tab=blackbox", host.Tui.Context.Router.Current!.FullPath, "/blackbox redirects to the Missions Black Box tab");
                    BlackBoxScreen screen = (BlackBoxScreen)((Armada.Tui.Screens.HubScreen)host.Tui.Shell.Screen!).Content;
                    BlackBoxEnumerateRequest first = stub.LastBody<BlackBoxEnumerateRequest>("POST", "/api/v1/blackbox/enumerate");
                    AssertNull(first.Fingerprint, "no fingerprint filter yet");
                    TuiCase.Contains(Status(host), "F Fingerprints", "fingerprints key in the hints");
                    host.Press("F");
                    AssertTrue(host.WaitForText("3x  Landing / DirtyCheckout"), "fingerprints view\n" + host.Screen());
                    int before = stub.CountFor("POST", "/api/v1/blackbox/enumerate");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/blackbox/enumerate") > before), "refetched");
                    AssertTrue(host.PumpUntil(() => stub.LastBody<BlackBoxEnumerateRequest>("POST", "/api/v1/blackbox/enumerate").Fingerprint == fx.Entry.Fingerprint), "fingerprint filter sent");
                    AssertEqual(EnumerationOrderEnum.CreatedDescending, screen.BuildRequest(2, 25).Order, "newest first");
                    AssertEqual(2, screen.BuildRequest(2, 25).PageNumber, "server paging");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/blackbox/" + fx.EntryId), "Enter opens the entry");
                }

                StubHttpHandler empty = TuiFixtures.SignedInServer();
                empty.Json("POST", "/api/v1/blackbox/enumerate", StubHttpHandler.PageJson("[]"));
                using (TuiTestHost host = TuiCase.SignedIn(180, 50, Armada.Tui.Routing.TabLinks.BlackBoxPath, empty))
                {
                    AssertTrue(host.WaitForText("No failures recorded."), "empty state\n" + host.Screen());
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "entry_screen_paged_objects", "The entry screen lists objects; the viewer reads 64 KB chunks with ] and [; 404 and missing objects are clear", () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load();
                StubHttpHandler stub = fx.StubEntry(TuiFixtures.SignedInServer());
                stub.Json("GET", "/api/v1/blackbox/" + fx.EntryId + "/objects", StubHttpHandler.PageJson("[{\"Name\":\"git-status.txt\",\"ContentType\":\"text/plain\",\"ContentEncoding\":\"identity\",\"SizeBytes\":100000,\"UncompressedBytes\":100000,\"IsMissing\":false},{\"Name\":\"gone.txt\",\"ContentType\":\"text/plain\",\"ContentEncoding\":\"identity\",\"SizeBytes\":5,\"IsMissing\":true}]"));
                stub.On("GET", "/api/v1/blackbox/" + fx.EntryId + "/objects/git-status.txt?offset=0&length=65536", b => Chunk("first chunk M src/a.cs", 0, 100000, 65536));
                stub.On("GET", "/api/v1/blackbox/" + fx.EntryId + "/objects/git-status.txt?offset=65536&length=65536", b => Chunk("second chunk tail", 65536, 100000, null));
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/blackbox/" + fx.EntryId, stub))
                {
                    AssertTrue(host.PumpUntil(() => host.Tui.Shell.Screen is BlackBoxEntryScreen s && s.Brief.Entry != null), "entry loaded\n" + host.Screen());
                    BlackBoxEntryScreen screen = (BlackBoxEntryScreen)host.Tui.Shell.Screen!;
                    TuiCase.NotContains(Status(host), "o Open entry", "no Open entry on the entry's own screen");
                    host.Press("]");
                    AssertTrue(host.WaitForText("git-status.txt"), "objects\n" + host.Screen());
                    host.Press("enter");
                    AssertTrue(host.WaitForText("first chunk M src/a.cs"), "first chunk\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "1-22 / 100,000 B", "range in the footer");
                    host.Press("]");
                    AssertTrue(host.WaitForText("second chunk tail"), "next chunk\n" + host.Screen());
                    host.Press("[");
                    AssertTrue(host.WaitForText("first chunk M src/a.cs"), "previous chunk");
                    host.Press("esc");
                    host.Press("down");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("BlackBoxObjectMissing"))), "missing object toast");
                    host.Press("]");
                    AssertTrue(host.WaitForText("Checked the dock by hand."), "notes\n" + host.Screen());
                    AssertEqual(1, screen.NoteRows.Count, "notes read");
                }

                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/blackbox/bbx_missing", TuiFixtures.SignedInServer()))
                {
                    AssertTrue(host.WaitForText("Black box entry not found."), "404 entry\n" + host.Screen());
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "storage_black_box_rows_and_gc_dry_run", "Storage shows the black box target, usage, retention, and GC; G runs a GC dry run", () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer();
                stub.Json("GET", "/api/v1/storage", ArmadaJson.Serialize(new Armada.Core.Storage.StorageSnapshot { HostCount = 0 }));
                stub.Json("POST", "/api/v1/storage/hosts/enumerate", StubHttpHandler.PageJson("[]"));
                stub.Json("GET", "/api/v1/blackbox/retention", "{\"Policy\":{\"RetentionDays\":30,\"KeepLatestPerMission\":true},\"TotalEntries\":12,\"TotalBytes\":4096,\"PinnedEntries\":1}");
                stub.Json("GET", "/api/v1/blackbox/storage", "{\"Target\":\"Database\",\"Health\":{\"Target\":\"Database\",\"Healthy\":true,\"LatencyMs\":3},\"Usage\":[{\"Target\":\"Database\",\"Entries\":12,\"Bytes\":4096}]}");
                stub.Json("POST", "/api/v1/blackbox/gc", "{\"DryRun\":true,\"Expired\":4,\"Evicted\":1,\"BytesFreed\":2048}");
                using (TuiTestHost host = new TuiTestHost(160, 50, stub, "http://127.0.0.1:9", options => { options.Token = "tok"; options.StartRoute = "/storage"; }))
                {
                    host.Start();
                    AssertTrue(host.PumpUntil(() => Storage(host)?.BlackBox != null), "black box status read: " + String.Join(", ", stub.Requests));
                    AssertTrue(host.WaitForText("Retention (days)"), "retention row: " + Storage(host)!.BlackBox!.Error + "\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "Storage target", "target row");
                    TuiCase.Contains(host.Screen(), "Healthy", "health");
                    TuiCase.Contains(Status(host), "G Black box GC dry run", "GC key in the hints");
                    host.Press("G");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/blackbox/gc") == 1), "gc call");
                    AssertTrue(stub.LastBody<BlackBoxGcRunRequest>("POST", "/api/v1/blackbox/gc").DryRun, "dry run only");
                    AssertTrue(host.WaitForText("Dry run: nothing was deleted."), "report\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "Expired: 4", "expired count");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "settings_black_box_retention", "Settings shows Black Box Retention and saves the whole black box object with only retention changed", () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer();
                stub.Json("GET", "/api/v1/status/health", "{\"Status\":\"healthy\",\"Version\":\"1.2.0\",\"Ports\":{\"Admiral\":7890,\"Mcp\":7891}}");
                string settings = "{\"AdmiralPort\":7890,\"McpPort\":7891,\"MaxCaptains\":4,\"BlackBox\":{\"Enabled\":true,\"Storage\":{\"Target\":\"Disk\",\"S3\":{\"SecretKey\":\"********\"}},"
                    + "\"Retention\":{\"RetentionDays\":180,\"ResolvedRetentionDays\":30,\"MaxEntriesPerVessel\":1000,\"MaxTotalBytes\":1073741824,\"GcIntervalMinutes\":60,\"GcBatchSize\":250,\"KeepLatestPerMission\":true,\"DeleteWithMission\":false,\"DeleteWithVessel\":true}}}";
                stub.Json("GET", "/api/v1/settings", settings);
                stub.On("PUT", "/api/v1/settings", body => StubHttpHandler.Response(HttpStatusCode.OK, settings));
                stub.Json("GET", "/api/v1/vessels", "{\"Objects\":[],\"TotalRecords\":0}");
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/server?tab=server", stub))
                {
                    ServerSettingsScreen? screen = null;
                    AssertTrue(host.PumpUntil(() =>
                    {
                        Armada.Tui.Screens.ScreenBase? sb = host.Tui.Shell.Screen;
                        if (sb is Armada.Tui.Screens.HubScreen hub) sb = hub.Content;
                        screen = sb as ServerSettingsScreen;
                        return screen != null && screen.Settings != null;
                    }), "settings screen");
                    host.Pump();
                    AssertTrue(screen!.Form.Rows.Any(r => r.IsSection && r.Label == "Black Box Retention"), "section");
                    AssertEqual("180", screen.BlackBoxRetentionDays.Value, "retention days");
                    AssertEqual("1024", screen.BlackBoxMaxTotalMegabytes.Value, "megabytes");
                    AssertTrue(screen.BlackBoxDeleteWithVessel.Value, "delete with vessel");
                    screen.Form.Scope.Focus(screen.BlackBoxRetentionDays);
                    host.Press("ctrl+u").Type("5000");
                    host.Press("ctrl+s");
                    host.Pump();
                    AssertEqual(0, stub.CountFor("PUT", "/api/v1/settings"), "an out-of-range value blocks the save");
                    host.Press("ctrl+u").Type("90");
                    host.Screen();
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("PUT", "/api/v1/settings") == 1), "PUT");
                    SettingsData body = stub.LastBody<SettingsData>("PUT", "/api/v1/settings");
                    AssertNotNull(body.BlackBox, "black box sent");
                    AssertEqual(90, body.BlackBox!.Retention.RetentionDays, "edited");
                    AssertEqual(250, body.BlackBox.Retention.GcBatchSize, "unedited retention kept");
                    AssertEqual(BlackBoxStorageTargetEnum.Disk, body.BlackBox.Storage.Target, "storage kept");
                    AssertNull(body.Retention, "only the black box group");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "navigation_and_prompt", "Black Box is a Missions tab with a Go command (g b) and routes; the Ask prompt says voyage for a voyage id", () =>
            {
                AssertFalse(Armada.Tui.Routing.NavCatalog.AllItems().Any(i => i.To == "/blackbox"), "no sidebar item");
                AssertTrue(Armada.Tui.Routing.NavCatalog.Shortcuts.Any(s => s.Item.To == Armada.Tui.Routing.TabLinks.BlackBoxPath && s.Item.GoKey == "b"), "shortcut with go key g b");
                AssertEqual("/missions", Armada.Tui.Routing.NavCatalog.ItemForPath("/blackbox/bbx_1")?.To, "Missions owns the entry page");
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/inbox"))
                {
                    Armada.Tui.Input.ArmadaCommand? go = host.Tui.Context.Commands.All().FirstOrDefault(c => c.Id == "go.blackbox");
                    AssertNotNull(go, "go command");
                    go!.Handler();
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.FullPath == "/missions?tab=blackbox"), "go.blackbox opens the Missions Black Box tab");
                    AssertEqual("blackbox", host.Tui.Context.Router.Current!.Tab?.Key, "black box tab");
                    AssertTrue(host.Tui.Context.Commands.All().First(c => c.Id == "go.blackbox").Gestures.Count > 0, "g b kept");
                }
                AssertNotNull(Armada.Tui.Routing.RouteTable.Find("/blackbox/:id"), "entry route");
                TuiCase.Contains(BlackBoxText.AskPrompt("vyg_1"), "for voyage vyg_1", "voyage prompt");
                TuiCase.Contains(BlackBoxText.AskPrompt("msn_1"), "for mission msn_1", "mission prompt");
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI black box", cases: cases);
        }

        private static MissionScreen OpenFailure(TuiTestHost host)
        {
            AssertTrue(host.PumpUntil(() => host.Tui.Shell.Screen is MissionScreen m && m.Mission != null), "mission loaded\n" + host.Screen());
            MissionScreen screen = (MissionScreen)host.Tui.Shell.Screen!;
            for (int i = 0; i < 6 && !ReferenceEquals(screen.CurrentPanel, screen.Failure); i++) host.Press("]");
            AssertTrue(ReferenceEquals(screen.CurrentPanel, screen.Failure), "failure tab selected");
            AssertTrue(host.PumpUntil(() => screen.Failure.Loaded), "failure panel loaded");
            host.Screen();
            return screen;
        }

        private static System.Net.Http.HttpResponseMessage Chunk(string text, long offset, long total, long? next)
        {
            System.Net.Http.HttpResponseMessage r = new System.Net.Http.HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new System.Net.Http.StringContent(text, System.Text.Encoding.UTF8, "text/plain");
            r.Headers.TryAddWithoutValidation("X-BlackBox-Offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture));
            r.Headers.TryAddWithoutValidation("X-BlackBox-Total-Bytes", total.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (next.HasValue) r.Headers.TryAddWithoutValidation("X-BlackBox-Next-Offset", next.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return r;
        }

        private static StorageScreen? Storage(TuiTestHost host)
        {
            if (host.Tui.Shell.Screen is StorageScreen direct) return direct;
            return (host.Tui.Shell.Screen as Armada.Tui.Screens.HubScreen)?.Content as StorageScreen;
        }

        private static void Settle(TuiTestHost host)
        {
            host.PumpUntil(() => false, 150);
        }

        private static string Status(TuiTestHost host)
        {
            string[] lines = host.Screen().Split('\n');
            return lines[host.Height - 1];
        }
    }
}
