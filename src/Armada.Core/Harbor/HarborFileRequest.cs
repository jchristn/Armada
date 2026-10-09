namespace Armada.Core.Harbor
{
    /// <summary>
    /// Server-to-Harbor request to work with a file on the Harbor host. Without <see cref="Root"/> it reads or writes a
    /// text file in one of the Harbor's docks (mission instruction files, playbooks) or adds a git exclude entry for a
    /// dock, and the absolute <see cref="Path"/> must be inside the Harbor's docks directory. With <see cref="Root"/> it
    /// works in a vessel's checkout (Workspace, check-run artifacts, readiness): <see cref="Path"/> is relative to the
    /// root, and the root must be inside the docks directory or be the checkout the Harbor itself maps the vessel to
    /// (<see cref="VesselId"/>, <see cref="VesselName"/>, <see cref="RepoUrl"/>). The Harbor refuses any other path.
    /// Answered by <see cref="HarborFileResult"/>.
    /// </summary>
    public class HarborFileRequest : HarborMessage
    {
        #region Public-Members

        /// <summary>
        /// Request identifier tying the result back to this call.
        /// </summary>
        public string RequestId { get; set; } = string.Empty;

        /// <summary>
        /// The operation to perform.
        /// </summary>
        public HarborFileOperationEnum Operation { get; set; } = HarborFileOperationEnum.Stat;

        /// <summary>
        /// Without <see cref="Root"/>: absolute path on the Harbor host (for AddGitExclude, the dock's worktree path).
        /// With <see cref="Root"/>: a path relative to the root (empty for the root itself).
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Text to write (Write, SaveFile), or the exclude line to add (AddGitExclude).
        /// </summary>
        public string? Content { get; set; } = null;

        /// <summary>
        /// The checkout (or dock) the request is confined to, as an absolute path on the Harbor host, or null for a dock
        /// file request by absolute path.
        /// </summary>
        public string? Root { get; set; } = null;

        /// <summary>
        /// Vessel identifier, so the Harbor can check that <see cref="Root"/> is its checkout of the vessel.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Vessel name (Harbor settings may name a vessel by its name).
        /// </summary>
        public string? VesselName { get; set; } = null;

        /// <summary>
        /// The vessel's repository URL (matched against checkouts under the Harbor's root folders).
        /// </summary>
        public string? RepoUrl { get; set; } = null;

        /// <summary>
        /// Destination path relative to <see cref="Root"/> (Rename).
        /// </summary>
        public string? NewPath { get; set; } = null;

        /// <summary>
        /// Hash of the content the editor opened (SaveFile); empty for a new file.
        /// </summary>
        public string? ExpectedHash { get; set; } = null;

        /// <summary>
        /// Text to find (Search).
        /// </summary>
        public string? Query { get; set; } = null;

        /// <summary>
        /// Largest number of matches (Search).
        /// </summary>
        public int MaxResults { get; set; } = 200;

        /// <summary>
        /// Largest file returned by Read with a <see cref="Root"/>, in bytes; a larger file is reported with
        /// <see cref="HarborFileResult.Truncated"/> and no content. 0 means the Harbor's default (8 MB).
        /// </summary>
        public long MaxBytes { get; set; } = 0;

        #endregion
    }
}
