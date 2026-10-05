namespace Armada.Tui.Services
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// One item in the approvals queue.
    /// </summary>
    public class ApprovalItem
    {
        #region Public-Members

        /// <summary>
        /// Stable key (kind plus entity id).
        /// </summary>
        public string Key
        {
            get { return Kind + ":" + EntityId; }
        }

        /// <summary>
        /// Kind.
        /// </summary>
        public ApprovalKindEnum Kind { get; set; } = ApprovalKindEnum.AskProposal;

        /// <summary>
        /// Entity id (proposal, mission, deployment, captain).
        /// </summary>
        public string EntityId { get; set; } = "";

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// Detail.
        /// </summary>
        public string Detail { get; set; } = "";

        /// <summary>
        /// Route opened by Enter.
        /// </summary>
        public string? Route { get; set; } = null;

        /// <summary>
        /// Urgency (higher first). Default 0.
        /// </summary>
        public int Urgency { get; set; } = 0;

        /// <summary>
        /// When the item appeared.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Parent id: the Ask thread of a proposal, or null.
        /// </summary>
        public string? ParentId { get; set; } = null;

        /// <summary>
        /// Tool name of an Ask proposal or a CLI permission request, or null.
        /// </summary>
        public string? ToolName { get; set; } = null;

        /// <summary>
        /// Exact arguments of an Ask proposal (JSON text), or null.
        /// </summary>
        public string? Arguments { get; set; } = null;

        /// <summary>
        /// Expiry of an Ask proposal or a CLI permission request, or null.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// Display name of the entity (mission title, captain name, deployment title), or null.
        /// </summary>
        public string? EntityName { get; set; } = null;

        /// <summary>
        /// Source of the item (English): Ask Armada, Needs You, or Live.
        /// </summary>
        public string Source { get; set; } = "";

        /// <summary>
        /// The CLI permission request of a <see cref="ApprovalKindEnum.CliPermission"/> item (tool, command, captain,
        /// vessel, mission or thread, expiry, and whether the user may decide it), or null for other kinds.
        /// </summary>
        public CliPermissionRequest? CliPermission { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ApprovalItem()
        {
        }

        #endregion
    }
}
