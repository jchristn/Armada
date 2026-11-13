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
    using Armada.Core.Storage;

    /// <summary>
    /// Server API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>Read cached global-administrator storage measurements without starting a scan.</summary>
        /// <exception cref="ArmadaApiException">Authorization, transport or API failure.</exception>
        public Task<StorageSnapshot?> GetStorageAsync(CancellationToken token = default)
        {
            return GetAsync<StorageSnapshot>("/api/v1/storage", null, token);
        }

        /// <summary>One page of cached storage host reports (the Admiral first, then each Harbor); the snapshot carries
        /// only host totals.</summary>
        /// <param name="query">Paging (page size 1 to 1000); null is the first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArmadaApiException">Authorization, transport or API failure.</exception>
        public Task<EnumerationResult<StorageHostReport>?> ListStorageHostsAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<StorageHostReport>>("/api/v1/storage/hosts/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>Every cached storage host report, read one bounded page (100 hosts) at a time.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Every host report.</returns>
        /// <exception cref="ArmadaApiException">Authorization, transport or API failure.</exception>
        public async Task<List<StorageHostReport>> ListAllStorageHostsAsync(CancellationToken token = default)
        {
            List<StorageHostReport> hosts = new List<StorageHostReport>();
            int page = 1;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                EnumerationResult<StorageHostReport>? result = await ListStorageHostsAsync(new EnumerationQuery { PageNumber = page, PageSize = 100 }, token).ConfigureAwait(false);
                if (result == null || result.Objects == null || result.Objects.Count == 0) break;
                hosts.AddRange(result.Objects);
                if (page >= result.TotalPages) break;
                page++;
            }

            return hosts;
        }

        /// <summary>Request bounded background collection. Exact counts are opt-in; concurrent requests coalesce.</summary>
        /// <exception cref="ArmadaApiException">Authorization, transport or API failure.</exception>
        public Task<StorageSnapshot?> RefreshStorageAsync(StorageRefreshRequest request, CancellationToken token = default)
        {
            return PostAsync<StorageSnapshot>("/api/v1/storage/refresh", request, null, token);
        }


        /// <summary>Generate historical synthetic activity without dispatching work. Global admin only.</summary>
        /// <param name="request">Bounded, fixed-window generation profile.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Per-type insert counts and screenshot Harbor/vessel identifiers.</returns>
        /// <exception cref="ArmadaApiException">Invalid profile, unauthorized caller, or API failure.</exception>
        public Task<SyntheticActivityResult?> GenerateSyntheticActivityAsync(SyntheticActivityRequest request, CancellationToken token = default)
        {
            return PostAsync<SyntheticActivityResult>("/api/v1/activity/synthetic", request, null, token);
        }

        /// <summary>
        /// Dashboard <c>getSettings</c>: GET '/api/v1/settings'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<SettingsData?> GetSettingsAsync(CancellationToken token = default)
        {
            return GetAsync<SettingsData>("/api/v1/settings", null, token);
        }

        /// <summary>
        /// Dashboard <c>updateSettings</c>: PUT '/api/v1/settings'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<SettingsData?> UpdateSettingsAsync(SettingsData data, CancellationToken token = default)
        {
            return PutAsync<SettingsData>("/api/v1/settings", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>stopServer</c>: POST '/api/v1/server/stop'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task StopServerAsync(CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Post, "/api/v1/server/stop", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>restartServer</c>: POST '/api/v1/server/restart'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task RestartServerAsync(CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Post, "/api/v1/server/restart", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>resetServer</c>: POST '/api/v1/server/reset'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task ResetServerAsync(CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Post, "/api/v1/server/reset", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>rebuildServer</c>: POST /api/v1/server/rebuild.
        /// </summary>
        /// <param name="body">Ref and options, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<RebuildStatus?> RebuildServerAsync(RebuildRequest? body = null, CancellationToken token = default)
        {
            return PostAsync<RebuildStatus>("/api/v1/server/rebuild", body ?? new RebuildRequest(), null, token);
        }

        /// <summary>
        /// Dashboard <c>getRebuildStatus</c>: GET '/api/v1/server/rebuild/status'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<RebuildStatus?> GetRebuildStatusAsync(CancellationToken token = default)
        {
            return GetAsync<RebuildStatus>("/api/v1/server/rebuild/status", null, token);
        }

        /// <summary>
        /// Dashboard <c>rollbackServer</c>: POST '/api/v1/server/rollback'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<RebuildStatus?> RollbackServerAsync(CancellationToken token = default)
        {
            return PostAsync<RebuildStatus>("/api/v1/server/rollback", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>downloadBackup</c>: GET /api/v1/backup. Returns the ZIP bytes and the server's suggested file name (the TUI saves it to a chosen path).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public async Task<BackupFile> DownloadBackupAsync(CancellationToken token = default)
        {
            using (HttpResponseMessage response = await SendRawAsync(HttpMethod.Get, "/api/v1/backup", null, null, 600000, token).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    ArmadaApiException ex = BuildException("GET", "/api/v1/backup", "", response, text);
                    if (ex.IsUnauthorized) RaiseUnauthorized(ex);
                    throw ex;
                }

                BackupFile file = new BackupFile();
                file.Content = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                string? name = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
                file.FileName = !String.IsNullOrEmpty(name) ? name! : "armada-backup-" + DateTime.UtcNow.ToString("yyyy-MM-dd") + ".zip";
                return file;
            }
        }

        /// <summary>
        /// Dashboard <c>restoreBackup</c>: POST /api/v1/restore with the raw ZIP bytes.
        /// </summary>
        /// <param name="content">ZIP bytes.</param>
        /// <param name="fileName">Original file name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is null.</exception>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public async Task<ArmadaRawJson> RestoreBackupAsync(byte[] content, string fileName, CancellationToken token = default)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            ByteArrayContent payload = new ByteArrayContent(content);
            payload.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            Dictionary<string, string> headers = new Dictionary<string, string>();
            headers["X-Original-Filename"] = String.IsNullOrEmpty(fileName) ? "backup.zip" : fileName;
            using (HttpResponseMessage response = await SendRawAsync(HttpMethod.Post, "/api/v1/restore", payload, headers, 600000, token).ConfigureAwait(false))
            {
                string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    ArmadaApiException ex = BuildException("POST", "/api/v1/restore", "", response, text);
                    if (ex.IsUnauthorized) RaiseUnauthorized(ex);
                    throw ex;
                }

                return new ArmadaRawJson(text);
            }
        }

        #endregion
    }
}
