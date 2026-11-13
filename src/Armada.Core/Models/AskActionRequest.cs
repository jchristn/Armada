namespace Armada.Core.Models
{
    using System;
    using System.Text.Json.Nodes;
    using Armada.Core.Services;

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
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "Top-level tool arguments of one quick action; SubmitQuickActionAsync rejects more.")]
        public JsonObject? Arguments { get; set; } = null;

        /// <summary>
        /// When true the action is not run: it becomes a Pending proposal with a confirm card that the user approves or
        /// rejects (the black box card's Fix it and Apply buttons). Default false: the submission is the confirmation and
        /// the action runs at once.
        /// </summary>
        public bool Propose { get; set; } = false;

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
