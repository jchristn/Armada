namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Harbor;
    using SyslogLogging;

    /// <summary>
    /// The Harbor side of Harbor-hosted mission docks. Resolves a vessel to a repository on this machine (a checkout named
    /// in the Harbor's settings, a checkout discovered under its root folders, or the Harbor's own bare clone), creates
    /// mission docks as git worktrees of that repository under the Harbor's docks directory, removes them, and reads and
    /// writes files inside them for the Admiral.
    /// A dock made from the user's checkout never touches the checkout's working tree, index, or current branch: the
    /// checkout is only fetched (remote-tracking refs), and the mission branch and worktree are added to it.
    /// </summary>
    public class HarborDockManager
    {
        #region Private-Members

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _RepoLocks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        private readonly string _Header = "[HarborDockManager] ";
        private readonly Func<HarborDockSettings> _Settings;
        private readonly LoggingModule _Logging;
        private readonly IHostCommandExecutor _Commands;
        private readonly HarborRepositoryLocator _Locator;
        private readonly GitService _Git;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Supplies the current repository and dock settings (read on every request, so edits apply
        /// without reconnecting).</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="commands">Executor for local git commands; defaults to <see cref="LocalHostCommandExecutor"/>.</param>
        public HarborDockManager(Func<HarborDockSettings> settings, LoggingModule logging, IHostCommandExecutor? commands = null)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Commands = commands ?? new LocalHostCommandExecutor();
            _Locator = new HarborRepositoryLocator(_Commands);
            _Git = new GitService(_Logging);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Carry out a dock request. Never throws for a request it cannot satisfy: the result says why.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result to send back.</returns>
        public async Task<HarborDockResult> HandleAsync(HarborDockRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            HarborDockResult result;
            try
            {
                HarborDockSettings settings = CurrentSettings();
                switch (request.Operation)
                {
                    case HarborDockOperationEnum.Resolve:
                        result = await ResolveAsync(settings, request, token).ConfigureAwait(false);
                        break;
                    case HarborDockOperationEnum.Provision:
                        result = await ProvisionAsync(settings, request, token).ConfigureAwait(false);
                        break;
                    case HarborDockOperationEnum.Reclaim:
                        result = await ReclaimAsync(settings, request, token).ConfigureAwait(false);
                        break;
                    default:
                        result = new HarborDockResult { Message = "unknown dock operation " + request.Operation };
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + request.Operation + " failed for vessel " + request.VesselName + ": " + ex.ToString());
                result = new HarborDockResult { Message = ex.Message };
            }

            result.RequestId = request.RequestId;
            result.CorrelationId = request.CorrelationId;
            return result;
        }

        /// <summary>
        /// Carry out a file request inside the Harbor's docks directory. Never throws for a request it cannot satisfy.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result to send back.</returns>
        public async Task<HarborFileResult> HandleFileAsync(HarborFileRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            HarborFileResult result = new HarborFileResult { RequestId = request.RequestId, CorrelationId = request.CorrelationId };
            try
            {
                HarborDockSettings settings = CurrentSettings();
                string path = request.Path ?? String.Empty;
                if (!Path.IsPathFullyQualified(path) || !HarborDockSettings.IsSameOrUnder(path, settings.DocksDirectory))
                {
                    result.Message = "refused: " + path + " is not inside the Harbor's docks folder (" + settings.DocksDirectory + ")";
                    return result;
                }

                switch (request.Operation)
                {
                    case HarborFileOperationEnum.Stat:
                        result.IsDirectory = Directory.Exists(path);
                        result.Exists = result.IsDirectory || File.Exists(path);
                        result.Success = true;
                        break;
                    case HarborFileOperationEnum.Read:
                        result.Exists = File.Exists(path);
                        if (result.Exists) result.Content = await File.ReadAllTextAsync(path, token).ConfigureAwait(false);
                        result.Success = true;
                        break;
                    case HarborFileOperationEnum.Write:
                        string? directory = Path.GetDirectoryName(path);
                        if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                        await File.WriteAllTextAsync(path, request.Content ?? String.Empty, token).ConfigureAwait(false);
                        result.Exists = true;
                        result.Success = true;
                        break;
                    case HarborFileOperationEnum.AddGitExclude:
                        await AddGitExcludeAsync(path, request.Content ?? String.Empty, token).ConfigureAwait(false);
                        result.Success = true;
                        break;
                    default:
                        result.Message = "unknown file operation " + request.Operation;
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is ArgumentException)
            {
                result.Success = false;
                result.Message = ex.Message;
            }

            return result;
        }

        #endregion

        #region Private-Methods

        private HarborDockSettings CurrentSettings()
        {
            HarborDockSettings settings = _Settings() ?? throw new InvalidOperationException("it has no dock settings");
            if (String.IsNullOrWhiteSpace(settings.DocksDirectory) || !Path.IsPathFullyQualified(settings.DocksDirectory))
                throw new InvalidOperationException("its docks folder is not an absolute path; set it in Harbor > Settings > Repositories");
            if (String.IsNullOrWhiteSpace(settings.ReposDirectory) || !Path.IsPathFullyQualified(settings.ReposDirectory))
                throw new InvalidOperationException("its clones folder is not an absolute path; set it in Harbor > Settings > Repositories");
            return settings;
        }

        private async Task<HarborDockResult> ResolveAsync(HarborDockSettings settings, HarborDockRequest request, CancellationToken token)
        {
            HarborRepositoryLocation location = await _Locator.LocateAsync(settings, request.VesselId, request.VesselName, request.RepoUrl, token).ConfigureAwait(false);
            return new HarborDockResult
            {
                Success = location.Source != HarborRepositorySourceEnum.None,
                Message = location.Message,
                Source = location.Source,
                RepositoryPath = location.RepositoryPath,
                CheckoutPath = location.CheckoutPath
            };
        }

        private async Task<HarborDockResult> ProvisionAsync(HarborDockSettings settings, HarborDockRequest request, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(request.BranchName)) return new HarborDockResult { Message = "the provision request names no branch" };
            if (String.IsNullOrWhiteSpace(request.DockName)) return new HarborDockResult { Message = "the provision request names no dock" };

            HarborRepositoryLocation location = await _Locator.LocateAsync(settings, request.VesselId, request.VesselName, request.RepoUrl, token).ConfigureAwait(false);
            if (location.Source == HarborRepositorySourceEnum.None || String.IsNullOrEmpty(location.RepositoryPath))
                return new HarborDockResult { Message = location.Message };

            string vesselFolder = HarborRepositoryLocator.SafeName(String.IsNullOrWhiteSpace(request.VesselName) ? request.VesselId : request.VesselName);
            string worktreePath = Path.Combine(settings.DocksDirectory, vesselFolder, HarborRepositoryLocator.SafeName(request.DockName!));
            if (location.CheckoutPath != null && HarborDockSettings.IsSameOrUnder(worktreePath, location.CheckoutPath))
                return new HarborDockResult { Message = "its docks folder " + settings.DocksDirectory + " is inside the checkout " + location.CheckoutPath + "; choose a docks folder outside your code in Harbor > Settings > Repositories" };

            string repositoryPath = location.RepositoryPath!;
            SemaphoreSlim repoLock = _RepoLocks.GetOrAdd(Path.GetFullPath(repositoryPath), _ => new SemaphoreSlim(1, 1));
            await repoLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                string defaultBranch = String.IsNullOrWhiteSpace(request.DefaultBranch) ? "main" : request.DefaultBranch.Trim();
                string headCommit;
                if (location.Source == HarborRepositorySourceEnum.Clone)
                {
                    await EnsureCloneAsync(repositoryPath, request.RepoUrl!, settings, token).ConfigureAwait(false);
                    await RemoveStaleWorktreeAsync(repositoryPath, worktreePath, settings, token).ConfigureAwait(false);
                    Directory.CreateDirectory(Path.GetDirectoryName(worktreePath)!);
                    await _Git.CreateWorktreeAsync(repositoryPath, worktreePath, request.BranchName!, defaultBranch, token).ConfigureAwait(false);
                    headCommit = await _Git.GetHeadCommitHashAsync(worktreePath, token).ConfigureAwait(false) ?? String.Empty;
                }
                else
                {
                    headCommit = await AddWorktreeFromCheckoutAsync(repositoryPath, worktreePath, request.BranchName!, defaultBranch, settings, token).ConfigureAwait(false);
                }

                if (String.IsNullOrEmpty(headCommit))
                    return new HarborDockResult { Message = "the new dock " + worktreePath + " has no HEAD commit" };

                _Logging.Info(_Header + "provisioned dock " + worktreePath + " for vessel " + request.VesselName + " from " + location.Source + " repository " + repositoryPath);
                return new HarborDockResult
                {
                    Success = true,
                    Source = location.Source,
                    RepositoryPath = repositoryPath,
                    CheckoutPath = location.CheckoutPath,
                    WorktreePath = worktreePath,
                    HeadCommit = headCommit
                };
            }
            finally
            {
                repoLock.Release();
            }
        }

        /// <summary>
        /// Add a mission worktree to the user's checkout. Only refs are written to the checkout's repository (the fetched
        /// remote-tracking refs and the mission branch); its working tree, index, and HEAD are never touched, and no
        /// repository configuration is changed.
        /// </summary>
        private async Task<string> AddWorktreeFromCheckoutAsync(string checkout, string worktreePath, string branchName, string defaultBranch, HarborDockSettings settings, CancellationToken token)
        {
            // Bring remote-tracking refs up to date so a new branch starts from the remote's default branch. A checkout
            // without an origin, or offline, keeps its local state.
            if ((await RunGitAsync(checkout, token, "remote", "get-url", "origin").ConfigureAwait(false)).Success)
            {
                HostCommandResult fetch = await RunGitAsync(checkout, token, "fetch", "origin").ConfigureAwait(false);
                if (!fetch.Success) _Logging.Warn(_Header + "fetch failed in " + checkout + ", continuing with local state: " + fetch.StandardError.Trim());
            }

            await RemoveStaleWorktreeAsync(checkout, worktreePath, settings, token).ConfigureAwait(false);

            bool createdBranch = false;
            if (!await RefExistsAsync(checkout, "refs/heads/" + branchName, token).ConfigureAwait(false))
            {
                string? start = null;
                foreach (string candidate in new string[] { "refs/remotes/origin/" + branchName, "refs/remotes/origin/" + defaultBranch, "refs/heads/" + defaultBranch })
                {
                    if (await RefExistsAsync(checkout, candidate, token).ConfigureAwait(false))
                    {
                        start = candidate;
                        break;
                    }
                }

                if (start == null)
                    throw new InvalidOperationException("the checkout " + checkout + " has no branch " + defaultBranch + " (locally or on origin) to start mission branch " + branchName + " from");

                HostCommandResult commit = await RunGitAsync(checkout, token, "rev-parse", "--verify", start + "^{commit}").ConfigureAwait(false);
                if (!commit.Success) throw new InvalidOperationException("could not resolve " + start + " in " + checkout + ": " + commit.StandardError.Trim());

                // A commit (not a remote-tracking ref) as the start point and --no-track: no upstream configuration is written.
                await RequireGitAsync(checkout, token, "branch", "--no-track", branchName, commit.StandardOutput.Trim()).ConfigureAwait(false);
                createdBranch = true;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(worktreePath)!);
                await RequireGitAsync(checkout, token, "worktree", "add", worktreePath, branchName).ConfigureAwait(false);
                HostCommandResult head = await RunGitAsync(worktreePath, token, "rev-parse", "HEAD").ConfigureAwait(false);
                if (!head.Success) throw new InvalidOperationException("the new dock " + worktreePath + " has no HEAD: " + head.StandardError.Trim());
                return head.StandardOutput.Trim();
            }
            catch
            {
                await RunGitAsync(checkout, CancellationToken.None, "worktree", "remove", "--force", worktreePath).ConfigureAwait(false);
                TryDeleteDirectory(worktreePath, settings);
                if (createdBranch) await RunGitAsync(checkout, CancellationToken.None, "branch", "-D", branchName).ConfigureAwait(false);
                throw;
            }
        }

        private async Task EnsureCloneAsync(string repositoryPath, string repoUrl, HarborDockSettings settings, CancellationToken token)
        {
            if (Directory.Exists(repositoryPath))
            {
                if (await _Git.IsBareRepositoryAsync(repositoryPath, token).ConfigureAwait(false))
                {
                    HostCommandResult origin = await RunGitAsync(repositoryPath, token, "remote", "get-url", "origin").ConfigureAwait(false);
                    if (origin.Success && !GitRemoteUrl.SameRepository(origin.StandardOutput.Trim(), repoUrl))
                        throw new InvalidOperationException("its clone " + repositoryPath + " is of " + origin.StandardOutput.Trim() + ", not " + repoUrl
                            + "; remove that folder or map the vessel to a checkout in Harbor > Settings > Repositories");

                    try
                    {
                        await _Git.FetchAsync(repositoryPath, token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is GitCommandException || ex is TimeoutException)
                    {
                        _Logging.Warn(_Header + "fetch failed for " + repositoryPath + ", continuing with local state: " + ex.Message);
                    }

                    return;
                }

                // A leftover from a failed clone: only ever inside the Harbor's own repositories directory.
                if (!HarborDockSettings.IsSameOrUnder(repositoryPath, settings.ReposDirectory))
                    throw new InvalidOperationException(repositoryPath + " exists but is not a git repository");
                _Logging.Warn(_Header + "removing incomplete clone " + repositoryPath);
                Directory.Delete(repositoryPath, true);
            }

            Directory.CreateDirectory(settings.ReposDirectory);
            try
            {
                await _Git.CloneBareAsync(repoUrl, repositoryPath, token).ConfigureAwait(false);
            }
            catch (GitCommandException ex)
            {
                TryDeleteDirectory(repositoryPath, settings);
                throw new InvalidOperationException("it could not clone " + repoUrl + " (" + ex.StandardError.Trim() + ");"
                    + " check this machine's git credentials for it, or map the vessel to an existing checkout in Harbor > Settings > Repositories", ex);
            }
        }

        private async Task RemoveStaleWorktreeAsync(string repositoryPath, string worktreePath, HarborDockSettings settings, CancellationToken token)
        {
            if (Directory.Exists(worktreePath))
            {
                _Logging.Debug(_Header + "removing stale dock " + worktreePath);
                await RunGitAsync(repositoryPath, token, "worktree", "remove", "--force", worktreePath).ConfigureAwait(false);
                TryDeleteDirectory(worktreePath, settings);
            }

            // Forget registrations of worktrees whose folders are gone, so their branches can be checked out again.
            await RunGitAsync(repositoryPath, token, "worktree", "prune").ConfigureAwait(false);
        }

        private async Task<HarborDockResult> ReclaimAsync(HarborDockSettings settings, HarborDockRequest request, CancellationToken token)
        {
            string worktreePath = request.WorktreePath ?? String.Empty;
            if (!Path.IsPathFullyQualified(worktreePath) || !HarborDockSettings.IsSameOrUnder(worktreePath, settings.DocksDirectory))
                return new HarborDockResult { Message = "refused: " + worktreePath + " is not inside the Harbor's docks folder (" + settings.DocksDirectory + ")" };

            string? repositoryPath = String.IsNullOrWhiteSpace(request.RepositoryPath) ? null : request.RepositoryPath;
            if (repositoryPath == null && Directory.Exists(worktreePath))
            {
                HostCommandResult common = await RunGitAsync(worktreePath, token, "rev-parse", "--git-common-dir").ConfigureAwait(false);
                if (common.Success) repositoryPath = ResolveAgainst(worktreePath, common.StandardOutput.Trim());
            }

            if (repositoryPath != null && Directory.Exists(repositoryPath))
            {
                HostCommandResult remove = await RunGitAsync(repositoryPath, token, "worktree", "remove", "--force", worktreePath).ConfigureAwait(false);
                if (!remove.Success && Directory.Exists(worktreePath))
                    _Logging.Debug(_Header + "git worktree remove failed for " + worktreePath + ": " + remove.StandardError.Trim());
            }

            TryDeleteDirectory(worktreePath, settings);
            if (repositoryPath != null && Directory.Exists(repositoryPath))
                await RunGitAsync(repositoryPath, token, "worktree", "prune").ConfigureAwait(false);

            if (Directory.Exists(worktreePath))
                return new HarborDockResult { Message = "could not remove " + worktreePath + " (a file in it may still be open)", WorktreePath = worktreePath };

            _Logging.Info(_Header + "reclaimed dock " + worktreePath);
            return new HarborDockResult { Success = true, WorktreePath = worktreePath, RepositoryPath = repositoryPath };
        }

        private async Task AddGitExcludeAsync(string worktreePath, string entry, CancellationToken token)
        {
            string line = (entry ?? String.Empty).Trim();
            if (line.Length == 0) throw new ArgumentException("the exclude entry is empty");
            if (!Directory.Exists(worktreePath)) throw new InvalidOperationException(worktreePath + " does not exist");

            HostCommandResult common = await RunGitAsync(worktreePath, token, "rev-parse", "--git-common-dir").ConfigureAwait(false);
            if (!common.Success) throw new InvalidOperationException(worktreePath + " is not a git worktree: " + common.StandardError.Trim());

            string excludePath = Path.Combine(ResolveAgainst(worktreePath, common.StandardOutput.Trim()), "info", "exclude");
            await GitExcludeFile.AddEntryAsync(excludePath, line, token).ConfigureAwait(false);
        }

        private static string ResolveAgainst(string directory, string path)
        {
            return Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(directory, path));
        }

        private async Task<bool> RefExistsAsync(string repository, string fullRef, CancellationToken token)
        {
            HostCommandResult result = await RunGitAsync(repository, token, "show-ref", "--verify", "--quiet", fullRef).ConfigureAwait(false);
            return result.Success;
        }

        private async Task RequireGitAsync(string workingDirectory, CancellationToken token, params string[] arguments)
        {
            HostCommandResult result = await RunGitAsync(workingDirectory, token, arguments).ConfigureAwait(false);
            if (!result.Success)
                throw new GitCommandException("git", new List<string>(arguments), result.ExitCode, result.StandardOutput, result.StandardError);
        }

        private async Task<HostCommandResult> RunGitAsync(string workingDirectory, CancellationToken token, params string[] arguments)
        {
            return await _Commands.RunAsync(new HostCommandRequest
            {
                Executable = "git",
                WorkingDirectory = workingDirectory,
                Arguments = new List<string>(arguments),
                TimeoutMs = 120000
            }, token).ConfigureAwait(false);
        }

        private void TryDeleteDirectory(string path, HarborDockSettings settings)
        {
            // Only the Harbor's own folders are ever deleted.
            if (!HarborDockSettings.IsSameOrUnder(path, settings.DocksDirectory) && !HarborDockSettings.IsSameOrUnder(path, settings.ReposDirectory)) return;
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _Logging.Warn(_Header + "could not delete " + path + ": " + ex.Message);
            }
        }

        #endregion
    }
}
