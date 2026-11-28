namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Short form of <see cref="IAdmiralService.CreateVoyageAsync"/> for tests: Armada itself dispatching (no caller).
    /// </summary>
    public static class VoyageDispatchTestExtensions
    {
        /// <summary>
        /// Create a voyage with missions through the one voyage creation path.
        /// </summary>
        /// <param name="admiral">Admiral.</param>
        /// <param name="title">Voyage title.</param>
        /// <param name="description">Voyage description.</param>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="missions">Missions.</param>
        /// <param name="pipelineId">Pipeline id or name, or null for the default.</param>
        /// <param name="approvalOptions">Approval options, or null.</param>
        /// <param name="isEvaluation">Whether the voyage is evaluation work.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created voyage.</returns>
        public static Task<Voyage> DispatchVoyageAsync(
            this IAdmiralService admiral,
            string title,
            string description,
            string vesselId,
            List<MissionDescription> missions,
            string? pipelineId = null,
            RunApprovalOptions? approvalOptions = null,
            bool isEvaluation = false,
            CancellationToken token = default)
        {
            return admiral.CreateVoyageAsync(null, new VoyageCreationRequest
            {
                Title = title,
                Description = description,
                VesselId = vesselId,
                Missions = missions,
                PipelineId = pipelineId,
                ApprovalOptions = approvalOptions,
                IsEvaluation = isEvaluation
            }, token);
        }
    }
}
