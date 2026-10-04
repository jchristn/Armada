namespace Armada.Core.Services.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Shared vessel creation logic used by the REST API, the MCP add_vessel tool, and vessel import, so all
    /// entry points apply the same validation.
    /// </summary>
    public interface IVesselService
    {
        /// <summary>
        /// Validate and create a vessel. The caller sets TenantId and UserId from its own auth context.
        /// LocalPath is never inferred: it is persisted only when the caller sets it, so removing the vessel can
        /// never delete an operator's own checkout.
        /// </summary>
        /// <param name="vessel">Vessel to create. RepoUrl is required.</param>
        /// <param name="inferWorkingDirectoryFromLocalClone">When true and WorkingDirectory is empty, a RepoUrl that
        /// names an existing local git repository (a file:// URL or a rooted path) becomes the WorkingDirectory.
        /// Default false.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created vessel.</returns>
        /// <exception cref="ArgumentNullException">Thrown when vessel is null.</exception>
        /// <exception cref="ArgumentException">Thrown when RepoUrl is null or empty.</exception>
        Task<Vessel> CreateAsync(Vessel vessel, bool inferWorkingDirectoryFromLocalClone = false, CancellationToken token = default);
    }
}
