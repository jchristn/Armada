namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Http;
    using Armada.Core.Models;

    /// <summary>
    /// Push notification device API calls (mobile apps).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// POST '/api/v1/push/devices': register or refresh the caller's device by its Expo push token.
        /// </summary>
        /// <param name="request">Registration.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The device (token masked).</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<PushDevice?> RegisterPushDeviceAsync(PushDeviceRegisterRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return PostAsync<PushDevice>("/api/v1/push/devices", request, null, token);
        }

        /// <summary>
        /// GET '/api/v1/push/devices': the caller's devices, or (admins) another user's.
        /// </summary>
        /// <param name="userId">User filter, or null for the caller.</param>
        /// <param name="tenantId">Tenant filter (global admins), or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Devices (tokens masked).</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<List<PushDevice>?> ListPushDevicesAsync(string? userId = null, string? tenantId = null, CancellationToken token = default)
        {
            List<string> query = new List<string>();
            if (!String.IsNullOrEmpty(userId)) query.Add("userId=" + E(userId));
            if (!String.IsNullOrEmpty(tenantId)) query.Add("tenantId=" + E(tenantId));
            string path = "/api/v1/push/devices" + (query.Count > 0 ? "?" + String.Join("&", query) : String.Empty);
            return GetAsync<List<PushDevice>>(path, null, token);
        }

        /// <summary>
        /// PUT '/api/v1/push/devices/{id}': update the device name and/or categories.
        /// </summary>
        /// <param name="id">Device identifier.</param>
        /// <param name="request">Update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated device.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<PushDevice?> UpdatePushDeviceAsync(string id, PushDeviceUpdateRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return PutAsync<PushDevice>($"/api/v1/push/devices/{E(id)}", request, null, token);
        }

        /// <summary>
        /// DELETE '/api/v1/push/devices/{id}': remove a device.
        /// </summary>
        /// <param name="id">Device identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeletePushDeviceAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/push/devices/{E(id)}", null, null, token);
        }

        /// <summary>
        /// POST '/api/v1/push/devices/{id}/test': send a test notification to the device.
        /// </summary>
        /// <param name="id">Device identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The outcome.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<PushTestResult?> TestPushDeviceAsync(string id, CancellationToken token = default)
        {
            return PostAsync<PushTestResult>($"/api/v1/push/devices/{E(id)}/test", null, null, token);
        }

        #endregion
    }
}
