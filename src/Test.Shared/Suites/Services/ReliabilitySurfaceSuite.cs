namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Server;
    using Armada.Server.WebSocket;
    using Test.Shared.Infrastructure;
    using Test.Shared.Suites.Tui;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The reliability surfaces' pure parts (RS-12.1, RS-12.3): the summary builder's counting, sorting and paging, the
    /// WebSocket attempt-change tracker, and the gate report store.
    /// </summary>
    public sealed class ReliabilitySurfaceSuite : IArmadaTestSuite
    {
        private const string Suite = "Services.ReliabilitySurface";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "summary_counts_per_model_and_persona", "The summary counts completion, repairs, escalations and Unclassified per (model, persona)", () =>
            {
                List<StageOutcome> outcomes = new List<StageOutcome>
                {
                    Outcome("qwen", "Worker", StageDispositionEnum.OutputRepair, null, ContractRepairKindEnum.FormatRepairTurn, 100),
                    Outcome("qwen", "Worker", StageDispositionEnum.Escalated, RuntimeFailureKindEnum.Unclassified, null, 300),
                    Outcome("QWEN", "worker", StageDispositionEnum.Completed, RuntimeFailureKindEnum.Clean, ContractRepairKindEnum.VerdictNonCanonical, 200),
                    Outcome("opus", "Judge", StageDispositionEnum.Completed, RuntimeFailureKindEnum.Clean, null, 50),
                    Outcome(null, null, StageDispositionEnum.Failed, RuntimeFailureKindEnum.Crash, null, null)
                };
                outcomes[4].FailureKind = MissionFailureKindEnum.Crash;
                ReliabilitySummary summary = ReliabilitySummaryBuilder.Build(outcomes, new ReliabilitySummaryQuery(), DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, false);

                AssertEqual(3, summary.TotalRecords, "model and persona group case-insensitively");
                ReliabilitySummaryRow qwen = summary.Rows[0];
                AssertEqual("qwen", qwen.Model, "most attempts first, first spelling kept");
                AssertEqual(3, qwen.Attempts);
                AssertEqual(2, qwen.Repairs, "output repair rerun and contract repair");
                AssertEqual(1, qwen.Escalations);
                AssertEqual(1, qwen.Unclassified);
                AssertEqual(0.5, qwen.CompletionRate, "one completed of completed, failed and escalated");
                AssertEqual(0.3333, qwen.UnclassifiedRate);
                AssertEqual((long?)200, qwen.MedianDurationMs, "median");
                AssertEqual(1, qwen.RuntimeFailureKinds["Unclassified"], "Clean excluded");
                AssertFalse(qwen.RuntimeFailureKinds.ContainsKey("Clean"), "Clean excluded");
                ReliabilitySummaryRow unknown = summary.Rows.Single(r => r.Model == ReliabilitySummaryBuilder.UnknownModel);
                AssertEqual(ReliabilitySummaryBuilder.NoPersona, unknown.Persona);
                AssertEqual(1, unknown.FailureKinds["Crash"]);
                AssertEqual(5, summary.Totals.Attempts, "totals");
                AssertEqual(ReliabilitySummaryBuilder.All, summary.Totals.Model);
            }));

            cases.Add(TuiCase.Sync(Suite, "summary_sorts_and_pages", "Sort by any field either way with a stable tie-break; pages neither overlap nor skip", () =>
            {
                List<StageOutcome> outcomes = new List<StageOutcome>();
                for (int i = 0; i < 7; i++) outcomes.Add(Outcome("m" + i, "Worker", i % 2 == 0 ? StageDispositionEnum.Completed : StageDispositionEnum.Failed, null, null, null));
                ReliabilitySummaryQuery query = new ReliabilitySummaryQuery { Sort = ReliabilitySortEnum.Model, Descending = false, PageSize = 3 };
                List<string> seen = new List<string>();
                for (int page = 1; page <= 3; page++)
                {
                    query.PageNumber = page;
                    ReliabilitySummary summary = ReliabilitySummaryBuilder.Build(outcomes, query, DateTime.UtcNow, DateTime.UtcNow, false);
                    AssertEqual(3, summary.TotalPages);
                    seen.AddRange(summary.Rows.Select(r => r.Model));
                }

                AssertEqual("m0,m1,m2,m3,m4,m5,m6", String.Join(",", seen), "ascending by model across pages");
                ReliabilitySummary byRate = ReliabilitySummaryBuilder.Build(outcomes, new ReliabilitySummaryQuery { Sort = ReliabilitySortEnum.CompletionRate, Descending = false }, DateTime.UtcNow, DateTime.UtcNow, false);
                AssertEqual(0.0, byRate.Rows[0].CompletionRate, "ascending rate");
                AssertEqual("m1", byRate.Rows[0].Model, "ties broken by model");
                ReliabilitySummary empty = ReliabilitySummaryBuilder.Build(new List<StageOutcome>(), new ReliabilitySummaryQuery(), DateTime.UtcNow, DateTime.UtcNow, false);
                AssertEqual(0, empty.TotalPages, "no rows, no pages");
            }));

            cases.Add(TuiCase.Sync(Suite, "attempt_tracker_reports_increases", "The attempt tracker reports escalations, repairs and new attempts once each, and forgets finished missions", () =>
            {
                MissionAttemptChangeTracker tracker = new MissionAttemptChangeTracker();
                Mission mission = new Mission { Status = MissionStatusEnum.InProgress };
                AssertEqual(0, tracker.Observe(mission).Count, "first sighting only records");
                mission.RedispatchAttempts = 1;
                AssertEqual(MissionAttemptChangeTracker.AttemptChangedEvent, tracker.Observe(mission).Single(), "redispatch");
                AssertEqual(0, tracker.Observe(mission).Count, "no change, no event");
                mission.OutputRepairAttempts = 1;
                mission.EscalationCount = 1;
                List<string> both = tracker.Observe(mission);
                AssertEqual(MissionAttemptChangeTracker.EscalatedEvent + "," + MissionAttemptChangeTracker.RepairedEvent, String.Join(",", both), "escalated then repaired");
                mission.Status = MissionStatusEnum.Complete;
                mission.StallRecoveryAttempts = 1;
                AssertEqual(MissionAttemptChangeTracker.AttemptChangedEvent, tracker.Observe(mission).Single(), "the terminal change still reports");
                mission.Status = MissionStatusEnum.InProgress;
                mission.StallRecoveryAttempts = 2;
                AssertEqual(0, tracker.Observe(mission).Count, "forgotten after the terminal status");
                MissionAttemptChange payload = MissionAttemptChange.From(mission);
                AssertEqual(mission.Id, payload.MissionId);
                AssertEqual(1, payload.EscalationCount);
            }));

            cases.Add(TuiCase.Sync(Suite, "gate_report_store", "The gate report store assigns ids, lists newest first, reads latest, and refuses traversal and empty reports", () =>
            {
                string dir = TestTemp.NewDirectory("gate_reports");
                GateReportStore store = new GateReportStore(dir);
                AssertNull(store.Read("latest"), "nothing stored");
                GateReport first = store.Save(new GateReport { Measures = new List<GateMeasure> { new GateMeasure { Id = "G1", Status = GateMeasureStatusEnum.Pass } } });
                GateReport second = store.Save(new GateReport { Pipelines = new List<GatePipelineResult> { new GatePipelineResult { Pipeline = "Gate-A" } } });
                AssertTrue(String.CompareOrdinal(second.Id, first.Id) > 0, "ids sort by time");
                AssertEqual(second.Id, store.Read("latest")!.Id, "latest");
                AssertEqual(GateMeasureStatusEnum.Pass, store.Read(first.Id)!.Measures.Single().Status, "round trip");
                EnumerationResult<GateReport> page = store.List(1, 1);
                AssertEqual(2L, page.TotalRecords);
                AssertEqual(second.Id, page.Objects.Single().Id, "newest first");
                AssertNull(store.Read("../settings"), "traversal refused");
                AssertThrows<ArgumentException>(() => store.Save(new GateReport()), "empty report");
                AssertTrue(File.Exists(Path.Combine(dir, "gate-reports", first.Id + ".json")), "stored under gate-reports");
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Reliability surfaces: summary, attempt events, gate reports", cases: cases);
        }

        private static StageOutcome Outcome(string? model, string? persona, StageDispositionEnum disposition, RuntimeFailureKindEnum? runtime, ContractRepairKindEnum? repair, long? durationMs)
        {
            return new StageOutcome { MissionId = "msn_x", Model = model, Persona = persona, Disposition = disposition, RuntimeFailureKind = runtime, ContractRepairKind = repair, DurationMs = durationMs };
        }
    }
}
