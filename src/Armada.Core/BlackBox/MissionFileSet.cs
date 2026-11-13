namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The files and directories a mission is likely to touch, compared case-insensitively: repository-relative files,
    /// bare file names (a scoped-files entry without a directory matches that name anywhere), and directories (a
    /// directory overlaps every file under it). Bounded by <see cref="BlackBoxInsightLimits.MaxComparedFiles"/>.
    /// </summary>
    public class MissionFileSet
    {
        #region Public-Members

        /// <summary>Repository-relative files.</summary>
        public HashSet<string> Files { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Bare file names (no directory).</summary>
        public HashSet<string> BareNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Directories (no trailing slash).</summary>
        public HashSet<string> Directories { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Entries, files first then directories (with a trailing slash), then bare names.</summary>
        public IEnumerable<string> Entries => Files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Concat(Directories.OrderBy(d => d, StringComparer.OrdinalIgnoreCase).Select(d => d + "/"))
            .Concat(BareNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

        /// <summary>Total entries.</summary>
        public int Count => Files.Count + BareNames.Count + Directories.Count;

        /// <summary>True when nothing is known.</summary>
        public bool IsEmpty => Count == 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public MissionFileSet()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Normalize a path: trim, forward slashes, no leading ./ or /, no trailing slash.
        /// </summary>
        /// <param name="path">Path, or null.</param>
        /// <returns>The normalized path (empty when nothing is left).</returns>
        public static string Normalize(string? path)
        {
            string p = (path ?? String.Empty).Trim().Trim('`', '"', '\'').Replace('\\', '/');
            while (p.StartsWith("./", StringComparison.Ordinal)) p = p.Substring(2);
            p = p.TrimStart('/').TrimEnd('/', '.', ',', ';', ':', ')');
            return p;
        }

        /// <summary>
        /// The parent directory of a path (empty for the repository root).
        /// </summary>
        /// <param name="path">Normalized path.</param>
        /// <returns>The directory.</returns>
        public static string DirectoryOf(string path)
        {
            int index = (path ?? String.Empty).LastIndexOf('/');
            return index < 0 ? String.Empty : path!.Substring(0, index);
        }

        /// <summary>
        /// Add a file (or a bare file name when it has no directory).
        /// </summary>
        /// <param name="path">Path.</param>
        public void AddFile(string? path)
        {
            string p = Normalize(path);
            if (p.Length == 0 || Count >= BlackBoxInsightLimits.MaxComparedFiles) return;
            if (p.IndexOf('/') < 0) BareNames.Add(p);
            else Files.Add(p);
        }

        /// <summary>
        /// Add a directory.
        /// </summary>
        /// <param name="path">Directory.</param>
        public void AddDirectory(string? path)
        {
            string p = Normalize(path);
            if (p.Length == 0 || Count >= BlackBoxInsightLimits.MaxComparedFiles) return;
            Directories.Add(p);
        }

        /// <summary>
        /// The entries of this set that overlap another set, in a stable order.
        /// </summary>
        /// <param name="other">Other set.</param>
        /// <returns>The overlapping entries of this set (files as paths, directories with a trailing slash).</returns>
        /// <exception cref="ArgumentNullException">Thrown when other is null.</exception>
        public List<string> OverlapWith(MissionFileSet other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            List<string> result = new List<string>();
            foreach (string file in Files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (other.Files.Contains(file) || other.BareNames.Contains(NameOf(file)) || other.Directories.Any(d => IsUnder(file, d))) result.Add(file);
            }
            foreach (string dir in Directories.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                if (other.Directories.Any(d => IsUnder(dir, d) || IsUnder(d, dir) || String.Equals(d, dir, StringComparison.OrdinalIgnoreCase))
                    || other.Files.Any(f => IsUnder(f, dir)))
                    result.Add(dir + "/");
            }
            foreach (string name in BareNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                if (other.BareNames.Contains(name) || other.Files.Any(f => String.Equals(NameOf(f), name, StringComparison.OrdinalIgnoreCase))) result.Add(name);
            }
            return result;
        }

        /// <summary>
        /// The entries of this set inside a directory (the directory itself, files under it, or a named directory that
        /// contains it).
        /// </summary>
        /// <param name="directory">Directory (normalized; empty is the repository root).</param>
        /// <returns>The entries.</returns>
        public List<string> EntriesIn(string directory)
        {
            string d = Normalize(directory);
            List<string> result = new List<string>();
            foreach (string file in Files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (String.Equals(DirectoryOf(file), d, StringComparison.OrdinalIgnoreCase)) result.Add(file);
            }
            foreach (string dir in Directories.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (String.Equals(dir, d, StringComparison.OrdinalIgnoreCase) || (d.Length > 0 && IsUnder(d, dir))) result.Add(dir + "/");
            }
            return result;
        }

        /// <summary>
        /// The parent directories of the files of this set.
        /// </summary>
        /// <returns>Distinct directories.</returns>
        public HashSet<string> FileDirectories()
        {
            HashSet<string> dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in Files) dirs.Add(DirectoryOf(f));
            return dirs;
        }

        #endregion

        #region Private-Methods

        private static bool IsUnder(string path, string directory)
        {
            if (directory.Length == 0) return true;
            return path.Length > directory.Length && path.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string NameOf(string path)
        {
            int index = path.LastIndexOf('/');
            return index < 0 ? path : path.Substring(index + 1);
        }

        #endregion
    }
}
