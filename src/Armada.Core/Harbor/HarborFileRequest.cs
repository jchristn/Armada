namespace Armada.Core.Harbor
{
    /// <summary>
    /// Server-to-Harbor request to read or write a text file in one of the Harbor's docks (mission instruction files,
    /// playbooks) or to add a git exclude entry for a dock. Paths must be inside the Harbor's docks directory; the
    /// Harbor refuses any other path. Answered by <see cref="HarborFileResult"/>.
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
        /// Absolute path on the Harbor host. For AddGitExclude, the dock's worktree path.
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Text to write (Write), or the exclude line to add (AddGitExclude).
        /// </summary>
        public string? Content { get; set; } = null;

        #endregion
    }
}
