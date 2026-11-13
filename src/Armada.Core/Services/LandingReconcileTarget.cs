namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Where the landing reconciler checks one vessel during a sweep: the repository it fetched and the remote ref it
    /// compares against, or why the vessel could not be checked.
    /// </summary>
    public class LandingReconcileTarget
    {
        #region Public-Members

        /// <summary>
        /// Whether the repository was reached and fetched, so its remote ref can be trusted for this sweep.
        /// </summary>
        public bool Available { get; set; } = false;

        /// <summary>
        /// Git service that runs where the repository lives (the Admiral host or a Harbor).
        /// </summary>
        public IGitService? Git { get; set; } = null;

        /// <summary>
        /// Repository path on that host.
        /// </summary>
        public string? RepositoryPath { get; set; } = null;

        /// <summary>
        /// The remote target ref (for example refs/remotes/origin/main).
        /// </summary>
        public string TargetRef { get; set; } = String.Empty;

        /// <summary>
        /// The Harbor that holds the repository, or null for the Admiral host.
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Why the vessel could not be checked, when <see cref="Available"/> is false.
        /// </summary>
        public string? Reason { get; set; } = null;

        #endregion
    }
}
