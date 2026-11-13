namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Per-entry async locks that coordinate the writers of an entry's objects within one Admiral process: the entry
    /// writer (capture, enrichment and re-analysis rewrite record.json and the briefs) and the storage migrator (which
    /// copies an entry's objects to another target and switches it). Holding an entry's lock means no other holder writes
    /// or moves that entry's objects. A lock exists only while it is held or awaited, so the table stays bounded by the
    /// work in flight. Thread-safe.
    /// </summary>
    public class BlackBoxEntryLocks
    {
        #region Public-Members

        /// <summary>
        /// The process-wide instance the runtime shares between its writer and its migrator.
        /// </summary>
        public static BlackBoxEntryLocks Default { get; } = new BlackBoxEntryLocks();

        /// <summary>
        /// Number of entries whose lock is held or awaited.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_Gate) return _Locks.Count;
            }
        }

        #endregion

        #region Private-Members

        private readonly object _Gate = new object();
        private readonly Dictionary<string, Holder> _Locks = new Dictionary<string, Holder>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Wait for and take the lock of one entry. Dispose the result to release it.
        /// </summary>
        /// <param name="entryId">Entry id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The held lock.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entryId is null or empty.</exception>
        public async Task<IDisposable> AcquireAsync(string entryId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));
            Holder holder;
            lock (_Gate)
            {
                if (!_Locks.TryGetValue(entryId, out Holder? existing))
                {
                    existing = new Holder();
                    _Locks[entryId] = existing;
                }
                existing.References++;
                holder = existing;
            }

            try
            {
                await holder.Semaphore.WaitAsync(token).ConfigureAwait(false);
            }
            catch
            {
                Release(entryId, holder, false);
                throw;
            }
            return new Releaser(this, entryId, holder);
        }

        #endregion

        #region Private-Methods

        private void Release(string entryId, Holder holder, bool held)
        {
            if (held) holder.Semaphore.Release();
            lock (_Gate)
            {
                holder.References--;
                if (holder.References == 0) _Locks.Remove(entryId);
            }
        }

        #endregion

        #region Private-Classes

        private sealed class Holder
        {
            public SemaphoreSlim Semaphore { get; } = new SemaphoreSlim(1, 1);

            public int References { get; set; } = 0;
        }

        private sealed class Releaser : IDisposable
        {
            private readonly BlackBoxEntryLocks _Owner;
            private readonly string _EntryId;
            private readonly Holder _Holder;
            private int _Disposed = 0;

            public Releaser(BlackBoxEntryLocks owner, string entryId, Holder holder)
            {
                _Owner = owner;
                _EntryId = entryId;
                _Holder = holder;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _Disposed, 1) == 1) return;
                _Owner.Release(_EntryId, _Holder, true);
            }
        }

        #endregion
    }
}
