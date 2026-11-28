namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using Armada.Client;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Build;
    using Armada.Tui.Screens.Operations;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The 1.5.0 reliability surfaces in the TUI (RS-12.9): the Model Profiles, Reliability and Battery Runs tabs of
    /// <c>/captains</c>, the model profile page, the mission Attempts panel, and the blocker reason label.
    /// </summary>
    public sealed class TuiReliabilitySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Tui.Reliability";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "model_profiles_tab_lists_profiles", "Model Profiles tab lists profiles with tier and suggestion", () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer();
                stub.Json("GET", "/api/v1/model-profiles", StubHttpHandler.PageJson("[" + ArmadaJson.Serialize(Profile()) + "]"));
                using (TuiTestHost host = TuiCase.SignedIn(180, 50, "/captains?tab=profiles", stub))
                {
                    AssertTrue(host.WaitForText("qwen3.8"), "list\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "Premium", "suggested tier");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "model_profile_page_shows_policy", "The model profile page shows served context, tier suggestion and recovery overrides", () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer();
                stub.Json("GET", "/api/v1/model-profiles/mdl_1", ArmadaJson.Serialize(Profile()));
                using (TuiTestHost host = TuiCase.SignedIn(180, 50, "/model-profiles/mdl_1", stub))
                {
                    AssertTrue(host.WaitForText("Suggested tier"), "page\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "65,536", "served context");
                    TuiCase.Contains(host.Screen(), "Twenty Judge passes", "suggestion reason");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "reliability_tab_shows_rows_and_gate", "Reliability tab shows per model and persona rows and the latest gate report line", () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer();
                ReliabilitySummary summary = new ReliabilitySummary { TotalRecords = 1, TotalPages = 1 };
                summary.Rows.Add(new ReliabilitySummaryRow { Model = "qwen3.8", Persona = "Worker", Attempts = 20, Completed = 17, Failed = 3, Repairs = 2, Escalations = 1, CompletionRate = 0.85 });
                stub.Json("GET", "/api/v1/reliability/summary", ArmadaJson.Serialize(summary));
                GateReport gate = new GateReport { Id = "gate-1", Passed = false, FinishedUtc = DateTime.UtcNow };
                gate.Measures.Add(new GateMeasure { Id = "G1", Name = "Completion", Status = GateMeasureStatusEnum.Fail });
                stub.Json("GET", "/api/v1/reliability/gate-reports/latest", ArmadaJson.Serialize(gate));
                using (TuiTestHost host = TuiCase.SignedIn(180, 50, "/captains?tab=reliability", stub))
                {
                    AssertTrue(host.WaitForText("85%"), "rows\n" + host.Screen());
                    AssertTrue(host.WaitForText("gate-1"), "gate line\n" + host.Screen());
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "battery_runs_tab_lists_runs", "Battery Runs tab lists runs; only queued and running runs can be cancelled", () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer();
                BatteryRun run = new BatteryRun { Status = BatteryRunStatusEnum.Running, CaptainName = "qwen-box", TotalTasks = 8, PassedCount = 3 };
                stub.Json("GET", "/api/v1/battery/runs", StubHttpHandler.PageJson("[" + ArmadaJson.Serialize(run) + "]"));
                using (TuiTestHost host = TuiCase.SignedIn(180, 50, "/captains?tab=battery-runs", stub))
                {
                    AssertTrue(host.WaitForText("qwen-box"), "list\n" + host.Screen());
                }

                AssertTrue(BatteryRunsScreen.IsOpen(run), "running is open");
                AssertFalse(BatteryRunsScreen.IsOpen(new BatteryRun { Status = BatteryRunStatusEnum.Completed }), "completed is not open");
            }));

            cases.Add(TuiCase.Sync(Suite, "sort_keys_map_to_server_sort", "Reliability grid sort keys map to the server sort", () =>
            {
                AssertEqual(ReliabilitySortEnum.CompletionRate, ReliabilityScreen.SortFor("completionRate"), "completion");
                AssertEqual(ReliabilitySortEnum.UnclassifiedRate, ReliabilityScreen.SortFor("unclassifiedRate"), "unclassified");
                AssertEqual(ReliabilitySortEnum.Attempts, ReliabilityScreen.SortFor(null), "default");
            }));

            cases.Add(TuiCase.Sync(Suite, "repair_and_blocker_labels", "Attempt repair labels and blocker reason labels read well", () =>
            {
                AssertEqual("OutputRepair", MissionAttemptsText.Repair(new MissionAttempt { IsRepair = true }), "output repair");
                AssertEqual("", MissionAttemptsText.Repair(new MissionAttempt()), "no repair");
                AssertEqual("Endpoint mission limit", MissionBlockerText.ReasonLabel(MissionAssignmentBlockerReasonEnum.EndpointMissionLimit), "blocker label");
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI reliability surfaces", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static ModelProfile Profile()
        {
            ModelProfile p = new ModelProfile();
            p.Id = "mdl_1";
            p.Identity = new ModelProfileIdentity { Runtime = "ApiEndpoint", Provider = "ollama", Model = "qwen3.8", ServedContextTokens = 65536, ServedContextSource = ContextLengthSourceEnum.Discovered };
            p.Tiering = new ModelProfileTiering { Tier = CaptainTierEnum.Standard, SuggestedTier = CaptainTierEnum.Premium, SuggestedReason = "Twenty Judge passes in a row", SuggestedSamples = 20 };
            p.Recovery = new ModelProfileRecovery { MaxResumes = 2 };
            return p;
        }

        #endregion
    }
}
