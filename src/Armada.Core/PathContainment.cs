namespace Armada.Core
{
    using System;
    using System.IO;

    /// <summary>
    /// Filesystem containment checks. A path is inside a root when <see cref="Path.GetRelativePath(string, string)"/>
    /// from the root yields a relative path that is not rooted and does not climb out with "..". This replaces string
    /// prefix checks, which accept sibling directories that share a prefix (<c>/srv/app</c> versus
    /// <c>/srv/app-secrets</c>). Both paths are made absolute with <see cref="Path.GetFullPath(string)"/> first, so
    /// "." and ".." segments are resolved. Case sensitivity follows the platform's file system rules.
    /// </summary>
    public static class PathContainment
    {
        #region Public-Methods

        /// <summary>
        /// Whether <paramref name="candidatePath"/> is inside <paramref name="rootDirectory"/>.
        /// </summary>
        /// <param name="rootDirectory">Root directory.</param>
        /// <param name="candidatePath">Absolute path, or a path relative to the current directory.</param>
        /// <param name="allowRoot">Whether the root itself counts as inside.</param>
        /// <returns>True when the candidate is the root (when allowed) or a descendant of it.</returns>
        public static bool IsInside(string rootDirectory, string candidatePath, bool allowRoot = false)
        {
            if (String.IsNullOrEmpty(rootDirectory)) throw new ArgumentNullException(nameof(rootDirectory));
            if (String.IsNullOrEmpty(candidatePath)) throw new ArgumentNullException(nameof(candidatePath));

            string root = Path.GetFullPath(rootDirectory);
            string candidate = Path.GetFullPath(candidatePath);
            string relative = Path.GetRelativePath(root, candidate);

            if (String.Equals(relative, ".", StringComparison.Ordinal))
            {
                return allowRoot;
            }

            if (Path.IsPathRooted(relative))
            {
                return false;
            }

            if (String.Equals(relative, "..", StringComparison.Ordinal) ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Resolve <paramref name="relativePath"/> against <paramref name="rootDirectory"/> and return the absolute
        /// path only when it stays inside the root. Rooted inputs and inputs that climb out with ".." return null.
        /// </summary>
        /// <param name="rootDirectory">Root directory.</param>
        /// <param name="relativePath">Path relative to the root.</param>
        /// <param name="allowRoot">Whether resolving to the root itself is allowed.</param>
        /// <returns>The absolute path inside the root, or null.</returns>
        public static string? ResolveInside(string rootDirectory, string relativePath, bool allowRoot = false)
        {
            if (String.IsNullOrEmpty(rootDirectory)) throw new ArgumentNullException(nameof(rootDirectory));
            if (String.IsNullOrEmpty(relativePath)) return null;
            if (Path.IsPathRooted(relativePath)) return null;

            string root = Path.GetFullPath(rootDirectory);
            string candidate = Path.GetFullPath(Path.Combine(root, relativePath));
            return IsInside(root, candidate, allowRoot) ? candidate : null;
        }

        #endregion
    }
}
