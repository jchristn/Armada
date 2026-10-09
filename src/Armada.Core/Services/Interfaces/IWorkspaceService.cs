namespace Armada.Core.Services.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Service for safe workspace browsing and editing inside a vessel's checkout. The overloads that take a
    /// <see cref="Vessel"/> work in its working directory on the Admiral host; the overloads that take a
    /// <see cref="VesselHost"/> work wherever the checkout lives (the Admiral host or a Harbor).
    /// </summary>
    public interface IWorkspaceService
    {
        /// <summary>
        /// List one workspace directory.
        /// </summary>
        Task<WorkspaceTreeResult> GetTreeAsync(Vessel vessel, string? path = null, CancellationToken token = default);

        /// <summary>
        /// Read one workspace file.
        /// </summary>
        Task<WorkspaceFileResponse> GetFileAsync(Vessel vessel, string path, CancellationToken token = default);

        /// <summary>
        /// Save one workspace file.
        /// </summary>
        Task<WorkspaceSaveResult> SaveFileAsync(Vessel vessel, WorkspaceSaveRequest request, CancellationToken token = default);

        /// <summary>
        /// Create one workspace directory.
        /// </summary>
        Task<WorkspaceOperationResult> CreateDirectoryAsync(Vessel vessel, WorkspaceCreateDirectoryRequest request, CancellationToken token = default);

        /// <summary>
        /// Rename or move one workspace entry.
        /// </summary>
        Task<WorkspaceOperationResult> RenameAsync(Vessel vessel, WorkspaceRenameRequest request, CancellationToken token = default);

        /// <summary>
        /// Delete one workspace entry.
        /// </summary>
        Task<WorkspaceOperationResult> DeleteAsync(Vessel vessel, string path, CancellationToken token = default);

        /// <summary>
        /// Search text files in a workspace.
        /// </summary>
        Task<WorkspaceSearchResult> SearchAsync(Vessel vessel, string query, int maxResults = 200, CancellationToken token = default);

        /// <summary>
        /// Inspect the current working tree changes for a workspace.
        /// </summary>
        Task<WorkspaceChangesResult> GetChangesAsync(Vessel vessel, CancellationToken token = default);

        /// <summary>
        /// Get high-level workspace status for one vessel.
        /// </summary>
        Task<WorkspaceStatusResult> GetStatusAsync(
            Vessel vessel,
            IReadOnlyList<WorkspaceActiveMission>? activeMissions = null,
            CancellationToken token = default);

        /// <summary>
        /// Run a shell command inside a vessel's workspace root (the in-browser dock terminal),
        /// bounded by a timeout that kills the process tree.
        /// </summary>
        Task<WorkspaceExecResult> ExecAsync(Vessel vessel, WorkspaceExecRequest request, CancellationToken token = default);

        /// <summary>
        /// Get a unified git diff of the vessel working tree against HEAD, optionally scoped to one path.
        /// </summary>
        Task<WorkspaceDiffResult> GetDiffAsync(Vessel vessel, string? path = null, CancellationToken token = default);

        /// <summary>
        /// List one workspace directory in a checkout on its host.
        /// </summary>
        Task<WorkspaceTreeResult> GetTreeAsync(VesselHost host, string? path = null, CancellationToken token = default);

        /// <summary>
        /// Read one workspace file in a checkout on its host.
        /// </summary>
        Task<WorkspaceFileResponse> GetFileAsync(VesselHost host, string path, CancellationToken token = default);

        /// <summary>
        /// Save one workspace file in a checkout on its host.
        /// </summary>
        Task<WorkspaceSaveResult> SaveFileAsync(VesselHost host, WorkspaceSaveRequest request, CancellationToken token = default);

        /// <summary>
        /// Create one directory in a checkout on its host.
        /// </summary>
        Task<WorkspaceOperationResult> CreateDirectoryAsync(VesselHost host, WorkspaceCreateDirectoryRequest request, CancellationToken token = default);

        /// <summary>
        /// Rename or move one entry in a checkout on its host.
        /// </summary>
        Task<WorkspaceOperationResult> RenameAsync(VesselHost host, WorkspaceRenameRequest request, CancellationToken token = default);

        /// <summary>
        /// Delete one entry in a checkout on its host.
        /// </summary>
        Task<WorkspaceOperationResult> DeleteAsync(VesselHost host, string path, CancellationToken token = default);

        /// <summary>
        /// Search a checkout on its host.
        /// </summary>
        Task<WorkspaceSearchResult> SearchAsync(VesselHost host, string query, int maxResults = 200, CancellationToken token = default);

        /// <summary>
        /// Get git changes of a checkout on its host.
        /// </summary>
        Task<WorkspaceChangesResult> GetChangesAsync(VesselHost host, CancellationToken token = default);

        /// <summary>
        /// Get high-level workspace status of a checkout on its host.
        /// </summary>
        Task<WorkspaceStatusResult> GetStatusAsync(
            VesselHost host,
            IReadOnlyList<WorkspaceActiveMission>? activeMissions = null,
            CancellationToken token = default);

        /// <summary>
        /// Run a shell command in a checkout on its host, bounded by a timeout.
        /// </summary>
        Task<WorkspaceExecResult> ExecAsync(VesselHost host, WorkspaceExecRequest request, CancellationToken token = default);

        /// <summary>
        /// Get a unified git diff of a checkout on its host against HEAD, optionally scoped to one path.
        /// </summary>
        Task<WorkspaceDiffResult> GetDiffAsync(VesselHost host, string? path = null, CancellationToken token = default);
    }
}
