namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// Inputs for <see cref="CaptainThreadMcpPlanner"/>: which runtime is launching, where the Admiral's MCP server listens,
    /// where per-launch files may be written, the session token the connection must carry, and the parts of the host
    /// user's own client configuration the planner needs to read (never write) so it can override or disable the host's
    /// Armada entries without touching the CLI's login.
    /// </summary>
    public sealed class CaptainThreadMcpPlanRequest
    {
        #region Public-Members

        /// <summary>
        /// The captain's runtime.
        /// </summary>
        public AgentRuntimeEnum Runtime { get; set; } = AgentRuntimeEnum.ClaudeCode;

        /// <summary>
        /// The Admiral MCP port. Values outside 1-65535 produce an empty plan.
        /// </summary>
        public int McpPort { get; set; } = 0;

        /// <summary>
        /// The host MCP clients must use to reach the Admiral's MCP listener (see
        /// <see cref="ArmadaMcpConfigBuilder.ClientHostFor"/>). Defaults to localhost; null or empty also means localhost.
        /// </summary>
        public string McpHost
        {
            get => _McpHost;
            set => _McpHost = String.IsNullOrWhiteSpace(value) ? ArmadaMcpConfigBuilder.DefaultHost : value;
        }

        /// <summary>
        /// Absolute path to a per-launch directory the caller deletes when the process exits. Files that hold no
        /// project context (for example the Claude Code and Mux server documents) are written here.
        /// </summary>
        public string ScopedConfigDirectory
        {
            get => _ScopedConfigDirectory;
            set => _ScopedConfigDirectory = value ?? String.Empty;
        }

        /// <summary>
        /// Absolute path to the throwaway working directory of the turn. Project-local client configuration
        /// (Gemini, Cursor) is written here, so it must never be a real repository checkout.
        /// </summary>
        public string WorkingDirectory
        {
            get => _WorkingDirectory;
            set => _WorkingDirectory = value ?? String.Empty;
        }

        /// <summary>
        /// The thread-scoped session token the connection must carry. An empty token produces an empty plan.
        /// </summary>
        public string SessionToken
        {
            get => _SessionToken;
            set => _SessionToken = value ?? String.Empty;
        }

        /// <summary>
        /// Contents of the host user's Codex <c>config.toml</c> (read from <c>CODEX_HOME</c> or <c>~/.codex</c>), or null.
        /// Used only to find existing Armada server entries to disable for the turn.
        /// </summary>
        /// <summary>
        /// Whether the plan may write client configuration into <see cref="WorkingDirectory"/> (Gemini, Cursor). True for
        /// Ask turns, whose working directory is a throwaway directory. Mission launches set false because the working
        /// directory is a real repository worktree; Gemini and Cursor then get an empty plan.
        /// </summary>
        public bool AllowWorkingDirectoryFiles { get; set; } = true;

        public string? HostCodexConfigToml { get; set; } = null;

        /// <summary>
        /// Contents of the host user's OpenCode configuration files (global <c>opencode.json</c> / <c>opencode.jsonc</c> and
        /// any <c>OPENCODE_CONFIG</c> file). Used only to find existing Armada server entries to disable for the turn.
        /// </summary>
        public List<string> HostOpenCodeConfigDocuments
        {
            get => _HostOpenCodeConfigDocuments;
            set => _HostOpenCodeConfigDocuments = value ?? new List<string>();
        }

        /// <summary>
        /// Contents of the host user's Cursor <c>~/.cursor/mcp.json</c>, or null. Used only to find existing Armada server
        /// entries, which the turn's project-local configuration then replaces under the same names.
        /// </summary>
        public string? HostCursorMcpJson { get; set; } = null;

        /// <summary>
        /// The <c>OPENCODE_CONFIG_CONTENT</c> value already present in the Admiral's environment, or null. The planner
        /// merges its own entries into it rather than replacing the user's inline configuration.
        /// </summary>
        public string? ExistingOpenCodeConfigContent { get; set; } = null;

        #endregion

        #region Private-Members

        private string _McpHost = ArmadaMcpConfigBuilder.DefaultHost;
        private string _ScopedConfigDirectory = String.Empty;
        private string _WorkingDirectory = String.Empty;
        private string _SessionToken = String.Empty;
        private List<string> _HostOpenCodeConfigDocuments = new List<string>();

        #endregion
    }
}
