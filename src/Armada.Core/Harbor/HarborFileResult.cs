namespace Armada.Core.Harbor
{
    using System;
    using Armada.Core.Models;

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
        /// Why the operation failed (None on success). Harbors from before checkout operations leave it None on failure.
        /// </summary>
        public HarborFileErrorCodeEnum ErrorCode { get; set; } = HarborFileErrorCodeEnum.None;

        /// <summary>
        /// Whether the path exists (Stat, Read).
        /// </summary>
        public bool Exists { get; set; } = false;

        /// <summary>
        /// Whether the path is a directory (Stat).
        /// </summary>
        public bool IsDirectory { get; set; } = false;

        /// <summary>
        /// The file's size in bytes (Stat and Read with a root), or null.
        /// </summary>
        public long? SizeBytes { get; set; } = null;

        /// <summary>
        /// When the file was last written, in UTC (Stat and Read with a root), or null.
        /// </summary>
        public DateTime? LastWriteUtc { get; set; } = null;

        /// <summary>
        /// Whether a Read with a root left the content out because the file is larger than the request allows.
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// The file's text (Read), or null.
        /// </summary>
        public string? Content { get; set; } = null;

        /// <summary>
        /// The directory listing (List), or null.
        /// </summary>
        public WorkspaceTreeResult? Tree { get; set; } = null;

        /// <summary>
        /// The file (ReadFile), or null.
        /// </summary>
        public WorkspaceFileResponse? File { get; set; } = null;

        /// <summary>
        /// The save result (SaveFile), or null.
        /// </summary>
        public WorkspaceSaveResult? Saved { get; set; } = null;

        /// <summary>
        /// The result of CreateDirectory, Rename, or Delete, or null.
        /// </summary>
        public WorkspaceOperationResult? Entry { get; set; } = null;

        /// <summary>
        /// The matches (Search), or null.
        /// </summary>
        public WorkspaceSearchResult? Search { get; set; } = null;

        /// <summary>
        /// Why the operation failed, or null.
        /// </summary>
        public string? Message { get; set; } = null;

        #endregion
    }
}
