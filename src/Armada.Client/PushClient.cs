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
    public sealed class PushClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal PushClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// POST '/api/v1/push/devices': register or refresh the caller's device by its Expo push token.
        /// </summary>
        /// <param name="request">Registration.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The device (token masked).</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<PushDevice?> RegisterAsync(PushDeviceRegisterRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return _Transport.PostAsync<PushDevice>("/api/v1/push/devices", request, null, token);
        }

        /// <summary>
        /// GET '/api/v1/push/devices': the caller's devices, or (admins) another user's.
        /// </summary>
        /// <param name="userId">User filter, or null for the caller.</param>
        /// <param name="tenantId">Tenant filter (global admins), or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Devices (tokens masked).</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<List<PushDevice>?> ListAsync(string? userId = null, string? tenantId = null, CancellationToken token = default)
        {
            Dictionary<string, object?> filters = new Dictionary<string, object?>();
            if (!String.IsNullOrEmpty(userId)) filters["UserId"] = userId;
            if (!String.IsNullOrEmpty(tenantId)) filters["TenantId"] = tenantId;
            return _Transport.EnumerateAllAsync<PushDevice>("/api/v1/push/devices/enumerate", filters, token);
        }

        /// <summary>
        /// PUT '/api/v1/push/devices/{id}': update the device name and/or categories.
        /// </summary>
        /// <param name="id">Device identifier.</param>
        /// <param name="request">Update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated device.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<PushDevice?> UpdateAsync(string id, PushDeviceUpdateRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return _Transport.PutAsync<PushDevice>($"/api/v1/push/devices/{ArmadaQueryString.Escape(id)}", request, null, token);
        }

        /// <summary>
        /// DELETE '/api/v1/push/devices/{id}': remove a device.
        /// </summary>
        /// <param name="id">Device identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/push/devices/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        /// <summary>
        /// POST '/api/v1/push/devices/{id}/test': send a test notification to the device.
        /// </summary>
        /// <param name="id">Device identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The outcome.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<PushTestResult?> TestAsync(string id, CancellationToken token = default)
        {
            return _Transport.PostAsync<PushTestResult>($"/api/v1/push/devices/{ArmadaQueryString.Escape(id)}/test", null, null, token);
        }

        #endregion
    }
}
