namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Body of PUT /api/v1/ask/threads/{id}. Absent fields are left unchanged; CaptainId present with null (or empty)
    /// clears the captain.
    /// </summary>
    public class AskThreadUpdateRequest
    {
        #region Public-Members

        /// <summary>
        /// New title, or null.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// New captain (cpt_ prefix); null or empty clears it when CaptainIdSpecified is true.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Whether the request body contained CaptainId (set by the route; not part of the JSON body).
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool CaptainIdSpecified { get; set; } = false;

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
