namespace Armada.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for model profiles (model_profiles), their immutable revisions (model_profile_revisions), the
    /// administrator's compatibility marks between revisions (model_profile_revision_compat), and the captain reference
    /// (captains.model_profile_id). There is deliberately no revision update or delete.
    /// </summary>
    public interface IModelProfileMethods
    {
        /// <summary>
        /// Create a profile. The identity key must be unique; a second profile with the same key is a duplicate.
        /// </summary>
        /// <param name="profile">Profile (its revisions are not written).</param>
        /// <param name="identityKey">Normalized identity key (see ModelProfileRules.IdentityKey).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created profile.</returns>
        Task<ModelProfile> CreateAsync(ModelProfile profile, string identityKey, CancellationToken token = default);

        /// <summary>
        /// Read a profile by id (any tenant), without its revisions.
        /// </summary>
        /// <param name="id">Profile identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The profile, or null.</returns>
        Task<ModelProfile?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a profile by its identity key, without its revisions.
        /// </summary>
        /// <param name="identityKey">Normalized identity key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The profile, or null.</returns>
        Task<ModelProfile?> ReadByIdentityKeyAsync(string identityKey, CancellationToken token = default);

        /// <summary>
        /// Update the mutable parts of a profile (capabilities, rendering, notes) and set LastUpdateUtc. The identity,
        /// tenant and creation time are never written.
        /// </summary>
        /// <param name="profile">Profile with updated fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated profile.</returns>
        Task<ModelProfile> UpdateAsync(ModelProfile profile, CancellationToken token = default);

        /// <summary>
        /// List one page of profiles (without revisions), newest first, with the total count of matching rows.
        /// </summary>
        /// <param name="query">Filters and paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        Task<EnumerationResult<ModelProfile>> EnumerateAsync(ModelProfileQuery query, CancellationToken token = default);

        /// <summary>
        /// Create a revision. Revisions are immutable: there is no update.
        /// </summary>
        /// <param name="revision">Revision.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created revision.</returns>
        Task<ModelProfileRevision> CreateRevisionAsync(ModelProfileRevision revision, CancellationToken token = default);

        /// <summary>
        /// Read a revision by id, with its compatibility marks.
        /// </summary>
        /// <param name="id">Revision identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The revision, or null.</returns>
        Task<ModelProfileRevision?> ReadRevisionAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Every revision of a profile, oldest first (FirstSeenUtc, then id), with their compatibility marks.
        /// </summary>
        /// <param name="profileId">Profile identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The revisions.</returns>
        Task<List<ModelProfileRevision>> ReadRevisionsAsync(string profileId, CancellationToken token = default);

        /// <summary>
        /// Replace the revisions marked compatible with one revision, symmetrically: every mark involving the revision is
        /// removed, then each listed revision is marked compatible with it in both directions. The revision rows are not
        /// touched.
        /// </summary>
        /// <param name="revisionId">Revision identifier.</param>
        /// <param name="compatibleWith">Revision identifiers to mark compatible (the revision itself is ignored).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        Task SetCompatibleWithAsync(string revisionId, IEnumerable<string> compatibleWith, CancellationToken token = default);

        /// <summary>
        /// Point a captain at a profile (captains.model_profile_id) without touching any other captain column.
        /// </summary>
        /// <param name="captainId">Captain identifier.</param>
        /// <param name="profileId">Profile identifier, or null to clear.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a captain row was updated.</returns>
        Task<bool> LinkCaptainAsync(string captainId, string? profileId, CancellationToken token = default);
    }
}
