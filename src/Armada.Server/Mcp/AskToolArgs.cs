namespace Armada.Server.Mcp
{
    using System.Collections.Generic;
    using System.Text.Json.Nodes;

    /// <summary>
    /// Arguments for the Ask Armada MCP tools (each tool reads the fields it documents).
    /// </summary>
    public class AskToolArgs
    {
        #region Public-Members

        /// <summary>Conversation ID (ath_ prefix).</summary>
        public string? ThreadId { get; set; } = null;

        /// <summary>Conversation IDs for bulk deletion.</summary>
        public List<string>? ThreadIds { get; set; } = null;

        /// <summary>Action proposal ID.</summary>
        public string? ProposalId { get; set; } = null;

        /// <summary>Tracked work ID.</summary>
        public string? WorkId { get; set; } = null;

        /// <summary>Message ID (amg_ prefix) whose tool calls to read.</summary>
        public string? MessageId { get; set; } = null;

        /// <summary>Status or state filter of a paged read.</summary>
        public string? Status { get; set; } = null;

        /// <summary>CreatedAscending or CreatedDescending (default) for a paged read.</summary>
        public string? Order { get; set; } = null;

        /// <summary>Conversation title.</summary>
        public string? Title { get; set; } = null;

        /// <summary>Captain that answers in the conversation, or null for the default.</summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>True to clear the conversation's captain (use the default) on update.</summary>
        public bool? ClearCaptain { get; set; } = null;

        /// <summary>Whether proposed actions run without asking.</summary>
        public bool? AutoApprove { get; set; } = null;

        /// <summary>Pin or unpin the conversation.</summary>
        public bool? Pinned { get; set; } = null;

        /// <summary>Archive or unarchive the conversation.</summary>
        public bool? Archived { get; set; } = null;

        /// <summary>Text to search conversation titles for.</summary>
        public string? Search { get; set; } = null;

        /// <summary>Include archived conversations in a listing.</summary>
        public bool? IncludeArchived { get; set; } = null;

        /// <summary>Page number (1-based).</summary>
        public int? PageNumber { get; set; } = null;

        /// <summary>Page size.</summary>
        public int? PageSize { get; set; } = null;

        /// <summary>Return messages before this sequence number (older pages).</summary>
        public int? BeforeSequence { get; set; } = null;

        /// <summary>Message text to send.</summary>
        public string? Content { get; set; } = null;

        /// <summary>Whether the captain's reasoning is shown in the reply.</summary>
        public bool? ShowThinking { get; set; } = null;

        /// <summary>Seconds to wait for the reply after sending (0 returns immediately; at most 600).</summary>
        public int? WaitSeconds { get; set; } = null;

        /// <summary>Quick action tool name.</summary>
        public string? ToolName { get; set; } = null;

        /// <summary>Quick action arguments.</summary>
        public JsonObject? Arguments { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskToolArgs()
        {
        }

        #endregion
    }
}
