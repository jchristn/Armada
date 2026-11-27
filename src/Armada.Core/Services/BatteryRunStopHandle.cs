namespace Armada.Core.Services
{
    using System;
    using System.Threading;

    /// <summary>
    /// The stop switch of one executing battery run: a token source linked to the caller's own cancellation token (a Full
    /// Evaluate's), so either the caller or <see cref="BatteryRunService.CancelAsync"/> stops the run.
    /// </summary>
    public sealed class BatteryRunStopHandle : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Token source whose token the run watches.
        /// </summary>
        public CancellationTokenSource Source { get; }

        #endregion

        #region Private-Members

        private readonly BatteryRunOptions _Options;
        private readonly object _Lock = new object();
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate, linked to the options' current cancellation token.
        /// </summary>
        /// <param name="options">The run's options; the reason is written to its StopReason.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public BatteryRunStopHandle(BatteryRunOptions options)
        {
            _Options = options ?? throw new ArgumentNullException(nameof(options));
            Source = CancellationTokenSource.CreateLinkedTokenSource(options.Cancellation);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Stop the run. The first reason recorded wins; stopping twice is harmless.
        /// </summary>
        /// <param name="reason">Reason recorded on the run.</param>
        public void Stop(string reason)
        {
            lock (_Lock)
            {
                if (_Disposed) return;
                if (String.IsNullOrEmpty(_Options.StopReason)) _Options.StopReason = reason;
                Source.Cancel();
            }
        }

        /// <summary>
        /// Release the token source.
        /// </summary>
        public void Dispose()
        {
            lock (_Lock)
            {
                if (_Disposed) return;
                _Disposed = true;
                Source.Dispose();
            }
        }

        #endregion
    }
}
