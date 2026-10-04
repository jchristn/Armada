namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for captains.
    /// </summary>
    public interface ICaptainMethods
    {
        /// <summary>
        /// Create a captain.
        /// </summary>
        Task<Captain> CreateAsync(Captain captain, CancellationToken token = default);

        /// <summary>
        /// Read a captain by identifier.
        /// </summary>
        Task<Captain?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a captain by name.
        /// </summary>
        Task<Captain?> ReadByNameAsync(string name, CancellationToken token = default);

        /// <summary>
        /// Update a captain.
        /// </summary>
        Task<Captain> UpdateAsync(Captain captain, CancellationToken token = default);

        /// <summary>
        /// Delete a captain by identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all captains.
        /// </summary>
        Task<List<Captain>> EnumerateAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate captains with pagination and filtering.
        /// </summary>
        Task<EnumerationResult<Captain>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate captains by state.
        /// </summary>
        Task<List<Captain>> EnumerateByStateAsync(CaptainStateEnum state, CancellationToken token = default);

        /// <summary>
        /// Update captain state.
        /// </summary>
        Task UpdateStateAsync(string id, CaptainStateEnum state, CancellationToken token = default);

        /// <summary>
        /// Update captain heartbeat timestamp.
        /// </summary>
        Task UpdateHeartbeatAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Update the captain's process-liveness timestamp without advancing the output heartbeat.
        /// Refreshed while the agent's OS process is alive but silent, so liveness telemetry stays
        /// current without masking a stall (which is measured from the output heartbeat).
        /// </summary>
        Task UpdateProcessAliveAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Check if a captain exists by identifier.
        /// </summary>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Atomically claim a captain for a mission. Sets state to Working and assigns
        /// mission/dock IDs, but only if the captain is currently Idle.
        /// Returns true if the claim succeeded, false if the captain was no longer Idle.
        /// </summary>
        Task<bool> TryClaimAsync(string captainId, string missionId, string dockId, CancellationToken token = default);

        /// <summary>
        /// Read a captain by tenant and identifier (tenant-scoped).
        /// </summary>
        Task<Captain?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Delete a captain by tenant and identifier (tenant-scoped).
        /// </summary>
        Task DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all captains in a tenant (tenant-scoped).
        /// </summary>
        Task<List<Captain>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>
        /// Enumerate captains with pagination and filtering (tenant-scoped).
        /// </summary>
        Task<EnumerationResult<Captain>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Read a captain by tenant and name (tenant-scoped).
        /// </summary>
        Task<Captain?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default);

        /// <summary>
        /// Enumerate captains by tenant and state (tenant-scoped).
        /// </summary>
        Task<List<Captain>> EnumerateByStateAsync(string tenantId, CaptainStateEnum state, CancellationToken token = default);

        /// <summary>
        /// Update captain state (tenant-scoped).
        /// </summary>
        Task UpdateStateAsync(string tenantId, string id, CaptainStateEnum state, CancellationToken token = default);

        /// <summary>
        /// Update captain heartbeat timestamp (tenant-scoped).
        /// </summary>
        Task UpdateHeartbeatAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Check if a captain exists by tenant and identifier (tenant-scoped).
        /// </summary>
        Task<bool> ExistsAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Atomically claim a captain for a mission (tenant-scoped). Sets state to Working
        /// and assigns mission/dock IDs, but only if the captain is currently Idle and belongs
        /// to the specified tenant. Returns true if the claim succeeded.
        /// </summary>
        Task<bool> TryClaimAsync(string tenantId, string captainId, string missionId, string dockId, CancellationToken token = default);

        /// <summary>
        /// Atomically reserve an Idle captain for non-mission work (for example a fleet categorization job) by moving
        /// it to the given busy state. Only succeeds when the captain is currently Idle in the tenant.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="captainId">Captain identifier.</param>
        /// <param name="state">Busy state to set; must not be Idle.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the captain was reserved; false when it was not Idle or does not exist.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId or captainId is null or empty.</exception>
        /// <exception cref="ArgumentException">Thrown when state is Idle.</exception>
        Task<bool> TryReserveAsync(string tenantId, string captainId, CaptainStateEnum state, CancellationToken token = default);

        /// <summary>
        /// Atomically release a captain reserved with <see cref="TryReserveAsync"/>: sets it back to Idle and clears its
        /// process identifier, but only while it is still in the given busy state (a stop or recall that already moved
        /// it elsewhere is left alone).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="captainId">Captain identifier.</param>
        /// <param name="state">Busy state the reservation used.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the captain was released.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId or captainId is null or empty.</exception>
        Task<bool> TryReleaseAsync(string tenantId, string captainId, CaptainStateEnum state, CancellationToken token = default);

        /// <summary>
        /// Read a captain by tenant, user, and identifier (user-scoped).
        /// </summary>
        Task<Captain?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Delete a captain by tenant, user, and identifier (user-scoped).
        /// </summary>
        Task DeleteAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all captains owned by a user within a tenant (user-scoped).
        /// </summary>
        Task<List<Captain>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default);

        /// <summary>
        /// Enumerate captains with pagination and filtering (user-scoped).
        /// </summary>
        Task<EnumerationResult<Captain>> EnumerateAsync(string tenantId, string userId, EnumerationQuery query, CancellationToken token = default);
    }
}
