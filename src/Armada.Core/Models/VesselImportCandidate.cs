namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// One discovered directory and its classification, before it is persisted as a <see cref="VesselImportItem"/>.
    /// </summary>
    public class VesselImportCandidate
    {
        #region Public-Members

        /// <summary>
        /// Normalized absolute path, with on-disk casing where it could be resolved. Never null.
        /// </summary>
        public string Path
        {
            get => _Path;
            set => _Path = value ?? String.Empty;
        }

        /// <summary>
        /// Proposed vessel name: the folder name, made unique within the tenant with a -2, -3 suffix. Never null.
        /// </summary>
        public string ProposedName
        {
            get => _ProposedName;
            set => _ProposedName = value ?? String.Empty;
        }

        /// <summary>
        /// Origin remote URL, or null when the repository has no origin.
        /// </summary>
        public string? RemoteUrl { get; set; } = null;

        /// <summary>
        /// Inferred default branch (origin HEAD, then the current branch, then main), or null for non-repositories.
        /// </summary>
        public string? DefaultBranch { get; set; } = null;

        /// <summary>
        /// Identifier of an existing tenant vessel with the same working directory or normalized remote URL, or null.
        /// </summary>
        public string? ExistingVesselId { get; set; } = null;

        /// <summary>
        /// Classification. Defaults to New.
        /// </summary>
        public VesselImportCandidateStatusEnum Status { get; set; } = VesselImportCandidateStatusEnum.New;

        #endregion

        #region Private-Members

        private string _Path = String.Empty;
        private string _ProposedName = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselImportCandidate()
        {
        }

        #endregion
    }
}
