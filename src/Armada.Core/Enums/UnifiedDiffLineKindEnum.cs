namespace Armada.Core.Enums
{
    /// <summary>
    /// Structural role of one line of git unified diff text, as determined by <c>UnifiedDiffParser</c> from the
    /// headers and hunk line counts (so a content line that looks like a header is still content).
    /// </summary>
    public enum UnifiedDiffLineKindEnum
    {
        /// <summary>
        /// Text outside any file section, or after a hunk ended.
        /// </summary>
        Other,

        /// <summary>
        /// A <c>diff --git</c> file header.
        /// </summary>
        FileHeader,

        /// <summary>
        /// An extended or path header (index, mode, rename, copy, similarity, binary, ---/+++).
        /// </summary>
        Meta,

        /// <summary>
        /// A hunk header (<c>@@ ... @@</c>).
        /// </summary>
        HunkHeader,

        /// <summary>
        /// An added line inside a hunk.
        /// </summary>
        Added,

        /// <summary>
        /// A removed line inside a hunk.
        /// </summary>
        Deleted,

        /// <summary>
        /// An unchanged context line inside a hunk.
        /// </summary>
        Context,

        /// <summary>
        /// A <c>\ No newline at end of file</c> marker.
        /// </summary>
        NoNewline
    }
}
