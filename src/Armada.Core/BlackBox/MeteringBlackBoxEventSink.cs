namespace Armada.Core.BlackBox
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Wraps the event sink and records <see cref="BlackBoxMetrics"/> from the events that pass through it
    /// (blackbox.captured, blackbox.capture_failed, blackbox.gc_completed), then forwards every event unchanged.
    /// Thread-safe.
    /// </summary>
    public class MeteringBlackBoxEventSink : IBlackBoxEventSink
    {
        #region Public-Members

        /// <summary>
        /// The wrapped sink.
        /// </summary>
        public IBlackBoxEventSink Inner { get; }

        #endregion

        #region Private-Members

        private readonly DatabaseDriver? _Database;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="inner">Sink to forward to.</param>
        /// <param name="database">Database (reads the captured entry's triage), or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when inner is null.</exception>
        public MeteringBlackBoxEventSink(IBlackBoxEventSink inner, DatabaseDriver? database)
        {
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _Database = database;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task EmitAsync(string eventType, string message, string? tenantId, string? entryId, string? missionId, string? voyageId, string? vesselId, object payload, CancellationToken token = default)
        {
            try
            {
                if (eventType == BlackBoxEventTypes.Captured && payload is BlackBoxCapturedPayload captured)
                {
                    BlackBoxTriageEnum triage = BlackBoxTriageEnum.Unknown;
                    if (_Database != null && !String.IsNullOrEmpty(captured.EntryId))
                    {
                        BlackBoxEntry? entry = await _Database.BlackBoxEntries.ReadAsync(captured.EntryId, null, false, token).ConfigureAwait(false);
                        if (entry != null) triage = entry.Triage;
                    }
                    BlackBoxMetrics.RecordEntry(captured.Phase, captured.Step, captured.Kind, triage);
                }
                else if (eventType == BlackBoxEventTypes.CaptureFailed)
                {
                    BlackBoxMetrics.RecordCaptureFailure();
                }
                else if (eventType == BlackBoxEventTypes.GcCompleted)
                {
                    BlackBoxMetrics.RecordGc(payload as BlackBoxGcReport);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Metrics never stop an event.
            }
            await Inner.EmitAsync(eventType, message, tenantId, entryId, missionId, voyageId, vesselId, payload, token).ConfigureAwait(false);
        }

        #endregion
    }
}
