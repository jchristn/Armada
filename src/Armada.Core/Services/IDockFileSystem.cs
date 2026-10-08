namespace Armada.Core.Services
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// File operations against a dock on the host that has it: the Admiral's own disk for a dock on the Admiral, or the
    /// Harbor's disk (over its link) for a Harbor-hosted dock. Paths are paths on that host.
    /// </summary>
    public interface IDockFileSystem
    {
        /// <summary>
        /// Whether a directory exists.
        /// </summary>
        /// <param name="path">Directory path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when it exists.</returns>
        Task<bool> DirectoryExistsAsync(string path, CancellationToken token = default);

        /// <summary>
        /// Read a text file.
        /// </summary>
        /// <param name="path">File path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The content, or null when the file does not exist.</returns>
        Task<string?> ReadTextAsync(string path, CancellationToken token = default);

        /// <summary>
        /// Write a text file, creating its directory when needed.
        /// </summary>
        /// <param name="path">File path.</param>
        /// <param name="content">Content.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task WriteTextAsync(string path, string content, CancellationToken token = default);

        /// <summary>
        /// Add an ignore pattern to the git exclude file of the repository a worktree belongs to, unless it is there.
        /// </summary>
        /// <param name="worktreePath">Worktree path.</param>
        /// <param name="entry">Ignore pattern.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>False when the path is not a git checkout or worktree (nothing was written).</returns>
        Task<bool> AddGitExcludeEntryAsync(string worktreePath, string entry, CancellationToken token = default);
    }
}
