namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Body of PUT /api/v1/ask/threads/{id}. Null fields are left unchanged; an empty CaptainId clears the captain.
    /// </summary>
    public class AskThreadUpdateRequest
    {
        #region Public-Members

        /// <summary>
        /// New title, or null.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// New captain (cpt_ prefix), empty string to clear, or null to keep.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// New auto-approve flag, or null.
        /// </summary>
        public bool? AutoApprove { get; set; } = null;

        /// <summary>
        /// New pinned flag, or null.
        /// </summary>
        public bool? Pinned { get; set; } = null;

        /// <summary>
        /// New archived flag, or null.
        /// </summary>
        public bool? Archived { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskThreadUpdateRequest()
        {
        }

        #endregion
    }
}
