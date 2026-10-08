namespace Armada.Core.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Harbor;
    using Armada.Core.Models;

    /// <summary>
    /// The Admiral's side of Harbor-hosted docks: asks a Harbor whether it can serve a vessel, and has it create and
    /// remove mission docks on its host.
    /// </summary>
    public class HarborDockClient
    {
        #region Public-Members

        /// <summary>
        /// How long to wait for a Harbor to say whether it can serve a vessel.
        /// </summary>
        public int ResolveTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// How long to wait for a Harbor to create a dock (which may include a first clone).
        /// </summary>
        public int ProvisionTimeoutMs { get; set; } = 600000;

        /// <summary>
        /// How long to wait for a Harbor to remove a dock.
        /// </summary>
        public int ReclaimTimeoutMs { get; set; } = 120000;

        /// <summary>
        /// The Harbor connections.
        /// </summary>
        public HarborConnectionManager Harbors { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="harbors">Harbor connections.</param>
        public HarborDockClient(HarborConnectionManager harbors)
        {
            Harbors = harbors ?? throw new ArgumentNullException(nameof(harbors));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Ask a Harbor where it would serve a vessel's repository from.
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <param name="vessel">Vessel.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result, or null when the Harbor did not answer in time.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the Harbor is not connected or does not host docks.</exception>
        public Task<HarborDockResult?> ResolveAsync(string harborId, Vessel vessel, CancellationToken token = default)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            HarborDockRequest request = NewRequest(HarborDockOperationEnum.Resolve, vessel);
            return Harbors.SendDockAsync(harborId, request, ResolveTimeoutMs, token);
        }

        /// <summary>
        /// Have a Harbor create a mission dock.
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <param name="vessel">Vessel.</param>
        /// <param name="branchName">Mission branch.</param>
        /// <param name="dockName">Dock folder name (the mission identifier).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result, or null when the Harbor did not answer in time.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the Harbor is not connected or does not host docks.</exception>
        public Task<HarborDockResult?> ProvisionAsync(string harborId, Vessel vessel, string branchName, string dockName, CancellationToken token = default)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            HarborDockRequest request = NewRequest(HarborDockOperationEnum.Provision, vessel);
            request.BranchName = branchName;
            request.DockName = dockName;
            return Harbors.SendDockAsync(harborId, request, ProvisionTimeoutMs, token);
        }

        /// <summary>
        /// Have a Harbor remove a mission dock.
        /// </summary>
        /// <param name="dock">The Harbor-hosted dock.</param>
        /// <param name="vessel">Its vessel, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result, or null when the Harbor did not answer in time.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the Harbor is not connected or does not host docks.</exception>
        public Task<HarborDockResult?> ReclaimAsync(Dock dock, Vessel? vessel, CancellationToken token = default)
        {
            if (dock == null) throw new ArgumentNullException(nameof(dock));
            if (String.IsNullOrWhiteSpace(dock.HarborId)) throw new ArgumentException("The dock is not on a Harbor.", nameof(dock));
            HarborDockRequest request = new HarborDockRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Operation = HarborDockOperationEnum.Reclaim,
                VesselId = dock.VesselId,
                VesselName = vessel?.Name ?? dock.VesselId,
                WorktreePath = dock.WorktreePath,
                RepositoryPath = dock.RepositoryPath
            };
            return Harbors.SendDockAsync(dock.HarborId!, request, ReclaimTimeoutMs, token);
        }

        #endregion

        #region Private-Methods

        private static HarborDockRequest NewRequest(HarborDockOperationEnum operation, Vessel vessel)
        {
            return new HarborDockRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Operation = operation,
                VesselId = vessel.Id,
                VesselName = vessel.Name,
                RepoUrl = String.IsNullOrWhiteSpace(vessel.RepoUrl) ? null : vessel.RepoUrl,
                DefaultBranch = String.IsNullOrWhiteSpace(vessel.DefaultBranch) ? "main" : vessel.DefaultBranch
            };
        }

        #endregion
    }
}
