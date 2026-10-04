namespace Armada.Core.Services.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Finds git repositories on the Admiral host for vessel import, and lists directories for the import browser.
    /// Writes nothing.
    /// </summary>
    public interface IVesselDiscoveryService
    {
        /// <summary>
        /// Discover and classify candidates under the requested directories and roots, matching them against the
        /// tenant's existing vessels.
        /// </summary>
        /// <param name="tenantId">Tenant whose vessels are matched and whose names must stay unique.</param>
        /// <param name="request">Discovery request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Classified candidates, truncation flag, and hints.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId or request is null.</exception>
        /// <exception cref="ArgumentException">Thrown when no paths are given, too many are given, or a path is relative or malformed.</exception>
        /// <exception cref="NotSupportedException">Thrown when a Harbor is requested.</exception>
        /// <exception cref="Armada.Core.Services.VesselImportPathNotAllowedException">Thrown when a path is outside the allowed roots.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        Task<VesselDiscoveryResult> DiscoverAsync(string tenantId, VesselDiscoveryRequest request, CancellationToken token = default);

        /// <summary>
        /// List the browsable subdirectories of a directory, or the allowed roots when no path is given.
        /// </summary>
        /// <param name="path">Absolute directory path, or null or empty for the allowed roots.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The listing.</returns>
        /// <exception cref="ArgumentException">Thrown when the path is relative or malformed.</exception>
        /// <exception cref="Armada.Core.Services.VesselImportPathNotAllowedException">Thrown when the path is outside the allowed roots.</exception>
        /// <exception cref="System.IO.DirectoryNotFoundException">Thrown when the directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the directory cannot be read.</exception>
        Task<VesselBrowseResult> BrowseAsync(string? path, CancellationToken token = default);

        /// <summary>
        /// The effective allowed roots, normalized: Import.AllowedRoots, or the user profile directory when none are
        /// configured.
        /// </summary>
        /// <returns>Normalized allowed roots.</returns>
        List<string> GetAllowedRoots();
    }
}
