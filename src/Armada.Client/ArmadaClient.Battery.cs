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
    public partial class ArmadaClient
    {
        private static string Escape(string value) => Uri.EscapeDataString(value);

        #region Public-Methods

        /// <summary>GetBatteryPackAsync: server-wide pack management; writes require a global admin.</summary>
        public Task<BatteryPackDetail?> GetBatteryPackAsync(string packId, CancellationToken token = default) =>
            GetAsync<BatteryPackDetail>($"/api/v1/battery/packs/{Escape(packId)}", null, token);

        /// <summary>ReadBatteryPackFileAsync: server-wide pack management; writes require a global admin.</summary>
        public Task<BatteryPackTextFile?> ReadBatteryPackFileAsync(string packId, string path, CancellationToken token = default) =>
            GetAsync<BatteryPackTextFile>($"/api/v1/battery/packs/{Escape(packId)}/files?path={Escape(path)}", null, token);

        /// <summary>CreateBatteryPackAsync: server-wide pack management; writes require a global admin.</summary>
        public Task<BatteryPackDetail?> CreateBatteryPackAsync(BatteryPackEditRequest request, CancellationToken token = default) =>
            PostAsync<BatteryPackDetail>($"/api/v1/battery/packs", request, null, token);

        /// <summary>UpdateBatteryPackManifestAsync: server-wide pack management; writes require a global admin.</summary>
        public Task<BatteryPackDetail?> UpdateBatteryPackManifestAsync(string packId, string manifestJson, CancellationToken token = default) =>
            PutAsync<BatteryPackDetail>($"/api/v1/battery/packs/{Escape(packId)}/manifest", new BatteryPackEditRequest { ManifestJson = manifestJson }, null, token);

        /// <summary>WriteBatteryPackFileAsync: server-wide pack management; writes require a global admin.</summary>
        public Task<BatteryPackDetail?> WriteBatteryPackFileAsync(string packId, string path, string content, CancellationToken token = default) =>
            PutAsync<BatteryPackDetail>($"/api/v1/battery/packs/{Escape(packId)}/files?path={Escape(path)}", new BatteryPackTextFile { Content = content }, null, token);

        /// <summary>DeleteBatteryPackFileAsync: server-wide pack management; writes require a global admin.</summary>
        public Task<BatteryPackDetail?> DeleteBatteryPackFileAsync(string packId, string path, CancellationToken token = default) =>
            DeleteAsync<BatteryPackDetail>($"/api/v1/battery/packs/{Escape(packId)}/files?path={Escape(path)}", null, token);

        /// <summary>DeleteBatteryPackAsync: server-wide pack management; writes require a global admin.</summary>
        public Task<object?> DeleteBatteryPackAsync(string packId, CancellationToken token = default) =>
            DeleteAsync<object>($"/api/v1/battery/packs/{Escape(packId)}", null, token);

        /// <summary>ValidateBatteryPackAsync: server-wide pack management; writes require a global admin.</summary>
        public Task<BatteryPackInfo?> ValidateBatteryPackAsync(string packId, CancellationToken token = default) =>
            PostAsync<BatteryPackInfo>($"/api/v1/battery/packs/{Escape(packId)}/validate", null, null, token);


        /// <summary>
        /// GET /api/v1/battery/packs: one page of battery packs, with validation errors for malformed ones.
        /// </summary>
        /// <param name="pageNumber">Page number, or null for 1.</param>
        /// <param name="pageSize">Page size, or null for 100.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of packs.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<BatteryPackInfo>?> ListBatteryPacksAsync(int? pageNumber = null, int? pageSize = null, CancellationToken token = default)
        {
            List<string> parts = new List<string>();
            if (pageNumber.HasValue) parts.Add("pageNumber=" + pageNumber.Value.ToString(CultureInfo.InvariantCulture));
            if (pageSize.HasValue) parts.Add("pageSize=" + pageSize.Value.ToString(CultureInfo.InvariantCulture));
            string query = parts.Count == 0 ? String.Empty : "?" + String.Join("&", parts);
            return GetAsync<EnumerationResult<BatteryPackInfo>>($"/api/v1/battery/packs{query}", null, token);
        }

        /// <summary>
        /// POST /api/v1/battery/packs/enumerate: one page of the fixture battery packs, with the total.
        /// </summary>
        /// <param name="query">Paging and filters, or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<BatteryPackInfo>?> EnumerateBatteryPacksAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<BatteryPackInfo>>("/api/v1/battery/packs/enumerate", query ?? new EnumerationQuery(), null, token);
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
        public Task<BatteryRun?> RunBatteryAsync(BatteryRunRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return PostAsync<BatteryRun>($"/api/v1/battery/runs", request, null, token);
        }

        /// <summary>
        /// GET /api/v1/battery/runs/{id}: one run with its outcome counts. The per-task results are not embedded; page
        /// them with <see cref="EnumerateBatteryRunResultsAsync"/>.
        /// </summary>
        /// <param name="id">Run id (bat_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The run.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<BatteryRun?> GetBatteryRunAsync(string id, CancellationToken token = default)
        {
            return GetAsync<BatteryRun>($"/api/v1/battery/runs/{E(id)}", null, token);
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
        public Task<EnumerationResult<BatteryTaskResult>?> ListBatteryRunResultsAsync(string id, int? pageNumber = null, int? pageSize = null, string? status = null, CancellationToken token = default)
        {
            List<string> parts = new List<string>();
            if (pageNumber.HasValue) parts.Add("pageNumber=" + pageNumber.Value.ToString(CultureInfo.InvariantCulture));
            if (pageSize.HasValue) parts.Add("pageSize=" + pageSize.Value.ToString(CultureInfo.InvariantCulture));
            if (!String.IsNullOrWhiteSpace(status)) parts.Add("status=" + Uri.EscapeDataString(status));
            string query = parts.Count == 0 ? String.Empty : "?" + String.Join("&", parts);
            return GetAsync<EnumerationResult<BatteryTaskResult>>($"/api/v1/battery/runs/{E(id)}/results{query}", null, token);
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
        public Task<EnumerationResult<BatteryTaskResult>?> EnumerateBatteryRunResultsAsync(string id, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<BatteryTaskResult>>($"/api/v1/battery/runs/{E(id)}/results/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        #endregion
    }
}
