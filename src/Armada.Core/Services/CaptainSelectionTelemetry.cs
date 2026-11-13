namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Text.RegularExpressions;
    using System.Threading;
    using Armada.Core.Enums;

    /// <summary>
    /// Best-effort captain-selection telemetry on the existing Armada meter and activity source. Labels are bounded to
    /// mode, strategy, outcome, trigger, reason, stage and reservation state; tenant, user, captain, mission, account,
    /// snapshot, model and provider text never become metric labels. Durable decision history stays the audit source:
    /// these counters reset on restart and a failing exporter never affects selection or admission.
    /// </summary>
    public static class CaptainSelectionTelemetry
    {
        #region Public-Members

        /// <summary>Label used for any value that is not a bounded semantic code.</summary>
        public const string OtherLabel = "Other";

        #endregion

        #region Private-Members

        private static readonly Meter _Meter = new Meter(ArmadaMetrics.MeterName);
        private static readonly ActivitySource _Source = new ActivitySource(ArmadaMetrics.MeterName);
        private static readonly Regex _SemanticCode = new Regex("^[A-Z][A-Za-z]{1,63}$", RegexOptions.CultureInvariant);
        private static readonly Regex _StageName = new Regex("^[a-z]{1,32}$", RegexOptions.CultureInvariant);
        private static readonly Counter<long> _Decisions = _Meter.CreateCounter<long>("armada.selection.decisions");
        private static readonly Counter<long> _Waits = _Meter.CreateCounter<long>("armada.selection.waits");
        private static readonly Counter<long> _Divergence = _Meter.CreateCounter<long>("armada.selection.observe_divergence");
        private static readonly Counter<long> _PersistenceFailures = _Meter.CreateCounter<long>("armada.selection.persistence_failures");
        private static readonly Counter<long> _Reconciliation = _Meter.CreateCounter<long>("armada.selection.reconciliation");
        private static readonly Counter<long> _Cooldowns = _Meter.CreateCounter<long>("armada.selection.cooldowns");
        private static readonly Counter<long> _RetentionDeleted = _Meter.CreateCounter<long>("armada.selection.retention.deleted");
        private static readonly Histogram<double> _StageDuration = _Meter.CreateHistogram<double>("armada.selection.stage.duration", "s");
        private static readonly object _ReservationLock = new object();
        private static Dictionary<string, long> _Reservations = new Dictionary<string, long>(StringComparer.Ordinal);
        private static readonly ObservableGauge<long> _ReservationGauge = _Meter.CreateObservableGauge<long>("armada.selection.reservations", ObserveReservations);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Begin a timed selection stage with a correlated span. Stage names are fixed by callers (eligibility, evidence,
        /// evaluation, transaction, reconcile, retention). Disposing without <see cref="CaptainSelectionStage.Complete"/>
        /// records the stage as Error.
        /// </summary>
        /// <param name="stage">Fixed stage name.</param>
        /// <param name="missionId">Optional mission identifier, attached to the span only.</param>
        /// <returns>The stage scope.</returns>
        public static CaptainSelectionStage Stage(string stage, string? missionId = null)
            => new CaptainSelectionStage(stage != null && _StageName.IsMatch(stage) ? stage : "other", missionId);

        /// <summary>Count one recorded decision.</summary>
        /// <param name="mode">Policy mode the decision was recorded under.</param>
        /// <param name="outcome">Decision outcome code.</param>
        /// <param name="trigger">Decision trigger code.</param>
        public static void Decision(UsageSelectionModeEnum mode, string? outcome, string? trigger)
        {
            try { _Decisions.Add(1, new TagList { { "mode", mode.ToString() }, { "outcome", Label(outcome) }, { "trigger", Label(trigger) } }); }
            catch (Exception) { }
        }

        /// <summary>Count one materially new waiting state by its semantic reason.</summary>
        /// <param name="mode">Policy mode.</param>
        /// <param name="reason">Semantic reason code.</param>
        public static void Wait(UsageSelectionModeEnum mode, string? reason)
        {
            try { _Waits.Add(1, new TagList { { "mode", mode.ToString() }, { "reason", Label(reason) } }); }
            catch (Exception) { }
        }

        /// <summary>Count an Observe decision whose configured strategy would have chosen differently from actual routing.</summary>
        /// <param name="strategy">Configured strategy.</param>
        public static void ObserveDivergence(CaptainSelectionStrategyEnum strategy)
        {
            try { _Divergence.Add(1, new KeyValuePair<string, object?>("strategy", strategy.ToString())); }
            catch (Exception) { }
        }

        /// <summary>Count a ledger write that could not be confirmed; the mission waits and no process starts.</summary>
        /// <param name="stage">Fixed stage name (history or admission).</param>
        public static void PersistenceFailure(string stage)
        {
            try { _PersistenceFailures.Add(1, new KeyValuePair<string, object?>("stage", Label(stage))); }
            catch (Exception) { }
        }

        /// <summary>Count reservation reconciliation outcomes.</summary>
        /// <param name="outcome">Settled, Unsettled, Uncertain, Released or Unchanged.</param>
        /// <param name="count">Rows with that outcome.</param>
        public static void Reconciled(string outcome, int count = 1)
        {
            if (count <= 0) return;
            try { _Reconciliation.Add(count, new KeyValuePair<string, object?>("outcome", Label(outcome))); }
            catch (Exception) { }
        }

        /// <summary>Count a classified provider-limit exit by whether it recorded an account cooldown.</summary>
        /// <param name="outcome">Recorded or Ignored.</param>
        public static void Cooldown(string outcome)
        {
            try { _Cooldowns.Add(1, new KeyValuePair<string, object?>("outcome", Label(outcome))); }
            catch (Exception) { }
        }

        /// <summary>Count decisions removed by history retention.</summary>
        /// <param name="count">Decisions deleted.</param>
        public static void RetentionDeleted(int count)
        {
            if (count <= 0) return;
            try { _RetentionDeleted.Add(count); }
            catch (Exception) { }
        }

        /// <summary>Publish the latest reservation counts by state for the reservations gauge.</summary>
        /// <param name="counts">Count per reservation state.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="counts"/> is null.</exception>
        public static void Reservations(IReadOnlyDictionary<UsageReservationStateEnum, long> counts)
        {
            if (counts == null) throw new ArgumentNullException(nameof(counts));
            Dictionary<string, long> snapshot = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (UsageReservationStateEnum state in Enum.GetValues<UsageReservationStateEnum>())
                snapshot[state.ToString()] = counts.TryGetValue(state, out long value) ? value : 0;
            lock (_ReservationLock) _Reservations = snapshot;
        }

        /// <summary>
        /// Reduce a value to a bounded label: semantic PascalCase codes pass through; anything else, including free-form
        /// text, identifiers and empty values, becomes <see cref="OtherLabel"/>.
        /// </summary>
        /// <param name="value">Candidate label value.</param>
        /// <returns>The bounded label.</returns>
        public static string Label(string? value) => value != null && _SemanticCode.IsMatch(value) ? value : OtherLabel;

        #endregion

        #region Internal-Methods

        internal static Activity? StartActivity(string stage)
        {
            try { return _Source.StartActivity("selection." + stage); }
            catch (Exception) { return null; }
        }

        internal static void RecordStage(string stage, string outcome, double seconds)
        {
            try { _StageDuration.Record(seconds, new TagList { { "stage", stage }, { "outcome", Label(outcome) } }); }
            catch (Exception) { }
        }

        #endregion

        #region Private-Methods

        private static IEnumerable<Measurement<long>> ObserveReservations()
        {
            Dictionary<string, long> snapshot;
            lock (_ReservationLock) snapshot = _Reservations;
            List<Measurement<long>> measurements = new List<Measurement<long>>();
            foreach (KeyValuePair<string, long> pair in snapshot)
                measurements.Add(new Measurement<long>(pair.Value, new KeyValuePair<string, object?>("state", pair.Key)));
            return measurements;
        }

        #endregion
    }
}
