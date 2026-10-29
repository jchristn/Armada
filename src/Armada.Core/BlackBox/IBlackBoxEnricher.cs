namespace Armada.Core.BlackBox
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// One enrichment step of the black box pipeline (git forensics, attempts, evidence, related entries). Enrichers record
    /// facts only; interpretation belongs to <see cref="IBlackBoxAdvisor"/>. An enricher that throws marks the entry
    /// EnrichmentFailed (the sweeper retries with backoff). Implementations must be thread-safe.
    /// TODO(blackbox-wave2: enrichment) git forensics, conflict attribution, attempts, evidence objects.
    /// </summary>
    public interface IBlackBoxEnricher
    {
        /// <summary>
        /// Name, for logs and errors.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Enrich an entry in place.
        /// </summary>
        /// <param name="context">Entry, record and collectors.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task EnrichAsync(BlackBoxEnrichmentContext context, CancellationToken token = default);
    }
}
