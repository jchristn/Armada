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
    /// Server API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

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
