namespace Armada.Core.BlackBox
{
    /// <summary>
    /// Queue of entries waiting for the enrichment pipeline (enrich, analyze, re-render briefs, update state). Capture
    /// enqueues every new entry; the sweeper requeues EnrichmentFailed and stale Captured entries. Implementations must be
    /// thread-safe and must never block the caller.
    /// </summary>
    public interface IBlackBoxEnrichmentQueue
    {
        /// <summary>
        /// Entries waiting.
        /// </summary>
        int Pending { get; }

        /// <summary>
        /// Queue an entry. Returns false when the queue is full or stopped (the sweeper picks the entry up later).
        /// </summary>
        /// <param name="entryId">Entry id.</param>
        /// <returns>True when queued.</returns>
        bool Enqueue(string entryId);
    }
}
