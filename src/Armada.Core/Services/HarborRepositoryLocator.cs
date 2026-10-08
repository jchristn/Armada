namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Harbor;

    /// <summary>
    /// Finds the repository a Harbor serves a vessel from, on the Harbor's own machine: the checkout named for the vessel
    /// in the Harbor's settings, else a checkout under one of its root folders whose remote names the vessel's repository
    /// (https and ssh forms, a .git suffix, and case do not matter), else the Harbor's own bare clone of the vessel's URL.
    /// Remote URLs read from discovered checkouts are cached until the checkout's git config changes.
    /// </summary>
    public class HarborRepositoryLocator
    {
        #region Public-Members

        /// <summary>
        /// How many folder levels below a root folder are searched for checkouts (a root itself is level 0).
        /// </summary>
        public const int MaxDiscoveryDepth = 3;

        #endregion

        #region Private-Members

        private static readonly HashSet<string> _SkippedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", "bin", "obj", "target", "build", "dist", "vendor", "packages"
        };

        private readonly IHostCommandExecutor _Commands;
        private readonly Dictionary<string, CachedRemotes> _RemoteCache = new Dictionary<string, CachedRemotes>(StringComparer.Ordinal);
        private readonly object _CacheLock = new object();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="commands">Executor for local git commands on the Harbor host.</param>
        public HarborRepositoryLocator(IHostCommandExecutor commands)
        {
            _Commands = commands ?? throw new ArgumentNullException(nameof(commands));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Find the repository for a vessel.
        /// </summary>
        /// <param name="settings">The Harbor's repository and dock settings.</param>
        /// <param name="vesselId">Vessel identifier.</param>
        /// <param name="vesselName">Vessel name.</param>
        /// <param name="repoUrl">The vessel's repository URL, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The location; Source None (with a message) when the Harbor cannot serve the vessel.</returns>
        public async Task<HarborRepositoryLocation> LocateAsync(HarborDockSettings settings, string vesselId, string vesselName, string? repoUrl, CancellationToken token = default)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            string vesselLabel = String.IsNullOrWhiteSpace(vesselName) ? vesselId : vesselName;

            HarborRepositoryMapping? mapping = FindMapping(settings, vesselId, vesselName);
            if (mapping != null)
            {
                string mapped = mapping.Path.Trim();
                if (!await IsCheckoutAsync(mapped, token).ConfigureAwait(false))
                {
                    return new HarborRepositoryLocation
                    {
                        Message = "its settings map vessel " + vesselLabel + " to " + mapped + ", which is not a git checkout;"
                            + " fix the path in Harbor > Settings > Repositories"
                    };
                }

                return new HarborRepositoryLocation { Source = HarborRepositorySourceEnum.Mapped, RepositoryPath = mapped, CheckoutPath = mapped };
            }

            string? wanted = GitRemoteUrl.Normalize(repoUrl);
            if (wanted != null)
            {
                string? discovered = await DiscoverAsync(settings, wanted, token).ConfigureAwait(false);
                if (discovered != null)
                    return new HarborRepositoryLocation { Source = HarborRepositorySourceEnum.Discovered, RepositoryPath = discovered, CheckoutPath = discovered };

                return new HarborRepositoryLocation
                {
                    Source = HarborRepositorySourceEnum.Clone,
                    RepositoryPath = Path.Combine(settings.ReposDirectory, SafeName(vesselLabel) + ".git")
                };
            }

            return new HarborRepositoryLocation
            {
                Message = "it has no checkout of vessel " + vesselLabel + ", and the vessel has no repository URL to clone;"
                    + " set the checkout's path in Harbor > Settings > Repositories or add a root folder that contains it"
            };
        }

        /// <summary>
        /// Make a vessel or dock name safe to use as one folder name.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>The name with path separators and characters invalid in file names replaced by underscores.</returns>
        public static string SafeName(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) return "_";
            char[] chars = name.Trim().ToCharArray();
            HashSet<char> invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            invalid.Add('/');
            invalid.Add('\\');
            invalid.Add(':');
            for (int i = 0; i < chars.Length; i++)
                if (invalid.Contains(chars[i]) || Char.IsControl(chars[i])) chars[i] = '_';
            string safe = new string(chars);
            if (safe == "." || safe == "..") safe = "_";
            return safe;
        }

        #endregion

        #region Private-Methods

        private static HarborRepositoryMapping? FindMapping(HarborDockSettings settings, string vesselId, string vesselName)
        {
            foreach (HarborRepositoryMapping mapping in settings.Repositories ?? new List<HarborRepositoryMapping>())
            {
                if (mapping == null || String.IsNullOrWhiteSpace(mapping.Vessel) || String.IsNullOrWhiteSpace(mapping.Path)) continue;
                string key = mapping.Vessel.Trim();
                if (String.Equals(key, vesselId, StringComparison.OrdinalIgnoreCase) || String.Equals(key, vesselName, StringComparison.OrdinalIgnoreCase))
                    return mapping;
            }

            return null;
        }

        private async Task<bool> IsCheckoutAsync(string path, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(path) || !Directory.Exists(path) || !Directory.Exists(Path.Combine(path, ".git"))) return false;
            HostCommandResult result = await RunGitAsync(path, token, "rev-parse", "--is-bare-repository").ConfigureAwait(false);
            return result.Success && String.Equals(result.StandardOutput.Trim(), "false", StringComparison.Ordinal);
        }

        /// <summary>
        /// Find a checkout under the root folders with a remote that names the wanted repository. When several match,
        /// one whose origin matches wins, then the shortest path, then the ordinal-first path, so the choice is stable.
        /// </summary>
        private async Task<string?> DiscoverAsync(HarborDockSettings settings, string wanted, CancellationToken token)
        {
            List<string> candidates = new List<string>();
            foreach (string root in settings.RepositoryRoots ?? new List<string>())
            {
                if (String.IsNullOrWhiteSpace(root)) continue;
                string trimmed = root.Trim();
                if (!Directory.Exists(trimmed)) continue;
                CollectCheckouts(trimmed, 0, settings, candidates);
            }

            string? best = null;
            bool bestIsOrigin = false;
            foreach (string candidate in candidates)
            {
                token.ThrowIfCancellationRequested();
                List<GitRemote> remotes = await ReadRemotesAsync(candidate, token).ConfigureAwait(false);
                bool matches = false;
                bool originMatches = false;
                foreach (GitRemote remote in remotes)
                {
                    if (!String.Equals(GitRemoteUrl.Normalize(remote.Url), wanted, StringComparison.Ordinal)) continue;
                    matches = true;
                    if (String.Equals(remote.Name, "origin", StringComparison.Ordinal)) originMatches = true;
                }

                if (!matches) continue;
                if (best == null
                    || (originMatches && !bestIsOrigin)
                    || (originMatches == bestIsOrigin && (candidate.Length < best.Length || (candidate.Length == best.Length && String.CompareOrdinal(candidate, best) < 0))))
                {
                    best = candidate;
                    bestIsOrigin = originMatches;
                }
            }

            return best;
        }

        private static void CollectCheckouts(string directory, int depth, HarborDockSettings settings, List<string> found)
        {
            // The Harbor's own docks and clones are never the user's checkout.
            if (!String.IsNullOrWhiteSpace(settings.DocksDirectory) && HarborDockSettings.IsSameOrUnder(directory, settings.DocksDirectory)) return;
            if (!String.IsNullOrWhiteSpace(settings.ReposDirectory) && HarborDockSettings.IsSameOrUnder(directory, settings.ReposDirectory)) return;

            // A checkout has a .git folder; a .git file marks a linked worktree or a submodule, which is not the user's checkout.
            if (Directory.Exists(Path.Combine(directory, ".git")))
            {
                found.Add(Path.GetFullPath(directory));
                return;
            }

            if (depth >= MaxDiscoveryDepth) return;

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(directory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                return;
            }

            List<string> ordered = new List<string>(children);
            ordered.Sort(StringComparer.Ordinal);
            foreach (string child in ordered)
            {
                string name = Path.GetFileName(child);
                if (name.StartsWith(".", StringComparison.Ordinal) || _SkippedFolders.Contains(name)) continue;
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
                {
                    continue;
                }

                CollectCheckouts(child, depth + 1, settings, found);
            }
        }

        private async Task<List<GitRemote>> ReadRemotesAsync(string checkout, CancellationToken token)
        {
            string configPath = Path.Combine(checkout, ".git", "config");
            DateTime stamp = File.Exists(configPath) ? File.GetLastWriteTimeUtc(configPath) : DateTime.MinValue;
            lock (_CacheLock)
            {
                if (_RemoteCache.TryGetValue(checkout, out CachedRemotes? cached) && cached.ConfigWriteUtc == stamp)
                    return cached.Remotes;
            }

            // --null: each entry is "key\nvalue\0", so values are read structurally rather than split on spaces.
            HostCommandResult result = await RunGitAsync(checkout, token, "config", "--null", "--get-regexp", "^remote\\..*\\.url$").ConfigureAwait(false);
            List<GitRemote> remotes = new List<GitRemote>();
            if (result.Success)
            {
                foreach (string entry in result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries))
                {
                    int newline = entry.IndexOf('\n');
                    if (newline <= 0) continue;
                    string key = entry.Substring(0, newline).Trim();
                    string url = entry.Substring(newline + 1).Trim();
                    const string prefix = "remote.";
                    const string suffix = ".url";
                    if (!key.StartsWith(prefix, StringComparison.Ordinal) || !key.EndsWith(suffix, StringComparison.Ordinal) || key.Length <= prefix.Length + suffix.Length) continue;
                    remotes.Add(new GitRemote(key.Substring(prefix.Length, key.Length - prefix.Length - suffix.Length), url));
                }
            }

            lock (_CacheLock)
            {
                _RemoteCache[checkout] = new CachedRemotes(stamp, remotes);
            }

            return remotes;
        }

        private async Task<HostCommandResult> RunGitAsync(string workingDirectory, CancellationToken token, params string[] arguments)
        {
            return await _Commands.RunAsync(new HostCommandRequest
            {
                Executable = "git",
                WorkingDirectory = workingDirectory,
                Arguments = new List<string>(arguments),
                TimeoutMs = 30000
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Types

        private sealed class GitRemote
        {
            public string Name { get; }

            public string Url { get; }

            public GitRemote(string name, string url)
            {
                Name = name;
                Url = url;
            }
        }

        private sealed class CachedRemotes
        {
            public DateTime ConfigWriteUtc { get; }

            public List<GitRemote> Remotes { get; }

            public CachedRemotes(DateTime configWriteUtc, List<GitRemote> remotes)
            {
                ConfigWriteUtc = configWriteUtc;
                Remotes = remotes;
            }
        }

        #endregion
    }
}
