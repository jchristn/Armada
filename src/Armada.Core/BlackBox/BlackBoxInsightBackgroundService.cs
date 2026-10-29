namespace Armada.Core.BlackBox
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// The insight worker: every blackBox.insightIntervalSeconds (while blackBox.enabled) it records successful landings
    /// (<see cref="BlackBoxSuccessRecorder"/>), deletes expired success records, settles recovery outcomes
    /// (<see cref="BlackBoxRecoveryOutcomeTracker"/>) and refreshes the advisor's path success rates; once a day it
    /// deletes recovery outcomes older than a year. Thread-safe.
    /// </summary>
    public class BlackBoxInsightBackgroundService : IDisposable
    {
        #region Private-Members

        private readonly string _Header = "[BlackBoxInsightBackgroundService] ";
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly BlackBoxSuccessRecorder _Successes;
        private readonly BlackBoxRecoveryOutcomeTracker _Outcomes;
        private readonly LoggingModule _Logging;
        private CancellationTokenSource? _Cts;
        private Task? _Loop;
        private DateTime _LastOutcomeCleanupUtc = DateTime.MinValue;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Settings.</param>
        /// <param name="successes">Success recorder.</param>
        /// <param name="outcomes">Outcome tracker.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxInsightBackgroundService(DatabaseDriver database, ArmadaSettings settings, BlackBoxSuccessRecorder successes, BlackBoxRecoveryOutcomeTracker outcomes, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Successes = successes ?? throw new ArgumentNullException(nameof(successes));
            _Outcomes = outcomes ?? throw new ArgumentNullException(nameof(outcomes));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the worker (idempotent).
        /// </summary>
        public void Start()
        {
            if (_Cts != null) return;
            _Cts = new CancellationTokenSource();
            CancellationToken token = _Cts.Token;
            _Loop = Task.Run(() => LoopAsync(token));
            _Logging.Info(_Header + "started");
        }

        /// <summary>
        /// Stop the worker and wait for the current pass.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task StopAsync()
        {
            if (_Cts == null) return;
            _Cts.Cancel();
            try
            {
                if (_Loop != null) await _Loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            _Cts.Dispose();
            _Cts = null;
        }

        /// <summary>
        /// Run one pass now (also used by tests).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task RunOnceAsync(CancellationToken token = default)
        {
            await _Successes.RunOnceAsync(token).ConfigureAwait(false);
            await _Successes.CleanupAsync(token).ConfigureAwait(false);
            await _Outcomes.SettleAsync(token).ConfigureAwait(false);
            await _Outcomes.Stats.RefreshAsync(token).ConfigureAwait(false);
            if (DateTime.UtcNow - _LastOutcomeCleanupUtc > TimeSpan.FromDays(1))
            {
                await _Database.BlackBoxRecoveryOutcomes.DeleteCreatedBeforeAsync(DateTime.UtcNow.AddDays(-365), token).ConfigureAwait(false);
                _LastOutcomeCleanupUtc = DateTime.UtcNow;
            }
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

        private async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_Settings.BlackBox.InsightIntervalSeconds), token).ConfigureAwait(false);
                    if (_Settings.BlackBox.Enabled) await RunOnceAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    _Logging.Warn(_Header + "pass failed: " + e.Message);
                }
            }
        }

        #endregion
    }
}
