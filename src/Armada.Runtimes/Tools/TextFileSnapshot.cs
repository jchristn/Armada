namespace Armada.Runtimes.Tools
{
    using System;
    using System.Text;

    /// <summary>
    /// A text file read for editing: its content normalized to LF, plus the encoding, byte order mark, and line
    /// ending to write it back with.
    /// </summary>
    public class TextFileSnapshot
    {
        /// <summary>
        /// The file content with every line ending normalized to LF.
        /// </summary>
        public string LfContent { get; set; } = String.Empty;

        /// <summary>
        /// The line ending the file uses (the first one found), or the platform default when it has none.
        /// </summary>
        public string LineEnding { get; set; } = Environment.NewLine;

        /// <summary>
        /// The encoding to write the file back with. Its preamble is the byte order mark the file had, if any.
        /// </summary>
        public Encoding Encoding { get; set; } = new UTF8Encoding(false);
    }
}
