namespace Armada.Core.Models
{
    /// <summary>
    /// Represents the authenticated identity context for a request.
    /// Populated by the authentication service and passed to handlers.
    /// </summary>
    public class AuthContext
    {
        #region Public-Members

        /// <summary>
        /// Whether the request is authenticated.
        /// </summary>
        public bool IsAuthenticated { get; set; } = false;

        /// <summary>
        /// Tenant identifier of the authenticated user.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User identifier of the authenticated user.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Whether the authenticated user has admin privileges.
        /// </summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>
        /// Whether the authenticated user has tenant-scoped admin privileges.
        /// </summary>
        public bool IsTenantAdmin { get; set; } = false;

        /// <summary>
        /// Authentication method used: "Bearer", "Session", "ApiKey", or null.
        /// </summary>
        public string? AuthMethod { get; set; } = null;

        /// <summary>
        /// Credential identifier used for bearer-token authentication when available.
        /// </summary>
        public string? CredentialId { get; set; } = null;

        /// <summary>
        /// Human-readable principal label for diagnostics and request history.
        /// </summary>
        public string? PrincipalDisplay { get; set; } = null;

        /// <summary>
        /// Ask Armada thread a session token is bound to, or null. A thread-scoped token is minted for a thread's captain
        /// turn and is accepted only by the MCP server, where tool calls made with it go through the thread's approval
        /// gate. REST and WebSocket authentication reject thread-scoped tokens.
        /// </summary>
        public string? AskThreadId { get; set; } = null;

        /// <summary>
        /// Mission a session token is bound to, or null. A mission-scoped token is minted for a captain's launch on that
        /// mission and is accepted only by the MCP server, only while the mission is assigned to or running on that
        /// captain. REST and WebSocket authentication reject mission-scoped tokens.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Captain a mission-scoped token was minted for, or null.
        /// </summary>
        public string? MissionCaptainId { get; set; } = null;

        /// <summary>
        /// True when this caller holds a dashboard session (session token) for a seeded admin user that still uses
        /// the well-known default password. The Admiral then refuses every API call except whoami, status, and the
        /// password change until the password is changed.
        /// </summary>
        public bool PasswordChangeRequired { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate (unauthenticated by default).
        /// </summary>
        public AuthContext()
        {
        }

        /// <summary>
        /// Create an authenticated context.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="userId">User identifier.</param>
        /// <param name="isAdmin">Admin flag.</param>
        /// <param name="isTenantAdmin">Tenant admin flag.</param>
        /// <param name="authMethod">Authentication method.</param>
        /// <param name="credentialId">Optional credential identifier.</param>
        /// <param name="principalDisplay">Optional principal display name.</param>
        /// <returns>Authenticated AuthContext.</returns>
        public static AuthContext Authenticated(
            string tenantId,
            string userId,
            bool isAdmin,
            bool isTenantAdmin,
            string authMethod,
            string? credentialId = null,
            string? principalDisplay = null)
        {
            return new AuthContext
            {
                IsAuthenticated = true,
                TenantId = tenantId,
                UserId = userId,
                IsAdmin = isAdmin,
                IsTenantAdmin = isTenantAdmin,
                AuthMethod = authMethod,
                CredentialId = credentialId,
                PrincipalDisplay = principalDisplay
            };
        }

        #endregion
    }
}
