namespace Armada.Client.Socket
{
    using System;

    /// <summary>
    /// A handler registered with <see cref="ArmadaSocket.On{T}"/>; disposing it unsubscribes.
    /// </summary>
    internal sealed class ArmadaSocketSubscription : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Event type, or <c>*</c>.
        /// </summary>
        public string EventType { get; }

        /// <summary>
        /// Handler.
        /// </summary>
        public Action<ArmadaSocketMessage> Handler { get; }

        #endregion

        #region Private-Members

        private readonly Action<ArmadaSocketSubscription> _Unsubscribe;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="eventType">Event type.</param>
        /// <param name="handler">Handler.</param>
        /// <param name="unsubscribe">Callback that removes the subscription.</param>
        public ArmadaSocketSubscription(string eventType, Action<ArmadaSocketMessage> handler, Action<ArmadaSocketSubscription> unsubscribe)
        {
            EventType = eventType ?? throw new ArgumentNullException(nameof(eventType));
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _Unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Unsubscribe.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _Unsubscribe(this);
        }

        #endregion
    }
}
