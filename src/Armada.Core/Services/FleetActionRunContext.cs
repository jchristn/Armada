namespace Armada.Core.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// In-memory state for a Command fleet action run that is executing in this process: the cancellation source
    /// shared by its in-flight targets, whether a user asked to cancel it (as opposed to the server shutting down),
    /// and the processing task. Thread-safe for the members it exposes.
    /// </summary>
    internal sealed class FleetActionRunContext : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Token observed by the run's in-flight targets. Cancelled by a user cancel or by server shutdown.
        /// </summary>
        public CancellationToken Token { get; }

        /// <summary>
        /// Whether a user cancelled the run. False when the token was cancelled only because the server is stopping.
        /// </summary>
        public bool CancelRequested
        {
            get => Volatile.Read(ref _CancelRequested);
            set => Volatile.Write(ref _CancelRequested, value);
        }

        /// <summary>
        /// The processing task, or null before it is assigned.
        /// </summary>
        public Task? Task { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly CancellationTokenSource _Cts;
        private bool _CancelRequested = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate, linked to the runner's lifetime token.
        /// </summary>
        /// <param name="lifetime">Runner lifetime token.</param>
        public FleetActionRunContext(CancellationToken lifetime)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            Token = _Cts.Token;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Cancel the run's token. Safe to call after disposal.
        /// </summary>
        public void Cancel()
        {
            try
            {
                _Cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>
        /// Dispose the cancellation source.
        /// </summary>
        public void Dispose()
        {
            _Cts.Dispose();
        }

        #endregion
    }
}
