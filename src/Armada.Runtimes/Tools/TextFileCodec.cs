namespace Armada.Runtimes.Tools
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Reads and writes text files for the editing tools so a rewrite keeps the file's byte order mark, encoding,
    /// and line endings. A file without a byte order mark is read and written as UTF-8 without one.
    /// </summary>
    public static class TextFileCodec
    {
        /// <summary>
        /// Read a text file for editing.
        /// </summary>
        /// <param name="path">The file to read.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The file's LF-normalized content with its encoding and line ending.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null.</exception>
        public static async Task<TextFileSnapshot> ReadAsync(string path, CancellationToken token = default)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            byte[] bytes = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
            Encoding encoding = DetectEncoding(bytes, out int preambleLength);
            string content = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
            return new TextFileSnapshot
            {
                LfContent = NormalizeToLf(content),
                LineEnding = DetectLineEnding(content),
                Encoding = encoding
            };
        }

        /// <summary>
        /// Write LF-normalized content with the snapshot's line ending, encoding, and byte order mark.
        /// </summary>
        /// <param name="path">The file to write.</param>
        /// <param name="lfContent">The content with LF line endings.</param>
        /// <param name="format">The encoding and line ending to write with.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the file is written.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static async Task WriteAsync(string path, string lfContent, TextFileSnapshot format, CancellationToken token = default)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (lfContent == null) throw new ArgumentNullException(nameof(lfContent));
            if (format == null) throw new ArgumentNullException(nameof(format));
            string output = format.LineEnding == "\n" ? lfContent : lfContent.Replace("\n", format.LineEnding);
            byte[] preamble = format.Encoding.GetPreamble();
            byte[] body = format.Encoding.GetBytes(output);
            byte[] bytes = new byte[preamble.Length + body.Length];
            Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
            Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
            await File.WriteAllBytesAsync(path, bytes, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Normalize CRLF and lone CR line endings to LF.
        /// </summary>
        /// <param name="text">The text to normalize.</param>
        /// <returns>The text with LF line endings.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is null.</exception>
        public static string NormalizeToLf(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static Encoding DetectEncoding(byte[] bytes, out int preambleLength)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                preambleLength = 3;
                return new UTF8Encoding(true);
            }

            if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
            {
                preambleLength = 4;
                return new UTF32Encoding(false, true);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                preambleLength = 2;
                return new UnicodeEncoding(false, true);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                preambleLength = 2;
                return new UnicodeEncoding(true, true);
            }

            preambleLength = 0;
            return new UTF8Encoding(false);
        }

        private static string DetectLineEnding(string content)
        {
            for (int i = 0; i < content.Length; i++)
            {
                if (content[i] == '\r') return i + 1 < content.Length && content[i + 1] == '\n' ? "\r\n" : "\r";
                if (content[i] == '\n') return "\n";
            }

            return Environment.NewLine;
        }
    }
}
