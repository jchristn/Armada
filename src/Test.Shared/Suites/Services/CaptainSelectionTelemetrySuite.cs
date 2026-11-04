namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Captain-selection telemetry keeps labels bounded (no identity, model or free-form text), marks unfinished stages
    /// as errors on their spans, publishes reservation state counts, and never lets a failing exporter reach the caller.
    /// </summary>
    public sealed class CaptainSelectionTelemetrySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.CaptainSelectionTelemetry";
        private static readonly HashSet<string> _AllowedTags = new HashSet<string>(StringComparer.Ordinal) { "mode", "outcome", "trigger", "reason", "strategy", "stage", "state" };

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("labels_are_bounded_semantic_codes", "Every selection instrument uses fixed tag keys; identifiers and free text collapse to Other", () =>
            {
                List<KeyValuePair<string, object?>> tags = new List<KeyValuePair<string, object?>>();
                List<string> names = new List<string>();
                using MeterListener listener = Listen((instrument, current) => current.EnableMeasurementEvents(instrument));
                listener.SetMeasurementEventCallback<long>((instrument, measurement, observed, state) => { names.Add(instrument.Name); foreach (KeyValuePair<string, object?> tag in observed) tags.Add(tag); });
                listener.SetMeasurementEventCallback<double>((instrument, measurement, observed, state) => { names.Add(instrument.Name); foreach (KeyValuePair<string, object?> tag in observed) tags.Add(tag); });
                listener.Start();
                CaptainSelectionTelemetry.Decision(UsageSelectionModeEnum.Enforce, "Admitted", "Dispatch");
                CaptainSelectionTelemetry.Wait(UsageSelectionModeEnum.Enforce, "msn_abc123 failed: quota for user@example.com");
                CaptainSelectionTelemetry.Wait(UsageSelectionModeEnum.Observe, "BelowReserve");
                CaptainSelectionTelemetry.ObserveDivergence(CaptainSelectionStrategyEnum.MostRemainingAllowance);
                CaptainSelectionTelemetry.PersistenceFailure("Admission");
                CaptainSelectionTelemetry.Reconciled("Settled", 3);
                CaptainSelectionTelemetry.Reconciled("Unchanged", 0);
                CaptainSelectionTelemetry.Cooldown("Recorded");
                CaptainSelectionTelemetry.RetentionDeleted(5);
                using (CaptainSelectionStage stage = CaptainSelectionTelemetry.Stage("evaluation", "msn_secret")) stage.Complete("Selected");

                foreach (string name in new[] { "armada.selection.decisions", "armada.selection.waits", "armada.selection.observe_divergence", "armada.selection.persistence_failures",
                    "armada.selection.reconciliation", "armada.selection.cooldowns", "armada.selection.retention.deleted", "armada.selection.stage.duration" })
                    AssertTrue(names.Contains(name), name + " recorded");
                AssertFalse(names.Count(n => n == "armada.selection.reconciliation") > 1, "zero counts are not recorded");
                foreach (KeyValuePair<string, object?> tag in tags)
                {
                    AssertTrue(_AllowedTags.Contains(tag.Key), "unexpected tag key " + tag.Key);
                    string value = tag.Value?.ToString() ?? "";
                    AssertFalse(value.Contains("msn_", StringComparison.Ordinal) || value.Contains("@", StringComparison.Ordinal) || value.Contains(' '), "no identifiers or free text: " + value);
                }
                AssertTrue(tags.Any(t => t.Key == "reason" && (string?)t.Value == CaptainSelectionTelemetry.OtherLabel), "free text reason becomes Other");
                AssertTrue(tags.Any(t => t.Key == "reason" && (string?)t.Value == "BelowReserve"), "semantic reason kept");
                AssertEqual("Other", CaptainSelectionTelemetry.Label(null));
                AssertEqual("Other", CaptainSelectionTelemetry.Label("cpt_123"));
                AssertEqual("Other", CaptainSelectionTelemetry.Label(new string('A', 70)));
                AssertEqual("UsageStale", CaptainSelectionTelemetry.Label("UsageStale"));
                using (CaptainSelectionStage odd = CaptainSelectionTelemetry.Stage("Evidence For msn_1")) odd.Complete();
                AssertFalse(tags.Any(t => t.Key == "stage" && (t.Value?.ToString() ?? "").Contains("msn_", StringComparison.Ordinal)), "stage names are bounded too");
                return Task.CompletedTask;
            }));

            cases.Add(Case("stage_spans_record_status_and_mission_only_on_the_span", "A completed stage is Ok; one disposed without Complete is Error with outcome Error", () =>
            {
                List<Activity> stopped = new List<Activity>();
                List<string> outcomes = new List<string>();
                using ActivityListener activities = new ActivityListener
                {
                    ShouldListenTo = source => source.Name == ArmadaMetrics.MeterName,
                    Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
                    ActivityStopped = activity => { if (activity.OperationName.StartsWith("selection.", StringComparison.Ordinal)) stopped.Add(activity); }
                };
                ActivitySource.AddActivityListener(activities);
                using MeterListener listener = Listen((instrument, current) => { if (instrument.Name == "armada.selection.stage.duration") current.EnableMeasurementEvents(instrument); });
                listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
                {
                    foreach (KeyValuePair<string, object?> tag in tags) if (tag.Key == "outcome") outcomes.Add(tag.Value?.ToString() ?? "");
                });
                listener.Start();
                using (CaptainSelectionStage ok = CaptainSelectionTelemetry.Stage("transaction", "msn_1")) ok.Complete("Admitted");
                try
                {
                    using CaptainSelectionStage failing = CaptainSelectionTelemetry.Stage("evidence", "msn_2");
                    throw new InvalidOperationException("database offline");
                }
                catch (InvalidOperationException) { }

                AssertEqual(2, stopped.Count, "two selection spans");
                Activity admitted = stopped.Single(a => a.OperationName == "selection.transaction");
                AssertEqual(ActivityStatusCode.Ok, admitted.Status);
                AssertEqual("msn_1", admitted.GetTagItem("armada.mission_id")?.ToString(), "mission id is a span attribute");
                Activity failed = stopped.Single(a => a.OperationName == "selection.evidence");
                AssertEqual(ActivityStatusCode.Error, failed.Status);
                AssertEqual("Error", failed.GetTagItem("armada.selection.outcome")?.ToString());
                AssertTrue(outcomes.Contains("Admitted") && outcomes.Contains("Error"), "duration outcomes");
                return Task.CompletedTask;
            }));

            cases.Add(Case("reservation_gauge_reports_every_state", "The reservations gauge reports each lifecycle state, zero when absent", () =>
            {
                Dictionary<string, long> observed = new Dictionary<string, long>(StringComparer.Ordinal);
                using MeterListener listener = Listen((instrument, current) => { if (instrument.Name == "armada.selection.reservations") current.EnableMeasurementEvents(instrument); });
                listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
                {
                    foreach (KeyValuePair<string, object?> tag in tags) if (tag.Key == "state") observed[tag.Value?.ToString() ?? ""] = measurement;
                });
                listener.Start();
                CaptainSelectionTelemetry.Reservations(new Dictionary<UsageReservationStateEnum, long> { { UsageReservationStateEnum.Active, 4 }, { UsageReservationStateEnum.Uncertain, 1 } });
                listener.RecordObservableInstruments();
                AssertEqual(4L, observed["Active"]); AssertEqual(1L, observed["Uncertain"]); AssertEqual(0L, observed["Unsettled"]);
                AssertEqual(Enum.GetValues<UsageReservationStateEnum>().Length, observed.Count, "every state reported");
                AssertThrows<ArgumentNullException>(() => CaptainSelectionTelemetry.Reservations(null!));
                return Task.CompletedTask;
            }));

            cases.Add(Case("failing_exporter_never_reaches_selection", "A listener that throws on every measurement cannot fail the selection code path", () =>
            {
                using MeterListener listener = Listen((instrument, current) => current.EnableMeasurementEvents(instrument));
                listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) => throw new InvalidOperationException("fixture telemetry sink"));
                listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) => throw new InvalidOperationException("fixture telemetry sink"));
                listener.Start();
                CaptainSelectionTelemetry.Decision(UsageSelectionModeEnum.Observe, "Waiting", "Waiting");
                CaptainSelectionTelemetry.Wait(UsageSelectionModeEnum.Observe, "UsageMissing");
                CaptainSelectionTelemetry.ObserveDivergence(CaptainSelectionStrategyEnum.FirstAvailable);
                CaptainSelectionTelemetry.PersistenceFailure("History");
                CaptainSelectionTelemetry.Reconciled("Released");
                CaptainSelectionTelemetry.Cooldown("Ignored");
                CaptainSelectionTelemetry.RetentionDeleted(1);
                using (CaptainSelectionStage stage = CaptainSelectionTelemetry.Stage("reconcile")) stage.Complete();
                return Task.CompletedTask;
            }));

            cases.Add(Case("grafana_dashboard_queries_only_emitted_series", "The provisioned selection dashboard references only series the selection instruments produce, and dashboard UIDs are unique", () =>
            {
                HashSet<string> series = new HashSet<string>(StringComparer.Ordinal);
                CaptainSelectionTelemetry.Reservations(new Dictionary<UsageReservationStateEnum, long>()); // ensure the instruments exist
                using (MeterListener listener = Listen((instrument, current) =>
                {
                    string baseName = instrument.Name.Replace('.', '_');
                    if (instrument is Counter<long>) series.Add(baseName + "_total");
                    else if (instrument is Histogram<double>) foreach (string suffix in new[] { "_bucket", "_count", "_sum" }) series.Add(baseName + "_seconds" + suffix);
                    else series.Add(baseName);
                })) { listener.Start(); }
                string directory = Path.Combine(DashboardSource.RepoRoot(), "docker", "armada", "observability", "grafana", "dashboards");
                HashSet<string> uids = new HashSet<string>(StringComparer.Ordinal);
                foreach (string file in Directory.GetFiles(directory, "*.json"))
                {
                    GrafanaDashboardFile? parsed = JsonSerializer.Deserialize<GrafanaDashboardFile>(File.ReadAllText(file));
                    AssertNotNull(parsed, file);
                    AssertTrue(uids.Add(parsed!.Uid), "unique uid " + parsed.Uid);
                }
                GrafanaDashboardFile selection = JsonSerializer.Deserialize<GrafanaDashboardFile>(File.ReadAllText(Path.Combine(directory, "armada-captain-selection.json")))!;
                AssertEqual("armada-captain-selection", selection.Uid);
                AssertTrue(selection.Panels.Count >= 10, "panels");
                List<string> referenced = new List<string>();
                foreach (GrafanaPanelFile panel in selection.Panels)
                {
                    AssertFalse(String.IsNullOrWhiteSpace(panel.Description), panel.Title + " explains itself");
                    foreach (GrafanaTargetFile target in panel.Targets)
                        foreach (Match match in Regex.Matches(target.Expr, "armada_selection_[a-z_]+")) referenced.Add(match.Value);
                }
                AssertTrue(referenced.Count > 0, "selection series referenced");
                foreach (string name in referenced) AssertTrue(series.Contains(name), "dashboard series " + name + " is emitted");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Captain selection telemetry", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static MeterListener Listen(Action<Instrument, MeterListener> published)
        {
            MeterListener listener = new MeterListener();
            listener.InstrumentPublished = (instrument, current) => { if (instrument.Name.StartsWith("armada.selection.", StringComparison.Ordinal)) published(instrument, current); };
            return listener;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<Task> body)
            => new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: token => body(), tags: new List<string> { TestTags.Positive });

        #endregion
    }
}
