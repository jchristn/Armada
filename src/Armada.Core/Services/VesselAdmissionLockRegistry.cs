namespace Armada.Core.Services
{
    /// <summary>Bounded per-vessel admission locks, removed when the last holder or waiter leaves.</summary>
    public sealed class VesselAdmissionLockRegistry
    {
        // Fixed order: at most one vessel lock, then the shared capacity lock, then DB writes.
        // Neither lock may survive into dock provisioning, process launch or completion callbacks.
        private readonly object _Sync = new object();
        private readonly Dictionary<string, Entry> _Entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        private sealed class Entry
        {
            internal readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
            internal int References;
        }

        /// <summary>Number of vessels with an active holder or waiter; idle keys are not retained.</summary>
        public int Count { get { lock (_Sync) return _Entries.Count; } }

        /// <summary>Acquire one vessel's admission lock, or return null when its bounded wait expires.</summary>
        public async Task<IDisposable?> TryEnterAsync(string vesselId, TimeSpan timeout, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            Entry entry;
            lock (_Sync)
            {
                if (!_Entries.TryGetValue(vesselId, out entry!)) _Entries[vesselId] = entry = new Entry();
                entry.References++;
            }
            bool acquired = false;
            try
            {
                acquired = await entry.Gate.WaitAsync(timeout, token).ConfigureAwait(false);
                return acquired ? new Lease(this, vesselId, entry) : null;
            }
            finally { if (!acquired) Dereference(vesselId, entry); }
        }

        private void Dereference(string vesselId, Entry entry)
        {
            lock (_Sync)
            {
                if (--entry.References != 0) return;
                _Entries.Remove(vesselId);
                entry.Gate.Dispose();
            }
        }

        private sealed class Lease : IDisposable
        {
            private VesselAdmissionLockRegistry? _Owner;
            private readonly string _VesselId;
            private readonly Entry _Entry;
            internal Lease(VesselAdmissionLockRegistry owner, string vesselId, Entry entry)
            { _Owner = owner; _VesselId = vesselId; _Entry = entry; }
            public void Dispose()
            {
                VesselAdmissionLockRegistry? owner = Interlocked.Exchange(ref _Owner, null);
                if (owner == null) return;
                _Entry.Gate.Release();
                owner.Dereference(_VesselId, _Entry);
            }
        }
    }
}
