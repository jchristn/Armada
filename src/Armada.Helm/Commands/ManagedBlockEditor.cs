namespace Armada.Helm.Commands
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// Inserts, replaces, and removes an Armada-managed block (delimited by a begin and an end marker) in a text file
    /// that the user also edits. A file whose markers are not exactly one begin followed by one end is malformed and
    /// raises <see cref="InvalidDataException"/> instead of being edited, so a lost end marker cannot cause a second
    /// block to be appended or user text to be swallowed.
    /// </summary>
    public static class ManagedBlockEditor
    {
        #region Public-Methods

        /// <summary>
        /// Replace the managed block in <paramref name="existing"/> with <paramref name="managedBlock"/>, or append it
        /// when the file has no markers.
        /// </summary>
        /// <param name="existing">Current file text, or null for a missing file.</param>
        /// <param name="managedBlock">Complete managed block, markers included.</param>
        /// <param name="beginMarker">Begin marker.</param>
        /// <param name="endMarker">End marker.</param>
        /// <returns>Updated text.</returns>
        /// <exception cref="InvalidDataException">The existing markers are malformed.</exception>
        public static string Upsert(string? existing, string managedBlock, string beginMarker, string endMarker)
        {
            if (String.IsNullOrWhiteSpace(existing))
                return managedBlock + Environment.NewLine;

            int start;
            int afterEnd;
            if (TryLocate(existing, beginMarker, endMarker, out start, out afterEnd))
            {
                return Combine(existing[..start].TrimEnd(), managedBlock, existing[afterEnd..].TrimStart());
            }

            return Combine(existing.TrimEnd(), managedBlock, String.Empty);
        }

        /// <summary>
        /// Remove the managed block. Text without markers is returned unchanged.
        /// </summary>
        /// <param name="existing">Current file text.</param>
        /// <param name="beginMarker">Begin marker.</param>
        /// <param name="endMarker">End marker.</param>
        /// <returns>Updated text.</returns>
        /// <exception cref="InvalidDataException">The existing markers are malformed.</exception>
        public static string Remove(string existing, string beginMarker, string endMarker)
        {
            if (String.IsNullOrEmpty(existing)) return existing ?? String.Empty;

            int start;
            int afterEnd;
            if (!TryLocate(existing, beginMarker, endMarker, out start, out afterEnd))
                return existing;

            return Combine(existing[..start].TrimEnd(), String.Empty, existing[afterEnd..].TrimStart());
        }

        #endregion

        #region Private-Methods

        private static bool TryLocate(string text, string beginMarker, string endMarker, out int start, out int afterEnd)
        {
            start = -1;
            afterEnd = -1;
            int beginCount = CountOccurrences(text, beginMarker);
            int endCount = CountOccurrences(text, endMarker);
            if (beginCount == 0 && endCount == 0) return false;

            if (beginCount != 1 || endCount != 1)
                throw new InvalidDataException("Expected one '" + beginMarker + "' followed by one '" + endMarker + "' but found " + beginCount + " begin and " + endCount + " end marker(s). Fix the markers by hand.");

            start = text.IndexOf(beginMarker, StringComparison.Ordinal);
            int end = text.IndexOf(endMarker, StringComparison.Ordinal);
            if (end < start + beginMarker.Length)
                throw new InvalidDataException("'" + endMarker + "' appears before '" + beginMarker + "'. Fix the markers by hand.");

            afterEnd = end + endMarker.Length;
            return true;
        }

        private static int CountOccurrences(string text, string marker)
        {
            int count = 0;
            int index = text.IndexOf(marker, StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = text.IndexOf(marker, index + marker.Length, StringComparison.Ordinal);
            }

            return count;
        }

        private static string Combine(string prefix, string middle, string suffix)
        {
            List<string> sections = new List<string>();
            if (!String.IsNullOrWhiteSpace(prefix)) sections.Add(prefix);
            if (!String.IsNullOrWhiteSpace(middle)) sections.Add(middle);
            if (!String.IsNullOrWhiteSpace(suffix)) sections.Add(suffix);

            return sections.Count == 0
                ? String.Empty
                : String.Join(Environment.NewLine + Environment.NewLine, sections) + Environment.NewLine;
        }

        #endregion
    }
}
