namespace Test.Shared.Infrastructure
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Vessel service wrapper that lets a fixed number of creations through and then blocks the next one until
    /// <see cref="Release"/> is called, so a test can act (for example cancel a job) while an import is provably in flight.
    /// </summary>
    public sealed class GatedVesselService : IVesselService
    {
        #region Public-Members

        /// <summary>
        /// Completes when a creation is blocked at the gate.
        /// </summary>
        public Task Blocked
        {
            get { return _Blocked.Task; }
        }

        #endregion

        #region Private-Members

        private readonly IVesselService _Inner;
        private readonly int _PassCount;
        private readonly TaskCompletionSource<bool> _Blocked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _Calls = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="inner">Real vessel service.</param>
        /// <param name="passCount">Creations allowed before the gate closes.</param>
        public GatedVesselService(IVesselService inner, int passCount)
        {
            _Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _PassCount = passCount;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Open the gate for every waiting and future creation.
        /// </summary>
        public void Release()
        {
            _Gate.TrySetResult(true);
        }

        /// <inheritdoc />
        public async Task<Vessel> CreateAsync(Vessel vessel, bool inferWorkingDirectoryFromLocalClone = false, CancellationToken token = default)
        {
            if (Interlocked.Increment(ref _Calls) > _PassCount)
            {
                _Blocked.TrySetResult(true);
                await _Gate.Task.WaitAsync(TimeSpan.FromSeconds(60), token).ConfigureAwait(false);
            }
            return await _Inner.CreateAsync(vessel, inferWorkingDirectoryFromLocalClone, token).ConfigureAwait(false);
        }

        #endregion
    }
}
