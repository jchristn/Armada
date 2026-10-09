namespace Armada.Core.Services
{
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// File operations in a vessel's checkout on the host that has it: the Admiral's disk for a working directory on the
    /// Admiral, or a Harbor's disk (over its link) for a checkout on a Harbor. Every path is relative to the checkout,
    /// and the Workspace rules apply (no absolute paths, no way out of the checkout, no symbolic links). Failures are
    /// reported with the same exception types on both hosts: <see cref="System.UnauthorizedAccessException"/> for a refused
    /// path, <see cref="System.IO.FileNotFoundException"/> or <see cref="System.IO.DirectoryNotFoundException"/> for a
    /// missing one, <see cref="WorkspaceConflictException"/> for a stale save, and
    /// <see cref="System.InvalidOperationException"/> for a request that does not fit the path or a Harbor that cannot
    /// answer.
    /// </summary>
    public interface IVesselCheckoutFiles
    {
        /// <summary>
        /// List one directory with Workspace rules.
        /// </summary>
        /// <param name="path">Relative directory path, or null for the checkout root.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The listing.</returns>
        Task<WorkspaceTreeResult> GetTreeAsync(string? path, CancellationToken token = default);

        /// <summary>
        /// Read one file for Workspace.
        /// </summary>
        /// <param name="path">Relative file path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The file.</returns>
        Task<WorkspaceFileResponse> GetFileAsync(string path, CancellationToken token = default);

        /// <summary>
        /// Save one text file with optimistic concurrency.
        /// </summary>
        /// <param name="request">Save request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        Task<WorkspaceSaveResult> SaveFileAsync(WorkspaceSaveRequest request, CancellationToken token = default);

        /// <summary>
        /// Create one directory.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        Task<WorkspaceOperationResult> CreateDirectoryAsync(WorkspaceCreateDirectoryRequest request, CancellationToken token = default);

        /// <summary>
        /// Rename or move one entry.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        Task<WorkspaceOperationResult> RenameAsync(WorkspaceRenameRequest request, CancellationToken token = default);

        /// <summary>
        /// Delete one entry.
        /// </summary>
        /// <param name="path">Relative path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        Task<WorkspaceOperationResult> DeleteAsync(string path, CancellationToken token = default);

        /// <summary>
        /// Search the checkout's text files.
        /// </summary>
        /// <param name="query">Text to find.</param>
        /// <param name="maxResults">Largest number of matches.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The matches.</returns>
        Task<WorkspaceSearchResult> SearchAsync(string query, int maxResults, CancellationToken token = default);

        /// <summary>
        /// Stat a path (build output folders included; .git is refused).
        /// </summary>
        /// <param name="path">Relative path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What was found.</returns>
        Task<VesselCheckoutFileInfo> StatAsync(string path, CancellationToken token = default);

        /// <summary>
        /// Read a text file (build output folders included; .git is refused).
        /// </summary>
        /// <param name="path">Relative path.</param>
        /// <param name="maxBytes">Largest file read; a larger one returns null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The text, or null when the file does not exist or is larger than <paramref name="maxBytes"/>.</returns>
        Task<string?> ReadTextAsync(string path, long maxBytes, CancellationToken token = default);
    }
}
