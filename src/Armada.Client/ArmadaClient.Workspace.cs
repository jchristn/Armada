namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Http;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Workspace API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>getWorkspaceStatus</c>: GET `/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/status`.
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<WorkspaceStatusResult?> GetWorkspaceStatusAsync(string vesselId, CancellationToken token = default)
        {
            return GetAsync<WorkspaceStatusResult>($"/api/v1/workspace/vessels/{E(vesselId)}/status", null, token);
        }

        /// <summary>
        /// Dashboard <c>getWorkspaceTree</c>: GET /api/v1/workspace/vessels/{vesselId}/tree.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="path">Directory, or null for the root.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<WorkspaceTreeResult?> GetWorkspaceTreeAsync(string vesselId, string? path = null, CancellationToken token = default)
        {
            return GetAsync<WorkspaceTreeResult>($"/api/v1/workspace/vessels/{E(vesselId)}/tree" + (String.IsNullOrEmpty(path) ? "" : "?path=" + ArmadaQueryString.EscapePath(path)), null, token);
        }

        /// <summary>
        /// Dashboard <c>getWorkspaceFile</c>: GET `/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/file?path=${encodeWorkspaceQueryPath(path)}`.
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="path">path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<WorkspaceFileResponse?> GetWorkspaceFileAsync(string vesselId, string path, CancellationToken token = default)
        {
            return GetAsync<WorkspaceFileResponse>($"/api/v1/workspace/vessels/{E(vesselId)}/file?path={ArmadaQueryString.EscapePath(path)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>saveWorkspaceFile</c>: PUT `/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/file`.
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<WorkspaceSaveResult?> SaveWorkspaceFileAsync(string vesselId, WorkspaceSaveRequest data, CancellationToken token = default)
        {
            return PutAsync<WorkspaceSaveResult>($"/api/v1/workspace/vessels/{E(vesselId)}/file", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>createWorkspaceDirectory</c>: POST `/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/directory`.
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<WorkspaceOperationResult?> CreateWorkspaceDirectoryAsync(string vesselId, WorkspaceCreateDirectoryRequest data, CancellationToken token = default)
        {
            return PostAsync<WorkspaceOperationResult>($"/api/v1/workspace/vessels/{E(vesselId)}/directory", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>renameWorkspaceEntry</c>: POST `/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/rename`.
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<WorkspaceOperationResult?> RenameWorkspaceEntryAsync(string vesselId, WorkspaceRenameRequest data, CancellationToken token = default)
        {
            return PostAsync<WorkspaceOperationResult>($"/api/v1/workspace/vessels/{E(vesselId)}/rename", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteWorkspaceEntry</c>: DEL `/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/entry?path=${encodeWorkspaceQueryPath(path)}`.
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="path">path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<WorkspaceOperationResult?> DeleteWorkspaceEntryAsync(string vesselId, string path, CancellationToken token = default)
        {
            return DeleteAsync<WorkspaceOperationResult>($"/api/v1/workspace/vessels/{E(vesselId)}/entry?path={ArmadaQueryString.EscapePath(path)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>searchWorkspace</c>: GET `/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/search?q=${encodeURIComponent(query)}&amp;maxResults=${maxResults}`.
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="query">query.</param>
        /// <param name="maxResults">maxResults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<WorkspaceSearchResult?> SearchWorkspaceAsync(string vesselId, string query, int maxResults = 200, CancellationToken token = default)
        {
            return GetAsync<WorkspaceSearchResult>($"/api/v1/workspace/vessels/{E(vesselId)}/search?q={E(query)}&maxResults={maxResults}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getWorkspaceChanges</c>: GET `/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/changes`.
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<WorkspaceChangesResult?> GetWorkspaceChangesAsync(string vesselId, CancellationToken token = default)
        {
            return GetAsync<WorkspaceChangesResult>($"/api/v1/workspace/vessels/{E(vesselId)}/changes", null, token);
        }

        /// <summary>
        /// Dashboard <c>execWorkspaceCommand</c>: POST `/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/exec`.
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="command">command.</param>
        /// <param name="timeoutSeconds">timeoutSeconds.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<WorkspaceExecResult?> ExecWorkspaceCommandAsync(string vesselId, string command, int? timeoutSeconds = null, CancellationToken token = default)
        {
            return PostAsync<WorkspaceExecResult>($"/api/v1/workspace/vessels/{E(vesselId)}/exec", new { Command = command, TimeoutSeconds = timeoutSeconds }, null, token);
        }

        /// <summary>
        /// Dashboard <c>getWorkspaceDiff</c>: GET /api/v1/workspace/vessels/{vesselId}/diff.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="path">File, or null for every change.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<WorkspaceDiffResult?> GetWorkspaceDiffAsync(string vesselId, string? path = null, CancellationToken token = default)
        {
            return GetAsync<WorkspaceDiffResult>($"/api/v1/workspace/vessels/{E(vesselId)}/diff" + (String.IsNullOrEmpty(path) ? "" : "?path=" + ArmadaQueryString.EscapePath(path)), null, token);
        }

        #endregion
    }
}
