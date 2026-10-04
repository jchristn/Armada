namespace Armada.Tui.Services
{
    using System;
    using System.Threading;
    using Armada.Client.Socket;

    /// <summary>
    /// A subscription registered with <see cref="EventPump"/>. Disposing it unsubscribes.
    /// </summary>
    internal sealed class EventPumpSubscription : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Filter: exact type, prefix (ending in <c>.</c> or <c>*</c>), or <c>*</c>.
        /// </summary>
        public string Filter { get; }

        /// <summary>
        /// Per-message handler, or null.
        /// </summary>
        public Action<ArmadaSocketMessage>? Handler { get; }

        /// <summary>
        /// Coalesced callback, or null.
        /// </summary>
        public Action? Callback { get; }

        /// <summary>
        /// True after disposal.
        /// </summary>
        public bool Disposed { get; private set; } = false;

        #endregion

        #region Private-Members

        private readonly Action<EventPumpSubscription> _Remove;
        private int _Armed = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="filter">Filter.</param>
        /// <param name="handler">Handler.</param>
        /// <param name="callback">Callback.</param>
        /// <param name="remove">Removal callback.</param>
        public EventPumpSubscription(string filter, Action<ArmadaSocketMessage>? handler, Action? callback, Action<EventPumpSubscription> remove)
        {
            Filter = String.IsNullOrEmpty(filter) ? "*" : filter;
            Handler = handler;
            Callback = callback;
            _Remove = remove ?? throw new ArgumentNullException(nameof(remove));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when the filter matches an event type.
        /// </summary>
        /// <param name="type">Event type.</param>
        /// <returns>True on a match.</returns>
        public bool Matches(string? type)
        {
            if (Filter == "*") return true;
            if (type == null) return false;
            if (Filter.EndsWith("*")) return type.StartsWith(Filter.TrimEnd('*'), StringComparison.Ordinal);
            if (Filter.EndsWith(".")) return type.StartsWith(Filter, StringComparison.Ordinal);
            return String.Equals(Filter, type, StringComparison.Ordinal);
        }

        /// <summary>
        /// Arm the coalescing timer; false when already armed.
        /// </summary>
        /// <returns>True when this call armed it.</returns>
        public bool TryArm()
        {
            return Interlocked.CompareExchange(ref _Armed, 1, 0) == 0;
        }

        /// <summary>
        /// Disarm after the callback is scheduled.
        /// </summary>
        public void Disarm()
        {
            Interlocked.Exchange(ref _Armed, 0);
        }

        /// <summary>
        /// Unsubscribe.
        /// </summary>
        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            _Remove(this);
        }

        #endregion
    }
}
