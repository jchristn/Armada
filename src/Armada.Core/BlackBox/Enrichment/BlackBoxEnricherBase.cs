namespace Armada.Core.BlackBox.Enrichment
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;

    /// <summary>
    /// Base of the built-in enrichers: forensics are best effort, so a failure (an unreachable host, a git error, a
    /// missing row) is logged and recorded as a truncation note naming the enricher, and never fails the entry. Only
    /// cancellation of the pipeline's token propagates.
    /// </summary>
    public abstract class BlackBoxEnricherBase : IBlackBoxEnricher
    {
        #region Public-Members

        /// <inheritdoc />
        public abstract string Name { get; }

        #endregion

        #region Private-Members

        /// <summary>
        /// Logging module.
        /// </summary>
        protected LoggingModule Logging { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when logging is null.</exception>
        protected BlackBoxEnricherBase(LoggingModule logging)
        {
            Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task EnrichAsync(BlackBoxEnrichmentContext context, CancellationToken token = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            try
            {
                await EnrichCoreAsync(context, token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException && token.IsCancellationRequested))
            {
                Logging.Warn("[BlackBox" + Name + "Enricher] " + context.Entry.Id + ": " + e.GetType().Name + ": " + e.Message);
                BlackBoxRecordBounder.AddNote(context.Record.Truncation, Name + " enrichment did not complete (" + e.GetType().Name + ": " + BlackBoxLimits.Clip(e.Message, 300) + "); its facts are partial or missing.");
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Enrich the entry. Exceptions are caught by <see cref="EnrichAsync"/>.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        protected abstract Task EnrichCoreAsync(BlackBoxEnrichmentContext context, CancellationToken token);

        #endregion
    }
}
