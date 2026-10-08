namespace Armada.Core.Harbor
{
    /// <summary>
    /// Harbor-to-server reply to a <see cref="HarborFileRequest"/>.
    /// </summary>
    public class HarborFileResult : HarborMessage
    {
        #region Public-Members

        /// <summary>
        /// Request identifier this result answers.
        /// </summary>
        public string RequestId { get; set; } = string.Empty;

        /// <summary>
        /// Whether the operation succeeded. A Read or Stat of a missing path succeeds with <see cref="Exists"/> false.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Whether the path exists (Stat, Read).
        /// </summary>
        public bool Exists { get; set; } = false;

        /// <summary>
        /// Whether the path is a directory (Stat).
        /// </summary>
        public bool IsDirectory { get; set; } = false;

        /// <summary>
        /// The file's text (Read), or null.
        /// </summary>
        public string? Content { get; set; } = null;

        /// <summary>
        /// Why the operation failed, or null.
        /// </summary>
        public string? Message { get; set; } = null;

        #endregion
    }
}
