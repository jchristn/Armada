namespace Armada.Core.Services.Health
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;

    /// <summary>
    /// Best-effort telemetry for vessel health evaluation: metrics on the shared <see cref="ArmadaMetrics"/> meter and
    /// spans on an activity source named <see cref="ArmadaMetrics.MeterName"/> (which the server telemetry host already
    /// observes). Metric labels are low-cardinality (outcome, criterion); vessel identifiers appear only on span
    /// attributes. Telemetry failures are swallowed and never fail an evaluation. Thread-safe.
    /// </summary>
    public static class VesselHealthTelemetry
    {
        #region Public-Members

        /// <summary>
        /// Activity source for health evaluation spans.
        /// </summary>
        public static readonly ActivitySource Source = new ActivitySource(ArmadaMetrics.MeterName);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start a span, or return null when nothing is listening or telemetry fails.
        /// </summary>
        /// <param name="name">Span name.</param>
        /// <returns>The activity, or null.</returns>
        public static Activity? StartSpan(string name)
        {
            try
            {
                return Source.StartActivity(name, ActivityKind.Internal);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>
        /// Record one vessel evaluation outcome.
        /// </summary>
        /// <param name="outcome">Outcome label (Succeeded, Failed, Cancelled).</param>
        public static void RecordEvaluation(string outcome)
        {
            try
            {
                ArmadaMetrics.HealthEvaluations.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
            }
            catch (InvalidOperationException)
            {
            }
        }

        /// <summary>
        /// Record one criterion duration.
        /// </summary>
        /// <param name="criterion">Criterion code.</param>
        /// <param name="seconds">Duration in seconds.</param>
        public static void RecordCriterionDuration(string criterion, double seconds)
        {
            try
            {
                ArmadaMetrics.HealthCriterionDuration.Record(seconds, new KeyValuePair<string, object?>("criterion", criterion));
            }
            catch (InvalidOperationException)
            {
            }
        }

        #endregion
    }
}
