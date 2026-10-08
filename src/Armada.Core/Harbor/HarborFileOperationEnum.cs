namespace Armada.Core.Harbor
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// What a <see cref="HarborFileRequest"/> asks the Harbor to do with a file in one of its docks.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborFileOperationEnum
    {
        /// <summary>
        /// Report whether the path exists and whether it is a directory.
        /// </summary>
        [EnumMember(Value = "Stat")]
        Stat,

        /// <summary>
        /// Return a text file's content (or that it does not exist).
        /// </summary>
        [EnumMember(Value = "Read")]
        Read,

        /// <summary>
        /// Write a text file, creating its directory when needed.
        /// </summary>
        [EnumMember(Value = "Write")]
        Write,

        /// <summary>
        /// Add a line to the git exclude file (info/exclude) of the repository that the worktree at the path belongs to,
        /// unless it is already there.
        /// </summary>
        [EnumMember(Value = "AddGitExclude")]
        AddGitExclude
    }
}
