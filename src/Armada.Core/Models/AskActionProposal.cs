namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// A state-changing action in an Ask Armada thread: either proposed by the captain through a thread-scoped MCP
    /// call (waiting for approval unless the thread auto-approves) or submitted through a quick action form. Executed
    /// in-process through the same MCP tool handler as a direct call.
    /// </summary>
    public class AskActionProposal
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (aap_ prefix).
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = String.IsNullOrEmpty(value) ? throw new ArgumentNullException(nameof(Id)) : value;
        }

        /// <summary>
        /// Tenant that owns the record.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User that owns the record (the thread owner).
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Thread (ath_ prefix).
        /// </summary>
        public string ThreadId
        {
            get => _ThreadId;
            set => _ThreadId = value ?? String.Empty;
        }

        /// <summary>
        /// ActionProposal message (amg_ prefix) that renders the confirm card, or null.
        /// </summary>
        public string? MessageId { get; set; } = null;

        /// <summary>
        /// MCP tool name (same names as the MCP API).
        /// </summary>
        public string ToolName
        {
            get => _ToolName;
            set => _ToolName = value ?? String.Empty;
        }

        /// <summary>
        /// Exact tool arguments (JSON object text). Never null; defaults to {}.
        /// </summary>
        public string ArgumentsText
        {
            get => _ArgumentsText;
            set => _ArgumentsText = String.IsNullOrWhiteSpace(value) ? "{}" : value;
        }

        /// <summary>
        /// One-line human description of what the action does.
        /// </summary>
        public string SummaryText
        {
            get => _SummaryText;
            set => _SummaryText = value ?? String.Empty;
        }

        /// <summary>
        /// Where the proposal came from.
        /// </summary>
        public AskProposalSourceEnum Source { get; set; } = AskProposalSourceEnum.Captain;

        /// <summary>
        /// Lifecycle status.
        /// </summary>
        public AskProposalStatusEnum Status { get; set; } = AskProposalStatusEnum.Pending;

        /// <summary>
        /// Tool result (JSON text) after execution, or null.
        /// </summary>
        public string? ResultText { get; set; } = null;

        /// <summary>
        /// Error text when the action failed or was refused, or null.
        /// </summary>
        public string? ErrorText { get; set; } = null;

        /// <summary>
        /// User that approved or rejected the proposal, or null.
        /// </summary>
        public string? DecidedByUserId { get; set; } = null;

        /// <summary>
        /// UTC time of the decision, or null.
        /// </summary>
        public DateTime? DecidedUtc { get; set; } = null;

        /// <summary>
        /// UTC time the action executed, or null.
        /// </summary>
        public DateTime? ExecutedUtc { get; set; } = null;

        /// <summary>
        /// UTC creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC last update time.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.AskActionProposalIdPrefix, 24);
        private string _ThreadId = String.Empty;
        private string _ToolName = String.Empty;
        private string _ArgumentsText = "{}";
        private string _SummaryText = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskActionProposal()
        {
        }

        #endregion
    }
}
