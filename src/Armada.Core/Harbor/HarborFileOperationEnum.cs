namespace Armada.Core.Harbor
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// What a <see cref="HarborFileRequest"/> asks the Harbor to do with a file in one of its docks, or (with
    /// <see cref="HarborFileRequest.Root"/> set) in a vessel's checkout on the Harbor host.
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
        AddGitExclude,

        /// <summary>
        /// List one directory of a checkout, with Workspace rules (hidden build folders and .git are left out). Requires
        /// <see cref="HarborFileRequest.Root"/>; answered with <see cref="HarborFileResult.Tree"/>.
        /// </summary>
        [EnumMember(Value = "List")]
        List,

        /// <summary>
        /// Read one file of a checkout for Workspace (preview, hash, editability). Requires
        /// <see cref="HarborFileRequest.Root"/>; answered with <see cref="HarborFileResult.File"/>.
        /// </summary>
        [EnumMember(Value = "ReadFile")]
        ReadFile,

        /// <summary>
        /// Save one text file of a checkout with optimistic concurrency (<see cref="HarborFileRequest.ExpectedHash"/>).
        /// Requires <see cref="HarborFileRequest.Root"/>; answered with <see cref="HarborFileResult.Saved"/>.
        /// </summary>
        [EnumMember(Value = "SaveFile")]
        SaveFile,

        /// <summary>
        /// Create one directory in a checkout. Requires <see cref="HarborFileRequest.Root"/>; answered with
        /// <see cref="HarborFileResult.Entry"/>.
        /// </summary>
        [EnumMember(Value = "CreateDirectory")]
        CreateDirectory,

        /// <summary>
        /// Rename or move one entry of a checkout to <see cref="HarborFileRequest.NewPath"/>. Requires
        /// <see cref="HarborFileRequest.Root"/>; answered with <see cref="HarborFileResult.Entry"/>.
        /// </summary>
        [EnumMember(Value = "Rename")]
        Rename,

        /// <summary>
        /// Delete one entry of a checkout. Requires <see cref="HarborFileRequest.Root"/>; answered with
        /// <see cref="HarborFileResult.Entry"/>.
        /// </summary>
        [EnumMember(Value = "Delete")]
        Delete,

        /// <summary>
        /// Search the text files of a checkout for <see cref="HarborFileRequest.Query"/>. Requires
        /// <see cref="HarborFileRequest.Root"/>; answered with <see cref="HarborFileResult.Search"/>.
        /// </summary>
        [EnumMember(Value = "Search")]
        Search
    }
}
