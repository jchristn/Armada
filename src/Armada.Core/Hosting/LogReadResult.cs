namespace Armada.Core.Hosting
{
    using System;

    /// <summary>
    /// Text read from a log file by <see cref="LogTailReader"/>, and where the next read should start.
    /// </summary>
    public class LogReadResult
    {
        #region Public-Members

        /// <summary>
        /// Complete lines read, each ending in a newline. Never null.
        /// </summary>
        public string Text
        {
            get { return _Text; }
            set { _Text = value ?? String.Empty; }
        }

        /// <summary>
        /// Text after the last newline: a line still being written, or a file's unterminated last line. Not covered by
        /// <see cref="EndOffset"/>, so the next read returns it again once complete. Never null.
        /// </summary>
        public string PartialLine
        {
            get { return _PartialLine; }
            set { _PartialLine = value ?? String.Empty; }
        }

        /// <summary>
        /// Byte offset just past <see cref="Text"/> (before <see cref="PartialLine"/>): pass it to the next <see cref="LogTailReader.ReadFrom"/>.
        /// </summary>
        public long EndOffset { get; set; } = 0;

        /// <summary>
        /// File length when it was read.
        /// </summary>
        public long FileLength { get; set; } = 0;

        /// <summary>
        /// True when earlier content was skipped: the read began part way into the file.
        /// </summary>
        public bool StartedMidFile { get; set; } = false;

        /// <summary>
        /// True when the file was shorter than the requested offset (truncated or replaced), so the read started over
        /// at its tail. A viewer should replace what it shows rather than append.
        /// </summary>
        public bool WasReset { get; set; } = false;

        #endregion

        #region Private-Members

        private string _Text = String.Empty;
        private string _PartialLine = String.Empty;

        #endregion
    }
}
