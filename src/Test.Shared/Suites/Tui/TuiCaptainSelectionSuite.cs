namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using Armada.Client;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Operations;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The mission screen's Captain Selection tab: summary and comparison rendering, panel keys in the status bar only
    /// while focused, history paging and filtering, opening a historical decision, read-only preview, the administrator
    /// recovery form (revision, reason, conflict), and reload on a matching captain-selection.changed event.
    /// </summary>
    public sealed class TuiCaptainSelectionSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.CaptainSelection";
        private const string MissionId = "msn_select";
        private const string Base = "/api/v1/missions/" + MissionId + "/captain-selection";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "tab_renders_summary_and_comparison", "The tab shows mode, strategy, selected captain, models, recovery and the comparison", () =>
            {
                StubHttpHandler stub = Stub(canManage: false);
                using (TuiTestHost host = TuiCase.SignedIn(180, 60, "/missions/" + MissionId, stub))
                {
                    MissionScreen screen = OpenSelection(host);
                    AssertTrue(host.WaitForText("Compare captains"), "comparison section\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Enforce", "mode");
                    TuiCase.Contains(frame, "Most remaining allowance", "strategy");
                    TuiCase.Contains(frame, "Alpha captain", "selected captain");
                    TuiCase.Contains(frame, "opus-4", "intended model");
                    TuiCase.Contains(frame, "Beta captain / sonnet", "second candidate");
                    TuiCase.Contains(frame, "Fallback allowed", "recovery record");
                    AssertNotNull(screen.Selection.Detail, "decision detail loaded");
                    AssertTrue(stub.Saw("GET", "/api/v1/captain-selection-decisions/csd_current"), "the current attempt is opened");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "keys_only_while_focused_and_recovery_needs_admin", "Selection keys appear only on the tab; a viewer gets no recovery key", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(220, 60, "/missions/" + MissionId, Stub(canManage: false)))
                {
                    AssertTrue(host.PumpUntil(() => host.Tui.Shell.Screen is MissionScreen m && m.Mission != null), "mission loaded");
                    TuiCase.NotContains(Status(host), "h Selection history", "overview: no selection keys");
                    OpenSelection(host);
                    string status = Status(host);
                    foreach (string hint in new[] { "c Compare captains", "h Selection history", "v Preview current selection", "j JSON", "g Refresh" })
                        TuiCase.Contains(status, hint, "hint " + hint);
                    TuiCase.NotContains(status, "a Recovery permissions", "viewers cannot manage recovery");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "history_filter_and_open_decision", "h pages history, f filters by outcome, Enter opens the highlighted decision", () =>
            {
                StubHttpHandler stub = Stub(canManage: false);
                using (TuiTestHost host = TuiCase.SignedIn(180, 60, "/missions/" + MissionId, stub))
                {
                    MissionScreen screen = OpenSelection(host);
                    host.Press("h");
                    AssertTrue(host.PumpUntil(() => screen.Selection.History != null), "history loaded");
                    AssertTrue(host.WaitForText("Selection history"), "history section");
                    TuiCase.Contains(host.Screen(), "Waiting", "waiting history row (no selected captain)");
                    TuiCase.Contains(host.Screen(), "> ", "highlighted row marker");
                    host.Press("f");
                    AssertTrue(host.PumpUntil(() => stub.Saw("GET", Base + "/history", r => r.Query.Contains("outcome=Admitted"))), "filter sent before paging");
                    host.Press("down").Press("enter");
                    AssertTrue(host.PumpUntil(() => stub.Saw("GET", "/api/v1/captain-selection-decisions/csd_old")), "historical decision opened");
                    AssertEqual(MissionSelectionViewEnum.Comparison, screen.Selection.View);
                    AssertTrue(host.WaitForText("Latest retained decision"), "an opened historical decision is labeled as not current");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "preview_is_read_only", "v posts the read-only preview and shows its reason", () =>
            {
                StubHttpHandler stub = Stub(canManage: false);
                using (TuiTestHost host = TuiCase.SignedIn(180, 60, "/missions/" + MissionId, stub))
                {
                    MissionScreen screen = OpenSelection(host);
                    host.Press("v");
                    AssertTrue(host.PumpUntil(() => screen.Selection.Preview != null), "preview loaded");
                    AssertEqual(1, stub.CountFor("POST", Base + "/preview"));
                    AssertTrue(host.WaitForText("Preview current selection"), "preview section");
                    AssertFalse(stub.Saw("POST", Base + "/recovery"), "preview never mutates");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "recovery_form_sends_revision_and_reason", "a opens recovery permissions; save posts the expected revision, flags and reason", () =>
            {
                StubHttpHandler stub = Stub(canManage: true);
                stub.Json("POST", Base + "/recovery", ArmadaJson.Serialize(new UsageSelectionRecovery { Id = "ura_2", MissionId = MissionId, ActorUserId = "usr_admin",
                    Reason = "Operator approved", AllowFallback = true, ReserveException = true, Revision = 4, CreatedUtc = DateTime.UtcNow }));
                using (TuiTestHost host = TuiCase.SignedIn(180, 60, "/missions/" + MissionId, stub))
                {
                    OpenSelection(host);
                    TuiCase.Contains(Status(host), "a Recovery permissions", "administrators see the recovery key");
                    host.Press("a");
                    AssertTrue(host.WaitForText("Allow soft-reserve exception"), "recovery form\n" + host.Screen());
                    host.Press("tab").Press("space");
                    host.Press("tab").Type("Operator approved");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", Base + "/recovery") == 1), "recovery posted");
                    UsageSelectionRecoveryRequest sent = stub.LastBody<UsageSelectionRecoveryRequest>("POST", Base + "/recovery");
                    AssertEqual(3, sent.ExpectedRevision, "optimistic revision of the shown record");
                    AssertTrue(sent.AllowFallback, "existing fallback kept");
                    AssertTrue(sent.ReserveException, "reserve toggled on");
                    AssertEqual("Operator approved", sent.Reason);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "reloads_only_for_matching_selection_event", "A captain-selection.changed event for this mission reloads the tab; others are ignored", () =>
            {
                StubHttpHandler stub = Stub(canManage: false);
                using (TuiTestHost host = TuiCase.SignedIn(180, 60, "/missions/" + MissionId, stub))
                {
                    MissionScreen screen = OpenSelection(host);
                    int before = stub.CountFor("GET", Base);
                    host.Tui.Context.Events.Inject(AskFixtures.EventJson("captain-selection.changed", "{\"missionId\":\"msn_other\",\"outcome\":\"Waiting\"}"));
                    host.Pump();
                    AssertEqual(before, stub.CountFor("GET", Base), "another mission's event is ignored");
                    host.Tui.Context.Events.Inject(AskFixtures.EventJson("captain-selection.changed", "{\"missionId\":\"" + MissionId + "\",\"decisionId\":\"csd_new\",\"outcome\":\"Admitted\"}"));
                    AssertTrue(host.PumpUntil(() => stub.CountFor("GET", Base) > before), "matching event reloads the summary");
                }
            }));

            return new TestSuiteDescriptor(Suite, "TUI captain selection", cases);
        }

        private static StubHttpHandler Stub(bool canManage)
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            stub.Json("GET", "/api/v1/missions/" + MissionId, "{\"Id\":\"" + MissionId + "\",\"Title\":\"Selection fixture\",\"Status\":\"Pending\",\"Priority\":100,"
                + "\"CreatedUtc\":\"2026-11-03T11:00:00Z\",\"LastUpdateUtc\":\"2026-11-03T12:00:00Z\"}");
            CaptainSelectionDecisionView current = Decision("csd_current", "Alpha captain", CaptainSelectionOutcomes.Admitted);
            CaptainSelectionSummary summary = new CaptainSelectionSummary
            {
                MissionId = MissionId, CurrentMode = UsageSelectionModeEnum.Enforce, CurrentStrategy = CaptainSelectionStrategyEnum.MostRemainingAllowance,
                HistoryAvailable = true, CurrentAttempt = current, LatestDecision = current, CanManageRecovery = canManage,
                Recovery = new UsageSelectionRecovery { Id = "ura_1", MissionId = MissionId, ActorUserId = "usr_admin", Reason = "Earlier approval",
                    AllowFallback = true, ReserveException = false, Revision = 3, CreatedUtc = DateTime.Parse("2026-11-03T12:30:00Z").ToUniversalTime() }
            };
            stub.Json("GET", Base, ArmadaJson.Serialize(summary));
            stub.Json("GET", "/api/v1/captain-selection-decisions/csd_current", ArmadaJson.Serialize(Detail(current)));
            CaptainSelectionDecisionView old = Decision("csd_old", "Historical captain", CaptainSelectionOutcomes.Waiting);
            stub.Json("GET", "/api/v1/captain-selection-decisions/csd_old", ArmadaJson.Serialize(Detail(old)));
            EnumerationResult<CaptainSelectionDecisionView> history = EnumerationResult<CaptainSelectionDecisionView>.Create(
                new EnumerationQuery { PageNumber = 1, PageSize = 10 }, new List<CaptainSelectionDecisionView> { current, old }, 2);
            stub.Json("GET", Base + "/history", ArmadaJson.Serialize(history));
            CaptainSelectionPreview preview = new CaptainSelectionPreview { MissionId = MissionId, Mode = UsageSelectionModeEnum.Enforce,
                Strategy = CaptainSelectionStrategyEnum.MostRemainingAllowance, ReasonCode = UsageSelectionReasonCodes.SelectedForHeadroom,
                EvaluatedUtc = DateTime.UtcNow, ActualCaptainId = "cpt_alpha", HypotheticalCaptainId = "cpt_alpha", Candidates = Candidates() };
            stub.Json("POST", Base + "/preview", ArmadaJson.Serialize(preview));
            return stub;
        }

        private static CaptainSelectionDecisionView Decision(string id, string captainName, string outcome)
            => new CaptainSelectionDecisionView
            {
                Id = id, TenantId = "default", MissionId = MissionId, Trigger = "Dispatch", Outcome = outcome,
                Mode = UsageSelectionModeEnum.Enforce, Strategy = CaptainSelectionStrategyEnum.MostRemainingAllowance,
                SelectedCaptainId = outcome == CaptainSelectionOutcomes.Admitted ? "cpt_alpha" : null,
                SelectedCaptainName = outcome == CaptainSelectionOutcomes.Admitted ? captainName : null,
                SelectedCaptainAvailable = true, IntendedModel = "opus-4", ReasonCode = UsageSelectionReasonCodes.SelectedForHeadroom,
                AlgorithmVersion = "usage-bands-v2", PolicyRevisionId = "csp_1", EvaluatedUtc = DateTime.Parse("2026-11-03T12:00:00Z").ToUniversalTime()
            };

        private static CaptainSelectionDecisionDetail Detail(CaptainSelectionDecisionView decision)
            => new CaptainSelectionDecisionDetail { Decision = decision, Policy = new Armada.Core.Settings.UsageSelectionSettings(), Candidates = Candidates() };

        private static EnumerationResult<CaptainSelectionCandidateView> Candidates()
            => EnumerationResult<CaptainSelectionCandidateView>.Create(new EnumerationQuery { PageNumber = 1, PageSize = 10 }, new List<CaptainSelectionCandidateView>
            {
                new CaptainSelectionCandidateView { CaptainId = "cpt_alpha", CaptainName = "Alpha captain", Model = "opus", Rank = 1, CanSelect = true, CaptainAvailable = true,
                    Band = UsageSelectionBandEnum.Comfortable, ReasonCode = UsageSelectionReasonCodes.SufficientHeadroom, AvailablePercent = 58, ActualSelection = true, HypotheticalSelection = true },
                new CaptainSelectionCandidateView { CaptainId = "cpt_beta", CaptainName = "Beta captain", Model = "sonnet", Rank = 2, CanSelect = false, CaptainAvailable = true,
                    Band = UsageSelectionBandEnum.Blocked, ReasonCode = UsageSelectionReasonCodes.BelowReserve, AvailablePercent = 9 }
            }, 2);

        private static MissionScreen OpenSelection(TuiTestHost host)
        {
            AssertTrue(host.PumpUntil(() => host.Tui.Shell.Screen is MissionScreen m && m.Mission != null), "mission loaded\n" + host.Screen());
            MissionScreen screen = (MissionScreen)host.Tui.Shell.Screen!;
            for (int i = 0; i < 8 && !ReferenceEquals(screen.CurrentPanel, screen.Selection); i++) host.Press("]");
            AssertTrue(ReferenceEquals(screen.CurrentPanel, screen.Selection), "selection tab selected");
            AssertTrue(host.PumpUntil(() => screen.Selection.Loaded), "selection panel loaded\n" + host.Screen());
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
