namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Request to rebuild the server.
    /// </summary>
    public class RebuildRequest
    {
        #region Public-Members

        /// <summary>
        /// Branch or ref to build, or null for the default.
        /// </summary>
        public string? Ref { get; set; } = null;

        /// <summary>
        /// Skip the dashboard build.
        /// </summary>
        public bool? SkipDashboard { get; set; } = null;

        /// <summary>
        /// Health-check window before automatic rollback.
        /// </summary>
        public int? RollbackTimeoutSeconds { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RebuildRequest()
        {
        }

        #endregion
    }
}
