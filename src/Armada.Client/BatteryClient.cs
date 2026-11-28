namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Http;
    using Armada.Core.Models;

    /// <summary>
    /// Fixture battery calls: list the packs, run packs against a captain and read a run
    /// (<c>/api/v1/battery</c>).
    /// </summary>
    public sealed class BatteryClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal BatteryClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        private static string Escape(string value) => Uri.EscapeDataString(value);

        #region Public-Methods

        /// <summary>GetPackAsync: server-wide pack management; writes require a global admin.</summary>
        [DashboardExport("getBatteryPack")]
        public Task<BatteryPackDetail?> GetPackAsync(string packId, CancellationToken token = default) =>
            _Transport.GetAsync<BatteryPackDetail>($"/api/v1/battery/packs/{Escape(packId)}", null, token);

        /// <summary>ReadPackFileAsync: server-wide pack management; writes require a global admin.</summary>
        [DashboardExport("readBatteryPackFile")]
        public Task<BatteryPackTextFile?> ReadPackFileAsync(string packId, string path, CancellationToken token = default) =>
            _Transport.GetAsync<BatteryPackTextFile>($"/api/v1/battery/packs/{Escape(packId)}/files?path={Escape(path)}", null, token);

        /// <summary>CreatePackAsync: server-wide pack management; writes require a global admin.</summary>
        [DashboardExport("createBatteryPack")]
        public Task<BatteryPackDetail?> CreatePackAsync(BatteryPackEditRequest request, CancellationToken token = default) =>
            _Transport.PostAsync<BatteryPackDetail>($"/api/v1/battery/packs", request, null, token);

        /// <summary>UpdatePackManifestAsync: server-wide pack management; writes require a global admin.</summary>
        [DashboardExport("updateBatteryPackManifest")]
        public Task<BatteryPackDetail?> UpdatePackManifestAsync(string packId, string manifestJson, CancellationToken token = default) =>
            _Transport.PutAsync<BatteryPackDetail>($"/api/v1/battery/packs/{Escape(packId)}/manifest", new BatteryPackEditRequest { ManifestJson = manifestJson }, null, token);

        /// <summary>WritePackFileAsync: server-wide pack management; writes require a global admin.</summary>
        [DashboardExport("writeBatteryPackFile")]
        public Task<BatteryPackDetail?> WritePackFileAsync(string packId, string path, string content, CancellationToken token = default) =>
            _Transport.PutAsync<BatteryPackDetail>($"/api/v1/battery/packs/{Escape(packId)}/files?path={Escape(path)}", new BatteryPackTextFile { Content = content }, null, token);

        /// <summary>DeletePackFileAsync: server-wide pack management; writes require a global admin.</summary>
        [DashboardExport("deleteBatteryPackFile")]
        public Task<BatteryPackDetail?> DeletePackFileAsync(string packId, string path, CancellationToken token = default) =>
            _Transport.DeleteAsync<BatteryPackDetail>($"/api/v1/battery/packs/{Escape(packId)}/files?path={Escape(path)}", null, token);

        /// <summary>DeletePackAsync: server-wide pack management; writes require a global admin.</summary>
        [DashboardExport("deleteBatteryPack")]
        public Task<object?> DeletePackAsync(string packId, CancellationToken token = default) =>
            _Transport.DeleteAsync<object>($"/api/v1/battery/packs/{Escape(packId)}", null, token);

        /// <summary>ValidatePackAsync: server-wide pack management; writes require a global admin.</summary>
        [DashboardExport("validateBatteryPack")]
        public Task<BatteryPackInfo?> ValidatePackAsync(string packId, CancellationToken token = default) =>
            _Transport.PostAsync<BatteryPackInfo>($"/api/v1/battery/packs/{Escape(packId)}/validate", null, null, token);


        /// <summary>
        /// GET /api/v1/battery/packs: one page of battery packs, with validation errors for malformed ones.
        /// </summary>
        /// <param name="pageNumber">Page number, or null for 1.</param>
        /// <param name="pageSize">Page size, or null for 100.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of packs.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listBatteryPacks")]
        public Task<EnumerationResult<BatteryPackInfo>?> ListPacksAsync(int? pageNumber = null, int? pageSize = null, CancellationToken token = default)
        {
            List<string> parts = new List<string>();
            if (pageNumber.HasValue) parts.Add("pageNumber=" + pageNumber.Value.ToString(CultureInfo.InvariantCulture));
            if (pageSize.HasValue) parts.Add("pageSize=" + pageSize.Value.ToString(CultureInfo.InvariantCulture));
            string query = parts.Count == 0 ? String.Empty : "?" + String.Join("&", parts);
            return _Transport.GetAsync<EnumerationResult<BatteryPackInfo>>($"/api/v1/battery/packs{query}", null, token);
        }

        /// <summary>
        /// POST /api/v1/battery/packs/enumerate: one page of the fixture battery packs, with the total.
        /// </summary>
        /// <param name="query">Paging and filters, or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<BatteryPackInfo>?> EnumeratePacksAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<BatteryPackInfo>>("/api/v1/battery/packs/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// POST /api/v1/battery/runs: run packs against a captain. Returns the queued run at once. Global admin or tenant
        /// admin of the captain's tenant; 400 for no packs, an unknown or malformed pack, or a missing fixture vessel
        /// without CreateFixtureVessel.
        /// </summary>
        /// <param name="request">Packs, captain, run count, seed and whether to create fixture vessels.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The queued run.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<BatteryRun?> RunAsync(BatteryRunRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return _Transport.PostAsync<BatteryRun>($"/api/v1/battery/runs", request, null, token);
        }

        /// <summary>
        /// GET /api/v1/battery/runs/{id}: one run with its outcome counts. The per-task results are not embedded; page
        /// them with <see cref="EnumerateRunResultsAsync"/>.
        /// </summary>
        /// <param name="id">Run id (bat_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The run.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<BatteryRun?> GetRunAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<BatteryRun>($"/api/v1/battery/runs/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// GET /api/v1/battery/runs/{id}/results: one page of a run's per-task results, with the total.
        /// </summary>
        /// <param name="id">Run id (bat_ prefix).</param>
        /// <param name="pageNumber">Page number, or null for 1.</param>
        /// <param name="pageSize">Page size (1 to 1000), or null for 10.</param>
        /// <param name="status">Outcome filter (Passed, Failed, CouldNotRun, PendingEvaluation), or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of results, newest first.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<BatteryTaskResult>?> ListRunResultsAsync(string id, int? pageNumber = null, int? pageSize = null, string? status = null, CancellationToken token = default)
        {
            List<string> parts = new List<string>();
            if (pageNumber.HasValue) parts.Add("pageNumber=" + pageNumber.Value.ToString(CultureInfo.InvariantCulture));
            if (pageSize.HasValue) parts.Add("pageSize=" + pageSize.Value.ToString(CultureInfo.InvariantCulture));
            if (!String.IsNullOrWhiteSpace(status)) parts.Add("status=" + Uri.EscapeDataString(status));
            string query = parts.Count == 0 ? String.Empty : "?" + String.Join("&", parts);
            return _Transport.GetAsync<EnumerationResult<BatteryTaskResult>>($"/api/v1/battery/runs/{ArmadaQueryString.Escape(id)}/results{query}", null, token);
        }

        /// <summary>
        /// POST /api/v1/battery/runs/{id}/results/enumerate: one page of a run's per-task results (grader evidence and
        /// raw output), with the total. Status filters by outcome; Order orders by creation.
        /// </summary>
        /// <param name="id">Run id (bat_ prefix).</param>
        /// <param name="query">Paging query, or null for the first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of results.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<BatteryTaskResult>?> EnumerateRunResultsAsync(string id, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<BatteryTaskResult>>($"/api/v1/battery/runs/{ArmadaQueryString.Escape(id)}/results/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// GET /api/v1/battery/runs: one page of the battery runs visible to the caller, newest first, without their
        /// per-task results.
        /// </summary>
        /// <param name="query">Filters (tenant for global admins, captain, status) and paging, or null for the first page of 25.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of runs.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listBatteryRuns")]
        public Task<EnumerationResult<BatteryRun>?> ListRunsAsync(BatteryRunQuery? query = null, CancellationToken token = default)
        {
            BatteryRunQuery source = query ?? new BatteryRunQuery();
            List<string> parts = new List<string>();
            if (!String.IsNullOrWhiteSpace(source.TenantId)) parts.Add("tenantId=" + Uri.EscapeDataString(source.TenantId));
            if (!String.IsNullOrWhiteSpace(source.CaptainId)) parts.Add("captainId=" + Uri.EscapeDataString(source.CaptainId));
            if (source.Status.HasValue) parts.Add("status=" + source.Status.Value.ToString());
            parts.Add("pageNumber=" + source.PageNumber.ToString(CultureInfo.InvariantCulture));
            parts.Add("pageSize=" + source.PageSize.ToString(CultureInfo.InvariantCulture));
            return _Transport.GetAsync<EnumerationResult<BatteryRun>>("/api/v1/battery/runs?" + String.Join("&", parts), null, token);
        }

        /// <summary>
        /// POST /api/v1/battery/runs/enumerate: the POST form of <see cref="ListRunsAsync"/>, one page of the battery runs
        /// visible to the caller, newest first.
        /// </summary>
        /// <param name="query">Filters (tenant for global admins, captain, status) and paging, or null for the first page of 25.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of runs.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<BatteryRun>?> EnumerateRunsAsync(BatteryRunQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<BatteryRun>>("/api/v1/battery/runs/enumerate", query ?? new BatteryRunQuery(), null, token);
        }

        /// <summary>
        /// POST /api/v1/battery/runs/{id}/cancel: stop a run (no further task is dispatched, its open mission is cancelled
        /// and its dock released). Returns the run after the stop; it may still be Running for a few seconds while its open
        /// mission stops.
        /// </summary>
        /// <param name="id">Run id (bat_ prefix).</param>
        /// <param name="reason">Reason recorded on the run (at most 500 characters), or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The run.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("cancelBatteryRun")]
        public Task<BatteryRun?> CancelRunAsync(string id, string? reason = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            return _Transport.PostAsync<BatteryRun>($"/api/v1/battery/runs/{ArmadaQueryString.Escape(id)}/cancel", new BatteryRunCancelRequest { Reason = reason }, null, token);
        }

        #endregion
    }
}
