namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Kind of change git reports for one path (from --name-status or a unified diff header).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GitChangeKindEnum
    {
        /// <summary>
        /// Contents or mode modified.
        /// </summary>
        Modified,

        /// <summary>
        /// Path added.
        /// </summary>
        Added,

        /// <summary>
        /// Path deleted.
        /// </summary>
        Deleted,

        /// <summary>
        /// Path renamed (old and new path both set).
        /// </summary>
        Renamed,

        /// <summary>
        /// Path copied (old and new path both set).
        /// </summary>
        Copied,

        /// <summary>
        /// File type changed (for example a regular file became a symlink).
        /// </summary>
        TypeChanged,

        /// <summary>
        /// Unmerged (conflicted) path.
        /// </summary>
        Unmerged,

        /// <summary>
        /// A status letter git emitted that is not otherwise classified.
        /// </summary>
        Unknown
    }
}
