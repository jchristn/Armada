namespace Armada.Tui.Services
{
    using System;

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
