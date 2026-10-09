namespace Armada.Core.Database.Sqlite
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;

    /// <summary>
    /// The single in-process write gate of one SQLite database file. Every write the process makes to that file (the
    /// provider's methods, migrations, data expiry, restore) enters the gate first, so SQLite never sees two writers
    /// from this process contend for its write lock.
    /// <para>
    /// Why: Microsoft.Data.Sqlite does not install a busy handler. A writer that finds the database locked sleeps
    /// 150 ms on the calling thread and retries, so each retry is a single sample of the lock. Writers that bypass
    /// an in-process lock keep winning those samples while a queued writer can lose every one of them, which is how
    /// one insert waited 17 s on a busy Windows runner. Behind this gate a writer waits in a strict first-in,
    /// first-out queue: it is granted the gate after the writers that asked before it, and never after one that asked
    /// later, whatever the timer resolution or scheduling of the host.
    /// </para>
    /// <para>
    /// The gate is shared by every driver, service, and connection that points at the same file (resolved by full
    /// path), so a second driver instance on the same database queues on the same gate. It is not reentrant: entering
    /// it again from a flow that already holds it throws instead of deadlocking.
    /// </para>
    /// <para>
    /// Connections opened through <see cref="SqliteProviderConnection"/> report every committed write transaction to
    /// the gate, which counts commits made by a flow that does not hold it (<see cref="UnguardedCommitCount"/>). When
    /// <see cref="StrictAudit"/> is on, such a commit is refused, so a write path that bypasses the gate fails loudly.
    /// </para>
    /// </summary>
    public sealed class SqliteWriteGate
    {
        #region Public-Members

        /// <summary>
        /// When true, a write transaction committed on a provider connection by a flow that does not hold the gate is
        /// rolled back (the commit fails with a constraint error). Off by default; the test runners turn it on so any
        /// write path that bypasses the gate fails its test.
        /// </summary>
        public static bool StrictAudit
        {
            get { return Volatile.Read(ref _StrictAudit); }
            set { Volatile.Write(ref _StrictAudit, value); }
        }

        /// <summary>
        /// Key of the database this gate guards: the full path of the database file, or the connection string for an
        /// in-memory database.
        /// </summary>
        public string Key
        {
            get { return _Key; }
        }

        /// <summary>
        /// Whether some writer holds the gate.
        /// </summary>
        public bool IsHeld
        {
            get
            {
                lock (_Lock) return _Holder != null;
            }
        }

        /// <summary>
        /// Number of writers queued behind the holder.
        /// </summary>
        public int QueueLength
        {
            get
            {
                lock (_Lock) return _Waiters.Count;
            }
        }

        /// <summary>
        /// Number of times the gate has been granted since it was created.
        /// </summary>
        public long GrantCount
        {
            get { return Interlocked.Read(ref _GrantCount); }
        }

        /// <summary>
        /// Number of write transactions committed on a provider connection by a flow that did not hold the gate.
        /// </summary>
        public long UnguardedCommitCount
        {
            get { return Interlocked.Read(ref _UnguardedCommitCount); }
        }

        /// <summary>
        /// Stack trace of the most recent unguarded commit, or null when there has been none.
        /// </summary>
        public string? LastUnguardedCommitStack
        {
            get { return Volatile.Read(ref _LastUnguardedCommitStack); }
        }

        /// <summary>
        /// Whether the current asynchronous flow holds this gate.
        /// </summary>
        public bool IsHeldByCurrentFlow
        {
            get { return FindActiveLease(this) != null; }
        }

        #endregion

        #region Private-Members

        private static readonly ConcurrentDictionary<string, SqliteWriteGate> _GatesByConnectionString = new ConcurrentDictionary<string, SqliteWriteGate>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, SqliteWriteGate> _GatesByKey = new ConcurrentDictionary<string, SqliteWriteGate>(StringComparer.OrdinalIgnoreCase);
        private static readonly AsyncLocal<SqliteWriteLease?> _CurrentLease = new AsyncLocal<SqliteWriteLease?>();
        private static bool _StrictAudit = false;

        private readonly string _Key;
        private readonly object _Lock = new object();
        private readonly LinkedList<SqliteWriteGateWaiter> _Waiters = new LinkedList<SqliteWriteGateWaiter>();
        private readonly List<SqliteWriteGateQueueWatch> _QueueWatches = new List<SqliteWriteGateQueueWatch>();
        private SqliteWriteLease? _Holder = null;
        private long _GrantCount = 0;
        private long _UnguardedCommitCount = 0;
        private string? _LastUnguardedCommitStack = null;

        #endregion

        #region Constructors-and-Factories

        private SqliteWriteGate(string key)
        {
            _Key = key;
        }

        /// <summary>
        /// The gate of the database a SQLite connection string points at. Every connection string that names the same
        /// file (by full path) gets the same gate.
        /// </summary>
        /// <param name="connectionString">SQLite connection string.</param>
        /// <returns>Gate.</returns>
        public static SqliteWriteGate ForConnectionString(string connectionString)
        {
            if (connectionString == null) throw new ArgumentNullException(nameof(connectionString));
            if (_GatesByConnectionString.TryGetValue(connectionString, out SqliteWriteGate? cached)) return cached;

            string key = KeyFor(connectionString);
            SqliteWriteGate gate = _GatesByKey.GetOrAdd(key, (string k) => new SqliteWriteGate(k));
            _GatesByConnectionString.TryAdd(connectionString, gate);
            return gate;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Enter the gate. The returned lease holds it until disposed. Writers are granted the gate strictly in the
        /// order they called this method. The lease marks the calling flow as the holder, which is what the commit
        /// audit checks, so call this from the method that performs the write (not from a helper that returns).
        /// </summary>
        /// <param name="token">Cancellation token; cancelling a queued entry removes it from the queue.</param>
        /// <returns>Lease to dispose when the write is done.</returns>
        /// <exception cref="InvalidOperationException">When the calling flow already holds this gate.</exception>
        public Task<SqliteWriteLease> EnterAsync(CancellationToken token = default)
        {
            // Deliberately not an async method: the lease is recorded in the caller's flow before the first await,
            // which an async method could not do (its AsyncLocal changes are undone when it returns).
            SqliteWriteLease? active = FindActiveLease(this);
            if (active != null)
                throw new InvalidOperationException("The SQLite write gate for " + _Key + " is already held by this flow; nested writes must reuse the open connection instead of entering the gate again.");

            token.ThrowIfCancellationRequested();

            SqliteWriteLease lease = new SqliteWriteLease(this, _CurrentLease.Value);
            _CurrentLease.Value = lease;

            SqliteWriteGateWaiter waiter;
            lock (_Lock)
            {
                if (_Holder == null && _Waiters.Count == 0)
                {
                    Grant(lease);
                    return Task.FromResult(lease);
                }

                waiter = new SqliteWriteGateWaiter(lease);
                waiter.Node = _Waiters.AddLast(waiter);
                CompleteQueueWatches();
            }

            if (token.CanBeCanceled)
            {
                CancellationTokenRegistration registration = token.Register(() => CancelWaiter(waiter, token));
                bool stillQueued;
                lock (_Lock)
                {
                    stillQueued = waiter.Node != null;
                    if (stillQueued) waiter.Registration = registration;
                }

                if (!stillQueued) registration.Dispose();
            }

            return waiter.Completion.Task;
        }

        /// <summary>
        /// Wait until at least the given number of writers are queued behind the holder. A diagnostic for tests that
        /// need to know a writer has reached the gate without sleeping.
        /// </summary>
        /// <param name="count">Queue length to wait for.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task completed once the queue holds at least <paramref name="count"/> writers.</returns>
        public Task WhenQueuedAsync(int count, CancellationToken token = default)
        {
            SqliteWriteGateQueueWatch watch = new SqliteWriteGateQueueWatch(count);
            lock (_Lock)
            {
                if (_Waiters.Count >= count) return Task.CompletedTask;
                _QueueWatches.Add(watch);
            }

            return watch.Completion.Task.WaitAsync(token);
        }

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Release the gate held by a lease and hand it to the next queued writer.
        /// </summary>
        /// <param name="lease">Lease that holds the gate.</param>
        internal void Release(SqliteWriteLease lease)
        {
            SqliteWriteGateWaiter? next = null;
            lock (_Lock)
            {
                if (!ReferenceEquals(_Holder, lease)) return;
                _Holder = null;

                if (_Waiters.Count > 0)
                {
                    next = _Waiters.First!.Value;
                    _Waiters.RemoveFirst();
                    next.Node = null;
                    Grant(next.Lease);
                }
            }

            if (next != null)
            {
                next.Completion.TrySetResult(next.Lease);
                next.Registration.Dispose();
            }
        }

        /// <summary>
        /// Called by the commit hook of a provider connection when a write transaction commits.
        /// </summary>
        /// <returns>0 to let the commit proceed; non-zero (strict audit only) to turn it into a rollback.</returns>
        internal int OnCommit()
        {
            if (IsHeldByCurrentFlow) return 0;

            Interlocked.Increment(ref _UnguardedCommitCount);
            Volatile.Write(ref _LastUnguardedCommitStack, Environment.StackTrace);
            return StrictAudit ? 1 : 0;
        }

        /// <summary>
        /// Restore the calling flow's lease marker after a lease is disposed in it.
        /// </summary>
        /// <param name="lease">Disposed lease.</param>
        internal static void RestoreFlow(SqliteWriteLease lease)
        {
            if (ReferenceEquals(_CurrentLease.Value, lease)) _CurrentLease.Value = lease.Previous;
        }

        /// <summary>
        /// Key of the database a SQLite connection string points at.
        /// </summary>
        /// <param name="connectionString">Connection string.</param>
        /// <returns>Full file path, or the connection string for an in-memory database.</returns>
        internal static string KeyFor(string connectionString)
        {
            SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder(connectionString);
            string dataSource = builder.DataSource ?? String.Empty;
            if (builder.Mode == SqliteOpenMode.Memory
                || String.IsNullOrEmpty(dataSource)
                || String.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
            {
                return "memory:" + connectionString;
            }

            if (dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return dataSource;
            return Path.GetFullPath(dataSource);
        }

        #endregion

        #region Private-Methods

        private static SqliteWriteLease? FindActiveLease(SqliteWriteGate gate)
        {
            SqliteWriteLease? lease = _CurrentLease.Value;
            while (lease != null)
            {
                if (ReferenceEquals(lease.Gate, gate) && lease.IsActive) return lease;
                lease = lease.Previous;
            }

            return null;
        }

        private void CompleteQueueWatches()
        {
            for (int i = _QueueWatches.Count - 1; i >= 0; i--)
            {
                if (_Waiters.Count < _QueueWatches[i].Count) continue;
                _QueueWatches[i].Completion.TrySetResult(true);
                _QueueWatches.RemoveAt(i);
            }
        }

        private void Grant(SqliteWriteLease lease)
        {
            _Holder = lease;
            lease.MarkGranted();
            Interlocked.Increment(ref _GrantCount);
        }

        private void CancelWaiter(SqliteWriteGateWaiter waiter, CancellationToken token)
        {
            lock (_Lock)
            {
                if (waiter.Node == null) return;
                _Waiters.Remove(waiter.Node);
                waiter.Node = null;
            }

            waiter.Completion.TrySetCanceled(token);
        }

        #endregion
    }
}
