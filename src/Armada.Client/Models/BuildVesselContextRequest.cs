namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Request to build a vessel's model context with a captain.
    /// </summary>
    public class BuildVesselContextRequest
    {
        #region Public-Members

        /// <summary>
        /// Captain that analyzes the repository.
        /// </summary>
        public string CaptainId { get; set; } = "";

        /// <summary>
        /// Guidance notes, or null.
        /// </summary>
        public string? Notes { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BuildVesselContextRequest()
        {
        }

        #endregion
    }
}
