namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for handoff artifacts (handoff_artifacts): one row per stage attempt that handed work on.
    /// </summary>
    public interface IHandoffArtifactMethods
    {
        /// <summary>
        /// Store an artifact.
        /// </summary>
        /// <param name="artifact">Artifact.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored artifact.</returns>
        Task<HandoffArtifact> CreateAsync(HandoffArtifact artifact, CancellationToken token = default);

        /// <summary>
        /// Read an artifact by id.
        /// </summary>
        /// <param name="id">Artifact id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The artifact, or null.</returns>
        Task<HandoffArtifact?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Rewrite an artifact (everything except its id, tenant and creation time).
        /// </summary>
        /// <param name="artifact">Artifact.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The artifact.</returns>
        Task<HandoffArtifact> UpdateAsync(HandoffArtifact artifact, CancellationToken token = default);

        /// <summary>
        /// The newest artifact of a mission, or null.
        /// </summary>
        /// <param name="missionId">Mission id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The artifact, or null.</returns>
        Task<HandoffArtifact?> ReadLatestByMissionAsync(string missionId, CancellationToken token = default);

        /// <summary>
        /// The artifact of one attempt of a mission (<see cref="HandoffArtifact.AttemptIdFor"/>), or null.
        /// </summary>
        /// <param name="missionId">Mission id.</param>
        /// <param name="attemptId">Attempt id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The artifact, or null.</returns>
        Task<HandoffArtifact?> ReadByAttemptAsync(string missionId, string attemptId, CancellationToken token = default);

        /// <summary>
        /// One page of artifacts, newest first.
        /// </summary>
        /// <param name="query">Filters and paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        Task<EnumerationResult<HandoffArtifact>> EnumerateAsync(HandoffArtifactQuery query, CancellationToken token = default);

        /// <summary>
        /// Delete every artifact of a mission.
        /// </summary>
        /// <param name="missionId">Mission id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> DeleteByMissionAsync(string missionId, CancellationToken token = default);
    }
}
