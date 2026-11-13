namespace Armada.Core.Services
{
    using System;
    using System.Threading;

    /// <summary>An idempotently released lease over the shared runtime settings update boundary.</summary>
    public sealed class SettingsUpdateLease : IDisposable
    {
        private SemaphoreSlim? _Gate;
        internal SettingsUpdateLease(SemaphoreSlim gate) { _Gate = gate; }
        internal bool BelongsTo(SemaphoreSlim gate) => ReferenceEquals(_Gate, gate);
        /// <summary>Release this lease once.</summary>
        public void Dispose() => Interlocked.Exchange(ref _Gate, null)?.Release();
    }
}
