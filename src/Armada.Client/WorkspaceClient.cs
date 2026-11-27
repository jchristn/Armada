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
    public sealed class WorkspaceClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal WorkspaceClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>getWorkspaceStatus</c>: GET `/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/status`.
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getWorkspaceStatus")]
        public Task<WorkspaceStatusResult?> GetStatusAsync(string vesselId, CancellationToken token = default)
        {
            return _Transport.GetAsync<WorkspaceStatusResult>($"/api/v1/workspace/vessels/{ArmadaQueryString.Escape(vesselId)}/status", null, token);
        }

        /// <summary>
        /// Dashboard <c>getWorkspaceTree</c>: GET /api/v1/workspace/vessels/{vesselId}/tree. One page of one directory
        /// (directories first, then files); PageNumber, PageSize, TotalPages, and TotalRecords describe the rest. Use
        /// <see cref="ListDirectoryAsync"/> to read every entry.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="path">Directory, or null for the root.</param>
        /// <param name="page">Paging (page size 1 to 1000); null is the server default (first page of 10).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getWorkspaceTree")]
        public Task<WorkspaceTreeResult?> GetTreeAsync(string vesselId, string? path, EnumerationQuery? page, CancellationToken token = default)
        {
            List<string> parts = new List<string>();
            if (!String.IsNullOrEmpty(path)) parts.Add("path=" + ArmadaQueryString.EscapePath(path));
            if (page != null)
            {
                parts.Add("pageNumber=" + page.PageNumber);
                parts.Add("pageSize=" + page.PageSize);
            }

            return _Transport.GetAsync<WorkspaceTreeResult>($"/api/v1/workspace/vessels/{ArmadaQueryString.Escape(vesselId)}/tree" + (parts.Count == 0 ? "" : "?" + String.Join("&", parts)), null, token);
        }

        /// <summary>
        /// Read every entry of one workspace directory by walking its pages (1000 per page) explicitly.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="path">Directory, or null for the root.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Every entry, directories first, then files.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listWorkspaceDirectory")]
        public async Task<List<WorkspaceTreeEntry>> ListDirectoryAsync(string vesselId, string? path = null, CancellationToken token = default)
        {
            List<WorkspaceTreeEntry> entries = new List<WorkspaceTreeEntry>();
            int pageNumber = 1;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                WorkspaceTreeResult? tree = await GetTreeAsync(vesselId, path, new EnumerationQuery { PageNumber = pageNumber, PageSize = 1000 }, token).ConfigureAwait(false);
                if (tree == null || tree.Entries == null || tree.Entries.Count == 0) break;
                entries.AddRange(tree.Entries);
                if (pageNumber >= tree.TotalPages) break;
                pageNumber++;
            }

            return entries;
        }

        /// <summary>
        /// Dashboard <c>getWorkspaceFile</c>: GET `/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/file?path=${encodeWorkspaceQueryPath(path)}`.
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="path">path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getWorkspaceFile")]
        public Task<WorkspaceFileResponse?> GetFileAsync(string vesselId, string path, CancellationToken token = default)
        {
            return _Transport.GetAsync<WorkspaceFileResponse>($"/api/v1/workspace/vessels/{ArmadaQueryString.Escape(vesselId)}/file?path={ArmadaQueryString.EscapePath(path)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>saveWorkspaceFile</c>: PUT `/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/file`.
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("saveWorkspaceFile")]
        public Task<WorkspaceSaveResult?> SaveFileAsync(string vesselId, WorkspaceSaveRequest data, CancellationToken token = default)
        {
            return _Transport.PutAsync<WorkspaceSaveResult>($"/api/v1/workspace/vessels/{ArmadaQueryString.Escape(vesselId)}/file", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>createWorkspaceDirectory</c>: POST `/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/directory`.
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createWorkspaceDirectory")]
        public Task<WorkspaceOperationResult?> CreateDirectoryAsync(string vesselId, WorkspaceCreateDirectoryRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<WorkspaceOperationResult>($"/api/v1/workspace/vessels/{ArmadaQueryString.Escape(vesselId)}/directory", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>renameWorkspaceEntry</c>: POST `/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/rename`.
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("renameWorkspaceEntry")]
        public Task<WorkspaceOperationResult?> RenameEntryAsync(string vesselId, WorkspaceRenameRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<WorkspaceOperationResult>($"/api/v1/workspace/vessels/{ArmadaQueryString.Escape(vesselId)}/rename", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteWorkspaceEntry</c>: DEL `/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/entry?path=${encodeWorkspaceQueryPath(path)}`.
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="path">path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteWorkspaceEntry")]
        public Task<WorkspaceOperationResult?> DeleteEntryAsync(string vesselId, string path, CancellationToken token = default)
        {
            return _Transport.DeleteAsync<WorkspaceOperationResult>($"/api/v1/workspace/vessels/{ArmadaQueryString.Escape(vesselId)}/entry?path={ArmadaQueryString.EscapePath(path)}", null, token);
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
        [DashboardExport("searchWorkspace")]
        public Task<WorkspaceSearchResult?> SearchAsync(string vesselId, string query, int maxResults = 200, CancellationToken token = default)
        {
            return _Transport.GetAsync<WorkspaceSearchResult>($"/api/v1/workspace/vessels/{ArmadaQueryString.Escape(vesselId)}/search?q={ArmadaQueryString.Escape(query)}&maxResults={maxResults}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getWorkspaceChanges</c>: GET `/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/changes`.
        /// Branch state and one page of changed files; TotalRecords counts every changed file.
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="page">Paging (page size 1 to 1000); null is the server default (first page of 10).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getWorkspaceChanges")]
        public Task<WorkspaceChangesResult?> GetChangesAsync(string vesselId, EnumerationQuery? page = null, CancellationToken token = default)
        {
            string query = page == null ? "" : "?pageNumber=" + page.PageNumber + "&pageSize=" + page.PageSize;
            return _Transport.GetAsync<WorkspaceChangesResult>($"/api/v1/workspace/vessels/{ArmadaQueryString.Escape(vesselId)}/changes" + query, null, token);
        }

        /// <summary>
        /// Dashboard <c>listAllWorkspaceChanges</c>: every changed file, read in explicit pages of 1000 (at most 100
        /// pages); the branch fields come from the first page.
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The changes, or null.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listAllWorkspaceChanges")]
        public async Task<WorkspaceChangesResult?> ListAllChangesAsync(string vesselId, CancellationToken token = default)
        {
            WorkspaceChangesResult? first = await GetChangesAsync(vesselId, new EnumerationQuery { PageNumber = 1, PageSize = 1000 }, token).ConfigureAwait(false);
            if (first == null) return null;
            int pages = Math.Min(first.TotalPages, 100);
            for (int pageNumber = 2; pageNumber <= pages; pageNumber++)
            {
                token.ThrowIfCancellationRequested();
                WorkspaceChangesResult? next = await GetChangesAsync(vesselId, new EnumerationQuery { PageNumber = pageNumber, PageSize = 1000 }, token).ConfigureAwait(false);
                if (next == null || next.Changes.Count == 0) break;
                first.Changes.AddRange(next.Changes);
            }

            return first;
        }

        /// <summary>
        /// Dashboard <c>listAllWorkspaceActiveMissions</c>: every active mission on the vessel, read in explicit pages of
        /// 1000 (at most 100 pages).
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The missions.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listAllWorkspaceActiveMissions")]
        public async Task<List<WorkspaceActiveMission>> ListAllActiveMissionsAsync(string vesselId, CancellationToken token = default)
        {
            List<WorkspaceActiveMission> all = new List<WorkspaceActiveMission>();
            for (int pageNumber = 1; pageNumber <= 100; pageNumber++)
            {
                token.ThrowIfCancellationRequested();
                EnumerationResult<WorkspaceActiveMission>? result = await ListActiveMissionsAsync(vesselId, new EnumerationQuery { PageNumber = pageNumber, PageSize = 1000 }, token).ConfigureAwait(false);
                if (result == null || result.Objects == null || result.Objects.Count == 0) break;
                all.AddRange(result.Objects);
                if (pageNumber >= result.TotalPages) break;
            }

            return all;
        }

        /// <summary>
        /// Dashboard <c>listWorkspaceActiveMissions</c>: POST `/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/active-missions/enumerate`.
        /// One page of the vessel's active missions with their scoped files (the status read carries only ActiveMissionCount).
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="query">Paging (page size 1 to 1000); null is the first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listWorkspaceActiveMissions")]
        public Task<EnumerationResult<WorkspaceActiveMission>?> ListActiveMissionsAsync(string vesselId, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<WorkspaceActiveMission>>($"/api/v1/workspace/vessels/{ArmadaQueryString.Escape(vesselId)}/active-missions/enumerate", query ?? new EnumerationQuery(), null, token);
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
        [DashboardExport("execWorkspaceCommand")]
        public Task<WorkspaceExecResult?> ExecCommandAsync(string vesselId, string command, int? timeoutSeconds = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<WorkspaceExecResult>($"/api/v1/workspace/vessels/{ArmadaQueryString.Escape(vesselId)}/exec", new { Command = command, TimeoutSeconds = timeoutSeconds }, null, token);
        }

        /// <summary>
        /// Dashboard <c>openWorkspaceTerminal</c>: POST /api/v1/workspace/vessels/{vesselId}/open-terminal. Opens a terminal
        /// window at the vessel's checkout on the computer that holds it (its Harbor, or the Admiral when called from the
        /// Admiral's own computer); the result says whether it opened, where, and why not.
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("openWorkspaceTerminal")]
        public Task<WorkspaceOpenTerminalResult?> OpenTerminalAsync(string vesselId, CancellationToken token = default)
        {
            return _Transport.PostAsync<WorkspaceOpenTerminalResult>($"/api/v1/workspace/vessels/{ArmadaQueryString.Escape(vesselId)}/open-terminal", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>getWorkspaceDiff</c>: GET /api/v1/workspace/vessels/{vesselId}/diff.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="path">File, or null for every change.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getWorkspaceDiff")]
        public Task<WorkspaceDiffResult?> GetDiffAsync(string vesselId, string? path = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<WorkspaceDiffResult>($"/api/v1/workspace/vessels/{ArmadaQueryString.Escape(vesselId)}/diff" + (String.IsNullOrEmpty(path) ? "" : "?path=" + ArmadaQueryString.EscapePath(path)), null, token);
        }

        #endregion
    }
}
