namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The files tracked at HEAD of a vessel's repository and every directory that contains one, for checking paths a
    /// brief names. Read once per vessel and cached by <see cref="MissionOverlapAnalyzer"/>.
    /// </summary>
    public class VesselTrackedFiles
    {
        #region Public-Members

        /// <summary>Tracked files (repository-relative, forward slashes).</summary>
        public HashSet<string> Files { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Directories that contain a tracked file (no trailing slash).</summary>
        public HashSet<string> Directories { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>When the list was read.</summary>
        public DateTime ReadUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselTrackedFiles()
        {
        }

        /// <summary>
        /// Build from a list of tracked paths.
        /// </summary>
        /// <param name="paths">Paths.</param>
        /// <returns>The set.</returns>
        /// <exception cref="ArgumentNullException">Thrown when paths is null.</exception>
        public static VesselTrackedFiles From(IEnumerable<string> paths)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            VesselTrackedFiles tracked = new VesselTrackedFiles();
            foreach (string raw in paths)
            {
                string p = MissionFileSet.Normalize(raw);
                if (p.Length == 0) continue;
                tracked.Files.Add(p);
                string dir = MissionFileSet.DirectoryOf(p);
                while (dir.Length > 0 && tracked.Directories.Add(dir)) dir = MissionFileSet.DirectoryOf(dir);
            }
            return tracked;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a path is a tracked file.
        /// </summary>
        /// <param name="path">Normalized path.</param>
        /// <returns>True when tracked.</returns>
        public bool IsFile(string path)
        {
            return Files.Contains(path);
        }

        /// <summary>
        /// Whether a path is a directory with tracked files.
        /// </summary>
        /// <param name="path">Normalized path.</param>
        /// <returns>True when it is.</returns>
        public bool IsDirectory(string path)
        {
            return Directories.Contains(path);
        }

        /// <summary>
        /// Whether the first segment of a path exists at the repository root (as a directory or a file).
        /// </summary>
        /// <param name="path">Normalized path.</param>
        /// <returns>True when it does.</returns>
        public bool RootExists(string path)
        {
            int index = path.IndexOf('/');
            string first = index < 0 ? path : path.Substring(0, index);
            return Directories.Contains(first) || Files.Contains(first);
        }

        #endregion
    }
}
