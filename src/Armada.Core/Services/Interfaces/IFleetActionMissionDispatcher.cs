namespace Armada.Core.Services.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// The seam between the fleet action runner and Armada's voyage machinery: validate and dispatch one voyage per
    /// Mission target, read a dispatched voyage's outcome, and cancel a voyage. The production implementation wraps
    /// <see cref="IAdmiralService"/>; tests substitute a stub.
    /// </summary>
    public interface IFleetActionMissionDispatcher
    {
        /// <summary>
        /// Validate a single-mission dispatch to a vessel using the same rules as voyage dispatch.
        /// </summary>
        /// <param name="vessel">Target vessel.</param>
        /// <param name="pipelineId">Optional pipeline identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Validation result.</returns>
        Task<DispatchValidationResult> ValidateAsync(Vessel vessel, string? pipelineId, CancellationToken token = default);

        /// <summary>
        /// Dispatch one voyage with a single mission to a vessel.
        /// </summary>
        /// <param name="vessel">Target vessel.</param>
        /// <param name="title">Voyage and mission title.</param>
        /// <param name="prompt">Rendered prompt, used as the mission description.</param>
        /// <param name="pipelineId">Optional pipeline identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The dispatched voyage identifier.</returns>
        Task<string> DispatchAsync(Vessel vessel, string title, string prompt, string? pipelineId, CancellationToken token = default);

        /// <summary>
        /// Read the outcome of a dispatched voyage.
        /// </summary>
        /// <param name="voyageId">Voyage identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcome.</returns>
        Task<FleetActionVoyageOutcomeEnum> GetOutcomeAsync(string voyageId, CancellationToken token = default);

        /// <summary>
        /// Cancel a voyage that has not landed, using the existing voyage cancel semantics (pending and assigned
        /// missions are cancelled; missions already in progress are left to finish).
        /// </summary>
        /// <param name="voyageId">Voyage identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task CancelVoyageAsync(string voyageId, CancellationToken token = default);
    }
}
