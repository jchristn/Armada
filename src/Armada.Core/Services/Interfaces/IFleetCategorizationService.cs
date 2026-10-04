namespace Armada.Core.Services.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Captain-driven fleet categorization for vessel import batches: a captain reads a generated manifest of the
    /// imported repositories in a scratch directory, writes fleet-recommendations.json, and the Admiral validates and
    /// stores the result as structured recommendations that can be edited and applied. Runs as a background job of
    /// kind FleetCategorization. Every operation is tenant-scoped.
    /// </summary>
    public interface IFleetCategorizationService
    {
        /// <summary>
        /// Effective run time limit in minutes (Import.CategorizationTimeoutMinutes unless overridden).
        /// </summary>
        int TimeoutMinutes { get; }

        /// <summary>
        /// Validate a categorization request: when enabled, the captain is required and must exist in the tenant.
        /// A null or disabled request is valid.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="request">Categorization request, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when tenantId is null or empty.</exception>
        /// <exception cref="ArgumentException">Thrown when the captain is missing or not found.</exception>
        Task ValidateRequestAsync(string tenantId, VesselImportCategorizationRequest? request, CancellationToken token = default);

        /// <summary>
        /// The default captain instructions (the import.fleet_categorization prompt template, as edited under
        /// Configuration > Prompts).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Prompt text. Never null.</returns>
        Task<string> GetDefaultPromptAsync(CancellationToken token = default);

        /// <summary>
        /// Record a requested categorization on a batch (status Pending) without starting it, so clients see it while
        /// the import runs. Clears recommendations from any earlier run.
        /// </summary>
        /// <param name="batch">Batch to update; its TenantId must be set.</param>
        /// <param name="request">Enabled categorization request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated batch.</returns>
        /// <exception cref="ArgumentNullException">Thrown when batch or request is null.</exception>
        Task<VesselImportBatch> ScheduleAsync(VesselImportBatch batch, VesselImportCategorizationRequest request, CancellationToken token = default);

        /// <summary>
        /// Start the categorization recorded on a batch: collects the batch's selected vessels (created and already
        /// existing), enqueues a FleetCategorization job, and runs it in the background. When the batch has no vessels
        /// the categorization fails immediately.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="batchId">Batch identifier.</param>
        /// <param name="userId">Requesting user, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The batch with its categorization job identifier.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the batch does not exist in the tenant.</exception>
        Task<VesselImportBatch> StartAsync(string tenantId, string batchId, string? userId, CancellationToken token = default);

        /// <summary>
        /// Run (or re-run) categorization for a batch whose import finished. Values omitted from the request fall back
        /// to the batch's previous captain, prompt, and auto-apply choice.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="batchId">Batch identifier.</param>
        /// <param name="userId">Requesting user, or null.</param>
        /// <param name="request">Overrides, or null to repeat the previous run.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The batch with categorization Pending and its job identifier.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the batch does not exist in the tenant.</exception>
        /// <exception cref="ArgumentException">Thrown when no captain is known or the captain does not exist.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the import has not finished or categorization is already running.</exception>
        Task<VesselImportBatch> CategorizeAsync(string tenantId, string batchId, string? userId, VesselImportCategorizationRequest? request, CancellationToken token = default);

        /// <summary>
        /// Apply fleet recommendations (possibly edited): creates fleets or reuses tenant fleets with the same name
        /// (case-insensitive), assigns each listed vessel to its fleet, stores the applied list as the batch's
        /// recommendations, and marks categorization Applied. A fleet named Uncategorized is not created; its vessels
        /// keep their current fleet.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="batchId">Batch identifier.</param>
        /// <param name="userId">Requesting user, or null.</param>
        /// <param name="request">Fleets to apply.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Fleets used and every assignment.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the batch does not exist in the tenant.</exception>
        /// <exception cref="ArgumentException">Thrown when a fleet has no name, a vessel is listed twice, or a vessel is not part of the batch.</exception>
        /// <exception cref="InvalidOperationException">Thrown while discovery, import, or categorization is still running.</exception>
        Task<FleetRecommendationApplyResult> ApplyAsync(string tenantId, string batchId, string? userId, FleetRecommendationApplyRequest request, CancellationToken token = default);

        /// <summary>
        /// Fail categorizations orphaned by an Admiral restart (Pending or Running, and their jobs); captains left in the
        /// Analyzing state return to Idle. Call once at startup, before new work is accepted.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        Task RecoverAsync(CancellationToken token = default);
    }
}
