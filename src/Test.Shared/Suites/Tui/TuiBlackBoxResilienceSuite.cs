namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Client;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Operations;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The Armada resilience section in the TUI: the Failure tab and the entry screen show it in handoff and landing scope
    /// (self-healable verdict, detection gap, severity, code pointers, proposed changes, regression test) and a short
    /// reason outside it, a suspected Armada defect is marked, <c>A</c> drafts an Armada fix mission (listed in the status
    /// bar and the action menu) by opening Dispatch prefilled on the Armada vessel, and the resilience gaps view
    /// (<c>G</c> on the Black Box tab) pages gaps and opens the newest entry.
    /// </summary>
    public sealed class TuiBlackBoxResilienceSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.BlackBoxResilience";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "section_in_scope", "The Failure tab shows the Armada resilience section of an in-scope failure, and A is listed while it has focus", () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load("land-002-dock-deleted", "bbx_res1");
                using (TuiTestHost host = TuiCase.SignedIn(240, 90, "/missions/" + fx.MissionId, Stub(fx)))
                {
                    MissionScreen screen = OpenFailure(host);
                    AssertTrue(host.PumpUntil(() => screen.Failure.Entry != null), "entry loaded");
                    AssertTrue(host.WaitForText("Armada resilience"), "section\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Self-healable", "self-healable field");
                    TuiCase.Contains(frame, "Armada could have recreated the worktree", "what Armada could have done");
                    TuiCase.Contains(frame, "Detection gap", "detection gap");
                    TuiCase.Contains(frame, "Proposed Armada changes", "proposed changes");
                    TuiCase.Contains(frame, "1. Recreate a missing dock worktree from the branch", "top change");
                    TuiCase.Contains(frame, "(A Draft Armada fix mission)", "the draft key next to the proposals");
                    TuiCase.Contains(Status(host), "A Draft Armada fix", "draft key in the focus hints");
                    host.Press("[");
                    TuiCase.NotContains(Status(host), "A Draft Armada fix", "another panel: no draft key");
                    AssertTrue(screen.Actions.Any(a => a.Id == "failure-armada-fix" && a.Label == "Draft Armada fix mission"), "the action menu and palette list it");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "section_details_text", "The section renders scope, severity with recurrence, code pointers at the build commit, the regression test and attempted recoveries", () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load("land-002-dock-deleted", "bbx_res2");
                BlackBoxResilience res = fx.Entry.Record!.Analysis!.Resilience!;
                res.ArmadaCommit = "abc1234";
                res.AttemptedRecoveries.Add("LandingRetry x2 (Expired)");
                OpsDocument doc = new OpsDocument(Armada.Tui.Theming.ThemePalettes.Dark(), new Armada.Tui.Services.LocalizationService());
                BlackBoxResilienceText.Append(doc, fx.Entry, "A");
                string text = String.Join("\n", doc.Lines.Select(l => l.ToPlainString()));
                TuiCase.Contains(text, "Armada handoff and landing", "scope");
                TuiCase.Contains(text, "seen 1 time in 30 days", "recurrence");
                TuiCase.Contains(text, "commit abc1234", "build commit");
                TuiCase.Contains(text, "Regression test", "regression test");
                TuiCase.Contains(text, "LandingRetry x2 (Expired)", "attempted recoveries");
                AssertTrue(res.CodePointers.All(p => text.Contains(p, StringComparison.Ordinal)), "every code pointer");
            }));

            cases.Add(TuiCase.Sync(Suite, "suspected_and_out_of_scope", "A suspected ArmadaDefect is marked; outside scope the section is the short reason and A is not offered", () =>
            {
                BlackBoxUiFixture suspected = BlackBoxUiFixture.Load("land-002-dock-deleted", "bbx_res3");
                suspected.Entry.Record!.Analysis!.Triage!.Suspected = true;
                suspected.Entry.Record.Analysis.Triage.Verdict = BlackBoxTriageEnum.ArmadaDefect;
                suspected.Entry.Triage = BlackBoxTriageEnum.ArmadaDefect;
                using (TuiTestHost host = TuiCase.SignedIn(240, 90, "/missions/" + suspected.MissionId, Stub(suspected)))
                {
                    MissionScreen screen = OpenFailure(host);
                    AssertTrue(host.PumpUntil(() => screen.Failure.Entry != null), "entry loaded");
                    AssertTrue(host.WaitForText("suspected Armada defect"), "suspected marker on the triage\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "Suspected Armada defect: only the handoff and landing prior voted ArmadaDefect.", "suspected line in the section");
                }

                BlackBoxUiFixture outside = BlackBoxUiFixture.Load("dep-001-parallel-dependency-mission-2", "bbx_res4");
                BlackBoxResilience res = outside.Entry.Record!.Analysis!.Resilience!;
                res.InScope = false;
                res.ProposedChanges.Clear();
                res.NoChangeReason = "No Armada change suggested: the failure is outside Armada's handoff and landing (phase Completion, step CaptainBlocked) and the triage is MissionBrief.";
                using (TuiTestHost host = TuiCase.SignedIn(240, 90, "/missions/" + outside.MissionId, Stub(outside)))
                {
                    MissionScreen screen = OpenFailure(host);
                    AssertTrue(host.PumpUntil(() => screen.Failure.Entry != null), "entry loaded");
                    AssertTrue(host.WaitForText("No Armada change suggested"), "short reason\n" + host.Screen());
                    string frame = host.Screen();
                    int start = frame.IndexOf("Armada resilience", StringComparison.Ordinal);
                    int end = frame.IndexOf("Recovery Paths", start, StringComparison.Ordinal);
                    AssertTrue(start >= 0 && end > start, "section bounded by Recovery Paths");
                    TuiCase.NotContains(frame.Substring(start, end - start), "Self-healable", "no full section outside scope");
                    TuiCase.NotContains(Status(host), "A Draft Armada fix", "no draft key without a proposed change");
                    AssertFalse(screen.Failure.DraftArmadaFix(), "drafting without a proposed change only warns");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "draft_armada_fix", "A opens Dispatch prefilled with the top change's mission brief on the Armada vessel", () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load("land-002-dock-deleted", "bbx_res5");
                BlackBoxProposedChange top = fx.Entry.Record!.Analysis!.Resilience!.ProposedChanges.OrderBy(c => c.Rank).First();
                using (TuiTestHost host = TuiCase.SignedIn(240, 90, "/missions/" + fx.MissionId, Stub(fx)))
                {
                    MissionScreen screen = OpenFailure(host);
                    AssertTrue(host.PumpUntil(() => screen.Failure.Entry != null), "entry loaded");
                    host.Press("A");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/dispatch"), "Dispatch opened: " + host.Tui.Context.Router.Current!.FullPath);
                    Dictionary<string, string> q = new Dictionary<string, string>(host.Tui.Context.Router.Current!.Query, StringComparer.Ordinal);
                    AssertEqual(OpsHandoff.FromBlackBox, q["from"], "handoff source");
                    AssertEqual("vsl_armada", q["vesselId"], "the Armada vessel");
                    AssertEqual(top.Title, q["voyageTitle"], "title");
                    AssertEqual(top.MissionBrief, q["prompt"], "the mission brief");
                    AssertTrue(host.WaitForText("Prefilled with a proposed Armada fix from the black box"), "banner\n" + host.Screen());
                }

                AssertEqual("vsl_b", BlackBoxResilienceText.ArmadaVesselId(new List<Vessel> { new Vessel { Id = "vsl_a", Name = "App", RepoUrl = "https://github.com/o/app" }, new Vessel { Id = "vsl_b", Name = "Core", RepoUrl = "https://github.com/o/Armada.git" } }), "found by repository");
                AssertNull(BlackBoxResilienceText.ArmadaVesselId(new List<Vessel> { new Vessel { Id = "vsl_a", Name = "App", RepoUrl = "https://github.com/o/app" } }), "no Armada vessel");
                AssertNull(BlackBoxResilienceText.ArmadaVesselId(null), "no vessels");
            }));

            cases.Add(TuiCase.Sync(Suite, "entry_screen_section", "The entry screen's Brief tab shows the section too", () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load("land-002-dock-deleted", "bbx_res6");
                StubHttpHandler stub = fx.StubEntry(TuiFixtures.SignedInServer());
                stub.Json("GET", "/api/v1/vessels", VesselsJson());
                using (TuiTestHost host = TuiCase.SignedIn(240, 90, "/blackbox/" + fx.EntryId, stub))
                {
                    AssertTrue(host.PumpUntil(() => host.Tui.Shell.Screen is BlackBoxEntryScreen s && s.Brief.Entry != null), "entry loaded\n" + host.Screen());
                    AssertTrue(host.WaitForText("Armada resilience"), "section\n" + host.Screen());
                    TuiCase.Contains(Status(host), "A Draft Armada fix", "draft key on the entry screen");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "gaps_view", "G on the Black Box tab opens the resilience gaps view: server paged (at most 50), filters sent, Enter opens the newest entry", () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load("land-002-dock-deleted", "bbx_res7");
                StubHttpHandler stub = TuiFixtures.SignedInServer();
                stub.Json("POST", "/api/v1/blackbox/enumerate", StubHttpHandler.PageJson("[" + fx.SummaryJson() + "]"));
                stub.Json("GET", "/api/v1/vessels", VesselsJson());
                stub.Json("POST", "/api/v1/blackbox/resilience-gaps/enumerate", StubHttpHandler.PageJson("[" + GapJson(fx) + "]"));
                fx.StubEntry(stub);
                using (TuiTestHost host = TuiCase.SignedIn(220, 60, "/blackbox", stub))
                {
                    AssertTrue(host.WaitForText(fx.Entry.MissionTitle!), "list\n" + host.Screen());
                    TuiCase.Contains(Status(host), "G Armada resilience gaps", "gaps key in the hints");
                    host.Press("G");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/blackbox/gaps"), "gaps route");
                    AssertTrue(host.WaitForText("Recreate a missing dock worktree"), "gap row\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Critical", "severity");
                    TuiCase.Contains(frame, "4x", "occurrences");
                    BlackBoxResilienceGapRequest sent = stub.LastBody<BlackBoxResilienceGapRequest>("POST", "/api/v1/blackbox/resilience-gaps/enumerate");
                    AssertTrue(sent.PageSize >= 1 && sent.PageSize <= 50, "page size within the server cap: " + sent.PageSize);
                    AssertEqual(1, sent.PageNumber, "first page");
                    BlackBoxGapsScreen screen = (BlackBoxGapsScreen)host.Tui.Shell.Screen!;
                    AssertEqual(50, screen.BuildRequest(3, 500).PageSize, "a larger request is clamped to 50");
                    AssertEqual(3, screen.BuildRequest(3, 25).PageNumber, "server paging");
                    TuiCase.Contains(screen.DetailsText(ArmadaJson.Deserialize<BlackBoxResilienceGap>(GapJson(fx))!), "Acceptance test", "details carry the acceptance test");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/blackbox/" + fx.EntryId), "Enter opens the newest entry");
                }

                StubHttpHandler empty = TuiFixtures.SignedInServer();
                empty.Json("POST", "/api/v1/blackbox/resilience-gaps/enumerate", StubHttpHandler.PageJson("[]"));
                empty.Json("GET", "/api/v1/vessels", VesselsJson());
                using (TuiTestHost host = TuiCase.SignedIn(180, 50, "/blackbox/gaps", empty))
                {
                    AssertTrue(host.WaitForText("No Armada resilience gaps recorded."), "empty state\n" + host.Screen());
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI black box resilience", cases: cases);
        }

        private static StubHttpHandler Stub(BlackBoxUiFixture fx)
        {
            StubHttpHandler stub = fx.StubMission(TuiFixtures.SignedInServer());
            stub.Json("GET", "/api/v1/vessels", VesselsJson());
            return stub;
        }

        private static string VesselsJson()
        {
            return "{\"Success\":true,\"Objects\":[{\"Id\":\"vsl_app\",\"Name\":\"App\",\"RepoUrl\":\"https://github.com/o/app\"},{\"Id\":\"vsl_armada\",\"Name\":\"Armada\",\"RepoUrl\":\"https://github.com/o/Armada.git\"}],\"TotalRecords\":2}";
        }

        private static string GapJson(BlackBoxUiFixture fx)
        {
            BlackBoxProposedChange top = fx.Entry.Record!.Analysis!.Resilience!.ProposedChanges.OrderBy(c => c.Rank).First();
            BlackBoxResilienceGap gap = new BlackBoxResilienceGap
            {
                Fingerprint = "fp_gap1",
                Phase = BlackBoxPhaseEnum.Landing,
                Step = BlackBoxStepEnum.TargetIntegration,
                Occurrences = 4,
                OpenCount = 3,
                FirstSeenUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                LastSeenUtc = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc),
                LatestEntryId = fx.EntryId,
                LatestMissionId = fx.MissionId,
                Severity = BlackBoxSeverityEnum.Critical,
                SelfHealable = BlackBoxSelfHealableEnum.Yes,
                TopProposedChange = new BlackBoxProposedChange { Rank = 1, Title = top.Title, Change = top.Change, AcceptanceTest = top.AcceptanceTest, Severity = BlackBoxSeverityEnum.Critical },
                DetectionGap = "The dock was reclaimed while the mission still had to land."
            };
            gap.ArmadaVersions.Add("1.1.0");
            gap.ArmadaVersions.Add("1.2.0");
            return ArmadaJson.Serialize(gap);
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

        private static string Status(TuiTestHost host)
        {
            string[] lines = host.Screen().Split('\n');
            return lines[host.Height - 1];
        }
    }
}
