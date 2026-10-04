namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;

    /// <summary>
    /// Discovers git repositories on the Admiral host for vessel import. A directory is a repository when it holds a
    /// .git directory; a .git file marks a worktree or submodule, reported as Worktree. Scans are breadth-first to
    /// Import.MaxDepth, skip excluded and dot-prefixed names and symbolic links, never descend into a repository, and
    /// report Armada's own repos, docks, and data directories as ArmadaManaged. Every input path must lie inside an
    /// allowed root. Thread-safe: settings are read live and no per-call state is shared.
    /// </summary>
    public class VesselDiscoveryService : IVesselDiscoveryService
    {
        #region Public-Members

        /// <summary>
        /// Maximum candidates one discovery returns; discovery stops and reports truncation at this count.
        /// Default 5000, minimum 1, maximum 100000.
        /// </summary>
        public int MaxCandidates
        {
            get => _MaxCandidates;
            set => _MaxCandidates = Math.Clamp(value, 1, 100000);
        }

        /// <summary>
        /// Maximum directories and roots in one request. Default 1000, minimum 1, maximum 10000.
        /// </summary>
        public int MaxRequestPaths
        {
            get => _MaxRequestPaths;
            set => _MaxRequestPaths = Math.Clamp(value, 1, 10000);
        }

        /// <summary>
        /// Maximum concurrent git queries while resolving remotes and default branches. Default 8, minimum 1,
        /// maximum 64.
        /// </summary>
        public int GitConcurrency
        {
            get => _GitConcurrency;
            set => _GitConcurrency = Math.Clamp(value, 1, 64);
        }

        /// <summary>
        /// Timeout for each git query in milliseconds. Default 15000, minimum 1000, maximum 120000.
        /// </summary>
        public int GitTimeoutMs
        {
            get => _GitTimeoutMs;
            set => _GitTimeoutMs = Math.Clamp(value, 1000, 120000);
        }

        /// <summary>
        /// Maximum entries one browse returns. Default 5000, minimum 1, maximum 100000.
        /// </summary>
        public int MaxBrowseEntries
        {
            get => _MaxBrowseEntries;
            set => _MaxBrowseEntries = Math.Clamp(value, 1, 100000);
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly IHostCommandExecutor _Executor;
        private int _MaxCandidates = 5000;
        private int _MaxRequestPaths = 1000;
        private int _GitConcurrency = 8;
        private int _GitTimeoutMs = 15000;
        private int _MaxBrowseEntries = 5000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver, used to read the tenant's vessels.</param>
        /// <param name="settings">Application settings; Import and the Armada directories are read live.</param>
        /// <param name="executor">Host command executor for git queries. Defaults to <see cref="LocalHostCommandExecutor"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when database or settings is null.</exception>
        public VesselDiscoveryService(DatabaseDriver database, ArmadaSettings settings, IHostCommandExecutor? executor = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Executor = executor ?? new LocalHostCommandExecutor();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<VesselDiscoveryResult> DiscoverAsync(string tenantId, VesselDiscoveryRequest request, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            List<string> inputs = ValidateAndNormalize(request);

            int maxDepth = request.MaxDepth ?? _Settings.Import.MaxDepth;
            HashSet<string> excluded = new HashSet<string>(_Settings.Import.ExcludedDirectoryNames, StringComparer.OrdinalIgnoreCase);
            List<string> managed = GetManagedDirectories();
            HashSet<string> managedSet = new HashSet<string>(managed, VesselImportPaths.PathComparer);

            VesselDiscoveryResult result = new VesselDiscoveryResult();
            HashSet<string> seen = new HashSet<string>(VesselImportPaths.PathComparer);
            int notFoundInputs = 0;

            foreach (string input in inputs)
            {
                token.ThrowIfCancellationRequested();
                if (result.Candidates.Count >= _MaxCandidates)
                {
                    result.Truncated = true;
                    break;
                }

                if (!Directory.Exists(input))
                {
                    VesselImportCandidateStatusEnum status = File.Exists(input) ? VesselImportCandidateStatusEnum.NotGit : VesselImportCandidateStatusEnum.NotFound;
                    if (status == VesselImportCandidateStatusEnum.NotFound) notFoundInputs++;
                    AddCandidate(result, seen, input, status);
                    continue;
                }

                if (managed.Any(m => VesselImportPaths.IsSameOrUnder(input, m)))
                {
                    AddCandidate(result, seen, input, VesselImportCandidateStatusEnum.ArmadaManaged);
                    continue;
                }

                VesselImportCandidateStatusEnum? self = ClassifyDirectory(input);
                if (self.HasValue)
                {
                    AddCandidate(result, seen, input, self.Value);
                    continue;
                }

                int before = result.Candidates.Count;
                bool accessible = Scan(input, maxDepth, excluded, managedSet, result, seen, token);
                if (!accessible)
                {
                    AddCandidate(result, seen, input, VesselImportCandidateStatusEnum.AccessDenied);
                }
                else if (result.Candidates.Count == before && !result.Truncated)
                {
                    AddCandidate(result, seen, input, VesselImportCandidateStatusEnum.NotGit);
                }
            }

            if (result.Truncated)
            {
                result.Hints.Add(new VesselImportHint(
                    VesselImportCodes.CandidateLimitReached,
                    "Discovery stopped after " + _MaxCandidates + " candidates. Narrow the roots or lower the depth to see the rest."));
            }

            if (inputs.Count > 0 && notFoundInputs == inputs.Count)
            {
                result.Hints.Add(new VesselImportHint(
                    VesselImportCodes.PathNotVisibleToAdmiral,
                    "None of the requested paths exist on the Admiral host. If the Admiral runs in a container, mount the directories into it or run discovery through a Harbor."));
            }

            await PopulateGitInfoAsync(result.Candidates, token).ConfigureAwait(false);

            List<Vessel> vessels = await _Database.Vessels.EnumerateAsync(tenantId, token).ConfigureAwait(false);
            VesselMatchIndex index = new VesselMatchIndex(vessels);
            Dictionary<string, string> namesById = vessels.ToDictionary(v => v.Id, v => v.Name, StringComparer.Ordinal);

            foreach (VesselImportCandidate candidate in result.Candidates)
            {
                bool isRepository = candidate.Status == VesselImportCandidateStatusEnum.New || candidate.Status == VesselImportCandidateStatusEnum.Worktree;
                if (isRepository)
                {
                    string? existing = index.Match(candidate.Path, candidate.RemoteUrl);
                    if (existing != null)
                    {
                        candidate.ExistingVesselId = existing;
                        if (candidate.Status == VesselImportCandidateStatusEnum.New) candidate.Status = VesselImportCandidateStatusEnum.AlreadyOnboarded;
                    }
                }

                if (candidate.ExistingVesselId != null && namesById.TryGetValue(candidate.ExistingVesselId, out string? existingName))
                    candidate.ProposedName = existingName;
                else if (isRepository)
                    candidate.ProposedName = index.ReserveName(VesselMatchIndex.FolderName(candidate.Path));
                else
                    candidate.ProposedName = VesselMatchIndex.FolderName(candidate.Path);
            }

            return result;
        }

        /// <inheritdoc />
        public void ValidateRequest(VesselDiscoveryRequest request)
        {
            ValidateAndNormalize(request);
        }

        /// <inheritdoc />
        public Task<VesselBrowseResult> BrowseAsync(string? path, CancellationToken token = default)
        {
            List<string> allowedRoots = GetAllowedRoots();
            HashSet<string> excluded = new HashSet<string>(_Settings.Import.ExcludedDirectoryNames, StringComparer.OrdinalIgnoreCase);
            VesselBrowseResult result = new VesselBrowseResult();

            if (String.IsNullOrWhiteSpace(path))
            {
                foreach (string root in allowedRoots)
                {
                    token.ThrowIfCancellationRequested();
                    if (!Directory.Exists(root)) continue;
                    result.Entries.Add(BuildEntry(root, root, excluded));
                }

                return Task.FromResult(result);
            }

            string normalized = VesselImportPaths.NormalizeInputPath(path);
            EnsureAllowed(normalized, allowedRoots);
            if (!Directory.Exists(normalized)) throw new DirectoryNotFoundException("Directory not found: " + normalized);

            result.Path = normalized;
            string? parent = Path.GetDirectoryName(normalized);
            if (!String.IsNullOrEmpty(parent) && allowedRoots.Any(r => VesselImportPaths.IsSameOrUnder(parent, r))) result.Parent = parent;

            DirectoryInfo directory = new DirectoryInfo(normalized);
            IEnumerable<DirectoryInfo> children = directory.EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, AttributesToSkip = 0 });
            List<DirectoryInfo> visible = new List<DirectoryInfo>();
            foreach (DirectoryInfo child in children)
            {
                token.ThrowIfCancellationRequested();
                if (!IsTraversable(child, excluded)) continue;
                visible.Add(child);
                if (visible.Count >= _MaxBrowseEntries) break;
            }

            foreach (DirectoryInfo child in visible.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                result.Entries.Add(BuildEntry(child.FullName, child.Name, excluded));
            }

            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public List<string> GetAllowedRoots()
        {
            List<string> configured = _Settings.Import.AllowedRoots.Where(r => !String.IsNullOrWhiteSpace(r)).ToList();
            if (configured.Count == 0)
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (String.IsNullOrWhiteSpace(home)) return new List<string>();
                configured = new List<string> { home };
            }

            List<string> roots = new List<string>();
            foreach (string root in configured)
            {
                try
                {
                    string normalized = VesselImportPaths.NormalizeInputPath(root);
                    if (!roots.Contains(normalized, VesselImportPaths.PathComparer)) roots.Add(normalized);
                }
                catch (ArgumentException)
                {
                    // A malformed or relative configured root is ignored rather than widening access.
                }
            }

            return roots;
        }

        #endregion

        #region Private-Methods

        private List<string> ValidateAndNormalize(VesselDiscoveryRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!String.IsNullOrWhiteSpace(request.HarborId))
                throw new NotSupportedException("Discovery on a Harbor is not supported yet; omit harborId to discover on the Admiral host.");

            List<string> rawPaths = request.Directories.Concat(request.Roots).Where(p => !String.IsNullOrWhiteSpace(p)).ToList();
            if (rawPaths.Count == 0) throw new ArgumentException("At least one directory or root is required.", nameof(request));
            if (rawPaths.Count > _MaxRequestPaths)
                throw new ArgumentException("Too many paths: " + rawPaths.Count + " (maximum " + _MaxRequestPaths + ").", nameof(request));

            List<string> inputs = VesselImportPaths.NormalizeDistinct(rawPaths);
            List<string> allowedRoots = GetAllowedRoots();
            foreach (string input in inputs) EnsureAllowed(input, allowedRoots);
            return inputs;
        }

        private static void EnsureAllowed(string normalized, List<string> allowedRoots)
        {
            if (allowedRoots.Any(r => VesselImportPaths.IsSameOrUnder(normalized, r))) return;
            throw new VesselImportPathNotAllowedException(
                "Path is outside the allowed import roots: " + normalized + ". Configure Import.AllowedRoots to permit it.",
                normalized);
        }

        private List<string> GetManagedDirectories()
        {
            List<string> managed = new List<string>();
            foreach (string? directory in new string?[] { _Settings.ReposDirectory, _Settings.DocksDirectory, _Settings.DataDirectory })
            {
                string? normalized = VesselImportPaths.NormalizeStoredPath(directory);
                if (normalized == null) continue;
                normalized = VesselImportPaths.ResolveOnDiskCasing(normalized);
                if (!managed.Contains(normalized, VesselImportPaths.PathComparer)) managed.Add(normalized);
            }

            return managed;
        }

        private static VesselImportCandidateStatusEnum? ClassifyDirectory(string directory)
        {
            string gitPath = Path.Combine(directory, ".git");
            if (Directory.Exists(gitPath)) return VesselImportCandidateStatusEnum.New;
            if (File.Exists(gitPath)) return VesselImportCandidateStatusEnum.Worktree;
            return null;
        }

        private static bool IsTraversable(DirectoryInfo directory, HashSet<string> excluded)
        {
            if (directory.Name.StartsWith(".", StringComparison.Ordinal)) return false;
            if (excluded.Contains(directory.Name)) return false;
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            return true;
        }

        private bool Scan(
            string root,
            int maxDepth,
            HashSet<string> excluded,
            HashSet<string> managedSet,
            VesselDiscoveryResult result,
            HashSet<string> seen,
            CancellationToken token)
        {
            EnumerationOptions options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, AttributesToSkip = 0 };
            Queue<VesselDiscoveryScanItem> queue = new Queue<VesselDiscoveryScanItem>();
            queue.Enqueue(new VesselDiscoveryScanItem(root, 0));
            bool rootAccessible = true;

            while (queue.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                VesselDiscoveryScanItem current = queue.Dequeue();

                List<DirectoryInfo> children;
                try
                {
                    children = new DirectoryInfo(current.Path).EnumerateDirectories("*", options).ToList();
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is System.Security.SecurityException)
                {
                    if (current.Depth == 0) rootAccessible = false;
                    continue;
                }

                foreach (DirectoryInfo child in children.OrderBy(c => c.Name, StringComparer.Ordinal))
                {
                    if (result.Candidates.Count >= _MaxCandidates)
                    {
                        result.Truncated = true;
                        return rootAccessible;
                    }

                    string childPath = Path.Combine(current.Path, child.Name);
                    if (managedSet.Contains(childPath))
                    {
                        AddCandidate(result, seen, childPath, VesselImportCandidateStatusEnum.ArmadaManaged);
                        continue;
                    }

                    if (!IsTraversable(child, excluded)) continue;

                    VesselImportCandidateStatusEnum? status = ClassifyDirectory(childPath);
                    if (status.HasValue)
                    {
                        AddCandidate(result, seen, childPath, status.Value);
                        continue;
                    }

                    if (current.Depth + 1 < maxDepth) queue.Enqueue(new VesselDiscoveryScanItem(childPath, current.Depth + 1));
                }
            }

            return rootAccessible;
        }

        private static void AddCandidate(VesselDiscoveryResult result, HashSet<string> seen, string path, VesselImportCandidateStatusEnum status)
        {
            if (!seen.Add(path)) return;
            VesselImportCandidate candidate = new VesselImportCandidate();
            candidate.Path = path;
            candidate.Status = status;
            result.Candidates.Add(candidate);
        }

        private VesselBrowseEntry BuildEntry(string fullPath, string name, HashSet<string> excluded)
        {
            VesselBrowseEntry entry = new VesselBrowseEntry();
            entry.Name = name;
            entry.Path = fullPath;
            VesselImportCandidateStatusEnum? status = ClassifyDirectory(fullPath);
            entry.IsGitRepository = status == VesselImportCandidateStatusEnum.New;
            entry.IsWorktree = status == VesselImportCandidateStatusEnum.Worktree;
            try
            {
                entry.HasSubdirectories = new DirectoryInfo(fullPath)
                    .EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, AttributesToSkip = 0 })
                    .Any(d => IsTraversable(d, excluded));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is System.Security.SecurityException)
            {
                entry.HasSubdirectories = false;
            }

            return entry;
        }

        private async Task PopulateGitInfoAsync(List<VesselImportCandidate> candidates, CancellationToken token)
        {
            List<VesselImportCandidate> repositories = candidates
                .Where(c => c.Status == VesselImportCandidateStatusEnum.New || c.Status == VesselImportCandidateStatusEnum.Worktree)
                .ToList();
            if (repositories.Count == 0) return;

            ParallelOptions options = new ParallelOptions { MaxDegreeOfParallelism = _GitConcurrency, CancellationToken = token };
            await Parallel.ForEachAsync(repositories, options, async (VesselImportCandidate candidate, CancellationToken ct) =>
            {
                candidate.RemoteUrl = await RunGitLineAsync(candidate.Path, ct, "remote", "get-url", "origin").ConfigureAwait(false);

                string? originHead = await RunGitLineAsync(candidate.Path, ct, "symbolic-ref", "--short", "refs/remotes/origin/HEAD").ConfigureAwait(false);
                if (!String.IsNullOrEmpty(originHead))
                {
                    candidate.DefaultBranch = originHead.StartsWith("origin/", StringComparison.Ordinal) ? originHead.Substring("origin/".Length) : originHead;
                }
                else
                {
                    string? current = await RunGitLineAsync(candidate.Path, ct, "symbolic-ref", "--short", "HEAD").ConfigureAwait(false);
                    candidate.DefaultBranch = String.IsNullOrEmpty(current) ? "main" : current;
                }
            }).ConfigureAwait(false);
        }

        private async Task<string?> RunGitLineAsync(string workingDirectory, CancellationToken token, params string[] arguments)
        {
            HostCommandRequest request = new HostCommandRequest();
            request.Executable = "git";
            request.WorkingDirectory = workingDirectory;
            request.Arguments = new List<string>(arguments);
            request.TimeoutMs = _GitTimeoutMs;

            try
            {
                HostCommandResult result = await _Executor.RunAsync(request, token).ConfigureAwait(false);
                if (!result.Success) return null;
                string line = result.StandardOutput.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? String.Empty;
                return line.Length == 0 ? null : line;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is IOException)
            {
                return null;
            }
        }

        #endregion
    }
}
