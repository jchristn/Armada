namespace Armada.Core
{
    using System;
    using System.IO;

    /// <summary>
    /// Produces a canonical form of a filesystem path for equality comparisons. Beyond
    /// <see cref="Path.GetFullPath(string)"/>, it resolves symbolic links in every existing segment, so
    /// two spellings of the same directory compare equal. This matters on macOS, where the temp
    /// directory lives under <c>/var</c> (a link to <c>/private/var</c>) and git reports the resolved
    /// <c>/private/var/...</c> form for worktree and top-level paths.
    /// </summary>
    public static class PathCanonicalizer
    {
        #region Public-Methods

        /// <summary>
        /// Return the canonical form of a path: absolute, with symbolic links in existing segments
        /// resolved and trailing directory separators removed. Segments that do not exist are kept as-is.
        /// </summary>
        /// <param name="path">Path to canonicalize.</param>
        /// <returns>Canonical path.</returns>
        public static string Canonicalize(string path)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));

            string full = Path.GetFullPath(path);
            string? root = Path.GetPathRoot(full);
            if (String.IsNullOrEmpty(root)) return TrimSeparators(full);

            string current = root;
            string[] segments = full.Substring(root.Length).Split(
                new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (string segment in segments)
            {
                current = Path.Combine(current, segment);
                current = ResolveLink(current);
            }

            return TrimSeparators(current);
        }

        /// <summary>
        /// Whether two paths refer to the same location after canonicalization. The comparison is
        /// case-insensitive, matching how Armada compares repository paths on every platform.
        /// </summary>
        /// <param name="first">First path.</param>
        /// <param name="second">Second path.</param>
        /// <returns>True when both paths canonicalize to the same location.</returns>
        public static bool AreEquivalent(string first, string second)
        {
            if (String.IsNullOrEmpty(first)) throw new ArgumentNullException(nameof(first));
            if (String.IsNullOrEmpty(second)) throw new ArgumentNullException(nameof(second));
            return String.Equals(Canonicalize(first), Canonicalize(second), StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Private-Methods

        private static string ResolveLink(string path)
        {
            try
            {
                FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
                if (!info.Exists || info.LinkTarget == null) return path;

                // The final target exists and is not itself a link, but its parent directories may be
                // (e.g. a link whose target lives under /var on macOS), so canonicalize it in turn.
                FileSystemInfo? target = info.ResolveLinkTarget(true);
                return target != null ? Canonicalize(target.FullName) : path;
            }
            catch (IOException)
            {
                return path;
            }
            catch (UnauthorizedAccessException)
            {
                return path;
            }
        }

        private static string TrimSeparators(string path)
        {
            string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return trimmed.Length == 0 ? path : trimmed;
        }

        #endregion
    }
}
