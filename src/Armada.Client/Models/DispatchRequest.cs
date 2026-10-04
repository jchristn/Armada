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
