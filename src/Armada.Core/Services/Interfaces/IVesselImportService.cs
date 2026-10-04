namespace Armada.Core.Services.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Two-step bulk vessel onboarding: discover candidates into a persisted batch, then import the candidates the
    /// operator kept. All operations are scoped to the caller's tenant.
    /// </summary>
    public interface IVesselImportService
    {
        /// <summary>
        /// Run discovery and persist the result as a batch in status Discovered with one item per candidate. Creates
        /// no vessels.
        /// </summary>
        /// <param name="tenantId">Caller's tenant.</param>
        /// <param name="userId">Caller's user, or null.</param>
        /// <param name="request">Discovery request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The batch identifier, batch, candidate items, truncation flag, and hints.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId or request is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the request has no paths, too many paths, or an invalid path.</exception>
        /// <exception cref="NotSupportedException">Thrown when a Harbor is requested.</exception>
        /// <exception cref="Armada.Core.Services.VesselImportPathNotAllowedException">Thrown when a path is outside the allowed roots.</exception>
        Task<VesselImportDiscoverResponse> DiscoverAsync(string tenantId, string? userId, VesselDiscoveryRequest request, CancellationToken token = default);

        /// <summary>
        /// Import the selected candidates of a batch. Runs inline when the selection is at or below
        /// Import.InlineBatchLimit; otherwise enqueues a background Job and returns immediately with
        /// <see cref="VesselImportResponse.RunsInBackground"/> set. Idempotent: a selected path that already has a
        /// vessel is recorded as SkippedExisting. Unselected candidates are recorded as SkippedNotSelected (or
        /// SkippedExisting when already onboarded).
        /// </summary>
        /// <param name="tenantId">Caller's tenant.</param>
        /// <param name="userId">Caller's user, or null.</param>
        /// <param name="request">Import request.</param>
        /// <param name="token">Cancellation token for the inline path; a background import is not bound to it.</param>
        /// <returns>The import response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId or request is null.</exception>
        /// <exception cref="ArgumentException">Thrown when batchId or paths are missing, a path is not in the batch, or the fleet does not exist.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the batch does not exist in the tenant.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the batch is already being imported.</exception>
        Task<VesselImportResponse> ImportAsync(string tenantId, string? userId, VesselImportRequest request, CancellationToken token = default);

        /// <summary>
        /// Read a batch with all of its items, or null when it does not exist in the tenant.
        /// </summary>
        /// <param name="tenantId">Caller's tenant.</param>
        /// <param name="batchId">Batch identifier (vib_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The batch detail, or null.</returns>
        Task<VesselImportBatchDetail?> ReadBatchAsync(string tenantId, string batchId, CancellationToken token = default);

        /// <summary>
        /// Enumerate the tenant's batches, newest first by default. Honors PageNumber, PageSize, Order, CreatedAfter,
        /// CreatedBefore, and Status.
        /// </summary>
        /// <param name="tenantId">Caller's tenant.</param>
        /// <param name="query">Enumeration query; null uses defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of batches.</returns>
        Task<EnumerationResult<VesselImportBatch>> EnumerateBatchesAsync(string tenantId, EnumerationQuery? query, CancellationToken token = default);

        /// <summary>
        /// List browsable subdirectories of an allowed directory, or the allowed roots when no path is given.
        /// </summary>
        /// <param name="path">Absolute directory path, or null for the allowed roots.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The listing.</returns>
        /// <exception cref="ArgumentException">Thrown when the path is relative or malformed.</exception>
        /// <exception cref="Armada.Core.Services.VesselImportPathNotAllowedException">Thrown when the path is outside the allowed roots.</exception>
        /// <exception cref="System.IO.DirectoryNotFoundException">Thrown when the directory does not exist.</exception>
        Task<VesselBrowseResult> BrowseAsync(string? path, CancellationToken token = default);
    }
}
