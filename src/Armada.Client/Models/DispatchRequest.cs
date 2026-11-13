namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// One mission to dispatch (also used inside a voyage create request).
    /// </summary>
    public class DispatchRequest
    {
        #region Public-Members

        /// <summary>
        /// Target vessel id, or null inside a voyage that sets the vessel.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Mission title.
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// Mission description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Priority (lower runs first), or null for the server default.
        /// </summary>
        public int? Priority { get; set; } = null;

        /// <summary>
        /// True for evaluation work: the mission's stage outcomes never count as production data. A mission added to an
        /// evaluation voyage is flagged regardless. Default false.
        /// </summary>
        public bool IsEvaluation { get; set; } = false;

        /// <summary>
        /// Optional key naming this mission within a voyage create request, so a later mission can name it as ParentKey.
        /// </summary>
        public string? Key { get; set; } = null;

        /// <summary>
        /// Optional Key of an earlier mission in the same voyage create request; this mission becomes its child.
        /// </summary>
        public string? ParentKey { get; set; } = null;

        /// <summary>
        /// Optional branch relationship to the parent: New, ContinueParent or ForkFromParent.
        /// </summary>
        public Armada.Core.Enums.MissionBranchStrategyEnum? BranchStrategy { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DispatchRequest()
        {
        }

        #endregion
    }
}
