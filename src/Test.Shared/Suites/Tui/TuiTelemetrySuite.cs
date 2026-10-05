namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using Armada.Core.Settings;
    using Armada.Tui;
    using Armada.Tui.Approvals;
    using Armada.Tui.Input;
    using Armada.Tui.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using TUIKit.Diagnostics;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// TUI telemetry (W8.6): the <c>Armada.Tui</c> meter emits screen views, commands (with source and outcome),
    /// approval decisions with latency, Ask messages, and sessions, observed with a <see cref="System.Diagnostics.Metrics.MeterListener"/>;
    /// command spans reach an <see cref="ActivityListener"/>; the switch turns emission off; the <c>tui.json</c>
    /// telemetry section is off by default and maps onto the server's telemetry settings; the exporter factory runs
    /// only when enabled. Cases in this suite toggle the process-wide switch and restore it.
    /// </summary>
    public sealed class TuiTelemetrySuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Telemetry";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "screen_views", "Navigating records armada.tui.screen.views by route pattern", () =>
            {
                using (TuiMeasurementCapture capture = new TuiMeasurementCapture())
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/missions"))
                {
                    host.Tui.Context.Navigate("/missions/msn_telemetry1");
                    host.Pump();
                    List<TuiMeasurement> views = capture.For("armada.tui.screen.views");
                    AssertTrue(views.Any(m => m.Tag(TuiTelemetry.TagRoute) == "/missions/:id"), "mission detail view by pattern");
                    AssertFalse(views.Any(m => (m.Tag(TuiTelemetry.TagRoute) ?? "").Contains("msn_telemetry1")), "no entity id in tags");
                    AssertTrue(capture.For("armada.tui.sessions").Count >= 1, "session counted at start");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "commands", "Commands record id, source, and outcome; key bindings count as key", () =>
            {
                using (TuiMeasurementCapture capture = new TuiMeasurementCapture())
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/missions"))
                {
                    host.Press("ctrl+k");
                    host.Press("esc");
                    AssertTrue(capture.For("armada.tui.commands").Any(m => m.Tag(TuiTelemetry.TagCommand) == "help.palette" && m.Tag(TuiTelemetry.TagSource) == TuiTelemetry.SourceKey && m.Tag(TuiTelemetry.TagOutcome) == "ok"), "palette via key");
                }

                using (TuiMeasurementCapture capture = new TuiMeasurementCapture())
                {
                    CommandService commands = new CommandService();
                    commands.Register(new ArmadaCommand("telemetry.ok", "Ok", CommandMenuEnum.None, () => { }));
                    commands.Register(new ArmadaCommand("telemetry.boom", "Boom", CommandMenuEnum.None, () => throw new InvalidOperationException("boom")));
                    AssertTrue(commands.Execute("telemetry.ok", TuiTelemetry.SourcePalette), "ran");
                    bool threw = false;
                    try
                    {
                        commands.Execute("telemetry.boom", TuiTelemetry.SourceMenu);
                    }
                    catch (InvalidOperationException)
                    {
                        threw = true;
                    }

                    AssertTrue(threw, "handler exception still propagates");
                    List<TuiMeasurement> runs = capture.For("armada.tui.commands");
                    AssertTrue(runs.Any(m => m.Tag(TuiTelemetry.TagCommand) == "telemetry.ok" && m.Tag(TuiTelemetry.TagSource) == TuiTelemetry.SourcePalette && m.Tag(TuiTelemetry.TagOutcome) == "ok"), "ok run");
                    AssertTrue(runs.Any(m => m.Tag(TuiTelemetry.TagCommand) == "telemetry.boom" && m.Tag(TuiTelemetry.TagSource) == TuiTelemetry.SourceMenu && m.Tag(TuiTelemetry.TagOutcome) == "error"), "error run");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "command_spans", "Commands open armada.tui.command spans on the Armada.Tui activity source", () =>
            {
                List<Activity> stopped = new List<Activity>();
                using (ActivityListener listener = new ActivityListener())
                {
                    listener.ShouldListenTo = source => source.Name == TuiTelemetry.ActivitySourceName;
                    listener.Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded;
                    listener.ActivityStopped = activity => { lock (stopped) stopped.Add(activity); };
                    ActivitySource.AddActivityListener(listener);
                    CommandService commands = new CommandService();
                    commands.Register(new ArmadaCommand("telemetry.span", "Span", CommandMenuEnum.None, () => { }));
                    commands.Execute("telemetry.span");
                }

                lock (stopped)
                {
                    Activity? span = stopped.FirstOrDefault(a => a.OperationName == "armada.tui.command" && (a.GetTagItem(TuiTelemetry.TagCommand) as string) == "telemetry.span");
                    AssertNotNull(span, "span");
                    AssertEqual(TuiTelemetry.SourceDirect, span!.GetTagItem(TuiTelemetry.TagSource) as string, "source tag");
                    AssertEqual("ok", span.GetTagItem(TuiTelemetry.TagOutcome) as string, "outcome tag");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "approvals", "Approval decisions record kind, decision, and latency from arrival", () =>
            {
                using (TuiMeasurementCapture capture = new TuiMeasurementCapture())
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/approvals"))
                {
                    ApprovalItem item = new ApprovalItem();
                    item.Kind = ApprovalKindEnum.FailedLanding;
                    item.EntityId = "msn_landing_telemetry";
                    item.Title = "Land it";
                    item.CreatedUtc = host.Tui.Context.Clock.UtcNow.AddSeconds(-30);
                    ApprovalActions actions = new ApprovalActions(host.Tui.Context);
                    AssertTrue(actions.RetryLanding(item), "retry started");
                    host.Pump();

                    TuiMeasurement? decision = capture.For("armada.tui.approval.decisions").FirstOrDefault(m => m.Tag(TuiTelemetry.TagKind) == "FailedLanding");
                    AssertNotNull(decision, "decision counted");
                    AssertEqual("retry", decision!.Tag(TuiTelemetry.TagDecision), "decision tag");
                    TuiMeasurement? latency = capture.For("armada.tui.approval.latency").FirstOrDefault(m => m.Tag(TuiTelemetry.TagKind) == "FailedLanding");
                    AssertNotNull(latency, "latency recorded");
                    AssertTrue(latency!.Value >= 29 && latency.Value < 600, "latency about 30 s, was " + latency.Value);
                }

                using (TuiMeasurementCapture capture = new TuiMeasurementCapture())
                {
                    TuiTelemetry.RecordApproval(ApprovalKindEnum.MissionReview, "approve", null, DateTime.UtcNow);
                    AssertEqual(1, capture.For("armada.tui.approval.decisions").Count(m => m.Tag(TuiTelemetry.TagKind) == "MissionReview"), "decision without arrival time");
                    AssertEqual(0, capture.For("armada.tui.approval.latency").Count(m => m.Tag(TuiTelemetry.TagKind) == "MissionReview"), "no latency without arrival time");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "ask_messages", "Ask messages are counted", () =>
            {
                using (TuiMeasurementCapture capture = new TuiMeasurementCapture())
                {
                    TuiTelemetry.RecordAskMessage();
                    AssertTrue(capture.For("armada.tui.ask.messages").Count >= 1, "ask message");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "switch_off", "Configure(false) silences the TUI and TUIKit instruments", () =>
            {
                try
                {
                    TuiTelemetry.Configure(false);
                    AssertFalse(TuiKitTelemetry.Enabled, "TUIKit off");
                    using (TuiMeasurementCapture capture = new TuiMeasurementCapture())
                    {
                        TuiTelemetry.RecordScreenView("/telemetry-off", "Off");
                        TuiTelemetry.RecordAskMessage();
                        TuiTelemetry.RecordApproval(ApprovalKindEnum.StalledCaptain, "stop", DateTime.UtcNow, DateTime.UtcNow);
                        CommandService commands = new CommandService();
                        commands.Register(new ArmadaCommand("telemetry.off", "Off", CommandMenuEnum.None, () => { }));
                        commands.Execute("telemetry.off");
                        AssertFalse(capture.For("armada.tui.screen.views").Any(m => m.Tag(TuiTelemetry.TagRoute) == "/telemetry-off"), "no view");
                        AssertFalse(capture.For("armada.tui.commands").Any(m => m.Tag(TuiTelemetry.TagCommand) == "telemetry.off"), "no command");
                        AssertFalse(capture.For("armada.tui.approval.decisions").Any(m => m.Tag(TuiTelemetry.TagKind) == "StalledCaptain"), "no approval");
                    }
                }
                finally
                {
                    TuiTelemetry.Configure(true);
                }
            }, TestTags.Negative));

            cases.Add(TuiCase.Sync(Suite, "settings", "tui.json telemetry is off by default, round-trips, and maps to the server settings", () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "armada-tui-telemetry-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                try
                {
                    string file = Path.Combine(dir, "tui.json");
                    File.WriteAllText(file, "{ \"SchemaVersion\": 1 }");
                    PreferencesService prefs = new PreferencesService(file);
                    prefs.Load();
                    AssertFalse(prefs.Current.Telemetry.Enabled, "off by default");
                    AssertEqual("armada-tui", prefs.Current.Telemetry.ServiceName, "service name");
                    AssertFalse(prefs.Current.Telemetry.PrometheusEnabled, "no scrape endpoint by default");

                    File.WriteAllText(file, "{ \"Telemetry\": { \"Enabled\": true, \"OtlpEndpoint\": \" http://127.0.0.1:4317 \", \"PrometheusPort\": 70000, \"ServiceName\": \"\" } }");
                    prefs = new PreferencesService(file);
                    prefs.Load();
                    TuiTelemetrySettings tui = prefs.Current.Telemetry;
                    AssertTrue(tui.Enabled, "enabled");
                    AssertEqual(65535, tui.PrometheusPort, "port clamped");
                    TelemetrySettings server = tui.ToTelemetrySettings();
                    AssertTrue(server.Enabled, "server enabled");
                    AssertEqual("armada-tui", server.ServiceName, "empty name falls back");
                    AssertEqual("http://127.0.0.1:4317", server.OtlpEndpoint, "endpoint trimmed");
                    AssertFalse(server.PrometheusEnabled, "scrape off");
                    AssertNull(server.LokiEndpoint, "no loki");
                }
                finally
                {
                    try { Directory.Delete(dir, true); } catch (IOException) { }
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "host_factory", "The exporter factory runs only when telemetry is enabled", () =>
            {
                try
                {
                    int calls = 0;
                    TelemetrySettings? seen = null;
                    TuiStartOptions options = new TuiStartOptions();
                    options.TelemetryHostFactory = s => { calls++; seen = s; return new MemoryStream(); };

                    IDisposable? none = ArmadaTuiApp.StartTelemetry(new TuiTelemetrySettings(), options);
                    AssertNull(none, "no host when disabled");
                    AssertEqual(0, calls, "factory not called when disabled");
                    AssertFalse(TuiTelemetry.Enabled, "instruments off when disabled");
                    AssertFalse(TuiKitTelemetry.Enabled, "TUIKit off when disabled");

                    TuiTelemetrySettings on = new TuiTelemetrySettings();
                    on.Enabled = true;
                    on.OtlpEndpoint = "http://127.0.0.1:4317";
                    IDisposable? host = ArmadaTuiApp.StartTelemetry(on, options);
                    AssertNotNull(host, "host when enabled");
                    host!.Dispose();
                    AssertEqual(1, calls, "factory called once");
                    AssertEqual("http://127.0.0.1:4317", seen?.OtlpEndpoint, "settings passed");
                    AssertTrue(TuiTelemetry.Enabled, "instruments on");
                    AssertTrue(TuiKitTelemetry.Enabled, "TUIKit on");

                    options.TelemetryHostFactory = s => throw new InvalidOperationException("exporter failed");
                    AssertNull(ArmadaTuiApp.StartTelemetry(on, options), "a failing factory never blocks the TUI");
                }
                finally
                {
                    TuiTelemetry.Configure(true);
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI telemetry", cases: cases);
        }
    }
}
