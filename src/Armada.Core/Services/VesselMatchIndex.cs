namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Armada.Core.Models;

    /// <summary>
    /// In-memory index of a tenant's vessels for import: finds an existing vessel by working directory or normalized
    /// repository URL, and hands out vessel names that are unique within the tenant. Not thread-safe; use one
    /// instance per discovery or import pass.
    /// </summary>
    public class VesselMatchIndex
    {
        #region Private-Members

        private readonly Dictionary<string, string> _ByWorkingDirectory = new Dictionary<string, string>(VesselImportPaths.PathComparer);
        private readonly Dictionary<string, string> _ByRepoUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Build an index over the supplied vessels.
        /// </summary>
        /// <param name="vessels">Vessels in the tenant; null is treated as empty.</param>
        public VesselMatchIndex(IEnumerable<Vessel>? vessels)
        {
            if (vessels == null) return;
            foreach (Vessel vessel in vessels) Add(vessel);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a vessel (for example one just created) to the index.
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <exception cref="ArgumentNullException">Thrown when vessel is null.</exception>
        public void Add(Vessel vessel)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            if (!String.IsNullOrEmpty(vessel.Name)) _Names.Add(vessel.Name);

            string? workingDirectory = VesselImportPaths.NormalizeStoredPath(vessel.WorkingDirectory);
            if (workingDirectory != null) _ByWorkingDirectory.TryAdd(workingDirectory, vessel.Id);

            string? repoKey = VesselImportPaths.NormalizeRepoUrl(vessel.RepoUrl);
            if (repoKey != null) _ByRepoUrl.TryAdd(repoKey, vessel.Id);
        }

        /// <summary>
        /// Find an existing vessel for a candidate by working directory, then by normalized remote URL. A candidate
        /// without a remote is also matched against vessels whose RepoUrl is the candidate's local path.
        /// </summary>
        /// <param name="path">Normalized candidate path.</param>
        /// <param name="remoteUrl">Candidate origin URL, or null.</param>
        /// <returns>The matching vessel identifier, or null.</returns>
        public string? Match(string path, string? remoteUrl)
        {
            if (!String.IsNullOrEmpty(path) && _ByWorkingDirectory.TryGetValue(path, out string? byDirectory)) return byDirectory;

            string? repoKey = VesselImportPaths.NormalizeRepoUrl(String.IsNullOrWhiteSpace(remoteUrl) ? path : remoteUrl);
            if (repoKey != null && _ByRepoUrl.TryGetValue(repoKey, out string? byUrl)) return byUrl;
            return null;
        }

        /// <summary>
        /// Return a name unique within the tenant and this pass, and reserve it. The base name is used as-is when
        /// free; otherwise -2, -3, and so on are appended.
        /// </summary>
        /// <param name="baseName">Preferred name, typically the folder name. Empty values become "repo".</param>
        /// <returns>The reserved unique name.</returns>
        public string ReserveName(string? baseName)
        {
            string name = String.IsNullOrWhiteSpace(baseName) ? "repo" : baseName.Trim();
            if (_Names.Add(name)) return name;

            for (int suffix = 2; ; suffix++)
            {
                string candidate = name + "-" + suffix;
                if (_Names.Add(candidate)) return candidate;
            }
        }

        /// <summary>
        /// Whether a name is already taken in the tenant or reserved in this pass.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>True when taken.</returns>
        public bool IsNameTaken(string name)
        {
            return !String.IsNullOrEmpty(name) && _Names.Contains(name);
        }

        /// <summary>
        /// Folder name used as the base vessel name for a path.
        /// </summary>
        /// <param name="path">Directory path.</param>
        /// <returns>The last path segment, or "repo" for a root.</returns>
        public static string FolderName(string path)
        {
            if (String.IsNullOrEmpty(path)) return "repo";
            string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return String.IsNullOrWhiteSpace(name) ? "repo" : name;
        }

        #endregion
    }
}
