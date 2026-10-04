namespace Armada.Core.Services
{
    using System;
    using System.IO;

    /// <summary>
    /// Guards recursive deletes of host directories: Armada removes a vessel's bare repository or dock directory only
    /// when it lies strictly inside the directory Armada manages for it (Settings.ReposDirectory or
    /// Settings.DocksDirectory). A vessel's LocalPath and Name are caller-controlled, so without this guard deleting a
    /// vessel could delete any directory the Admiral process can write.
    /// </summary>
    public static class ManagedPaths
    {
        #region Public-Methods

        /// <summary>
        /// Whether <paramref name="path"/> resolves to a location strictly inside <paramref name="root"/> (not the root
        /// itself), after normalizing both and resolving a symbolic link at the path itself.
        /// </summary>
        /// <param name="path">Candidate path.</param>
        /// <param name="root">Managed root directory.</param>
        /// <returns>True when the path is inside the root.</returns>
        public static bool IsStrictlyUnder(string? path, string? root)
        {
            if (String.IsNullOrWhiteSpace(path) || String.IsNullOrWhiteSpace(root)) return false;
            try
            {
                string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
                string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

                DirectoryInfo info = new DirectoryInfo(fullPath);
                if (info.Exists && info.LinkTarget != null) return false;

                StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                string prefix = fullRoot + Path.DirectorySeparatorChar;
                return fullPath.Length > prefix.Length && fullPath.StartsWith(prefix, comparison);
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion
    }
}
