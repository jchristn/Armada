namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Metrics;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Per-attempt reliability metrics (RS-1.4, RS-12.5): every stored stage outcome counts once, records its duration,
    /// and counts repairs and escalations with bounded labels and no identifiers.
    /// </summary>
    public sealed class StageOutcomeMetricsSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.StageOutcomeMetrics";

        // Outcomes here use the Pi runtime so measurements other suites record at the same time are filtered out.
        private const AgentRuntimeEnum MarkerRuntime = AgentRuntimeEnum.Pi;

        private static readonly HashSet<string> _Instruments = new HashSet<string>(StringComparer.Ordinal)
        {
            "armada.stage_outcomes", "armada.stage_attempts.duration_seconds", "armada.stage_repairs", "armada.stage_escalations"
        };

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("completed_attempt_counts_and_records_duration", "A completed attempt counts once and records its duration in seconds, with no repair or escalation", () =>
            {
                List<MetricMeasurement> seen = Capture(() => StageOutcomeMetrics.Record(Outcome("Worker", "qwen3.8:32b", StageDispositionEnum.Completed, 90000, null, null)));
                AssertEqual(1, seen.Count(m => m.Name == "armada.stage_outcomes"), "one outcome");
                MetricMeasurement duration = seen.Single(m => m.Name == "armada.stage_attempts.duration_seconds");
                AssertEqual(90.0, duration.Value, "duration in seconds");
                AssertEqual("qwen3.8:32b", duration.Tags["model"]);
                AssertEqual("Completed", duration.Tags["disposition"]);
                AssertFalse(seen.Any(m => m.Name == "armada.stage_repairs" || m.Name == "armada.stage_escalations"), "no repair or escalation");
                return Task.CompletedTask;
            }));

            cases.Add(Case("repairs_and_escalations_are_counted", "A contract repair and an output-repair rerun count as repairs; an escalation counts with its failure kind", () =>
            {
                List<MetricMeasurement> seen = Capture(() =>
                {
                    StageOutcomeMetrics.Record(Outcome("Worker", "qwen3.8", StageDispositionEnum.Completed, null, ContractRepairKindEnum.CompletionLineMissing, null));
                    StageOutcomeMetrics.Record(Outcome("Worker", "qwen3.8", StageDispositionEnum.OutputRepair, null, ContractRepairKindEnum.None, MissionFailureKindEnum.InvalidOutput));
                    StageOutcomeMetrics.Record(Outcome("Judge", "qwen3.8", StageDispositionEnum.Escalated, null, null, MissionFailureKindEnum.Timeout));
                });
                List<MetricMeasurement> repairs = seen.Where(m => m.Name == "armada.stage_repairs").ToList();
                AssertEqual(2, repairs.Count, "two repairs (None is not a repair)");
                AssertTrue(repairs.Any(r => r.Tags["kind"] == "CompletionLineMissing"), "contract repair kind");
                AssertTrue(repairs.Any(r => r.Tags["kind"] == StageOutcomeMetrics.RerunRepairKind), "rerun repair kind");
                MetricMeasurement escalation = seen.Single(m => m.Name == "armada.stage_escalations");
                AssertEqual("Judge", escalation.Tags["persona"]);
                AssertEqual("Timeout", escalation.Tags["failure_kind"]);
                AssertFalse(seen.Any(m => m.Name == "armada.stage_attempts.duration_seconds"), "no duration when unknown");
                return Task.CompletedTask;
            }));

            cases.Add(Case("labels_are_bounded", "Long or free-text persona and model names become Other; missing ones become none; ids are never tagged", () =>
            {
                List<MetricMeasurement> seen = Capture(() =>
                {
                    StageOutcomeMetrics.Record(Outcome("a persona with spaces", new string('m', 80), StageDispositionEnum.Failed, 1000, null, MissionFailureKindEnum.Crash));
                    StageOutcomeMetrics.Record(Outcome(null, " ", StageDispositionEnum.Completed, null, null, null));
                    StageOutcomeMetrics.Record(null);
                });
                List<MetricMeasurement> outcomes = seen.Where(m => m.Name == "armada.stage_outcomes").ToList();
                AssertEqual(2, outcomes.Count, "null outcome ignored");
                AssertEqual(StageOutcomeMetrics.OtherLabel, outcomes[0].Tags["persona"]);
                AssertEqual(StageOutcomeMetrics.OtherLabel, outcomes[0].Tags["model"]);
                AssertEqual("none", outcomes[1].Tags["persona"]);
                AssertEqual("none", outcomes[1].Tags["model"]);
                foreach (MetricMeasurement m in seen)
                    foreach (KeyValuePair<string, string> tag in m.Tags)
                        AssertFalse(tag.Value.Contains("msn_", StringComparison.Ordinal) || tag.Value.Contains("cpt_", StringComparison.Ordinal), "no identifiers: " + tag.Key);
                return Task.CompletedTask;
            }));

            cases.Add(Case("pipeline_dashboard_reads_emitted_series", "The Pipeline Reliability dashboard explains every panel and only queries series Armada emits", () =>
            {
                HashSet<string> series = new HashSet<string>(StringComparer.Ordinal);
                AssertNotNull(ArmadaMetrics.QueueSeconds, "instruments exist");
                using (MeterListener listener = new MeterListener())
                {
                    listener.InstrumentPublished = (instrument, current) =>
                    {
                        if (instrument.Meter.Name != ArmadaMetrics.MeterName) return;
                        string baseName = instrument.Name.Replace('.', '_');
                        if (instrument is Counter<long>) series.Add(baseName + "_total");
                        else if (instrument is Histogram<double>)
                        {
                            string histogram = baseName.EndsWith("_seconds", StringComparison.Ordinal) ? baseName : baseName + "_seconds";
                            foreach (string suffix in new[] { "_bucket", "_count", "_sum" }) series.Add(histogram + suffix);
                        }
                        else series.Add(baseName);
                    };
                    listener.Start();
                }

                string path = Path.Combine(DashboardSource.RepoRoot(), "docker", "armada", "observability", "grafana", "dashboards", "armada-pipeline-reliability.json");
                GrafanaDashboardFile? dashboard = JsonSerializer.Deserialize<GrafanaDashboardFile>(File.ReadAllText(path));
                AssertNotNull(dashboard, "dashboard parses");
                AssertEqual("armada-pipeline-reliability", dashboard!.Uid);
                List<string> referenced = new List<string>();
                foreach (GrafanaPanelFile panel in dashboard.Panels)
                {
                    AssertFalse(String.IsNullOrWhiteSpace(panel.Description), panel.Title + " explains itself");
                    foreach (GrafanaTargetFile target in panel.Targets)
                        foreach (Match match in Regex.Matches(target.Expr, "armada_[a-z_]+")) referenced.Add(match.Value);
                }
                foreach (string name in new[] { "armada_stage_outcomes_total", "armada_stage_repairs_total", "armada_stage_escalations_total", "armada_missions_queue_seconds_bucket", "armada_stage_attempts_duration_seconds_bucket" })
                    AssertTrue(referenced.Contains(name), "dashboard queries " + name);
                foreach (string name in referenced) AssertTrue(series.Contains(name), "dashboard series " + name + " is emitted");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Stage outcome metrics", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static StageOutcome Outcome(string? persona, string? model, StageDispositionEnum disposition, long? durationMs, ContractRepairKindEnum? repair, MissionFailureKindEnum? failureKind)
        {
            return new StageOutcome
            {
                MissionId = "msn_metrics",
                CaptainId = "cpt_metrics",
                Persona = persona,
                Model = model,
                Runtime = MarkerRuntime,
                Disposition = disposition,
                DurationMs = durationMs,
                ContractRepairKind = repair,
                FailureKind = failureKind
            };
        }

        private static List<MetricMeasurement> Capture(Action action)
        {
            List<MetricMeasurement> seen = new List<MetricMeasurement>();
            object gate = new object();
            using (MeterListener listener = new MeterListener())
            {
                listener.InstrumentPublished = (instrument, current) => { if (_Instruments.Contains(instrument.Name)) current.EnableMeasurementEvents(instrument); };
                listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) => { lock (gate) seen.Add(new MetricMeasurement(instrument.Name, value, tags)); });
                listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) => { lock (gate) seen.Add(new MetricMeasurement(instrument.Name, value, tags)); });
                listener.Start();
                action();
            }

            string marker = MarkerRuntime.ToString();
            lock (gate) return seen.Where(m => m.Tags.TryGetValue("runtime", out string? runtime) && runtime == marker).ToList();
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<Task> body)
            => new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: token => body(), tags: new List<string> { TestTags.Positive });

        #endregion
    }
}
