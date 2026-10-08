namespace Armada.Core.Hosting
{
    using System;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Reads the end of a log file, then follows it as it grows (tail -f), without blocking the process writing it.
    /// Reads are bounded so a large log never loads whole and start on a line boundary. A trailing line without its
    /// newline yet comes back separately as <see cref="LogReadResult.PartialLine"/> and is read again (complete) by the
    /// next <see cref="ReadFrom"/>, so a viewer shows it now and replaces it later.
    /// </summary>
    public static class LogTailReader
    {
        #region Public-Members

        /// <summary>
        /// Default number of bytes read from the end of a file.
        /// </summary>
        public const int DefaultTailBytes = 256 * 1024;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read the last <paramref name="maxBytes"/> of a file, starting at the first complete line.
        /// </summary>
        /// <param name="path">File.</param>
        /// <param name="maxBytes">Maximum bytes to read.</param>
        /// <returns>The text and the offset to follow from.</returns>
        /// <exception cref="FileNotFoundException">The file does not exist.</exception>
        public static LogReadResult ReadTail(string path, int maxBytes = DefaultTailBytes)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (maxBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxBytes));

            using (FileStream stream = Open(path))
            {
                long length = stream.Length;
                long start = Math.Max(0, length - maxBytes);
                return Read(stream, start, length, start > 0, false);
            }
        }

        /// <summary>
        /// Read what was appended since <paramref name="offset"/>, at most <paramref name="maxBytes"/>. When the
        /// file is now shorter than the offset it was truncated or replaced, and this reads its tail instead with
        /// <see cref="LogReadResult.WasReset"/> set.
        /// </summary>
        /// <param name="path">File.</param>
        /// <param name="offset">EndOffset of the previous read.</param>
        /// <param name="maxBytes">Maximum bytes to read; when more was appended, the read skips ahead to the newest.</param>
        /// <returns>The new text and the offset to follow from.</returns>
        /// <exception cref="FileNotFoundException">The file does not exist.</exception>
        public static LogReadResult ReadFrom(string path, long offset, int maxBytes = DefaultTailBytes)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (maxBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxBytes));

            using (FileStream stream = Open(path))
            {
                long length = stream.Length;
                if (length < offset)
                {
                    long tailStart = Math.Max(0, length - maxBytes);
                    return Read(stream, tailStart, length, tailStart > 0, true);
                }

                long start = offset;
                bool skipped = false;
                if (length - start > maxBytes)
                {
                    start = length - maxBytes;
                    skipped = true;
                }

                return Read(stream, start, length, skipped, false);
            }
        }

        #endregion

        #region Private-Methods

        private static FileStream Open(string path)
        {
            // The writer keeps the file open; share read, write, and delete so rotation is not blocked.
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }

        private static LogReadResult Read(FileStream stream, long start, long length, bool startedMidFile, bool wasReset)
        {
            LogReadResult result = new LogReadResult
            {
                FileLength = length,
                EndOffset = start,
                StartedMidFile = startedMidFile,
                WasReset = wasReset
            };

            int count = (int)(length - start);
            if (count <= 0) return result;

            byte[] buffer = new byte[count];
            stream.Seek(start, SeekOrigin.Begin);
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(buffer, read, count - read);
                if (n <= 0) break;
                read += n;
            }

            int first = 0;
            if (startedMidFile)
            {
                // Skip the partial first line (it may also start inside a multi-byte character).
                int newline = Array.IndexOf(buffer, (byte)'\n', 0, read);
                // With no newline at all the read is one line longer than the window: keep it rather than show nothing.
                first = newline < 0 ? 0 : newline + 1;
            }

            int last = read > first ? Array.LastIndexOf(buffer, (byte)'\n', read - 1, read - first) : -1;
            int end = last >= first ? last + 1 : first;

            result.Text = Encoding.UTF8.GetString(buffer, first, end - first);
            result.PartialLine = Encoding.UTF8.GetString(buffer, end, read - end);
            result.EndOffset = start + end;
            return result;
        }

        #endregion
    }
}
