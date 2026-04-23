namespace Armada.Core.Services.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Validates, snapshots, and renders mission playbooks.
    /// </summary>
    public interface IPlaybookService
    {
        /// <summary>
        /// Validate a playbook before persistence.
        /// </summary>
        void Validate(Playbook playbook);

        /// <summary>
        /// Validate playbook selections for a tenant and preserve request order.
        /// </summary>
        Task<List<Playbook>> ResolveSelectionsAsync(string tenantId, List<SelectedPlaybook> selections, CancellationToken token = default);

        /// <summary>
        /// Build immutable mission snapshots for the supplied selections.
        /// </summary>
        Task<List<MissionPlaybookSnapshot>> CreateSnapshotsAsync(string tenantId, List<SelectedPlaybook>? selections, CancellationToken token = default);
    }
}
