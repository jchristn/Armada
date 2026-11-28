namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// First-use context discovery on the Admiral (RELIABILITY_SIMPLICITY_PLAN.md RS-7.8). Before an in-process API
    /// captain's first turn, an endpoint that configures no window gets the one its model server reports, and the value is
    /// stored on the endpoint as discovered. A Harbor discovers on its own host and reports what it found in its exit
    /// message, which <see cref="RecordReportedAsync"/> stores. A configured window always wins and is never overwritten.
    /// </summary>
    public class ModelContextDiscoveryService
    {
        #region Public-Members

        /// <summary>
        /// How long a stored discovered window is reused before the server is asked again (10 minutes), and how long a
        /// failed discovery is not retried, so a server that reports nothing costs one request per window, not one per
        /// launch.
        /// </summary>
        public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(10);

        #endregion

        #region Private-Members

        private readonly string _Header = "[ModelContextDiscoveryService] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;
        private readonly Func<DateTime> _Clock;
        private readonly Func<ModelEndpoint, string?, CancellationToken, Task<ModelContextDiscoveryResult>> _Discover;
        private readonly ConcurrentDictionary<string, DateTime> _LastMiss = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="discover">Optional discovery seam for tests; defaults to <see cref="ModelContextDiscovery.DiscoverAsync"/>.</param>
        /// <param name="clock">Optional clock for tests; defaults to UTC now.</param>
        /// <exception cref="ArgumentNullException">Thrown when database or logging is null.</exception>
        public ModelContextDiscoveryService(
            DatabaseDriver database,
            LoggingModule logging,
            Func<ModelEndpoint, string?, CancellationToken, Task<ModelContextDiscoveryResult>>? discover = null,
            Func<DateTime>? clock = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Discover = discover ?? ModelContextDiscovery.DiscoverAsync;
            _Clock = clock ?? (() => DateTime.UtcNow);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Give a run snapshot of an endpoint its discovered window when it configures none: reuse a stored value read in
        /// the last <see cref="RefreshInterval"/>, otherwise ask the model server on this host and store what it reports.
        /// Never throws for server, network or database failures; the run then keeps whatever window it had.
        /// </summary>
        /// <param name="snapshot">Run snapshot of the endpoint; its discovered fields are updated in place.</param>
        /// <param name="model">Model the run uses, or null for the endpoint's model.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when snapshot is null.</exception>
        public async Task EnsureAsync(ModelEndpoint snapshot, string? model, CancellationToken token = default)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.InferenceOptions.ContextLength.HasValue || !ModelContextDiscovery.Applies(snapshot)) return;
            DateTime now = _Clock();
            if (snapshot.DiscoveredContextLength.HasValue && snapshot.ContextDiscoveredUtc.HasValue
                && now - snapshot.ContextDiscoveredUtc.Value < RefreshInterval) return;
            string key = snapshot.Id + "\n" + (model ?? String.Empty);
            if (_LastMiss.TryGetValue(key, out DateTime missed) && now - missed < RefreshInterval) return;

            ModelContextDiscoveryResult found;
            try
            {
                found = await _Discover(snapshot, model, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                found = new ModelContextDiscoveryResult { Reason = ex.Message };
            }

            if (!found.Found)
            {
                _LastMiss[key] = now;
                _Logging.Debug(_Header + "no context window for endpoint " + snapshot.Id + ": " + (found.Reason ?? "not reported"));
                return;
            }

            _LastMiss.TryRemove(key, out DateTime _);
            ModelContextDiscovery.Apply(snapshot, found, now);
            // A run on a model other than the endpoint's own keeps the window for this run only.
            if (!String.IsNullOrWhiteSpace(model) && !String.Equals(model, snapshot.Model, StringComparison.OrdinalIgnoreCase)) return;
            await StoreAsync(snapshot.Id, found.ContextLength!.Value, found.Source!, now, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Store a window a Harbor read from the model server for an endpoint. Ignored when the endpoint is gone, configures
        /// a window, or already stores the same value from the same source.
        /// </summary>
        /// <param name="endpointId">Endpoint identifier.</param>
        /// <param name="contextLength">Reported window in tokens.</param>
        /// <param name="source">Where the Harbor read it.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the value was stored.</returns>
        public async Task<bool> RecordReportedAsync(string? endpointId, int? contextLength, string? source, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(endpointId) || !contextLength.HasValue || String.IsNullOrWhiteSpace(source)) return false;
            if (contextLength.Value < ModelContextDiscovery.MinContextLength || contextLength.Value > ModelContextDiscovery.MaxContextLength) return false;
            try
            {
                ModelEndpoint? endpoint = await _Database.ModelEndpoints.ReadAsync(endpointId!, token).ConfigureAwait(false);
                if (endpoint == null || endpoint.InferenceOptions.ContextLength.HasValue) return false;
                if (endpoint.DiscoveredContextLength == contextLength && String.Equals(endpoint.DiscoveredContextSource, source, StringComparison.Ordinal)) return false;
                return await StoreAsync(endpoint.Id, contextLength.Value, source!, _Clock(), token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "could not read endpoint " + endpointId + " to record a reported context window: " + ex.Message);
                return false;
            }
        }

        #endregion

        #region Private-Methods

        private async Task<bool> StoreAsync(string endpointId, int contextLength, string source, DateTime utc, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(endpointId)) return false;
            try
            {
                bool stored = await _Database.ModelEndpoints.UpdateContextDiscoveryAsync(endpointId, contextLength, source, utc, token).ConfigureAwait(false);
                if (stored) _Logging.Info(_Header + "endpoint " + endpointId + " serves a " + contextLength + "-token context (" + source + ")");
                return stored;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "could not store the discovered context window of endpoint " + endpointId + ": " + ex.Message);
                return false;
            }
        }

        #endregion
    }
}
