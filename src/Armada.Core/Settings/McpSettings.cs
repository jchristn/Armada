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

        /// <summary>
        /// When true (default), every captain launched for a mission gets a mission-scoped MCP session token (MCP only,
        /// bound to the mission's tenant, owner, and captain, valid only while the mission is assigned or in progress)
        /// and its Armada MCP connection is bound to that token, so mission captains act as the mission's owner instead
        /// of relying on the unauthenticated loopback identity. Applies to local launches (Claude Code, Codex, OpenCode,
        /// Mux, API endpoint; Gemini and Cursor only with <c>IsolateCaptainLaunch</c>) and to Harbor launches.
        /// </summary>
        public bool MissionScopedTokens { get; set; } = true;

        /// <summary>
        /// Tool calls one MCP client may make per second (MCP requires servers to rate-limit tool calls). Default 100;
        /// 0 disables the limit. Clamped to 0-1000000. A call over the limit gets a tool result with isError true.
        /// </summary>
        public int ToolCallsPerSecond
        {
            get { return _ToolCallsPerSecond; }
            set { _ToolCallsPerSecond = value < 0 ? 0 : (value > 1000000 ? 1000000 : value); }
        }

        #endregion

        #region Private-Members

        private int _ToolCallsPerSecond = 100;

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
