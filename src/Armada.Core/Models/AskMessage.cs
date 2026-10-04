namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// One message in an Ask Armada thread. Kind and role are typed; ContentText and ThinkingText are unmanaged text
    /// (model output). Sequence is assigned by the database, per thread, monotonically increasing.
    /// </summary>
    public class AskMessage
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (amg_ prefix).
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
        /// Thread (ath_ prefix) the message belongs to.
        /// </summary>
        public string ThreadId
        {
            get => _ThreadId;
            set => _ThreadId = value ?? String.Empty;
        }

        /// <summary>
        /// Per-thread sequence number, assigned on create (1-based, strictly increasing).
        /// </summary>
        public int Sequence { get; set; } = 0;

        /// <summary>
        /// Author role.
        /// </summary>
        public AskMessageRoleEnum Role { get; set; } = AskMessageRoleEnum.User;

        /// <summary>
        /// Message kind.
        /// </summary>
        public AskMessageKindEnum Kind { get; set; } = AskMessageKindEnum.Text;

        /// <summary>
        /// Message text (markdown). Never null.
        /// </summary>
        public string ContentText
        {
            get => _ContentText;
            set => _ContentText = value ?? String.Empty;
        }

        /// <summary>
        /// Captain reasoning shown in a collapsible section, or null.
        /// </summary>
        public string? ThinkingText { get; set; } = null;

        /// <summary>
        /// Linked action proposal (aap_ prefix) for ActionProposal and ActionResult messages, or null.
        /// </summary>
        public string? ProposalId { get; set; } = null;

        /// <summary>
        /// Linked tracked work (atw_ prefix) for WorkUpdate and ActionResult messages, or null.
        /// </summary>
        public string? TrackedWorkId { get; set; } = null;

        /// <summary>
        /// Captain that wrote the message, or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Turn duration in milliseconds for captain replies, or null.
        /// </summary>
        public long? DurationMs { get; set; } = null;

        /// <summary>
        /// UTC creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC last update time.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Tool calls the captain made while writing this message (populated on read; not a column).
        /// </summary>
        public List<AskMessageToolCall> ToolCalls
        {
            get => _ToolCalls;
            set => _ToolCalls = value ?? new List<AskMessageToolCall>();
        }

        /// <summary>
        /// The linked proposal (populated on read when ProposalId is set).
        /// </summary>
        public AskActionProposal? Proposal { get; set; } = null;

        /// <summary>
        /// The linked tracked work (populated on read when TrackedWorkId is set).
        /// </summary>
        public AskTrackedWork? TrackedWork { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.AskMessageIdPrefix, 24);
        private string _ThreadId = String.Empty;
        private string _ContentText = String.Empty;
        private List<AskMessageToolCall> _ToolCalls = new List<AskMessageToolCall>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskMessage()
        {
        }

        #endregion
    }
}
