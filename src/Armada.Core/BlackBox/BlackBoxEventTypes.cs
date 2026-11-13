namespace Armada.Core.BlackBox
{
    /// <summary>
    /// Black box event types (ArmadaEvent.EventType, also broadcast over the WebSocket). Payloads are typed classes in
    /// Armada.Core.Models (docs/BLACKBOX.md, "Events").
    /// </summary>
    public static class BlackBoxEventTypes
    {
        /// <summary>An entry was captured (BlackBoxCapturedPayload).</summary>
        public const string Captured = "blackbox.captured";

        /// <summary>A capture failed; the sweeper retries (BlackBoxCaptureFailedPayload).</summary>
        public const string CaptureFailed = "blackbox.capture_failed";

        /// <summary>Enrichment and analysis finished (BlackBoxAnalyzedPayload).</summary>
        public const string Analyzed = "blackbox.analyzed";

        /// <summary>An entry was resolved or reopened (BlackBoxResolvedPayload).</summary>
        public const string Resolved = "blackbox.resolved";

        /// <summary>An entry was deleted (BlackBoxDeletedPayload).</summary>
        public const string Deleted = "blackbox.deleted";

        /// <summary>Garbage collection finished (BlackBoxGcReport).</summary>
        public const string GcCompleted = "blackbox.gc_completed";

        /// <summary>An external write fell back to the database (BlackBoxStorageFallbackPayload).</summary>
        public const string StorageFallback = "blackbox.storage_fallback";

        /// <summary>A recovery path ran (BlackBoxRecoveryRunPayload).</summary>
        public const string RecoveryRun = "blackbox.recovery_run";

        /// <summary>Entity type recorded on black box events.</summary>
        public const string EntityType = "blackbox";
    }
}
