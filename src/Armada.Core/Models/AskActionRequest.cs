namespace Armada.Core.Models
{
    using System;
    using System.Text.Json.Nodes;

    /// <summary>
    /// Body of POST /api/v1/ask/threads/{id}/actions: a quick action expressed as an MCP tool call.
    /// </summary>
    public class AskActionRequest
    {
        #region Public-Members

        /// <summary>
        /// MCP tool name (same names as the MCP API).
        /// </summary>
        public string ToolName
        {
            get => _ToolName;
            set => _ToolName = value ?? String.Empty;
        }

        /// <summary>
        /// Tool arguments with the same shape as the MCP tool's input, or null for none.
        /// </summary>
        public JsonObject? Arguments { get; set; } = null;

        #endregion

        #region Private-Members

        private string _ToolName = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskActionRequest()
        {
        }

        #endregion
    }
}
