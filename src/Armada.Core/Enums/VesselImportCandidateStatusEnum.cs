namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Discovery classification of a single vessel import candidate path.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum VesselImportCandidateStatusEnum
    {
        /// <summary>
        /// A git repository that is not yet registered as a vessel in the tenant.
        /// </summary>
        New,

        /// <summary>
        /// A git repository that already matches an existing vessel (by working directory or normalized remote URL).
        /// </summary>
        AlreadyOnboarded,

        /// <summary>
        /// A git worktree or submodule (the .git entry is a file); excluded by default.
        /// </summary>
        Worktree,

        /// <summary>
        /// A directory owned by Armada itself (repos, docks, or data directory); always excluded.
        /// </summary>
        ArmadaManaged,

        /// <summary>
        /// The path does not exist or is not visible to the host performing discovery.
        /// </summary>
        NotFound,

        /// <summary>
        /// The path exists but is not a git repository and contains none within the scan depth.
        /// </summary>
        NotGit,

        /// <summary>
        /// The path could not be read because of permissions.
        /// </summary>
        AccessDenied
    }
}
