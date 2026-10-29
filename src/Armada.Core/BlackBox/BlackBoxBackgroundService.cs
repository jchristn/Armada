namespace Armada.Core.BlackBox
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Runs the black box background work in the Admiral: the enrichment queue worker, the sweeper every
    /// BlackBox.Sweeper.IntervalSeconds (followed by the re-analysis of entries with older rules) and garbage collection every BlackBox.Retention.GcIntervalMinutes. Intervals and
    /// BlackBox.Enabled are read on every tick, so settings changes apply without a restart. Thread-safe.
    /// </summary>
    public class BlackBoxBackgroundService : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Storage migrator whose unfinished jobs resume on <see cref="Start"/> and stop on <see cref="StopAsync"/>, or null.
        /// </summary>
        public Armada.Core.BlackBox.Storage.BlackBoxStorageMigrator? Migrator { get; set; } = null;

        /// <summary>
        /// Background re-analysis of entries analyzed with older rules, run after each sweep, or null.
        /// </summary>
        public BlackBoxReanalyzer? Reanalyzer { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly string _Header = "[BlackBoxBackgroundService] ";
        private readonly ArmadaSettings _Settings;
        private readonly BlackBoxEnrichmentQueue _Queue;
        private readonly BlackBoxSweeper _Sweeper;
        private readonly BlackBoxGarbageCollector _Gc;
        private readonly LoggingModule _Logging;
        private CancellationTokenSource? _Cts;
        private Task? _SweepLoop;
        private Task? _GcLoop;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Armada settings.</param>
        /// <param name="queue">Enrichment queue.</param>
        /// <param name="sweeper">Sweeper.</param>
        /// <param name="gc">Garbage collector.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxBackgroundService(ArmadaSettings settings, BlackBoxEnrichmentQueue queue, BlackBoxSweeper sweeper, BlackBoxGarbageCollector gc, LoggingModule logging)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _Sweeper = sweeper ?? throw new ArgumentNullException(nameof(sweeper));
            _Gc = gc ?? throw new ArgumentNullException(nameof(gc));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the queue worker and the loops. Idempotent.
        /// </summary>
        public void Start()
        {
            if (_Cts != null) return;
            _Cts = new CancellationTokenSource();
            CancellationToken token = _Cts.Token;
            _Queue.Start();
            _SweepLoop = Task.Run(() => SweepLoopAsync(token));
            _GcLoop = Task.Run(() => GcLoopAsync(token));
            Migrator?.ResumePendingAsync();
            _Logging.Info(_Header + "started");
        }

        /// <summary>
        /// Stop the loops and the queue worker.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task StopAsync()
        {
            if (_Cts == null) return;
            _Cts.Cancel();
            try
            {
                if (_SweepLoop != null) await _SweepLoop.ConfigureAwait(false);
                if (_GcLoop != null) await _GcLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            if (Migrator != null) await Migrator.StopAsync().ConfigureAwait(false);
            await _Queue.StopAsync().ConfigureAwait(false);
            _Cts.Dispose();
            _Cts = null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _Cts?.Cancel();
            _Cts?.Dispose();
            _Cts = null;
        }

        #endregion

        #region Private-Methods

        private async Task SweepLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_Settings.BlackBox.Sweeper.IntervalSeconds), token).ConfigureAwait(false);
                    if (_Settings.BlackBox.Enabled) await _Sweeper.SweepAsync(token).ConfigureAwait(false);
                    BlackBoxReanalyzer? reanalyzer = Reanalyzer;
                    if (_Settings.BlackBox.Enabled && reanalyzer != null) await reanalyzer.RunOnceAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    _Logging.Warn(_Header + "sweep failed: " + e.Message);
                }
            }
        }

        private async Task GcLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    TimeSpan interval = TimeSpan.FromMinutes(_Settings.BlackBox.Retention.GcIntervalMinutes);
                    _Gc.NextRunUtc = DateTime.UtcNow + interval;
                    await Task.Delay(interval, token).ConfigureAwait(false);
                    if (_Settings.BlackBox.Enabled) await _Gc.RunAsync(false, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    _Logging.Warn(_Header + "garbage collection failed: " + e.Message);
                }
            }
        }

        #endregion
    }
}
