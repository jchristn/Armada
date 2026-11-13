namespace Armada.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for playbooks and their voyage/mission associations.
    /// </summary>
    public interface IPlaybookMethods
    {
        /// <summary>
        /// Create a playbook.
        /// </summary>
        Task<Playbook> CreateAsync(Playbook playbook, CancellationToken token = default);

        /// <summary>
        /// Read a playbook by identifier.
        /// </summary>
        Task<Playbook?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a playbook by tenant and identifier.
        /// </summary>
        Task<Playbook?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read a playbook by tenant and filename.
        /// </summary>
        Task<Playbook?> ReadByFileNameAsync(string tenantId, string fileName, CancellationToken token = default);

        /// <summary>
        /// Update a playbook.
        /// </summary>
        Task<Playbook> UpdateAsync(Playbook playbook, CancellationToken token = default);

        /// <summary>
        /// Delete a playbook by identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all playbooks.
        /// </summary>
        Task<List<Playbook>> EnumerateAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate playbooks with pagination.
        /// </summary>
        Task<EnumerationResult<Playbook>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate playbooks in a tenant.
        /// </summary>
        Task<List<Playbook>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>
        /// Enumerate playbooks in a tenant with pagination.
        /// </summary>
        Task<EnumerationResult<Playbook>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate the playbooks one caller may see, paged in the data source with the visibility applied before
        /// counting: rows in <paramref name="tenantId"/>, and when <paramref name="viewerUserId"/> is set only the
        /// tenant-wide rows plus that user's own (the ScopedVisibility.CanView rule for a regular user). Ordered by
        /// creation time in the query order with the id as the tie-break.
        /// </summary>
        /// <param name="tenantId">Visible tenant.</param>
        /// <param name="viewerUserId">Regular user's id, or null for a tenant admin's view of the whole tenant.</param>
        /// <param name="query">Paging and created-range filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of visible playbooks.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="tenantId"/> is null or empty.</exception>
        Task<EnumerationResult<Playbook>> EnumerateVisibleAsync(string tenantId, string? viewerUserId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Check if a playbook exists by identifier.
        /// </summary>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Check if a playbook exists by tenant and filename.
        /// </summary>
        Task<bool> ExistsByFileNameAsync(string tenantId, string fileName, CancellationToken token = default);

        /// <summary>
        /// Replace the ordered playbook selections for a voyage.
        /// </summary>
        Task SetVoyageSelectionsAsync(string voyageId, List<SelectedPlaybook> selections, CancellationToken token = default);

        /// <summary>
        /// Retrieve the ordered playbook selections for a voyage.
        /// </summary>
        Task<List<SelectedPlaybook>> GetVoyageSelectionsAsync(string voyageId, CancellationToken token = default);

        /// <summary>
        /// Replace the immutable playbook snapshots for a mission.
        /// </summary>
        Task SetMissionSnapshotsAsync(string missionId, List<MissionPlaybookSnapshot> snapshots, CancellationToken token = default);

        /// <summary>
        /// Retrieve the immutable playbook snapshots for a mission.
        /// </summary>
        Task<List<MissionPlaybookSnapshot>> GetMissionSnapshotsAsync(string missionId, CancellationToken token = default);
    }
}
