namespace Armada.Core.Services
{
    using System;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Decides what a reader captain can receive from hard facts only: the served context of its model endpoint and the
    /// tools its runtime and endpoint actually give it. No judgment and no model is involved.
    /// </summary>
    public static class HandoffReaderResolver
    {
        #region Public-Methods

        /// <summary>
        /// Resolve the reader. A null captain is an unknown reader: unknown context, MCP tools and file access assumed (the
        /// CLI default). CLI runtimes (Claude Code, Codex, Gemini, Cursor, OpenCode, Pi) read files and get the Armada MCP
        /// server; a Custom runtime reads files but is not assumed to have MCP. API endpoint and Mux captains follow their
        /// endpoint's inference options: no tools means neither MCP nor files; MCP needs MCP tools enabled and, with an
        /// allow-list, get_handoff_artifact on it.
        /// </summary>
        /// <param name="captain">Reader captain, or null.</param>
        /// <param name="endpoint">The captain's model endpoint, or null.</param>
        /// <returns>The reader.</returns>
        public static HandoffReader Resolve(Captain? captain, ModelEndpoint? endpoint)
        {
            HandoffReader reader = new HandoffReader();
            InferenceOptions? options = endpoint?.InferenceOptions;
            // The configured window, else the one the model server reported (RS-7.8), so a discovered endpoint is budgeted too.
            int? window = endpoint != null ? ModelContextDiscovery.WindowOf(endpoint) : null;
            if (window.HasValue && window.Value > 0) reader.ContextTokens = window.Value;
            if (captain == null) return reader;

            switch (captain.Runtime)
            {
                case AgentRuntimeEnum.Custom:
                    reader.HasMcpTools = false;
                    reader.CanReadFiles = true;
                    break;
                case AgentRuntimeEnum.ApiEndpoint:
                case AgentRuntimeEnum.Mux:
                    if (options != null)
                    {
                        reader.CanReadFiles = options.ToolsEnabled;
                        reader.HasMcpTools = options.ToolsEnabled && options.McpToolsEnabled
                            && (options.AllowedTools == null || options.AllowedTools.Any(t => t != null && t.EndsWith("get_handoff_artifact", StringComparison.OrdinalIgnoreCase)));
                    }

                    break;
            }

            return reader;
        }

        #endregion
    }
}
