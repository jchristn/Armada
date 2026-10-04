namespace Armada.Server.WebSocket
{
    using System;
    using WatsonWebserver.Core.WebSockets;
    using Armada.Core.Models;

    /// <summary>
    /// Per-connection state for an authenticated /ws client: the socket, the identity resolved at upgrade, and whether
    /// a global admin opted in to receive every tenant's entity events.
    /// </summary>
    /// <remarks>Thread safety: <see cref="AllTenants"/> is written by the session's read loop and read by broadcasters;
    /// it is a single volatile boolean, so reads always observe the latest value.</remarks>
    public class WebSocketClientState
    {
        #region Public-Members

        /// <summary>
        /// The Watson WebSocket session.
        /// </summary>
        public WebSocketSession Session { get; }

        /// <summary>
        /// The identity that authenticated the upgrade. Never null and always authenticated.
        /// </summary>
        public AuthContext Auth { get; }

        /// <summary>
        /// Tenant of the connected identity.
        /// </summary>
        public string TenantId => Auth.TenantId ?? String.Empty;

        /// <summary>
        /// User of the connected identity.
        /// </summary>
        public string UserId => Auth.UserId ?? String.Empty;

        /// <summary>
        /// Whether the identity is a global admin.
        /// </summary>
        public bool IsAdmin => Auth.IsAdmin;

        /// <summary>
        /// Whether the identity is a tenant admin (global admins are also tenant admins).
        /// </summary>
        public bool IsTenantAdmin => Auth.IsTenantAdmin || Auth.IsAdmin;

        /// <summary>
        /// When true (global admins only), entity events of every tenant are delivered to this socket. Default false.
        /// </summary>
        public bool AllTenants
        {
            get => _AllTenants;
            set => _AllTenants = value && Auth.IsAdmin;
        }

        /// <summary>
        /// UTC time the socket connected.
        /// </summary>
        public DateTime ConnectedUtc { get; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private volatile bool _AllTenants = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="session">WebSocket session.</param>
        /// <param name="auth">Authenticated identity.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the identity is not authenticated.</exception>
        public WebSocketClientState(WebSocketSession session, AuthContext auth)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Auth = auth ?? throw new ArgumentNullException(nameof(auth));
            if (!auth.IsAuthenticated) throw new ArgumentException("A WebSocket client must be authenticated.", nameof(auth));
        }

        #endregion
    }
}
