namespace Armada.Core.Services.Health
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    /// <summary>
    /// A bounded list of repository-relative file paths used by the vessel health criteria (manifest hashing,
    /// dependency target discovery, test detection, CI and license detection, language detection). Built either by
    /// scanning a working directory on disk or from the files tracked at HEAD of a bare repository. Directories named
    /// in the import exclude list, and directories whose name starts with a dot (except .github), are skipped.
    /// Paths always use forward slashes. Not thread-safe for concurrent construction; immutable afterwards.
    /// </summary>
    public class RepositoryFileInventory
    {
        #region Public-Members

        /// <summary>
        /// Repository-relative file paths with forward slashes. Never null.
        /// </summary>
        public IReadOnlyList<string> Files => _Files;

        /// <summary>
        /// Absolute root directory on disk when the inventory was scanned from a working directory; null when it was
        /// built from a bare repository's tracked files (file contents are then unavailable).
        /// </summary>
        public string? RootPath { get; private set; } = null;

        /// <summary>
        /// True when the scan stopped early because it reached the file cap.
        /// </summary>
        public bool Truncated { get; private set; } = false;

        /// <summary>
        /// Maximum number of files collected. Default 50000.
        /// </summary>
        public static int MaxFiles { get; set; } = 50000;

        /// <summary>
        /// Maximum directory depth scanned below the root. Default 10.
        /// </summary>
        public static int MaxDepth { get; set; } = 10;

        #endregion

        #region Private-Members

        private readonly List<string> _Files = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty inventory.
        /// </summary>
        public RepositoryFileInventory()
        {
        }

        /// <summary>
        /// Scan a working directory on disk.
        /// </summary>
        /// <param name="rootPath">Directory to scan.</param>
        /// <param name="excludedDirectoryNames">Directory names to skip (case-insensitive). Null means none.</param>
        /// <returns>The inventory.</returns>
        /// <exception cref="ArgumentNullException">Thrown when rootPath is null or empty.</exception>
        public static RepositoryFileInventory FromDirectory(string rootPath, IEnumerable<string>? excludedDirectoryNames)
        {
            if (String.IsNullOrEmpty(rootPath)) throw new ArgumentNullException(nameof(rootPath));
            RepositoryFileInventory inventory = new RepositoryFileInventory();
            inventory.RootPath = Path.GetFullPath(rootPath);
            HashSet<string> excluded = BuildExcludedSet(excludedDirectoryNames);
            inventory.ScanDirectory(inventory.RootPath, String.Empty, 0, excluded);
            inventory._Files.Sort(StringComparer.Ordinal);
            return inventory;
        }

        /// <summary>
        /// Build an inventory from a list of tracked repository-relative paths (for example from git ls-tree).
        /// </summary>
        /// <param name="trackedFiles">Tracked paths.</param>
        /// <param name="excludedDirectoryNames">Directory names to skip (case-insensitive). Null means none.</param>
        /// <returns>The inventory.</returns>
        /// <exception cref="ArgumentNullException">Thrown when trackedFiles is null.</exception>
        public static RepositoryFileInventory FromTrackedFiles(IEnumerable<string> trackedFiles, IEnumerable<string>? excludedDirectoryNames)
        {
            if (trackedFiles == null) throw new ArgumentNullException(nameof(trackedFiles));
            RepositoryFileInventory inventory = new RepositoryFileInventory();
            HashSet<string> excluded = BuildExcludedSet(excludedDirectoryNames);
            foreach (string raw in trackedFiles)
            {
                if (String.IsNullOrWhiteSpace(raw)) continue;
                string path = raw.Replace('\\', '/').Trim('/');
                string[] segments = path.Split('/');
                bool skip = false;
                for (int i = 0; i < segments.Length - 1; i++)
                {
                    if (IsExcludedDirectory(segments[i], excluded)) { skip = true; break; }
                }

                if (skip) continue;
                if (inventory._Files.Count >= MaxFiles) { inventory.Truncated = true; break; }
                inventory._Files.Add(path);
            }

            inventory._Files.Sort(StringComparer.Ordinal);
            return inventory;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Find files whose file name equals the given name (case-insensitive).
        /// </summary>
        /// <param name="fileName">File name, for example "package.json".</param>
        /// <returns>Matching relative paths.</returns>
        public List<string> FindByFileName(string fileName)
        {
            if (String.IsNullOrEmpty(fileName)) return new List<string>();
            return _Files.Where(f => String.Equals(GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>
        /// Find files with the given extension (case-insensitive, including the dot, for example ".csproj").
        /// </summary>
        /// <param name="extension">Extension including the dot.</param>
        /// <returns>Matching relative paths.</returns>
        public List<string> FindByExtension(string extension)
        {
            if (String.IsNullOrEmpty(extension)) return new List<string>();
            return _Files.Where(f => f.EndsWith(extension, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>
        /// Whether any file lives under a directory with the given name at any depth (case-insensitive).
        /// </summary>
        /// <param name="directoryName">Directory name, for example "tests".</param>
        /// <returns>True when such a directory contains at least one file.</returns>
        public bool HasDirectoryNamed(string directoryName)
        {
            if (String.IsNullOrEmpty(directoryName)) return false;
            foreach (string file in _Files)
            {
                string[] segments = file.Split('/');
                for (int i = 0; i < segments.Length - 1; i++)
                {
                    if (String.Equals(segments[i], directoryName, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Read a file's text from disk, up to a byte limit. Returns null when the inventory was not built from disk,
        /// the file cannot be read, or the path escapes the root.
        /// </summary>
        /// <param name="relativePath">Repository-relative path.</param>
        /// <param name="maxBytes">Maximum bytes to read. Default 1048576.</param>
        /// <returns>The text, or null.</returns>
        public string? ReadText(string relativePath, int maxBytes = 1048576)
        {
            if (RootPath == null || String.IsNullOrEmpty(relativePath)) return null;
            try
            {
                string full = Path.GetFullPath(Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!full.StartsWith(RootPath, StringComparison.Ordinal)) return null;
                FileInfo info = new FileInfo(full);
                if (!info.Exists) return null;
                using (FileStream stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(stream))
                {
                    if (info.Length <= maxBytes) return reader.ReadToEnd();
                    char[] buffer = new char[maxBytes];
                    int read = reader.Read(buffer, 0, buffer.Length);
                    return new string(buffer, 0, read);
                }
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Get the file name (last path segment) of a relative path.
        /// </summary>
        /// <param name="relativePath">Relative path with forward slashes.</param>
        /// <returns>The file name.</returns>
        public static string GetFileName(string relativePath)
        {
            if (String.IsNullOrEmpty(relativePath)) return String.Empty;
            int slash = relativePath.LastIndexOf('/');
            return slash < 0 ? relativePath : relativePath.Substring(slash + 1);
        }

        /// <summary>
        /// Get the directory part of a relative path ("" for a root-level file).
        /// </summary>
        /// <param name="relativePath">Relative path with forward slashes.</param>
        /// <returns>The directory part without a trailing slash.</returns>
        public static string GetDirectory(string relativePath)
        {
            if (String.IsNullOrEmpty(relativePath)) return String.Empty;
            int slash = relativePath.LastIndexOf('/');
            return slash < 0 ? String.Empty : relativePath.Substring(0, slash);
        }

        #endregion

        #region Private-Methods

        private static HashSet<string> BuildExcludedSet(IEnumerable<string>? names)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (names == null) return set;
            foreach (string name in names)
            {
                if (!String.IsNullOrWhiteSpace(name)) set.Add(name.Trim());
            }

            return set;
        }

        private static bool IsExcludedDirectory(string name, HashSet<string> excluded)
        {
            if (excluded.Contains(name)) return true;
            if (name.StartsWith(".", StringComparison.Ordinal) && !String.Equals(name, ".github", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void ScanDirectory(string absolute, string relative, int depth, HashSet<string> excluded)
        {
            if (Truncated) return;

            string[] files;
            string[] directories;
            try
            {
                files = Directory.GetFiles(absolute);
                directories = Directory.GetDirectories(absolute);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            Array.Sort(files, StringComparer.Ordinal);
            Array.Sort(directories, StringComparer.Ordinal);

            foreach (string file in files)
            {
                if (_Files.Count >= MaxFiles) { Truncated = true; return; }
                string name = Path.GetFileName(file);
                _Files.Add(relative.Length == 0 ? name : relative + "/" + name);
            }

            if (depth >= MaxDepth) return;

            foreach (string directory in directories)
            {
                string name = Path.GetFileName(directory);
                if (IsExcludedDirectory(name, excluded)) continue;
                try
                {
                    FileAttributes attributes = File.GetAttributes(directory);
                    if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) continue;
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                ScanDirectory(directory, relative.Length == 0 ? name : relative + "/" + name, depth + 1, excluded);
                if (Truncated) return;
            }
        }

        #endregion
    }
}
