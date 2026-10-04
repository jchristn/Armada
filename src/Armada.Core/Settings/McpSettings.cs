namespace Armada.Core.Settings
{
    /// <summary>
    /// MCP server settings.
    /// </summary>
    public class McpSettings
    {
        #region Public-Members

        /// <summary>
        /// When true (default), MCP calls without credentials are accepted only while the MCP listener is bound to a
        /// loopback hostname (localhost, 127.0.0.1, ::1) and the caller connects from loopback; such callers act as
        /// the default tenant's tenant admin, which keeps the local Claude Code setup working without a token. When
        /// false, or whenever the listener is bound to a non-loopback hostname, every MCP call must present a
        /// credential (Authorization: Bearer, X-Token, or X-Api-Key).
        /// </summary>
        public bool AllowUnauthenticatedLoopback { get; set; } = true;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public McpSettings()
        {
        }

        #endregion
    }
}
