namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Armada.Client contract for pipeline reliability (RS-12.1): GetMissionAttemptsAsync, ListMissionSubmissionsAsync,
    /// GetReliabilitySummaryAsync, and the gate report upload, list and read, against a live Admiral.
    /// </summary>
    public sealed class ClientContractReliabilitySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Client.Contract.Reliability";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("attempts_and_summary", "Mission attempts oldest first with escalation and repair flags; summary per model and persona", async (c, fx) =>
            {
                string suffix = ClientContract.Suffix();
                string open = "qwen-contract-" + suffix;
                string frontier = "opus-contract-" + suffix;
                Mission mission = await SeedAsync(fx, open, frontier).ConfigureAwait(false);

                MissionAttemptHistory history = (await c.GetMissionAttemptsAsync(mission.Id))!;
                AssertEqual(mission.Id, history.MissionId, "mission");
                AssertEqual(1, history.EscalationCount, "escalation count");
                AssertEqual(open, history.EscalatedFromModel, "escalated from");
                AssertEqual(1, history.OutputRepairAttempts, "repair counter");
                AssertEqual(3, history.Attempts.Count, "three attempts");
                AssertEqual(open, history.Attempts[0].Model, "oldest first");
                AssertTrue(history.Attempts[0].IsRepair, "output repair attempt");
                AssertTrue(history.Attempts[1].Escalated, "escalated attempt");
                AssertEqual("cpt_next_" + suffix, history.Attempts[1].NextCaptainId, "escalation target");
                AssertEqual(RuntimeFailureKindEnum.Unclassified, history.Attempts[1].RuntimeFailureKind, "runtime failure kind");
                AssertEqual(StageDispositionEnum.Completed, history.Attempts[2].Disposition, "final attempt");
                await ClientContract.ExpectErrorAsync(() => c.GetMissionAttemptsAsync("msn_missing"), "unknown mission", 404, 404);

                List<StageSubmission> submissions = (await c.ListMissionSubmissionsAsync(mission.Id))!;
                AssertEqual(0, submissions.Count, "no submissions for a seeded mission");
                await ClientContract.ExpectErrorAsync(() => c.ListMissionSubmissionsAsync("msn_missing"), "unknown mission submissions", 404, 404);

                ReliabilitySummary summary = (await c.GetReliabilitySummaryAsync(new ReliabilitySummaryQuery { Model = open, Sort = ReliabilitySortEnum.Escalations }))!;
                ReliabilitySummaryRow row = summary.Rows.Single();
                AssertEqual("Worker", row.Persona, "persona");
                AssertEqual(2, row.Attempts, "attempts of the open model");
                AssertEqual(1, row.Repairs, "repairs");
                AssertEqual(1, row.Escalations, "escalations");
                AssertEqual(1, row.Unclassified, "unclassified");
                AssertEqual(0.5, row.UnclassifiedRate, "unclassified rate");
                AssertEqual(0.0, row.CompletionRate, "the open model finished nothing");
                AssertEqual(2, summary.Totals.Attempts, "totals follow the filter");

                ReliabilitySummary both = (await c.GetReliabilitySummaryAsync(new ReliabilitySummaryQuery { Persona = "Worker", Sort = ReliabilitySortEnum.CompletionRate, PageSize = 1000 }))!;
                ReliabilitySummaryRow best = both.Rows.First(r => r.Model == frontier);
                AssertEqual(1.0, best.CompletionRate, "frontier completed");
                int frontierIndex = both.Rows.FindIndex(r => r.Model == frontier);
                int openIndex = both.Rows.FindIndex(r => r.Model == open);
                AssertTrue(frontierIndex < openIndex, "sorted by completion rate, descending");
                await ClientContract.ExpectErrorAsync(() => c.GetReliabilitySummaryAsync(new ReliabilitySummaryQuery { FromUtc = DateTime.UtcNow.AddDays(-200) }), "window too long", 400, 400);
            }));

            cases.Add(Case("gate_reports", "Upload a gate report, list it newest first, read it by id and as latest", async (c, fx) =>
            {
                GateReport report = new GateReport
                {
                    StartedUtc = DateTime.UtcNow.AddMinutes(-5),
                    FinishedUtc = DateTime.UtcNow,
                    Packs = new List<string> { "csharp" },
                    Pipelines = new List<GatePipelineResult> { new GatePipelineResult { Pipeline = "Gate-A", Status = "Completed", Passed = 9, Failed = 1 } },
                    Measures = new List<GateMeasure> { new GateMeasure { Id = "G1", Name = "Completion", Value = "90%", Threshold = ">= 95%", Status = GateMeasureStatusEnum.Fail } }
                };
                GateReport stored = (await c.UploadGateReportAsync(report))!;
                AssertFalse(String.IsNullOrEmpty(stored.Id), "id assigned");
                AssertNotNull(stored.UploadedUtc, "uploaded time");
                GateReport latest = (await c.GetGateReportAsync("latest"))!;
                AssertEqual(stored.Id, latest.Id, "latest");
                AssertEqual(GateMeasureStatusEnum.Fail, latest.Measures.Single().Status, "measure round trip");
                AssertEqual(stored.Id, (await c.GetGateReportAsync(stored.Id!))?.Id, "by id");
                EnumerationResult<GateReport> page = (await c.ListGateReportsAsync(1, 5))!;
                AssertEqual(stored.Id, page.Objects.First().Id, "newest first");
                await ClientContract.ExpectErrorAsync(() => c.GetGateReportAsync("gate-00000000-000000000"), "unknown report", 404, 404);
                await ClientContract.ExpectErrorAsync(() => c.UploadGateReportAsync(new GateReport()), "empty report", 400, 400);
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Armada.Client contract: pipeline reliability", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<Mission> SeedAsync(E2EServerFixture fx, string openModel, string frontierModel)
        {
            LoggingModule quiet = new LoggingModule();
            quiet.Settings.EnableConsole = false;
            DatabaseSettings settings = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(fx.TempDir, "armada.db") };
            using (DatabaseDriver db = DatabaseDriverFactory.Create(settings, quiet))
            {
                await db.InitializeAsync().ConfigureAwait(false);
                string suffix = openModel.Substring(openModel.LastIndexOf('-') + 1);
                Mission mission = await db.Missions.CreateAsync(new Mission
                {
                    TenantId = Armada.Core.Constants.DefaultTenantId,
                    Title = "Reliability contract " + suffix,
                    Persona = "Worker",
                    Status = MissionStatusEnum.Complete,
                    EscalationCount = 1,
                    EscalatedFromModel = openModel,
                    OutputRepairAttempts = 1
                }).ConfigureAwait(false);

                DateTime start = DateTime.UtcNow.AddMinutes(-30);
                await db.StageOutcomes.CreateAsync(Outcome(mission, openModel, 1, StageDispositionEnum.OutputRepair, null, start, null)).ConfigureAwait(false);
                await db.StageOutcomes.CreateAsync(Outcome(mission, openModel, 2, StageDispositionEnum.Escalated, RuntimeFailureKindEnum.Unclassified, start.AddMinutes(5), "cpt_next_" + suffix)).ConfigureAwait(false);
                await db.StageOutcomes.CreateAsync(Outcome(mission, frontierModel, 3, StageDispositionEnum.Completed, RuntimeFailureKindEnum.Clean, start.AddMinutes(10), null)).ConfigureAwait(false);
                return mission;
            }
        }

        private static StageOutcome Outcome(Mission mission, string model, int attempt, StageDispositionEnum disposition, RuntimeFailureKindEnum? runtime, DateTime started, string? next)
        {
            return new StageOutcome
            {
                TenantId = mission.TenantId,
                MissionId = mission.Id,
                Persona = "Worker",
                Model = model,
                Attempt = attempt,
                Disposition = disposition,
                RuntimeFailureKind = runtime,
                NextCaptainId = next,
                StartedUtc = started,
                EndedUtc = started.AddMinutes(4),
                DurationMs = 240000
            };
        }

        private TestCaseDescriptor Case(string id, string name, Func<ArmadaClient, E2EServerFixture, Task> body)
        {
            return ClientContract.Case(Suite, this, id, name, body);
        }

        #endregion
    }
}
