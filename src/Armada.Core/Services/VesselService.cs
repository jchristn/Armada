namespace Armada.Core.Services
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Shared vessel creation logic. Thread-safe: holds no mutable state beyond the database driver.
    /// </summary>
    public class VesselService : IVesselService
    {
        #region Private-Members

        private readonly DatabaseDriver _Database;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
        public VesselService(DatabaseDriver database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Vessel> CreateAsync(Vessel vessel, bool inferWorkingDirectoryFromLocalClone = false, CancellationToken token = default)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            if (String.IsNullOrEmpty(vessel.RepoUrl)) throw new ArgumentException("repoUrl is required when creating a vessel", nameof(vessel));

            // When the repo is a local clone (file:// URL or an existing local path) and no explicit working
            // directory was given, point the working directory at that clone so the vessel is immediately
            // usable (notably for Settings > Rebuild Armada). LocalPath is deliberately NOT set: LocalPath is
            // treated as an Armada-managed bare repo and is deleted on vessel removal, which must never wipe
            // the user's own working clone.
            if (inferWorkingDirectoryFromLocalClone
                && String.IsNullOrWhiteSpace(vessel.WorkingDirectory)
                && TryResolveLocalClonePath(vessel.RepoUrl, out string? localClone))
            {
                vessel.WorkingDirectory = localClone;
            }

            vessel.NormalizeGitHubTokenOverride();
            await DuplicateEntityGuard.EnsureVesselNameAvailableAsync(_Database, vessel, token).ConfigureAwait(false);
            return await _Database.Vessels.CreateAsync(vessel, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolve a repository URL that names a local git clone (a file:// URL or a rooted filesystem path) to its
        /// full local path. Only an existing directory that is a git working tree (with a .git directory or file)
        /// or a bare repository is accepted.
        /// </summary>
        /// <param name="repoUrl">Repository URL or path; may be null.</param>
        /// <param name="localPath">The resolved full path, or null when the URL is not a local git clone.</param>
        /// <returns>True when the URL resolved to a local git clone.</returns>
        public static bool TryResolveLocalClonePath(string? repoUrl, out string? localPath)
        {
            localPath = null;
            if (String.IsNullOrWhiteSpace(repoUrl)) return false;

            string candidate;
            if (repoUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                try { candidate = new Uri(repoUrl).LocalPath; }
                catch (UriFormatException) { return false; }
            }
            else if (Path.IsPathRooted(repoUrl) && (repoUrl.Contains(Path.DirectorySeparatorChar) || repoUrl.Contains('/')))
            {
                candidate = repoUrl;
            }
            else
            {
                return false;
            }

            if (String.IsNullOrWhiteSpace(candidate)) return false;
            try
            {
                candidate = Path.GetFullPath(candidate);
                if (!Directory.Exists(candidate)) return false;
                // Only accept an actual git repository (working tree with .git, or a bare repo directory).
                bool isGit = Directory.Exists(Path.Combine(candidate, ".git"))
                    || File.Exists(Path.Combine(candidate, ".git"))
                    || Directory.Exists(Path.Combine(candidate, "hooks")) && File.Exists(Path.Combine(candidate, "HEAD"));
                if (!isGit) return false;
                localPath = candidate;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException)
            {
                return false;
            }
        }

        #endregion
    }
}
