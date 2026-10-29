namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Metrics;
    using System.Linq;
    using System.Threading;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Black box instruments on Armada's meter (<see cref="ArmadaMetrics.MeterName"/>), exported by the Admiral's
    /// telemetry host (Prometheus names in parentheses): armada.blackbox.entries (armada_blackbox_entries_total; phase,
    /// step, kind, triage), armada.blackbox.bytes (armada_blackbox_bytes; target), armada.blackbox.gc_deleted
    /// (armada_blackbox_gc_deleted_total; reason) and armada.blackbox.capture_failures
    /// (armada_blackbox_capture_failures_total). Recording never throws. Thread-safe.
    /// </summary>
    public static class BlackBoxMetrics
    {
        // Declared first: static fields initialize in textual order and every instrument below needs the meter.
        private static readonly Meter _Meter = new Meter(ArmadaMetrics.MeterName);

        #region Public-Members

        /// <summary>
        /// Entries captured.
        /// </summary>
        public static readonly Counter<long> Entries = _Meter.CreateCounter<long>("armada.blackbox.entries", null, "Black box entries captured");

        /// <summary>
        /// Entries and orphaned objects deleted by garbage collection.
        /// </summary>
        public static readonly Counter<long> GcDeleted = _Meter.CreateCounter<long>("armada.blackbox.gc_deleted", null, "Black box entries and orphaned objects deleted by garbage collection");

        /// <summary>
        /// Captures that failed.
        /// </summary>
        public static readonly Counter<long> CaptureFailures = _Meter.CreateCounter<long>("armada.blackbox.capture_failures", null, "Black box captures that failed");

        #endregion

        #region Private-Members

        private static Dictionary<string, long> _Bytes = new Dictionary<string, long>();
        private static readonly ObservableGauge<long> _BytesGauge = _Meter.CreateObservableGauge<long>("armada.blackbox.bytes", ObserveBytes, null, "Black box stored bytes per storage target");

        #endregion

        #region Public-Methods

        /// <summary>
        /// Count a captured entry.
        /// </summary>
        /// <param name="phase">Phase.</param>
        /// <param name="step">Step.</param>
        /// <param name="kind">Failure kind, or null.</param>
        /// <param name="triage">Triage at capture.</param>
        public static void RecordEntry(BlackBoxPhaseEnum phase, BlackBoxStepEnum step, MissionFailureKindEnum? kind, BlackBoxTriageEnum triage)
        {
            try
            {
                Entries.Add(1,
                    new KeyValuePair<string, object?>("phase", phase.ToString()),
                    new KeyValuePair<string, object?>("step", step.ToString()),
                    new KeyValuePair<string, object?>("kind", kind?.ToString() ?? "None"),
                    new KeyValuePair<string, object?>("triage", triage.ToString()));
            }
            catch (Exception)
            {
                // Telemetry is best effort.
            }
        }

        /// <summary>
        /// Count a failed capture.
        /// </summary>
        public static void RecordCaptureFailure()
        {
            try { CaptureFailures.Add(1); }
            catch (Exception) { /* Telemetry is best effort. */ }
        }

        /// <summary>
        /// Count what a garbage collection run deleted.
        /// </summary>
        /// <param name="report">Report (dry runs are ignored).</param>
        public static void RecordGc(BlackBoxGcReport? report)
        {
            if (report == null || report.DryRun) return;
            try
            {
                Add("expired", report.Expired);
                Add("evicted", report.Evicted);
                Add("cascaded", report.Cascaded);
                Add("retried", report.RetriedDeletes);
                Add("orphan", report.OrphansRemoved);
            }
            catch (Exception)
            {
                // Telemetry is best effort.
            }
        }

        /// <summary>
        /// Replace the bytes-per-target snapshot the gauge reports.
        /// </summary>
        /// <param name="usage">Usage per target.</param>
        public static void SetBytes(IEnumerable<BlackBoxStorageTargetUsage>? usage)
        {
            if (usage == null) return;
            Dictionary<string, long> next = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (BlackBoxStorageTargetUsage row in usage) next[row.Target.ToString()] = next.GetValueOrDefault(row.Target.ToString()) + row.Bytes;
            Volatile.Write(ref _Bytes, next);
        }

        /// <summary>
        /// The bytes-per-target snapshot (for tests and diagnostics).
        /// </summary>
        /// <returns>A copy of the snapshot.</returns>
        public static Dictionary<string, long> BytesSnapshot()
        {
            return new Dictionary<string, long>(Volatile.Read(ref _Bytes), StringComparer.Ordinal);
        }

        #endregion

        #region Private-Methods

        private static void Add(string reason, long count)
        {
            if (count > 0) GcDeleted.Add(count, new KeyValuePair<string, object?>("reason", reason));
        }

        private static IEnumerable<Measurement<long>> ObserveBytes()
        {
            return Volatile.Read(ref _Bytes).Select(b => new Measurement<long>(b.Value, new KeyValuePair<string, object?>("target", b.Key))).ToArray();
        }

        #endregion
    }
}
