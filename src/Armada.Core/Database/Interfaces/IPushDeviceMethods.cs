namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for push notification devices (push_devices).
    /// </summary>
    public interface IPushDeviceMethods
    {
        /// <summary>
        /// Create a device.
        /// </summary>
        /// <param name="device">Device.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created device.</returns>
        Task<PushDevice> CreateAsync(PushDevice device, CancellationToken token = default);

        /// <summary>
        /// Read a device by id (any tenant).
        /// </summary>
        /// <param name="id">Device identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The device, or null.</returns>
        Task<PushDevice?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a device by its Expo push token (any tenant).
        /// </summary>
        /// <param name="expoPushToken">Expo push token.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The device, or null.</returns>
        Task<PushDevice?> ReadByTokenAsync(string expoPushToken, CancellationToken token = default);

        /// <summary>
        /// Update every mutable field of a device (owner, platform, name, app version, locale, categories, active, last
        /// seen). The id, token, and creation time are fixed.
        /// </summary>
        /// <param name="device">Device with updated fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated device.</returns>
        Task<PushDevice> UpdateAsync(PushDevice device, CancellationToken token = default);

        /// <summary>
        /// Set whether a device is active.
        /// </summary>
        /// <param name="id">Device identifier.</param>
        /// <param name="active">Active flag.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a row was updated.</returns>
        Task<bool> SetActiveAsync(string id, bool active, CancellationToken token = default);

        /// <summary>
        /// Delete a device.
        /// </summary>
        /// <param name="id">Device identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a row was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Delete every device of a user.
        /// </summary>
        /// <param name="tenantId">Tenant.</param>
        /// <param name="userId">User.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of rows deleted.</returns>
        Task<int> DeleteByUserAsync(string tenantId, string userId, CancellationToken token = default);

        /// <summary>
        /// Delete every device of a tenant.
        /// </summary>
        /// <param name="tenantId">Tenant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of rows deleted.</returns>
        Task<int> DeleteByTenantAsync(string tenantId, CancellationToken token = default);

        /// <summary>
        /// List devices, oldest first.
        /// </summary>
        /// <param name="query">Filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Devices.</returns>
        Task<List<PushDevice>> EnumerateAsync(PushDeviceQuery query, CancellationToken token = default);
    }
}
