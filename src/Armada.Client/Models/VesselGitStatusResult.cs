namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Ahead and behind counts for a vessel's working copy.
    /// </summary>
    public class VesselGitStatusResult
    {
        #region Public-Members

        /// <summary>
        /// Vessel id.
        /// </summary>
        public string VesselId { get; set; } = "";

        /// <summary>
        /// Commits ahead of the remote, or null.
        /// </summary>
        public int? CommitsAhead { get; set; } = null;

        /// <summary>
        /// Commits behind the remote, or null.
        /// </summary>
        public int? CommitsBehind { get; set; } = null;

        /// <summary>
        /// Error, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselGitStatusResult()
        {
        }

        #endregion
    }
}
