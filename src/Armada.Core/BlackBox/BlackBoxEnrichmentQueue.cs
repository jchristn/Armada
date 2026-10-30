namespace Armada.Core.BlackBox
{
    using System;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using SyslogLogging;

    /// <summary>
    /// In-process enrichment queue: a bounded channel (capacity 10000; full means the sweeper picks the entry up later)
    /// drained by one background worker that runs <see cref="BlackBoxPipeline.ProcessAsync"/>. Thread-safe.
    /// </summary>
    public class BlackBoxEnrichmentQueue : IBlackBoxEnrichmentQueue, IDisposable
    {
        #region Private-Members

        private readonly string _Header = "[BlackBoxEnrichmentQueue] ";
        private readonly BlackBoxPipeline _Pipeline;
        private readonly LoggingModule _Logging;
        private readonly Channel<string> _Channel;
        private CancellationTokenSource? _Cts;
        private Task? _Worker;
        private int _Pending;
        private bool _Disposed;

        #endregion

        #region Public-Members

        /// <inheritdoc />
        public int Pending => Volatile.Read(ref _Pending);

        /// <summary>
        /// Queue capacity.
        /// </summary>
        public const int Capacity = 10000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="pipeline">Pipeline.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxEnrichmentQueue(BlackBoxPipeline pipeline, LoggingModule logging)
        {
            _Pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Channel = Channel.CreateBounded<string>(new BoundedChannelOptions(Capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public bool Enqueue(string entryId)
        {
            if (String.IsNullOrEmpty(entryId) || _Disposed) return false;
            if (!_Channel.Writer.TryWrite(entryId)) return false;
            Interlocked.Increment(ref _Pending);
            return true;
        }

        /// <summary>
        /// Start the worker. Idempotent.
        /// </summary>
        public void Start()
        {
            if (_Worker != null) return;
            _Cts = new CancellationTokenSource();
            CancellationToken token = _Cts.Token;
            _Worker = Task.Run(() => RunAsync(token));
        }

        /// <summary>
        /// Process everything queued now, on the caller's task (tests and shutdown).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entries processed.</returns>
        public async Task<int> DrainAsync(CancellationToken token = default)
        {
            int processed = 0;
            while (_Channel.Reader.TryRead(out string? entryId))
            {
                Interlocked.Decrement(ref _Pending);
                await ProcessOneAsync(entryId, token).ConfigureAwait(false);
                processed++;
            }
            return processed;
        }

        /// <summary>
        /// Stop the worker.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task StopAsync()
        {
            if (_Cts == null) return;
            _Cts.Cancel();
            if (_Worker != null)
            {
                try { await _Worker.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            _Worker = null;
            _Cts.Dispose();
            _Cts = null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _Channel.Writer.TryComplete();
            _Cts?.Cancel();
            _Cts?.Dispose();
            _Cts = null;
        }

        #endregion

        #region Private-Methods

        private async Task RunAsync(CancellationToken token)
        {
            try
            {
                while (await _Channel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    while (_Channel.Reader.TryRead(out string? entryId))
                    {
                        Interlocked.Decrement(ref _Pending);
                        await ProcessOneAsync(entryId, token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }

        private async Task ProcessOneAsync(string entryId, CancellationToken token)
        {
            try
            {
                await _Pipeline.ProcessAsync(entryId, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "pipeline failed for " + entryId + ": " + e.Message);
            }
        }

        #endregion
    }
}
