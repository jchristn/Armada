namespace Armada.Core.BlackBox
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Enricher that does nothing; a placeholder and a template for real enrichers. Thread-safe.
    /// </summary>
    public class NoOpBlackBoxEnricher : IBlackBoxEnricher
    {
        /// <inheritdoc />
        public string Name => "NoOp";

        /// <inheritdoc />
        public Task EnrichAsync(BlackBoxEnrichmentContext context, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }
    }
}
