namespace Armada.Core.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A coalescing wake for the Admiral dispatcher (RS-4.5). Any number of requests made while a pass is pending collapse
    /// into one pass. Raising it never blocks and never waits for dispatch, so completion paths stay fast.
    /// </summary>
    public sealed class DispatchWakeSignal
    {
        private readonly SemaphoreSlim _Signal = new SemaphoreSlim(0, 1);
        private readonly object _Lock = new object();
        private long _Requests = 0;

        /// <summary>Total requests raised, including coalesced ones.</summary>
        public long Requests => Interlocked.Read(ref _Requests);

        /// <summary>Whether a wake is waiting to be consumed.</summary>
        public bool IsPending => _Signal.CurrentCount > 0;

        /// <summary>Request a dispatch pass. Returns immediately.</summary>
        public void Request()
        {
            Interlocked.Increment(ref _Requests);
            lock (_Lock)
            {
                if (_Signal.CurrentCount == 0) _Signal.Release();
            }
        }

        /// <summary>Wait for a request.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when a request is pending; the request is consumed.</returns>
        public Task WaitAsync(CancellationToken token = default)
        {
            return _Signal.WaitAsync(token);
        }
    }
}
