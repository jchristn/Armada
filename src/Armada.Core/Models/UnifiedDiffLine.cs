namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// One added line inside a unified diff hunk.
    /// </summary>
    public class UnifiedDiffLine
    {
        #region Public-Members

        /// <summary>
        /// 1-based line number in the new version of the file.
        /// </summary>
        public int NewLineNumber { get; set; } = 0;

        /// <summary>
        /// Line content without the leading '+' marker.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        #endregion
    }
}
