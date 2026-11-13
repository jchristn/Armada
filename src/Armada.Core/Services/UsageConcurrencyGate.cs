namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A counting concurrency gate whose capacity can change while permits are held. Usage capture uses
    /// one for the server-wide probe bound and one per credential host, so a settings change that lowers
    /// or raises a concurrency limit governs every subsequent attempt: a lowered capacity makes new
    /// waiters queue until enough holders release, a raised capacity admits queued waiters at once.
    /// Permits are always returned to the gate that granted them, so a resize never strands or
    /// duplicates a permit.
    /// </summary>
    public sealed class UsageConcurrencyGate
    {
        #region Public-Members

        /// <summary>
        /// The current capacity (at least 1).
        /// </summary>
        public int Capacity
        {
            get
            {
                lock (_Lock) return _Capacity;
            }
        }

        /// <summary>
        /// The number of permits currently held.
        /// </summary>
        public int Active
        {
            get
            {
                lock (_Lock) return _Active;
            }
        }

        /// <summary>
        /// The number of callers waiting for a permit.
        /// </summary>
        public int Waiting
        {
            get
            {
                lock (_Lock) return _Waiters.Count;
            }
        }

        #endregion

        #region Private-Members

        private readonly object _Lock = new object();
        private readonly LinkedList<TaskCompletionSource<bool>> _Waiters = new LinkedList<TaskCompletionSource<bool>>();
        private int _Capacity;
        private int _Active;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="capacity">Initial capacity; values below 1 are treated as 1.</param>
        public UsageConcurrencyGate(int capacity)
        {
            _Capacity = Math.Max(1, capacity);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Change the capacity. Held permits are unaffected; a raise admits queued waiters immediately and
        /// a reduction makes later waiters queue until the active count falls below the new capacity.
        /// </summary>
        /// <param name="capacity">New capacity; values below 1 are treated as 1.</param>
        public void SetCapacity(int capacity)
        {
            List<TaskCompletionSource<bool>> admitted;
            lock (_Lock)
            {
                _Capacity = Math.Max(1, capacity);
                admitted = AdmitLocked();
            }

            Complete(admitted);
        }

        /// <summary>
        /// Wait for a permit. The caller owns the permit when the task completes successfully and must
        /// return it with <see cref="Release"/>; a cancelled wait owns nothing.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the permit is granted.</returns>
        /// <exception cref="OperationCanceledException">The wait was cancelled before a permit was granted.</exception>
        public Task WaitAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();

            TaskCompletionSource<bool> waiter;
            LinkedListNode<TaskCompletionSource<bool>> node;
            lock (_Lock)
            {
                if (_Waiters.Count == 0 && _Active < _Capacity)
                {
                    _Active++;
                    return Task.CompletedTask;
                }

                waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                node = _Waiters.AddLast(waiter);
            }

            if (token.CanBeCanceled)
            {
                CancellationTokenRegistration registration = token.Register(() =>
                {
                    bool removed = false;
                    lock (_Lock)
                    {
                        // A waiter still in the queue was never granted, so it owns no permit; one that was
                        // already granted keeps its permit and its caller releases it.
                        if (node.List != null)
                        {
                            _Waiters.Remove(node);
                            removed = true;
                        }
                    }

                    if (removed) waiter.TrySetCanceled(token);
                });

                _ = waiter.Task.ContinueWith(_ => registration.Dispose(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }

            return waiter.Task;
        }

        /// <summary>
        /// Return a permit and admit as many queued waiters as the current capacity allows.
        /// </summary>
        /// <exception cref="InvalidOperationException">No permit is held.</exception>
        public void Release()
        {
            List<TaskCompletionSource<bool>> admitted;
            lock (_Lock)
            {
                if (_Active <= 0) throw new InvalidOperationException("No usage concurrency permit is held.");
                _Active--;
                admitted = AdmitLocked();
            }

            Complete(admitted);
        }

        #endregion

        #region Private-Methods

        private List<TaskCompletionSource<bool>> AdmitLocked()
        {
            List<TaskCompletionSource<bool>> admitted = new List<TaskCompletionSource<bool>>();
            while (_Waiters.Count > 0 && _Active < _Capacity)
            {
                LinkedListNode<TaskCompletionSource<bool>> first = _Waiters.First!;
                _Waiters.RemoveFirst();
                _Active++;
                admitted.Add(first.Value);
            }

            return admitted;
        }

        private static void Complete(List<TaskCompletionSource<bool>> admitted)
        {
            foreach (TaskCompletionSource<bool> waiter in admitted)
                waiter.TrySetResult(true);
        }

        #endregion
    }
}
