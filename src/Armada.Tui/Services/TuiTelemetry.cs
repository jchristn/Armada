namespace Armada.Tui.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using TUIKit.Diagnostics;

    /// <summary>
    /// The TUI's telemetry instruments. Like <c>Armada.Core.ArmadaMetrics</c>, emission rides the .NET base class
    /// library (<see cref="Meter"/>, <see cref="ActivitySource"/>) and costs nothing until a listener subscribes, so the
    /// TUI takes no dependency on an exporter. Helm's <c>armada tui</c> starts an exporter only when the
    /// <c>Telemetry</c> section of <c>tui.json</c> enables it (off by default) and then subscribes to
    /// <see cref="MeterName"/>, <see cref="ActivitySourceName"/>, and TUIKit's own <c>TUIKit</c> meter and activity
    /// source. Tags carry only low-cardinality values (route patterns, command ids, approval kinds and decisions);
    /// entity ids, titles, and message text are never tagged. Units are left unset on counters so the Prometheus
    /// exporter yields clean <c>armada_tui_*_total</c> series names.
    /// </summary>
    public static class TuiTelemetry
    {
        #region Public-Members

        /// <summary>
        /// The meter name a telemetry host subscribes to.
        /// </summary>
        public const string MeterName = "Armada.Tui";

        /// <summary>
        /// The activity source name a telemetry host subscribes to.
        /// </summary>
        public const string ActivitySourceName = "Armada.Tui";

        /// <summary>
        /// Command source: a key binding.
        /// </summary>
        public const string SourceKey = "key";

        /// <summary>
        /// Command source: the command palette.
        /// </summary>
        public const string SourcePalette = "palette";

        /// <summary>
        /// Command source: the menu bar.
        /// </summary>
        public const string SourceMenu = "menu";

        /// <summary>
        /// Command source: code or a mouse click (anything else).
        /// </summary>
        public const string SourceDirect = "direct";

        /// <summary>
        /// Master switch for the TUI instruments. Default true, which is free while nothing listens; Helm sets it (and
        /// TUIKit's <see cref="TuiKitTelemetry.Enabled"/>) from the <c>tui.json</c> telemetry settings at start.
        /// </summary>
        public static bool Enabled
        {
            get { return _Enabled; }
            set { _Enabled = value; }
        }

        /// <summary>
        /// The meter.
        /// </summary>
        public static Meter Meter
        {
            get { return _Meter; }
        }

        /// <summary>
        /// The activity source.
        /// </summary>
        public static ActivitySource ActivitySource
        {
            get { return _ActivitySource; }
        }

        /// <summary>
        /// TUI sessions started (armada_tui_sessions_total).
        /// </summary>
        public static readonly Counter<long> Sessions;

        /// <summary>
        /// Screens shown, tagged by route pattern and screen (armada_tui_screen_views_total).
        /// </summary>
        public static readonly Counter<long> ScreenViews;

        /// <summary>
        /// Commands run, tagged by command id, source, and outcome (armada_tui_commands_total).
        /// </summary>
        public static readonly Counter<long> Commands;

        /// <summary>
        /// Approval decisions, tagged by kind and decision (armada_tui_approval_decisions_total).
        /// </summary>
        public static readonly Counter<long> ApprovalDecisions;

        /// <summary>
        /// Seconds from an approval item first reaching the TUI to the operator's decision, tagged by kind and
        /// decision (armada_tui_approval_latency_seconds).
        /// </summary>
        public static readonly Histogram<double> ApprovalLatency;

        /// <summary>
        /// Ask Armada messages sent (armada_tui_ask_messages_total).
        /// </summary>
        public static readonly Counter<long> AskMessages;

        /// <summary>
        /// Tag key: route pattern.
        /// </summary>
        public const string TagRoute = "route";

        /// <summary>
        /// Tag key: screen class name.
        /// </summary>
        public const string TagScreen = "screen";

        /// <summary>
        /// Tag key: command id.
        /// </summary>
        public const string TagCommand = "command";

        /// <summary>
        /// Tag key: command source.
        /// </summary>
        public const string TagSource = "source";

        /// <summary>
        /// Tag key: outcome (ok or error).
        /// </summary>
        public const string TagOutcome = "outcome";

        /// <summary>
        /// Tag key: approval kind.
        /// </summary>
        public const string TagKind = "kind";

        /// <summary>
        /// Tag key: approval decision.
        /// </summary>
        public const string TagDecision = "decision";

        #endregion

        #region Private-Members

        private static readonly Meter _Meter = new Meter(MeterName, Armada.Core.Constants.ProductVersion);
        private static readonly ActivitySource _ActivitySource = new ActivitySource(ActivitySourceName, Armada.Core.Constants.ProductVersion);
        private static volatile bool _Enabled = true;

        #endregion

        #region Constructors-and-Factories

        static TuiTelemetry()
        {
            Sessions = _Meter.CreateCounter<long>("armada.tui.sessions", null, "TUI sessions started");
            ScreenViews = _Meter.CreateCounter<long>("armada.tui.screen.views", null, "TUI screens shown");
            Commands = _Meter.CreateCounter<long>("armada.tui.commands", null, "TUI commands run");
            ApprovalDecisions = _Meter.CreateCounter<long>("armada.tui.approval.decisions", null, "TUI approval decisions");
            ApprovalLatency = _Meter.CreateHistogram<double>("armada.tui.approval.latency", "s", "Time from an approval arriving to the decision");
            AskMessages = _Meter.CreateCounter<long>("armada.tui.ask.messages", null, "Ask Armada messages sent");
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Turn the TUI and TUIKit instruments on or off together.
        /// </summary>
        /// <param name="enabled">Enabled.</param>
        public static void Configure(bool enabled)
        {
            _Enabled = enabled;
            TuiKitTelemetry.Enabled = enabled;
        }

        /// <summary>
        /// Count a session start.
        /// </summary>
        public static void RecordSession()
        {
            if (!_Enabled) return;
            Safe(() => Sessions.Add(1));
        }

        /// <summary>
        /// Count a screen view.
        /// </summary>
        /// <param name="routePattern">Route pattern (for example <c>/missions/:id</c>), never a concrete path.</param>
        /// <param name="screen">Screen name.</param>
        public static void RecordScreenView(string routePattern, string screen)
        {
            if (!_Enabled) return;
            Safe(() => ScreenViews.Add(1,
                new KeyValuePair<string, object?>(TagRoute, routePattern ?? ""),
                new KeyValuePair<string, object?>(TagScreen, screen ?? "")));
        }

        /// <summary>
        /// Start a span for a command (null when nothing listens or telemetry is off).
        /// </summary>
        /// <param name="commandId">Command id.</param>
        /// <param name="source">Command source.</param>
        /// <returns>The activity, or null.</returns>
        public static Activity? StartCommand(string commandId, string source)
        {
            if (!_Enabled) return null;
            try
            {
                Activity? activity = _ActivitySource.StartActivity("armada.tui.command", ActivityKind.Internal);
                if (activity != null)
                {
                    activity.SetTag(TagCommand, commandId);
                    activity.SetTag(TagSource, source);
                }

                return activity;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Count a command run and close its span.
        /// </summary>
        /// <param name="activity">Span from <see cref="StartCommand"/>, or null.</param>
        /// <param name="commandId">Command id.</param>
        /// <param name="source">Command source.</param>
        /// <param name="ok">True when the handler returned normally.</param>
        public static void RecordCommand(Activity? activity, string commandId, string source, bool ok)
        {
            string outcome = ok ? "ok" : "error";
            if (activity != null)
            {
                Safe(() =>
                {
                    activity.SetTag(TagOutcome, outcome);
                    if (!ok) activity.SetStatus(ActivityStatusCode.Error);
                    activity.Dispose();
                });
            }

            if (!_Enabled) return;
            Safe(() => Commands.Add(1,
                new KeyValuePair<string, object?>(TagCommand, commandId ?? ""),
                new KeyValuePair<string, object?>(TagSource, source ?? SourceDirect),
                new KeyValuePair<string, object?>(TagOutcome, outcome)));
        }

        /// <summary>
        /// Count an approval decision and record its latency.
        /// </summary>
        /// <param name="kind">Approval kind.</param>
        /// <param name="decision">Decision (approve, reject, deny, conditional, more_work, retry, stop, recall, restart).</param>
        /// <param name="arrivedUtc">When the item first reached the TUI, or null when unknown (no latency recorded).</param>
        /// <param name="nowUtc">Decision time.</param>
        public static void RecordApproval(ApprovalKindEnum kind, string decision, DateTime? arrivedUtc, DateTime nowUtc)
        {
            if (!_Enabled) return;
            KeyValuePair<string, object?> kindTag = new KeyValuePair<string, object?>(TagKind, kind.ToString());
            KeyValuePair<string, object?> decisionTag = new KeyValuePair<string, object?>(TagDecision, decision ?? "");
            Safe(() => ApprovalDecisions.Add(1, kindTag, decisionTag));
            if (arrivedUtc == null) return;
            double seconds = Math.Max(0, (nowUtc - arrivedUtc.Value).TotalSeconds);
            Safe(() => ApprovalLatency.Record(seconds, kindTag, decisionTag));
        }

        /// <summary>
        /// Count an Ask Armada message sent.
        /// </summary>
        public static void RecordAskMessage()
        {
            if (!_Enabled) return;
            Safe(() => AskMessages.Add(1));
        }

        #endregion

        #region Private-Methods

        private static void Safe(Action action)
        {
            try
            {
                action();
            }
            catch (Exception)
            {
                // A throwing listener never changes TUI behavior.
            }
        }

        #endregion
    }
}
