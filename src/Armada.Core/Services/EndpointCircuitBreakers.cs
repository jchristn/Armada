namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// The per-endpoint circuit breakers (RS-3.7): opened after repeated failed runs, closed by a clean run, and kept
    /// across a restart in the recovery state store. The only owner of breaker state.
    /// </summary>
    internal sealed class EndpointCircuitBreakers
    {
        private const int _Threshold = 3;
        private static readonly TimeSpan _OpenFor = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan _Retention = TimeSpan.FromDays(1);

        private readonly ConcurrentDictionary<string, EndpointBreakerState> _Breakers = new ConcurrentDictionary<string, EndpointBreakerState>(StringComparer.OrdinalIgnoreCase);
        private readonly RecoveryStateStore _Store;
        private readonly LoggingModule _Logging;
        private readonly string _Header;
        private readonly Action<TimeSpan> _OnOpened;

        /// <summary>Instantiate.</summary>
        /// <param name="store">Recovery state store the breakers persist to.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="header">Log header of the owning service.</param>
        /// <param name="onOpened">Called with the open duration when a breaker opens, to wake dispatch when it closes.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        internal EndpointCircuitBreakers(RecoveryStateStore store, LoggingModule logging, string header, Action<TimeSpan> onOpened)
        {
            _Store = store ?? throw new ArgumentNullException(nameof(store));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Header = header ?? throw new ArgumentNullException(nameof(header));
            _OnOpened = onOpened ?? throw new ArgumentNullException(nameof(onOpened));
        }

        /// <summary>True when the breaker for the endpoint key is open now.</summary>
        /// <param name="key">Endpoint capacity key.</param>
        /// <returns>True when open.</returns>
        internal bool IsOpen(string key)
        {
            if (!_Breakers.TryGetValue(key, out EndpointBreakerState? state)) return false;
            lock (state) return state.OpenUntilUtc.HasValue && state.OpenUntilUtc.Value > DateTime.UtcNow;
        }

        /// <summary>Record one run's outcome against an endpoint and persist the change.</summary>
        /// <param name="key">Endpoint capacity key.</param>
        /// <param name="baseUrl">Endpoint base URL, for logging.</param>
        /// <param name="healthy">True when the run was clean.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        internal async Task RecordAsync(string key, string? baseUrl, bool healthy, CancellationToken token)
        {
            EndpointBreakerState state = _Breakers.GetOrAdd(key, _ => new EndpointBreakerState());
            EndpointBreakerState snapshot = new EndpointBreakerState();
            bool wasClean;
            lock (state)
            {
                wasClean = state.ConsecutiveFailures == 0 && !state.OpenUntilUtc.HasValue;
                if (healthy)
                {
                    if (state.OpenUntilUtc.HasValue) _Logging.Info(_Header + "endpoint " + baseUrl + " ran cleanly; circuit closed");
                    state.ConsecutiveFailures = 0;
                    state.OpenUntilUtc = null;
                }
                else
                {
                    state.ConsecutiveFailures++;
                    if (state.ConsecutiveFailures >= _Threshold)
                    {
                        state.OpenUntilUtc = DateTime.UtcNow.Add(_OpenFor);
                        _Logging.Warn(_Header + "endpoint " + baseUrl + " failed " + state.ConsecutiveFailures + " run(s) in a row; no new missions go to it until " + state.OpenUntilUtc.Value.ToString("o"));
                        _OnOpened(_OpenFor);
                    }
                }
                snapshot.ConsecutiveFailures = state.ConsecutiveFailures;
                snapshot.OpenUntilUtc = state.OpenUntilUtc;
            }

            // Kept across a restart so an Admiral restarted mid-outage does not send the next missions straight back to
            // the failing endpoint. A clean run on a clean breaker writes nothing.
            if (healthy)
            {
                if (!wasClean) await _Store.DeleteAsync(RecoveryStateKinds.EndpointBreaker, key, token).ConfigureAwait(false);
            }
            else
            {
                await _Store.SaveAsync(RecoveryStateKinds.EndpointBreaker, key, snapshot, (snapshot.OpenUntilUtc ?? DateTime.UtcNow).Add(_Retention), token).ConfigureAwait(false);
            }
        }

        /// <summary>Load the breakers a previous run left.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        internal async Task LoadAsync(CancellationToken token)
        {
            foreach (KeyValuePair<string, EndpointBreakerState> breaker in await _Store.LoadAsync<EndpointBreakerState>(RecoveryStateKinds.EndpointBreaker, token).ConfigureAwait(false))
                _Breakers[breaker.Key] = breaker.Value;
        }
    }
}
